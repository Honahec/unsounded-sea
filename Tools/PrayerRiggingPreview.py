"""Inspect a Hyper3D rig without changing weights or the production prefab.

blender -b --python Tools/PrayerRiggingPreview.py -- RIG.glb SOURCE.glb OUTPUT
The three poses are skinning diagnostics, not finished gameplay animations.
"""
import sys, json, math, pathlib, hashlib, struct
import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree

rig_path, source_path, output = map(pathlib.Path, sys.argv[sys.argv.index('--') + 1:])
output.mkdir(parents=True, exist_ok=True)


def glb_arrays(path):
    raw = path.read_bytes()
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + size])
    binary = raw[28 + size:]
    def array(index):
        a = doc['accessors'][index]
        v = doc['bufferViews'][a['bufferView']]
        dt = {5126: '<f4', 5125: '<u4', 5123: '<u2', 5121: 'u1'}[a['componentType']]
        width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
        offset = v.get('byteOffset', 0) + a.get('byteOffset', 0)
        return np.ndarray((a['count'], width), dtype=dt, buffer=binary,
                          offset=offset, strides=(v.get('byteStride', np.dtype(dt).itemsize * width), np.dtype(dt).itemsize)).copy()
    p = doc['meshes'][0]['primitives'][0]
    return doc, p, array


source_doc, source_prim, source_array = glb_arrays(source_path)
rig_doc, rig_prim, rig_array = glb_arrays(rig_path)
sp = source_array(source_prim['attributes']['POSITION'])
rp = rig_array(rig_prim['attributes']['POSITION'])
su = source_array(source_prim['attributes']['TEXCOORD_0'])
ru = rig_array(rig_prim['attributes']['TEXCOORD_0'])
# The service centers and uniformly scales the mesh. Compare in that frame,
# allowing vertex duplication at seams, but never assuming matching indices.
sp = (sp - (sp.min(0) + sp.max(0)) / 2) * ((rp.max(0)[1] - rp.min(0)[1]) / (sp.max(0)[1] - sp.min(0)[1]))
tree = KDTree(len(sp))
for i, v in enumerate(sp):
    tree.insert(v, i)
tree.balance()
position_error, uv_error = [], []
for point, uv in zip(rp, ru):
    candidates = tree.find_range(point, 0.00005)
    if not candidates:
        candidates = [tree.find(point)]
    position_error.append(min(c[2] for c in candidates))
    uv_error.append(min(float(np.max(np.abs(su[c[1]] - uv))) for c in candidates))
uv_ok = max(position_error) < 0.00005 and max(uv_error) < 0.00005
weights = np.concatenate([rig_array(v) for k, v in rig_prim['attributes'].items() if k.startswith('WEIGHTS_')], axis=1)
report = {
    'purpose': 'First rig review; diagnostic poses only, not final animations or production integration',
    'source_sha256': hashlib.sha256(source_path.read_bytes()).hexdigest(),
    'rig_sha256': hashlib.sha256(rig_path.read_bytes()).hexdigest(),
    'triangles': int(len(rig_array(rig_prim['indices'])) / 3),
    'vertices': len(rp), 'joints': len(rig_doc['skins'][0]['joints']),
    'max_normalized_position_error': max(position_error), 'max_uv_error': max(uv_error),
    'original_material_reuse_verified': uv_ok,
    'unweighted_vertices': int(np.sum(weights.sum(1) < 0.999)),
    'max_weight_sum_error': float(np.max(np.abs(weights.sum(1) - 1))),
    'max_influences': int(np.max(np.sum(weights > 0, axis=1))),
    'pose_bone_changes_degrees': {
        'rest': {},
        'arm_bend': {'RightUpperArm': [0, 0, -15], 'RightLowerArm': [35, 0, 0], 'LeftLowerArm': [20, 0, 0]},
        'torso_turn': {'Spine': [-8, 0, 0], 'Chest': [0, 18, 0], 'Head': [5, 0, 0]}
    }
}
if max(position_error) >= 0.00005:
    raise RuntimeError('Geometry changed; original material transfer needs review.')

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source_path))
materials = [m for obj in bpy.context.scene.objects if obj.type == 'MESH' for m in obj.data.materials]
source_mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH').data
source_positions = np.array([v.co[:] for v in source_mesh.vertices])
source_center = (source_positions.min(0) + source_positions.max(0)) / 2
source_scale = 1.9 / (source_positions.max(0)[2] - source_positions.min(0)[2])
source_positions = (source_positions - source_center) * source_scale
source_faces = []
face_tree = KDTree(len(source_mesh.polygons))
for face in source_mesh.polygons:
    points = [Vector(source_positions[i]) for i in face.vertices]
    uvs = [tuple(source_mesh.uv_layers.active.data[i].uv) for i in face.loop_indices]
    source_faces.append((points, uvs))
    face_tree.insert(sum(points, Vector()) / 3, face.index)
