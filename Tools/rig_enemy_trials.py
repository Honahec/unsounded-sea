"""Blender CLI: bind inspected enemy sources and export five animation clips.
Fitted landmarks and action poses are specific to the archived source revisions.
"""
import argparse
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Vector
from mathutils.kdtree import KDTree

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'ArtSource/Characters/RigTrials'
RENDERS = ROOT / 'Logs/RigTrials'
SAMPLES = json.loads((ROOT / 'ArtSource/Characters/rig-profiles.json').read_text())


def import_source(config):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(ROOT / 'ArtSource/Characters' / config['source']))
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    assert meshes, 'Source has no mesh'
    for obj in meshes:
        matrix = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = matrix
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        obj.select_set(False)
    points = [v.co.copy() for obj in meshes for v in obj.data.vertices]
    low = Vector([min(v[i] for v in points) for i in range(3)])
    high = Vector([max(v[i] for v in points) for i in range(3)])
    center = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    scale = config['height'] / (high.z - low.z)
    for obj in meshes:
        for vertex in obj.data.vertices:
            vertex.co = (vertex.co - center) * scale
        bpy.context.view_layer.objects.active = obj
        obj.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.remove_doubles(threshold=.00001)
        bpy.ops.object.mode_set(mode='OBJECT')
        obj.select_set(False)
    return meshes


def create_skeleton(name, c):
    armature = bpy.data.armatures.new(name + '_Skeleton')
    rig = bpy.data.objects.new(name + '_Rig', armature)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    def bone(name, head, tail, parent=None, deform=True):
        b = armature.edit_bones.new(name)
        b.head, b.tail = head, tail
        b.use_deform = deform
        if parent:
            b.parent = armature.edit_bones[parent]
        return b
    bone('root', (c['center'], 0, 0), (c['center'], 0, .2), deform=False)
    bone('hips', c['hips'], c['spine'], 'root')
    bone('spine', c['spine'], c['chest'], 'hips')
    bone('chest', c['chest'], c['neck'], 'spine')
    bone('neck', c['neck'], c['head'], 'chest')
    bone('head', c['head'], (c['head'][0], c['head'][1], c['height']-.025), 'neck')
    for side in ('L', 'R'):
        p = c[side]
        bone('shoulder.'+side, c['chest'], p['shoulder'], 'chest')
        bone('upper_arm.'+side, p['shoulder'], p['elbow'], 'shoulder.'+side)
        bone('forearm.'+side, p['elbow'], p['wrist'], 'upper_arm.'+side)
        bone('hand.'+side, p['wrist'], p['hand'], 'forearm.'+side)
        bone('thigh.'+side, p['hip'], p['knee'], 'hips')
        bone('shin.'+side, p['knee'], p['ankle'], 'thigh.'+side)
        bone('foot.'+side, p['ankle'], p['toe'], 'shin.'+side)
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.show_in_front = True
    return rig


def bind(meshes, rig):
    bpy.ops.object.select_all(action='DESELECT')
    for mesh in meshes:
        mesh.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    report = []
    deform = {b.name for b in rig.data.bones if b.use_deform}
    for obj in meshes:
        assert any(m.type == 'ARMATURE' and m.object == rig for m in obj.modifiers)
        groups = {g.index:g for g in obj.vertex_groups if g.name in deform}
        unweighted = [v.index for v in obj.data.vertices if sum(g.weight for g in v.groups if g.group in groups) < 1e-8]
        transfer_distance = 0.0
        if unweighted:
            assert len(unweighted) < len(obj.data.vertices)*.01, f'{obj.name}: bone heat failed beyond isolated fragments'
            missing = set(unweighted)
            tree = KDTree(len(obj.data.vertices)-len(missing))
            for v in obj.data.vertices:
                if v.index not in missing:
                    tree.insert(v.co, v.index)
            tree.balance()
            for index in unweighted:
                _, nearest, distance = tree.find(obj.data.vertices[index].co)
                assert distance < .1, f'{obj.name}: unbound fragment too far from weighted surface'
                transfer_distance = max(transfer_distance, distance)
                for g in obj.data.vertices[nearest].groups:
                    if g.group in groups:
                        groups[g.group].add([index], g.weight, 'REPLACE')
        for v in obj.data.vertices:
            weights = sorted(((g.group, g.weight) for g in v.groups if g.group in groups and g.weight > 1e-6), key=lambda x:x[1], reverse=True)[:4]
            total = sum(w for _, w in weights)
            for index in [g.group for g in v.groups if g.group in groups]:
                groups[index].remove([v.index])
            for index, weight in weights:
                groups[index].add([v.index], weight/total, 'REPLACE')
        used = {g.name:0 for g in groups.values()}
        for v in obj.data.vertices:
            total = sum(g.weight for g in v.groups if g.group in groups)
            assert abs(total-1)<1e-5
            for g in v.groups:
                if g.group in groups and g.weight>.01:
                    used[groups[g.group].name]+=1
        for side in ('L','R'):
            for part in ('upper_arm','forearm','thigh','shin'):
                assert used[part+'.'+side]>10, f'Missing limb influence: {part}.{side}'
        report.append({'mesh':obj.name,'vertices':len(obj.data.vertices),'weightedVertices':len(obj.data.vertices),'surfaceTransferredVertices':len(unweighted),'maxTransferDistance':transfer_distance,'boneInfluenceVertexCounts':used})
    return report




