using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnsoundedSea.Runtime;
using UnsoundedSea.Core;

// Isolated presentation review plus a real lethal command / victory wait check.
public static class SeaBossDefeatQA
{
    const string Dir = "Logs/BossDefeat";
    static BattleDirector Director => UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static string Start()
    {
        Check(Application.isPlaying, "Enter Play first");
        Directory.CreateDirectory(Dir); File.WriteAllText(Dir + "/result.txt", "running");
        VoyageRuntime.BeginValidation(Path.GetFullPath("Logs/UiExperience/BossDefeat"));
        Application.runInBackground = true; _ = Run(); return "Recording isolated defeat sequences";
    }
    static async Task Open(string id)
    {
        for (int i=0; i<600 && GameSceneLoader.IsLoading; i++) await Task.Delay(25);
        Check(!GameSceneLoader.IsLoading, "Previous scene transition did not finish");
        VoyageRuntime.StartDebugEncounter(id, "fog", true);
        await Task.Delay(300);
        for (int i=0; i<600 && (Director == null || Director.Busy || GameSceneLoader.IsLoading); i++) await Task.Delay(25);
        Check(Director != null && !Director.Busy, "Battle did not become ready: " + id);
    }
    static void Capture(string name)
    {
        var camera = Director.Stage.battleCamera; var old = camera.targetTexture; var active = RenderTexture.active;
        var rt = RenderTexture.GetTemporary(1280,720,24); var image = new Texture2D(1280,720,TextureFormat.RGB24,false);
        try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
            image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply(); File.WriteAllBytes(Dir+"/"+name+".png",image.EncodeToPNG()); }
        finally {camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);}
    }
    public static string StartIntegration()
    {
        Check(Application.isPlaying, "Enter Play first");
        VoyageRuntime.BeginValidation(Path.GetFullPath("Logs/UiExperience/BossDefeat"));
        _ = Run(false); return "Validating real commands";
    }
    static async Task Run(bool visuals = true)
    {
        try
        {
            foreach (var id in visuals ? new[] { "storm-gauntlet", "m02", "m03", "m04", "m05" } : Array.Empty<string>())
            {
                await Open(id); var d=Director; var actor=d.Stage.Actors[0]; var water=d.Stage.water;
                int waves=water.ImpactCount; Capture(id+"-0");
                if(id=="m05") actor.Retire(true); else actor.Feedback(false,false,false);
                int sample=1; float[] at={.08f,.2f,.33f,.5f,.75f,.98f};
                for(int frame=0;frame<1200&&sample<=at.Length;frame++)
                {
                    await Task.Delay(20);
                    if(actor.RetirementProgress>=at[sample-1]) Capture(id+"-"+sample++);
                }
                Check(sample>at.Length,"Retirement did not advance: "+id);
                await Task.Delay(500);
                Check(!d.Busy,"Retirement never released lock: "+id);
                Check(water.ImpactCount-waves==(id=="m04"?0:1),"Unexpected water contact count: "+id);
                File.AppendAllText(Dir+"/result.txt","\n"+id+": pose sequence + single contact + lock release passed");
            }
            await Open("storm-gauntlet");
            var director=Director;
            // Detach the synthetic encounter from the voyage contract before changing its rules.
            VoyageRuntime.BeginValidation(Path.GetFullPath("Logs/UiExperience/BossDefeat"));
            // Runtime-only clone allows a deterministic lethal card without changing authored balance.
            var scenario=UnityEngine.Object.Instantiate(director.scenario);
            scenario.encounter=UnityEngine.Object.Instantiate(scenario.encounter);
            var enemy=UnityEngine.Object.Instantiate(scenario.encounter.enemies[0]); enemy.health=1;
            scenario.encounter.enemies=new[]{enemy}; scenario.encounter.objective=ObjectiveKind.Defeat;
            scenario.encounter.playerHealth=1000;scenario.rules=null; director.scenario=scenario;
            director.seed=Enumerable.Range(0,100).Select(i=>(uint)i).First(seed=>scenario.CreateSession(seed,true).View.Hand.Any(c=>c.Definition.Id=="revolver"));
            director.Restart();
            await Task.Delay(200);
            var card=director.View.Hand.FirstOrDefault(c=>c.Definition.Id=="revolver");
            Check(card!=null,"Seeded starter hand must contain damage");
            var lethal = director.Submit(BattleCommand.Play(card.InstanceId,card.Targets.First(t=>t.Enabled).Id));
            Check(lethal.Success,"Lethal card rejected: "+lethal.Error);
            Check(director.View.Phase==BattlePhase.Victory&&director.Busy,"Victory must wait for the corpse");
            await Task.Delay(900);Check(director!=null&&director.Busy,"Victory left before the body lost support");
            director.SetPaused(true);float progress=director.Stage.Actors[0].RetirementProgress;
            await Task.Delay(300);Check(Mathf.Abs(progress-director.Stage.Actors[0].RetirementProgress)<.001f,"Pause advanced death");director.SetPaused(false);
            for(int i=0;i<1000&&director.Busy;i++)await Task.Delay(25);
            Check(!director.Busy&&director.View.Phase==BattlePhase.Victory,"Completed death did not release victory");
            UnityEngine.Object.Destroy(scenario.encounter);UnityEngine.Object.Destroy(scenario);UnityEngine.Object.Destroy(enemy);
            await Open("storm-gauntlet"); director=Director;
            for(int turn=0;turn<5;turn++)
            {
                Check(director.Submit(BattleCommand.EndTurn()).Success,"Survival turn rejected");
                if(turn<4)for(int i=0;i<600&&director.Busy;i++)await Task.Delay(25);
            }
            Check(director.View.Phase==BattlePhase.Victory&&director.View.Enemies[0].Health>0,"Survival must not kill the boss");
            var bell=director.Stage.Actors[0].GetComponentInChildren<BellMotherRigidMotion>();
            for(int i=0;i<600&&bell.ActiveAction!="Withdraw";i++)await Task.Delay(25);
            Check(bell.ActiveAction=="Withdraw"&&director.Busy,"Final slap must precede the living withdrawal");
            for(int i=0;i<800&&director!=null;i++)await Task.Delay(25);
            Check(director==null,"Survival withdrawal never returned to voyage");
            for(int i=0;i<600&&GameSceneLoader.IsLoading;i++)await Task.Delay(25);
            VoyageRuntime.EndValidation();
            File.AppendAllText(Dir+"/result.txt","\nPASS: real lethal command, victory lock, pause and return; five survival turns followed by living withdrawal; isolated save only");
        }
        catch(Exception e){File.AppendAllText(Dir+"/result.txt","\nFAIL: "+e);Debug.LogException(e);}
    }
}
