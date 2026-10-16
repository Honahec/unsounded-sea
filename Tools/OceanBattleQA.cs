using System;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Runtime;
public static class OceanBattleQA
{
    public static string Resolution(int width,int height)
    {
        var asm=typeof(UnityEditor.Editor).Assembly;var type=asm.GetType("UnityEditor.GameView");var view=EditorWindow.GetWindow(type);
        var sizesType=asm.GetType("UnityEditor.GameViewSizes");var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);var sizes=singleton.GetProperty("instance").GetValue(null);
        var groupType=sizesType.GetProperty("currentGroupType").GetValue(sizes);var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new[]{groupType});
        var sizeType=asm.GetType("UnityEditor.GameViewSize");var enumType=asm.GetType("UnityEditor.GameViewSizeType");
        var custom=Activator.CreateInstance(sizeType,new[]{Enum.ToObject(enumType,1),(object)width,height,"Ocean QA "+width+"x"+height});
        var texts=(string[])group.GetType().GetMethod("GetDisplayTexts").Invoke(group,null);int index=Array.FindIndex(texts,x=>x.StartsWith("Ocean QA "+width+"x"+height));
        if(index<0) {group.GetType().GetMethod("AddCustomSize").Invoke(group,new[]{custom});index=texts.Length;}
        type.GetProperty("selectedSizeIndex",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SetValue(view,index);view.Repaint();return "Selected "+width+"x"+height;
    }
    public static string Click(string id)
    {
        var root=UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;var button=root.Q<Button>(id);
        if(button==null||!button.enabledInHierarchy) throw new Exception("Missing/disabled button: "+id);
        button.Focus();using(var evt=NavigationSubmitEvent.GetPooled()) {evt.target=button;button.SendEvent(evt);}return id;
    }
    static async Task Ready()
    {
        for(int i=0;i<100;i++) {var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();if(!d.Busy)return;await Task.Delay(50);}
        throw new Exception("Feedback lock did not release.");
    }
    static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    public static async Task<object> VictoryFlow()
    {
        var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();await Ready();
        Click("battle-pause");await Task.Delay(80);
        Check(d.Paused,"Pause failed");Check(!d.Submit(UnsoundedSea.Core.BattleCommand.EndTurn()).Success,"Pause accepted gameplay");
        Check(!d.Stage.ambience.isPlaying,"Ambient sound did not pause");
        Click("battle-retry");await Task.Delay(150);
        Check(d.View.Turn==1&&d.View.ObjectiveProgress==0,"Retry state leaked");
        Click("target-echo:1");Check(d.SelectedEnemy=="echo:1","Target selection failed");
        Click("card-10");await Ready();
        Check(d.View.Enemies[1].Health==995&&d.View.Enemies[0].Health==30,"Damage hit wrong target");
        Click("target-prayer:0");Click("tool-lantern");await Ready();
        Check(d.View.Enemies[0].IntentConfirmed&&d.View.Tools[0].RemainingUses==0,"Investigation failed");
        for(int i=0;i<4;i++)
        {
            Click("battle-task");await Ready();
            if(i<3){Click("battle-end-turn");await Ready();}
        }
        Check(d.View.Phase==UnsoundedSea.Core.BattlePhase.Victory,"Four seals did not win");
        return State();
    }
    public static async Task<object> DefeatFlow()
    {
        Click("battle-retry");await Task.Delay(150);
        var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
        for(int i=0;i<40&&d.View.Phase==UnsoundedSea.Core.BattlePhase.PlayerTurn;i++){await Ready();Click("battle-end-turn");await Ready();}
        Check(d.View.Phase==UnsoundedSea.Core.BattlePhase.Defeat,"No defeat");
        Check(!d.Submit(UnsoundedSea.Core.BattleCommand.Task()).Success,"Terminal session accepted task");
        return State();
    }
    public static async Task<object> SwapAndPick()
    {
        Click("battle-retry");await Task.Delay(180);
        var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();var original=d.scenario;
        var alternate=ScriptableObject.Instantiate(original);
        var alternateStage=UnityEngine.Object.Instantiate(original.stagePrefab);alternateStage.gameObject.SetActive(false);
        alternateStage.name="QA alternate stage";alternate.stagePrefab=alternateStage;
        alternate.actorOverrides=new[]{original.ActorPrefab(1),original.ActorPrefab(0)};
        try
        {
            d.scenario=alternate;d.Restart();d.Stage.gameObject.SetActive(true);await Task.Delay(180);
            Check(d.Stage.name.StartsWith("QA alternate stage"),"Stage swap ignored");
            Check(d.Stage.Actors[0].transform.GetChild(0).name.StartsWith("Echo"),"Actor override ignored");
        }
        finally {d.scenario=original;UnityEngine.Object.Destroy(alternateStage.gameObject);UnityEngine.Object.Destroy(alternate);d.Restart();}
        await Task.Delay(200);
        var root=UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
        var actor=d.Stage.Actors[1];var screen=d.Stage.battleCamera.WorldToScreenPoint(actor.transform.position+Vector3.up);
        var point=RuntimePanelUtils.ScreenToPanel(root.panel,new Vector2(screen.x,Screen.height-screen.y));var picked=root.panel.Pick(point);
        Check(picked!=null&&picked.name=="battle-surface","Model click occluded by UI");
        using(var evt=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=point})){evt.target=picked;picked.SendEvent(evt);}
        Check(d.SelectedEnemy==actor.EnemyId,"3D pointer selection failed");
        return State();
    }
    public static async Task<object> MenuAndMotion()
    {
        var previous=GameSettings.ReducedMotion;
        try
        {
            Click("motion-1");Click("settings-back");Click("battle-resume");await Task.Delay(200);
            var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();var pose=d.Stage.vessel.localRotation;
            await Task.Delay(250);Check(Quaternion.Angle(pose,d.Stage.vessel.localRotation)<0.001f,"Reduced motion did not stop ship roll");
        }
        finally {GameSettings.ReducedMotion=previous;}
        Click("battle-pause");Click("battle-menu");await Task.Delay(250);
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="MainMenu","Return to title failed");
        Click("menu-start");await Task.Delay(350);
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="OceanBattle","Title did not load new battle");
        return State();
    }
    public static object State()
    {
        var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();return new{frame=Time.frameCount,size=Screen.width+"x"+Screen.height,turn=d.View.Turn,phase=d.View.Phase.ToString(),health=d.View.Health,sanity=d.View.Sanity,progress=d.View.ObjectiveProgress,selected=d.SelectedEnemy,paused=d.Paused,actors=d.Stage.Actors.Count,cameras=Camera.allCamerasCount,hand=d.View.Hand.Select(c=>new{id=c.InstanceId,name=c.Definition.Name}).ToArray()};
    }
}
