"""Author anatomical skins over the archived assembled BANG parts (Blender 5.x).

blender -b --python Tools/GiantBossRig.py -- INPUT_DIR OUTPUT_DIR
Input: the four unskinned *_BANG_LOD0.glb files from revision 3430ecc.
Output: skinned GLBs, per-asset construction/UV/weight reports. Does not generate
new geometry or textures. All coordinates below use Blender's Z-up model space.
Hard shells are one-bone weighted; only named soft regions use short chains.
"""
import bpy, sys, json, hashlib, math
from pathlib import Path
from mathutils import Vector

NAMES=['TideBonesChoirmaster','InvertedSurveyor','UnlitFlagBearer','StarlessWatcher']

EXPECTED_INPUT_SHA256={'TideBonesChoirmaster': 'e52c5facb59d9ed6b4c723b89ee1cbdec956e14e136b3471ec07487906716e0f', 'InvertedSurveyor': '6ae17694ba9702a32be54238a756ee66eedfe2717c409b6b2cc5932196d42414', 'UnlitFlagBearer': 'b81e1345a7eb414855e8c82aab6f633ce304502491cb0f5cc405ea03c4d62536', 'StarlessWatcher': '635e245013070b36f27f395c26686ed044cf345342c423973679083e8c0c4737'}

def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest()
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)

def rig_spec(name):
 # name, head, parent; all bone rest axes are parallel, simplifying additive poses.
 if name==NAMES[0]:
  return [('Body',(0,.05,.65),None),('Head',(0,-.60,.63),'Body'),('Gullet',(0,-.2,.8),'Body'),('Fin_L',(-.14,-.14,.50),'Body'),('Fin_R',(.14,-.14,.50),'Body'),('Tail',(0,.38,.70),'Body'),('TailTip',(0,.66,.50),'Tail')]
 if name==NAMES[1]:
  return [('Body',(0,0,.82),None),('Arch_L',(-.20,0,.8),'Body'),('Arch_R',(.20,0,.8),'Body'),('Pendulum',(-.10,-.1,.18),'Body'),('PendulumTip',(-.1,-.1,-.35),'Pendulum'),('Pendant_R',(.19,-.2,-.1),'Arch_R'),('Pendant_Back',(-.04,.18,-.29),'Body')]
 if name==NAMES[2]:
  return [('Body',(.23,0,.85),None),('Head',(.23,0,1.39),'Body'),('Pole',(-.10,-.2,1.09),'Body'),('FlagTop',(-.25,-.17,1.68),'Pole'),('FlagMid',(-.35,-.17,1.2),'FlagTop'),('FlagLow',(-.44,-.17,.7),'FlagMid'),('FreeArm',(.44,-.02,1.23),'Body')]
 return [('Body',(0,0,.92),None),('Head',(0,-.02,1.38),'Body'),('Arch_L',(-.50,.16,.18),None),('Arch_R',(.50,.16,.18),None),('Curtain_L',(-.33,-.12,1.04),'Body'),('CurtainMid_L',(-.38,-.10,.63),'Curtain_L'),('CurtainLow_L',(-.43,-.10,.23),'CurtainMid_L'),('Curtain_R',(.33,-.12,1.04),'Body'),('CurtainMid_R',(.38,-.10,.63),'Curtain_R'),('CurtainLow_R',(.43,-.10,.23),'CurtainMid_R')]

def blend(a,b,w):return {a:1-w,b:w}
def chain(names,heights,z):
 for i in range(len(heights)-1):
  if z>=heights[i+1]:return blend(names[i],names[i+1],1-smooth(heights[i+1],heights[i],z))
 return {names[-1]:1}

