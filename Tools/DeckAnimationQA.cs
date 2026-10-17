// Unity CLI run_script in OceanBattle Play Mode: .Gallery(page[, recordName]) or .Live().
// Gallery uses Unity BakeMesh for deterministic pose captures; Live verifies the real player loop.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

public static class DeckAnimationQA
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    public static object Gallery(int page = 0, string recordName = null)
    {
        Check(Application.isPlaying, "Gallery requires OceanBattle Play Mode");
        var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
        Check(d!=null&&d.Stage!=null,"A live battle stage is required");
        var wasPaused=d.Paused;d.SetPaused(true);
        Directory.CreateDirectory("Logs/DeckAnimation/Gallery");
        var paths=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/UnsoundedSea/Art/Prefabs/CuratedRigs"})
            .Select(AssetDatabase.GUIDToAssetPath).Concat(new[]{"Assets/UnsoundedSea/Art/Prefabs/Prayer.prefab","Assets/UnsoundedSea/Art/Prefabs/Echo.prefab"}).OrderBy(p=>Path.GetFileName(p)).ToArray();
        paths=recordName==null ? paths.Skip(page*9).Take(9).ToArray() : paths.Where(p=>Path.GetFileNameWithoutExtension(p)==recordName).ToArray();
        var camObject=new GameObject("Deck motion QA camera");var cam=camObject.AddComponent<Camera>();
        cam.enabled=false;cam.fieldOfView=27;cam.nearClipPlane=.1f;cam.farClipPlane=30;
        cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.12f,.15f,.16f);
        var lightObject=new GameObject("Deck motion QA light");var light=lightObject.AddComponent<Light>();
        light.type=LightType.Directional;light.intensity=2;light.transform.rotation=Quaternion.Euler(25,150,0);
        var previousFog=RenderSettings.fog;RenderSettings.fog=false;
        var rt=new RenderTexture(320,400,24);cam.targetTexture=rt;
        var results=new System.Collections.Generic.List<object>();
        try
        {
            foreach(var path in paths)
            {
                var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                try
                {
                    go.transform.position=new Vector3(0,1000,0);
                    var motion=go.GetComponent<DeckActorMotion>();motion.Initialize(go.name);
                    var bounds=BattleActorView.MeasureLocalBounds(go.transform);var h=bounds.size.y;
                    // Camera.Render alone reuses same-frame skin matrices. Bake the actual Unity
                    // skin for each deterministic pose so sequential samples show distinct geometry.
                    var skin=go.GetComponentInChildren<SkinnedMeshRenderer>();
                    var baked=new Mesh();var display=new GameObject("Sampled Unity skin");display.transform.SetParent(skin.transform,false);
                    display.AddComponent<MeshFilter>().sharedMesh=baked;
                    display.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;skin.enabled=false;
                    var center=go.transform.TransformPoint(bounds.center);
                    cam.transform.position=center+new Vector3(h*.28f,h*.08f,h*2.9f);cam.transform.LookAt(center);
                    foreach(var state in recordName==null ? new[]{"idle","windup","attack","hit"} : Enumerable.Range(0,60).Select(i=>i.ToString("000")))
                    {
                        if(recordName!=null)
                        {
                            int frame=int.Parse(state);
                            if(frame==8)motion.PlayAttack(false,false);
                            if(frame==40)motion.PlayHit();
                            motion.Tick(.06f,false,false);
                        }
                        if(state=="idle")motion.Tick(.7f,false,false);
                        if(state=="windup"){motion.PlayAttack(false,false);motion.Tick(motion.attackDuration*.23f,false,false);}
                        if(state=="attack")motion.Tick(motion.attackDuration*.20f,false,false);
                        if(state=="hit"){motion.Tick(3,false,false);motion.PlayHit();motion.Tick(.1f,false,false);}
                        skin.BakeMesh(baked,true);
                        cam.Render();var prior=RenderTexture.active;RenderTexture.active=rt;
                        var output=new Texture2D(320,400,TextureFormat.RGB24,false);
                        output.ReadPixels(new Rect(0,0,320,400),0,0);output.Apply();RenderTexture.active=prior;
                        File.WriteAllBytes("Logs/DeckAnimation/Gallery/"+Path.GetFileNameWithoutExtension(path)+"-"+state+".png",output.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(output);
                    }
                    UnityEngine.Object.DestroyImmediate(baked);
                    results.Add(new {path,height=h,style=motion.style.ToString(),joints=motion.DrivenJointCount});
                } finally {UnityEngine.Object.DestroyImmediate(go);}
            }
        }
        finally
        {
            cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camObject);UnityEngine.Object.DestroyImmediate(lightObject);
            RenderSettings.fog=previousFog;d.SetPaused(wasPaused);
        }
        return results;
    }
    public static async Task<object> Live()
    {
        Check(Application.isPlaying,"Enter OceanBattle Play Mode");
        var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
        Check(d!=null&&d.Stage!=null,"Open OceanBattle before Live QA");
        var priorReduced=GameSettings.ReducedMotion;
        Directory.CreateDirectory("Logs/DeckAnimation/Live");
        try
        {
            GameSettings.ReducedMotion=false;
            d.Restart();await Task.Delay(400);
            var frame=Time.frameCount;var motion=d.Stage.Actors[0].GetComponentInChildren<DeckActorMotion>();
            var age=motion.Age;var water=d.Stage.water;var sea=water.SeaTime;await Task.Delay(180);
            Check(Time.frameCount>frame&&motion.Age>age&&water.SeaTime>sea,"Player loop/idle/water did not advance");
            Check(water.Reflections!=null&&water.Reflections.sourceCamera==d.Stage.battleCamera,"Reflection camera binding missing");
            var ship=d.Stage.vessel;var rot=ship.localRotation;await Task.Delay(650);
            Check((new Vector4(rot.x,rot.y,rot.z,rot.w)-new Vector4(ship.localRotation.x,ship.localRotation.y,ship.localRotation.z,ship.localRotation.w)).sqrMagnitude>1e-10f,"Vessel idle frozen: "+rot+" -> "+ship.localRotation);
            var root=d.GetComponent<UIDocument>().rootVisualElement;
            var button=root.Q<Button>("battle-end-turn");Check(button!=null&&button.enabledInHierarchy,"End-turn button missing");
            int turn=d.View.Turn;
            using(var evt=NavigationSubmitEvent.GetPooled()){evt.target=button;button.SendEvent(evt);}
            Check(d.View.Turn==turn+1&&d.Busy,"End-turn failed");
            Check(!d.Submit(BattleCommand.EndTurn()).Success,"Duplicate turn accepted during animation");
            await Task.Delay(220);d.SetPaused(true);
            age=motion.Age;sea=water.SeaTime;rot=ship.localRotation;await Task.Delay(250);
            Check(age==motion.Age&&sea==water.SeaTime&&water.Reflections.suspended&&Quaternion.Angle(rot,ship.localRotation)<.01f&&d.Busy,"Pause failed");
            d.SetPaused(false);
            float peak=0, peakGust=0;
            var skirt=d.Stage.GetComponentInChildren<PrayerSkirtMotion>();
            for(int i=0;i<45;i++)
            {
                await Task.Delay(45);peak=Mathf.Max(peak,d.Stage.CombatSway);
                if(skirt!=null)peakGust=Mathf.Max(peakGust,skirt.GustStrength);
                if(i%4==0)ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/DeckAnimation/Live/frame-"+i.ToString("00")+".png"));
            }
            Check(!d.Busy&&peak>.1f,"Animation did not recover or ship combat response missing");
            Check(skirt==null||peakGust>.5f,"Prayer wind did not signal the attack");
            GameSettings.ReducedMotion=true;
            Check(d.Submit(BattleCommand.EndTurn()).Success,"Reduced-motion turn failed");
            await Task.Delay(2100);rot=ship.localRotation;await Task.Delay(150);
            Check(Quaternion.Angle(rot,ship.localRotation)<.01f&&!d.Busy,"Reduced-motion vessel/lock failed");
            return new {frameAdvances=Time.frameCount-frame,peakCombatSway=peak,peakSkirtGust=peakGust,turn=d.View.Turn,paused=true,reduced=true,duplicateRejected=true,waterClockAndReflection=true};
        } finally {GameSettings.ReducedMotion=priorReduced;d.SetPaused(false);}
    }
}
