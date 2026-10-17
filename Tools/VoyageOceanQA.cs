using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Live Editor QA. Back up voyage.json/.bak/.tmp before invoking UI flows: real buttons checkpoint.
public static class VoyageOceanQA
{
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static VoyageHud Hud => UnityEngine.Object.FindAnyObjectByType<VoyageHud>();
    static VoyageSailingPresentation Sailing => UnityEngine.Object.FindAnyObjectByType<VoyageSailingPresentation>();
    static VisualElement Root => Hud.GetComponent<UIDocument>().rootVisualElement;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Click(string name)
    {
        var button=UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement.Q<Button>(name); Check(button!=null&&button.enabledInHierarchy,"Missing/disabled "+name);
        using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}
    }
    static async Task Until(Func<bool> predicate, float seconds=35)
    {
        float end=Time.realtimeSinceStartup+seconds;
        while(!predicate()&&Time.realtimeSinceStartup<end) await Task.Delay(50);
        Check(predicate(),"Timed out waiting for voyage state");
    }
    public static object Reset(int choice=0)
    {
        var hud=Hud;hud.enabled=false;
        foreach(var name in new[]{"paused","settingsOpen","journalOpen","chartOpen"})typeof(VoyageHud).GetField(name,Private).SetValue(hud,false);
        typeof(VoyageRuntime).GetProperty("Session").SetValue(null,VoyageSession.Create(18));
        hud.enabled=true;
        var session=VoyageRuntime.Session;
        Click("route-"+session.Choices[choice].Id);
        return new{session.Seed,selected=session.Selected.Id,heading=session.HeadingTo(session.Selected),reduced=GameSettings.ReducedMotion};
    }
    public static async Task<object> NavigationLayout(int choiceCount = 2)
    {
        // Find a real known-route state, including one- and three-exit stops.
        VoyageSession fixture = null;
        for (uint seed = 0; seed < 128 && fixture == null; seed++)
        {
            var candidate = VoyageSession.Create(seed);
            for (int leg = 0; leg < 5; leg++)
            {
                candidate.RevealRoutes();
                if (candidate.Choices.Count == choiceCount) { fixture = candidate; break; }
                candidate.Select(candidate.Choices.Last().Id); candidate.SetSail(); candidate.Arrive();
                if (candidate.Current.Kind == RunNodeKind.Battle || candidate.Current.Kind == RunNodeKind.Boss)
                { candidate.BeginEncounter(); candidate.ResolveEncounter(true, candidate.Health, candidate.Sanity); }
                else if (candidate.Phase == VoyagePhase.Arrived)
                {
                    candidate.ChooseNode(candidate.GetNodeChoices().First(x => x.Enabled).Id);
                    candidate.Continue();
                }
                if (candidate.Phase == VoyagePhase.Reward) candidate.ClaimReward(null);
            }
        }
        Check(fixture != null, "No fixture with requested exits");
        var hud = Hud; hud.enabled = false;
        foreach (var name in new[] { "paused", "settingsOpen", "journalOpen", "chartOpen" })
            typeof(VoyageHud).GetField(name, Private).SetValue(hud, false);
        typeof(VoyageHud).GetField("notice", Private).SetValue(hud, null);
        typeof(VoyageRuntime).GetProperty("Session").SetValue(null, fixture);
        hud.enabled = true;
        Check(Root.Q<Button>("set-sail") == null, "Unselected route offers sail");
        Check(Root.Q<Button>("scout-ahead") == null, "Tools still clutter closed sea view");
        await Task.Delay(200);
        var choices = Root.Query<Button>(className: "route-choice").ToList();
        Check(choices.Count == choiceCount, "Visible route count differs from known choices");
        var before = choices.Select(b => b.worldBound).ToArray();
        for (int i = 0; i < choices.Count; i++)
        {
            var id = fixture.Choices[i].Id;
            Root.Q<Button>("route-" + id).Focus(); Click("route-" + id); await Task.Delay(100);
            Check(fixture.Selected.Id == id && fixture.Phase == VoyagePhase.ChoosingRoute, "Selection departed automatically");
            var button = Root.Q<Button>("route-" + id);
            Check(button.ClassListContains("selected"), "Selection has no visible state");
            Check(Mathf.Abs(before[i].y - button.worldBound.y) < 1, "Route selection shifts hit areas");
            foreach (var item in Root.Query<Button>().ToList())
            {
                var r = item.worldBound;
                Check(r.xMin >= 0 && r.yMin >= 0 && r.xMax <= Root.worldBound.xMax + 1 && r.yMax <= Root.worldBound.yMax + 1,
                    "Button outside viewport: " + item.name);
                Check(r.height >= 32, "Button target too small: " + item.name);
            }
        }
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/VoyageUi/routes-" + choiceCount + "-" + Screen.width + "x" + Screen.height + ".png"));
        await Task.Delay(100);
        Click("voyage-chart-toggle"); await Task.Delay(150);
        Check(Root.Q<Button>("scout-ahead") != null && Root.Q<Button>("reveal-routes") != null, "Chart tools missing");
        Check(Root.Query<Button>(className: "chart-node").ToList().Count == fixture.KnownStops.Count, "Chart reveals unknown stops");
        var map = Root.Q(className: "voyage-map-panel").worldBound;
        Check(!map.Overlaps(Root.Q(className: "voyage-navigation").worldBound), "Chart overlaps destination action");
        Check(!map.Overlaps(Root.Q(className: "navigation-tools").worldBound), "Chart overlaps tools");
        if (fixture.CanScoutAhead)
        {
            int uses = fixture.LookoutUses; Click("scout-ahead"); await Task.Delay(100);
            Check(fixture.LookoutUses == uses - 1, "Lookout did not spend exactly once");
            Check(!Root.Q<Button>("scout-ahead").enabledInHierarchy, "Repeated scouting enabled");
        }
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/VoyageUi/chart-" + Screen.width + "x" + Screen.height + ".png"));
        await Task.Delay(100); Click("voyage-chart-toggle");
        return new { choiceCount, screen = Screen.width + "x" + Screen.height, selectionOnly = true, stableTargets = true, toolsInChart = true };
    }
    public static string Start() { Click("set-sail");return "Sailing"; }
    public static async Task<object> Passage(int choice=0)
    {
        bool originalReduced=GameSettings.ReducedMotion;
        GameSettings.ReducedMotion=false;
        try
        {
            Reset(choice);await Task.Delay(300);
            var p=Sailing;var session=VoyageRuntime.Session;
            var destination=session.Selected;
            Vector3 expected=VoyageSailingPath.WorldPoint(new Vector2(destination.X,destination.Y));
            var start=p.RoutePosition; var startCamera=p.VoyageCamera.transform.rotation;
            Click("set-sail");await Until(()=>p.Progress>0.28f);
            Click("voyage-menu");await Task.Delay(100);
            var ship=p.Vessel.position;var rotation=p.Vessel.rotation;var camera=p.VoyageCamera.transform.position;
            var cameraRotation=p.VoyageCamera.transform.rotation;float time=p.Water.SeaTime, progress=p.Progress;
            await Task.Delay(450);
            Check(ship==p.Vessel.position&&rotation==p.Vessel.rotation&&camera==p.VoyageCamera.transform.position&&cameraRotation==p.VoyageCamera.transform.rotation,"Pause moved ship/camera");
            Check(time==p.Water.SeaTime&&progress==p.Progress,"Pause advanced ocean/passage");
            Check(p.Water.Reflections.suspended,"Pause did not suspend reflections");
            Click("pause-resume");await Until(()=>p.Progress>0.42f);
            Click("voyage-journal");await Task.Delay(100);
            time=p.Water.SeaTime;progress=p.Progress;await Task.Delay(250);
            Check(time==p.Water.SeaTime&&progress==p.Progress,"Journal advanced voyage");
            Click("journal-close");
            int wakeSamples=UnityEngine.Object.FindAnyObjectByType<VoyageWake>().SampleCount;
            Check(wakeSamples>8,"No persistent vessel wake");
            ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/VoyageOcean/underway-"+choice+".png"));
            float minY=100,maxY=-100,maxStep=0,maxTurn=0;int samples=0;
            var lastPosition=p.Vessel.position;var lastRotation=p.VoyageCamera.transform.rotation;
            while(session.Phase==VoyagePhase.Sailing)
            {
                await Task.Delay(50);samples++;
                minY=Mathf.Min(minY,p.Vessel.position.y);maxY=Mathf.Max(maxY,p.Vessel.position.y);
                maxStep=Mathf.Max(maxStep,Vector3.Distance(lastPosition,p.Vessel.position));
                maxTurn=Mathf.Max(maxTurn,Quaternion.Angle(lastRotation,p.VoyageCamera.transform.rotation));
                lastPosition=p.Vessel.position;lastRotation=p.VoyageCamera.transform.rotation;
                Check(samples<900,"Sailing never completed");
            }
            Check(session.Phase==VoyagePhase.Arrived&&session.LegsSailed==1,"Arrival did not commit exactly once");
            Check(Vector3.Distance(p.RoutePosition,expected)<0.001f,"Ship did not reach selected chart position");
            Check(!p.IsPlaying,"Presentation did not finish");
            await Task.Delay(400);
            Check(session.LegsSailed==1&&Vector3.Distance(p.RoutePosition,expected)<0.001f,"Arrival reset/committed twice");
            Check(Quaternion.Angle(startCamera,p.VoyageCamera.transform.rotation)>15,"Camera did not turn with heading");
            Check(maxStep<3&&maxTurn<8,"Discontinuous sailing motion");
            Check(maxY-minY>0.2f,"Ship did not follow waves");
            Click("voyage-chart-toggle");await Task.Delay(100);time=p.Water.SeaTime;
            await Task.Delay(200);Check(time==p.Water.SeaTime,"Open chart did not freeze sea");Click("voyage-chart-toggle");
            return new{choice,distance=Vector3.Distance(start,expected),duration=p.Duration,wakeSamples,heave=maxY-minY,maxStep,maxTurn,cameraTurn=Quaternion.Angle(startCamera,p.VoyageCamera.transform.rotation),session.LegsSailed,session.ElapsedHours,pause=true,journal=true,chart=true};
        }
        finally {GameSettings.ReducedMotion=originalReduced;}
    }
    public static async Task<object> Reduced()
    {
        bool original=GameSettings.ReducedMotion;
        try
        {
            GameSettings.ReducedMotion=true;Reset();await Task.Delay(150);
            var p=Sailing;var camera=p.VoyageCamera.transform.position;var ship=p.Vessel.position;
            await Task.Delay(250);Check(camera==p.VoyageCamera.transform.position&&ship==p.Vessel.position,"Reduced motion idling moves camera/ship");
            float start=Time.realtimeSinceStartup;Click("set-sail");await Until(()=>VoyageRuntime.Session.Phase==VoyagePhase.Arrived,3);
            float elapsed=Time.realtimeSinceStartup-start;Check(elapsed<1,"Reduced motion passage is not shortened");
            camera=p.VoyageCamera.transform.position;ship=p.Vessel.position;await Task.Delay(250);
            Check(camera==p.VoyageCamera.transform.position&&ship==p.Vessel.position,"Reduced motion arrival is not stable");
            Check(UnityEngine.Object.FindAnyObjectByType<VoyageWake>().SampleCount==0,"Reduced motion left wake");
            return new{elapsed,VoyageRuntime.Session.LegsSailed,stable=true};
        }
        finally{GameSettings.ReducedMotion=original;}
    }
    public static async Task<object> ContinueLeg()
    {
        Click("arrival-continue");await Task.Delay(100);
        var session=VoyageRuntime.Session;var choices=session.Choices;
        Check(choices.Count>0,"No next leg");Click("route-"+choices.Last().Id);
        var p=Sailing;var start=p.RoutePosition;var cam=p.VoyageCamera.transform.rotation;
        int legs=session.LegsSailed;Click("set-sail");await Until(()=>session.Phase==VoyagePhase.Arrived);
        Check(session.LegsSailed==legs+1,"Second leg not committed once");
        return new{distance=Vector3.Distance(start,p.RoutePosition),cameraTurn=Quaternion.Angle(cam,p.VoyageCamera.transform.rotation),session.LegsSailed};
    }
    public static async Task<object> Composition()
    {
        Reset(); await Task.Delay(350);
        var p=Sailing;var camera=p.VoyageCamera;
        float minX=1,maxX=0,minY=1,maxY=0;
        for(int frame=0;frame<30;frame++)
        {
            await Task.Delay(50);
            foreach(float x in new[]{-3.13f,3.13f}) foreach(float y in new[]{0f,5.06f}) foreach(float z in new[]{-9f,9f})
            {
                var view=camera.WorldToViewportPoint(p.Vessel.TransformPoint(new Vector3(x,y,z)));
                Check(view.z>0&&view.x>0.03f&&view.x<0.97f&&view.y>0.25f&&view.y<0.9f,"Vessel clipped by viewport");
                minX=Mathf.Min(minX,view.x); maxX=Mathf.Max(maxX,view.x); minY=Mathf.Min(minY,view.y); maxY=Mathf.Max(maxY,view.y);
            }
        }
        Check(UnityEngine.Object.FindObjectsByType<PlanarReflectionManager>().Length==1,"Duplicate reflection managers");
        Check(UnityEngine.Object.FindObjectsByType<VoyageWake>().Length==1,"Duplicate wake");
        return new{minX,maxX,minY,maxY,width=maxX-minX,screen=Screen.width+"x"+Screen.height};
    }
    public static async Task<object> MidpassageAccessibility()
    {
        bool original=GameSettings.ReducedMotion;
        try
        {
            GameSettings.ReducedMotion=false;Reset();Click("set-sail");await Until(()=>Sailing.Progress>0.3f);
            Click("voyage-menu");GameSettings.ReducedMotion=true;Click("pause-resume");
            float start=Time.realtimeSinceStartup;
            await Until(()=>VoyageRuntime.Session.Phase==VoyagePhase.Arrived,3);
            Check(VoyageRuntime.Session.LegsSailed==1,"Accessibility toggle double arrival");
            return new{elapsed=Time.realtimeSinceStartup-start,legs=VoyageRuntime.Session.LegsSailed};
        }
        finally{GameSettings.ReducedMotion=original;}
    }
    static void CheckReleased()
    {
        const int expected = 0;
        Check(UnityEngine.Object.FindObjectsByType<BattleWater>().Length==expected,"Unexpected water after voyage exit");
        Check(UnityEngine.Object.FindObjectsByType<PlanarReflectionManager>().Length==expected,"Unexpected reflection managers");
        Check(UnityEngine.Object.FindObjectsByType<PlanarReflectionVolume>().Length==expected,"Unexpected reflection volumes");
        Check(UnityEngine.Object.FindObjectsByType<VoyageSceneView>().Length==0,"Voyage destination leaked across scene");
        Check(!Resources.FindObjectsOfTypeAll<Mesh>().Any(m=>m.name=="Voyage wake (runtime)"),"Wake mesh leaked");
    }
    public static async Task<object> Lifecycle()
    {
        bool original=GameSettings.ReducedMotion;
        try
        {
            GameSettings.ReducedMotion=false;Reset();Click("set-sail");await Until(()=>Sailing.Progress>0.2f);
            Click("voyage-menu");Click("pause-title");
            await Until(()=>SceneManager.GetActiveScene().name=="MainMenu");await Task.Delay(200);CheckReleased();
            Check(VoyageRuntime.Session.Phase==VoyagePhase.Sailing,"Exit lost pending passage");
            Click("menu-continue");await Until(()=>Hud!=null&&Sailing.IsPlaying);
            Check(Sailing.Progress<0.2f&&VoyageRuntime.Session.LegsSailed==0,"Saved sailing passage did not restart cleanly");
            await Until(()=>VoyageRuntime.Session.Phase==VoyagePhase.Arrived);
            Check(VoyageRuntime.Session.LegsSailed==1,"Resumed passage duplicated arrival");
            GameSettings.ReducedMotion=true;
            var expectedSeed=VoyageRuntime.Session.Current.BattleSeed;
            Click("arrival-investigate");await Until(()=>SceneManager.GetActiveScene().name=="OceanBattle");await Task.Delay(250);
            CheckReleased();var director=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
            Check(director.seed==expectedSeed,"Encounter seed changed");
            await Until(()=>!director.Busy,5);
            Click("battle-end-turn");await Until(()=>!director.Busy,5);
            var turn=director.View.Turn;var health=director.View.Health;var sanity=director.View.Sanity;
            int legs=VoyageRuntime.Session.LegsSailed;
            Check(VoyageRuntime.TryResume(out var resumeError),resumeError);
            director.SetPaused(true);Click("battle-voyage");await Until(()=>Hud!=null);await Task.Delay(250);
            Check(VoyageRuntime.Session.LegsSailed==legs&&VoyageRuntime.Session.Phase==VoyagePhase.Encounter,"Battle return lost pending encounter");
            Check(UnityEngine.Object.FindObjectsByType<PlanarReflectionManager>().Length==1,"Return did not recreate exactly one ocean");
            Click("resume-encounter");await Until(()=>SceneManager.GetActiveScene().name=="OceanBattle");await Task.Delay(250);
            director=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
            Check(director.View.Turn==turn&&director.View.Health==health&&director.View.Sanity==sanity,"Mid-battle continuation reset resources");
            return new{saveResume=true,releaseOnMenu=true,releaseOnBattle=true,encounterSeed=expectedSeed,turn,health,sanity,legs,returnToVoyage=true};
        }
        finally{GameSettings.ReducedMotion=original;}
    }
}
