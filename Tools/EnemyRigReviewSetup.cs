// Execute through Unity CLI run_script; kept outside Assets to avoid editor code in the player.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

public static class EnemyRigReviewSetup
{
    const string SourceRoot = "ArtSource/Characters/RigTrials";
    const string ModelRoot = "Assets/UnsoundedSea/Art/Models/RigTrials";
    const string MaterialRoot = "Assets/UnsoundedSea/Art/Materials/RigReview";
    const string AnimationRoot = "Assets/UnsoundedSea/Art/Animations/RigTrials";
    const string ScenePath = "Assets/UnsoundedSea/Scenes/EnemyRigReview.unity";
    static readonly string[] RequiredClips = { "Idle", "Action", "Hit", "Death", "RigValidation" };

    public static object Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode before building the rig review.");
        for (var i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene edits before building the rig review.");

        var sourceDirectory = Path.GetFullPath(SourceRoot);
        if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);
        var files = Directory.GetFiles(sourceDirectory, "*_Rigged.glb", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0) throw new InvalidDataException("No *_Rigged.glb files found in " + SourceRoot + ".");

        EnsureFolder(ModelRoot);
        EnsureFolder(MaterialRoot);
        EnsureFolder(AnimationRoot);
        foreach (var file in files) Import(file);

