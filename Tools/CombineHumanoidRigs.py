"""Blender CLI: -- NEW_DIR OLD_DIR OUTPUT_DIR NAME ...
Combine Hyper3D upper-body binding with fitted RigTrials lower-body binding.
Inputs are normalized to the same two-metre rest geometry before transfer.
No actions are exported. CompareHumanoidRigs.py supplies independent pose checks.
"""
import sys
import json
import hashlib
from pathlib import Path
import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree
sys.path.insert(0, str(Path(__file__).parent))
import CompareHumanoidRigs as review


def combine(new_path, old_path, target):
    old_arm, old_mesh, old_map, old_rest = review.setup(old_path, 1)
    old_names, old_w, _, _, _ = review.weights(old_mesh, old_arm)
    lower = [k for k in old_map if k.startswith(('thigh.', 'shin.', 'foot.'))]
    joints = {k: (old_arm.matrix_world @ old_arm.data.bones[old_map[k]].head_local,
                  old_arm.matrix_world @ old_arm.data.bones[old_map[k]].tail_local)
              for k in lower}
    tree = KDTree(len(old_rest))
    for i, point in enumerate(old_rest): tree.insert(point, i)
    tree.balance()
    arm, mesh, mapping, rest = review.setup(new_path, 0)
    matches = [tree.find(v) for v in rest]
    distance = max(x[2] for x in matches)
    assert distance < 0.00005, ('Different rest geometry', distance)
    indices = np.array([x[1] for x in matches])
    names, w, _, _, _ = review.weights(mesh, arm)
    transferred = np.zeros_like(w)
    for semantic in ['hips'] + lower:
        old_name = old_map[semantic]
        transferred[:, names.index(mapping[semantic])] = old_w[indices, old_names.index(old_name)]
    lower_names = {mapping[k] for k in ['hips'] + lower} | {'Root'}
    for side in ['L','R']:
        lower_names.update(b.name for b in arm.pose.bones[mapping['foot.'+side]].children_recursive)
    lower_indices = [i for i,n in enumerate(names) if n in lower_names]
    mass = w[:,lower_indices].sum(1)
    old_mass = transferred.sum(1)
    valid = old_mass > 1e-7
    transferred[valid] *= (mass[valid]/old_mass[valid])[:,None]
    # Preserve every upper-body influence exactly. Copying old arm influences
    # below the pelvis would reintroduce the old rig's cloth/prop pulling.
    for i,n in enumerate(names):
        if n not in lower_names: transferred[:,i] = w[:,i]
    transferred[~valid] = w[~valid]
    hip_z = min(joints['thigh.L'][0].z, joints['thigh.R'][0].z)
    # Continuous blend across the pelvis, preserving the complete new upper body.
    alpha = np.clip((hip_z + .12 - rest[:, 2]) / .25, 0, 1)
    alpha = alpha * alpha * (3 - 2 * alpha)
    result = w * (1-alpha[:, None]) + transferred * alpha[:, None]
    result /= result.sum(1)[:, None]
    for group in mesh.vertex_groups: group.remove(range(len(mesh.data.vertices)))
    for j, name in enumerate(names):
        group = mesh.vertex_groups.get(name) or mesh.vertex_groups.new(name=name)
        for i in np.flatnonzero(result[:, j] > 1e-7): group.add([int(i)], float(result[i,j]), 'REPLACE')
    # Move the lower-joint rest locations as well as their weights; leave upper
    # joints unchanged and translate toe helpers with their ankle parent.
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    inverse = arm.matrix_world.inverted()
    for semantic in lower:
        bone = arm.data.edit_bones[mapping[semantic]]
        head, tail = (inverse @ p for p in joints[semantic])
        delta = head - bone.head
        if semantic.startswith('foot.'):
            for child in bone.children_recursive:
                child.head += delta; child.tail += delta
        bone.use_connect = False
        bone.head = head; bone.tail = tail
    bpy.ops.object.mode_set(mode='OBJECT')
    assert np.max(abs(review.vertices(mesh)-rest)) < 1e-5
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.context.scene.objects:
        if obj.type in ('ARMATURE','MESH','EMPTY'): obj.select_set(True)
    target.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(target), export_format='GLB', use_selection=True,
        export_animations=False, export_all_influences=True)
    # Re-import the actual exported artifact for the rest-geometry check.
    _, _, _, exported = review.setup(target, 0)
    check = KDTree(len(rest))
    for i, p in enumerate(rest): check.insert(p, i)
    check.balance()
    error = max(check.find(p)[2] for p in exported)
    assert error < 5e-5, error
    return {'method':'New upper binding + old lower joints/weights, smooth 25 cm pelvis transition at 2 m normalized height',
        'parents': {label: {'path':str(p),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
                    for label,p in [('hyper3d',new_path),('rigtrials',old_path)]},
        'sha256':hashlib.sha256(target.read_bytes()).hexdigest(), 'bytes':target.stat().st_size,
        'vertex_match_max_m':distance,'export_rest_error_max_m':error,'animation_count':0}


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--')+1:]
    new, old, out = map(Path, args[:3])
    for name in args[3:]:
        result = combine(new/(name+'_Rigged.glb'), old/(name+'_Rigged.glb'), out/(name+'_Rigged.glb'))
        (out/(name+'.json')).write_text(json.dumps(result,indent=2)+'\n')
        print('COMBINED',name,flush=True)
