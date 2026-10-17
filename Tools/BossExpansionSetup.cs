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

// Creates new encounter assets only. Existing authored stages, scenes, water and vessel assets are never modified.
public static class BossExpansionSetup
{
    const string Root = "Assets/UnsoundedSea";
    const string BossArt = Root + "/Art/Bosses";
    const string Models = Root + "/Art/Models";
    const string SourceStage = Root + "/Art/SeaBoss/DrownedBellStage.prefab";
    const string SourceScene = Root + "/Scenes/OceanBattle.unity";

    sealed class Spec
    {
        public readonly string Code, Model, Name, EncounterName, Chapter, Location, Objective, Hint;
        public readonly BossEncounterKind Kind;
        public readonly float Size, WorldScale, Yaw;
        public readonly Vector3 BossPosition;
        public readonly string[] EnemyIds, EnemyNames;
        public Spec(string code, string model, string name, string encounterName, BossEncounterKind kind, float size,
            Vector3 bossPosition, float worldScale, float yaw,
            string[] enemyIds, string[] enemyNames, string objective, string hint)
        {
            Code=code;Model=model;Name=name;EncounterName=encounterName;Kind=kind;Size=size;BossPosition=bossPosition;
            WorldScale=worldScale;Yaw=yaw;EnemyIds=enemyIds;EnemyNames=enemyNames;
            Chapter=code+" / "+encounterName;Location="鸦号调查船 · 禁航海域";Objective=objective;Hint=hint;
        }
    }

    static readonly Spec[] Specs =
    {
        new Spec("M02","TideBonesChoirmaster","骸潮领唱者","潮骨唱序",BossEncounterKind.Choirmaster,18,new Vector3(-45,-55,280),20,145,
            new[]{"b02-choirmaster","b02-bone-servant","b02-tide-messenger"},new[]{"发声囊","骨片侍从","潮囊信使"},"按序关闭潮、骨、息三匣，再击破发声囊。","唱序由公开约束唯一确定；召唤单位会攻击或治疗发声囊。"),
        new Spec("M03","InvertedSurveyor","倒悬司衡者","盐格司衡",BossEncounterKind.Surveyor,12,new Vector3(-25,-25,255),17,140,
            new[]{"b03-surveyor","b03-moth-1","b03-moth-2"},new[]{"倒悬司衡者","雾灯蛾群 I","雾灯蛾群 II"},"在盐格中开启 B4、C1、C4、D2 四枚封栓。","数字只表示相邻危险总数；本体不能用普通武装选中。"),
        new Spec("M04","UnlitFlagBearer","无灯执旗者","无灯旗舰",BossEncounterKind.FlagBearer,3.8f,new Vector3(-20,-20,230),45,145,
            new[]{"b04-flag-bearer","b04-flag-soldier"},new[]{"无灯执旗者","空甲执旗兵"},"校准继电器，依次释放三道闸锁，再击退执旗者。","继电器与当前闸锁条件始终公开；错误接线可以直接纠正。"),
        new Spec("M05","StarlessWatcher","无星王座守望者","无星王座",BossEncounterKind.Watcher,30,new Vector3(0,-68,300),10.5f,180,
            new[]{"b05-watcher","b05-guard-anchor","b05-guard-lamp","b05-guard-bell","b05-guard-sail","b05-guard-eye"},new[]{"无星守望者","锚守卫","灯守卫","钟守卫","帆守卫","眼守卫"},"用遭遇牌召唤五名守卫，击退全部守卫。","本体每回合造成2点理智冲击；守卫召唤当回合不攻击，可分批应对。")
    };

