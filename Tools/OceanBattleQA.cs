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
    public static async Task<object> EndTurnStability()
    {
        Check(EditorApplication.isPlaying, "Enter Play Mode before checking the HUD");
        var d = UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
        d.Restart(); await Task.Delay(300); await Ready();
        var root = d.GetComponent<UIDocument>().rootVisualElement;
        int turn = d.View.Turn;
        System.IO.Directory.CreateDirectory("Logs/HitAndHud");
        ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Logs/HitAndHud/turn-before.png"));
        await Task.Delay(80);
        Click("battle-end-turn");
        var end = root.Q<Button>("battle-end-turn");
        var hand = root.Q<ScrollView>("hand");
        var plate = root.Q<Button>(className: "actor-label");
        Check(!end.enabledInHierarchy, "Feedback did not lock end turn");
        var firstCard = hand.Q<Button>(className: "card-anchor");
        float minX = float.MaxValue, maxX = float.MinValue, minOpacity = 1;
        for (int frame = 0; frame < 110; frame++)
        {
            await Task.Delay(25);
            Check(root.Q<Button>("battle-end-turn") == end && root.Q<ScrollView>("hand") == hand &&
                root.Q<Button>(className: "actor-label") == plate, "Feedback completion rebuilt the HUD");
            var rect = plate.worldBound;
            Check(!float.IsNaN(rect.x) && rect.y >= 63 && rect.width > 100, "Nameplate lost its layout");
            if (firstCard != null) { minX = Mathf.Min(minX, firstCard.worldBound.x); maxX = Mathf.Max(maxX, firstCard.worldBound.x); }
            minOpacity = Mathf.Min(minOpacity, end.resolvedStyle.opacity);
            if (frame < 4 || frame == 8 || frame == 40 || frame == 100)
                ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath($"Logs/HitAndHud/turn-{frame:000}.png"));
        }
        Check(!d.Busy && end.enabledInHierarchy, "Feedback did not restore end turn");
        Check(d.View.Turn == turn + 1, "End turn advanced more than once");
        Check(maxX - minX < 2, "Hand shifted during feedback: " + (maxX - minX));
        Check(minOpacity > 0.95f, "Temporary feedback flashed the button dim");
        return new { turn = d.View.Turn, stableElements = true, handDrift = maxX - minX, minOpacity };
    }

    public static async Task<object> HudFlow()
    {
        var d = UnityEngine.Object.FindAnyObjectByType<BattleDirector>(); d.Restart();
        await Task.Delay(250); await Ready();
        VisualElement Root() => d.GetComponent<UIDocument>().rootVisualElement;
        void Values()
        {
            Check(Root().Q<Label>("battle-health").text == $"{d.View.Health} / {d.scenario.encounter.playerHealth}", "Incorrect health");
            Check(Root().Q<Label>("battle-sanity").text == $"{d.View.Sanity} / {d.scenario.encounter.playerSanity}", "Incorrect sanity");
            Check(Root().Q<Label>("battle-actions").text == $"{d.View.Actions} / {d.scenario.encounter.actionsPerTurn}", "Incorrect actions");
        }
        Values();
        var hand = Root().Q<ScrollView>("hand").worldBound;
        foreach (var cls in new[] { "battle-vitals", "tools", "turn-controls" })
        {
            var rect = Root().Q(className: cls).worldBound;
            Check(!rect.Overlaps(hand), "HUD overlaps hand: " + cls);
            Check(rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= Root().worldBound.xMax + 1 && rect.yMax <= Root().worldBound.yMax + 1, "HUD outside view: " + cls);
        }
        foreach (var cls in new[] { "tool", "end-turn", "task-button", "sea-button" })
            foreach (var button in Root().Query<Button>(className: cls).ToList())
            {
                Check(button.worldBound.height >= 34, "Small HUD target: " + button.name);
                var picked = Root().panel.Pick(button.worldBound.center);
                Check(picked == button || button.Contains(picked), "HUD target occluded: " + button.name);
            }
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        System.IO.Directory.CreateDirectory("Logs/BattleUi");
        ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath($"Logs/BattleUi/{scene}-{Screen.width}x{Screen.height}.png"));
        await Task.Delay(100);
        Click("battle-pause"); await Task.Delay(80);
        Check(d.Paused && Root().Q(className: "pile-summary") != null, "Journal missing pile totals");
        var stageAge = typeof(BattleStage).GetField("age", BindingFlags.Instance | BindingFlags.NonPublic);
        float SeaTime() => d.Stage.water != null ? d.Stage.water.SeaTime : (float)stageAge.GetValue(d.Stage);
        float seaTime = SeaTime(); await Task.Delay(150);
        Check(SeaTime() == seaTime, "Pause did not freeze environment");
        Click("battle-settings"); await Task.Delay(80);
        Check(Root().Q<Button>("settings-back") != null, "Settings unavailable");
        Click("settings-back"); Click("battle-resume");
        Check(!d.Paused, "Resume failed");
        Click("battle-chart-toggle"); await Task.Delay(80); Check(d.Paused, "Chart did not pause");
        Click("battle-chart-close"); Check(!d.Paused, "Chart did not resume");
        var target = d.View.Enemies.First(x => x.Health > 0); Click("actor-" + target.Id);
        var tool = d.View.Tools.FirstOrDefault(x => x.RemainingUses > 0 && x.Definition.SanityCost < d.View.Sanity);
        if (tool != null)
        {
            int uses = tool.RemainingUses; Click("tool-" + tool.Definition.Id); await Ready();
            Check(d.View.Tools.First(x => x.Definition.Id == tool.Definition.Id).RemainingUses == uses - 1, "Tool did not consume once");
        }
        var protection = d.View.Hand.FirstOrDefault(x => x.Definition.Effects.Any(e => e.Id == "block" || e.Id == "ward") && x.Definition.Cost <= d.View.Actions);
        if (protection != null)
        {
            Click("card-" + protection.InstanceId); await Ready(); Values();
            Check(d.View.Block > 0 || d.View.Ward > 0, "Protection not applied");
            Check(Root().Q<Label>("battle-health-guard").text == (d.View.Block > 0 ? $"格挡  {d.View.Block}" : ""), "Incorrect block label");
            Check(Root().Q<Label>("battle-sanity-guard").text == (d.View.Ward > 0 ? $"防护  {d.View.Ward}" : ""), "Incorrect ward label");
        }
        bool hasTask = Root().Q<Button>("battle-task") != null;
        if (hasTask)
        {
            int progress = d.View.ObjectiveProgress; Click("battle-task"); await Ready();
            Check(d.View.ObjectiveProgress == progress + 1 && !Root().Q<Button>("battle-task").enabledInHierarchy, "Task not once per turn");
        }
        Values(); await Task.Delay(100);
        ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath($"Logs/BattleUi/{scene}-protection-{Screen.width}.png"));
        await Task.Delay(80);
        int turn = d.View.Turn; Click("battle-end-turn"); await Ready();
        Check(d.View.Turn == turn + 1, "End turn did not advance once"); Values();
        return new { scene, screen = Screen.width + "x" + Screen.height, hasTask, hitTargets = true, stats = true, pauseSettingsChart = true, tool = tool?.Definition.Id, protection = protection?.Definition.Id, turn = d.View.Turn };
    }
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
        var actor=d.Stage.Actors[1];var viewport=d.Stage.battleCamera.WorldToViewportPoint(actor.transform.position+Vector3.up);
        var point=root.LocalToWorld(new Vector2(viewport.x*root.resolvedStyle.width,(1-viewport.y)*root.resolvedStyle.height));var picked=root.panel.Pick(point);
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
