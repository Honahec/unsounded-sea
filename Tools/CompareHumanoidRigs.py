"""Read-only Blender rig comparison. No model, skeleton, weights or clips are saved.
blender -b --python Tools/CompareHumanoidRigs.py -- NEW_DIR OLD_DIR OUTPUT_DIR NAME ...
Writes paired neutral/diagnostic views, joint landmarks and strain statistics.
Angles are defined in the character's world axes, conjugated into each bone's
rest basis so different local bone rolls do not invalidate the comparison.
"""
import bpy
import hashlib
import json
import math
import sys
from pathlib import Path
import numpy as np
from mathutils import Vector, Quaternion, Matrix
from mathutils.kdtree import KDTree
from bpy_extras.object_utils import world_to_camera_view

MAP = {
 'hips':('Hips','hips'),'spine':('Spine','spine'),'chest':('Chest','chest'),
 'neck':('Neck','neck'),'head':('Head','head'),
 'shoulder.L':('LeftShoulder','shoulder.L'),'shoulder.R':('RightShoulder','shoulder.R'),
 'upper_arm.L':('LeftUpperArm','upper_arm.L'),'upper_arm.R':('RightUpperArm','upper_arm.R'),
 'forearm.L':('LeftLowerArm','forearm.L'),'forearm.R':('RightLowerArm','forearm.R'),
 'hand.L':('LeftHand','hand.L'),'hand.R':('RightHand','hand.R'),
 'thigh.L':('LeftUpperLeg','thigh.L'),'thigh.R':('RightUpperLeg','thigh.R'),
 'shin.L':('LeftLowerLeg','shin.L'),'shin.R':('RightLowerLeg','shin.R'),
 'foot.L':('LeftFoot','foot.L'),'foot.R':('RightFoot','foot.R')}
CHAINS=[['hips','spine','chest','neck','head']]+[
 ['chest','shoulder.'+s,'upper_arm.'+s,'forearm.'+s,'hand.'+s] for s in ['L','R']]+[
 ['hips','thigh.'+s,'shin.'+s,'foot.'+s] for s in ['L','R']]
POSES={'rest':{},'torso':{'chest':((0,0,1),15)},
 'elbows':{'forearm.L':((1,0,0),-40),'forearm.R':((1,0,0),-40)},
 'knee':{'shin.L':((1,0,0),35)}}
VIEWS=[('rest','front'),('rest','side'),('torso','front'),('elbows','front'),('elbows','side'),('knee','side')]


def vertices(mesh):
 bpy.context.view_layer.update()
 obj=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
 data=obj.to_mesh()
 try:
  coords=np.empty(len(data.vertices)*3);data.vertices.foreach_get('co',coords)
  coords=coords.reshape(-1,3);m=np.array(mesh.matrix_world)
  return coords@m[:3,:3].T+m[:3,3]
 finally:obj.to_mesh_clear()


def setup(path,kind):
 bpy.ops.wm.read_factory_settings(use_empty=True)
 bpy.ops.import_scene.gltf(filepath=str(path))
 arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
 mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
 for obj in bpy.context.scene.objects:obj.animation_data_clear()
 for b in arm.pose.bones:b.matrix_basis=Matrix.Identity(4)
 rest=vertices(mesh);low=rest.min(0);high=rest.max(0);scale=2/(high[2]-low[2])
 root=bpy.data.objects.new('Review normalization',None);bpy.context.scene.collection.objects.link(root)
 for obj in list(bpy.context.scene.objects):
  if obj!=root and obj.parent is None:obj.parent=root
 root.scale=(scale,)*3;root.location=-Vector(((low[0]+high[0])/2,(low[1]+high[1])/2,low[2]))*scale
 bpy.context.view_layer.update()
 mapping={k:v[kind] for k,v in MAP.items()};assert all(n in arm.pose.bones for n in mapping.values())
 rest=vertices(mesh)
 return arm,mesh,mapping,rest


