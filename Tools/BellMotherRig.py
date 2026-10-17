"""Build and validate the Bell Mother animation prototype in Blender.

Run on the archive/animation workstation:
  blender -b --python Tools/BellMotherRig.py

The source LOD0 GLB stays untouched. This creates a derived skinned FBX/GLB,
a Blender source file, a turntable preview, and a machine-readable report.
"""
import bpy, os, json, math, hashlib
from mathutils import Vector

ROOT = os.path.expanduser('~/Projects/unsounded-sea')
SRC = os.path.join(ROOT, 'Assets/UnsoundedSea/Art/Models/BellMother_LOD0.glb')
OUT_DIR = os.path.join(ROOT, 'ArtSource/Bosses')
RUN_DIR = os.path.join(ROOT, 'Assets/UnsoundedSea/Art/Models')
BLEND = os.path.join(OUT_DIR, 'BellMother_Rigged.blend')
FBX = os.path.join(RUN_DIR, 'BellMother_Rigged.fbx')
GLB = os.path.join(RUN_DIR, 'BellMother_Rigged.glb')
REPORT = os.path.join(OUT_DIR, 'BellMother_Rig-manifest.json')
PREVIEW = os.path.join(OUT_DIR, 'BellMother_Rig-preview.png')

os.makedirs(OUT_DIR, exist_ok=True)
os.makedirs(RUN_DIR, exist_ok=True)

