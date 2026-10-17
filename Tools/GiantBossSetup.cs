using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;
using UnsoundedSea.Core;

public static class GiantBossSetup
{
    const string Root="Assets/UnsoundedSea";
    static readonly string[] Names={"TideBonesChoirmaster","InvertedSurveyor","UnlitFlagBearer","StarlessWatcher"};
    static readonly string[] M05GuardNames={"StarlessGuardAnchor","StarlessGuardLamp","StarlessGuardBell","StarlessGuardSail","StarlessGuardEye"};
    static readonly string[] M05GuardIds={"b05-guard-anchor","b05-guard-lamp","b05-guard-bell","b05-guard-sail","b05-guard-eye"};
    public static string Import(string directory="Logs/GiantBosses")
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before import");
        foreach(var n in Names)
            if(!File.Exists(Path.Combine(directory,n+"_BANG_LOD0.glb")))throw new FileNotFoundException("Missing rig output",n);
        foreach(var n in Names)
        {
            var path=Root+"/Art/Models/"+n+"_BANG_LOD0.glb";
            File.Copy(Path.Combine(directory,n+"_BANG_LOD0.glb"),path,true);
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        }
        return "Imported four anatomical BANG skins; original meta GUIDs retained.";
    }
    public static string Build()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before authoring");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene");
        RequireM05Assets();
        var mistPath=Root+"/Art/Bosses/GiantMist.mat";
        var mist=AssetDatabase.LoadAssetAtPath<Material>(mistPath);
        if(mist==null){mist=new Material(AssetDatabase.LoadAssetAtPath<Material>(Root+"/Art/SeaBoss/ImpactSpray.mat"));AssetDatabase.CreateAsset(mist,mistPath);}
        mist.SetFloat("_Emission",.45f);EditorUtility.SetDirty(mist);AssetDatabase.SaveAssets();
        for(int i=0;i<Names.Length;i++)Actor(i);
        for(int i=0;i<Names.Length-1;i++)Stage(i);
        ConfigureM05Stage();
        return "Updated four boss actors and their existing stages; M05 uses its dedicated frontal six-slot composition.";
    }
    public static string BuildM05()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before authoring");
        for(int i=0;i<UnityEngine.SceneManagement.SceneManager.sceneCount;i++)
            if(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene");
        RequireM05Assets();
        Actor(3);
        ConfigureM05Stage();
        return "Configured only M05: frontal Watcher, vessel, and five fixed sea guard slots.";
    }
    static void RequireM05Assets()
    {
        if(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/StarlessWatcher.prefab")==null)
            throw new FileNotFoundException("Missing M05 boss prefab",Root+"/Art/Prefabs/StarlessWatcher.prefab");
        foreach(var name in M05GuardNames)
            if(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/"+name+".prefab")==null)
                throw new FileNotFoundException("Missing M05 guard prefab; import all five guards before configuring the stage.",Root+"/Art/Prefabs/"+name+".prefab");
    }
    static void ConfigureM05Stage()
    {
        RequireM05Assets();
        var path=Root+"/Art/Bosses/M05Stage.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var stage=root.GetComponent<BattleStage>();
            foreach(var anchor in stage.actorAnchors)if(anchor!=null)UnityEngine.Object.DestroyImmediate(anchor.gameObject);
            var positions=new[]{new Vector3(0,-68,300),new Vector3(-129.9f,-8,225),new Vector3(-210,-8,75),new Vector3(0,-8,235),new Vector3(210,-8,75),new Vector3(129.9f,-8,225)};
            stage.actorAnchors=new Transform[positions.Length];
            for(var i=0;i<positions.Length;i++)
            {
                var go=new GameObject(i==0?"Boss actor — frontal sea":"Sea guard — "+M05GuardIds[i-1]);
                go.transform.SetParent(stage.transform,false);go.transform.localPosition=positions[i];
                go.transform.localRotation=Quaternion.Euler(0,180,0);
                go.transform.localScale=Vector3.one*(i==0?10.5f:i==3?2.625f:3.15f);
                var anchor=go.AddComponent<BattleAnchor>();anchor.environment=BattleEnvironment.Sea;
                anchor.showSelectionRing=i>0;anchor.labelHeight=i==0?.62f:.9f;
                anchor.labelOffset=i==0?new Vector3(0,0,4):new Vector3(0,.8f,0);
                anchor.heave=i==0?.12f:.3f;anchor.waterRadius=i==0?126:8;
                anchor.emergeDepth=i==0?4:9;anchor.emergeDuration=i==0?4.2f:2.4f;
                if(i>0)go.AddComponent<BossTargetAnchor>().targetId=M05GuardIds[i-1];
                stage.actorAnchors[i]=go.transform;
            }
            stage.vessel.localPosition=new Vector3(0,stage.vessel.localPosition.y,45);
            stage.vessel.localScale=Vector3.one*5.8f;
            stage.vessel.localRotation=Quaternion.identity;
            stage.battleCamera.transform.localPosition=new Vector3(0,120,-200);
            stage.battleCamera.transform.localRotation=Quaternion.LookRotation(new Vector3(0,80,170)-stage.battleCamera.transform.localPosition,Vector3.up);
            stage.battleCamera.fieldOfView=65;
            stage.vesselFollowsWater=true;stage.placeholderCombatMotion=false;stage.cameraShake=.035f;
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        for(var i=0;i<M05GuardIds.Length;i++)
        {
            var enemy=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(Root+"/Content/Enemies/"+M05GuardIds[i]+".asset");
            if(enemy==null)throw new InvalidOperationException("Missing M05 guard definition: "+M05GuardIds[i]);
            enemy.presentationPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Prefabs/"+M05GuardNames[i]+".prefab");
            EditorUtility.SetDirty(enemy);AssetDatabase.SaveAssetIfDirty(enemy);
        }
        var scenario=AssetDatabase.LoadAssetAtPath<BattleScenario>(Root+"/Content/Scenarios/M05.asset");
        scenario.actorOverrides=Array.Empty<GameObject>();
        scenario.chapter="M05 / 无星王座";
        scenario.objectiveHeading="召唤并击退五名守卫";
        scenario.objectiveHint="守卫先行动；本体理智冲击逐回合增强，上限10。";
        scenario.introduction="召唤并击退五名守卫。";
        scenario.taskLabel="";
        scenario.victoryText="五名守卫沉没，无星海域的封锁解除。";
        scenario.encounter.displayName="无星王座";
        EditorUtility.SetDirty(scenario.encounter);AssetDatabase.SaveAssetIfDirty(scenario.encounter);
        EditorUtility.SetDirty(scenario);AssetDatabase.SaveAssetIfDirty(scenario);
    }
    static void Actor(int index)
    {
        var n=Names[index];var path=Root+"/Art/Prefabs/"+n+".prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var old=root.transform.GetChild(0);
            UnityEngine.Object.DestroyImmediate(old.gameObject);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/Models/"+n+"_BANG_LOD0.glb");
            if(source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length==0)throw new Exception("Missing skin "+n);
            var body=(GameObject)PrefabUtility.InstantiatePrefab(source,root.transform);body.transform.localPosition=Vector3.zero;body.transform.localRotation=Quaternion.identity;body.transform.localScale=Vector3.one;
            // Importing an armature changes glTF node file IDs, so old nested overrides
            // cannot be trusted. Recompute the documented normalized model dimensions.
            var bounds=BattleActorView.MeasureLocalBounds(body.transform);
            float extent=index==0?Mathf.Max(bounds.size.x,bounds.size.z):bounds.size.y;
            float factor=new[]{18f,12f,3.8f,30f}[index]/extent;
            body.transform.localScale=Vector3.one*factor;
            body.transform.localPosition=new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z)*factor;
            var motion=body.AddComponent<GiantBossMotion>();motion.kind=(BossEncounterKind)(index+1);
            motion.duration=new[]{2.8f,3.1f,2.7f,3.6f}[index];motion.contactTime=motion.duration*.57f;
            motion.breathPeriod=new[]{7.5f,9f,8.4f,12f}[index];motion.waveAmplitude=new[]{65f,75f,80f,0f}[index];
            motion.effectColor=new[]{new Color(.76f,.8f,.69f),new Color(.53f,.75f,.71f),new Color(.67f,.64f,.49f),new Color(.18f,.6f,1.4f)}[index];
            motion.mistMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Art/Bosses/GiantMist.mat");
            var bones=body.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("GB_")).ToArray();
            motion.joints=bones.Select(t=>new GiantJointMotion{bone=t}).ToArray();
            GiantJointMotion J(string name)=>motion.joints.Single(j=>j.bone.name=="GB_"+name);
            void Pose(string name,Vector3 idle,Vector3 attack,Vector3 drag,float lag=0)
            {var j=J(name);j.idleEuler=idle;j.attackEuler=attack;j.followEuler=drag;j.lag=lag;j.phase=motion.joints.ToList().IndexOf(j)*.63f;}
            if(index==0)
            {
                Pose("Body",new Vector3(.45f,0,.25f),new Vector3(3.6f,0,0),new Vector3(1.3f,0,0));
                Pose("Head",new Vector3(.7f,.2f,0),new Vector3(15,0,0),new Vector3(3,0,0),.04f);
                Pose("Gullet",new Vector3(.4f,0,0),new Vector3(2,0,0),new Vector3(2,0,0),.13f);
                J("Gullet").breathScale=new Vector3(.018f,.022f,.018f);
                Pose("Fin_L",new Vector3(0,.3f,1.6f),new Vector3(0,0,18),new Vector3(0,0,4),.12f);
                Pose("Fin_R",new Vector3(0,-.3f,-1.4f),new Vector3(0,0,-16),new Vector3(0,0,-4),.20f);
                Pose("Tail",new Vector3(.7f,1.3f,0),new Vector3(-3,2,0),new Vector3(-5,-1,0),.22f);
                Pose("TailTip",new Vector3(1.4f,1.8f,0),new Vector3(-2,2,0),new Vector3(-5,2,0),.35f);
                J("Body").attackShift=new Vector3(0,-.018f,0);J("Body").hitEuler=new Vector3(-1.1f,0,.25f);J("Head").hitEuler=new Vector3(-1.5f,.3f,0);
                motion.effectOrigin=J("Head").bone;
            }
            else if(index==1)
            {
                Pose("Body",new Vector3(.7f,0,.6f),new Vector3(5,0,-3),new Vector3(1,0,-1));
                Pose("Arch_L",new Vector3(.5f,0,-.8f),new Vector3(-2,0,-3.2f),new Vector3(1,0,1),.08f);
                Pose("Arch_R",new Vector3(.4f,0,.7f),new Vector3(-2,0,3.2f),new Vector3(1,0,-1),.15f);
                Pose("Pendulum",new Vector3(1.3f,0,1.2f),new Vector3(12,0,6),new Vector3(4,0,2),.16f);
                Pose("PendulumTip",new Vector3(1,0,.8f),new Vector3(3,0,2),new Vector3(3,0,1),.3f);
                Pose("Pendant_R",new Vector3(1,0,1.5f),new Vector3(8,0,-4),new Vector3(3,0,-1),.25f);
                Pose("Pendant_Back",new Vector3(1,0,1),new Vector3(5,0,2),new Vector3(2,0,1),.32f);
                J("Body").attackShift=new Vector3(0,-.035f,0);J("Body").hitEuler=new Vector3(-1.1f,0,.6f);J("Pendulum").hitEuler=new Vector3(-1.5f,0,.5f);
                motion.effectOrigin=J("PendulumTip").bone;
            }
            else if(index==2)
            {
                Pose("Body",new Vector3(.3f,.25f,.15f),new Vector3(2.8f,-2,-.6f),new Vector3(1,0,0));
                Pose("Head",new Vector3(.35f,.3f,0),new Vector3(2,-3,0),new Vector3(1,0,0),.08f);
                Pose("Pole",new Vector3(.3f,0,.3f),new Vector3(15,4,0),new Vector3(2,1,0),.08f);
                Pose("FlagTop",new Vector3(1.2f,1,0),new Vector3(-3,0,0),new Vector3(4,2,0),.15f);
                Pose("FlagMid",new Vector3(3,1.8f,0),new Vector3(-5,0,0),new Vector3(8,2,0),.26f);
                Pose("FlagLow",new Vector3(4,2.5f,0),new Vector3(-6,0,0),new Vector3(10,3,0),.40f);
                Pose("FreeArm",new Vector3(.6f,0,.3f),new Vector3(4,0,-1),new Vector3(2,0,0),.15f);
                J("Body").hitEuler=new Vector3(-1,0,-.35f);J("Pole").hitEuler=new Vector3(-1.1f,0,.25f);
                var socket=new GameObject("Pole sea release").transform;socket.SetParent(body.transform,false);socket.localPosition=new Vector3(.13f,.05f,.17f);socket.SetParent(J("Pole").bone,true);motion.effectOrigin=socket;
            }
            else
            {
                Pose("Body",new Vector3(.2f,0,.14f),new Vector3(.7f,0,0),new Vector3(.3f,0,0));
                Pose("Head",new Vector3(.3f,.25f,0),new Vector3(2,0,0),new Vector3(.5f,0,0),.2f);
                foreach(var side in new[]{"L","R"})
                {
                    float sign=side=="L"?1:-1;
                    Pose("Arch_"+side,new Vector3(0,0,sign*.2f),new Vector3(0,0,sign*1.3f),Vector3.zero,.1f);
                    Pose("Curtain_"+side,new Vector3(.35f,0,sign*.6f),new Vector3(-2,0,sign*8),new Vector3(1,0,sign*2),.05f);
                    Pose("CurtainMid_"+side,new Vector3(.6f,0,sign*.9f),new Vector3(-1,0,sign*3),new Vector3(2,0,sign*3),.25f);
                    Pose("CurtainLow_"+side,new Vector3(1,0,sign*1.2f),new Vector3(-1,0,sign*2),new Vector3(2,0,sign*4),.4f);
                    J("Curtain_"+side).hitEuler=new Vector3(.8f,0,sign*.8f);
                }
                J("Body").hitEuler=new Vector3(-.7f,0,.2f);
                var socket=new GameObject("Chest resonance release").transform;socket.SetParent(body.transform,false);socket.localPosition=new Vector3(0,1.02f,.22f);socket.SetParent(J("Body").bone,true);motion.effectOrigin=socket;
            }
            J("Body").idleShift=Vector3.up*.003f;J("Body").hitShift=Vector3.down*.008f;
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
    static void Stage(int index)
    {
        var path=Root+"/Art/Bosses/M0"+(index+2)+"Stage.prefab";var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var stage=root.GetComponent<BattleStage>();var template=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Art/SeaBoss/DrownedBellStage.prefab").GetComponent<BattleStage>();
            stage.water.impactSprayMaterial=template.water.impactSprayMaterial;
            stage.vesselFollowsWater=true;stage.placeholderCombatMotion=false;stage.cameraShake=0;
            stage.battleCamera.transform.localPosition=new[]{new Vector3(65,19,-27),new Vector3(63,22,-30),new Vector3(61,18,-28),new Vector3(60,21,-27)}[index];
            var target=new[]{new Vector3(-8,12,96),new Vector3(-8,10,101),new Vector3(-10,10,90),new Vector3(-15,10,105)}[index];
            stage.battleCamera.transform.localRotation=Quaternion.LookRotation(target-stage.battleCamera.transform.localPosition,Vector3.up);stage.battleCamera.fieldOfView=new[]{60f,63f,63f,65f}[index];
            stage.actorAnchors[0].localPosition=new[]{new Vector3(-45,-55,280),new Vector3(-25,-25,255),new Vector3(-32,-38,265),new Vector3(-30,-68,300)}[index];
            stage.actorAnchors[0].localRotation=Quaternion.Euler(0,new[]{205f,140f,145f,150f}[index],0);
            stage.actorAnchors[0].localScale=Vector3.one*new[]{20f,17f,55f,10.5f}[index];
            stage.fogColor=new[]{new Color(.055f,.085f,.085f),new Color(.045f,.075f,.08f),new Color(.075f,.08f,.075f),new Color(.045f,.055f,.08f)}[index];
            // Keep the authored deck props, cameras, ocean and target anchors; no encounter rebuild.
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
    }
}
