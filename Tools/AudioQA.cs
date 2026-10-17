using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Live acceptance against real commands. Never writes the player's save; run only after Editor coordination.
public static class AudioQA
{
    const string Dir="Logs/AudioQA";
    static readonly List<string> Passed=new List<string>();
    static BattleDirector Director=>UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Passed.Add(message);}
    static async Task Until(Func<bool> predicate,string failure,int milliseconds=45000)
    {
        var deadline=DateTime.UtcNow.AddMilliseconds(milliseconds);
        while(!predicate()){if(DateTime.UtcNow>deadline)throw new Exception(failure);await Task.Delay(30);}
    }
    static async Task Ready(){await Task.Delay(120);await Until(()=>Director!=null&&Director.View!=null&&!Director.Busy&&!Director.Paused&&!GameSceneLoader.IsLoading,"Battle did not settle");}
    static async Task Load(Action action,string scene)
    {
        action();await Until(()=>!GameSceneLoader.IsLoading&&SceneManager.GetActiveScene().name==scene,"Scene did not load: "+scene);await Task.Delay(250);
    }
    public static string Start()
    {
        Directory.CreateDirectory(Dir);File.WriteAllText(Dir+"/result.json","{\"status\":\"running\"}");_=Run();return Dir+"/result.json";
    }
    static async Task Run()
    {
        int volume=GameSettings.Volume,music=GameSettings.MusicVolume,ambience=GameSettings.AmbienceVolume,effects=GameSettings.EffectsVolume;
        bool reduced=GameSettings.ReducedMotion,narrow=GameSettings.NarrowDynamics;
        string save=Path.Combine(Application.persistentDataPath,"voyage.json");byte[] original=File.Exists(save)?File.ReadAllBytes(save):null;
        var errors=new List<string>();Application.LogCallback log=(text,stack,type)=>{if(type==LogType.Error||type==LogType.Exception)errors.Add(text);};
        Application.logMessageReceived+=log;Passed.Clear();
        try
        {
            Check(Application.isPlaying,"Editor in Play mode");Check(SeaAudio.Ready,"Audio bank booted");
            VoyageRuntime.BeginValidation(Path.GetFullPath("Logs/UiExperience/Audio"));
            GameSettings.Volume=8;GameSettings.MusicVolume=10;GameSettings.AmbienceVolume=10;GameSettings.EffectsVolume=10;
            await Load(()=>GameSceneLoader.Load(VoyageRuntime.MainMenuScene),VoyageRuntime.MainMenuScene);
            await Task.Delay(3500);
            var audio=SeaAudio.Instance;
            Check(audio.Context=="harbor","Title score selected");
            var playing=audio.GetComponentsInChildren<AudioSource>().Where(s=>s.isPlaying&&s.clip!=null&&s.clip.name.StartsWith("music_harbor_")).ToArray();
            Check(playing.Length==3,"Three phase-aligned music stems playing");
            Check(playing.Max(s=>s.timeSamples)-playing.Min(s=>s.timeSamples)<2048,"Music stems share transport");
            float[] buffer=new float[1024];bool audible=false;
            for(int sample=0;sample<30&&!audible;sample++)
            {
                playing[0].GetOutputData(buffer,0);audible=buffer.Any(f=>Math.Abs(f)>.000001f);
                if(!audible)await Task.Delay(100);
            }
            Check(audible,"Music reaches Unity DSP output");
            GameSettings.MusicVolume=0;await Task.Delay(120);
            Check(playing.All(s=>s.volume==0),"Music slider mutes music");
            var sea=audio.GetComponentsInChildren<AudioSource>().Single(s=>s.clip!=null&&s.clip.name=="sea");
            Check(sea.volume>0&&sea.isPlaying,"Sea remains audible with music muted");
            GameSettings.AmbienceVolume=0;await Task.Delay(120);Check(sea.volume==0,"Environment slider independent");
            GameSettings.MusicVolume=10;GameSettings.AmbienceVolume=10;
            await Load(()=>VoyageRuntime.StartDebugEncounter("voyage-fog-0","fog",false),VoyageRuntime.BattleScene);await Ready();
            var d=Director;
            Check(d.GetComponent<BattleAudioPresentation>()!=null,"Battle audio bound");
            Check(d.Stage.ambience==null||!d.Stage.ambience.enabled,"Legacy sea loop disabled once");
            Check(d.Stage.impactCue==null&&d.Stage.sanityCue==null,"Generic legacy hits disabled");
            for(uint seed=0;seed<12&&!d.View.Hand.Any(c=>c.Definition.Id=="revolver");seed++){d.seed=seed;d.Restart();await Ready();}
            var card=d.View.Hand.First(c=>c.Definition.Id=="revolver");int shots=audio.CueCount("shot"),hits=audio.CueCount("hit_body");
            var shot=d.Submit(BattleCommand.Play(card.InstanceId,card.Targets.First(t=>t.Enabled).Id));
            Check(shot.Success,"Gun command accepted: "+shot.Error);await Ready();
            Check(audio.CueCount("shot")==shots+1,"One gun command plays one shot");
            Check(audio.CueCount("hit_body")==hits+1,"One target hit plays one material impact");
            shots=audio.CueCount("shot");
            Check(!d.Submit(BattleCommand.Play(-999,"missing")).Success,"Invalid card rejected");
            Check(audio.CueCount("shot")==shots,"Rejected command produces no attack sound");
            d.SetPaused(true);await Task.Delay(120);Check(audio.Paused,"Battle pause reaches audio director");
            var transport=audio.GetComponentsInChildren<AudioSource>().Where(s=>s.clip!=null&&s.loop&&s.clip.name.StartsWith("music_")).ToArray();
            var positions=transport.Select(s=>s.timeSamples).ToArray();int frame=Time.frameCount;await Task.Delay(220);
            Check(Time.frameCount>frame,"Player loop advances during test");
            Check(transport.Select((s,i)=>Math.Abs(s.timeSamples-positions[i])<2048).All(x=>x),"Pause freezes music transport");
            d.SetPaused(false);await Task.Delay(150);Check(!audio.Paused,"Resume releases audio pause");
            int bodyBefore=audio.CueCount("hit_body");Check(d.Submit(BattleCommand.EndTurn()).Success,"Enemy turn accepted");
            Check(audio.CueCount("hit_body")==bodyBefore,"Enemy damage sound waits for contact");await Ready();
            Check(audio.CueCount("hit_body")==bodyBefore+1,"Enemy contact produces one damage sound");
            GameSettings.ReducedMotion=true;bool reducedHit=false;
            for(int attempt=0;attempt<3&&!reducedHit;attempt++)
            {
                bodyBefore=audio.CueCount("hit_body");int hpBefore=d.View.Health;
                Check(d.Submit(BattleCommand.EndTurn()).Success,"Reduced-motion turn accepted");
                int expected=d.View.ResolvedEnemyActions.Count(a=>{bool hit=a.Health<hpBefore;hpBefore=a.Health;return hit;});
                await Ready();
                Check(audio.CueCount("hit_body")==bodyBefore+expected,"Reduced motion matches resolved damage count");reducedHit=expected>0;
            }
            Check(reducedHit,"Reduced-motion damage action exercised");GameSettings.ReducedMotion=false;
            foreach(var id in new[]{"m02","m03","m04","m05","drowned-bell"})
            {
                await Load(()=>VoyageRuntime.StartDebugEncounter(id,"fog",true),VoyageRuntime.BattleScene);await Ready();d=Director;
                int body=audio.CueCount("hit_body"),san=audio.CueCount("sanity"),splash=audio.CueCount("splash");
                int hp=d.View.Health,sp=d.View.Sanity;
                Check(d.Submit(BattleCommand.EndTurn()).Success,id+" real turn accepted");
                var actions=d.View.ResolvedEnemyActions.ToArray();int expectedBody=0,expectedSan=0;
                foreach(var a in actions){if(a.Health<hp)expectedBody++;if(a.Sanity<sp)expectedSan++;hp=a.Health;sp=a.Sanity;}
                await Ready();
                Check(audio.CueCount("hit_body")==body+expectedBody,id+" per-action physical cue count");
                Check(audio.CueCount("sanity")==san+expectedSan,id+" per-action sanity cue count");
                if(id=="m02"||id=="m03"||id=="m04")Check(audio.CueCount("splash")==splash,id+" does not invent attack waves");
            }
            await Load(()=>VoyageRuntime.StartDebugNode("fog",RunNodeKind.Event),VoyageRuntime.VoyageScene);await Task.Delay(300);
            var sailing=UnityEngine.Object.FindAnyObjectByType<VoyageSailingPresentation>();
            Check(sailing!=null,"Voyage presentation present");
            // Drive only the existing presentation API; voyage rules and saves are untouched.
            sailing.Begin(new Vector2(25,30),false);
            for(int i=0;i<18;i++){sailing.Advance(.1f,false);sailing.Tick(.1f,false,false);await Task.Delay(40);}
            Check(sailing.Speed>0,"Voyage presentation has real travel speed");
            Check(audio.WakeGain>0,"Travel raises the wake layer");
            sailing.Finish();sailing.Tick(.1f,false,false);await Task.Delay(1500);
            Check(audio.WakeGain<.01f,"Arrival releases wake layer");
            await Load(()=>GameSceneLoader.Load(VoyageRuntime.MainMenuScene),VoyageRuntime.MainMenuScene);await Task.Delay(500);
            Check(UnityEngine.Object.FindObjectsByType<SeaAudio>().Length==1,"Scene changes keep one audio director");
            Check(audio.GetComponentsInChildren<AudioSource>().Length==29,"Audio source budget remains bounded at 29");
            Check(errors.Count==0,"No runtime errors during audio scenarios");
            Check(original==null?!File.Exists(save):File.ReadAllBytes(save).SequenceEqual(original),"Formal voyage save unchanged");
            File.WriteAllText(Dir+"/result.json",JsonConvert.SerializeObject(new{status="passed",checks=Passed,errors},Formatting.Indented));
        }
        catch(Exception e){File.WriteAllText(Dir+"/result.json",JsonConvert.SerializeObject(new{status="failed",checks=Passed,error=e.ToString(),errors},Formatting.Indented));}
        finally
        {
            GameSettings.Volume=volume;GameSettings.MusicVolume=music;GameSettings.AmbienceVolume=ambience;GameSettings.EffectsVolume=effects;
            GameSettings.ReducedMotion=reduced;GameSettings.NarrowDynamics=narrow;VoyageRuntime.EndValidation();Application.logMessageReceived-=log;
        }
    }
}
