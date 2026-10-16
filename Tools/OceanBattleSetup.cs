using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Run via unity command run_script. Rebuilds only this encounter's authored assets.
public static class OceanBattleSetup
{
    const string Root = "Assets/UnsoundedSea";
    const string Art = Root + "/Art/OceanBattle";
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var i = path.LastIndexOf('/'); Folder(path.Substring(0,i)); AssetDatabase.CreateFolder(path.Substring(0,i), path.Substring(i+1));
    }
    static T Asset<T>(string path, Action<T> init) where T : ScriptableObject
    {
        Folder(Path.GetDirectoryName(path).Replace('\\','/'));
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null) { a = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(a,path); }
        init(a); EditorUtility.SetDirty(a); return a;
    }
    static Material Mat(string name, string shader, Color color, float metallic=0, float smooth=0.5f)
    {
        var path = Art + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m,path); }
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);else if(m.HasProperty("_Color"))m.SetColor("_Color",color); if(m.HasProperty("_Metallic")) m.SetFloat("_Metallic",metallic); if(m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness",smooth);
        EditorUtility.SetDirty(m); return m;
    }
    static GameObject Obj(string name, Transform parent, Vector3 pos)
    { var go = new GameObject(name); go.transform.SetParent(parent,false); go.transform.localPosition=pos; return go; }
    static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material material)
    { var go = GameObject.CreatePrimitive(type); go.name=name; go.transform.SetParent(parent,false); go.transform.localPosition=pos; go.transform.localScale=scale; go.GetComponent<Renderer>().sharedMaterial=material; UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); return go; }
    static Light Light(string name, Transform parent, Vector3 pos, Color color, float intensity, LightType type)
    { var light=Obj(name,parent,pos).AddComponent<Light>(); light.type=type; light.color=color; light.intensity=intensity; light.shadows=LightShadows.Soft; light.range=12; light.shadowBias=0.03f; return light; }
    static Mesh OceanMesh()
    {
        const int n=180; var v=new Vector3[(n+1)*(n+1)]; var t=new int[n*n*6]; int k=0;
        for(int z=0;z<=n;z++) for(int x=0;x<=n;x++) v[z*(n+1)+x]=new Vector3((x-n/2f)*1.4f,0,(z-n/2f)*1.4f);
        for(int z=0;z<n;z++) for(int x=0;x<n;x++) {int a=z*(n+1)+x; t[k++]=a;t[k++]=a+n+1;t[k++]=a+1;t[k++]=a+1;t[k++]=a+n+1;t[k++]=a+n+2;}
        var path=Art+"/OceanMesh.asset"; var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(mesh==null) {mesh=new Mesh();AssetDatabase.CreateAsset(mesh,path);} mesh.Clear();mesh.vertices=v;mesh.triangles=t;mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);return mesh;
    }
    public static string Polish()
    {
        var root=PrefabUtility.LoadPrefabContents(Art+"/FogSeaStage.prefab");
        foreach(var light in root.GetComponentsInChildren<Light>())if(light.name=="Cold deck fill")light.intensity=20;
        PrefabUtility.SaveAsPrefabAsset(root,Art+"/FogSeaStage.prefab");PrefabUtility.UnloadPrefabContents(root);
        AssetDatabase.DeleteAsset(Art+"/WetOak.mat");AssetDatabase.DeleteAsset(Art+"/Basalt.mat");AssetDatabase.SaveAssets();return "Saved final fill light and removed unused materials.";
    }
    public static string Build()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        for(int i=0;i<SceneManager.sceneCount;i++) if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first.");
        Folder(Art); Folder(Root+"/Content/Scenarios");
        var boat = AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/SurveyVessel.prefab");
        if(boat==null) throw new InvalidOperationException("Import and normalize SurveyVessel.prefab before building.");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var stageRoot=new GameObject("Fog Sea — interchangeable stage"); var stage=stageRoot.AddComponent<BattleStage>(); stage.fogColor=new Color(0.16f,0.23f,0.25f);stage.fogDensity=0.032f;
        var brass=Mat("TarnishedBrass","Universal Render Pipeline/Lit",new Color(0.34f,0.26f,0.12f),0.65f,0.65f);
        var glow=Mat("LanternGlass","Universal Render Pipeline/Lit",new Color(1,0.47f,0.11f)); glow.EnableKeyword("_EMISSION");glow.SetColor("_EmissionColor",new Color(1,0.45f,0.08f)*3);EditorUtility.SetDirty(glow);
        var sigil=Mat("SelectionBrass","Universal Render Pipeline/Unlit",new Color(0.72f,0.49f,0.18f)); stage.selectionMaterial=sigil;
        var sea=Mat("AbyssWater","UnsoundedSea/Abyss Ocean",Color.white);
        var water=Obj("Open ocean",stageRoot.transform,Vector3.zero);water.AddComponent<MeshFilter>().sharedMesh=OceanMesh();stage.ocean=water.AddComponent<MeshRenderer>();stage.ocean.sharedMaterial=sea;
        stage.ambience=Obj("Sea and hull soundscape",stageRoot.transform,Vector3.zero).AddComponent<AudioSource>();
        stage.ambience.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Art+"/FogSea.wav");stage.ambience.loop=true;stage.ambience.volume=0.55f;stage.ambience.playOnAwake=true;
        stage.feedback=Obj("Action cues",stageRoot.transform,Vector3.zero).AddComponent<AudioSource>();stage.feedback.playOnAwake=false;stage.feedback.volume=0.7f;
        stage.sealCue=AssetDatabase.LoadAssetAtPath<AudioClip>(Art+"/BrassSeal.wav");stage.impactCue=AssetDatabase.LoadAssetAtPath<AudioClip>(Art+"/HullImpact.wav");
        var vessel=Obj("Vessel rig",stageRoot.transform,Vector3.zero).transform;stage.vessel=vessel;
        var hull=(GameObject)PrefabUtility.InstantiatePrefab(boat);hull.transform.SetParent(vessel,false);
        stage.actorAnchors=new Transform[2];
        stage.actorAnchors[0]=Obj("Enemy slot 0",vessel,new Vector3(-1.15f,1.45f,1.3f)).transform;
        stage.actorAnchors[1]=Obj("Enemy slot 1",vessel,new Vector3(1.15f,1.45f,2.3f)).transform;
        foreach(var anchor in stage.actorAnchors) anchor.localRotation=Quaternion.Euler(0,140,0);
        // Deck props are modular and stay with the vessel during the subtle roll.
        for(int side=-1;side<=1;side+=2)
        {
            for(int z=-2;z<=5;z+=7)
            {
                Primitive("Lantern post",PrimitiveType.Cylinder,vessel,new Vector3(side*2.65f,2.1f,z),new Vector3(0.045f,0.65f,0.045f),brass);
                Primitive("Lantern glass",PrimitiveType.Cylinder,vessel,new Vector3(side*2.65f,2.85f,z),new Vector3(0.13f,0.16f,0.13f),glow);
                Primitive("Lantern cap",PrimitiveType.Cylinder,vessel,new Vector3(side*2.65f,3.03f,z),new Vector3(0.21f,0.035f,0.21f),brass);
                Light("Amber deck lamp",vessel,new Vector3(side*2.5f,2.9f,z),new Color(1,0.53f,0.23f),2.2f,LightType.Point);
            }
        }
        var camera=Obj("Battle Camera",stageRoot.transform,new Vector3(5.5f,5.4f,-3.5f)).AddComponent<Camera>();camera.tag="MainCamera";camera.transform.LookAt(new Vector3(0,2,2.8f));camera.fieldOfView=52;camera.nearClipPlane=0.08f;camera.farClipPlane=220;camera.allowHDR=true;camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;camera.gameObject.AddComponent<AudioListener>();stage.battleCamera=camera;
        var moon=Light("Moon through fog",stageRoot.transform,Vector3.zero,new Color(0.42f,0.67f,0.78f),2.3f,LightType.Directional);moon.transform.rotation=Quaternion.Euler(27,-32,0);
        var fill=Light("Cold deck fill",stageRoot.transform,new Vector3(0,4,-2),new Color(0.46f,0.68f,0.72f),20.0f,LightType.Point);fill.range=16;fill.shadows=LightShadows.None;
        var profile=Asset<VolumeProfile>(Art+"/OceanGrade.asset",p=>{});
        var tone=profile.components.OfType<Tonemapping>().FirstOrDefault()??profile.Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
        var bloom=profile.components.OfType<Bloom>().FirstOrDefault()??profile.Add<Bloom>();bloom.intensity.Override(0.35f);bloom.threshold.Override(1.1f);
        var vignette=profile.components.OfType<Vignette>().FirstOrDefault()??profile.Add<Vignette>();vignette.intensity.Override(0.26f);vignette.smoothness.Override(0.65f);
        foreach(var c in profile.components) if(!AssetDatabase.Contains(c))AssetDatabase.AddObjectToAsset(c,profile);
        EditorUtility.SetDirty(profile);
        var volume=Obj("Atmosphere grade",stageRoot.transform,Vector3.zero).AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=profile;
        var sky=Mat("NightSky","UnsoundedSea/Fog Sky",Color.white);RenderSettings.skybox=sky;
        RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(0.14f,0.23f,0.27f);RenderSettings.ambientEquatorColor=new Color(0.075f,0.12f,0.13f);RenderSettings.ambientGroundColor=new Color(0.018f,0.035f,0.04f);RenderSettings.reflectionIntensity=0.4f;
        var stageAsset=PrefabUtility.SaveAsPrefabAsset(stageRoot,Art+"/FogSeaStage.prefab").GetComponent<BattleStage>();UnityEngine.Object.DestroyImmediate(stageRoot);
        CardDefinition Card(string id)=>AssetDatabase.LoadAssetAtPath<CardDefinition>(Root+"/Content/Cards/"+id+".asset");
        EnemyDefinition Enemy(string id)=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"/Content/Enemies/"+id+".asset");
        var prayer=Enemy("prayer");var echo=Enemy("echo");
        // Scenario overrides leave the older rule examples independent from this art direction.
        var encounter=Asset<EncounterDefinition>(Root+"/Content/Encounters/fog-visitors.asset",e=>
        {e.id="fog-visitors";e.displayName="雾海来客";e.objective=ObjectiveKind.Seal;e.objectiveTarget=4;e.playerHealth=60;e.playerSanity=30;e.actionsPerTurn=3;e.drawPerTurn=5;
        e.enemies=new[]{prayer,echo};e.deck=new[]{Card("revolver"),Card("revolver"),Card("revolver"),Card("cover"),Card("cover"),Card("steady"),Card("steady"),Card("intuition"),Card("notes"),Card("tear"),Card("light"),Card("contradiction")};
        e.equippedTools=new[]{AssetDatabase.LoadAssetAtPath<ToolDefinition>(Root+"/Content/Tools/lantern.asset"),AssetDatabase.LoadAssetAtPath<ToolDefinition>(Root+"/Content/Tools/listener.asset")};});
        var rules=Asset<SealingRuleModule>(Root+"/Content/Scenarios/FogSealing.asset",r=>r.objectiveDescription="完成四段封线，驶离回声海域");
        var scenario=Asset<BattleScenario>(Root+"/Content/Scenarios/FogVisitors.asset",s=>
        {s.encounter=encounter;s.stagePrefab=stageAsset;s.rules=rules;s.chapter="01 / 雾海来客";s.location="鸦号调查船 · 北纬 47° · 02:17";
        s.actorOverrides=new[]{AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/Prayer.prefab"),AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/Echo.prefab")};
        s.introduction="雾中多了两位乘客。每回合刻下一段封线，同时防备它们的行动。";s.taskLabel="刻下封线";
        s.victoryText="第四段封线闭合。船头终于划开浓雾。\n身后的脚步消失了，海图上却多出一条不属于你的笔迹。";
        s.defeatText="呼号淹没在潮声里。\n没有人能确定，船上究竟曾有多少乘客。";});
        scenario.ValidatePresentation();
        var host=new GameObject("Battle Composition Root");var ui=host.AddComponent<UIDocument>();
        ui.panelSettings=Asset<PanelSettings>(Root+"/Settings/OceanBattlePanel.asset",p=>{p.scaleMode=PanelScaleMode.ScaleWithScreenSize;p.referenceResolution=new Vector2Int(1600,900);p.match=0.5f;});
        var director=host.AddComponent<BattleDirector>();director.scenario=scenario;
        var hud=host.AddComponent<OceanBattleHud>();hud.font=AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/Fonts/default_zh.asset");hud.styleSheet=AssetDatabase.LoadAssetAtPath<StyleSheet>(Root+"/Runtime/Battle/OceanBattleHud.uss");hud.menuStyle=AssetDatabase.LoadAssetAtPath<StyleSheet>(Root+"/Runtime/Presentation/GameMenu.uss");
        var scenePath=Root+"/Scenes/OceanBattle.unity";EditorSceneManager.SaveScene(scene,scenePath);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Root+"/Scenes/MainMenu.unity",true),new EditorBuildSettingsScene(scenePath,true)}.Concat(EditorBuildSettings.scenes.Where(x=>x.path!=scenePath&&x.path!=Root+"/Scenes/MainMenu.unity")).ToArray();
        AssetDatabase.SaveAssets();EditorSceneManager.OpenScene(scenePath);return "Built OceanBattle with a replaceable stage, two actors and four-seal objective.";
    }
}
