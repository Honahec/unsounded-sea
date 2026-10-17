// Unity CLI run_script in OceanBattle Play Mode: .Gallery(page[, recordName]) or .Live().
// Gallery uses Unity BakeMesh for deterministic pose captures; Live verifies the real player loop.
// Readability runs real turns and damage cards in isolated saves, restoring device preferences.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

public static class DeckAnimationQA
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static async Task Wait(Func<bool> ready)
    {for(int i=0;i<1500;i++){if(ready())return;await Task.Delay(20);}throw new Exception("Battle readiness timed out.");}
    static void CaptureLive(string name)
    {
        var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(t=>t!=null);
        var bytes=(byte[])type.GetMethod("EncodeScreenToPng",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,new object[]{Screen.width,Screen.height});
        File.WriteAllBytes("Logs/UiExperience/DeckMotion/"+name+".png",bytes);
    }
    public static async Task<object> Readability()
    {
        Check(Application.isPlaying,"Enter Play first.");const string dir="Logs/UiExperience/DeckMotion";Directory.CreateDirectory(dir);
        var background=Application.runInBackground;Application.runInBackground=true;
        var hadReduced=PlayerPrefs.HasKey("settings.reducedMotion");var reduced=GameSettings.ReducedMotion;
        var formal=Directory.GetFiles(Application.persistentDataPath,"voyage*.json*").ToDictionary(p=>p,File.ReadAllBytes);
        var errors=new List<string>();Application.LogCallback log=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);};Application.logMessageReceived+=log;
        var results=new List<object>();VoyageRuntime.BeginValidation(Path.GetFullPath(dir+"/save"));
        try
        {
            GameSettings.ReducedMotion=false;
            foreach(var encounter in new[]{"voyage-fog-0","voyage-fog-2","voyage-fleet-1","voyage-fog-3"})
            {
                VoyageRuntime.StartDebugEncounter(encounter,"fog",false);
                await Wait(()=>!GameSceneLoader.IsLoading&&UnityEngine.Object.FindAnyObjectByType<BattleDirector>()?.Stage!=null);
                var d=UnityEngine.Object.FindAnyObjectByType<BattleDirector>();d.seed=42;d.Restart();await Task.Delay(250);
                var actor=d.Stage.Actors[0];var motion=actor.GetComponentInChildren<DeckActorMotion>();var skin=actor.GetComponentInChildren<SkinnedMeshRenderer>();
                Check(motion!=null&&motion.DrivenJointCount>=10,"Missing bound motion.");
                var feet=skin.bones.Where(b=>b.name=="LeftFoot"||b.name=="foot.L"||b.name=="RightFoot"||b.name=="foot.R").ToArray();
                var feetRest=feet.Select(f=>actor.transform.InverseTransformPoint(f.position)).ToArray();
                Quaternion[] Pose()=>skin.bones.Select(b=>b.localRotation).ToArray();
                float Difference(Quaternion[] pose)=>skin.bones.Select((b,i)=>Quaternion.Angle(pose[i],b.localRotation)).Max();
                void Feet(){for(int i=0;i<feet.Length;i++)Check(Vector3.Distance(feetRest[i],actor.transform.InverseTransformPoint(feet[i].position))<.0002f,"Foot slid during motion.");}
                var idlePose=Pose();var age=motion.Age;float idlePeak=0,attackPeak=0,hitPeak=0;int attackFrames=0;bool pausedChecked=false;
                for(int i=0;i<40;i++){await Task.Delay(30);idlePeak=Mathf.Max(idlePeak,Difference(idlePose));Feet();}
                Check(motion.Age>age&&idlePeak>.3f,"Idle did not animate.");CaptureLive(encounter+"-idle");
                for(int turn=0;turn<4&&attackFrames==0;turn++)
                {
                    var pose=Pose();var root=d.GetComponent<UIDocument>().rootVisualElement;var end=root.Q<Button>("battle-end-turn");
                    using(var e=NavigationSubmitEvent.GetPooled()){e.target=end;end.SendEvent(e);}
                    Check(d.Busy,"End turn was not accepted.");
                    bool captured=false;
                    while(d.Busy)
                    {
                        await Task.Delay(25);Feet();
                        if(motion.Attacking)
                        {
                            attackFrames++;attackPeak=Mathf.Max(attackPeak,Difference(pose));
                            if(!captured&&motion.ActionImpact>.3f){CaptureLive(encounter+"-attack");captured=true;}
                            if(!pausedChecked)
                            {
                                d.SetPaused(true);var frozen=motion.Age;var frozenPose=Pose();await Task.Delay(100);
                                Check(motion.Age==frozen&&Difference(frozenPose)<.01f,"Pause did not freeze animation.");d.SetPaused(false);pausedChecked=true;
                            }
                        }
                    }
                }
                Check(attackFrames>0&&attackPeak>3,"Real enemy action did not produce a visible attack.");
                var damageIds=new[]{"c001","c002","c003","c004","c005","c006","c007","c008","c012","c013","c015","c016","c018"};
                CardView card=null;
                for(int turn=0;turn<4&&card==null;turn++)
                {
                    card=d.View.Hand.FirstOrDefault(c=>c.Enabled&&c.Definition.RequiresTarget&&c.Definition.Effects.Any(e=>damageIds.Contains(e.Id)));
                    if(card==null){Check(d.Submit(BattleCommand.EndTurn()).Success,"Could not cycle the hand.");await Wait(()=>!d.Busy);}
                }
                Check(card!=null,"No legal damage card available.");
                var beforeHealth=d.View.Enemies[0].Health;var hitPose=Pose();
                var target=card.Targets.FirstOrDefault(t=>t.Enabled&&(t.Id==actor.EnemyId||t.Id=="enemy:"+actor.EnemyId))?.Id??actor.EnemyId;
                var damage=d.Submit(BattleCommand.Play(card.InstanceId,target));
                Check(damage.Success,"Damage card failed: "+card.Definition.Id+" / "+damage.Error);
                Check(d.View.Enemies[0].Health>0&&d.View.Enemies[0].Health<beforeHealth,"Card did not hit a living enemy.");
                bool hitCaptured=false;
                for(int i=0;i<45;i++)
                {
                    await Task.Delay(25);Feet();var difference=Difference(hitPose);hitPeak=Mathf.Max(hitPeak,difference);
                    if(!hitCaptured&&difference>4){CaptureLive(encounter+"-hit");hitCaptured=true;}
                }
                Check(hitPeak>4&&!d.Busy,"Real card hit did not animate or recover.");
                GameSettings.ReducedMotion=true;
                Check(d.Submit(BattleCommand.EndTurn()).Success,"Reduced motion turn failed.");await Wait(()=>!d.Busy);await Task.Delay(100);
                var staticPose=Pose();await Task.Delay(180);Check(Difference(staticPose)<.01f,"Reduced motion did not suppress poses.");GameSettings.ReducedMotion=false;
                results.Add(new{encounter,style=motion.style.ToString(),motion.DrivenJointCount,motion.idleAmplitude,motion.attackAmplitude,motion.hitAmplitude,idlePeak,attackPeak,hitPeak,attackFrames,pausedChecked,feetFixed=true,reducedChecked=true});
            }
            Check(errors.Count==0,string.Join("; ",errors));
            var after=Directory.GetFiles(Application.persistentDataPath,"voyage*.json*").ToDictionary(p=>p,File.ReadAllBytes);
            Check(formal.Count==after.Count&&formal.All(p=>after.TryGetValue(p.Key,out var bytes)&&p.Value.SequenceEqual(bytes)),"Formal saves changed.");
            var report=new{success=true,results,runtimeErrors=errors.Count,formalSavesUnchanged=true};File.WriteAllText(dir+"/result.json",JsonConvert.SerializeObject(report,Formatting.Indented));return report;
        }
        catch(Exception error){File.WriteAllText(dir+"/error.txt",error.ToString());throw;}
        finally
        {
            VoyageRuntime.EndValidation();GameSettings.ReducedMotion=reduced;if(!hadReduced){PlayerPrefs.DeleteKey("settings.reducedMotion");PlayerPrefs.Save();}
            Application.runInBackground=background;Application.logMessageReceived-=log;SceneManager.LoadScene("MainMenu");
        }
    }
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
