using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

public static class NewVoyageDepartureQA
{
    const string Dir="Logs/UiExperience/NewVoyage";
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static VisualElement Root=>UnityEngine.Object.FindObjectsByType<UIDocument>().First(d=>d.gameObject.scene==SceneManager.GetActiveScene()).rootVisualElement;
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static async Task Wait(Func<bool> value,string message)
    {for(int i=0;i<1500;i++){if(value())return;await Task.Delay(20);}throw new InvalidOperationException(message);}
    static void Click(string name)
    {
        var button=Root.Q<Button>(name);Check(button!=null&&button.enabledInHierarchy,"Missing button: "+name+"; scene="+SceneManager.GetActiveScene().name+"; loading="+GameSceneLoader.IsLoading+"; buttons="+string.Join(",",Root.Query<Button>().ToList().Select(b=>b.name+":"+b.enabledInHierarchy)));
        using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}
    }
    static async Task Capture(string name)
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(t=>t!=null);
        var bytes=(byte[])type.GetMethod("EncodeScreenToPng",All).Invoke(null,new object[]{Screen.width,Screen.height});File.WriteAllBytes(Dir+"/"+name+".png",bytes);await Task.Delay(30);
    }
    static async Task Resize(int width,int height)
    {
        var asm=typeof(Editor).Assembly;var viewType=asm.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(viewType);
        var sizesType=asm.GetType("UnityEditor.GameViewSizes");var sizes=typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance",All).GetValue(null);
        var group=sizesType.GetMethod("GetGroup",All).Invoke(sizes,new[]{Enum.Parse(asm.GetType("UnityEditor.GameViewSizeGroupType"),"Standalone")});
        int builtins=(int)group.GetType().GetMethod("GetBuiltinCount",All).Invoke(group,null),customs=(int)group.GetType().GetMethod("GetCustomCount",All).Invoke(group,null),index=-1;
        for(int i=0;i<builtins+customs;i++)
        {
            var size=group.GetType().GetMethod("GetGameViewSize",All).Invoke(group,new object[]{i});
            if((int)size.GetType().GetProperty("width",All).GetValue(size)==width&&(int)size.GetType().GetProperty("height",All).GetValue(size)==height){index=i;break;}
        }
        if(index<0)
        {
            var size=Activator.CreateInstance(asm.GetType("UnityEditor.GameViewSize"),All,null,new object[]{Enum.Parse(asm.GetType("UnityEditor.GameViewSizeType"),"FixedResolution"),width,height,"Departure QA "+width+"x"+height},null);
            group.GetType().GetMethod("AddCustomSize",All).Invoke(group,new[]{size});index=builtins+customs;
        }
        viewType.GetProperty("selectedSizeIndex",All).SetValue(view,index);view.Repaint();await Task.Delay(300);
        Check(Screen.width==width&&Screen.height==height,"Game View did not resize.");
    }
    static async Task<object> Run(bool reduced,int width,int height,bool switchMidway=false)
    {
        var label=(switchMidway?"switch":reduced?"reduced":"normal")+"-"+width+"x"+height;
        VoyageRuntime.BeginValidation(Path.GetFullPath(Dir+"/save-"+label));
        PlayerPrefs.SetInt("settings.reducedMotion",reduced?1:0);
        SceneManager.LoadScene("MainMenu");await Task.Delay(500);await Resize(width,height);
        await Wait(()=>Root.Q<Button>("menu-start")?.enabledInHierarchy==true,"Menu did not become ready.");
        var sea=UnityEngine.Object.FindAnyObjectByType<MainMenuSeaPresentation>();var start=sea.vessel.position;var camera=sea.menuCamera.transform.position;
        var priority=Application.backgroundLoadingPriority;
        // Cancelling the crew picker must not create a voyage or light the beacon.
        Click("menu-start");if(Root.Q<Button>("menu-confirm-new")!=null)Click("menu-confirm-new");Click("crew-back");
        Check(!GameSceneLoader.IsLoading&&sea.BeaconStrength==0,"Cancelling crew selection launched the transition.");
        await Capture(label+"-title");
        Click("menu-start");if(Root.Q<Button>("menu-confirm-new")!=null)Click("menu-confirm-new");Click("crew-medic");
        Check(GameSceneLoader.IsLoading&&NewVoyageDeparture.IsActive,"New voyage did not use the departure.");
        var checkpoint=JsonConvert.SerializeObject(VoyageRuntime.Session.Export());
        GameSceneLoader.Depart("Voyage",sea);
        Check(UnityEngine.Object.FindObjectsByType<NewVoyageDeparture>().Length==1,"Double click created another transition.");
        bool loadedUnderFog=false;
        void Loaded(Scene scene,LoadSceneMode mode)
        {
            if(scene.name=="Voyage")
            {
                var transition=UnityEngine.Object.FindAnyObjectByType<NewVoyageDeparture>();
                loadedUnderFog=GameSceneLoader.IsLoading&&transition!=null&&transition.VeilOpacity>=.999f;
            }
        }
        SceneManager.sceneLoaded+=Loaded;
        var phases=new HashSet<string>();float maxTravel=0,maxCamera=0;int wake=0;float beacon=0;
        try
        {
            var began=Time.realtimeSinceStartup;
            while(GameSceneLoader.IsLoading)
            {
                Check(Time.realtimeSinceStartup-began<25,"Departure remained locked.");
                var transition=UnityEngine.Object.FindAnyObjectByType<NewVoyageDeparture>();
                if(transition!=null)
                {
                    if(sea!=null)
                    {
                        var drift=Vector3.ProjectOnPlane(sea.vessel.position-start,Vector3.up).magnitude;
                        maxTravel=Mathf.Max(maxTravel,drift);maxCamera=Mathf.Max(maxCamera,Vector3.Distance(camera,sea.menuCamera.transform.position));
                        wake=Mathf.Max(wake,sea.DepartureWakeSamples);beacon=Mathf.Max(beacon,sea.BeaconStrength);
                    }
                    if(phases.Add(transition.Phase))await Capture(label+"-"+transition.Phase);
                    if(switchMidway&&transition.Phase=="departure")PlayerPrefs.SetInt("settings.reducedMotion",1);
                    using(var key=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape})){key.target=Root;Root.SendEvent(key);}
                }
                await Task.Delay(20);
            }
        }
        finally{SceneManager.sceneLoaded-=Loaded;}
        await Task.Delay(250);await Capture(label+"-arrived");
        Check(loadedUnderFog,"Scene changed before the fog fully covered it.");
        Check(!NewVoyageDeparture.IsActive&&UnityEngine.Object.FindAnyObjectByType<GameSceneLoader>()==null,"Transition leaked its lock or host.");
        Check(Application.backgroundLoadingPriority==priority,"Loading priority was not restored.");
        Check(checkpoint==JsonConvert.SerializeObject(VoyageRuntime.Session.Export()),"The cinematic changed voyage rules or history.");
        Check(Root.style.opacity.value>=.99f&&Root.Q<Button>("voyage-support-manifest")!=null,"Voyage UI did not recover.");
        Check(beacon>.5f,"Lighthouse did not light.");
        if(reduced)Check(maxCamera<.01f&&maxTravel<.3f&&wake==0,"Reduced motion moved the camera or sailed the ship.");
        else if(!switchMidway)Check(maxTravel>15&&maxCamera>5&&wake>0,"Normal departure did not sail with its camera and wake.");
        var report=new{label,loadedUnderFog,maxTravel,maxCamera,wake,beacon,phases=phases.ToArray(),rulesUnchanged=true};
        File.WriteAllText(Dir+"/"+label+"-result.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
    }
    public static async Task<object> Verify()
    {
        Check(EditorApplication.isPlaying,"Enter Play first.");Directory.CreateDirectory(Dir);
        var viewType=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(viewType);var size=(int)viewType.GetProperty("selectedSizeIndex",All).GetValue(view);
        var preferences=new[]{"settings.reducedMotion","unsounded-sea.prepared-crew"}.ToDictionary(k=>k,k=>new{had=PlayerPrefs.HasKey(k),text=PlayerPrefs.GetString(k),number=PlayerPrefs.GetInt(k)});
        var background=Application.runInBackground;Application.runInBackground=true;
        Dictionary<string,byte[]> Formal()=>Directory.GetFiles(Application.persistentDataPath,"voyage*.json*").ToDictionary(p=>p,File.ReadAllBytes);
        var before=Formal();var errors=new List<string>();Application.LogCallback log=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);};Application.logMessageReceived+=log;
        try
        {
            var cases=new List<object>();
            foreach(var dimensions in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1080),new Vector2Int(1600,1200)})cases.Add(await Run(false,dimensions.x,dimensions.y));
            cases.Add(await Run(true,1920,1080));cases.Add(await Run(false,1920,1080,true));
            SceneManager.LoadScene("MainMenu");await Task.Delay(500);Click("menu-continue");
            Check(!NewVoyageDeparture.IsActive,"Continue replayed the new-voyage cinematic.");await Wait(()=>!GameSceneLoader.IsLoading,"Continue stayed locked.");
            var after=Formal();Check(before.Count==after.Count&&before.All(p=>after.TryGetValue(p.Key,out var bytes)&&p.Value.SequenceEqual(bytes)),"Formal saves changed.");
            Check(errors.Count==0,string.Join("; ",errors));
            var report=new{success=true,cases,continueWithoutCinematic=true,runtimeErrors=errors.Count,formalSavesUnchanged=true};
            File.WriteAllText(Dir+"/result.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
        }
        catch(Exception error){File.WriteAllText(Dir+"/error.txt",error.ToString());throw;}
        finally
        {
            VoyageRuntime.EndValidation();Application.runInBackground=background;Application.logMessageReceived-=log;
            foreach(var p in preferences){if(!p.Value.had)PlayerPrefs.DeleteKey(p.Key);else if(p.Key=="settings.reducedMotion")PlayerPrefs.SetInt(p.Key,p.Value.number);else PlayerPrefs.SetString(p.Key,p.Value.text);}PlayerPrefs.Save();
            viewType.GetProperty("selectedSizeIndex",All).SetValue(view,size);view.Repaint();
        }
    }
}
