"""Restore source UVs/materials on Hyper3D skeletons, without authoring clips.

blender -b --python Tools/PrepareHyper3DRigs.py -- WORKDIR NAME [NAME ...]
WORKDIR contains sources/, raw/, and specs.json. Outputs are binding-only GLBs,
per-asset structural reports and neutral front/side/back previews.
"""
import hashlib
import json
import math
from pathlib import Path
import struct
import sys
import bpy
import numpy as np
from mathutils import Vector
from mathutils.kdtree import KDTree


def read_glb(path):
    raw = path.read_bytes()
    assert raw[:4] == b'glTF' and struct.unpack_from('<I', raw, 8)[0] == len(raw)
    size = struct.unpack_from('<I', raw, 12)[0]
    doc = json.loads(raw[20:20 + size])
    binary = raw[28 + size:]
    def array(index):
        a = doc['accessors'][index]
        v = doc['bufferViews'][a['bufferView']]
        dt = {5126: '<f4', 5125: '<u4', 5123: '<u2', 5121: 'u1'}[a['componentType']]
        width = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}[a['type']]
        return np.ndarray((a['count'], width), dtype=dt, buffer=binary,
            offset=v.get('byteOffset', 0) + a.get('byteOffset', 0),
            strides=(v.get('byteStride', np.dtype(dt).itemsize * width), np.dtype(dt).itemsize)).copy()
    assert len(doc['meshes']) == 1 and len(doc['meshes'][0]['primitives']) == 1
    primitive = doc['meshes'][0]['primitives'][0]
    assert primitive.get('mode', 4) == 4
    return doc, primitive, array


