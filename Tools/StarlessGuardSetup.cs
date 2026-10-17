using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;

public static class StarlessGuardSetup
{
    const string Root = "Assets/UnsoundedSea/Art/";
    static readonly string[] Names = { "StarlessGuardAnchor", "StarlessGuardLamp", "StarlessGuardBell", "StarlessGuardSail", "StarlessGuardEye" };

    public static string Import()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before importing guardians.");
        foreach (var name in Names)
        {
            var path = Root + "Models/" + name + "_BANG_LOD0.glb";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null || model.GetComponentsInChildren<MeshFilter>(true).Length < 2)
                throw new InvalidOperationException("Missing genuine BANG split model: " + path);
        }
        foreach (var name in Names)
        {
            var prefabPath = Root + "Prefabs/" + name + ".prefab";
            var root = new GameObject(name);
            try
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Models/" + name + "_BANG_LOD0.glb");
                var body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                var bounds = BattleActorView.MeasureLocalBounds(body.transform);
                if (bounds.size.y <= 0 || body.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m == null)))
                    throw new InvalidOperationException("Invalid guardian bounds or material: " + name);
                var scale = 40f / bounds.size.y;
                body.transform.localScale = Vector3.one * scale;
                body.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        return "Imported five BANG guardians, each 40m tall with grounded original-material geometry.";
    }
}