def pose(arm,mapping,changes):
 for b in arm.pose.bones:b.matrix_basis=Matrix.Identity(4)
 for semantic,(axis,angle) in changes.items():
  bone=arm.pose.bones[mapping[semantic]]
  q=(arm.matrix_world.to_quaternion()@bone.bone.matrix_local.to_quaternion()).normalized()
  bone.rotation_mode='QUATERNION';bone.rotation_quaternion=q.inverted()@Quaternion(Vector(axis),math.radians(angle))@q
 bpy.context.view_layer.update()


def weights(mesh,arm):
 names=[b.name for b in arm.pose.bones];index={n:i for i,n in enumerate(names)}
 groups={g.index:index[g.name] for g in mesh.vertex_groups if g.name in index}
 w=np.zeros((len(mesh.data.vertices),len(names)))
 for vertex in mesh.data.vertices:
  for g in vertex.groups:
   if g.group in groups:w[vertex.index,groups[g.group]]=g.weight
 assert np.max(abs(w.sum(1)-1))<1e-4
 top=np.argsort(w,axis=1)[:,-4:];w4=np.zeros_like(w);rows=np.arange(len(w))[:,None];w4[rows,top]=w[rows,top]
 removed=1-w4.sum(1);w4/=w4.sum(1)[:,None]
 coords=np.array([(*v.co,1) for v in mesh.data.vertices])
 return names,w,w4,coords,removed


def linear_skin(arm,mesh,names,w,coords):
 out=np.zeros((len(coords),3))
 for i,name in enumerate(names):
  b=arm.pose.bones[name]
  m=np.array(arm.matrix_world@b.matrix@b.bone.matrix_local.inverted()@arm.matrix_world.inverted()@mesh.matrix_world)
  out+=(coords@m.T)[:,:3]*w[:,i,None]
 return out