    public static string ImportModels()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before importing boss models.");
        foreach (var spec in Specs)
            if (!File.Exists(Models + "/" + spec.Model + "_BANG_LOD0.glb"))
                throw new FileNotFoundException("Build the archived BANG model with Tools/BossBangBuild.py first.", spec.Model);
        Folder(BossArt); Folder(Root+"/Art/Prefabs");
        foreach (var spec in Specs)
        {
            AssetDatabase.ImportAsset(Models+"/"+spec.Model+"_BANG_LOD0.glb",ImportAssetOptions.ForceSynchronousImport);
            NormalizeModel(spec);
        }
        return "Imported BANG models; retained existing anatomical rigs and authored encounter stages.";
    }

    public static string Build()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before building boss encounters.");
        for(var i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save open scene edits first.");
        var template=AssetDatabase.LoadAssetAtPath<GameObject>(SourceStage);
        if(template==null)throw new InvalidOperationException("Build SeaBossSetup and UberWaterSetup first; DrownedBellStage is missing.");
        foreach(var spec in Specs)BuildEncounter(spec,template);
        GiantBossSetup.BuildM05();
        var settings = Resources.FindObjectsOfTypeAll<EditorBuildSettings>().Single();
        var copy = UnityEngine.Object.Instantiate(settings);
        try
        {
            copy.hideFlags = HideFlags.None;
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(
                new UnityEngine.Object[] { copy }, Path.GetFullPath("ProjectSettings/EditorBuildSettings.asset"), true);
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
        EditorSceneManager.OpenScene(Root+"/Scenes/BossM02.unity");
        return "Built BossM02–BossM05 scenarios, cloned stages and scenes.";
    }

    static void NormalizeModel(Spec spec)
    {
        // Preserve the anatomical rig and authored motion profile on subsequent imports.
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/"+spec.Model+".prefab");
        if (existing != null && existing.GetComponentInChildren<GiantBossMotion>(true) != null) return;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(Models+"/"+spec.Model+"_BANG_LOD0.glb");
        if(source==null)throw new InvalidOperationException(spec.Model+" is still importing.");
        if(source.GetComponentsInChildren<MeshFilter>(true).Length < 2)
            throw new InvalidOperationException(spec.Model+" is not a split BANG model.");
        var root=new GameObject(spec.Name);
        try
        {
            var body=(GameObject)PrefabUtility.InstantiatePrefab(source);body.transform.SetParent(root.transform,false);
            var bounds=BattleActorView.MeasureLocalBounds(root.transform);
            var extent=spec.Kind==BossEncounterKind.Choirmaster?Mathf.Max(bounds.size.x,bounds.size.z):bounds.size.y;
            if(extent<=0.001f)throw new InvalidOperationException(spec.Model+" has empty renderer bounds.");
            var scale=spec.Size/extent;body.transform.localScale=Vector3.one*scale;
            body.transform.localPosition=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z)*scale;
            PrefabUtility.SaveAsPrefabAsset(root,Root+"/Art/Prefabs/"+spec.Model+".prefab");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }

    static void BuildEncounter(Spec spec,GameObject template)
    {
        var bossPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/"+spec.Model+".prefab");
        if(bossPrefab==null)throw new InvalidOperationException("Run BossExpansionSetup.ImportModels first: "+spec.Model);
        var stageRoot=(GameObject)PrefabUtility.InstantiatePrefab(template);stageRoot.name=spec.EncounterName+" — cloned moving-vessel stage";
        var stage=stageRoot.GetComponent<BattleStage>();
        foreach(var old in stage.actorAnchors)UnityEngine.Object.DestroyImmediate(old.gameObject);
        stage.actorAnchors=CreateActorAnchors(spec,stage);
        stage.vesselFollowsWater=true;stage.placeholderCombatMotion=false;stage.cameraShake=0;
        BuildDeckSet(spec,stage.vessel);
        var stagePrefab=PrefabUtility.SaveAsPrefabAsset(stageRoot,BossArt+"/"+spec.Code+"Stage.prefab").GetComponent<BattleStage>();
        UnityEngine.Object.DestroyImmediate(stageRoot);

        var enemies=new EnemyDefinition[spec.EnemyIds.Length];
        for(var i=0;i<enemies.Length;i++)
        {
            var slot=i;var prefab=i==0?bossPrefab:SmallEnemyPrefab(i);
            enemies[i]=Asset<EnemyDefinition>(Root+"/Content/Enemies/"+spec.EnemyIds[i]+".asset",e=>
            {e.id=spec.EnemyIds[slot];e.displayName=spec.EnemyNames[slot];e.health=EnemyHealth(spec.Kind,slot);e.candidateDescription=EnemyIntent(spec.Kind,slot);e.presentationPrefab=prefab;e.intents=Intents(spec.Kind,slot);});
        }
        CardDefinition Card(string id)=>AssetDatabase.LoadAssetAtPath<CardDefinition>(Root+"/Content/Cards/"+id+".asset");
        var encounter=Asset<EncounterDefinition>(Root+"/Content/Encounters/"+spec.Code.ToLowerInvariant()+".asset",e=>
        {
            e.id=spec.Code.ToLowerInvariant();e.displayName=spec.EncounterName;e.objective=ObjectiveKind.Defeat;e.objectiveTarget=1;
            e.playerHealth=60;e.playerSanity=30;e.actionsPerTurn=3;e.drawPerTurn=5;e.enemies=enemies;
            e.deck=new[]{Card("revolver"),Card("revolver"),Card("revolver"),Card("cover"),Card("cover"),Card("steady"),Card("steady"),Card("intuition"),Card("notes"),Card("tear"),Card("light"),Card("contradiction")};
            e.equippedTools=Array.Empty<ToolDefinition>();
        });
        var rules=Asset<BossRuleModule>(Root+"/Content/Scenarios/"+spec.Code+"Rules.asset",r=>r.bossKind=spec.Kind);
        var scenario=Asset<BattleScenario>(Root+"/Content/Scenarios/"+spec.Code+".asset",s=>
        {
            s.encounter=encounter;s.stagePrefab=stagePrefab;s.rules=rules;s.actorOverrides=Array.Empty<GameObject>();s.chapter=spec.Chapter;s.location=spec.Location;
            s.enemyHeading=spec.Code+" / "+spec.Name;s.enemyStatus="特殊遭遇";s.objectiveHeading=spec.Objective;s.objectiveHint=spec.Hint;s.taskLabel="";
            s.introduction=spec.Objective;s.victoryText="工序完成。鸦号脱离了这片海域。";s.defeatText="甲板沉入无灯的海雾。";
        });
        scenario.ValidatePresentation();
        BuildScene(spec,scenario);
    }

    static Transform[] CreateActorAnchors(Spec spec,BattleStage stage)
    {
        var anchors=new Transform[spec.EnemyIds.Length];
        var boss=Obj("Boss actor — independent of vessel",stage.transform,spec.BossPosition);
        boss.transform.localRotation=Quaternion.Euler(0,spec.Yaw,0);boss.transform.localScale=Vector3.one*spec.WorldScale;
        var placement=boss.AddComponent<BattleAnchor>();placement.environment=BattleEnvironment.Sea;placement.showSelectionRing=false;
        placement.labelHeight=.62f;placement.labelOffset=new Vector3(spec.Size*.25f,0,spec.Size*.15f);
        placement.heave=1.2f/spec.WorldScale;placement.waterRadius=spec.Size*spec.WorldScale*.4f;anchors[0]=boss.transform;
        for(var i=1;i<anchors.Length;i++)
        {
            var x=(i-(anchors.Length-1)*.5f)*2.1f;var z=3+(i%2)*2.3f;
            var go=Obj("Deck actor "+spec.EnemyIds[i],stage.vessel,new Vector3(x,1.25f,z));
            var deck=go.AddComponent<BattleAnchor>();deck.environment=BattleEnvironment.Deck;deck.labelHeight=.8f;deck.waterRadius=0;anchors[i]=go.transform;
        }
        return anchors;
    }

    static void BuildDeckSet(Spec spec,Transform vessel)
    {
        var set=Obj(spec.Code+" deck encounter set",vessel,new Vector3(0,1.05f,0)).transform;
        if(spec.Kind==BossEncounterKind.Choirmaster)
        {
            TargetBox(set,"choir-a","TIDE",new Vector3(-2.5f,.55f,2.8f),new Vector3(1.3f,1.1f,.9f));
            TargetBox(set,"choir-b","BONE",new Vector3(0,.55f,3.3f),new Vector3(1.3f,1.1f,.9f));
            TargetBox(set,"choir-c","BREATH",new Vector3(2.5f,.55f,2.8f),new Vector3(1.3f,1.1f,.9f));
        }
        else if(spec.Kind==BossEncounterKind.Surveyor)
        {
            for(var row=0;row<4;row++)for(var col=0;col<4;col++)
            {var id=((char)('A'+row)).ToString()+(col+1);TargetBox(set,id,id,new Vector3((col-1.5f)*1.1f,.12f,(row-1.5f)*1.1f+2),new Vector3(1,.18f,1));}
        }
        else if(spec.Kind==BossEncounterKind.FlagBearer)
        {
            foreach(var relay in new[]{"R1","R2"})
            {
                var x=relay=="R1"?-2.4f:-.8f;TargetBox(set,relay+":off",relay+" OFF",new Vector3(x,.35f,1),new Vector3(1.1f,.7f,.8f));
                TargetBox(set,relay+":positive",relay+" +",new Vector3(x,.35f,2),new Vector3(1.1f,.7f,.8f));
                TargetBox(set,relay+":reverse",relay+" REV",new Vector3(x,.35f,3),new Vector3(1.1f,.7f,.8f));
            }
            TargetBox(set,"lock-I","I",new Vector3(1.1f,.45f,1),new Vector3(.8f,.9f,.8f));TargetBox(set,"lock-II","II",new Vector3(2.1f,.45f,2),new Vector3(.8f,.9f,.8f));TargetBox(set,"lock-III","III",new Vector3(3.1f,.45f,3),new Vector3(.8f,.9f,.8f));
        }
        else
        {
            TargetCylinder(set,"winch","WINCH",new Vector3(0,.55f,1.2f),new Vector3(1.2f,.55f,1.2f));
            var ids=new[]{"anchor","lamp","bell","sail","eye"};var labels=new[]{"ANCHOR","LAMP","BELL","SAIL","EYE"};
            for(var i=0;i<ids.Length;i++)TargetCylinder(set,"anchor-"+ids[i],labels[i],new Vector3((i-2)*1.55f,.35f,3.1f+Mathf.Abs(i-2)*.35f),new Vector3(.65f,.35f,.65f));
        }
    }

    static void TargetBox(Transform parent,string id,string label,Vector3 position,Vector3 scale)
    {var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=id;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;FinishTarget(go,id,label);}
    static void TargetCylinder(Transform parent,string id,string label,Vector3 position,Vector3 scale)
    {var go=GameObject.CreatePrimitive(PrimitiveType.Cylinder);go.name=id;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;FinishTarget(go,id,label);}
    static void FinishTarget(GameObject go,string id,string label)
    {
        go.AddComponent<BossTargetAnchor>().targetId=id;
        var brass = PropMaterial("TaskBrass", new Color(.34f,.25f,.11f), .65f);
        var body = PropMaterial("TaskHousing", new Color(.075f,.12f,.12f), .35f);
        go.GetComponent<Renderer>().sharedMaterial = body;
        for (var x = -1; x <= 1; x += 2)
            for (var z = -1; z <= 1; z += 2)
            {
                var stud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                stud.name = "Brass rivet"; stud.transform.SetParent(go.transform, false);
                stud.transform.localPosition = new Vector3(x * .4f, .52f, z * .4f);
                stud.transform.localScale = Vector3.one * .1f; stud.GetComponent<Renderer>().sharedMaterial = brass;
                UnityEngine.Object.DestroyImmediate(stud.GetComponent<Collider>());
            }
        if (id.StartsWith("choir-", StringComparison.Ordinal))
            for (var i = 0; i < 3; i++)
            {
                var pipe = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pipe.name = "Resonance pipe"; pipe.transform.SetParent(go.transform, false);
                pipe.transform.localPosition = new Vector3((i-1)*.24f,.9f+i*.12f,0);
                pipe.transform.localScale = new Vector3(.14f,.4f+i*.12f,.14f);
                pipe.GetComponent<Renderer>().sharedMaterial = brass;
                UnityEngine.Object.DestroyImmediate(pipe.GetComponent<Collider>());
            }
    }

    static Material PropMaterial(string name, Color color, float metallic)
    {
        var path = BossArt + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        material.SetColor("_BaseColor", color); material.SetFloat("_Metallic", metallic); material.SetFloat("_Smoothness", .35f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void BuildScene(Spec spec,BattleScenario scenario)
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var templateScene=EditorSceneManager.OpenScene(SourceScene,OpenSceneMode.Additive);
        var source=templateScene.GetRootGameObjects().First(x=>x.GetComponent<BattleDirector>()!=null);
        var host=UnityEngine.Object.Instantiate(source);host.name=spec.Code+" Battle Composition Root";SceneManager.MoveGameObjectToScene(host,scene);host.GetComponent<BattleDirector>().scenario=scenario;
        EditorSceneManager.CloseScene(templateScene,true);SceneManager.SetActiveScene(scene);
        RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Art/SeaBoss/StormSky.mat");RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.18f,.27f,.3f);RenderSettings.ambientEquatorColor=new Color(.09f,.16f,.17f);RenderSettings.ambientGroundColor=new Color(.025f,.045f,.05f);
        var path=Root+"/Scenes/Boss"+spec.Code+".unity";EditorSceneManager.SaveScene(scene,path);
        if(!EditorBuildSettings.scenes.Any(x=>x.path==path))EditorBuildSettings.scenes=EditorBuildSettings.scenes.Concat(new[]{new EditorBuildSettingsScene(path,true)}).ToArray();
    }

    static GameObject SmallEnemyPrefab(int index)
    {
        var id=index%2==0?"Echo":"Prayer";
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/"+id+".prefab");
        if(prefab==null)throw new InvalidOperationException("Missing existing small enemy prefab: "+id);
        return prefab;
    }
    static int EnemyHealth(BossEncounterKind kind,int slot)
    {return UnsoundedSea.Editor.CombatBalanceSetup.BossHealth(kind,slot);}
    static string EnemyIntent(BossEncounterKind kind,int slot)
    {return UnsoundedSea.Editor.CombatBalanceSetup.BossIntent(kind,slot);}
    static IntentData[] Intents(BossEncounterKind kind,int slot)
    {
        return UnsoundedSea.Editor.CombatBalanceSetup.BossIntents(kind,slot);
    }
    static GameObject Obj(string name,Transform parent,Vector3 position)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;return go;}
    static T Asset<T>(string path,Action<T> initialize) where T:ScriptableObject
    {Folder(Path.GetDirectoryName(path).Replace('\\','/'));var asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset==null){asset=ScriptableObject.CreateInstance<T>();AssetDatabase.CreateAsset(asset,path);}initialize(asset);EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);return asset;}
    static void Folder(string path)
    {if(AssetDatabase.IsValidFolder(path))return;var split=path.LastIndexOf('/');Folder(path.Substring(0,split));AssetDatabase.CreateFolder(path.Substring(0,split),path.Substring(split+1));}
}
