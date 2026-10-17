using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnsoundedSea.Runtime;
public static class EnemyAnimationQA
{
    public static async Task<object> Run()
    {
        var r=UnityEngine.Object.FindAnyObjectByType<EnemyRigReview>();
        if(!Application.isPlaying || !r) throw new Exception("Review must be playing");
        Application.runInBackground=true;
        var rows=new List<object>();
        for(int a=0;a<r.actors.Length;a++)
        {
            r.SelectActor(a);r.SetView(0);
            var entry=r.actors[a];var skin=entry.actor.GetComponentInChildren<SkinnedMeshRenderer>();var mesh=new Mesh();
            try
            {
                for(int c=0;c<5;c++)
                {
                    r.SelectClip(c);r.SetPlaying(false);r.SetNormalizedTime(0);skin.BakeMesh(mesh);var rest=mesh.vertices;
                    r.SetNormalizedTime(c==3?1:c==4?.13f:.5f);skin.BakeMesh(mesh);var posed=mesh.vertices;
                    float delta=0;for(int v=0;v<rest.Length;v++)delta=Mathf.Max(delta,Vector3.Distance(rest[v],posed[v]));
                    if(delta<.0001f)throw new Exception(entry.label+" clip does not deform: "+c);
                    if(c==3)
                    {
                        if(entry.death.isLooping)throw new Exception(entry.label+" Death loops");
                        var world=posed.Select(v=>skin.transform.TransformPoint(v)).ToArray();
                        float bottom=world.Min(v=>v.y),top=world.Max(v=>v.y);
                        if(bottom<-.04f || bottom>.08f || top>1.5f)throw new Exception(entry.label+" Death not grounded/prone: "+bottom+".."+top);
                    }
                    if(c==1 || c==3){foreach(var view in new[]{0,1}){r.SetView(view);await Task.Delay(100);ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/RigTrials/"+entry.actor.name+"-"+entry.Clip(c).name+"-unity-"+(view==0?"front":"side")+".png"));await Task.Delay(150);}r.SetView(0);}
                    rows.Add(new {enemy=entry.label,clip=entry.Clip(c).name,maxVertexDelta=delta});
                }
                r.SelectClip(0);r.Restart();await Task.Delay((int)(entry.idle.length*1000)+250);
                if(!r.IsPlaying || r.Time>=entry.idle.length)throw new Exception("Idle loop failed");
                r.SelectClip(3);r.Restart();await Task.Delay((int)(entry.death.length*1000)+250);
                if(r.IsPlaying || Math.Abs(r.Time-entry.death.length)>.001)throw new Exception("Death did not hold");
                skin.BakeMesh(mesh);var held=mesh.vertices;await Task.Delay(150);skin.BakeMesh(mesh);var later=mesh.vertices;
                if(held.Where((v,i)=>Vector3.Distance(v,later[i])>.0001f).Any())throw new Exception("Death pose drifted");
            }
            finally{UnityEngine.Object.Destroy(mesh);}
        }
        r.SelectActor(0);r.SelectClip(0);r.Restart();
        var json=Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented);File.WriteAllText("ArtSource/Characters/RigTrials/unity-animation-check.json",json);
        return rows;
    }
}
