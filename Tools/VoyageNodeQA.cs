using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

public static class VoyageNodeQA
{
    static VoyageSailingPresentation Sailing=>UnityEngine.Object.FindAnyObjectByType<VoyageSailingPresentation>();
    static VoyageHud Hud=>UnityEngine.Object.FindAnyObjectByType<VoyageHud>();
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Click(string name)
    {
        var button=Hud.GetComponent<UIDocument>().rootVisualElement.Q<Button>(name);Check(button!=null&&button.enabledInHierarchy,"Missing button "+name);
        using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}
    }
    public static async Task<object> Gallery()
    {
        Check(Application.isPlaying,"Enter Play first");
        var p=Sailing;var list=new System.Collections.Generic.List<object>();
        var ui=Hud.GetComponent<UIDocument>().rootVisualElement;
        ui.style.display=DisplayStyle.None;
        try
        {
        foreach(var entry in p.Destinations.catalog.entries)
        {
            p.SetLocation(new Vector2(50,12),Vector2.up,entry.location);
            
            await Task.Delay(500);
            var renderers=p.Destinations.Current.GetComponentsInChildren<Renderer>();
            Check(renderers.All(r=>r.sharedMaterials.All(m=>m!=null&&m.shader.isSupported)),"Missing material "+entry.location);
            Check(renderers.All(r=>r.enabled),"Hidden arrival "+entry.location);
            string path=Path.GetFullPath("Logs/VoyageNodes/"+entry.prefab.name+".png");
            ScreenCapture.CaptureScreenshot(path);await Task.Delay(180);
            list.Add(new{entry.location,path,renderers=renderers.Length,triangles=p.Destinations.Current.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3)});
        }
        return list;
        } finally {ui.style.display=DisplayStyle.Flex;}
    }
    public static async Task<object> Timing()
    {
        var hud=Hud;hud.enabled=false;typeof(VoyageRuntime).GetProperty("Session").SetValue(null,VoyageSession.Create(18));hud.enabled=true;
        GameSettings.ReducedMotion=false;var session=VoyageRuntime.Session;Click("route-"+session.Choices[0].Id);
        var p=Sailing;Click("set-sail");float started=Time.realtimeSinceStartup;float firstVisible=-1;bool paused=false;float pauseTime=0;
        int captured=0;float maxVisibilityBeforeThree=0;float middleVisibility=-1;
        while(session.Phase==VoyagePhase.Sailing)
        {
            await Task.Delay(30);float elapsed=p.Progress*p.Duration;float visibility=p.Destinations.Destination?.Visibility??1;
            if(elapsed<3)maxVisibilityBeforeThree=Mathf.Max(maxVisibilityBeforeThree,visibility);
            if(visibility>0&&firstVisible<0)firstVisible=elapsed;
            if(elapsed>3.8f&&elapsed<4.2f)middleVisibility=visibility;
            if(!paused&&elapsed>3.5f)
            {
                Click("voyage-menu");await Task.Delay(60);
                float progress=p.Progress,fog=RenderSettings.fogDensity;
                float visible=p.Destinations.Destination.Visibility;var pos=p.Destinations.Destination.transform.position;
                var floating=p.Destinations.Destination.GetComponentsInChildren<VoyageSceneFloat>().Select(f=>f.transform.position).ToArray();
                float pauseStart=Time.realtimeSinceStartup;await Task.Delay(400);
                Check(progress==p.Progress&&fog==RenderSettings.fogDensity&&visible==p.Destinations.Destination.Visibility,"Paused reveal changed");
                Check(floating.SequenceEqual(p.Destinations.Destination.GetComponentsInChildren<VoyageSceneFloat>().Select(f=>f.transform.position)),"Paused destination moves");
                Click("pause-resume");pauseTime+=Time.realtimeSinceStartup-pauseStart+.06f;paused=true;
            }
            float[] marks={2.5f,3.6f,4.3f};
            if(captured<marks.Length&&elapsed>=marks[captured])
            {ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/VoyageNodes/reveal-"+captured+".png"));captured++;}
            Check(Time.realtimeSinceStartup-started<12,"Travel did not complete");
        }
        float duration=Time.realtimeSinceStartup-started-pauseTime;
        Check(Mathf.Abs(duration-5)<.5f,"Expected five-second passage: "+duration);
        Check(maxVisibilityBeforeThree==0,"Destination leaked before last two seconds");
        Check(firstVisible>=3&&firstVisible<3.25f,"Reveal did not begin at three seconds");
        Check(middleVisibility>.2f&&middleVisibility<.8f,"Reveal is not gradual");
        Check(p.Destinations.Current!=null&&p.Destinations.Current.Visibility==1&&p.Destinations.Destination==null,"Arrival scene not promoted");
        Check(session.LegsSailed==1,"Arrival did not commit exactly once");await Task.Delay(400);
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/VoyageNodes/arrival.png"));
        return new{duration,firstVisible,middleVisibility,maxVisibilityBeforeThree,pausedReveal=true,session.LegsSailed,current=p.Destinations.Current.name};
    }
    public static async Task<object> Record()
    {
        var hud=Hud;hud.enabled=false;typeof(VoyageRuntime).GetProperty("Session").SetValue(null,VoyageSession.Create(18));hud.enabled=true;
        GameSettings.ReducedMotion=false;Click("route-"+VoyageRuntime.Session.Choices[0].Id);
        string folder=Path.GetFullPath("Logs/VoyageNodes/frames");Directory.CreateDirectory(folder);
        var frames=new System.Collections.Generic.List<(string path,float time)>();
        float start=Time.realtimeSinceStartup;bool begun=false;
        while(Time.realtimeSinceStartup-start<7)
        {
            float now=Time.realtimeSinceStartup-start;
            if(now>.75f&&!begun){Click("set-sail");begun=true;}
            string path=Path.Combine(folder,frames.Count.ToString("D4")+".png");
            ScreenCapture.CaptureScreenshot(path);frames.Add((path,now));await Task.Delay(90);
        }
        await Task.Delay(150);
        var text=new System.Text.StringBuilder();
        for(int i=0;i<frames.Count;i++)
        {
            text.AppendLine("file '"+frames[i].path+"'");
            text.AppendLine("duration "+(i+1<frames.Count?frames[i+1].time-frames[i].time:.1f).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        File.WriteAllText(Path.Combine(folder,"frames.txt"),text.ToString());
        return new{frames=frames.Count,phase=VoyageRuntime.Session.Phase.ToString(),Sailing.Duration};
    }

}