def lighting():
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8;scene.cycles.use_denoising=True
 scene.render.resolution_x=240;scene.render.resolution_y=340;scene.render.resolution_percentage=100
 scene.world=bpy.data.worlds.new('Review');scene.world.color=(.15,.15,.15);scene.view_settings.view_transform='AgX'
 for xyz,power in [((2,-3,4),450),((-3,-2,1),180),((1,2,3),550)]:
  data=bpy.data.lights.new('Review','AREA');data.energy=power;data.size=4
  obj=bpy.data.objects.new('Review',data);scene.collection.objects.link(obj);obj.location=xyz
  obj.rotation_euler=(Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
 bpy.ops.object.camera_add();camera=bpy.context.object;camera.data.type='ORTHO';camera.data.ortho_scale=2.7;scene.camera=camera
 return scene,camera


def locality(arm, mesh, mapping, rest):
 # Conservative spatial probes, not a semantic cloth/body segmentation.
 pose(arm,mapping,POSES['elbows']);elbow_delta=np.linalg.norm(vertices(mesh)-rest,axis=1)
 pose(arm,mapping,POSES['knee']);knee_delta=np.linalg.norm(vertices(mesh)-rest,axis=1)
 feet=rest[:,2]<.2
 right_foot=feet & (rest[:,0]<-.05)
 result={
  'elbows_lowest_20cm_max_motion_m':float(elbow_delta[feet].max()) if feet.any() else None,
  'left_knee_right_foot_region_max_motion_m':float(knee_delta[right_foot].max()) if right_foot.any() else None,
  'left_knee_right_foot_vertices_over_1cm':int(np.sum(knee_delta[right_foot]>.01)),
  'right_foot_region_vertex_count':int(right_foot.sum())}
 pose(arm,mapping,{})
 return result


def run(path,kind,out,no_render=False):
 arm,mesh,mapping,rest=setup(path,kind);names,w,w4,coords,removed=weights(mesh,arm)
 edges=np.array([e.vertices[:] for e in mesh.data.edges]);base=np.linalg.norm(rest[edges[:,0]]-rest[edges[:,1]],axis=1);valid=base>1e-5
 landmarks={k:list(arm.matrix_world@arm.pose.bones[n].head) for k,n in mapping.items()}
 report={'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'bones':len(arm.data.bones),'vertices':len(rest),
  'landmarks_at_2m_height':landmarks,'influences_max':int(np.max((w>0).sum(1))),
  'top4_removed_mass_max':float(removed.max()),'vertices_losing_over_5pct_weight':float(np.mean(removed>.05)),
  'poses':{},'screens':{},'locality':locality(arm,mesh,mapping,rest)}
 scene,camera=lighting()
 for label,changes in POSES.items():
  pose(arm,mapping,changes);deformed=vertices(mesh)
  ratio=np.linalg.norm(deformed[edges[:,0]]-deformed[edges[:,1]],axis=1)[valid]/base[valid]
  full=linear_skin(arm,mesh,names,w,coords);four=linear_skin(arm,mesh,names,w4,coords)
  cpu_error=float(np.max(np.linalg.norm(full-deformed,axis=1)));assert cpu_error<1e-4,(path.name,cpu_error)
  report['poses'][label]={'edge_ratio_p01':float(np.percentile(ratio,1)),'edge_ratio_p99':float(np.percentile(ratio,99)),
   'edges_over_1_5x_fraction':float(np.mean(ratio>1.5)),'edges_under_half_fraction':float(np.mean(ratio<.5)),
   'max_displacement_m':float(np.max(np.linalg.norm(deformed-rest,axis=1))),
   'top4_vs_full_max_m':float(np.max(np.linalg.norm(full-four,axis=1))),
   'top4_vs_full_p99_m':float(np.percentile(np.linalg.norm(full-four,axis=1),99)),
   'cpu_skin_vs_blender_max_error':cpu_error}
  for target,side in VIEWS:
   if target!=label or no_render:continue
   camera.location=(0,-6,1.15) if side=='front' else (6,0,1.15)
   camera.rotation_euler=(Vector((0,0,1))-camera.location).to_track_quat('-Z','Y').to_euler()
   scene.render.filepath=str(out/f'{label}-{side}.png');bpy.ops.render.render(write_still=True)
   if label=='rest':
    screen={}
    for semantic,point in landmarks.items():
     xy=world_to_camera_view(scene,camera,Vector(point));screen[semantic]=[xy.x*240,(1-xy.y)*340]
    report['screens'][side]=screen
 pose(arm,mapping,{})
 report['restore_error_m']=float(np.max(np.abs(vertices(mesh)-rest)))
 np.save(out/'rest.npy',rest)
 if no_render and (out/'metrics.json').exists():
  report['screens']=json.loads((out/'metrics.json').read_text())['screens']
 (out/'metrics.json').write_text(json.dumps(report,indent=2)+'\n')
 return report


if __name__=='__main__':
 args=sys.argv[sys.argv.index('--')+1:];no_render='--no-render' in args
 if no_render:args.remove('--no-render')
 new,old,out=map(Path,args[:3])
 for name in args[3:]:
  reports={}
  for kind,folder in enumerate([new,old]):
   file=folder/(name+'_Rigged.glb')
   if not file.exists():continue
   target=out/name/('hyper3d' if kind==0 else 'rigtrials');target.mkdir(parents=True,exist_ok=True)
   reports[str(kind)]=run(file,kind,target,no_render)
  if len(reports)==2:
   a=np.load(out/name/'hyper3d/rest.npy');b=np.load(out/name/'rigtrials/rest.npy');tree=KDTree(len(b))
   for i,v in enumerate(b):tree.insert(v,i)
   tree.balance();dist=[tree.find(v)[2] for v in a]
   (out/name/'rest-match.json').write_text(json.dumps({'new_to_old_nearest_max_m':max(dist),'p99_m':float(np.percentile(dist,99))},indent=2)+'\n')
  print('REVIEWED',name,flush=True)
