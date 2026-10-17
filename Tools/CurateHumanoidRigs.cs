// Execute through Unity CLI. Move selected assets preserving GUIDs, rebuild
// standalone normalized prefabs, validate, then remove obsolete trial folders.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CurateHumanoidRigs
{
    const string Models = "Assets/UnsoundedSea/Art/Models/CuratedRigs";
    const string Prefabs = "Assets/UnsoundedSea/Art/Prefabs/CuratedRigs";
    const string Evidence = "ArtSource/Characters/CuratedRigs/unity-validation.json";
    const string OldScene = "Assets/UnsoundedSea/Scenes/EnemyRigReview.unity";
    static readonly string[] Obsolete = {
        "Assets/UnsoundedSea/Art/Models/RigTrials", "Assets/UnsoundedSea/Art/Models/Hyper3DRigs",
        "Assets/UnsoundedSea/Art/Prefabs/Hyper3DRigs", "Assets/UnsoundedSea/Art/Animations/RigTrials"
    };
    public class Selection { public Entry[] assets; }
    public class Entry { public string name, selection, model, prefab, previous_model, staging_override; public float height; }
    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        Folder(Path.GetDirectoryName(path).Replace('\\','/'));
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));
    }
    static void Move(string from, string to)
    {
        if (File.Exists(to)) return;
        var error = AssetDatabase.MoveAsset(from,to);
        if (!string.IsNullOrEmpty(error)) throw new IOException(error);
    }
    static Vector3[] Bake(SkinnedMeshRenderer skin)
    {
        var mesh = new Mesh();
        try { skin.BakeMesh(mesh,true); return mesh.vertices.Select(skin.transform.TransformPoint).ToArray(); }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }
    static Bounds BoundsOf(Vector3[] points)
    {
        var bounds = new Bounds(points[0],Vector3.zero);
        foreach (var p in points) bounds.Encapsulate(p);
        return bounds;
    }
    static float Delta(Vector3[] a,Vector3[] b) => a.Zip(b,(x,y)=>Vector3.Distance(x,y)).Max();
    static void Rotate(Transform bone, Vector3 axis, float degrees)
    {
        var basis = bone.rotation;
        bone.localRotation *= Quaternion.Inverse(basis)*Quaternion.AngleAxis(degrees,axis)*basis;
    }
    static void Guard()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Editor is playing.");
        if (Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).path==OldScene))
            throw new InvalidOperationException("Close the obsolete review scene first.");
    }
    public static string Consolidate(string selectionPath)
    {
        Guard(); Folder(Models); Folder(Prefabs);
        var selected = Newtonsoft.Json.JsonConvert.DeserializeObject<Selection>(File.ReadAllText(selectionPath));
        foreach (var e in selected.assets)
        {
            if (!File.Exists(e.model) && !File.Exists(e.previous_model)) throw new FileNotFoundException(e.previous_model);
            if (!string.IsNullOrEmpty(e.staging_override) && !File.Exists(e.staging_override)) throw new FileNotFoundException(e.staging_override);
        }
        var results = new List<object>();
        foreach (var e in selected.assets)
        {
            var guid = AssetDatabase.AssetPathToGUID(File.Exists(e.model)? e.model:e.previous_model);
            Move(e.previous_model,e.model);
            if (!string.IsNullOrEmpty(e.staging_override)) File.Copy(e.staging_override,e.model,true);
            AssetDatabase.ImportAsset(e.model,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            if (AssetDatabase.AssetPathToGUID(e.model)!=guid) throw new InvalidDataException("Model GUID changed.");
            Move("Assets/UnsoundedSea/Art/Prefabs/Hyper3DRigs/"+e.name+".prefab",e.prefab);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject wrapper = null;
            try
            {
                wrapper = new GameObject(e.name); SceneManager.MoveGameObjectToScene(wrapper,scene);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(e.model);
                var visual = UnityEngine.Object.Instantiate(model,wrapper.transform);
                foreach(var a in visual.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);
                foreach(var a in visual.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(a);
                var skin = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single();
                if (skin.bones.Length!=skin.sharedMesh.bindposes.Length || skin.bones.Any(b=>b==null)) throw new InvalidDataException(e.name+": invalid bones.");
                if (skin.sharedMaterials.Any(m=>m==null || m.shader==null || !m.shader.isSupported)) throw new InvalidDataException(e.name+": invalid material.");
                var bounds = BoundsOf(Bake(skin));
                var scale = e.height/bounds.size.y;
                visual.transform.localScale *= scale;
                visual.transform.localPosition = (visual.transform.localPosition + new Vector3(-bounds.center.x,-bounds.min.y,-bounds.center.z))*scale;
                var rest = Bake(skin); bounds = BoundsOf(rest);
                if (Mathf.Abs(bounds.size.y-e.height)>.005f || Mathf.Abs(bounds.min.y)>.005f) throw new InvalidDataException(e.name+": incorrect normalization: "+bounds+" target="+e.height+" scale="+scale+" skinScale="+skin.transform.lossyScale);
                var old = e.selection=="rigtrials";
                var targets = new [] {new[]{old?"chest":"Chest"},new[]{old?"forearm.L":"LeftLowerArm",old?"forearm.R":"RightLowerArm"},new[]{old?"shin.L":"LeftLowerLeg"}};
                var poses = new List<object>();
                for (int i=0;i<targets.Length;i++)
                {
                    var bones=targets[i].Select(n=>skin.bones.First(b=>b.name==n)).ToArray();
                    var rotations=bones.Select(b=>b.localRotation).ToArray();
                    for(int j=0;j<bones.Length;j++) Rotate(bones[j],i==0?Vector3.up:Vector3.right,i==0?15:i==1?-40:35);
                    var deformed=Bake(skin);
                    if(deformed.Any(v=>float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.sqrMagnitude))) throw new InvalidDataException("Nonfinite skinning.");
                    for(int j=0;j<bones.Length;j++) bones[j].localRotation=rotations[j];
                    var reset=Delta(rest,Bake(skin)); var motion=Delta(rest,deformed);
                    if(reset>0.0001f || motion<0.0001f) throw new InvalidDataException(e.name+": pose/reset failed.");
                    poses.Add(new {pose=new[]{"torso","elbows","knee"}[i],maxMotionMetres=motion,restoreErrorMetres=reset});
                }
                if(PrefabUtility.SaveAsPrefabAsset(wrapper,e.prefab)==null) throw new IOException(e.prefab);
                results.Add(new {e.name,e.selection,e.model,e.prefab,modelGuid=guid,bones=skin.bones.Length,triangles=skin.sharedMesh.triangles.Length/3,
                    height=bounds.size.y,bottom=bounds.min.y,embeddedLegacyClips=AssetDatabase.LoadAllAssetsAtPath(e.model).OfType<AnimationClip>().Count(),
                    prefabAnimationDrivers=0,materialsSupported=true,poses});
            }
            finally { if(wrapper!=null) UnityEngine.Object.DestroyImmediate(wrapper); EditorSceneManager.ClosePreviewScene(scene); }
        }
        File.WriteAllText(Evidence,Newtonsoft.Json.JsonConvert.SerializeObject(new {date="2026-10-17",unity=Application.unityVersion,
            scope="Editor import, grounded normalized prefab, torso/elbow/knee BakeMesh checks using actual imported weights. No gameplay/performance acceptance.",assets=results},Newtonsoft.Json.Formatting.Indented)+"\n");
        return "Validated "+results.Count+" curated prefabs. Obsolete folders not deleted until dependency audit.";
    }
    public static string DeleteObsolete()
    {
        Guard();
        if(!File.Exists(Evidence)) throw new InvalidOperationException("Validate first.");
        var consumers = new List<string>();
        foreach(var path in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p)))
        {
            if(path==OldScene || Obsolete.Any(root=>path.StartsWith(root+"/",StringComparison.Ordinal))) continue;
            if(AssetDatabase.GetDependencies(path,false).Any(dep=>dep==OldScene || Obsolete.Any(root=>dep.StartsWith(root+"/",StringComparison.Ordinal)))) consumers.Add(path);
        }
        if(consumers.Count>0) throw new InvalidOperationException("Obsolete assets still referenced: "+string.Join(",",consumers));
        if(File.Exists(OldScene) && !AssetDatabase.DeleteAsset(OldScene)) throw new IOException(OldScene);
        foreach(var folder in Obsolete)
            if(AssetDatabase.IsValidFolder(folder) && !AssetDatabase.DeleteAsset(folder)) throw new IOException(folder);
        return "Removed obsolete models, animation trials, prefabs and their sole review scene; no remaining external consumers.";
    }
}
