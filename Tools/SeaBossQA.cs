using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Run through Unity CLI in Play mode. Exercises actual UI callbacks and the model-selection raycast.
public static class SeaBossQA
{
    static BattleDirector Director => UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
    static VisualElement Root => UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
    static void Check(bool pass,string message) {if(!pass)throw new Exception(message);}
    public static string Click(string id)
    {
        var button=Root.Q<Button>(id);Check(button!=null&&button.enabledInHierarchy,"Missing/disabled button "+id);
        button.Focus();using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}return id;
    }
    static async Task Ready()
    {
        for(int i=0;i<100;i++){if(Director!=null&&!Director.Busy)return;await Task.Delay(50);}
        throw new Exception("Feedback did not release.");
    }
    public static async Task<object> PlacementAndPause()
    {
        await Ready();var d=Director;var actor=d.Stage.Actors.Single();
        Check(actor.IsSeaActor,"Expected sea placement");Check(!actor.transform.IsChildOf(d.Stage.vessel),"Boss attached to vessel");
        Check(Root.Q<Button>("battle-task")==null,"Survival shows seal task");
        var position=actor.transform.position;var ship=d.Stage.vessel;var shipPosition=ship.localPosition;var shipRotation=ship.localRotation;
        try {ship.localPosition+=new Vector3(3,1,0);ship.localRotation*=Quaternion.Euler(0,0,15);Check(Vector3.Distance(position,actor.transform.position)<0.001f,"Vessel moves boss");}
        finally {ship.localPosition=shipPosition;ship.localRotation=shipRotation;}
        var hit=actor.GetComponent<BoxCollider>();var screen=d.Stage.battleCamera.WorldToScreenPoint(actor.transform.TransformPoint(hit.center));
        Check(Physics.Raycast(d.Stage.battleCamera.ScreenPointToRay(screen),out var rayHit)&&rayHit.collider==hit,"Boss hit volume missed");
        var point=RuntimePanelUtils.ScreenToPanel(Root.panel,new Vector2(screen.x,Screen.height-screen.y));var picked=Root.panel.Pick(point);
        Check(picked!=null&&picked.name=="battle-surface","Boss body occluded by UI");
        using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=point})){e.target=picked;picked.SendEvent(e);}
        Check(d.SelectedEnemy==actor.EnemyId,"Model raycast selection failed");
        Click("battle-pause");var body=actor.transform.GetChild(0);var pose=body.localPosition;var rotation=body.localRotation;
        await Task.Delay(200);Check(Vector3.Distance(pose,body.localPosition)<0.001f&&Quaternion.Angle(rotation,body.localRotation)<0.001f,"Pause did not freeze actor");
        Check(!d.Stage.ambience.isPlaying,"Pause did not stop ambience");Check(!d.Submit(BattleCommand.EndTurn()).Success,"Pause allowed input");Click("battle-resume");
        return State();
    }
    public static async Task<object> Victory()
    {
        await Ready();var d=Director;
        while(d.View.Phase==BattlePhase.PlayerTurn&&d.View.Turn<=5)
        {
            for(int i=0;i<3;i++)
            {
                var v=d.View;string effect=v.Enemies[0].Intent.Contains("理智")?"ward":"block";
                var card=v.Hand.Where(x=>x.Definition.Cost<=v.Actions).OrderByDescending(x=>x.Definition.Effects.Where(e=>e.Id==effect).Sum(e=>e.Amount)).FirstOrDefault(x=>x.Definition.Effects.Any(e=>e.Id==effect));
                if(card==null)break;Click("card-"+card.InstanceId);await Ready();
            }
            Click("battle-end-turn");await Ready();
        }
        Check(d.View.Phase==BattlePhase.Victory&&d.View.ObjectiveProgress==5,"Survival did not win");
        Check(d.View.Enemies[0].Health>0,"Boss was killed instead of escaped");return State();
    }
    public static async Task<object> ReducedMotion()
    {
        var previous=GameSettings.ReducedMotion;
        try
        {
            GameSettings.ReducedMotion=true;await Task.Delay(150);
            var stage=Director.Stage;var body=stage.Actors[0].transform.GetChild(0);var position=body.localPosition;var rotation=body.localRotation;var ship=stage.vessel.localRotation;
            await Task.Delay(200);
            Check(Vector3.Distance(position,body.localPosition)<0.001f&&Quaternion.Angle(rotation,body.localRotation)<0.001f,"Reduced motion did not stop Boss");
            Check(Quaternion.Angle(ship,stage.vessel.localRotation)<0.001f,"Reduced motion did not stop vessel");return State();
        }
        finally {GameSettings.ReducedMotion=previous;}
    }
    public static async Task<object> Defeat()
    {
        Click("battle-retry");await Task.Delay(200);var d=Director;
        for(int i=0;i<5&&d.View.Phase==BattlePhase.PlayerTurn;i++){await Ready();Click("battle-end-turn");await Ready();}
        Check(d.View.Phase==BattlePhase.Defeat,"Ignoring attacks did not lose");Check(!d.Submit(BattleCommand.Task()).Success,"Defeat allowed task");return State();
    }
    public static async Task<object> MenuRoundTrip()
    {
        Click("battle-menu");await Task.Delay(500);Check(Root.Q<Button>("menu-sea-boss")!=null,"No Boss menu entry");
        Click("menu-start");await Task.Delay(500);Check(Director.View.Enemies.Count==2,"Original battle entry broken");
        Click("battle-pause");Click("battle-menu");await Task.Delay(500);
        Click("menu-sea-boss");await Task.Delay(500);Check(Director.Stage.Actors.Single().IsSeaActor,"Menu did not load sea Boss");return State();
    }
    public static object State()
    {
        var d=Director;return new{frame=Time.frameCount,size=Screen.width+"x"+Screen.height,scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,phase=d.View.Phase.ToString(),turn=d.View.Turn,health=d.View.Health,sanity=d.View.Sanity,progress=d.View.ObjectiveProgress,bossHealth=d.View.Enemies[0].Health,actors=d.Stage.Actors.Count,cameras=Camera.allCamerasCount};
    }
}
