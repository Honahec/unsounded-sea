using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Runtime;

public static class DeckEnemyPlacement
{
    const string Path="Assets/UnsoundedSea/Art/OceanBattle/FogSeaStage.prefab";
    const string Dir="Logs/UiExperience/DeckPlacement";
    const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static async Task Wait(Func<bool> ready)
    {for(int i=0;i<1000;i++){if(ready())return;await Task.Delay(20);}throw new InvalidOperationException("Battle did not become ready.");}
    static async Task Resize(int width,int height)
    {
        var asm=typeof(Editor).Assembly;var type=asm.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);
        var sizesType=asm.GetType("UnityEditor.GameViewSizes");var sizes=typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance",All).GetValue(null);
        var group=sizesType.GetMethod("GetGroup",All).Invoke(sizes,new[]{Enum.Parse(asm.GetType("UnityEditor.GameViewSizeGroupType"),"Standalone")});
        int count=(int)group.GetType().GetMethod("GetBuiltinCount",All).Invoke(group,null)+(int)group.GetType().GetMethod("GetCustomCount",All).Invoke(group,null),index=-1;
        for(int i=0;i<count;i++)
        {
            var size=group.GetType().GetMethod("GetGameViewSize",All).Invoke(group,new object[]{i});
            if((int)size.GetType().GetProperty("width",All).GetValue(size)==width&&(int)size.GetType().GetProperty("height",All).GetValue(size)==height){index=i;break;}
        }
        if(index<0)
        {
            var size=Activator.CreateInstance(asm.GetType("UnityEditor.GameViewSize"),All,null,new object[]{Enum.Parse(asm.GetType("UnityEditor.GameViewSizeType"),"FixedResolution"),width,height,"Deck QA "+width+"x"+height},null);
            group.GetType().GetMethod("AddCustomSize",All).Invoke(group,new[]{size});index=count;
        }
        type.GetProperty("selectedSizeIndex",All).SetValue(view,index);view.Repaint();await Task.Delay(250);
        Check(Screen.width==width&&Screen.height==height,"Game View did not resize.");
    }
    static void Capture(string name)
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(t=>t!=null);
        File.WriteAllBytes(Dir+"/"+name+".png",(byte[])type.GetMethod("EncodeScreenToPng",All).Invoke(null,new object[]{Screen.width,Screen.height}));
    }
    public static async Task<object> Verify()
    {
        Check(Application.isPlaying,"Enter Play first.");Directory.CreateDirectory(Dir);
        var viewType=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(viewType);var size=(int)viewType.GetProperty("selectedSizeIndex",All).GetValue(view);
        var background=Application.runInBackground;Application.runInBackground=true;
        var formal=Directory.GetFiles(Application.persistentDataPath,"voyage*.json*").ToDictionary(p=>p,File.ReadAllBytes);
        var results=new List<object>();var errors=new List<string>();Application.LogCallback log=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);};Application.logMessageReceived+=log;
        VoyageRuntime.BeginValidation(System.IO.Path.GetFullPath(Dir+"/save"));
        try
        {
            var prefab=AssetDatabase.LoadAssetAtPath<BattleStage>(Path);
            Check(prefab.singleActorAnchor==prefab.actorAnchors[1],"Solo slot is not the original right-hand slot.");
            Check(prefab.actorAnchors[0].localPosition==new Vector3(-1.15f,1.45f,1.3f)&&prefab.actorAnchors[1].localPosition==new Vector3(1.15f,1.45f,2.3f),"Original paired layout changed.");
            foreach(var encounter in new[]{"voyage-fog-2","voyage-fog-4"})
            {
                VoyageRuntime.StartDebugEncounter(encounter,"fog",false);
                await Wait(()=>!GameSceneLoader.IsLoading&&UnityEngine.Object.FindAnyObjectByType<BattleDirector>()?.Stage!=null);
                var director=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();var stage=director.Stage;
                foreach(var dimensions in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1080),new Vector2Int(1600,1200)})
                {
                    await Resize(dimensions.x,dimensions.y);Physics.SyncTransforms();
                    var root=director.GetComponent<UIDocument>().rootVisualElement;var labels=new List<Rect>();
                    for(int i=0;i<stage.Actors.Count;i++)
                    {
                        var actor=stage.Actors[i];var expected=stage.Actors.Count==1?stage.actorAnchors[1]:stage.actorAnchors[i];
                        Check(actor.transform.parent==expected,"Actor uses the wrong anchor.");
                        var collider=actor.GetComponent<BoxCollider>();var ray=new Ray(stage.battleCamera.transform.position,collider.bounds.center-stage.battleCamera.transform.position);
                        Check(collider.Raycast(ray,out _,100),"Enemy collider is not selectable.");
                        var label=root.Q<Button>("actor-"+actor.EnemyId);Check(label!=null,"Missing enemy label.");var rect=label.worldBound;
                        Check(rect.xMin>=0&&rect.yMin>=0&&rect.xMax<=root.worldBound.xMax+1&&rect.yMax<=root.worldBound.yMax+1,"Enemy label is outside the view.");
                        Check(labels.All(r=>!r.Overlaps(rect)),"Enemy labels overlap.");labels.Add(rect);
                    }
                    var labelName=encounter+"-"+dimensions.x+"x"+dimensions.y;Capture(labelName);
                    results.Add(new{encounter,width=dimensions.x,height=dimensions.y,actors=stage.Actors.Select(a=>new{a.name,slot=a.transform.parent.name,viewport=stage.battleCamera.WorldToViewportPoint(a.transform.position).ToString("F3")}).ToArray()});
                }
            }
            Check(errors.Count==0,string.Join("; ",errors));
            var after=Directory.GetFiles(Application.persistentDataPath,"voyage*.json*").ToDictionary(p=>p,File.ReadAllBytes);
            Check(formal.Count==after.Count&&formal.All(p=>after.TryGetValue(p.Key,out var bytes)&&p.Value.SequenceEqual(bytes)),"Formal saves changed.");
            var report=new{success=true,results,runtimeErrors=errors.Count,formalSavesUnchanged=true};File.WriteAllText(Dir+"/result.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
        }
        finally
        {
            VoyageRuntime.EndValidation();Application.runInBackground=background;Application.logMessageReceived-=log;
            viewType.GetProperty("selectedSizeIndex",All).SetValue(view,size);view.Repaint();SceneManager.LoadScene("MainMenu");
        }
    }
    public static object Inspect()
    {
        object Describe(BattleStage stage)=>new
        {
            name=stage.name,
            camera=stage.battleCamera.transform.position.ToString("F3"),
            anchors=stage.actorAnchors.Select(a=>new{a.name,local=a.localPosition.ToString("F3"),viewport=stage.battleCamera.WorldToViewportPoint(a.position).ToString("F3")}).ToArray(),
            actors=stage.Actors.Select(a=>new{a.name,viewport=stage.battleCamera.WorldToViewportPoint(a.transform.position).ToString("F3")}).ToArray()
        };
        var asset=AssetDatabase.LoadAssetAtPath<BattleStage>(Path);
        var live=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
        return new{asset=Describe(asset),live=live?.Stage!=null?Describe(live.Stage):null};
    }
}