        var models = files.Select(Inspect).ToArray();
        var reviewClips = files.Select(ExtractClips).ToArray();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var entries = new EnemyRigReview.ActorEntry[files.Length];
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(file));
            var actor = (GameObject)PrefabUtility.InstantiatePrefab(source);
            actor.name = Path.GetFileNameWithoutExtension(file);
            Normalize(actor.transform, 2.1f);
            actor.SetActive(i == 0);
            if (!actor.GetComponent<Animator>()) actor.AddComponent<Animator>();
            var clips = reviewClips[i];
            entries[i] = new EnemyRigReview.ActorEntry
            {
                label = Humanize(actor.name.Substring(0, actor.name.Length - "_Rigged".Length)),
                actor = actor.transform,
                idle = clips["Idle"],
                action = clips["Action"],
                hit = clips["Hit"],
                death = clips["Death"],
                rigValidation = clips["RigValidation"]
            };
        }

        BuildEnvironment(out var camera);
        var review = new GameObject("Rig Review Controls").AddComponent<EnemyRigReview>();
        review.reviewCamera = camera;
        review.actors = entries;

        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath))
            throw new IOException("Failed to save " + ScenePath);
        return new { scene = ScenePath, models };
    }

    static object Inspect(string file)
    {
        var path = ModelPath(file);
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!root) throw new InvalidDataException("GLB import failed: " + path);
        var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skins.Length == 0) throw new InvalidDataException(file + " has no skinned mesh renderer.");
        var boneCount = skins.SelectMany(s => s.bones ?? Array.Empty<Transform>()).Where(b => b).Distinct().Count();
        if (boneCount == 0 || skins.Any(s => s.bones == null || s.bones.Length == 0))
            throw new InvalidDataException(file + " has a skinned renderer without bones.");
        var clips = Clips(file);
        foreach (var name in RequiredClips)
            if (clips[name].empty || clips[name].length <= 0)
                throw new InvalidDataException(file + " " + name + " has no animation curves.");
        var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
        var triangles = meshes.Sum(m => Enumerable.Range(0, m.subMeshCount).Sum(s => (long)m.GetIndexCount(s) / 3));
        return new { file, skinnedRenderers = skins.Length, bones = boneCount, clips = clips.Values.Select(c => new { c.name, c.length, c.frameRate }).ToArray(), triangles };
    }

    static System.Collections.Generic.Dictionary<string, AnimationClip> Clips(string file)
    {
        var imported = AssetDatabase.LoadAllAssetsAtPath(ModelPath(file)).OfType<AnimationClip>().ToArray();
        var clips = new System.Collections.Generic.Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        foreach (var name in RequiredClips)
        {
            var clip = imported.FirstOrDefault(candidate => string.Equals(candidate.name, name, StringComparison.Ordinal));
            if (!clip) throw new InvalidDataException(file + " has no animation named " + name + ". Imported clips: " + string.Join(", ", imported.Select(c => c.name)));
            clips.Add(name, clip);
        }
        return clips;
    }

    static System.Collections.Generic.Dictionary<string, AnimationClip> ExtractClips(string file)
    {
        var imported = Clips(file);
        var enemy = Path.GetFileNameWithoutExtension(file);
        enemy = enemy.Substring(0, enemy.Length - "_Rigged".Length);
        var folder = AnimationRoot + "/" + enemy;
        EnsureFolder(folder);

        var extracted = new System.Collections.Generic.Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        foreach (var name in RequiredClips)
        {
            var path = folder + "/" + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip)
            {
                EditorUtility.CopySerialized(imported[name], clip);
            }
            else
            {
                clip = new AnimationClip();
                EditorUtility.CopySerialized(imported[name], clip);
                AssetDatabase.CreateAsset(clip, path);
            }

            clip.name = name;
            var loops = name == "Idle" || name == "RigValidation";
            clip.wrapMode = loops ? WrapMode.Loop : WrapMode.ClampForever;
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loops;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            extracted.Add(name, clip);
        }
        return extracted;
    }

    static void Import(string file)
    {
        var source = Path.GetFullPath(SourceRoot + "/" + file);
        var destination = Path.GetFullPath(ModelPath(file));
        File.Copy(source, destination, true); // Overwriting bytes leaves an existing Unity .meta GUID intact.
        AssetDatabase.ImportAsset(ModelPath(file), ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
    }

    static void Normalize(Transform actor, float height)
    {
        actor.rotation = Quaternion.Euler(0, 180, 0);
        var renderers = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (renderers.Length == 0) throw new InvalidDataException(actor.name + " has no skinned mesh.");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        if (bounds.size.y <= 0.001f) throw new InvalidDataException(actor.name + " has empty renderer bounds.");
        var scale = height / bounds.size.y;
        actor.localScale = Vector3.one * scale;
        actor.position = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);
    }

    static void BuildEnvironment(out Camera camera)
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Neutral Ground";
        ground.transform.localScale = new Vector3(.7f, 1, .7f);
        UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.GetComponent<Renderer>().sharedMaterial = Material("Ground.mat", new Color(.15f, .17f, .18f));

        var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backdrop.name = "Neutral Backdrop";
        backdrop.transform.position = new Vector3(0, 2.25f, 1.2f);
        backdrop.transform.localScale = new Vector3(8, 4.5f, 1);
        backdrop.transform.rotation = Quaternion.Euler(0, 180, 0);
        UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());
        backdrop.GetComponent<Renderer>().sharedMaterial = Material("Backdrop.mat", new Color(.08f, .10f, .11f));

        camera = new GameObject("Review Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.fieldOfView = 35;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 30;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.055f, .065f, .07f);
        camera.transform.position = new Vector3(0, 1.25f, -5.5f);
        camera.transform.LookAt(new Vector3(0, 1.05f, 0));

        Light("Warm Key", new Color(1, .92f, .8f), 1.65f, new Vector3(35, -35, 0), true);
        Light("Cool Fill", new Color(.65f, .8f, 1), .8f, new Vector3(25, 145, 0), false);
        Light("Rim", new Color(.75f, .85f, 1), .65f, new Vector3(20, 200, 0), false);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.32f, .32f, .32f);
        RenderSettings.fog = false;
    }

    static Material Material(string file, Color color)
    {
        var path = MaterialRoot + "/" + file;
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader) throw new InvalidOperationException("URP Lit shader is unavailable.");
        material = new Material(shader) { name = Path.GetFileNameWithoutExtension(file) };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", .15f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static void Light(string name, Color color, float intensity, Vector3 rotation, bool shadows)
    {
        var light = new GameObject(name).AddComponent<UnityEngine.Light>();
        light.type = LightType.Directional;
        light.color = color;
        light.intensity = intensity;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.transform.rotation = Quaternion.Euler(rotation);
    }

    static string ModelPath(string file) => ModelRoot + "/" + file;

    static string Humanize(string name) =>
        string.Concat(name.Select((character, index) => index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
