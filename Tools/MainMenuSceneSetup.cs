using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

// Explicit authoring entry point. Replaces only the named menu background and leaves UI/settings intact.
public static class MainMenuSceneSetup
{
    const string Root = "Assets/UnsoundedSea/";
    const string ScenePath = Root + "Scenes/MainMenu.unity";
    const string VesselPath = Root + "Art/Prefabs/SurveyVessel.prefab";
    const string LighthousePath = Root + "Art/Prefabs/Lighthouse.prefab";
    const string WaterStagePath = Root + "Art/SeaBoss/DrownedBellStage.prefab";

    public static string ImportLighthouse()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        const string path = Root + "Art/Models/Lighthouse_LOD0.glb";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null) throw new InvalidOperationException("Missing generated lighthouse: " + path);
        var root = new GameObject("Lighthouse");
        try
        {
            var body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            var bounds = BattleActorView.MeasureLocalBounds(body.transform);
            if (bounds.size.y <= 0 || body.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m == null)))
                throw new InvalidOperationException("Invalid lighthouse geometry or materials.");
            var scale = 24f / bounds.size.y;
            body.transform.localScale = Vector3.one * scale;
            body.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z) * scale;
            PrefabUtility.SaveAsPrefabAsset(root, LighthousePath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        return LighthousePath;
    }

    public static object Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        for (var i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first.");

        var vesselPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VesselPath);
        var lighthousePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LighthousePath);
        var waterStage = AssetDatabase.LoadAssetAtPath<BattleStage>(WaterStagePath);
        if (vesselPrefab == null) throw new InvalidOperationException("Missing survey vessel: " + VesselPath);
        if (lighthousePrefab == null) throw new InvalidOperationException("Missing production lighthouse model prefab: " + LighthousePath);
        if (waterStage == null || waterStage.water == null || waterStage.water.surface == null)
            throw new InvalidOperationException("Missing reusable animated ocean: " + WaterStagePath);
        var oceanMaterial = AssetDatabase.LoadAssetAtPath<Material>(Root + "Art/Voyage/VoyageOcean.mat");
        if (oceanMaterial == null) throw new InvalidOperationException("Missing sailing ocean material.");

        var scene = EditorSceneManager.OpenScene(ScenePath);
        var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
        if (camera == null) throw new InvalidOperationException("MainMenu needs its existing camera.");
        foreach (var old in scene.GetRootGameObjects().Where(x => x.name == "Greybox Title Stage (placeholder)" || x.name == "Main Menu Sea Stage"))
            UnityEngine.Object.DestroyImmediate(old);

        var root = new GameObject("Main Menu Sea Stage");
        var vessel = (GameObject)PrefabUtility.InstantiatePrefab(vesselPrefab, root.transform);
        vessel.name = "Survey vessel";
        vessel.transform.SetLocalPositionAndRotation(new Vector3(10.5f, 0, 18), Quaternion.Euler(0, 42, 0));

        var lighthouse = (GameObject)PrefabUtility.InstantiatePrefab(lighthousePrefab, root.transform);
        lighthouse.name = "Lighthouse";
        lighthouse.transform.SetLocalPositionAndRotation(new Vector3(20, 0, 43), Quaternion.Euler(0, -20, 0));

        var sea = new GameObject("Open ocean");
        sea.transform.SetParent(root.transform, false);
        sea.layer = 4;
        sea.AddComponent<MeshFilter>().sharedMesh = waterStage.water.surface.GetComponent<MeshFilter>().sharedMesh;
        var seaRenderer = sea.AddComponent<MeshRenderer>();
        seaRenderer.sharedMaterial = oceanMaterial;
        seaRenderer.shadowCastingMode = ShadowCastingMode.Off;
        var water = root.AddComponent<BattleWater>();
        water.surface = seaRenderer;

        var moon = new GameObject("Moon through cloud").AddComponent<Light>();
        moon.transform.SetParent(root.transform, false);
        moon.type = LightType.Directional;
        moon.color = new Color(0.55f, 0.73f, 0.76f);
        moon.intensity = 1.8f;
        moon.shadows = LightShadows.Soft;
        moon.transform.rotation = Quaternion.Euler(30, -35, 0);

        var volume = new GameObject("Atmosphere").AddComponent<Volume>();
        volume.transform.SetParent(root.transform, false);
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "Art/OceanBattle/OceanGrade.asset");

        camera.transform.SetPositionAndRotation(new Vector3(-2, 10, -32), Quaternion.LookRotation(new Vector3(8, 2.5f, 25) - new Vector3(-2, 10, -32)));
        camera.fieldOfView = 46;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1800;
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.allowHDR = true;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var seaPlane = new Plane(Vector3.up, Vector3.zero);
        var boatRay = camera.ViewportPointToRay(new Vector3(0.77f, 0.26f, 0));
        var towerRay = camera.ViewportPointToRay(new Vector3(0.75f, 0.5f, 0));
        if (!seaPlane.Raycast(boatRay, out var boatDistance) || !seaPlane.Raycast(towerRay, out var towerDistance))
            throw new InvalidOperationException("Menu framing must intersect the ocean.");
        vessel.transform.position = boatRay.GetPoint(boatDistance);
        lighthouse.transform.position = towerRay.GetPoint(towerDistance);

        var presentation = root.AddComponent<MainMenuSeaPresentation>();
        presentation.water = water;
        presentation.menuCamera = camera;
        presentation.vessel = vessel.transform;
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(Root + "Art/SeaBoss/StormSky.mat");
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.18f, 0.29f, 0.32f);
        RenderSettings.fogDensity = 0.0045f;

        EditorSceneManager.SaveScene(scene);
        return new { scene = ScenePath, vessel = VesselPath, lighthouse = LighthousePath, ocean = WaterStagePath };
    }
}
