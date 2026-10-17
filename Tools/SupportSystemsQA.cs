using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Play Mode smoke checks use debug sessions; the player's saved voyage is untouched.
public static class SupportSystemsQA
{
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    static VisualElement Root=>UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
    static void Click(string id)
    {
        var button=Root.Q<Button>(id);Check(button!=null&&button.enabledInHierarchy,"Missing/disabled "+id);
        button.Focus();using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}
    }
    static async Task Wait(Func<bool> condition)
    {
        for(var i=0;i<100;i++){if(condition())return;await Task.Delay(100);}throw new Exception("Timed out waiting for scene/UI.");
    }
    static string SaveContents()=>File.Exists(VoyageRuntime.SavePath)?File.ReadAllText(VoyageRuntime.SavePath):null;
    public static async Task<object> Manifest()
    {
        Check(Application.isPlaying,"Enter Play Mode first.");var saved=SaveContents();
        VoyageRuntime.StartDebugNode("fog",RunNodeKind.Supply);
        await Task.Delay(250); // LoadScene completes on the next frame; do not target the outgoing HUD.
        await Wait(()=>UnityEngine.Object.FindAnyObjectByType<VoyageHud>()!=null&&Root.Q<Button>("voyage-support-manifest")!=null);
        Click("voyage-support-manifest");await Task.Delay(200);
        var sheet=Root.Q(className:"action-book");Check(sheet!=null&&sheet.worldBound.width>500,"Manifest did not lay out.");
        var choice=VoyageRuntime.Session.GetSupportChoices().First(x=>x.Id.StartsWith("buy:")&&x.Enabled&&SupportCatalog.Item(x.Id.Substring(4)).Kind==ItemKind.Consumable);
        var before=VoyageRuntime.Session.Supplies;Click("support-"+choice.Id);Check(VoyageRuntime.Session.Supplies==before-1,"Purchase did not charge exactly once.");
        Check(!VoyageRuntime.Session.ManageSupport(choice.Id),"Repeated purchase accepted.");
        Click("support-hire:listener");Check(VoyageRuntime.Session.CrewIds.Count==2&&VoyageRuntime.Session.Supplies==0,"Hiring resources/roster mismatch.");
        var scroll=Root.Q(className:"action-book").Q<ScrollView>();scroll.scrollOffset=Vector2.zero;
        Check(saved==SaveContents(),"Debug UI changed saved voyage.");
        return new { inventory=VoyageRuntime.Session.Inventory.Count,crew=VoyageRuntime.Session.CrewIds,supplies=VoyageRuntime.Session.Supplies,saveUnchanged=true };
    }
    public static async Task<object> Battle()
    {
        Check(Application.isPlaying,"Enter Play Mode first.");var saved=SaveContents();
        VoyageRuntime.StartDebugEncounter("voyage-fog-0","fog",false);
        await Task.Delay(250);
        await Wait(()=>UnityEngine.Object.FindAnyObjectByType<BattleDirector>()?.View!=null&&Root.Q<Button>("battle-end-turn")!=null);
        var director=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
        await Wait(()=>!director.Busy);
        // Debug selection uses the fixed starter without the voyage loadout.
        // Bind the ordinary voyage construction path without writing a real save.
        var battle=director.scenario.CreateSession(director.seed,voyage:VoyageRuntime.Session);
        typeof(BattleDirector).GetField("session",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(director,battle);
        typeof(OceanBattleHud).GetMethod("Render",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(UnityEngine.Object.FindAnyObjectByType<OceanBattleHud>(),null);
        for(var i=0;i<3&&director.View.Health==60;i++){Click("battle-end-turn");await Wait(()=>!director.Busy);}
        var health=director.View.Health;Check(health<60,"Encounter did not damage player.");
        Click("supply-i10");
        Check(director.View.SpentConsumables.Count==1&&director.View.Health==Math.Min(60,health+8),"Medicine mismatch.");
        Check(!Root.Q<Button>("supply-i10").enabledInHierarchy,"Spent medicine stayed enabled.");
        await Wait(()=>!director.Busy);
        Click("crew-medic");Check(director.View.Supports.Single().Used,"Crew use not recorded.");
        await Wait(()=>!director.Busy);
        Click("battle-end-turn");Check(!Root.Q<Button>("battle-end-turn").enabledInHierarchy,"Feedback lock missing.");
        await Wait(()=>!director.Busy);Check(Root.Q<Button>("battle-end-turn").enabledInHierarchy,"Feedback lock did not release.");
        Check(saved==SaveContents(),"Debug battle changed saved voyage.");
        return new { turn=director.View.Turn,consumed=director.View.SpentConsumables,crewUsed=true,saveUnchanged=true };
    }
}