def weights(name,part,v):
 x,y,z=v
 if name==NAMES[0]:
  role={1:'Body',2:'Gullet',3:'Fin_R',4:'Fin_L',5:'TailTip',6:'Head',7:'Tail',8:'Head',9:'TailTip',10:'Tail'}[part]
  if part==2:return blend('Body','Gullet',1-smooth(.55,.82,z))
  return {role:1}
 if name==NAMES[1]:
  role={1:'Body',2:'Arch_L',3:'Arch_R',4:'Pendulum',5:'Pendant_R',6:'Body',7:'Arch_L',8:'Arch_R',9:'Pendant_Back'}[part]
  if part==4:return blend('Pendulum','PendulumTip',1-smooth(-.34,.08,z))
  return {role:1}
 if name==NAMES[2]:
  if part==2:
   # Follow the visible pole axis. The crossbar and attachment strip stay rigid;
   # only fabric to its outside and below its crossbar gets the cloth chain.
   pole_x=-.10 + (1.09-z)*.024
   cloth=(1-smooth(pole_x-.105,pole_x-.055,x))*(1-smooth(1.62,1.72,z))
   result={k:w*cloth for k,w in chain(['FlagTop','FlagMid','FlagLow'],[1.66,1.15,.55],z).items()}
   result['Pole']=1-cloth;return result
  if part==3:return {'Body':1}
  if part==4:return {'FreeArm':1}
  head=smooth(1.36,1.45,z)
  if head>0:return blend('Body','Head',head)
  # The grip and forearm follow the pole; shoulders are fixed under their plate.
  pole=(1-smooth(-.055,.06,x))*smooth(.87,1.02,z)*(1-smooth(1.18,1.31,z))
  free=smooth(.38,.45,x)*(1-smooth(1.17,1.29,z))*smooth(.73,.88,z)
  return {'Body':1-max(pole,free),'Pole':pole,'FreeArm':free}
 # Watcher: rigid external arches, drapery chains only below shoulder attachment.
 if part==2:return {'Arch_R':1}
 if part==3:return {'Arch_L':1}
 side='L' if x<0 else 'R'
 if part in (4,5):return {'Curtain_'+side:1}
 cloth=smooth(.16,.31,abs(x))*(1-smooth(.91,1.09,z))
 head=smooth(1.35,1.48,z)
 if head>0:return blend('Body','Head',head)
 result={k:w*cloth for k,w in chain(['Curtain_'+side,'CurtainMid_'+side,'CurtainLow_'+side],[1.04,.61,.2],z).items()}
 result['Body']=1-cloth;return result

def split_flag_cloth(obj):
 # Split faces without remeshing or moving a vertex. The shared transition strip
 # has identical weights in both renderers so the seam remains joined.
 bpy.context.tool_settings.mesh_select_mode=(False,False,True)
 for v in obj.data.vertices:v.select=False
 for e in obj.data.edges:e.select=False
 for p in obj.data.polygons:
  c=p.center;pole_x=-.10+(1.09-c.z)*.024
  p.select=(c.x<pole_x-.065 and c.z<1.70)
 bpy.context.view_layer.objects.active=obj;obj.select_set(True)
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.separate(type='SELECTED');bpy.ops.object.mode_set(mode='OBJECT')
 parts=[o for o in bpy.context.selected_objects if o.type=='MESH']
 assert len(parts)==2,'Flag cloth split did not create two meshes'
 for p in parts:
  # Smaller mean X is the cloth, independently bound but original UV preserved.
  p.name='FlagCloth' if sum(v.co.x for v in p.data.vertices)/len(p.data.vertices)<-.30 else 'FlagPole'
  p.select_set(False)
 return parts

def signature(meshes):
 # Order-independent triangle corner signatures survive face separation.
 rows=[]
 for o in meshes:
  uv=o.data.uv_layers.active.data
  for p in o.data.polygons:
   corners=[]
   for i in p.loop_indices:
    v=o.data.vertices[o.data.loops[i].vertex_index].co;t=uv[i].uv
    corners.append(tuple(round(float(a),6) for a in (*v,*t)))
   rows.append(tuple(sorted(corners)))
 return hashlib.sha256(repr(sorted(rows)).encode()).hexdigest()

