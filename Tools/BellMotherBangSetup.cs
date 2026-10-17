using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;

// Builds the Bell Mother prefab from the BANG rigid-part export. The imported
// GLB stays as a source asset; this prefab only adds scale, pivot references,
// and the runtime rigid-motion presenter.
public static class BellMotherBangSetup
{
    const string Root = "Assets/UnsoundedSea";
    const string ModelPath = Root + "/Art/Models/BellMother_BANG_LOD0.glb";
    const string PrefabPath = Root + "/Art/Prefabs/BellMother.prefab";
    const string EnemyPath = Root + "/Content/Enemies/bell-mother.asset";

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var result = FindDeep(child, name);
            if (result != null) return result;
        }
        return null;
    }

    public static string Build()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null) throw new InvalidOperationException("Import BANG GLB first: " + ModelPath);
        EnsureFolder(Root + "/Art/Prefabs");
        var root = new GameObject("BellMother");
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.name = "BellMother_BANG_RigidMesh";
            model.transform.SetParent(root.transform, false);
            var bounds = BattleActorView.MeasureLocalBounds(root.transform);
            if (bounds.size.y <= 0.01f) throw new InvalidOperationException("BANG mesh has empty bounds.");
            var scale = 32f / bounds.size.y;
            model.transform.localScale *= scale;
            model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;

            var motion = root.AddComponent<BellMotherRigidMotion>();
            motion.body = FindDeep(model.transform, "Body");
            motion.crown = FindDeep(model.transform, "Crown");
            motion.tentacles = Enumerable.Range(1, 6)
                .Select(i => FindDeep(model.transform, "Tentacle_" + i.ToString("00")))
                .ToArray();
            if (motion.body == null || motion.crown == null || motion.tentacles.Any(x => x == null))
                throw new InvalidOperationException("BANG pivot hierarchy is incomplete.");
            ConfigureAttackJoints(motion);
            motion.CaptureRestPose();

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Could not save " + PrefabPath);
            var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(EnemyPath);
            if (enemy != null)
            {
                enemy.presentationPrefab = prefab;
                EditorUtility.SetDirty(enemy);
            }
            AssetDatabase.SaveAssets();
            return $"Bell Mother BANG rigid prefab ready: {PrefabPath}; 12 parts, scale {scale:0.###}, height {BattleActorView.MeasureLocalBounds(prefab.transform).size.y:0.###}m.";
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // Calibrate in model space, then store points in each part's local space.
    // The inner/outer 5% averages avoid selecting a single noisy surface vertex.
    static void ConfigureAttackJoints(BellMotherRigidMotion motion)
    {
        motion.tentacleRoots = new Vector3[motion.tentacles.Length];
        motion.tentacleTips = new Vector3[motion.tentacles.Length];
        for (var i = 0; i < motion.tentacles.Length; i++)
        {
            var part = motion.tentacles[i];
            var parent = part.parent;
            var center = parent.InverseTransformPoint(motion.body.position);
            var points = part.GetComponentsInChildren<MeshFilter>().SelectMany(filter =>
                filter.sharedMesh.vertices.Select(v => parent.InverseTransformPoint(filter.transform.TransformPoint(v))))
                .OrderBy(v => new Vector2(v.x - center.x, v.z - center.z).sqrMagnitude).ToArray();
            if (points.Length < 20) throw new InvalidOperationException("Missing readable tentacle geometry: " + part.name);
            var count = Mathf.Max(1, points.Length / 20);
            var inner = Vector3.zero;
            var outer = Vector3.zero;
            for (var j = 0; j < count; j++)
            {
                inner += points[j];
                outer += points[points.Length - 1 - j];
            }
            inner /= count;
            outer /= count;
            if (Vector3.ProjectOnPlane(outer - inner, Vector3.up).magnitude < 0.1f)
                throw new InvalidOperationException("Ambiguous tentacle axis: " + part.name);
            motion.tentacleRoots[i] = part.InverseTransformPoint(parent.TransformPoint(inner));
            motion.tentacleTips[i] = part.InverseTransformPoint(parent.TransformPoint(outer));
        }
    }

    public static string UpdateAttackJoints()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var motion = root.GetComponent<BellMotherRigidMotion>();
            ConfigureAttackJoints(motion);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            return "Baked six root/outer-tip pairs; preserved meshes, materials and imported hierarchy.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