def set_pose(rig, rotations, locations=None):
    locations = locations or {}
    for bone in rig.pose.bones:
        bone.rotation_mode = 'XYZ'
        bone.rotation_euler = tuple(math.radians(v) for v in rotations.get(bone.name, (0, 0, 0)))
        bone.location = bone.bone.matrix_local.to_quaternion().inverted() @ Vector(locations.get(bone.name, (0, 0, 0)))


def author_action(rig, name, poses, locations=None):
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    slot = action.slots.new('OBJECT', rig.name)
    rig.animation_data.action = action
    rig.animation_data.action_slot = slot
    for frame, rotations in poses.items():
        set_pose(rig, rotations, (locations or {}).get(frame))
        if name == 'Death':
            bpy.context.view_layer.update()
            depsgraph = bpy.context.evaluated_depsgraph_get()
            bottom = min((obj.matrix_world @ Vector(corner)).z
                         for obj in rig.children if obj.type == 'MESH'
                         for corner in obj.evaluated_get(depsgraph).bound_box)
            root = rig.pose.bones['root']
            root.location += root.bone.matrix_local.to_quaternion().inverted() @ Vector((0, 0, -bottom))
        for bone in rig.pose.bones:
            bone.keyframe_insert(data_path='rotation_euler', frame=frame, group=bone.name)
            bone.keyframe_insert(data_path='location', frame=frame, group=bone.name)
    assert rig.animation_data.action_slot == slot and action.frame_range[1] >= max(poses)
    return action, slot


def animate(rig, config):
    scene = bpy.context.scene
    scene.render.fps = 24
    rig.animation_data_create()
    clips = {
        'Idle': ({1: {}, 13: {'spine': (1.5, 0, 0), 'chest': (-1, 0, 0)}, 25: {}}, None),
        'Action': ({int(frame): pose for frame, pose in config['motion']['action'].items()}, None),
        'Hit': ({1: {}, 4: {'spine': (-8, 0, 0), 'chest': (-12, 4, 0), 'head': (9, -3, 0)}, 9: {'spine': (3, 0, 0), 'chest': (5, -2, 0)}, 17: {}}, None),
        'Death': (
            {
                1: {},
                8: {'spine': (-8, 0, 3), 'chest': (-10, 0, 4)},
                18: {'root': (35, 0, 6), 'spine': (12, 0, 4), 'chest': (8, 0, 3), 'thigh.L': (-12, 0, 0), 'thigh.R': (-18, 0, 0), 'shin.L': (18, 0, 0), 'shin.R': (24, 0, 0)},
                36: {'root': (82, 0, 12), 'spine': (8, 0, 4), 'chest': (12, 0, 3), 'neck': (-5, 0, 0), 'thigh.L': (-18, 0, 4), 'thigh.R': (-26, 0, -3), 'shin.L': (25, 0, 0), 'shin.R': (35, 0, 0)},
                48: {'root': (82, 0, 12), 'spine': (8, 0, 4), 'chest': (12, 0, 3), 'neck': (-5, 0, 0), 'thigh.L': (-18, 0, 4), 'thigh.R': (-26, 0, -3), 'shin.L': (25, 0, 0), 'shin.R': (35, 0, 0)},
            }, None,
        ),
        'RigValidation': ({
            1: {}, 25: {'upper_arm.L': (0, 0, -22), 'upper_arm.R': (0, 0, 22)}, 49: {},
            73: {'forearm.L': (35, 0, 0), 'forearm.R': (35, 0, 0)}, 97: {},
            121: {'thigh.L': (-15, 0, 0), 'shin.L': (30, 0, 0), 'thigh.R': (-15, 0, 0), 'shin.R': (30, 0, 0)}, 145: {},
            169: {'spine': (0, 12, 0), 'chest': (0, 10, 0), 'neck': (0, -8, 0)}, 193: {},
        }, None),
    }
    authored = []
    for clip_name, (poses, locations) in clips.items():
        action, slot = author_action(rig, clip_name, poses, locations)
        track = rig.animation_data.nla_tracks.new()
        track.name = clip_name
        strip = track.strips.new(clip_name, int(action.frame_range[0]), action)
        strip.action_slot = slot
        track.mute = True
        authored.append(action)
    rig.animation_data.action = None
    assert {a.name for a in authored} == set(clips)
    scene.frame_start, scene.frame_end = 1, 193
    scene.frame_set(1)
    return authored


