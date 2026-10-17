using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Imports the authored generic rig into a reusable Unity prefab and controller.
// The generated assets are deterministic and can be rebuilt after a new Blender export.
public static class BellMotherAnimationSetup
{
    const string Root = "Assets/UnsoundedSea";
    const string FbxPath = Root + "/Art/Models/BellMother_Rigged.fbx";
    const string PrefabPath = Root + "/Art/Prefabs/BellMother.prefab";
    const string ControllerPath = Root + "/Art/Animations/BellMother.controller";
    const string EnemyPath = Root + "/Content/Enemies/bell-mother.asset";

    static readonly string[] ActionNames =
    {
        "BellMother_Idle", "BellMother_Telegraph", "BellMother_Attack_A",
        "BellMother_Recover", "BellMother_Hit", "BellMother_Death"
    };

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static AnimationClip FindClip(string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(FbxPath)
            .OfType<AnimationClip>()
            .Where(x => !x.name.StartsWith("__preview__", StringComparison.Ordinal))
            .FirstOrDefault(x => x.name == name || x.name.EndsWith("|" + name, StringComparison.Ordinal));
    }

    static AnimatorController BuildController(IReadOnlyDictionary<string, AnimationClip> clips)
    {
        EnsureFolder(Root + "/Art/Animations");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null) AssetDatabase.DeleteAsset(ControllerPath);
        controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var layer = controller.layers[0];
        var states = new Dictionary<string, AnimatorState>();
        foreach (var name in ActionNames)
        {
            var state = layer.stateMachine.AddState(name);
            state.motion = clips[name];
            state.writeDefaultValues = true;
            states.Add(name, state);
        }
        layer.stateMachine.defaultState = states["BellMother_Idle"];
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
    }

    static void ConfigureImporter()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("FBX importer is unavailable: " + FbxPath);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.importAnimation = true;
        importer.optimizeGameObjects = false;
        var clips = importer.clipAnimations;
        foreach (var clip in clips)
        {
            clip.loopTime = clip.name.EndsWith("BellMother_Idle", StringComparison.Ordinal);
            clip.loopPose = clip.loopTime;
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    public static string Build()
    {
        ConfigureImporter();
        var clips = ActionNames.ToDictionary(x => x, FindClip);
        var missing = clips.Where(x => x.Value == null).Select(x => x.Key).ToArray();
        if (missing.Length > 0) throw new InvalidOperationException("Missing animation clips: " + string.Join(", ", missing));
        var controller = BuildController(clips);

        var source = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (source == null) throw new InvalidOperationException("Import FBX first: " + FbxPath);
        EnsureFolder(Root + "/Art/Prefabs");
        var root = new GameObject("BellMother");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.name = "BellMother_RiggedMesh";
            model.transform.SetParent(root.transform, false);
            var bounds = UnsoundedSea.Runtime.BattleActorView.MeasureLocalBounds(root.transform);
            if (bounds.size.y <= 0.01f) throw new InvalidOperationException("Rigged mesh has empty bounds.");
            var scale = 32f / bounds.size.y;
            model.transform.localScale *= scale;
            model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.applyRootMotion = false;
            EditorUtility.SetDirty(animator);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Could not save " + PrefabPath);

            var enemy = AssetDatabase.LoadAssetAtPath<UnsoundedSea.Runtime.EnemyDefinition>(EnemyPath);
            if (enemy != null)
            {
                enemy.presentationPrefab = prefab;
                EditorUtility.SetDirty(enemy);
            }
            AssetDatabase.SaveAssets();
            return $"Bell Mother animated prefab ready: {PrefabPath}; {clips.Count} clips, scale {scale:0.###}, height {UnsoundedSea.Runtime.BattleActorView.MeasureLocalBounds(prefab.transform).size.y:0.###}m.";
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static string RepairControllerReference()
    {
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller == null) throw new InvalidOperationException("Controller is not imported: " + ControllerPath);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            var serialized = new SerializedObject(animator);
            serialized.FindProperty("m_Controller").objectReferenceValue = controller;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            return "Repaired BellMother Animator controller reference.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