def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for datablocks in (bpy.data.actions, bpy.data.armatures, bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        pass

def import_source():
    if not os.path.isfile(SRC):
        raise FileNotFoundError(SRC)
    bpy.ops.import_scene.gltf(filepath=SRC)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if len(meshes) != 1:
        raise RuntimeError('Expected one Bell Mother mesh, found %d' % len(meshes))
    mesh = meshes[0]
    mesh.name = 'BellMother_RiggedMesh'
    mesh.data.name = 'BellMother_RiggedMesh'
    mesh.rotation_mode = 'XYZ'
    return mesh

def add_bone(arm, name, head, tail, parent=None, use_connect=False):
    b = arm.data.edit_bones.new(name)
    b.head = head; b.tail = tail
    b.parent = arm.data.edit_bones.get(parent) if parent else None
    b.use_connect = bool(use_connect and parent)
    return b

def build_armature(mesh):
    arm_data = bpy.data.armatures.new('BellMother_Rig')
    arm = bpy.data.objects.new('BellMother_Rig', arm_data)
    bpy.context.collection.objects.link(arm)
    arm.show_in_front = True
    arm.display_type = 'WIRE'
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    add_bone(arm, 'root', (0, -0.58, 0), (0, -0.20, 0))
    add_bone(arm, 'body', (0, -0.20, 0), (0, 0.20, 0), 'root', True)
    add_bone(arm, 'spine_01', (0, 0.18, 0), (0, 0.43, 0), 'body', True)
    add_bone(arm, 'spine_02', (0, 0.40, 0), (0, 0.63, 0), 'spine_01', True)
    add_bone(arm, 'crown', (0, 0.58, 0), (0, 0.94, 0), 'spine_02', True)
    add_bone(arm, 'jaw', (0, 0.20, 0.32), (0, 0.08, 0.45), 'spine_02')

    # Six asymmetric root-to-tip chains. The mesh is a single connected object,
    # so radial bones provide controllable tentacle motion without re-generating it.
    tentacles = [
        ('tentacle_front_left', math.radians(125), 0.05),
        ('tentacle_front_right', math.radians(55), -0.04),
        ('tentacle_side_left', math.radians(178), 0.02),
        ('tentacle_side_right', math.radians(2), -0.02),
        ('tentacle_rear_left', math.radians(235), -0.05),
        ('tentacle_rear_right', math.radians(305), 0.04),
    ]
    chains = {}
    for name, angle, bend in tentacles:
        chain = []
        for i in range(4):
            r0 = 0.12 + i * 0.27
            r1 = 0.12 + (i + 1) * 0.27
            a0 = angle + math.sin(i * 1.3 + bend * 8) * 0.12
            a1 = angle + math.sin((i + 1) * 1.3 + bend * 8) * 0.18 + bend * (i + 1)
            y0 = -0.22 - i * 0.08
            y1 = -0.27 - (i + 1) * 0.08
            head = (math.cos(a0) * r0, y0, math.sin(a0) * r0)
            tail = (math.cos(a1) * r1, y1, math.sin(a1) * r1)
            bone_name = '%s_%02d' % (name, i + 1)
            add_bone(arm, bone_name, head, tail, chain[-1] if chain else 'root', False)
            chain.append(bone_name)
        chains[name] = chain
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm, chains

def segment_distance(p, a, b):
    ab = b - a
    denom = ab.length_squared
    if denom < 1e-8:
        return (p - a).length, 0.0
    t = max(0.0, min(1.0, (p - a).dot(ab) / denom))
    return (p - (a + ab * t)).length, t

def skin_mesh(mesh, arm):
    # The source is one connected mesh. Classify the central mass separately
    # from the six low radial appendages, then blend only adjacent bones.
    # This avoids the long spikes produced by generic nearest-bone weights.
    for name in arm.data.bones.keys():
        mesh.vertex_groups.new(name=name)
    tentacle_names = [n for n in arm.data.bones.keys() if n.startswith('tentacle_')]
    body_names = ['root', 'body', 'spine_01', 'spine_02', 'crown']
    chains = {}
    for name in tentacle_names:
        chains.setdefault(name.rsplit('_', 1)[0], []).append(name)
    for key in chains:
        chains[key].sort()
    def add_pair(vertex, pair):
        total = sum(w for _, w in pair)
        for name, weight in pair:
            mesh.vertex_groups[name].add([vertex.index], weight / max(total, 1e-8), 'REPLACE')
    for v in mesh.data.vertices:
        p = v.co; radial = math.sqrt(p.x * p.x + p.z * p.z)
        # Upper/central mass follows a stable vertical spine. The threshold
        # leaves the underslung tentacle roots free to move.
        if p.y > -0.46 or radial < 0.58:
            u = max(0.0, min(1.0, (p.y + 0.58) / 1.52))
            pos = u * 4.0
            i = min(3, int(pos)); t = pos - i
            a = body_names[i]; b = body_names[min(4, i + 1)]
            add_pair(v, [(a, 1.0 - t), (b, t)] if a != b else [(a, 1.0)])
            continue
        # Lower radial geometry is assigned to the closest tentacle chain by
        # angle and then to adjacent segments by projection along the chain.
        angle = math.atan2(p.z, p.x)
        best = None
        for chain_key, names in chains.items():
            first = arm.data.bones[names[0]].head_local
            last = arm.data.bones[names[-1]].tail_local
            chain_angle = math.atan2(last.z - first.z, last.x - first.x)
            delta = abs((angle - chain_angle + math.pi) % (2 * math.pi) - math.pi)
            score = delta + max(0.0, 0.30 - radial) * 2.0
            if best is None or score < best[0]: best = (score, names)
        names = best[1]
        distances = []
        for name in names:
            bone = arm.data.bones[name]
            d, t = segment_distance(p, Vector(bone.head_local), Vector(bone.tail_local))
            distances.append((d, name, t))
        distances.sort(key=lambda x: x[0])
        d0, n0, _ = distances[0]; d1, n1, _ = distances[1]
        # If a vertex is near a chain segment, use the two nearest segments;
        # otherwise keep a small root influence to avoid harsh seams.
        w0 = 1.0 / max(d0, 0.025); w1 = 1.0 / max(d1, 0.025)
        add_pair(v, [(n0, w0), (n1, w1)])
    mod = mesh.modifiers.new('BellMother_Rig_Skin', 'ARMATURE')
    mod.object = arm
    mod.use_deform_preserve_volume = True
    mesh.parent = arm
    mesh.parent_type = 'OBJECT'
    return len(mesh.data.vertices)

def quat_key(pbone, frame, euler):
    pbone.rotation_mode = 'XYZ'
    pbone.rotation_euler = euler
    pbone.keyframe_insert(data_path='rotation_euler', frame=frame)

def scale_key(pbone, frame, scale):
    pbone.scale = scale
    pbone.keyframe_insert(data_path='scale', frame=frame)

def action_setup(name, frame_end):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action.frame_start = 1; action.frame_end = frame_end
    return action

def set_action(arm, action):
    arm.animation_data_create(); arm.animation_data.action = action

def add_cycle_keys(arm, action, frame_end, phase=0.0, strength=1.0):
    set_action(arm, action)
    poses = [1, frame_end // 4, frame_end // 2, frame_end * 3 // 4, frame_end]
    for frame in poses:
        u = (frame - 1) / max(1, frame_end - 1)
        wave = math.sin(u * math.tau + phase) * strength
        for idx, name in enumerate(['body','spine_01','spine_02','crown']):
            b = arm.pose.bones[name]
            quat_key(b, frame, (wave * (0.08 + idx * 0.02), wave * 0.035, wave * (0.05 + idx * 0.01)))
        for idx, name in enumerate(n for n in arm.pose.bones.keys() if n.startswith('tentacle_')):
            b = arm.pose.bones[name]
            quat_key(b, frame, (wave * 0.08 * (1 + idx % 3), wave * 0.04, wave * 0.06))
    return action

def add_action(arm, name, frame_end, frames):
    action = action_setup(name, frame_end); set_action(arm, action)
    all_names = list(arm.pose.bones.keys())
    for frame, values in frames:
        for bone_name, euler in values.items():
            if bone_name in arm.pose.bones:
                quat_key(arm.pose.bones[bone_name], frame, euler)
    # Ensure every action has a deterministic neutral key on all bones.
    # Blender 5.x stores channels through action slots rather than exposing
    # the legacy Action.fcurves collection, so insert the keys directly.
    for name2 in all_names:
        quat_key(arm.pose.bones[name2], 1, (0,0,0))
    return action

def make_actions(arm, chains):
    actions = []
    actions.append(add_cycle_keys(arm, action_setup('BellMother_Idle', 120), 120, 0.0, 1.0))
    # Deliberate readable gameplay beats: telegraph, attack, recover, hit and death.
    actions.append(add_action(arm, 'BellMother_Telegraph', 36, [
        (1, {'body':(0,0,0), 'crown':(0,0,0)}),
        (18, {'body':(-0.035,0.0,0.02), 'crown':(-0.07,0.0,0.0)}),
        (36, {'body':(-0.075,0.0,0.035), 'crown':(-0.12,0.0,0.0)}),
    ]))
    attack_frames=[]
    for f, amp in [(1,0.0),(12,0.05),(24,-0.12),(36,0.08),(48,0.0)]:
        vals={'body':(amp*0.3,0,amp*0.2), 'crown':(amp*0.5,0,0)}
        for i, (name, chain) in enumerate(chains.items()):
            direction = -1 if i % 2 else 1
            for j, bone_name in enumerate(chain):
                vals[bone_name]=(0, direction * amp * (0.65 + j*0.18), direction * amp * (0.25 if j%2 else -0.2))
        attack_frames.append((f, vals))
    actions.append(add_action(arm, 'BellMother_Attack_A', 48, attack_frames))
    actions.append(add_action(arm, 'BellMother_Recover', 36, [
        (1, {'body':(0.12,0,0.0), 'crown':(0.16,0,0)}),
        (18, {'body':(0.04,0,0), 'crown':(0.06,0,0)}),
        (36, {'body':(0,0,0), 'crown':(0,0,0)}),
    ]))
    actions.append(add_action(arm, 'BellMother_Hit', 24, [
        (1, {'body':(0,0,0)}), (6, {'body':(0,-0.06,0.03), 'crown':(0,-0.09,0.05)}),
        (12, {'body':(0,0.05,-0.02), 'crown':(0,0.07,-0.03)}), (24, {'body':(0,0,0), 'crown':(0,0,0)}),
    ]))
    actions.append(add_action(arm, 'BellMother_Death', 72, [
        (1, {'body':(0,0,0), 'crown':(0,0,0)}),
        (24, {'body':(0.10,0.0,0.06), 'crown':(0.16,0.0,0.10)}),
        (48, {'body':(0.22,0.0,0.12), 'crown':(0.32,0.0,0.18)}),
        (72, {'body':(0.38,0.0,0.20), 'crown':(0.52,0.0,0.28)}),
    ]))
    arm.animation_data.action = actions[0]
    return actions

def evaluate_actions(arm, mesh, actions):
    scene=bpy.context.scene; deps=bpy.context.evaluated_depsgraph_get(); out=[]
    for action in actions:
        arm.animation_data.action=action
        samples=[]
        for frame in sorted(set([1, int(action.frame_end), max(1, int(action.frame_end/2))])):
            scene.frame_set(frame); deps.update()
            ev=mesh.evaluated_get(deps)
            corners=[ev.matrix_world @ Vector(c) for c in ev.bound_box]
            if not all(math.isfinite(v) for c in corners for v in c): raise RuntimeError('Non-finite deformation in '+action.name)
            samples.append(frame)
        out.append({'name':action.name,'frame_end':int(action.frame_end),'samples':samples})
    return out

def export_assets(mesh, arm, actions):
    bpy.ops.object.select_all(action='DESELECT'); mesh.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active=arm
    # FBX is the Unity runtime handoff; all actions are baked into one file.
    bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={'ARMATURE','MESH'}, add_leaf_bones=False,
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL')
    bpy.ops.export_scene.gltf(filepath=GLB, export_format='GLB', use_selection=True, export_animations=True,
        export_animation_mode='ACTIONS', export_skins=True, export_morph=True)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)

def render_preview(mesh, arm):
    scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'; scene.render.resolution_x=720; scene.render.resolution_y=720; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.render.filepath=PREVIEW
    world=bpy.data.worlds.new('BellMotherPreviewWorld'); scene.world=world; world.color=(0.008,0.016,0.02)
    bpy.ops.object.camera_add(location=(3.2,2.1,3.6)); cam=bpy.context.object; scene.camera=cam
    bpy.ops.object.light_add(type='AREA', location=(2.0,3.0,3.0)); key=bpy.context.object; key.data.energy=950; key.data.size=3.5
    bpy.ops.object.light_add(type='AREA', location=(-2.5,1.4,0.5)); fill=bpy.context.object; fill.data.energy=450; fill.data.size=2.5
    bpy.ops.object.light_add(type='AREA', location=(0.0,1.0,-3.0)); rim=bpy.context.object; rim.data.energy=700; rim.data.size=2
    for o in [cam,key,fill,rim]: o.rotation_euler=((Vector((0,0,0))-o.location).to_track_quat('-Z','Y')).to_euler()
    idle = bpy.data.actions.get('BellMother_Idle')
    if idle is not None: arm.animation_data.action = idle
    scene.frame_set(1)
    arm.hide_viewport=True; arm.hide_render=True
    bpy.ops.object.select_all(action='DESELECT'); mesh.select_set(True); bpy.context.view_layer.objects.active=mesh
    bpy.ops.render.render(write_still=True)

def sha(path):
    h=hashlib.sha256()
    with open(path,'rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''): h.update(block)
    return h.hexdigest()

def main():
    clear_scene(); mesh=import_source(); arm, chains=build_armature(mesh); vertex_count=skin_mesh(mesh,arm); actions=make_actions(arm,chains); samples=evaluate_actions(arm,mesh,actions); export_assets(mesh,arm,actions); render_preview(mesh,arm)
    report={
        'asset':'BellMother', 'source':'Assets/UnsoundedSea/Art/Models/BellMother_LOD0.glb',
        'method':'Blender 5.2.2 custom creature rig; source mesh unchanged',
        'rig_type':'generic', 'armature':'BellMother_Rig', 'bones':len(arm.data.bones),
        'vertex_count':vertex_count, 'actions':[{'name':a.name,'frames':int(a.frame_end)} for a in actions],
        'deformation_samples':samples, 'outputs':{
            'blend':os.path.relpath(BLEND,ROOT), 'fbx':os.path.relpath(FBX,ROOT), 'glb':os.path.relpath(GLB,ROOT), 'preview':os.path.relpath(PREVIEW,ROOT)},
        'sha256':{'fbx':sha(FBX),'glb':sha(GLB),'blend':sha(BLEND),'preview':sha(PREVIEW)}
    }
    with open(REPORT,'w',encoding='utf-8') as f: json.dump(report,f,ensure_ascii=False,indent=2)
    print(json.dumps(report,ensure_ascii=False,indent=2))

if __name__=='__main__': main()
