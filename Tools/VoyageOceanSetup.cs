using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

// Explicit authoring entry point. Updates only the Voyage scene's presentation and its own prefab/materials.
public static class VoyageOceanSetup
{
    const string Root = "Assets/UnsoundedSea/";
    const string Art = Root + "Art/Voyage";
    static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); return go;
    }
    public static object Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first.");
        var source = AssetDatabase.LoadAssetAtPath<BattleStage>(Root + "Art/SeaBoss/DrownedBellStage.prefab");
        if (source == null || source.water == null) throw new InvalidOperationException("The shared survey vessel and Uber ocean must be imported first.");
        var scene = EditorSceneManager.OpenScene(Root + "Scenes/Voyage.unity");
        var hud = UnityEngine.Object.FindAnyObjectByType<VoyageHud>();
        var presentation = hud.GetComponent<VoyageSailingPresentation>();
        var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
        if (!AssetDatabase.IsValidFolder(Art)) AssetDatabase.CreateFolder(Root + "Art", "Voyage");
        var material = AssetDatabase.LoadAssetAtPath<Material>(Art + "/VoyageOcean.mat");
        if (material == null) { material = new Material(source.water.surface.sharedMaterial); AssetDatabase.CreateAsset(material, Art + "/VoyageOcean.mat"); }
        else material.CopyPropertiesFromMaterial(source.water.surface.sharedMaterial);
        // The same opaque, irregular sea, without a giant creature's disturbance.
        material.SetFloat("_StormStrength", 0.55f); material.SetVector("_Threat", Vector4.zero);
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        var foam = AssetDatabase.LoadAssetAtPath<Material>(Art + "/VesselWake.mat");
        if (foam == null) { foam = new Material(Shader.Find("UnsoundedSea/Voyage Wake")); AssetDatabase.CreateAsset(foam, Art + "/VesselWake.mat"); }
        foreach (var old in scene.GetRootGameObjects().Where(g => g.name == "Greybox Navigation Stage (placeholder)" || g.name == "Voyage Sea Stage" || g.name == "VoyageSeaStage"))
            UnityEngine.Object.DestroyImmediate(old);
        var root = new GameObject("Voyage Sea Stage");
        var vessel = UnityEngine.Object.Instantiate(source.vessel.gameObject, root.transform).transform;
        vessel.name = "Survey vessel rig"; vessel.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        // Keep the ship as a nested prefab so later model/material revisions propagate here.
        UnityEngine.Object.DestroyImmediate(vessel.Find("SurveyVessel").gameObject);
        var ship = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Art/Prefabs/SurveyVessel.prefab"));
        ship.transform.SetParent(vessel, false);
        var surface = Child("Open ocean", root.transform); surface.layer = 4;
        surface.AddComponent<MeshFilter>().sharedMesh = source.water.surface.GetComponent<MeshFilter>().sharedMesh;
        var renderer = surface.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
        var water = root.AddComponent<BattleWater>(); water.surface = renderer;
        var wakeObject = Child("Vessel wake", root.transform); wakeObject.layer = 4;
        var wake = wakeObject.AddComponent<VoyageWake>(); wake.GetComponent<MeshRenderer>().sharedMaterial = foam;
        var moon = Child("Moon through cloud", root.transform).AddComponent<Light>();
        moon.type = LightType.Directional; moon.color = new Color(0.55f,0.73f,0.76f); moon.intensity = 1.8f;
        moon.transform.rotation = Quaternion.Euler(30,-35,0); moon.shadows = LightShadows.Soft;
        var fill = Child("Vessel sky fill", vessel).AddComponent<Light>();
        fill.transform.localPosition = new Vector3(12,9,-8); fill.type = LightType.Point; fill.range = 35; fill.intensity = 100;
        fill.color = new Color(0.43f,0.62f,0.7f); fill.shadows = LightShadows.None;
        var volume = Child("Atmosphere", root.transform).AddComponent<Volume>();
        volume.isGlobal = true; volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Root + "Art/OceanBattle/OceanGrade.asset");
        var ambience = Child("Sea and hull soundscape", root.transform).AddComponent<AudioSource>();
        ambience.clip = source.ambience.clip; ambience.loop = true; ambience.volume = 0.45f; ambience.playOnAwake = true; ambience.spatialBlend = 0;
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, Art + "/VoyageSeaStage.prefab");
        UnityEngine.Object.DestroyImmediate(root);
        root = (GameObject)PrefabUtility.InstantiatePrefab(prefab); root.name = "Voyage Sea Stage";
        water = root.GetComponent<BattleWater>(); vessel = root.transform.Find("Survey vessel rig");
        wake = root.GetComponentInChildren<VoyageWake>(); ambience = root.GetComponentInChildren<AudioSource>();
        camera.transform.position = new Vector3(25,17,-29); camera.transform.LookAt(new Vector3(0,0,4));
        camera.fieldOfView = 48; camera.nearClipPlane = 0.1f; camera.farClipPlane = 1800;
        camera.clearFlags = CameraClearFlags.Skybox; camera.allowHDR = true; camera.tag = "MainCamera";
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        if (camera.GetComponent<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();
        var so = new SerializedObject(presentation);
        so.FindProperty("voyageCamera").objectReferenceValue = camera;
        so.FindProperty("vessel").objectReferenceValue = vessel;
        so.FindProperty("water").objectReferenceValue = water;
        so.FindProperty("wake").objectReferenceValue = wake;
        so.FindProperty("ambience").objectReferenceValue = ambience;
        so.FindProperty("cameraOffset").vector3Value = new Vector3(25,17,-29);
        so.FindProperty("cameraTarget").vector3Value = new Vector3(0,0,4);
        so.ApplyModifiedPropertiesWithoutUndo();
        RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(Root + "Art/SeaBoss/StormSky.mat");
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.3f,0.45f,0.48f); RenderSettings.fogDensity = 0.0035f;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.18f,0.27f,0.3f);
        RenderSettings.ambientEquatorColor = new Color(0.09f,0.16f,0.17f);
        RenderSettings.ambientGroundColor = new Color(0.025f,0.045f,0.05f);
        EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        return new { scene = scene.path, prefab = AssetDatabase.GetAssetPath(prefab), reusedMesh = AssetDatabase.GetAssetPath(water.surface.GetComponent<MeshFilter>().sharedMesh) };
    }
}
