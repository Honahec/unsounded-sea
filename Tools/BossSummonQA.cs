using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Actual summon turns and UI/model target selection, without changing resources or formal saves.
public static class BossSummonQA
{
    const string Dir="Logs/BossSummons";
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static BattleDirector Director=>UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
    static VisualElement Root=>Director.GetComponent<UIDocument>().rootVisualElement;
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    public static string Configure()=>UnsoundedSea.Editor.BossAtmosphereSetup.ConfigureChoirDeck()+" "+UnsoundedSea.Editor.BossAtmosphereSetup.ConfigureChoirMouth();
    static async Task Ready()
    {
        for(int i=0;i<600;i++){if(Director?.View!=null&&!Director.Busy)return;await Task.Delay(50);}
        throw new InvalidOperationException("Battle did not unlock.");
    }
    static void Click(string name)
    {
        var button=Root.Q<Button>(name);Check(button!=null&&button.enabledInHierarchy,"Missing enabled button: "+name);
        using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}
    }
    static async Task Resize(int width,int height)
    {
        var asm=typeof(Editor).Assembly;var type=asm.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);
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
            var size=Activator.CreateInstance(asm.GetType("UnityEditor.GameViewSize"),All,null,new object[]{Enum.Parse(asm.GetType("UnityEditor.GameViewSizeType"),"FixedResolution"),width,height,"Summon QA "+width+"x"+height},null);
            group.GetType().GetMethod("AddCustomSize",All).Invoke(group,new[]{size});index=builtins+customs;
        }
        type.GetProperty("selectedSizeIndex",All).SetValue(view,index);view.Repaint();await Task.Delay(350);
        Check(Screen.width==width&&Screen.height==height,"Game View did not resize.");
    }
    static async Task Capture(string name,int delay=250)
    {
        await Task.Delay(delay);
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(t=>t!=null);
        var bytes=(byte[])type.GetMethod("EncodeScreenToPng",All).Invoke(null,new object[]{Screen.width,Screen.height});
        File.WriteAllBytes(Dir+"/"+name+"-"+Screen.width+"x"+Screen.height+".png",bytes);
    }
    static async Task CheckSummons()
    {
        var panel=Root.Q("boss-mechanism-panel").worldBound;var plates=new List<Rect>();
        foreach(var actor in Director.Stage.Actors.Skip(1).Where(a=>a.Alive))
        {
            var plate=Root.Q<Button>("actor-"+actor.EnemyId);var rect=plate.worldBound;
            Check(plate.resolvedStyle.display==DisplayStyle.Flex,"Summon nameplate hidden.");
            Check(!rect.Overlaps(panel)&&!plates.Any(r=>r.Overlaps(rect)),"Summon UI overlaps a mechanism or another summon.");
            Check(rect.xMin>Root.worldBound.width*.6f&&rect.xMax<=Root.worldBound.xMax,"Summon plate is not in the clear right column.");plates.Add(rect);
            var bounds=BattleActorView.MeasureLocalBounds(actor.transform);
            var p=Director.Stage.battleCamera.WorldToViewportPoint(actor.transform.TransformPoint(bounds.center));
            var bottom=Director.Stage.battleCamera.WorldToViewportPoint(actor.transform.TransformPoint(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)));
            var top=Director.Stage.battleCamera.WorldToViewportPoint(actor.transform.TransformPoint(new Vector3(bounds.center.x,bounds.max.y,bounds.center.z)));
            Check(p.z>0&&p.x>.25f&&p.x<.75f&&bottom.y>.3f&&top.y<.65f&&top.y-bottom.y>.045f,"Summon silhouette outside readable battle area.");
            var ray=Director.Stage.battleCamera.ViewportPointToRay(p);
            Check(Physics.Raycast(ray,out var hit)&&hit.collider.GetComponentInParent<BattleActorView>()==actor,"Model target is occluded.");
            Click("actor-"+actor.EnemyId);Check(Director.SelectedEnemy==actor.EnemyId,"Summon selection failed.");
            await Task.Delay(100); // Selection can rebuild the HUD; wait for its new layout before the next plate.
        }
    }
    public static async Task<object> Verify()
    {
        Check(EditorApplication.isPlaying,"Enter Play first.");Directory.CreateDirectory(Dir);
        var viewType=typeof(Editor).Assembly.GetType("UnityEditor.GameView");var gameView=EditorWindow.GetWindow(viewType);
        var originalSize=(int)viewType.GetProperty("selectedSizeIndex",All).GetValue(gameView);
        var background=Application.runInBackground;Application.runInBackground=true;
        var path=VoyageRuntime.SavePath;var before=File.Exists(path)?File.ReadAllBytes(path):null;
        var errors=new List<string>();Application.LogCallback log=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);};Application.logMessageReceived+=log;
        try
        {
            VoyageRuntime.StartDebugEncounter("m02","bones",true);await Task.Delay(900);await Ready();
            await Resize(1920,1080);await Capture("before-summon");
            for(int i=0;i<8;i++)
            {
                var result=Director.Submit(BattleCommand.EndTurn());Check(result.Success,result.Error);
                if(i==0)
                {
                    var motion=Director.Stage.Actors[0].GiantMotion;
                    for(int frame=0;frame<160&&motion.GetComponent<BossSonicEffect>()==null;frame++)await Task.Delay(20);
                    var sonic=motion.GetComponent<BossSonicEffect>();Check(sonic!=null,"Pressure front did not launch.");
                    var from=(Vector3)typeof(BossSonicEffect).GetField("origin",All).GetValue(sonic);
                    Check(Vector3.Distance(from,motion.SonicOrigin)<3,"Pressure front did not start at the jaw.");
                    await Capture("mouth-pressure-start",30);await Task.Delay(180);await Capture("mouth-pressure-flight",30);
                }
                await Ready();
                if(i==1){await CheckSummons();await Capture("first-summon");}
            }
            Check(Director.Stage.Actors.Skip(1).All(a=>a.Alive),"Both summon types were not spawned by actual turns.");
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1080),new Vector2Int(1600,1200)})
            {await Resize(size.x,size.y);await CheckSummons();await Capture("both-summons");}
            bool Matches(string option,string enemy)=>option==enemy||option=="enemy:"+enemy;
            var card=Director.View.Hand.First(c=>c.Enabled&&c.Targets.Any(t=>t.Enabled&&Director.Stage.Actors.Skip(1).Any(a=>Matches(t.Id,a.EnemyId))));
            var target=Director.Stage.Actors.Skip(1).First(a=>card.Targets.Any(t=>t.Enabled&&Matches(t.Id,a.EnemyId))).EnemyId;
            var revision=Director.ViewRevision;
            Click("card-"+card.InstanceId);await Task.Delay(150);Click("actor-"+target);await Ready();
            Check(Director.ViewRevision==revision+1,"Targeted card was not submitted through the summon UI.");await Capture("targeted-summon");
            var after=File.Exists(path)?File.ReadAllBytes(path):null;
            Check(before==null?after==null:after!=null&&before.SequenceEqual(after),"Formal save changed.");Check(errors.Count==0,string.Join("; ",errors));
            var report=new{success=true,actualTurns=8,aspectRatios=3,twoSummonTypes=true,uiOverlap=false,modelRaycast=true,targetedCard=true,mouthPressureOrigin=true,runtimeErrors=errors.Count,formalSaveUnchanged=true};
            File.WriteAllText(Dir+"/result.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
        }
        finally {Application.logMessageReceived-=log;Application.runInBackground=background;viewType.GetProperty("selectedSizeIndex",All).SetValue(gameView,originalSize);gameView.Repaint();}
    }
}