def render_checks(name, c, rig):
    scene=bpy.context.scene
    scene.render.engine='BLENDER_WORKBENCH'
    scene.display.shading.light='STUDIO'
    scene.display.shading.color_type='TEXTURE'
    scene.display.shading.background_type='VIEWPORT'
    scene.display.shading.background_color=(.12,.12,.12)
    scene.render.resolution_x=512
    scene.render.resolution_y=768
    scene.render.resolution_percentage=100
    d=bpy.data.cameras.new('ReviewCamera');camera=bpy.data.objects.new('ReviewCamera',d)
    scene.collection.objects.link(camera);scene.camera=camera;d.type='ORTHO'
    for clip,frame,label in [('RigValidation',25,'arms'),('RigValidation',73,'elbows'),('RigValidation',121,'knees'),('RigValidation',169,'twist'),('Action',14,'action'),('Death',48,'death')]:
        action = bpy.data.actions[clip]
        rig.animation_data.action = action
        rig.animation_data.action_slot = action.slots[0]
        scene.frame_set(frame)
        depsgraph = bpy.context.evaluated_depsgraph_get()
        points = [obj.matrix_world @ Vector(corner) for obj in rig.children if obj.type == 'MESH'
                  for corner in obj.evaluated_get(depsgraph).bound_box]
        low = Vector([min(p[i] for p in points) for i in range(3)])
        high = Vector([max(p[i] for p in points) for i in range(3)])
        target = (low+high)*.5
        for view,offset,width in [('front',(0,-5,0),high.x-low.x),('side',(5,0,0),high.y-low.y)]:
            d.ortho_scale=max(high.z-low.z,width*1.5)*1.25
            camera.location=target+Vector(offset)
            camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
            scene.render.filepath=str(RENDERS/(name+'-'+label+'-'+view+'.png'))
            bpy.ops.render.render(write_still=True)
    scene.frame_set(1)
    rig.animation_data.action = None


def selected_samples():
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    parser = argparse.ArgumentParser()
    parser.add_argument('--only', nargs='+', choices=SAMPLES)
    requested = parser.parse_args(args).only
    return ((name, SAMPLES[name]) for name in (requested or SAMPLES))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    RENDERS.mkdir(parents=True, exist_ok=True)
    for name, c in selected_samples():
        meshes = import_source(c)
        rig = create_skeleton(name, c)
        report = bind(meshes, rig)
        actions = animate(rig, c)
        bpy.ops.object.select_all(action='DESELECT')
        for obj in meshes + [rig]:
            obj.select_set(True)
        bpy.context.scene.frame_set(1)
        bpy.ops.export_scene.gltf(
            filepath=str(OUT / (name + '_Rigged.glb')), export_format='GLB',
            use_selection=True, export_animations=True, export_skins=True,
            export_all_influences=False, export_animation_mode='ACTIONS',
            export_force_sampling=True,
        )
        bpy.ops.wm.save_as_mainfile(filepath=str(OUT / (name + '_Rigged.blend')))
        (OUT / (name + '-binding.json')).write_text(json.dumps({
            'motionProfile': c['motion_profile'],
            'source': c['source'],
            'method': 'Fitted anatomical landmarks + Blender bone heat; normalized strongest four influences',
            'height': c['height'], 'bones': len(rig.data.bones),
            'animations': [action.name for action in actions], 'meshes': report,
            'limitations': [
                'No finger articulation or facial rig',
                'Original props are fused geometry; attachment deformation requires visual review',
                'Not certified for arbitrary retargeted animation',
            ],
        }, indent=2) + '\n')
        render_checks(name, c, rig)
        print('RIG_TRIAL_COMPLETE', name)


if __name__ == '__main__':
    main()
