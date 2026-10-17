using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Editor
{
    /// <summary>Rebuilds only the M04 set dressing. Encounter definitions and target IDs stay intact.</summary>
    public static class UnlitFlagshipSetup
    {
        const string Art="Assets/UnsoundedSea/Art/";
        public static string Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Stop Play and save scene edits first.");
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"Models/UnlitFlagship_LOD0.glb");
            if(source==null)throw new InvalidOperationException("Import the archived flagship first.");
            Material Material(string name,Color color,float metallic,float smoothness)
            {
                var p=Art+"Bosses/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);
                if(m==null){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,p);}
                m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
                EditorUtility.SetDirty(m);return m;
            }
            var iron=Material("FlagshipIron",new Color(.18f,.17f,.14f),.55f,.22f);
            var ward=Material("FlagshipWard",new Color(.19f,.22f,.19f),.4f,.2f);
            ward.EnableKeyword("_EMISSION");ward.SetColor("_EmissionColor",new Color(.12f,.3f,.27f));
            var path=Art+"Bosses/M04Stage.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var stage=root.GetComponent<BattleStage>();
                var old=root.transform.Find("Drifting flagship");
                // Keep nested authored anchors/targets when rebuilding this presentation.
                if(old!=null)
                {
                    stage.actorAnchors[0].SetParent(root.transform,true);
                    foreach(var target in old.GetComponentsInChildren<BossTargetAnchor>())target.transform.SetParent(root.transform,true);
                    UnityEngine.Object.DestroyImmediate(old.gameObject);
                }
                var pivot=new GameObject("Drifting flagship").transform;pivot.SetParent(root.transform,false);pivot.localPosition=new Vector3(0,0,190);
                var hull=(GameObject)PrefabUtility.InstantiatePrefab(source,pivot);
                hull.name="Ancient flagship wreck";
                var bounds=BattleActorView.MeasureLocalBounds(hull.transform);
                var scale=220/bounds.size.z;hull.transform.localScale=new Vector3(1.5f,1,1.1f)*scale;
                hull.transform.localRotation=Quaternion.Euler(0,180,0);
                hull.transform.localPosition=new Vector3(-bounds.center.x*scale,-12-bounds.min.y*scale,-bounds.center.z*scale);
                stage.actorAnchors[0].SetParent(pivot,true);
                stage.actorAnchors[0].position=new Vector3(-20,-55,120);
                stage.actorAnchors[0].rotation=Quaternion.Euler(0,145,0);
                stage.actorAnchors[0].localScale=Vector3.one*55;
                var anchor=stage.actorAnchors[0].GetComponent<BattleAnchor>();anchor.emergeDepth=0;anchor.heave=0;anchor.waterRadius=0;
                stage.battleCamera.transform.localPosition=new Vector3(110,65,-55);
                stage.battleCamera.transform.localRotation=Quaternion.LookRotation(new Vector3(-10,15,140)-stage.battleCamera.transform.localPosition,Vector3.up);
                stage.battleCamera.fieldOfView=64;
                stage.fogColor=stage.skybox.GetColor("_Horizon");
                var presentation=root.GetComponent<FlagshipPresentation>()??root.AddComponent<FlagshipPresentation>();
                presentation.hull=pivot;presentation.chainMaterial=iron;
                presentation.dustMaterial=AssetDatabase.LoadAssetAtPath<Material>(Art+"Bosses/GiantMist.mat");
                var roots=new System.Collections.Generic.List<Transform>();var wards=new System.Collections.Generic.List<Renderer>();
                for(int i=0;i<4;i++)
                {
                    float x=(i%2==0?-1:1)*(i<2?28:34),z=i<2?145:180;
                    var cleat=Primitive("Captive iron bollard",PrimitiveType.Cube,pivot,new Vector3(x,23,z),new Vector3(3,9,4),iron);
                    cleat.transform.rotation=Quaternion.Euler(0,15*(i-1),i%2==0?-8:8);
                    var socket=new GameObject("Chain cleat "+i).transform;socket.SetParent(pivot,false);socket.position=new Vector3(x,28,z);roots.Add(socket);
                }
                presentation.tetherRoots=roots.ToArray();
                // Narrow structural ribs surround the waist; the source hull carries the broad silhouette.
                for(int i=0;i<7;i++)
                {
                    float a=(25+i*47)*Mathf.Deg2Rad;
                    var rib=Primitive("Cradle rib",PrimitiveType.Cube,pivot,new Vector3(Mathf.Cos(a)*22,24,135+Mathf.Sin(a)*17),new Vector3(3.2f,27,4.6f),iron);
                    rib.transform.rotation=Quaternion.Euler(Mathf.Sin(a)*-12, -a*Mathf.Rad2Deg,Mathf.Cos(a)*12);
                }
                var targets=root.GetComponentsInChildren<BossTargetAnchor>();
                foreach(var target in targets)
                {
                    var id=target.targetId;int lockIndex=id=="lock-I"?0:id=="lock-II"?1:2;
                    bool relay=id.StartsWith("R",StringComparison.Ordinal);bool first=id.StartsWith("R1",StringComparison.Ordinal);
                    int terminal=id.EndsWith("off",StringComparison.Ordinal)?0:id.EndsWith("positive",StringComparison.Ordinal)?1:2;
                    target.transform.SetParent(pivot,true);
                    target.transform.position=relay?new Vector3(first?-25:24,29,(first?164:235)+terminal*4):new Vector3(-12+lockIndex*12,30,148);
                    target.transform.localScale=Vector3.one*(relay?3:4);
                }
                for(int i=0;i<3;i++)
                {
                    var clasp=Primitive("Protective iron clasp",PrimitiveType.Cube,pivot,new Vector3(-12+i*12,37,119),new Vector3(8,.8f,1),ward);
                    wards.Add(clasp.GetComponent<Renderer>());
                }
                presentation.wardRings=wards.ToArray();
                var fill=root.GetComponentsInChildren<Light>().Single(l=>l.name=="Sea creature soft fill");
                fill.transform.position=new Vector3(85,90,80);fill.intensity=1900;fill.range=330;fill.color=new Color(.63f,.71f,.73f);
                var rim=root.GetComponentsInChildren<Light>().Single(l=>l.name=="Sea creature rim");
                rim.transform.position=new Vector3(-75,120,260);rim.intensity=2400;rim.range=350;rim.color=new Color(.75f,.72f,.58f);
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            path=Art+"Prefabs/UnlitFlagBearer.prefab";root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var motion=root.GetComponentInChildren<GiantBossMotion>();motion.poleMaxAdvance=8;motion.poleDrawDistance=4;
                // Fit the socket to the actual lower cap, not a guessed model-space coordinate.
                var pole=root.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="FlagPole");
                var mesh=new Mesh();
                try
                {
                    pole.BakeMesh(mesh,true);
                    var points=mesh.vertices.Select(v=>pole.transform.TransformPoint(v)).ToArray();
                    var min=points.Min(p=>p.y);var cap=points.Where(p=>p.y<min+.02f).ToArray();
                    motion.effectOrigin.position=cap.Aggregate(Vector3.zero,(sum,p)=>sum+p)/cap.Length;
                    motion.effectOrigin.name="Physical pole cap";
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
                PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
            AssetDatabase.SaveAssets();return "M04: captive flag bearer on a drifting flagship, nine retained mechanism targets, physical pole reach and chained hull motion.";
        }
        static GameObject Primitive(string name,PrimitiveType type,Transform parent,Vector3 position,Vector3 scale,Material material)
        {
            var go=GameObject.CreatePrimitive(type);go.name=name;go.transform.SetParent(parent,false);go.transform.position=position;go.transform.localScale=scale;
            go.GetComponent<Renderer>().sharedMaterial=material;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;
        }
    }
}
