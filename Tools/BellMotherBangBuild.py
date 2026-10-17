"""Turn a Hyper3D BANG split into a low-poly rigid-part runtime asset.

Run on the Blender workstation after downloading the BANG GLB to /tmp:
  blender -b --python Tools/BellMotherBangBuild.py

The output keeps each semantic BANG part as a separate mesh. It deliberately
does not add a skinned armature: Unity drives the part pivots as rigid pieces,
which avoids deformation artefacts on the fused source mesh.
"""
import bpy, json, math, os, hashlib
from mathutils import Vector, Matrix

ROOT = os.path.expanduser('~/Projects/unsounded-sea')
SRC = '/tmp/BellMother_BANG.glb'
OUT_DIR = os.path.join(ROOT, 'ArtSource/Bosses')
RUN_DIR = os.path.join(ROOT, 'Assets/UnsoundedSea/Art/Models')
GLB = os.path.join(RUN_DIR, 'BellMother_BANG_LOD0.glb')
PREVIEW = os.path.join(OUT_DIR, 'BellMother_BANG-preview.png')
REPORT = os.path.join(OUT_DIR, 'BellMother_BANG-manifest.json')

os.makedirs(OUT_DIR, exist_ok=True)
os.makedirs(RUN_DIR, exist_ok=True)

def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)

def bounds(obj):
    points = [obj.matrix_world @ Vector(corner) for corner in obj.bound_box]
    lo = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    return lo, hi, (lo + hi) * 0.5

def make_empty(name, parent, location):
    pivot = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(pivot)
    pivot.empty_display_type = 'PLAIN_AXES'
    pivot.empty_display_size = 0.08
    pivot.location = location
    pivot.parent = parent
    return pivot

def parent_preserve(child, parent):
    world = child.matrix_world.copy()
    child.parent = parent
    child.matrix_world = world

def classify(meshes):
    # This BANG result is stable for the archived Bell Mother generation:
    # six low/outer parts are tentacles, root.10 is the crown, and the rest
    # are overlapping shell pieces. Keep this explicit so a changed BANG
    # result fails loudly rather than silently animating the wrong silhouette.
    names = {m.name for m in meshes}
    tentacle_names = {'root.0', 'root.4', 'root.12', 'root.14', 'root.16', 'root.17'}
    if not tentacle_names.issubset(names) or 'root.10' not in names:
        raise RuntimeError('Unexpected BANG part set: ' + ', '.join(sorted(names)))
    return tentacle_names, 'root.10'

def decimate(meshes, ratio=0.08):
    before = sum(len(m.data.polygons) for m in meshes)
    for mesh in meshes:
        bpy.context.view_layer.objects.active = mesh
        mesh.select_set(True)
        modifier = mesh.modifiers.new('BellMother_BANG_RuntimeDecimate', 'DECIMATE')
        modifier.decimate_type = 'COLLAPSE'
        modifier.ratio = ratio
        modifier.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        mesh.select_set(False)
    after = sum(len(m.data.polygons) for m in meshes)
    return before, after

def build_hierarchy():
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    if len(meshes) != 12:
        raise RuntimeError('Expected 12 BANG meshes, found %d' % len(meshes))
    tentacle_names, crown_name = classify(meshes)
    root = bpy.data.objects.new('BellMother_BANG_Rigid', None)
    bpy.context.collection.objects.link(root)
    root.empty_display_type = 'PLAIN_AXES'; root.empty_display_size = 0.18
    body = make_empty('Body', root, (0.0, 0.08, 0.52))
    crown = make_empty('Crown', root, (0.0, 0.36, 0.86))
    ordered = sorted(tentacle_names, key=lambda name: math.atan2(bounds(next(m for m in meshes if m.name == name))[2].z, bounds(next(m for m in meshes if m.name == name))[2].x))
    tentacles = []
    for index, name in enumerate(ordered, 1):
        mesh = next(m for m in meshes if m.name == name)
        center = bounds(mesh)[2]
        # The outer pieces pivot near the body, leaving their generated mesh
        # geometry untouched and preventing a large swing around world origin.
        pivot = Vector((center.x * 0.42, -0.13, 0.38))
        tentacle = make_empty('Tentacle_%02d' % index, root, pivot)
        tentacles.append(tentacle)
        mesh.name = 'Tentacle_%02d_Mesh' % index
        parent_preserve(mesh, tentacle)
    for mesh in meshes:
        if mesh.name.startswith('Tentacle_'):
            continue
        if mesh.name == crown_name:
            mesh.name = 'Crown_Mesh'
            parent_preserve(mesh, crown)
        else:
            mesh.name = 'Shell_%02d_Mesh' % (len(body.children) + 1)
            parent_preserve(mesh, body)
    return root, body, crown, tentacles, meshes

