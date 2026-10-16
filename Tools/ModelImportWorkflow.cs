// Execute through Unity CLI run_script; kept outside Assets to avoid runtime code.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

public static class ModelImportWorkflow
{
    const string Art = "Assets/UnsoundedSea/Art";
    const string ScenePath = "Assets/UnsoundedSea/Scenes/ModelImportReview.unity";
    static readonly string[] Names = { "Prayer", "Echo" };
    static readonly string[] SourceNames = { "Prayer_LOD0.glb", "Echo_LOD0.glb" };
    static readonly string[] Hashes = {
        "0fbc583e10eb89c42fd8ca6883082b4b77ef2d7e38a81df1fbb0cd8861d92aaf",
        "4a6d417b05dd63457c8b99e59d00f5f9b03d4a5cabbad9a4af2642fc16070972"
    };
    static readonly float[] Heights = { 1.8f, 2.1f };
    static string Model(int i) => Art + "/Models/" + SourceNames[i];
    static string Prefab(int i) => Art + "/Prefabs/" + Names[i] + ".prefab";

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    public static string ImportSources(string sourceDirectory)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        // Validate both sources before copying either. Do not silently replace another revision.
        for (int i = 0; i < Names.Length; i++)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(Path.Combine(sourceDirectory, SourceNames[i]));
            var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            if (hash != Hashes[i]) throw new InvalidDataException("Unexpected source revision: " + SourceNames[i]);
            if (File.Exists(Model(i)))
            {
                using var existing = File.OpenRead(Model(i));
                var previous = BitConverter.ToString(sha.ComputeHash(existing)).Replace("-", "").ToLowerInvariant();
                if (previous != hash) throw new InvalidDataException("Destination contains another revision: " + Model(i));
            }
        }
        EnsureFolder(Art + "/Models");
        for (int i = 0; i < Names.Length; i++)
        {
            if (!File.Exists(Model(i))) File.Copy(Path.Combine(sourceDirectory, SourceNames[i]), Model(i));
            AssetDatabase.ImportAsset(Model(i), ImportAssetOptions.ForceSynchronousImport);
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Model(i))) throw new InvalidDataException("GLB import failed: " + Model(i));
        }
        AssetDatabase.SaveAssets();
        return "Imported both verified GLBs.";
    }

    public static string CreateReviewScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits before opening the review scene.");
        if (File.Exists(ScenePath))
        {
            EditorSceneManager.OpenScene(ScenePath);
            return "Opened existing review scene without overwriting edits.";
        }
        // Check all inputs and existing bindings before modifying the active scene.
        for (int i = 0; i < Names.Length; i++)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Model(i))) throw new InvalidDataException("Import both runtime GLBs first.");
            var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/UnsoundedSea/Content/Enemies/" + Names[i].ToLowerInvariant() + ".asset");
            if (!enemy) throw new InvalidDataException("Missing enemy definition: " + Names[i]);
            if (enemy.presentationPrefab && enemy.presentationPrefab != AssetDatabase.LoadAssetAtPath<GameObject>(Prefab(i)))
                throw new InvalidOperationException("Enemy already has a different presentation prefab.");
        }
        EnsureFolder(Art + "/Prefabs");
        EnsureFolder(Art + "/Materials");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        for (int i = 0; i < Names.Length; i++)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model(i));
            if (!source) throw new InvalidDataException("Import the GLBs first.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab(i));
            if (!prefab)
            {
                var root = new GameObject(Names[i]);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source);
                visual.transform.SetParent(root.transform, false);
                var renderers = visual.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                float scale = Heights[i] / bounds.size.y;
                visual.transform.localScale *= scale;
                visual.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                prefab = PrefabUtility.SaveAsPrefabAsset(root, Prefab(i));
                UnityEngine.Object.DestroyImmediate(root);
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(i == 0 ? -0.85f : 0.85f, 0, 0);
            instance.transform.rotation = Quaternion.Euler(0, 180, 0);
            var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/UnsoundedSea/Content/Enemies/" + Names[i].ToLowerInvariant() + ".asset");
            if (enemy.presentationPrefab && enemy.presentationPrefab != prefab)
                throw new InvalidOperationException("Enemy already has a different presentation prefab.");
            enemy.presentationPrefab = prefab;
            EditorUtility.SetDirty(enemy);
        }
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Neutral Ground";
        ground.transform.localScale = Vector3.one * 2;
        UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/ReviewGround.mat");
        if (!mat)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "Review Ground";
            mat.SetColor("_BaseColor", new Color(0.12f, 0.16f, 0.18f));
            mat.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(mat, Art + "/Materials/ReviewGround.mat");
        }
        ground.GetComponent<Renderer>().sharedMaterial = mat;
        var camera = new GameObject("Review Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 1.6f, -5.2f);
        camera.transform.LookAt(new Vector3(0, 1.05f, 0));
        camera.fieldOfView = 34;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 30;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.12f, 0.15f);
        var key = new GameObject("Warm Key").AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1, 0.93f, 0.83f);
        key.intensity = 1.6f;
        key.shadows = LightShadows.Soft;
        key.transform.rotation = Quaternion.Euler(35, -30, 0);
        var fill = new GameObject("Cool Fill").AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.color = new Color(0.65f, 0.8f, 1);
        fill.intensity = 0.65f;
        fill.transform.rotation = Quaternion.Euler(25, 145, 0);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.4f);
        RenderSettings.fog = false;
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
        return "Created review scene and linked runtime LOD0 presentation prefabs. No rigging or animation included.";
    }

    public static object Validate()
    {
        return Names.Select((name, i) => {
            var assets = AssetDatabase.LoadAllAssetsAtPath(Model(i));
            var meshes = assets.OfType<Mesh>().ToArray();
            var materials = assets.OfType<Material>().ToArray();
            var textures = assets.OfType<Texture2D>().ToArray();
            var triangles = meshes.Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(s => (long)m.GetIndexCount(s) / 3));
            if (triangles != (i == 0 ? 60000 : 90000)) throw new InvalidDataException("Unexpected triangle count: " + name);
            if (materials.Length != 1 || materials.Any(m => !m.shader || !m.shader.isSupported)) throw new InvalidDataException("Invalid material: " + name);
            if (textures.Length != 3 || textures.Any(t => t.width != 4096 || t.height != 4096)) throw new InvalidDataException("Missing 4K textures: " + name);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab(i));
            if (!prefab) throw new InvalidDataException("Missing review prefab: " + name);
            var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/UnsoundedSea/Content/Enemies/" + name.ToLowerInvariant() + ".asset");
            if (enemy.presentationPrefab != prefab) throw new InvalidDataException("Broken presentation link: " + name);
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length != 1 || renderers.Any(r => r.sharedMaterials.Any(m => !m)))
                throw new InvalidDataException("Missing renderer or material: " + name);
            var bounds = renderers[0].bounds;
            if (Mathf.Abs(bounds.size.y - Heights[i]) > 0.001f || Mathf.Abs(bounds.min.y) > 0.001f)
                throw new InvalidDataException("Incorrect height or ground contact: " + name);
            return new { name, triangles, meshes = meshes.Length, shader = materials[0].shader.name,
                height = bounds.size.y, feetY = bounds.min.y,
                textures = textures.Select(t => new { t.name, t.width, t.height, t.mipmapCount, format = t.format.ToString() }).ToArray(),
                prefab = Prefab(i), guid = AssetDatabase.AssetPathToGUID(Model(i)) };
        }).ToArray();
    }
}
