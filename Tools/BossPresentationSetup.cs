using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

// Updates only the three existing boss stages; M04 gets a reachable physical pole strike.
public static class BossPresentationSetup
{
    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Stop Play and save scene edits before configuring boss lighting.");
        ConfigureWindResource();
        for (var number = 2; number <= 4; number++)
        {
            var scenario = AssetDatabase.LoadAssetAtPath<BattleScenario>("Assets/UnsoundedSea/Content/Scenarios/M0" + number + ".asset");
            var path = AssetDatabase.GetAssetPath(scenario.stagePrefab);
            if (EditorUtility.IsDirty(scenario.stagePrefab)) throw new InvalidOperationException("Unsaved boss stage: " + path);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var stage = root.GetComponent<BattleStage>();
                // Bring M04 within the real pole's reach; keep a clear three-quarter view of the thrust.
                if (number == 4)
                {
                    stage.actorAnchors[0].localPosition = new Vector3(-20, -55, 120);
                    stage.battleCamera.transform.localPosition = new Vector3(115, 55, -145);
                    stage.battleCamera.transform.localRotation = Quaternion.LookRotation(new Vector3(-10, 40, 95) - stage.battleCamera.transform.localPosition, Vector3.up);
                    stage.battleCamera.fieldOfView = 60;
                }
                var bounds = BattleActorView.MeasureLocalBounds(scenario.ActorPrefab(0).transform);
                var center = stage.actorAnchors[0].TransformPoint(bounds.center);
                var lights = root.GetComponentsInChildren<Light>();
                var fill = lights.Single(l => l.name == "Sea creature soft fill");
                var rim = lights.Single(l => l.name == "Sea creature rim");
                fill.transform.position = center + new Vector3(65, 30, -75);
                fill.intensity = number == 4 ? 1800 : 1000; fill.range = 340;
                fill.color = new Color(.57f, .74f, .8f);
                rim.transform.position = center + new Vector3(-80, 65, 40);
                rim.intensity = number == 4 ? 1600 : 950; rim.range = 310;
                rim.color = new Color(.76f, .8f, .66f);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return "Retargeted M02-M04 fill/rim lights and M04's pole-reach composition; no model or encounter rebuild, other stages retained.";
    }
    public static string ConfigureWindResource()
    {
        const string folder = "Assets/UnsoundedSea/Art/Shaders/Resources";
        const string source = "Assets/UnsoundedSea/Art/Shaders/BossWind.shader";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/UnsoundedSea/Art/Shaders", "Resources");
        if (AssetDatabase.LoadAssetAtPath<Shader>(source) != null)
        {
            var error = AssetDatabase.MoveAsset(source, folder + "/BossWind.shader");
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
        }
        if (AssetDatabase.LoadAssetAtPath<Shader>(folder + "/BossWind.shader") == null)
            throw new InvalidOperationException("Missing wind shader.");
        return "BossWind shader is explicitly included through Resources; GUID retained.";
    }
}
