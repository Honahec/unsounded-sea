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
}
