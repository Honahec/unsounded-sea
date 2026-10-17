"""Build Boss/guardian BANG runtime parts: Blender -b --python Tools/BossBangBuild.py -- <asset>."""
import json
import struct
import sys
from pathlib import Path

import bpy
sys.path.insert(0, str(Path(__file__).resolve().parent))
import BellMotherBangBuild as bell

ROOT = Path(__file__).resolve().parent.parent
JOBS = {
    'TideBonesChoirmaster': '3056a97e-803f-469f-8ddc-82d1891d91bf',
    'InvertedSurveyor': '409bcb0b-309b-4377-ad77-ad69cf1ec0df',
    'UnlitFlagBearer': '8d53af76-3933-4dbf-aa3c-443736f00da4',
    'StarlessWatcher': '5d772709-ad20-4832-bb1c-48e0a6512054',
}


def inspect_glb(path):
    data = path.read_bytes()
    magic, version, length, json_length, chunk_type = struct.unpack_from('<4s4I', data)
    assert magic == b'glTF' and version == 2 and length == len(data)
    assert chunk_type == 0x4E4F534A
    document = json.loads(data[20:20 + json_length])
    assert all('uri' not in buffer for buffer in document['buffers'])
    assert all('bufferView' in image for image in document.get('images', []))
    triangles = vertices = 0
    for mesh in document['meshes']:
        for primitive in mesh['primitives']:
            assert primitive.get('mode', 4) == 4
            attributes = primitive['attributes']
            assert all(key in attributes for key in ('POSITION', 'NORMAL', 'TEXCOORD_0'))
            count = document['accessors'][primitive['indices']]['count']
            assert count % 3 == 0
            triangles += count // 3
            vertices += document['accessors'][attributes['POSITION']]['count']
    return dict(bytes=len(data), triangles=triangles, vertices=vertices,
                meshes=len(document['meshes']), materials=len(document.get('materials', [])),
                images=len(document.get('images', [])), skins=len(document.get('skins', [])),
                animations=len(document.get('animations', [])))


def build(asset):
    if asset not in JOBS and asset not in {'StarlessGuard' + kind for kind in ('Anchor', 'Lamp', 'Bell', 'Sail', 'Eye')}:
        raise ValueError('Unknown boss or guardian: ' + asset)
    archive = ROOT / 'ArtSource/Bosses'
    original = json.loads((archive / (asset + '-manifest.json')).read_text())
    source = archive / (asset + '_BANG_High.glb')
    output = ROOT / 'Assets/UnsoundedSea/Art/Models' / (asset + '_BANG_LOD0.glb')
    preview = archive / (asset + '_BANG-preview.png')
    source_stats = inspect_glb(source)
    assert source_stats['meshes'] > 1 and source_stats['skins'] == 0
    bell.clear_scene()
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes = sorted((obj for obj in bpy.context.scene.objects if obj.type == 'MESH'), key=lambda obj: obj.name)
    assert len(meshes) == source_stats['meshes']
    root = bell.make_empty(asset + '_BANG_Rigid', None, (0, 0, 0))
    body = bell.make_empty('Body', root, (0, 0, 0))
    mapping = []
    for index, mesh in enumerate(meshes, 1):
        old_bounds = bell.bounds(mesh)
        source_name = mesh.name
        pivot = bell.make_empty('Part_%02d' % index, body, old_bounds[2])
        bpy.context.view_layer.update()
        bell.parent_preserve(mesh, pivot)
        mesh.name = pivot.name + '_Mesh'
        bpy.context.view_layer.update()
        assert all((a - b).length < 0.00001 for a, b in zip(old_bounds, bell.bounds(mesh)))
        mapping.append(dict(pivot=pivot.name, source_mesh=source_name,
                            center=list(old_bounds[2]), minimum=list(old_bounds[0]), maximum=list(old_bounds[1])))
    budget = original['runtime_budget_triangles']
    ratio = min(1.0, budget / source_stats['triangles'])
    bell.decimate(meshes, ratio)
    texture_limit = original.get('runtime_texture_size')
    if texture_limit:
        for image in bpy.data.images:
            width, height = image.size
            if max(width, height) > texture_limit:
                scale = texture_limit / max(width, height)
                image.scale(round(width * scale), round(height * scale))
    bell.GLB = str(output)
    bell.PREVIEW = str(preview)
    bell.export(root)
    runtime_stats = inspect_glb(output)
    assert runtime_stats['meshes'] == len(meshes)
    assert 0 < runtime_stats['triangles'] <= budget
    assert runtime_stats['skins'] == runtime_stats['animations'] == 0
    bell.render_preview(root, front=asset.startswith('StarlessGuard'))
    report = {
        'asset': asset, 'provider': 'Hyper3D BANG',
        'source_generation': original.get('bang_generation', JOBS.get(asset)), 'source_asset': original['high_generation'],
        'request': {'asset_id': original['high_generation'], 'geometry_file_format': 'glb'},
        'privacy': original.get('privacy', {'basis': 'API output is not published to public ASSETS or shared with other users.',
                                          'policy': 'https://docs.hyper3d.ai/en/legal/data-retention-policy'}),
        'method': 'BANG semantic split; independent rigid part pivots; Blender decimation',
        'parts': len(meshes), 'part_mapping': mapping, 'decimate_ratio': ratio,
        'triangles_before': source_stats['triangles'], 'triangles_after': runtime_stats['triangles'],
        'source_spec': source_stats, 'runtime_spec': runtime_stats,
        'outputs': {key: str(path.relative_to(ROOT)) for key, path in
                    [('source', source), ('glb', output), ('preview', preview)]},
        'sha256': {key: bell.sha(str(path)) for key, path in
                   [('source', source), ('glb', output), ('preview', preview)]},
        'animation_ready': False,
        'notes': ['Part pivots preserve the assembled silhouette. Anatomical joint calibration and combat animation are not included.'],
    }
    (archive / (asset + '_BANG-manifest.json')).write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'asset': asset, 'parts': len(meshes), 'runtime': runtime_stats}))


if __name__ == '__main__':
    build(sys.argv[sys.argv.index('--') + 1])
