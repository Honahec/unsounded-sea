using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnsoundedSea.Editor
{
    public static class OrdinaryEnemyStaticSetup
    {
        private const string SourceRoot = "ArtSource/Characters";
        private const string ModelRoot = "Assets/UnsoundedSea/Art/Characters/OrdinaryStatic";
        private const string PrefabRoot = "Assets/UnsoundedSea/Art/Prefabs/OrdinaryStatic";
        private static readonly (string Name, string File, float Height)[] Models =
        {
            ("FogRigger", "FogRigger_LOD0.glb", 1.85f), ("LostSailor", "LostSailor_Source.glb", 1.90f),
            ("WhiteLampSignaller", "WhiteLampSignaller_Source.glb", 1.70f), ("WetCoatPilot", "WetCoatPilot_Source.glb", 1.95f),
            ("BarrelThief", "BarrelThief_Source.glb", 1.60f), ("TideBoneScavenger", "TideBoneScavenger_Source.glb", 1.72f),
            ("BoneFluteAttendant", "BoneFluteAttendant_Source.glb", 2.05f), ("SaltMarrowPriest", "SaltMarrowPriest_Source.glb", 1.92f),
            ("EmptyHoldKnocker", "EmptyHoldKnocker_Source.glb", 1.68f), ("BoneNetStitcher", "BoneNetStitcher_Source.glb", 1.82f),
            ("InvertedSurveyHand", "InvertedSurveyHand_Source.glb", 2.00f), ("ReverseNavigator", "ReverseNavigator_Source.glb", 1.78f),
            ("MirrorTideScribe", "MirrorTideScribe_Source.glb", 1.70f), ("PlumbWatcher", "PlumbWatcher_Source.glb", 2.10f),
            ("DisplacedWatchman", "DisplacedWatchman_Source.glb", 1.86f), ("UnflaggedBoarder", "UnflaggedBoarder_Source.glb", 1.88f),
            ("MisfireGunner", "MisfireGunner_Source.glb", 1.82f), ("BlackSailMessenger", "BlackSailMessenger_Source.glb", 1.98f),
            ("AbandonShipMedic", "AbandonShipMedic_Source.glb", 1.84f), ("RustChainEscort", "RustChainEscort_Source.glb", 1.92f),
            ("StarlessPeeker", "StarlessPeeker_Source.glb", 2.08f), ("AbyssRunner", "AbyssRunner_Source.glb", 1.68f),
            ("LampOutBellkeeper", "LampOutBellkeeper_Source.glb", 1.78f), ("LastTideWitness", "LastTideWitness_Source.glb", 2.02f),
            ("RailNameTaker", "RailNameTaker_Source.glb", 2.12f)
        };

        [MenuItem("Unsounded Sea/Content/Import 25 Static Ordinary Enemies")]
        public static string Setup()
        {
            EnsureFolder(ModelRoot); EnsureFolder(PrefabRoot);
            foreach (var model in Models)
            {
                var source = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceRoot, model.File));
                if (!File.Exists(source)) throw new FileNotFoundException($"Missing archived enemy source: {source}");
                var assetPath = $"{ModelRoot}/{model.Name}.glb";
                if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath))))
                    FileUtil.CopyFileOrDirectory(source, assetPath); // Existing imports and their GUIDs are intentionally retained.
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var model in Models) CreatePrefab(model.Name, model.Height);
            return $"Imported and normalized {Models.Length} archived static enemy models.";
        }

        public static GameObject LoadPrefab(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{name}.prefab");

        private static void CreatePrefab(string name, float targetHeight)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/{name}.glb");
            if (source == null) throw new InvalidOperationException($"glTF import failed for {name}.");
            var instance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (instance == null) throw new InvalidOperationException($"Cannot instantiate {name}.");
            try
            {
                instance.name = name;
                var renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException($"{name} has no renderers.");
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                if (bounds.size.y <= 0.001f) throw new InvalidOperationException($"{name} has invalid mesh bounds.");
                var scale = targetHeight / bounds.size.y;
                instance.transform.localScale = Vector3.one * scale;
                instance.transform.position = new Vector3(-bounds.center.x * scale, -bounds.min.y * scale, -bounds.center.z * scale);
                foreach (var renderer in renderers)
                    foreach (var material in renderer.sharedMaterials)
                        if (material == null || material.shader == null || !material.shader.isSupported)
                            throw new InvalidOperationException($"{name} has a missing or unsupported material shader.");
                PrefabUtility.SaveAsPrefabAsset(instance, $"{PrefabRoot}/{name}.prefab");
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static void EnsureFolder(string path)
        {
            var parts = path.Split('/');
            for (var i = 1; i < parts.Length; i++)
            {
                var parent = string.Join("/", parts, 0, i);
                var child = parts[i];
                if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
