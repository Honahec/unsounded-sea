using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;
using UnsoundedSea.Core;
public static class GiantBossQA
{
    static BattleDirector Director()
    {if(!EditorApplication.isPlaying)throw new Exception("Play mode required");return UnityEngine.Object.FindAnyObjectByType<BattleDirector>();}
    public static string Select(int index)
    {
        var d=Director();d.scenario=AssetDatabase.LoadAssetAtPath<BattleScenario>("Assets/UnsoundedSea/Content/Scenarios/M0"+(index+2)+".asset");d.Restart();
        return "Selected M0"+(index+2);
    }
    public static string Snapshot(int index)
    {
        var d=Director();var actor=d.Stage.Actors[0];var motion=actor.GiantMotion;
        Capture(d.Stage.battleCamera,"Logs/GiantBosses/M0"+(index+2)+"-idle.png");
        var ship=d.Stage.battleCamera.WorldToViewportPoint(d.Stage.vessel.position);var origin=d.Stage.battleCamera.WorldToViewportPoint(motion.Origin);
        return JsonUtility.ToJson(new SnapshotInfo{frame=Time.frameCount,bones=motion.joints.Length,ship=ship,origin=origin});
    }
    [Serializable]class SnapshotInfo{public int frame,bones;public Vector3 ship,origin;}
    public static async Task<string> Record(int index)
    {
        var d=Director();Select(index);await Task.Delay(400);d=Director();
        var maxParticles=0;float low=float.PositiveInfinity,high=float.NegativeInfinity;
        var initial=Time.frameCount;var m=d.Stage.Actors[0].GiantMotion;var prefix="Logs/GiantBosses/M0"+(index+2);Directory.CreateDirectory(prefix);
        if(index==3)
        {
            var card=d.View.Hand.Single(c=>c.Definition.Id=="E05-01");var prepare=d.Submit(BattleCommand.Play(card.InstanceId,"winch"));if(!prepare.Success)throw new Exception(prepare.Error);
            await Task.Delay(200);
        }
        // Two real enemy turns include a physical strike and a summon/ritual when applicable.
        for(int turn=0;turn<2;turn++)
        {
            var result=d.Submit(BattleCommand.EndTurn());if(!result.Success)throw new Exception(result.Error);
            for(int i=0;i<42;i++)
            {maxParticles=Mathf.Max(maxParticles,m.GetComponent<GiantBossEffects>().ParticleCount);low=Mathf.Min(low,d.Stage.vessel.position.y);high=Mathf.Max(high,d.Stage.vessel.position.y);Capture(d.Stage.battleCamera,prefix+"/turn"+turn+"-"+i.ToString("D3")+".png");await Task.Delay(90);}
            if(d.Busy)throw new Exception("Busy did not release");
        }
        if(maxParticles==0)throw new Exception("No visible particle population");
        if(Time.frameCount<=initial+20)throw new Exception("Game did not advance");
        var age=m.Age;var waterTime=d.Stage.water.SeaTime;d.SetPaused(true);await Task.Delay(180);
        if(m.Age!=age||d.Stage.water.SeaTime!=waterTime)throw new Exception("Pause drift");d.SetPaused(false);
        return "M0"+(index+2)+": "+(Time.frameCount-initial)+" frames, releases="+m.ReleaseCount+", water impacts="+d.Stage.water.ImpactCount+", max particles="+maxParticles+", ship vertical range="+(high-low).ToString("F2")+", pause passed, turn="+d.View.Turn;
    }
    public static async Task<string> Hit(int index)
    {
        var d=Director();Select(index);await Task.Delay(400);d=Director();var m=d.Stage.Actors[0].GiantMotion;
        var prefix="Logs/GiantBosses/M0"+(index+2)+"/hit";Directory.CreateDirectory(prefix);
        // Diagnostic only for untargetable bodies; gameplay instead uses objective/anchor disruption.
        m.PlayHit(index<2?1:.8f);
        for(int i=0;i<30;i++){Capture(d.Stage.battleCamera,prefix+"/"+i.ToString("D3")+".png");await Task.Delay(65);}
        return "Hit overlay recorded";
    }
    public static async Task<string> EffectsProbe()
    {
        Select(3);await Task.Delay(300);var d=Director();var m=d.Stage.Actors[0].GiantMotion;
        m.PlayAttack(true);await Task.Delay(2500);d.SetPaused(true);await Task.Delay(80);
        var ps=m.GetComponentInChildren<ParticleSystem>();var renderer=ps.GetComponent<ParticleSystemRenderer>();
        Capture(d.Stage.battleCamera,"Logs/GiantBosses/effects-on.png");renderer.enabled=false;
        Capture(d.Stage.battleCamera,"Logs/GiantBosses/effects-off.png");renderer.enabled=true;
        var points=new ParticleSystem.Particle[ps.particleCount];ps.GetParticles(points);
        var info="particles="+ps.particleCount+", bounds="+renderer.bounds+", origin="+m.Origin+", first="+(points.Length>0?points[0].position.ToString():"none")+", scale="+ps.transform.lossyScale;
        d.SetPaused(false);return info;
    }
    public static void Capture(Camera camera,string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));var rt=RenderTexture.GetTemporary(960,540,24);var old=camera.targetTexture;var active=RenderTexture.active;
        Texture2D image=null;
        try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;image=new Texture2D(960,540,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,960,540),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());}
        finally{camera.targetTexture=old;RenderTexture.active=active;RenderTexture.ReleaseTemporary(rt);if(image!=null)UnityEngine.Object.DestroyImmediate(image);}
    }
}
