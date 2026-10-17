// Run through Unity CLI. Import standalone bound models, without changing scenes,
// enemy references, controllers, or the existing animation trial assets.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Hyper3DRigImport
{
    const string Destination = "Assets/UnsoundedSea/Art/Models/Hyper3DRigs";
    const string Prefabs = "Assets/UnsoundedSea/Art/Prefabs/Hyper3DRigs";
    const string Staging = "Logs/AssetDesign/hyper3d-humanoids/prepared";
    const string Evidence = "ArtSource/Characters/Hyper3DRigs/unity-validation.json";

    [Serializable] public class Entry
    {
        public string name, path, prefab;
        public int bones, vertices, triangles, clips, materials;
        public float height, bottom, maxDeformation, maxRestoreError;
        public bool shadersSupported;
    }
    [Serializable] public class Spec { public float height_m; }
    [Serializable] public class Results
    {
        public string date = "2026-10-17";
        public string scope = "Editor import and reversible 3-degree chest deformation. No gameplay animations or performance approval.";
        public string unityVersion;
        public List<Entry> assets = new List<Entry>();
    }

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        Folder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Vector3[] Bake(SkinnedMeshRenderer renderer)
    {
        var mesh = new Mesh();
        // Verified on Unity 6000.6.5f1: this explicit overload returns local
        // vertices at parent scales 0.5, 1 and 2. TransformPoint applies scale once.
        try { renderer.BakeMesh(mesh, true); return mesh.vertices; }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }

    static float Delta(Vector3[] first, Vector3[] second)
    {
        if (first.Length != second.Length) throw new InvalidDataException("Vertex count changed while posing.");
        float max = 0;
        for (int i = 0; i < first.Length; i++) max = Mathf.Max(max, Vector3.Distance(first[i], second[i]));
        return max;
    }

    public static string ImportValidate(string names)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before importing.");
        Folder(Destination);
        Folder(Prefabs);
        var results = File.Exists(Evidence) ? Newtonsoft.Json.JsonConvert.DeserializeObject<Results>(File.ReadAllText(Evidence)) : new Results();
        results.unityVersion = Application.unityVersion;
        foreach (var name in names.Split(','))
        {
            if (name.Length == 0 || name.Any(c => !char.IsLetterOrDigit(c))) throw new ArgumentException("Invalid asset name.");
            var source = Path.Combine(Staging, name + "_Rigged.glb");
            var path = Destination + "/" + name + "_Rigged.glb";
            if (!File.Exists(source)) throw new FileNotFoundException(source);
            if (File.Exists(path))
            {
                using var sha = System.Security.Cryptography.SHA256.Create();
                var a = sha.ComputeHash(File.ReadAllBytes(source));
                var b = sha.ComputeHash(File.ReadAllBytes(path));
                if (!a.SequenceEqual(b)) throw new InvalidOperationException("Destination is another revision: " + path);
            }
            else File.Copy(source, path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidDataException("GLB import failed: " + path);
            var preview = EditorSceneManager.NewPreviewScene();
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(model);
                SceneManager.MoveGameObjectToScene(instance, preview);
                var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (skins.Length != 1) throw new InvalidDataException(name + ": expected one skinned renderer.");
                var skin = skins[0];
                var mesh = skin.sharedMesh;
                if (skin.bones.Length == 0 || mesh.bindposes.Length != skin.bones.Length || skin.bones.Any(b => b == null))
                    throw new InvalidDataException(name + ": invalid bone references.");
                int clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Count();
                if (clips != 0) throw new InvalidDataException(name + ": unexpected animations.");
                var rest = Bake(skin);
                var bounds = new Bounds(skin.transform.TransformPoint(rest[0]), Vector3.zero);
                foreach (var vertex in rest) bounds.Encapsulate(skin.transform.TransformPoint(vertex));
                var chest = skin.bones.First(b => b.name == "Chest");
                var rotation = chest.localRotation;
                chest.localRotation = rotation * Quaternion.Euler(0, 3, 0);
                var bent = Bake(skin);
                chest.localRotation = rotation;
                var restored = Bake(skin);
                var delta = Delta(rest, bent);
                var restoreError = Delta(rest, restored);
                if (delta < 0.0001f || restoreError > 0.0001f) throw new InvalidDataException(name + ": skinning did not deform/reset.");
                var materials = skin.sharedMaterials;
                bool supported = materials.Length > 0 && materials.All(m => m != null && m.shader != null && m.shader.isSupported);
                if (!supported) throw new InvalidDataException(name + ": missing/unsupported material.");
                var targetHeight = JsonUtility.FromJson<Spec>(File.ReadAllText(Path.Combine(Staging, name + ".json"))).height_m;
                var wrapper = new GameObject(name);
                SceneManager.MoveGameObjectToScene(wrapper, preview);
                var visual = instance;
                instance = wrapper;
                visual.transform.SetParent(wrapper.transform, false);
                float scale = targetHeight / bounds.size.y;
                visual.transform.localScale *= scale;
                visual.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                var normalized = Bake(skin);
                bounds = new Bounds(skin.transform.TransformPoint(normalized[0]), Vector3.zero);
                foreach (var vertex in normalized) bounds.Encapsulate(skin.transform.TransformPoint(vertex));
                if (Mathf.Abs(bounds.min.y) > 0.01f || bounds.size.y < 1.5f || bounds.size.y > 2.3f)
                    throw new InvalidDataException(name + ": unexpected metre scale or foot alignment: " + bounds);
                var prefab = Prefabs + "/" + name + ".prefab";
                if (PrefabUtility.SaveAsPrefabAsset(wrapper, prefab) == null) throw new IOException("Failed to save " + prefab);
                results.assets.RemoveAll(x => x.name == name);
                results.assets.Add(new Entry { name = name, path = path, prefab = prefab, bones = skin.bones.Length,
                    vertices = mesh.vertexCount, triangles = mesh.triangles.Length / 3, clips = clips,
                    materials = materials.Length, height = bounds.size.y, bottom = bounds.min.y,
                    maxDeformation = delta, maxRestoreError = restoreError, shadersSupported = supported });
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                EditorSceneManager.ClosePreviewScene(preview);
            }
            results.assets = results.assets.OrderBy(x => x.name).ToList();
            File.WriteAllText(Evidence, Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented) + "\n");
        }
        return "Validated " + results.assets.Count + " standalone binding-only models. No encounter or animation references changed.";
    }
}
