using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;
using UnsoundedSea.Core;
public static class GiantBossQA
{
    static BattleDirector Director()
    {if(!EditorApplication.isPlaying)throw new Exception("Play mode required");return UnityEngine.Object.FindAnyObjectByType<BattleDirector>();}
    static async Task WaitReady()
    {
        var d=Director();
        for(var i=0;i<80&&d.Busy;i++)await Task.Delay(100);
        if(d.Busy)throw new Exception("Battle presentation did not unlock.");
    }
    public static string Select(int index)
    {
        var d=Director();d.scenario=AssetDatabase.LoadAssetAtPath<BattleScenario>("Assets/UnsoundedSea/Content/Scenarios/M0"+(index+2)+".asset");d.Restart();
        return "Selected M0"+(index+2);
    }
    public static string Snapshot(int index)
    {
        var d=Director();var actor=d.Stage.Actors[0];var motion=actor.GiantMotion;
        Capture(d.Stage.battleCamera,"Logs/GiantBosses/M0"+(index+2)+"-idle.png");
        var ship=d.Stage.battleCamera.WorldToViewportPoint(d.Stage.vessel.position);var origin=d.Stage.battleCamera.WorldToViewportPoint(motion.Origin);
        return JsonUtility.ToJson(new SnapshotInfo{frame=Time.frameCount,bones=motion.joints.Length,ship=ship,origin=origin});
    }
    [Serializable]class SnapshotInfo{public int frame,bones;public Vector3 ship,origin;}
    public static async Task<string> Record(int index)
    {
        Select(index);await WaitReady();var d=Director();
        var maxParticles=0;float low=float.PositiveInfinity,high=float.NegativeInfinity;
        var initial=Time.frameCount;var m=d.Stage.Actors[0].GiantMotion;var prefix="Logs/GiantBosses/M0"+(index+2);Directory.CreateDirectory(prefix);
        if(index==3)
        {
            var card=d.View.Hand.Single(c=>c.Definition.Id=="E05-01");var prepare=d.Submit(BattleCommand.Play(card.InstanceId,card.Targets.First(t=>t.Enabled).Id));if(!prepare.Success)throw new Exception(prepare.Error);
            await WaitReady();
        }
        // Two real enemy turns include a physical strike and a summon/ritual when applicable.
        for(int turn=0;turn<2;turn++)
        {
            var result=d.Submit(BattleCommand.EndTurn());if(!result.Success)throw new Exception(result.Error);
            for(int i=0;i<42;i++)
            {maxParticles=Mathf.Max(maxParticles,m.GetComponent<GiantBossEffects>().ParticleCount);low=Mathf.Min(low,d.Stage.vessel.position.y);high=Mathf.Max(high,d.Stage.vessel.position.y);Capture(d.Stage.battleCamera,prefix+"/turn"+turn+"-"+i.ToString("D3")+".png");await Task.Delay(90);}
            if(d.Busy)throw new Exception("Busy did not release");
        }
        if(maxParticles==0)throw new Exception("No visible particle population");
        if(Time.frameCount<=initial+20)throw new Exception("Game did not advance");
        var age=m.Age;var waterTime=d.Stage.water.SeaTime;d.SetPaused(true);await Task.Delay(180);
        if(m.Age!=age||d.Stage.water.SeaTime!=waterTime)throw new Exception("Pause drift");d.SetPaused(false);
        return "M0"+(index+2)+": "+(Time.frameCount-initial)+" frames, releases="+m.ReleaseCount+", water impacts="+d.Stage.water.ImpactCount+", max particles="+maxParticles+", ship vertical range="+(high-low).ToString("F2")+", pause passed, turn="+d.View.Turn;
    }
    public static async Task<string> Hit(int index)
    {
        Select(index);await WaitReady();var d=Director();var m=d.Stage.Actors[0].GiantMotion;
        var prefix="Logs/GiantBosses/M0"+(index+2)+"/hit";Directory.CreateDirectory(prefix);
        // Diagnostic only for untargetable bodies; gameplay instead uses objective/anchor disruption.
        m.PlayHit(index<2?1:.8f);
        for(int i=0;i<30;i++){Capture(d.Stage.battleCamera,prefix+"/"+i.ToString("D3")+".png");await Task.Delay(65);}
        return "Hit overlay recorded";
    }
    public static async Task<string> EffectsProbe()
    {
        Select(3);await WaitReady();var d=Director();var m=d.Stage.Actors[0].GiantMotion;
        m.PlayAttack(true);await Task.Delay(2500);d.SetPaused(true);await Task.Delay(80);
        var ps=m.GetComponentInChildren<ParticleSystem>();var renderer=ps.GetComponent<ParticleSystemRenderer>();
        Capture(d.Stage.battleCamera,"Logs/GiantBosses/effects-on.png");renderer.enabled=false;
        Capture(d.Stage.battleCamera,"Logs/GiantBosses/effects-off.png");renderer.enabled=true;
        var points=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(points);
        var info="particles="+ps.particleCount+", bounds="+renderer.bounds+", origin="+m.Origin+", first="+(points.Length>0?points[0].position.ToString():"none")+", scale="+ps.transform.lossyScale;
        d.SetPaused(false);return info;
    }
    public static async Task<string> M05EntranceProbe()
    {
        Select(3);var d=Director();var boss=d.Stage.Actors[0];
        if(!boss.IsEmerging||!d.Busy)throw new Exception("M05 entrance did not lock input from its first visible frame.");
        if(boss.HighestPointY>=d.Stage.water.SurfaceHeight(boss.transform.position))throw new Exception("M05 boss was visible above water on its first frame.");
        var impacts=d.Stage.water.ImpactCount;
        var brokeSurface=false;boss.BrokeSurface+=_=>brokeSurface=true;
        for(var i=0;i<60&&d.Busy;i++)await Task.Delay(100);
        if(boss.IsEmerging||d.Busy)throw new Exception("M05 entrance lock did not release.");
        if(!brokeSurface||d.Stage.water.ImpactCount<=impacts)throw new Exception("M05 surface impact missing: broke="+brokeSurface+", impacts="+impacts+"->"+d.Stage.water.ImpactCount+", top="+boss.HighestPointY);
        await Task.Delay(100);
        var summon=d.View.Hand.Single(c=>c.Definition.Id=="E05-01");
        var root=d.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement;
        var button=UnityEngine.UIElements.UQueryExtensions.Q<UnityEngine.UIElements.Button>(root,"card-"+summon.InstanceId);
        if(button==null||!button.enabledInHierarchy)throw new Exception("M05 summon card stayed disabled after entrance.");
        return "M05 entrance locked input and emitted a surface impact.";
    }
    public static async Task<string> M05ImpactProbe()
    {
        Select(3);var d=Director();
        for(var i=0;i<80&&d.Busy;i++)await Task.Delay(100);
        var root=d.GetComponent<UnityEngine.UIElements.UIDocument>().rootVisualElement;
        var sanity=d.View.Sanity;
        var result=d.Submit(BattleCommand.EndTurn());if(!result.Success)throw new Exception(result.Error);
        await Task.Delay(300);
        var early=UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(root).ToList().Any(l=>l.text=="理智 -2");
        var delayed=d.Stage.DelaysHudUntilImpact;
        for(var i=0;i<80&&d.Busy;i++)await Task.Delay(100);
        if(d.Busy||d.Stage.DelaysHudUntilImpact)throw new Exception("M05 attack did not finish.");
        if(d.View.Sanity!=sanity-2)throw new Exception("M05 did not apply exactly two sanity.");
        if(early||!delayed)throw new Exception("Sanity feedback appeared before the vessel impact.");
        return "earlySanityPulse="+early+", delayedAt300ms="+delayed+", sanity="+sanity+"->"+d.View.Sanity;
    }
    public static async Task<string> M05AllGuards()
    {
        var saved=File.Exists(VoyageRuntime.SavePath)?File.ReadAllText(VoyageRuntime.SavePath):null;
        VoyageRuntime.StartDebugEncounter("m05","abyss",true);
        await Task.Delay(500);await WaitReady();var d=Director();
        for(var i=0;i<5;i++)
        {
            if(d.View.Actions==0){var end=d.Submit(BattleCommand.EndTurn());if(!end.Success)throw new Exception(end.Error);await WaitReady();}
            var card=d.View.Hand.Single(c=>c.Definition.Id=="E05-01");
            var target=card.Targets.First(t=>t.Enabled);
            var actor=d.Stage.Actors.Single(a=>a.EnemyId==target.Id);
            var crossed=false;actor.BrokeSurface+=_=>crossed=true;
            var result=d.Submit(BattleCommand.Play(card.InstanceId,target.Id));if(!result.Success)throw new Exception(result.Error);
            if(!actor.IsEmerging||actor.HighestPointY>=d.Stage.water.SurfaceHeight(actor.transform.position))throw new Exception("Guard did not start underwater: "+target.Id);
            await WaitReady();
            if(!crossed||actor.IsEmerging)throw new Exception("Guard did not finish breaking the surface: "+target.Id);
        }
        if(d.View.Enemies.Skip(1).Count(e=>e.Health>0)!=5||d.View.ObjectiveProgress!=0)throw new Exception("Five summoned guards must remain alive without victory progress.");
        var positions=d.Stage.Actors.Skip(1).Select(a=>d.Stage.battleCamera.WorldToViewportPoint(a.transform.position).x).OrderBy(x=>x).ToArray();
        if(positions[1]>=.45f||positions[3]<=.55f||Mathf.Abs(positions[2]-.5f)>.05f)throw new Exception("Guard slots are not left two, center one, right two.");
        Capture(d.Stage.battleCamera,"Logs/GiantBosses/M05-all-guards.png");
        if(saved!=(File.Exists(VoyageRuntime.SavePath)?File.ReadAllText(VoyageRuntime.SavePath):null))throw new Exception("Formal save changed.");
        return "Five real summons emerged; slots="+string.Join(",",positions.Select(x=>x.ToString("F3")))+"; formal save unchanged.";
    }
    public static void Capture(Camera camera,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));var rt=RenderTexture.GetTemporary(960,540,24);var old=camera.targetTexture;var active=RenderTexture.active;
        Texture2D image=null;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);if(image!=null)UnityEngine.Object.DestroyImmediate(image);}
    }
}
