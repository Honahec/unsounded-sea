using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnsoundedSea.Runtime;
using UnsoundedSea.Core;

public static class FlagshipQA
{
    const string Dir="Logs/Flagship";
    static BattleDirector Director=>UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static async Task Ready(){for(int i=0;i<200&&(Director==null||Director.Busy);i++)await Task.Delay(50);Check(Director!=null&&!Director.Busy,"Battle remained locked");}
    public static string Start(){Directory.CreateDirectory(Dir);File.WriteAllText(Dir+"/result.txt","running");_=Run();return "Recording M04: Logs/Flagship/result.txt";}
    public static void Capture(string name,bool hud=false)
    {
        if(hud)
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(t=>t!=null);
            File.WriteAllBytes(Dir+"/"+name+".png",(byte[])type.GetMethod("EncodeScreenToPng",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{Screen.width,Screen.height}));return;
        }
        var camera=Director.Stage.battleCamera;var old=camera.targetTexture;var active=RenderTexture.active;
        var rt=RenderTexture.GetTemporary(1600,900,24);var image=new Texture2D(1600,900,TextureFormat.RGB24,false);
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1600,900),0,0);image.Apply();File.WriteAllBytes(Dir+"/"+name+".png",image.EncodeToPNG());}
        finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(image);}
    }
    static async Task Run()
    {
        try
        {
            Check(VoyageRuntime.IsValidationSession,"Use isolated validation first");await Ready();var d=Director;var s=d.Stage;
            var p=s.GetComponent<FlagshipPresentation>();var m=s.Actors[0].GiantMotion;
            Check(p!=null&&s.actorAnchors[0].IsChildOf(p.hull),"Body detached from wreck");
            Capture("idle");Capture("idle-hud",true);
            float contact=float.PositiveInfinity,meshContact=float.PositiveInfinity;int hits=0;
            m.VesselImpact+=()=>
            {
                contact=Vector3.Distance(m.PoleTip,m.StrikeTarget);hits++;
                var skin=m.GetComponentsInChildren<SkinnedMeshRenderer>().Single(r=>r.name=="FlagPole");var mesh=new Mesh();
                try{skin.BakeMesh(mesh,true);meshContact=mesh.vertices.Min(v=>Vector3.Distance(skin.transform.TransformPoint(v),m.StrikeTarget));}
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            };
            var result=d.Submit(BattleCommand.EndTurn());Check(result.Success,result.Error);
            bool captured=false;
            for(int i=0;i<30;i++){await Task.Delay(100);Capture("attack-"+i.ToString("D2"));if(hits>0&&!captured){Capture("contact");captured=true;}}
            await Ready();Check(contact<2&&meshContact<2&&hits==1,"Pole contact incorrect: socket="+contact+" mesh="+meshContact+" hits="+hits);
            m.PlayHit();await Task.Delay(450);Capture("hit");await Ready();
            var position=p.hull.position;var rotation=p.hull.rotation;var age=m.Age;d.SetPaused(true);await Task.Delay(300);
            Check(p.hull.position==position&&p.hull.rotation==rotation&&m.Age==age,"Paused wreck drifted");d.SetPaused(false);
            Check(p.StressBursts>=2,"No hull stress feedback");
            var attackReport="socket error="+contact+"m; visible mesh distance="+meshContact+"m; one contact; stress bursts="+p.StressBursts+"; shared hull parent; pause stable";
            // Exercise the public relay/lock route, without changing resources or encounter state.
            d.Restart();await Ready();p=d.Stage.GetComponent<FlagshipPresentation>();
            async Task Play(string id,string target)
            {
                var card=d.View.Hand.Single(c=>c.Definition.Id==id);
                var actual=card.Targets.Single(t=>t.Enabled&&(t.Id==target||t.Id=="scene:"+target));
                var r=d.Submit(BattleCommand.Play(card.InstanceId,actual.Id));Check(r.Success,r.Error);await Ready();
            }
            async Task End(){var r=d.Submit(BattleCommand.EndTurn());Check(r.Success,r.Error);await Ready();}
            await Play("E04-01","R1:positive");await End();
            await Play("E04-01","R2:reverse");await End();
            Check(d.View.BossStage==2,"Relay phase did not transition");Capture("locks-hud",true);
            await Play("E04-01","R1:off");await Play("E04-02","lock-I");await End();
            await Play("E04-01","R1:positive");await Play("E04-02","lock-II");await End();
            await Play("E04-01","R2:off");await Play("E04-02","lock-III");await Task.Delay(900);
            Check(d.View.BossStage==3&&p.WardStrength==0,"Open locks did not extinguish the ward");
            Capture("exposed");Capture("exposed-hud",true);
            var reduced=GameSettings.ReducedMotion;
            try
            {
                GameSettings.ReducedMotion=true;await Task.Delay(150);var rest=p.hull.localPosition;await Task.Delay(180);
                Check(p.Roll==0&&p.hull.localPosition==rest,"Reduced motion still moves wreck");
            }
            finally{GameSettings.ReducedMotion=reduced;}
            File.WriteAllText(Dir+"/result.txt","PASS M04: "+attackReport+"; public relay/three-lock path reached stage 3; ward fully extinguished; reduced-motion hull stable.");
        }
        catch(Exception e){File.WriteAllText(Dir+"/result.txt",e.ToString());}
    }
}
