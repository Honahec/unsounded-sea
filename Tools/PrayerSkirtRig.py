"""Add a weighted, two-level eight-panel skirt to the UV-restored Prayer binding.
blender -b --python Tools/PrayerSkirtRig.py -- INPUT.glb OUTPUT.glb REPORT.json
Keeps the original skeleton and upper-body weights; never touches the raw archive.
"""
import bpy, sys, math, json, hashlib
from pathlib import Path
from mathutils import Vector
source, output, report = map(Path, sys.argv[sys.argv.index('--') + 1:])
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(source))
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
points = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
zmin = min(v.z for v in points); height = max(v.z for v in points) - zmin
cx = (min(v.x for v in points) + max(v.x for v in points)) * .5
cy = (min(v.y for v in points) + max(v.y for v in points)) * .5
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
names=[]
for i in range(8):
    angle=i*math.tau/8
    radial=Vector((math.cos(angle), math.sin(angle), 0))
    a=Vector((cx,cy,zmin+height*.51))+radial*height*.055
    b=Vector((cx,cy,zmin+height*.31))+radial*height*.10
    c=Vector((cx,cy,zmin+height*.15))+radial*height*.15
    upper=arm.data.edit_bones.new(f'Skirt_{i:02}_Upper')
    upper.head=arm.matrix_world.inverted()@a;upper.tail=arm.matrix_world.inverted()@b
    upper.parent=arm.data.edit_bones['Hips']
    lower=arm.data.edit_bones.new(f'Skirt_{i:02}_Hem')
    lower.head=upper.tail;lower.tail=arm.matrix_world.inverted()@c
    lower.parent=upper;lower.use_connect=True
    names.append((upper.name,lower.name))
bpy.ops.object.mode_set(mode='OBJECT')
groups=[(mesh.vertex_groups.new(name=a),mesh.vertex_groups.new(name=b)) for a,b in names]
def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
changed=0;max_boot_weight=0
for vertex, point in zip(mesh.data.vertices, points):
    y=(point.z-zmin)/height
    # Boots remain entirely outside the wind field; top fades below the held hands.
    mask=smooth(.105,.165,y)*(1-smooth(.34,.52,y))
    if mask<=1e-7:continue
    changed+=1
    theta=math.atan2(point.y-cy,point.x-cx)
    sectors=[max(0,math.cos(theta-i*math.tau/8))**6 for i in range(8)]
    total=sum(sectors)
    hem=1-smooth(.18,.35,y)
    original=[(g.group,g.weight) for g in vertex.groups]
    for group,weight in original:mesh.vertex_groups[group].add([vertex.index],weight*(1-mask),'REPLACE')
    for i,(upper,lower) in enumerate(groups):
        w=mask*sectors[i]/total
        if w*(1-hem)>1e-7:upper.add([vertex.index],w*(1-hem),'REPLACE')
        if w*hem>1e-7:lower.add([vertex.index],w*hem,'REPLACE')
    if y<.105:max_boot_weight=max(max_boot_weight,mask)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=str(output),export_format='GLB',use_selection=True,
    export_animations=False,export_skins=True,export_all_influences=True,export_materials='EXPORT',export_image_format='AUTO')
report.write_text(json.dumps({'source_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),
    'output_sha256':hashlib.sha256(output.read_bytes()).hexdigest(),'output_bytes':output.stat().st_size,
    'skirt_bones':[n for pair in names for n in pair],'modified_vertices':changed,
    'mask_height_fraction':[.105,.165,.34,.52],'boot_wind_weight':max_boot_weight,
    'method':'Eight overlapping angular sectors, waist/hem chain. Preserve source weights outside skirt; blend to wind bones only below hands and above boots.',
    'limitations':'Controlled supernatural gust deformation, not cloth collision simulation. Unity imports four influences per vertex; inspect final skinning.'},indent=2)+'\n')
print('SKIRT_RIG',changed,'vertices')
