using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Editor
{
    public static class DeckAnimationSetup
    {
        private const string Root = "Assets/UnsoundedSea/Art/Prefabs/CuratedRigs/";
        public static GameObject LoadOrdinary(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Root + name + ".prefab");

        private static void CalibrateFacing(GameObject root)
        {
            float yaw;
            switch (root.name)
            {
                case "MisfireGunner": yaw = 60; break;
                case "BlackSailMessenger": case "InvertedSurveyHand": yaw = 15; break;
                default: return;
            }
            if (root.transform.childCount != 1) throw new InvalidOperationException("Unexpected deck model hierarchy: " + root.name);
            // Normalize the visible authored pose, rather than treating every wrapper's +Z as its gaze.
            root.transform.GetChild(0).localRotation = Quaternion.Euler(0, yaw, 0);
        }

        public static string CalibrateDeckFacing()
        {
            var paths = new[] { "MisfireGunner", "BlackSailMessenger", "InvertedSurveyHand" }.Select(name => Root + name + ".prefab").ToArray();
            var editing = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (editing != null && paths.Contains(editing.assetPath) && editing.scene.isDirty)
                throw new InvalidOperationException("Save the open deck prefab edits first.");
            foreach (var path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try { CalibrateFacing(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return "Calibrated the authored firearm and two oblique character poses toward +Z; preserved rigs and model GUIDs.";
        }

        [MenuItem("Unsounded Sea/Animation/Apply Deck Combat Animation")]
        public static string Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Exit Play and save scene edits before applying deck animation.");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BuildLegacyRig("Prayer", 1.8f); BuildLegacyRig("Echo", 2.1f);
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { Root.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath))
                Configure(path);
            Configure("Assets/UnsoundedSea/Art/Prefabs/Prayer.prefab");
            Configure("Assets/UnsoundedSea/Art/Prefabs/Echo.prefab");
            var changed = 0;
            foreach (var path in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/UnsoundedSea/Content" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
                if (enemy.presentationPrefab == null) continue;
                var replacement = LoadOrdinary(enemy.presentationPrefab.name);
                if (replacement == null || replacement == enemy.presentationPrefab) continue;
                enemy.presentationPrefab = replacement;
                EditorUtility.SetDirty(enemy); AssetDatabase.SaveAssetIfDirty(enemy); changed++;
            }
            return "Configured 27 deck rigs; replaced " + changed + " enemy references. No scene rebuild.";
        }

        private static void BuildLegacyRig(string name, float height)
        {
            var path = "Assets/UnsoundedSea/Art/Prefabs/" + name + ".prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnsoundedSea/Art/Models/DeckRigs/" + name + "_Rigged.glb");
            if (source == null) throw new FileNotFoundException(name + " prepared rig missing");
            var root = new GameObject(name);
            try
            {
                var model = UnityEngine.Object.Instantiate(source, root.transform);
                foreach (var a in model.GetComponentsInChildren<Animator>(true)) UnityEngine.Object.DestroyImmediate(a);
                foreach (var a in model.GetComponentsInChildren<Animation>(true)) UnityEngine.Object.DestroyImmediate(a);
                var skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
                var mesh = new Mesh();
                try
                {
                    skin.BakeMesh(mesh, true);
                    var vertices = mesh.vertices.Select(skin.transform.TransformPoint).ToArray();
                    var bounds = new Bounds(vertices[0], Vector3.zero);
                    foreach (var p in vertices) bounds.Encapsulate(p);
                    var scale = height / bounds.size.y;
                    model.transform.localScale *= scale;
                    model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
                PrefabUtility.SaveAsPrefabAsset(root, path); // Retains existing GUID and all consumers.
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void Configure(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                CalibrateFacing(root);
                var motion = root.GetComponent<DeckActorMotion>() ?? root.AddComponent<DeckActorMotion>();
                motion.style = StyleFor(root.name);
                motion.attackDuration = motion.style == DeckMotionStyle.Heavy ? 1.8f : motion.style == DeckMotionStyle.Skirmisher ? 1.18f : motion.style == DeckMotionStyle.Veiled ? 1.65f : 1.45f;
                motion.hitDuration = motion.style == DeckMotionStyle.Heavy ? .92f : .75f;
                motion.amplitude = motion.style == DeckMotionStyle.Veiled ? .8f : motion.style == DeckMotionStyle.Uncanny ? 1.1f : 1;
                if (root.name == "Prayer")
                {
                    var skirt = root.GetComponent<PrayerSkirtMotion>() ?? root.AddComponent<PrayerSkirtMotion>();
                    skirt.upperSwing = 16; skirt.hemSwing = 34;
                    var bones = root.GetComponentsInChildren<Transform>(true);
                    skirt.upperPanels = Enumerable.Range(0, 8).Select(i => bones.Single(b => b.name == $"Skirt_{i:00}_Upper")).ToArray();
                    skirt.hemPanels = Enumerable.Range(0, 8).Select(i => bones.Single(b => b.name == $"Skirt_{i:00}_Hem")).ToArray();
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        public static DeckMotionStyle StyleFor(string name)
        {
            switch (name)
            {
                case "Prayer": case "WetCoatPilot": case "LastTideWitness": return DeckMotionStyle.Veiled;
                case "Echo": case "BarrelThief": case "PlumbWatcher": case "RustChainEscort": case "UnflaggedBoarder": return DeckMotionStyle.Heavy;
                case "LostSailor": case "TideBoneScavenger": case "AbyssRunner": return DeckMotionStyle.Skirmisher;
                case "WhiteLampSignaller": case "BoneFluteAttendant": case "SaltMarrowPriest": case "MirrorTideScribe": case "ReverseNavigator": return DeckMotionStyle.Ritual;
                case "StarlessPeeker": case "RailNameTaker": case "DisplacedWatchman": case "LampOutBellkeeper": return DeckMotionStyle.Uncanny;
                case "MisfireGunner": return DeckMotionStyle.Gunner;
                default: return DeckMotionStyle.Sailor;
            }
        }
    }
}