def export(root):
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    for child in root.children_recursive:
        child.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.gltf(
        filepath=GLB, export_format='GLB', use_selection=True,
        export_animations=False, export_skins=False, export_morph=False,
        export_materials='EXPORT', export_image_format='AUTO')

def render_preview(root):
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 720; scene.render.resolution_y = 720; scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'; scene.render.filepath = PREVIEW
    world = bpy.data.worlds.new('BellMotherBangPreviewWorld'); scene.world = world; world.color = (0.008, 0.016, 0.02)
    for obj in list(scene.objects):
        if obj.type in {'CAMERA', 'LIGHT'}: bpy.data.objects.remove(obj, do_unlink=True)
    renderables = [obj for obj in root.children_recursive if obj.type == 'MESH']
    points = [obj.matrix_world @ Vector(corner) for obj in renderables for corner in obj.bound_box]
    low = Vector((min(p.x for p in points), min(p.y for p in points), min(p.z for p in points)))
    high = Vector((max(p.x for p in points), max(p.y for p in points), max(p.z for p in points)))
    target = (low + high) * 0.5
    radius = max((high - low).length, 1.0)
    camera_location = target + Vector((radius * 0.78, radius * 0.48, radius * 0.88))
    bpy.ops.object.camera_add(location=camera_location)
    camera = bpy.context.object; scene.camera = camera
    camera.rotation_euler = ((target - camera.location).to_track_quat('-Z', 'Y')).to_euler()
    camera.data.lens = 52
    for loc, energy, size in [((2, 3, 3), 950, 3.5), ((-2.5, 1.4, .5), 450, 2.5), ((0, 1, -3), 700, 2)]:
        bpy.ops.object.light_add(type='AREA', location=loc)
        light = bpy.context.object; light.data.energy = energy; light.data.size = size
        light.rotation_euler = ((Vector((0, 0, 0)) - light.location).to_track_quat('-Z', 'Y')).to_euler()
    bpy.ops.render.render(write_still=True)

def sha(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()

def main():
    if not os.path.isfile(SRC): raise FileNotFoundError(SRC)
    clear_scene(); bpy.ops.import_scene.gltf(filepath=SRC)
    root, body, crown, tentacles, meshes = build_hierarchy()
    before, after = decimate(meshes)
    export(root); render_preview(root)
    report = {
        'asset': 'BellMother', 'provider': 'Hyper3D BANG',
        'source_generation': 'a69dfe2b-089e-4ca1-9987-0d36ec94312c',
        'source_asset': '020d73c8-7721-581f-ae65-3f8fd8c93d1d',
        'method': 'BANG semantic part split; rigid pivot hierarchy; Blender decimate ratio 0.08',
        'parts': 12, 'tentacle_parts': len(tentacles),
        'triangles_before': before, 'triangles_after': after,
        'outputs': {'glb': os.path.relpath(GLB, ROOT), 'preview': os.path.relpath(PREVIEW, ROOT)},
        'sha256': {'glb': sha(GLB), 'preview': sha(PREVIEW)},
    }
    with open(REPORT, 'w', encoding='utf-8') as stream: json.dump(report, stream, ensure_ascii=False, indent=2)
    print(json.dumps(report, ensure_ascii=False, indent=2))

if __name__ == '__main__': main()
