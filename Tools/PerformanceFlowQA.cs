using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Live smoke test; all saves are isolated from the player's voyage.
public static class PerformanceFlowQA
{
    static readonly List<object> Results = new List<object>();
    static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static VisualElement Root => UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
        .First(d => d.GetComponent<GameSceneLoader>() == null).rootVisualElement;
    static void Click(string name)
    {
        var button = Root.Q<Button>(name); Check(button != null && button.enabledInHierarchy, "Missing button: " + name);
        using var e = NavigationSubmitEvent.GetPooled(); e.target = button; button.SendEvent(e);
    }
    static async Task Load(string label, Action action, string expected)
    {
        var clock=Stopwatch.StartNew(); var before=Time.frameCount;
        action();
        var session=VoyageRuntime.Session;
        // Repeated launch input must not replace the session while loading.
        VoyageRuntime.StartDebugNode("bones", RunNodeKind.Event);
        Check(ReferenceEquals(session,VoyageRuntime.Session), "Duplicate transition mutated the session.");
        while(GameSceneLoader.IsLoading || SceneManager.GetActiveScene().name != expected)
        {
            Check(clock.Elapsed.TotalSeconds<45, "Loading timed out: "+label);
            await Task.Delay(10);
        }
        var loadMs=clock.ElapsedMilliseconds;
        await Task.Delay(100);
        Check(UnityEngine.Object.FindAnyObjectByType<GameSceneLoader>()==null,"Loading panel was not released.");
        Results.Add(new { label, loadMs, settledMs=clock.ElapsedMilliseconds, frames=Time.frameCount-before });
    }
    public static async Task<object> Main()
    {
        Check(EditorApplication.isPlaying, "Enter Play mode first.");
        var oldBackground=Application.runInBackground;
        var oldPriority=Application.backgroundLoadingPriority;
        var errors=new List<string>();
        Application.LogCallback capture=(message,stack,type)=> { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message); };
        Application.logMessageReceived+=capture;
        Application.runInBackground=true;
        Directory.CreateDirectory("Logs/Performance");
        Results.Clear();
        var formalPath=Path.Combine(Application.persistentDataPath,"voyage.json");
        var formal=File.Exists(formalPath)?File.ReadAllBytes(formalPath):null;
        VoyageRuntime.BeginValidation(Path.GetFullPath("Logs/UiExperience/Performance"));
        try
        {
            VoyageRuntime.StartNew(7);
            await Load("title-to-voyage",()=>GameSceneLoader.Load(VoyageRuntime.VoyageScene),VoyageRuntime.VoyageScene);
            Check(UnityEngine.Object.FindAnyObjectByType<VoyageHud>()!=null,"No voyage HUD");
            if(Root.Q<Button>("voyage-flow-continue")!=null)Click("voyage-flow-continue");
            foreach(var id in new[]{"voyage-journal","journal-close","voyage-action-book","action-book-close","voyage-menu","pause-resume"})
            {
                var watch=Stopwatch.StartNew();Click(id);await Task.Delay(60);
                Results.Add(new { button=id, elapsedMs=watch.ElapsedMilliseconds });
            }
            await Task.Delay(200);
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/Performance/voyage.png"));
            await Task.Delay(100);
            await Load("voyage-to-battle",()=>VoyageRuntime.StartDebugEncounter("voyage-fog-0","fog",false),VoyageRuntime.BattleScene);
            var director=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
            Check(director!=null && director.Stage!=null && director.View!=null,"Battle failed to initialize.");
            director.SetPaused(true);
            await Load("battle-to-voyage",()=>VoyageRuntime.ReturnToVoyage(),VoyageRuntime.VoyageScene);
            foreach(var id in new[]{"fog-visitors","m02","m03","m04","m05","drowned-bell","storm-gauntlet","hollow-beacon"})
            {
                await Load(id,()=>VoyageRuntime.StartDebugEncounter(id,"fog",true),VoyageRuntime.BattleScene);
                director=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
                Check(director!=null && director.Stage!=null && director.View!=null,"Boss failed: "+id);
                await Load(id+"-title",()=>GameSceneLoader.Load(VoyageRuntime.MainMenuScene),VoyageRuntime.MainMenuScene);
            }
            Click("menu-debug");Check(Root.Q<Button>("debug-level-32")!=null,"Debug selector incomplete.");Click("debug-close");
            Click("menu-collection");Click("collection-next");Click("collection-previous");Click("collection-close");
            Check(Application.backgroundLoadingPriority==oldPriority,"Loading priority not restored.");
            Check(errors.Count==0,"Runtime errors: "+string.Join("; ",errors));
            var after=File.Exists(formalPath)?File.ReadAllBytes(formalPath):null;
            Check(formal==null?after==null:after!=null&&formal.SequenceEqual(after),"Formal save was changed.");
            var report=new { success=true, results=Results, formalSaveUnchanged=true, runtimeErrors=errors.Count };
            File.WriteAllText("Logs/Performance/flow-result.json",JsonConvert.SerializeObject(report,Formatting.Indented));
            return report;
        }
        finally { VoyageRuntime.EndValidation();Application.runInBackground=oldBackground;Application.logMessageReceived-=capture; }
    }
}