def build(name,inp,out):
 bpy.ops.wm.read_factory_settings(use_empty=True)
 src=inp/(name+'_BANG_LOD0.glb')
 assert sha(src)==EXPECTED_INPUT_SHA256[name], 'Expected the archived unskinned BANG LOD0; do not rig an already skinned output'
 bpy.ops.import_scene.gltf(filepath=str(src))
 meshes=sorted([o for o in bpy.context.scene.objects if o.type=='MESH'],key=lambda o:o.name)
 for o in meshes:
  mw=o.matrix_world.copy();o.parent=None;o.matrix_world=mw
  bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
  bpy.ops.object.transform_apply(location=True,rotation=True,scale=True);o.select_set(False)
 for o in list(bpy.context.scene.objects):
  if o.type!='MESH':bpy.data.objects.remove(o,do_unlink=True)
 before=signature(meshes);spec=rig_spec(name)
 assignments={o.name:int(o.name.split('_')[1]) for o in meshes}
 if name==NAMES[2]:
  flag=meshes[1];meshes.remove(flag)
  for o in split_flag_cloth(flag):assignments[o.name]=2;meshes.append(o)
 after=signature(meshes);assert before==after,'UV or geometry changed during partition'
 armdata=bpy.data.armatures.new(name+'_Armature');arm=bpy.data.objects.new(name+'_Rig',armdata);bpy.context.collection.objects.link(arm)
 bpy.ops.object.select_all(action='DESELECT');arm.select_set(True);bpy.context.view_layer.objects.active=arm;bpy.ops.object.mode_set(mode='EDIT')
 for n,pos,parent in spec:
  b=armdata.edit_bones.new('GB_'+n);b.head=pos;b.tail=Vector(pos)+Vector((0,0,.1))
  if parent:b.parent=armdata.edit_bones['GB_'+parent]
 bpy.ops.object.mode_set(mode='OBJECT')
 influences=0;error=0;mapping=[]
 for o in meshes:
  groups={n:o.vertex_groups.new(name='GB_'+n) for n,_,_ in spec}
  part=assignments[o.name];used=set()
  for v in o.data.vertices:
   ws={k:w for k,w in weights(name,part,v.co).items() if w>1e-7};total=sum(ws.values());assert total>0
   influences=max(influences,len(ws));error=max(error,abs(total-1))
   for n,w in ws.items():groups[n].add([v.index],w/total,'REPLACE');used.add(n)
  mod=o.modifiers.new('Anatomical skin','ARMATURE');mod.object=arm;o.parent=arm
  mapping.append({'mesh':o.name,'bang_part':part,'bones':sorted(used),'triangles':len(o.data.polygons)})
 # Verify a posed skin genuinely moves and its bind pose restores exactly.
 deps=bpy.context.evaluated_depsgraph_get()
 def positions():
  bpy.context.view_layer.update();return [[v.co.copy() for v in o.evaluated_get(deps).data.vertices] for o in meshes]
 neutral=positions()
 for b in arm.pose.bones:b.rotation_mode='XYZ';b.rotation_euler.x=math.radians(3)
 posed=positions();displacement=max((a-b).length for aa,bb in zip(neutral,posed) for a,b in zip(aa,bb))
 for b in arm.pose.bones:b.rotation_euler=(0,0,0)
 restored=positions();restore=max((a-b).length for aa,bb in zip(neutral,restored) for a,b in zip(aa,bb));assert displacement>.005 and restore<1e-5
 bpy.ops.object.select_all(action='SELECT');dst=out/(name+'_BANG_LOD0.glb')
 bpy.ops.export_scene.gltf(filepath=str(dst),export_format='GLB',use_selection=True,export_skins=True,export_animations=False,export_morph=False,export_materials='EXPORT',export_image_format='AUTO',export_all_influences=False)
 report={'asset':name,'method':'Anatomical armature on assembled BANG parts; rigid shell weights and localized soft chains','input_sha256':sha(src),'output_sha256':sha(dst),'bones':[{'name':'GB_'+n,'head_blender':list(p),'parent':('GB_'+parent if parent else None)} for n,p,parent in spec],'meshes':mapping,'triangle_uv_signature_before':before,'triangle_uv_signature_after':after,'max_influences':influences,'weight_sum_error':error,'pose_test_displacement':displacement,'bind_restore_error':restore,'embedded_clips':0,'animation_driver':'GiantBossMotion; authored curves in GiantBossSetup','target_device_performance_tested':False}
 (out/(name+'_Rig-manifest.json')).write_text(json.dumps(report,indent=2)+'\n')
 print(json.dumps({'asset':name,'bones':len(spec),'triangles':sum(len(o.data.polygons) for o in meshes),'restore':restore}))
if __name__=='__main__':
 args=sys.argv[sys.argv.index('--')+1:];inp,out=map(Path,args[:2]);out.mkdir(parents=True,exist_ok=True)
 for name in NAMES:build(name,inp,out)
