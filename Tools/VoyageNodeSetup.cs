using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

// Run explicitly through Unity CLI. Owns only Art/Voyage/Nodes and the destination binding in Voyage.
public static class VoyageNodeSetup
{
    const string Root = "Assets/UnsoundedSea/";
    const string Art = Root + "Art/Voyage/Nodes";
    static Material iron, timber, brass, canvas, glass, lamp, redLamp;
    static Shader shader;
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Folder(Path.GetDirectoryName(path).Replace('\\','/')); AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));
    }
    static void Guard()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before authoring.");
        for(int i=0;i<SceneManager.sceneCount;i++) if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save existing scene edits first.");
    }
    static GameObject Child(string name, Transform parent, Vector3 position)
    {
        var go=new GameObject(name); go.transform.SetParent(parent,false);go.transform.localPosition=position;return go;
    }
    static Material Mat(string name,Color color,float metal=0,Color? emission=null)
    {
        string path=Art+"/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!m) {m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
        m.SetColor("_BaseColor",color);m.SetFloat("_Metallic",metal);m.SetFloat("_Roughness",0.83f);
        if(emission.HasValue)m.SetColor("_EmissionColor",emission.Value);
        else m.SetTexture("_BaseMap",Weathering());
        EditorUtility.SetDirty(m);return m;
    }
    static Texture2D Weathering()
    {
        string path=Art+"/SaltAndRust.png";
        var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(existing)return existing;
        var tex=new Texture2D(512,512,TextureFormat.RGB24,false);
        var pixels=new Color[512*512];
        for(int y=0;y<512;y++)for(int x=0;x<512;x++)
        {
            float coarse=Mathf.PerlinNoise(x/43f,y/58f),grain=Mathf.PerlinNoise(x/3f,y/7f);
            float streak=Mathf.PerlinNoise(x/8f,y/190f);
            var value=Color.Lerp(new Color(.38f,.24f,.15f),new Color(.87f,.91f,.86f),Mathf.SmoothStep(0,1,(coarse-.32f)*3));
            pixels[y*512+x]=value*Mathf.Lerp(.65f,1,grain)*Mathf.Lerp(.8f,1,streak);
        }
        tex.SetPixels(pixels);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    static GameObject Shape(Transform p,string name,PrimitiveType shape,Vector3 pos,Vector3 size,Material mat)
    {
        var g=GameObject.CreatePrimitive(shape);g.name=name;g.transform.SetParent(p,false);g.transform.localPosition=pos;g.transform.localScale=size;
        UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());g.GetComponent<Renderer>().sharedMaterial=mat;return g;
    }
    static void Box(Transform p,string n,Vector3 pos,Vector3 size,Material m)=>Shape(p,n,PrimitiveType.Cube,pos,size,m);
    static void Beam(Transform p,Vector3 a,Vector3 b,float radius,Material m)
    {
        var g=Shape(p,"Rigging / spar",PrimitiveType.Cylinder,(a+b)*.5f,new Vector3(radius*2,(b-a).magnitude*.5f,radius*2),m);
        g.transform.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
    }
    static void Ring(Transform p,Vector3 center,float radius,Material m,int sides=16)
    {
        for(int i=0;i<sides;i++)
        {
            float a=i*Mathf.PI*2/sides,b=(i+1)*Mathf.PI*2/sides;
            Beam(p,center+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius,center+new Vector3(Mathf.Cos(b),0,Mathf.Sin(b))*radius,.055f,m);
        }
    }
    static Transform Float(Transform parent,string name,Vector3 pos,float x=2,float z=5)
    {
        var t=Child(name,parent,pos).transform; t.gameObject.AddComponent<VoyageSceneFloat>().sampleExtents=new Vector2(x,z);return t;
    }
    static void Light(Transform p,Vector3 pos,bool red=false,float strength=35)
    {
        Shape(p,"Lantern glass",PrimitiveType.Sphere,pos,new Vector3(.25f,.4f,.25f),red?redLamp:lamp);
        var light=Child("Navigation lamp",p,pos).AddComponent<Light>();light.type=LightType.Point;light.color=red?new Color(.85f,.13f,.045f):new Color(1,.65f,.29f);light.intensity=strength;light.range=12;
    }
    static void Rail(Transform p,float width,float length,float y)
    {
        foreach(float x in new[]{-width,width})
        {
            Beam(p,new Vector3(x,y+1,-length),new Vector3(x,y+1,length),.05f,iron);
            for(float z=-length;z<=length;z+=2)Beam(p,new Vector3(x,y,z),new Vector3(x,y+1,z),.06f,iron);
        }
    }
    static void Buoy(Transform parent,Vector3 pos,bool red=false,float scale=1)
    {
        var t=Float(parent,"Bell buoy",pos,1,1);t.localScale=Vector3.one*scale;
        Shape(t,"Riveted float",PrimitiveType.Sphere,new Vector3(0,-.1f,0),new Vector3(2.7f,1.6f,2.7f),iron);
        Shape(t,"Collar",PrimitiveType.Cylinder,new Vector3(0,.5f,0),new Vector3(2.4f,.12f,2.4f),brass);
        foreach(float x in new[]{-.65f,.65f})foreach(float z in new[]{-.65f,.65f})Beam(t,new Vector3(x,.6f,z),new Vector3(x*.55f,3.8f,z*.55f),.08f,iron);
        Ring(t,new Vector3(0,2.2f,0),.65f,brass);Ring(t,new Vector3(0,3.7f,0),.55f,iron);
        Shape(t,"Hanging bell",PrimitiveType.Sphere,new Vector3(0,2.8f,0),new Vector3(.55f,.7f,.55f),brass);
        Beam(t,new Vector3(0,3.7f,0),new Vector3(0,4.4f,0),.055f,iron);
        Light(t,new Vector3(0,4.1f,0),red,18);
    }
    static Material Convert(Material source,string key)
    {
        string path=Art+"/"+key+".mat";var result=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!result){result=new Material(shader);AssetDatabase.CreateAsset(result,path);}
        result.SetColor("_BaseColor",source.HasProperty("baseColorFactor")?source.GetColor("baseColorFactor"):Color.white);
        foreach(var pair in new[]{("baseColorTexture","_BaseMap"),("normalTexture","_BumpMap"),("metallicRoughnessTexture","_MetallicRoughnessMap")})
            if(source.HasProperty(pair.Item1))result.SetTexture(pair.Item2,source.GetTexture(pair.Item1));
        result.SetFloat("_Metallic",source.HasProperty("metallicFactor")?source.GetFloat("metallicFactor"):.1f);
        result.SetFloat("_Roughness",source.HasProperty("roughnessFactor")?Mathf.Max(.6f,source.GetFloat("roughnessFactor")):.8f);
        EditorUtility.SetDirty(result);return result;
    }
    static Transform Hull(Transform root,string model,float length,float yaw=0)
    {
        var body=Float(root,model+" afloat",Vector3.zero,length*.16f,length*.4f);
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"Art/Models/"+model+".glb");
        if(!source)throw new InvalidOperationException("Missing generated model "+model);
        var visual=(GameObject)PrefabUtility.InstantiatePrefab(source);visual.transform.SetParent(body,false);
        visual.transform.localRotation=Quaternion.Euler(0,yaw,0);
        var renderers=visual.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
        foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
        float scale=length/bounds.size.z;
        visual.transform.localScale=Vector3.one*scale;
        // Tall deck structures must not push the barge waterline above its low cargo deck.
        float immersion = model == "DerelictBarge" ? .12f : model == "SurveyVessel_LOD0" ? .37f : .28f;
        visual.transform.localPosition=new Vector3(-bounds.center.x,-bounds.min.y-bounds.size.y*immersion,-bounds.center.z)*scale;
        foreach(var renderer in renderers)
        {
            var mats=renderer.sharedMaterials;
            for(int i=0;i<mats.Length;i++)mats[i]=Convert(mats[i],model+"-"+i);
            renderer.sharedMaterials=mats;
        }
        return body;
    }
    static Vector3 Deck(Transform body, Vector3 position)
    {
        // Find the actual generated deck rather than assuming a common freeboard across models.
        float highest=float.NegativeInfinity;
        foreach(var filter in body.GetComponentsInChildren<MeshFilter>())
        {
            if(!AssetDatabase.GetAssetPath(filter.sharedMesh).EndsWith(".glb",StringComparison.OrdinalIgnoreCase))continue;
            var collider=filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh=filter.sharedMesh;
            Physics.SyncTransforms();
            var ray=new Ray(body.TransformPoint(new Vector3(position.x,50,position.z)),Vector3.down);
            if(collider.Raycast(ray,out var hit,100))highest=Mathf.Max(highest,body.InverseTransformPoint(hit.point).y);
            UnityEngine.Object.DestroyImmediate(collider);
        }
        if(!float.IsNegativeInfinity(highest))position.y=highest-.05f;
        return position;
    }
    static void Crane(Transform p,Vector3 pos,float height=8)
    {
        pos=Deck(p,pos); pos.y=Mathf.Min(pos.y,1.2f);
        Beam(p,pos,pos+Vector3.up*height,.18f,iron);
        var tip=pos+new Vector3(-5,height-1,1);
        Beam(p,pos+Vector3.up*height,tip,.16f,iron);
        Beam(p,pos+Vector3.up*3,tip,.07f,brass);
        Beam(p,tip,tip-Vector3.up*4,.035f,iron);
        Ring(p,tip-Vector3.up*4,.25f,brass,8);
    }
    static void Antenna(Transform p,Vector3 pos,float height)
    {
        pos=Deck(p,pos); pos.y=Mathf.Min(pos.y,1.2f);
        Beam(p,pos,pos+Vector3.up*height,.09f,iron);
        for(int i=0;i<4;i++)
        {
            float y=height-i*1.3f,w=2+i*.55f;
            Beam(p,pos+new Vector3(-w,y,0),pos+new Vector3(w,y,0),.045f,brass);
        }
        Beam(p,pos+Vector3.up*height,pos+new Vector3(3,0,4),.022f,iron);
        Beam(p,pos+Vector3.up*height,pos+new Vector3(-3,0,4),.022f,iron);
    }
    static void Net(Transform p,Vector3 pos)
    {
        for(int i=0;i<12;i++)Beam(p,pos+new Vector3(0,0,i*.32f),pos+new Vector3(.8f,-3,i*.32f+.4f),.025f,timber);
        for(int j=0;j<9;j++)Beam(p,pos+new Vector3(j*.1f,-j*.375f,0),pos+new Vector3(j*.1f,-j*.375f,4),.025f,timber);
    }
    static void Debris(Transform parent,int count,float radius)
    {
        for(int i=0;i<count;i++)
        {
            float a=i*2.39996f;float d=3+Mathf.Sqrt(i+1)*radius/Mathf.Sqrt(count);
            var p=Float(parent,"Drift timber "+i,new Vector3(Mathf.Cos(a)*d,0,Mathf.Sin(a)*d),.3f,1);
            p.localRotation=Quaternion.Euler(0,i*71,0);
            Box(p,"Salt-soaked plank",Vector3.zero,new Vector3(.2f,.18f,1.4f+(i%4)*.7f),timber);
        }
    }
    static void BuildContent(int index,Transform root)
    {
        Transform boat;
        switch(index)
        {
            case 0: // Large supply tender, crane, barrels, two departure channels.
                boat=Hull(root,"DerelictBarge",46);
                Crane(boat,new Vector3(4,3,-6),11);Crane(boat,new Vector3(-4,3,4),9);
                for(int i=0;i<6;i++)Shape(boat,"Stores barrel",PrimitiveType.Cylinder,new Vector3(-3+(i%3)*1.3f,3.5f,-i/3f*2),new Vector3(.9f,.7f,.9f),brass);
                for(int i=0;i<3;i++){Buoy(root,new Vector3(14,0,-15+i*10));Buoy(root,new Vector3(24,0,-15+i*10));}
                Light(boat,new Vector3(0,7,-9),false,100);break;
            case 1:
                boat=Hull(root,"DerelictBarge",28);Debris(root,6,16);Light(boat,new Vector3(2,4,-6),false,15);break;
            case 2:
                boat=Hull(root,"SurveyVessel_LOD0",12);Crane(boat,new Vector3(-1.4f,1,-2),3.5f);
                for(int i=0;i<3;i++)Shape(boat,"Rubber fender",PrimitiveType.Sphere,new Vector3(-1.9f,.2f,-2+i*2),new Vector3(.65f,1,.4f),iron);
                break;
            case 3:
                boat=Hull(root,"LostLifeboat",7);Box(boat,"Waxed chest",new Vector3(0,.45f,0),new Vector3(.8f,.5f,1.1f),canvas);Debris(root,4,6);break;
            case 4:
                boat=Hull(root,"DerelictBarge",25);Antenna(boat,new Vector3(0,4,4),11);Antenna(boat,new Vector3(0,4,-6),8);
                Light(boat,new Vector3(2,4,-5),false,45);break;
            case 5:
                boat=Float(root,"Instrument pontoon",Vector3.zero,4,6);
                foreach(float x in new[]{-3f,3f}){var drum=Shape(boat,"Pontoon",PrimitiveType.Cylinder,new Vector3(x,-.6f,0),new Vector3(1.5f,6,1.5f),iron);drum.transform.localRotation=Quaternion.Euler(90,0,0);}
                Box(boat,"Planked platform",new Vector3(0,.5f,0),new Vector3(8,.6f,13),timber);Rail(boat,3.7f,5.8f,.8f);
                foreach(float x in new[]{-2.8f,2.8f})Beam(boat,new Vector3(x,1,3),new Vector3(x*.4f,8,3),.18f,iron);
                Beam(boat,new Vector3(-1.1f,8,3),new Vector3(1.1f,8,3),.16f,iron);
                Beam(boat,new Vector3(0,8,3),new Vector3(0,-2,3),.04f,brass);
                Box(boat,"Instrument desk",new Vector3(0,1.5f,-2),new Vector3(3,1.3f,1.5f),brass);
                Box(boat,"Glass chart cover",new Vector3(0,2.2f,-2),new Vector3(2.7f,.08f,1.3f),glass);
                Light(boat,new Vector3(2,3,-2));Buoy(root,new Vector3(9,0,5));break;
            case 6:
                boat=Hull(root,"SurveyVessel_LOD0",18);
                Beam(boat,new Vector3(0,1,2),new Vector3(-6,3,6),.17f,timber);
                Net(boat,new Vector3(-2.5f,1,-3));Debris(root,8,13);break;
            case 7:
                boat=Hull(root,"DerelictBarge",30);
                Box(boat,"Sealed cargo hold",new Vector3(0,3.2f,3),new Vector3(6,2.5f,11),iron);
                for(int i=0;i<6;i++)Beam(boat,new Vector3(-3,4.5f,-2+i*2),new Vector3(3,4.5f,-2+i*2),.075f,brass);
                for(int i=0;i<4;i++)Box(boat,"Bolted planks",new Vector3(0,3+i*.4f,8.6f),new Vector3(5,.27f,.2f),timber);
                break;
            case 8:
                boat=Hull(root,"SurveyVessel_LOD0",15);
                Beam(boat,new Vector3(0,2,3),new Vector3(0,10,3),.12f,iron);
                for(int i=0;i<3;i++)Light(boat,new Vector3(0,7+i*1.2f,3),i==1,60);
                root.GetComponent<VoyageSceneView>().signalPeriod=4.8f;break;
            case 9:
                boat=Hull(root,"SurveyVessel_LOD0",21);Light(boat,new Vector3(-2.4f,3,-4),false,45);
                Buoy(root,new Vector3(-10,0,-9),true,.7f);Buoy(root,new Vector3(12,0,12),false,.7f);break;
            case 10:
                boat=Hull(root,"DerelictBarge",27);Crane(boat,new Vector3(2,3,3),10);
                Box(boat,"Workshop roof",new Vector3(-1,5,-1),new Vector3(5,.25f,5),canvas);
                foreach(float x in new[]{-3.3f,1.3f})foreach(float z in new[]{-3f,1f})Beam(boat,new Vector3(x,2,z),new Vector3(x,5,z),.08f,iron);
                Box(boat,"Tool cabinet",new Vector3(-1,3,-1),new Vector3(2,1.4f,1),brass);Light(boat,new Vector3(-1,4.5f,-1),false,75);break;
            case 11:
                Debris(root,26,19);Buoy(root,new Vector3(-13,0,10),false,.55f);Buoy(root,new Vector3(13,0,-7),true,.55f);
                // Three spars trace diverging currents, a readable surface-only encounter.
                for(int i=0;i<3;i++){var f=Float(root,"Current marker",new Vector3(-6+i*6,0,3+i*2),.4f,2);f.localRotation=Quaternion.Euler(0,25+i*35,0);Beam(f,new Vector3(0,0,-2),new Vector3(0,0,2),.13f,timber);}
                break;
            case 12:
                Buoy(root,Vector3.zero,true,1.7f);Debris(root,5,7);break;
            case 13:
                boat=Hull(root,"DerelictBarge",38);
                Shape(boat,"Lightship tower",PrimitiveType.Cylinder,new Vector3(0,6.5f,2),new Vector3(3.6f,3.5f,3.6f),iron);
                Shape(boat,"Lantern gallery",PrimitiveType.Cylinder,new Vector3(0,10,2),new Vector3(6,.2f,6),brass);
                Ring(boat,new Vector3(0,11,2),2.8f,iron);
                for(int i=0;i<10;i++){float a=i*Mathf.PI/5;Beam(boat,new Vector3(Mathf.Cos(a)*2.8f,10,2+Mathf.Sin(a)*2.8f),new Vector3(Mathf.Cos(a)*2.8f,11,2+Mathf.Sin(a)*2.8f),.055f,iron);}
                Shape(boat,"Extinguished lantern",PrimitiveType.Cylinder,new Vector3(0,11.8f,2),new Vector3(3.5f,1.1f,3.5f),glass);
                Shape(boat,"Lantern roof",PrimitiveType.Sphere,new Vector3(0,13.2f,2),new Vector3(4.5f,1,4.5f),iron);
                Light(boat,new Vector3(-4,2,-4),false,20);break;
        }
    }
    static void Bake(Transform root,string key)
    {
        // Collapse each independently floating body by material; avoid hundreds of tiny rigging draw calls.
        int bodyIndex=0;
        foreach(var body in root.GetComponentsInChildren<VoyageSceneFloat>())
        {
            var filters=body.GetComponentsInChildren<MeshFilter>().Where(f => !AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith(".glb", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach(var group in filters.GroupBy(f=>f.GetComponent<MeshRenderer>().sharedMaterial))
            {
                var mesh=new Mesh {name=key+"-"+bodyIndex+"-"+group.Key.name,indexFormat=IndexFormat.UInt32};
                mesh.CombineMeshes(group.Select(f=>new CombineInstance {mesh=f.sharedMesh,transform=body.transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray());
                string path=Art+"/Meshes/"+mesh.name+".asset";
                var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}else AssetDatabase.CreateAsset(mesh,path);
                EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
                var merged=Child(group.Key.name,body.transform,Vector3.zero);merged.AddComponent<MeshFilter>().sharedMesh=mesh;
                merged.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
            foreach(var f in filters) UnityEngine.Object.DestroyImmediate(f.gameObject);
            bodyIndex++;
        }
    }
    public static object Build()
    {
        Guard();Folder(Art);Folder(Art+"/Meshes");shader=Shader.Find("UnsoundedSea/Voyage Landmark");if(!shader)throw new Exception("Landmark shader missing");
        iron=Mat("Salt iron",new Color(.26f,.34f,.32f),.3f);timber=Mat("Wet timber",new Color(.44f,.31f,.19f));
        brass=Mat("Oxidized brass",new Color(.5f,.39f,.2f),.5f);canvas=Mat("Waxed canvas",new Color(.34f,.39f,.31f));
        glass=Mat("Dark glass",new Color(.035f,.075f,.085f),.1f);
        lamp=Mat("Amber lamp",new Color(.9f,.6f,.22f),0,new Color(2.5f,1.05f,.25f));redLamp=Mat("Red lamp",new Color(.7f,.1f,.04f),0,new Color(2,.13f,.04f));
        string[] names={"补给母船","漂流货船","无旗接驳艇","失联救生艇","旧无线电船","漂浮测量站","断桅渔船","封舱驳船","无人信号艇","停摆引航船","漂浮修械船","潮流观测点","远海浮标","无声灯船"};
        string[] keys={"SupplyTender","DriftingCargo","UnflaggedLaunch","LostLifeboat","RadioShip","SurveyPlatform","BrokenMastFishingBoat","SealedBarge","SignalBoat","StoppedPilot","WorkshopShip","CurrentObservation","OffshoreBuoy","SilentLightship"};
        var catalog=AssetDatabase.LoadAssetAtPath<VoyageSceneCatalog>(Art+"/VoyageScenes.asset");
        if(!catalog){catalog=ScriptableObject.CreateInstance<VoyageSceneCatalog>();AssetDatabase.CreateAsset(catalog,Art+"/VoyageScenes.asset");}
        var entries=new List<VoyageSceneCatalog.Entry>();
        for(int i=0;i<names.Length;i++)
        {
            var root=new GameObject(keys[i]);root.AddComponent<VoyageSceneView>();
            try
            {
                BuildContent(i,root.transform);Bake(root.transform,keys[i]);
                var prefab=PrefabUtility.SaveAsPrefabAsset(root,Art+"/"+keys[i]+".prefab");
                Vector3 offset=i==0?new Vector3(-39,0,30):i==13?new Vector3(-40,0,30):i==3||i==12?new Vector3(-13,0,10):i==11?new Vector3(-18,0,17):new Vector3(-25,0,17);
                entries.Add(new VoyageSceneCatalog.Entry{location=names[i],prefab=prefab.GetComponent<VoyageSceneView>(),offset=offset,heading=i==3?30:20});
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        catalog.entries=entries.ToArray();EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
        var scene=EditorSceneManager.OpenScene(Root+"Scenes/Voyage.unity");
        var sailing=UnityEngine.Object.FindAnyObjectByType<VoyageSailingPresentation>();
        var presentation=sailing.GetComponent<VoyageDestinationPresentation>()??sailing.gameObject.AddComponent<VoyageDestinationPresentation>();
        presentation.catalog=catalog;var so=new SerializedObject(sailing);so.FindProperty("destinations").objectReferenceValue=presentation;so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new{count=entries.Count,prefabs=entries.Select(x=>x.location).ToArray()};
    }
}