def prepare(work, name, spec):
    source = work / 'sources' / spec['filename']
    raw = work / 'raw' / (name + '_Raw.glb')
    dest = work / 'outputs' / (name + '_Rigged.glb')
    report_path = work / 'outputs' / (name + '.json')
    if report_path.exists() and dest.exists():
        report = json.loads(report_path.read_text())
        assert report['source_sha256'] == spec['source_sha256'] == hashlib.sha256(source.read_bytes()).hexdigest()
        assert report['raw_sha256'] == hashlib.sha256(raw.read_bytes()).hexdigest()
        assert report['output_sha256'] == hashlib.sha256(dest.read_bytes()).hexdigest()
        if report.get('pipeline_version') == 2 and report['height_m'] == float(spec['height']):
            print('ALREADY_PREPARED', name, flush=True)
            return
    sd, sp, sa = read_glb(source)
    rd, rp, ra = read_glb(raw)
    source_sha = hashlib.sha256(source.read_bytes()).hexdigest()
    assert source_sha == spec['source_sha256']
    assert not rd.get('animations'), 'Expected a binding-only API result'
    assert len(rd.get('skins', [])) == 1
    positions = ra(rp['attributes']['POSITION'])
    weights = np.concatenate([ra(v) for k, v in rp['attributes'].items() if k.startswith('WEIGHTS_')], axis=1)
    assert np.isfinite(weights).all() and np.min(weights) >= 0
    assert np.max(np.abs(weights.sum(1) - 1)) < 0.0001
    assert len(sa(sp['indices'])) == len(ra(rp['indices']))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    original = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    material = original.data.materials[0]
    coords = np.array([v.co[:] for v in original.data.vertices])
    scale = float(np.ptp(positions[:, 1])) / np.ptp(coords[:, 2])
    coords = (coords - (coords.min(0) + coords.max(0)) / 2) * scale
    faces = []
    tree = KDTree(len(original.data.polygons))
    for face in original.data.polygons:
        assert len(face.vertices) == 3
        points = [Vector(coords[i]) for i in face.vertices]
        uvs = [tuple(original.data.uv_layers.active.data[i].uv) for i in face.loop_indices]
        faces.append((points, uvs))
        tree.insert(sum(points, Vector()) / 3, face.index)
    tree.balance()
    for obj in list(bpy.context.scene.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.import_scene.gltf(filepath=str(raw))
    arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
    mesh.data.materials.clear()
    mesh.data.materials.append(material)
    error = 0
    for face in mesh.data.polygons:
        points = [mesh.data.vertices[i].co for i in face.vertices]
        _, index, _ = tree.find(sum(points, Vector()) / 3)
        source_points, uvs = faces[index]
        for loop, point in zip(face.loop_indices, points):
            nearest = min(range(3), key=lambda i: (point - source_points[i]).length)
            error = max(error, (point - source_points[nearest]).length)
            mesh.data.uv_layers.active.data[loop].uv = uvs[nearest]
    assert error < 0.00005, f'UV transfer corner mismatch: {error}'

    # Measure real skin deformation with a small reversible spine perturbation.
    # This is structural evidence, not a gameplay motion or aesthetic approval.
    def evaluated_positions():
        bpy.context.view_layer.update()
        obj = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        return np.array([v.co[:] for v in obj.data.vertices])
    rest = evaluated_positions()
    chest = arm.pose.bones['Chest']
    baseline = chest.matrix_basis.copy()
    chest.rotation_mode = 'XYZ'
    chest.rotation_euler.y = math.radians(3)
    displaced = evaluated_positions()
    chest.matrix_basis = baseline
    restored = evaluated_positions()
    deformation = float(np.max(np.linalg.norm(displaced - rest, axis=1)))
    assert deformation > 0.0001 and np.max(np.abs(restored - rest)) < 0.00001

    # Keep the GLB skeleton in its original coordinate frame. Normalize through
    # a separate Unity prefab: glTFast and Blender differ on scaled skin roots.
    root = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(root)
    for obj in list(bpy.context.scene.objects):
        if obj != root and obj.parent is None:
            obj.parent = root
    height = float(spec['height'])
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.gltf(filepath=str(dest), export_format='GLB', use_selection=True,
        export_animations=False, export_skins=True, export_all_influences=True,
        export_materials='EXPORT', export_image_format='AUTO', export_yup=True)
    od, op, oa = read_glb(dest)
    assert not od.get('animations') and len(od['skins']) == 1
    ow = np.concatenate([oa(v) for k, v in op['attributes'].items() if k.startswith('WEIGHTS_')], axis=1)
    assert np.max(np.abs(ow.sum(1) - 1)) < 0.0001
    report = {'name': name, 'pipeline_version': 2, 'source_sha256': source_sha,
        'raw_sha256': hashlib.sha256(raw.read_bytes()).hexdigest(),
        'output_sha256': hashlib.sha256(dest.read_bytes()).hexdigest(),
        'output_bytes': dest.stat().st_size, 'triangles': len(oa(op['indices'])) // 3,
        'vertices': len(oa(op['attributes']['POSITION'])), 'joints': len(od['skins'][0]['joints']),
        'animations': len(od.get('animations', [])), 'height_m': height,
        'uv_transfer_max_corner_error': error, 'max_influences': int(np.max(np.sum(ow > 0, axis=1))),
        'weight_sum_max_error': float(np.max(np.abs(ow.sum(1) - 1))),
        'spine_3_degree_max_vertex_displacement': deformation,
        'rest_pose_restore_max_error': float(np.max(np.abs(restored - rest))),
        'materials': len(od.get('materials', [])), 'images': len(od.get('images', [])),
        'bones_and_weights': 'Hyper3D output, not manually corrected',
        'materials_note': 'Original triangle-corner UVs and Blender-imported material reused; images may be re-encoded on export',
        'acceptance': 'Structural binding checks only; action range, props, cloth and final gameplay motion pending'}
    report_path.write_text(json.dumps(report, indent=2) + '\n')
    root.scale = (height / float(np.ptp(positions[:, 1])),) * 3
    root.location.z = height / 2
    render(work, name, height)
    print('PREPARED', name, report['joints'], 'bones', report['animations'], 'clips', flush=True)


def render(work, name, height):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    scene.render.resolution_x = 384
    scene.render.resolution_y = 512
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('Review world')
    scene.world.color = (0.15, 0.15, 0.15)
    scene.view_settings.view_transform = 'AgX'
    def aim(obj):
        obj.rotation_euler = (Vector((0, 0, height / 2)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    for location, power in [((2, -3, 4), 450), ((-3, -2, 1), 180), ((1, 2, 3), 550)]:
        light = bpy.data.lights.new('Review', 'AREA')
        light.energy = power
        light.size = 4
        obj = bpy.data.objects.new('Review', light)
        scene.collection.objects.link(obj)
        obj.location = location
        aim(obj)
    bpy.ops.object.camera_add()
    camera = bpy.context.object
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = height * 1.28
    scene.camera = camera
    for label, angle in [('front', 0), ('side', math.pi / 2), ('back', math.pi)]:
        camera.location = (math.sin(angle) * height * 3, -math.cos(angle) * height * 3, height * 0.6)
        aim(camera)
        scene.render.filepath = str(work / 'previews' / (name + '-' + label + '.png'))
        bpy.ops.render.render(write_still=True)


work = Path(sys.argv[sys.argv.index('--') + 1])
names = sys.argv[sys.argv.index('--') + 2:]
specs = json.loads((work / 'specs.json').read_text())
for folder in ['outputs', 'previews']:
    (work / folder).mkdir(exist_ok=True)
failures = []
for name in names:
    try:
        prepare(work, name, specs[name])
    except Exception as error:
        failures.append(name)
        print('PREPARE_FAILED', name, repr(error), flush=True)
if failures:
    raise RuntimeError('Incomplete binding preparation: ' + ', '.join(failures))
