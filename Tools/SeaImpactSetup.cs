using System;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;

public static class SeaImpactSetup
{
    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play first.");
        const string folder = "Assets/UnsoundedSea/Art/SeaBoss/";
        var shader = Shader.Find("UnsoundedSea/Impact Spray");
        if (shader == null) throw new InvalidOperationException("Import the spray shader first.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(folder + "ImpactSpray.mat");
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, folder + "ImpactSpray.mat");
        }
        const string path = folder + "DrownedBellStage.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            root.GetComponent<BattleStage>().water.impactSprayMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return "Assigned impact spray to existing stage; retained camera, ship, water and actor settings.";
    }
}