face_tree.balance()
for obj in list(bpy.context.scene.objects):
    bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.import_scene.gltf(filepath=str(rig_path))
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
mesh.data.materials.clear()
mesh.data.materials.append(materials[0])
transfer_error = 0
for face in mesh.data.polygons:
    points = [mesh.data.vertices[i].co for i in face.vertices]
    _, face_index, distance = face_tree.find(sum(points, Vector()) / 3)
    source_points, uvs = source_faces[face_index]
    for loop_index, point in zip(face.loop_indices, points):
        closest = min(range(3), key=lambda i: (point - source_points[i]).length)
        transfer_error = max(transfer_error, (point - source_points[closest]).length)
        mesh.data.uv_layers.active.data[loop_index].uv = uvs[closest]
if transfer_error >= 0.00005:
    raise RuntimeError('UV triangle transfer failed: ' + str(transfer_error))
report['uv_restoration'] = {'method': 'matching source triangle corners after uniform normalization',
                            'max_corner_distance': transfer_error, 'all_triangles_verified': True}
report['original_material_reuse_verified'] = True
arm.show_in_front = True
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x = 720
scene.render.resolution_y = 900
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.world = bpy.data.worlds.new('Review world')
scene.world.color = (0.15, 0.15, 0.15)
scene.view_settings.view_transform = 'AgX'

verts = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
minimum = Vector(tuple(min(v[i] for v in verts) for i in range(3)))
maximum = Vector(tuple(max(v[i] for v in verts) for i in range(3)))
center = (minimum + maximum) / 2
height = maximum.z - minimum.z

bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, minimum.z - 0.012))
floor = bpy.context.object
floor.name = 'Review floor'
mat = bpy.data.materials.new('Neutral charcoal')
mat.diffuse_color = (0.065, 0.075, 0.085, 1)
floor.data.materials.append(mat)

def aim(obj, target):
    obj.rotation_euler = (target - obj.location).to_track_quat('-Z', 'Y').to_euler()

for name, location, power, size in [('Key', (2, -3, 4), 450, 4), ('Fill', (-3, -2, 1), 180, 3), ('Rim', (1, 2, 3), 550, 3)]:
    data = bpy.data.lights.new(name, 'AREA')
    data.energy = power
    data.shape = 'DISK'
    data.size = size
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = location
    aim(obj, center)
bpy.ops.object.camera_add(location=center + Vector((height * 0.22, -height * 2.6, height * 0.12)))
camera = bpy.context.object
camera.data.type = 'ORTHO'
camera.data.ortho_scale = height * 1.24
aim(camera, center)
scene.camera = camera

for index, (name, changes) in enumerate(report['pose_bone_changes_degrees'].items()):
    scene.frame_set(1 + index * 30)
    for bone in arm.pose.bones:
        bone.rotation_mode = 'XYZ'
        bone.rotation_euler = (0, 0, 0)
    for bone, degrees in changes.items():
        arm.pose.bones[bone].rotation_euler = tuple(math.radians(x) for x in degrees)
    for bone in arm.pose.bones:
        bone.keyframe_insert('rotation_euler', frame=1 + index * 30)
    scene.frame_set(1 + index * 30)
    bpy.context.view_layer.update()
    evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    posed = np.array([v.co[:] for v in evaluated.data.vertices])
    if index == 0:
        rest_positions = posed
    report.setdefault('pose_max_vertex_displacement', {})[name] = float(np.max(np.linalg.norm(posed - rest_positions, axis=1)))
    scene.render.filepath = str(output / (name + '.png'))
    bpy.ops.render.render(write_still=True)
scene.frame_start = 1
scene.frame_end = 61
scene.frame_set(1)
arm.animation_data.action.name = 'Rig_Diagnostic_Poses_Not_Gameplay'
bpy.ops.wm.save_as_mainfile(filepath=str(output / 'Prayer_RiggingReview.blend'))
report['bones'] = [{'name': b.name, 'parent': b.parent.name if b.parent else None,
                    'head': list(b.head_local), 'tail': list(b.tail_local)} for b in arm.data.bones]
(output / 'inspection.json').write_text(json.dumps(report, indent=2))
print('RIG_REVIEW ' + json.dumps({k: v for k, v in report.items() if k != 'bones'}))
