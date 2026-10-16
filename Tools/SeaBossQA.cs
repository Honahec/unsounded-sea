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
    public static async Task<object> ExteriorComposition(bool duringAttack=false)
    {
        await Ready();var stage=Director.Stage;var camera=stage.battleCamera;
        Check(!camera.transform.IsChildOf(stage.vessel),"Camera inherits vessel motion");
        var cameraPosition=camera.transform.position;var cameraRotation=camera.transform.rotation;
        var shipPosition=stage.vessel.localPosition;var shipRotation=stage.vessel.localRotation;
        try
        {
            stage.vessel.localPosition+=Vector3.up;stage.vessel.Rotate(0,0,10);
            Check(Vector3.Distance(cameraPosition,camera.transform.position)<0.001f&&Quaternion.Angle(cameraRotation,camera.transform.rotation)<0.001f,"Vessel moves camera");
        }
        finally {stage.vessel.localPosition=shipPosition;stage.vessel.localRotation=shipRotation;}
        if(duringAttack)Click("battle-end-turn");
        float minX=1,maxX=0,minY=1,maxY=0;
        for(int frame=0;frame<30;frame++)
        {
            var bounds=new Bounds(stage.vessel.position,Vector3.zero);
            foreach(var renderer in stage.vessel.GetComponentsInChildren<Renderer>())bounds.Encapsulate(renderer.bounds);
            for(int corner=0;corner<8;corner++)
            {
                var world=new Vector3((corner&1)==0?bounds.min.x:bounds.max.x,Mathf.Max(0,(corner&2)==0?bounds.min.y:bounds.max.y),(corner&4)==0?bounds.min.z:bounds.max.z);
                var p=camera.WorldToViewportPoint(world);
                Check(p.z>camera.nearClipPlane&&p.x>0&&p.x<1&&p.y>0&&p.y<1,"Vessel cropped by camera");
                var ui=Root.LocalToWorld(new Vector2(p.x*Root.resolvedStyle.width,(1-p.y)*Root.resolvedStyle.height));
                Check(Root.panel.Pick(ui)?.name=="battle-surface","Vessel occluded by HUD");
                minX=Mathf.Min(minX,p.x);maxX=Mathf.Max(maxX,p.x);minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);
            }
            await Task.Delay(50);
        }
        var actor=stage.Actors.Single();var separation=Vector2.Distance(new Vector2(stage.vessel.position.x,stage.vessel.position.z),new Vector2(actor.transform.position.x,actor.transform.position.z));
        Check(separation>=250,"Sea engagement is too close");
        return new {size=camera.pixelWidth+"x"+camera.pixelHeight,separation,duringAttack,shipViewport=new[]{minX,minY,maxX,maxY},cameraIndependent=true};
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
        var viewport=d.Stage.battleCamera.WorldToViewportPoint(actor.transform.TransformPoint(hit.center));
        var point=Root.LocalToWorld(new Vector2(viewport.x*Root.resolvedStyle.width,(1-viewport.y)*Root.resolvedStyle.height));var picked=Root.panel.Pick(point);
        Check(picked!=null&&picked.name=="battle-surface","Boss body occluded by UI");
        using(var e=PointerDownEvent.GetPooled(new Event{type=EventType.MouseDown,button=0,mousePosition=point})){e.target=picked;picked.SendEvent(e);}
        Check(d.SelectedEnemy==actor.EnemyId,"Model raycast selection failed");
        Click("battle-pause");var body=actor.transform.GetChild(0);var pose=body.localPosition;var rotation=body.localRotation;
        await Task.Delay(200);Check(Vector3.Distance(pose,body.localPosition)<0.001f&&Quaternion.Angle(rotation,body.localRotation)<0.001f,"Pause did not freeze actor");
        Check(!d.Stage.ambience.isPlaying,"Pause did not stop ambience");Check(!d.Submit(BattleCommand.EndTurn()).Success,"Pause allowed input");Click("battle-resume");
        return State();
    }
    public static async Task<object> SeaMotion()
    {
        Director.Restart();await Task.Delay(500);
        var d=Director;var s=d.Stage;var origin=s.vessel.position;var min=origin;var max=origin;
        var camera=s.battleCamera.transform.position;float maxTilt=0;float lastTime=s.water.SeaTime;
        Check(s.vesselFollowsWater&&!s.placeholderCombatMotion,"Expected continuous sea motion without placeholder combat animation");
        for(int i=0;i<100;i++)
        {
            if(i==20||i==60)
            {
                var health=d.View.Health;var sanity=d.View.Sanity;
                Click("battle-end-turn");
                Check(d.View.Health<health||d.View.Sanity<sanity,"Attack did not resolve damage");
            }
            await Task.Delay(100);
            Check(s.water.SeaTime>lastTime,"Wave clock stalled during battle");lastTime=s.water.SeaTime;
            var p=s.vessel.position;min=Vector3.Min(min,p);max=Vector3.Max(max,p);
            Check(new Vector2(p.x,p.z).magnitude<3,"Vessel drift escaped its bounded area");
            float pitch=Mathf.Abs(Mathf.DeltaAngle(0,s.vessel.localEulerAngles.x)),roll=Mathf.Abs(Mathf.DeltaAngle(0,s.vessel.localEulerAngles.z));
            Check(pitch<=s.maxWavePitch+0.1f&&roll<=s.maxWaveRoll+0.1f,"Vessel tilt exceeded configured limit");
            maxTilt=Mathf.Max(maxTilt,pitch,roll);
            Check(Vector3.Distance(camera,s.battleCamera.transform.position)<0.001f,"Placeholder hit shook camera");
            Check(Mathf.Abs(Mathf.DeltaAngle(0,s.Actors[0].transform.GetChild(0).localEulerAngles.x))<0.001f,"Placeholder hit pitched Boss");
            if(i==24||i==64)Check(!d.Busy,"Still waiting for placeholder attack animation");
        }
        var range=max-min;Check(range.y>1&&new Vector2(range.x,range.z).magnitude>1&&maxTilt>1,"Sea/vessel motion is too static");
        d.SetPaused(true);var pose=s.vessel.position;var rotation=s.vessel.rotation;lastTime=s.water.SeaTime;
        await Task.Delay(250);
        Check(s.water.SeaTime==lastTime&&Vector3.Distance(pose,s.vessel.position)<0.001f&&Quaternion.Angle(rotation,s.vessel.rotation)<0.001f,"Pause did not freeze sea and vessel");
        d.SetPaused(false);
        return new {sampleSeconds=10,range,maxTilt,continuousDuringCommands=true,pausedTogether=true,placeholderCombatMotion=false};
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
        var d=Director;return new{frame=Time.frameCount,size=d.Stage.battleCamera.pixelWidth+"x"+d.Stage.battleCamera.pixelHeight,scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,phase=d.View.Phase.ToString(),turn=d.View.Turn,health=d.View.Health,sanity=d.View.Sanity,progress=d.View.ObjectiveProgress,bossHealth=d.View.Enemies[0].Health,actors=d.Stage.Actors.Count,cameras=Camera.allCamerasCount};
    }
}
