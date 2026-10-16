using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Explicitly rebuilds this encounter only; saved edits in its authored assets will be replaced.
public static class SeaBossSetup
{
    const string Root="Assets/UnsoundedSea";
    const string Art=Root+"/Art/SeaBoss";
    static void Folder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        var i=path.LastIndexOf('/');Folder(path.Substring(0,i));AssetDatabase.CreateFolder(path.Substring(0,i),path.Substring(i+1));
    }
    static T Asset<T>(string path,Action<T> init) where T:ScriptableObject
    {
        Folder(Path.GetDirectoryName(path).Replace('\\','/'));
        var a=AssetDatabase.LoadAssetAtPath<T>(path);if(a==null){a=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(a,path);}
        init(a);EditorUtility.SetDirty(a);return a;
    }
    static GameObject Obj(string name,Transform parent,Vector3 pos)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=pos;return go;}

    static Material SeaSky()
    {
        var path=Art+"/StormSky.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(Shader.Find("UnsoundedSea/Storm Sky"));AssetDatabase.CreateAsset(material,path);}
        return material;
    }

    public static object ImportModel()
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Models/BellMother_LOD0.glb");
        if(source==null)throw new InvalidOperationException("Import BellMother_LOD0.glb first.");
        Folder(Art);
        var root=new GameObject("BellMother");
        try
        {
            var body=(GameObject)PrefabUtility.InstantiatePrefab(source);body.transform.SetParent(root.transform,false);
            var bounds=BattleActorView.MeasureLocalBounds(root.transform);float scale=32/bounds.size.y;
            body.transform.localScale*=scale;body.transform.localPosition=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z)*scale;
            var renderer=body.GetComponentInChildren<Renderer>();
            var materialPath=Art+"/BellMother.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if(material==null){material=new Material(renderer.sharedMaterial);AssetDatabase.CreateAsset(material,materialPath);}
            material.SetFloat("metallicFactor",0.45f);material.SetFloat("roughnessFactor",1.3f);
            material.SetTexture("emissiveTexture",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/BellMotherEmission.png"));
            material.SetColor("emissiveFactor",new Color(0.65f,0.9f,0.85f));EditorUtility.SetDirty(material);
            renderer.sharedMaterial=material;
            PrefabUtility.SaveAsPrefabAsset(root,Root+"/Art/Prefabs/BellMother.prefab");AssetDatabase.SaveAssetIfDirty(material);
            return new {sourceBounds=bounds.ToString(),scale,height=32,normalizedBounds=BattleActorView.MeasureLocalBounds(root.transform).ToString()};
        }
        finally {UnityEngine.Object.DestroyImmediate(root);}
    }

    public static string Build()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before rebuilding.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save scene edits first.");
        var boss=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/BellMother.prefab");
        if(boss==null)throw new InvalidOperationException("Import the Boss prefab first.");
        Folder(Art);
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var template=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/OceanBattle/FogSeaStage.prefab");
        var go=UnityEngine.Object.Instantiate(template);go.name="Drowned Bell — sea encounter stage";
        var stage=go.GetComponent<BattleStage>();
        foreach(var old in stage.actorAnchors)UnityEngine.Object.DestroyImmediate(old.gameObject);
        var anchor=Obj("Sea actor 0 — independent of vessel",go.transform,new Vector3(0,-7,60));
        anchor.transform.localRotation=Quaternion.Euler(0,180,0);
        var layout=anchor.AddComponent<BattleAnchor>();layout.environment=BattleEnvironment.Sea;layout.labelHeight=0.85f;layout.labelOffset=new Vector3(-14,0.7f,0);layout.showSelectionRing=false;layout.heave=0.22f;layout.waterRadius=14;
        stage.actorAnchors=new[]{anchor.transform};stage.fogColor=new Color(0.13f,0.22f,0.24f);stage.fogDensity=0.009f;
        stage.vesselHeave=0.12f;stage.vesselPitch=0.55f;stage.vesselRoll=0.8f;stage.cameraShake=0.06f;
        var camera=stage.battleCamera;camera.transform.position=new Vector3(0,3.2f,0);camera.transform.LookAt(new Vector3(0,4,60));camera.fieldOfView=52;camera.farClipPlane=240;
        foreach(var light in go.GetComponentsInChildren<Light>())
        {
            if(light.type==LightType.Directional){light.intensity=1.8f;light.color=new Color(0.55f,0.73f,0.76f);light.transform.rotation=Quaternion.Euler(30,-35,0);}
            if(light.name=="Cold deck fill")light.intensity=9;
        }
        var fill=Obj("Sea creature soft fill",go.transform,new Vector3(-12,16,37)).AddComponent<Light>();fill.type=LightType.Point;fill.range=65;fill.intensity=80;fill.color=new Color(0.38f,0.69f,0.67f);fill.shadows=LightShadows.None;
        var rim=Obj("Sea creature rim",go.transform,new Vector3(12,28,72)).AddComponent<Light>();rim.type=LightType.Point;rim.range=55;rim.intensity=90;rim.color=new Color(0.52f,0.69f,0.84f);rim.shadows=LightShadows.None;
        stage.sanityCue=AssetDatabase.LoadAssetAtPath<AudioClip>(Art+"/DeepBell.wav");stage.feedback.volume=0.6f;
        // No objective chime on each survival tick: the creature's low bell remains the focal sound.
        stage.sealCue=null;
        var stagePrefab=PrefabUtility.SaveAsPrefabAsset(go,Art+"/DrownedBellStage.prefab").GetComponent<BattleStage>();UnityEngine.Object.DestroyImmediate(go);
        var enemy=Asset<EnemyDefinition>(Root+"/Content/Enemies/bell-mother.asset",e=>
        {
            e.id="bell-mother";e.displayName="沉钟之母";e.health=240;e.presentationPrefab=boss;
            e.candidateDescription="巨浪：21 生命伤害 / 沉钟：11 理智冲击";
            e.intents=new[]{
                new IntentData{id="undertow",displayName="倾覆巨浪",clue="海面向它的躯体凹陷。下一道浪正越过船首。",channel=DamageChannel.Health,amount=21,isPublic=true,visibleAction=true,soundSource="船首海面",interruptible=false},
                new IntentData{id="drowned-bell",displayName="深水钟鸣",clue="它的裂隙正在张开。钟声从船壳内部传来。",channel=DamageChannel.Sanity,amount=11,isPublic=true,visibleAction=true,soundSource="船壳下的深水",interruptible=true}};
        });
        CardDefinition Card(string id)=>AssetDatabase.LoadAssetAtPath<CardDefinition>(Root+"/Content/Cards/"+id+".asset");
        var encounter=Asset<EncounterDefinition>(Root+"/Content/Encounters/drowned-bell.asset",e=>
        {
            e.id="drowned-bell";e.displayName="沉钟海域";e.objective=ObjectiveKind.Survive;e.objectiveTarget=5;e.playerHealth=60;e.playerSanity=30;e.actionsPerTurn=3;e.drawPerTurn=5;
            e.enemies=new[]{enemy};e.deck=new[]{Card("cover"),Card("steady"),Card("intuition"),Card("cover"),Card("steady"),Card("intuition"),Card("cover"),Card("steady"),Card("intuition"),Card("cover"),Card("steady"),Card("tear"),Card("notes"),Card("light"),Card("revolver")};
            e.equippedTools=new[]{AssetDatabase.LoadAssetAtPath<ToolDefinition>(Root+"/Content/Tools/listener.asset")};
        });
        var rules=Asset<SurvivalRuleModule>(Root+"/Content/Scenarios/BellSurvival.asset",r=>r.objectiveDescription="撑过五轮袭击，让船驶离它的阴影");
        var scenario=Asset<BattleScenario>(Root+"/Content/Scenarios/DrownedBell.asset",s=>
        {
            s.encounter=encounter;s.stagePrefab=stagePrefab;s.rules=rules;s.actorOverrides=Array.Empty<GameObject>();s.chapter="02 / 沉钟海域";s.location="鸦号调查船 · 外海禁航区 · 03:41";
            s.enemyHeading="02   /   海面上的巨大身影";s.enemyStatus="巨型显现体";s.objectiveHeading="活着驶离它的阴影。";s.taskLabel="";
            s.objectiveHint="每轮袭击后航程推进一段。巨浪用格挡，钟鸣用理智防护或打断；火力不能代替撤离。";
            s.introduction="那不是一座岛。撑过五轮袭击，保持船员生命与理智。";
            s.victoryText="第五道浪落在了船尾。巨大的轮廓终于被浓雾吞没。\n你没有击败它，只是驶出了它的目光。";
            s.defeatText="船舷的灯接连熄灭。\n最后一声钟鸣，来自你自己的胸腔。";
        });scenario.ValidatePresentation();
        var templateScene=EditorSceneManager.OpenScene(Root+"/Scenes/OceanBattle.unity",OpenSceneMode.Additive);
        var host=UnityEngine.Object.Instantiate(templateScene.GetRootGameObjects().First(x=>x.GetComponent<BattleDirector>()!=null));host.name="Sea Boss Composition Root";SceneManager.MoveGameObjectToScene(host,scene);host.GetComponent<BattleDirector>().scenario=scenario;
        RenderSettings.skybox=SeaSky();
        EditorSceneManager.CloseScene(templateScene,true);SceneManager.SetActiveScene(scene);
        RenderSettings.skybox=SeaSky();RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(0.18f,0.27f,0.3f);RenderSettings.ambientEquatorColor=new Color(0.09f,0.16f,0.17f);RenderSettings.ambientGroundColor=new Color(0.025f,0.045f,0.05f);
        string scenePath=Root+"/Scenes/SeaBossBattle.unity";EditorSceneManager.SaveScene(scene,scenePath);
        var menuScene=EditorSceneManager.OpenScene(Root+"/Scenes/MainMenu.unity");var menu=UnityEngine.Object.FindAnyObjectByType<MainMenu>();var serialized=new SerializedObject(menu);serialized.FindProperty("seaBossScene").stringValue="SeaBossBattle";serialized.ApplyModifiedPropertiesWithoutUndo();EditorSceneManager.SaveScene(menuScene);
        if(!EditorBuildSettings.scenes.Any(x=>x.path==scenePath))EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(scenePath,true)}).ToArray();
        AssetDatabase.SaveAssets();EditorSceneManager.OpenScene(scenePath);return "Sea Boss encounter built. MainMenu offers both encounters.";
    }
}
