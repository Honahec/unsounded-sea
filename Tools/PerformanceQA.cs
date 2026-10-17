using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Collections.Generic;
using UnsoundedSea.Runtime;
using UnsoundedSea.Editor;

public static class PerformanceQA
{
    public static int ConfigureCatalog() => VoyageEncounterLoadingSetup.Configure(
        AssetDatabase.LoadAssetAtPath<VoyageEncounterCatalog>("Assets/UnsoundedSea/Resources/VoyageEncounterCatalog.asset"));

    public static object CompareShip() => CompareModel("Assets/UnsoundedSea/Art/Prefabs/SurveyVessel.prefab", "Assets/UnsoundedSea/Art/Models/SurveyVessel_LOD0.glb", "ship");
    public static object CompareModel(string prefabPath, string modelPath, string name)
    {
        var preview = EditorSceneManager.NewPreviewScene();
        var replacements = new Dictionary<Texture, Texture2D>();
        var originals = new List<Texture2D>();
        try
        {
            var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            SceneManager.MoveGameObjectToScene(model, preview);
            var camera = new GameObject("Comparison camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, preview);
            camera.scene = preview;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
            camera.transform.position = new Vector3(22, 14, -23); camera.transform.LookAt(new Vector3(0, 3, 0));
            camera.backgroundColor = new Color(.08f, .12f, .14f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = 42;
            if (name != "ship")
            {
                var renderers=model.GetComponentsInChildren<Renderer>();
                var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                camera.transform.position=bounds.center+new Vector3(.6f,.35f,1.2f)*bounds.size.magnitude;
                camera.transform.LookAt(bounds.center);
            }
            var light = new GameObject("Comparison light").AddComponent<Light>(); light.type = LightType.Directional;
            SceneManager.MoveGameObjectToScene(light.gameObject, preview);
            light.transform.rotation = Quaternion.Euler(40, -40, 0); light.intensity = 2;
            var materials = model.GetComponentsInChildren<Renderer>().SelectMany(r => r.materials).Distinct().ToArray();
            var record = JObject.Parse(File.ReadAllText("ArtSource/RuntimeTextures/manifest.json"))["models"][modelPath];
            long compressedBytes=0, originalBytes=0;
            foreach (var item in record["textures"])
            {
                var path = (string)item["path"];
                var compressed = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, !(bool)item["srgb"]);
                ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true);
                originals.Add(texture); replacements[compressed] = texture;
                compressedBytes += Profiler.GetRuntimeMemorySizeLong(compressed);
                originalBytes += Profiler.GetRuntimeMemorySizeLong(texture);
            }
            Directory.CreateDirectory("Logs/Performance");
            var compressedImage = Render(camera);
            File.WriteAllBytes("Logs/Performance/"+name+"-bc7.png", compressedImage.EncodeToPNG());
            foreach (var material in materials)
                foreach (var property in material.GetTexturePropertyNames())
                    if (material.GetTexture(property) != null && replacements.TryGetValue(material.GetTexture(property), out var original)) material.SetTexture(property, original);
            var originalImage = Render(camera);
            File.WriteAllBytes("Logs/Performance/"+name+"-original.png", originalImage.EncodeToPNG());
            var a = originalImage.GetPixels32(); var b = compressedImage.GetPixels32();
            double error=0;
            for(int i=0;i<a.Length;i++) error += Math.Abs(a[i].r-b[i].r)+Math.Abs(a[i].g-b[i].g)+Math.Abs(a[i].b-b[i].b);
            UnityEngine.Object.DestroyImmediate(compressedImage); UnityEngine.Object.DestroyImmediate(originalImage);
            foreach (var material in materials) UnityEngine.Object.DestroyImmediate(material);
            return new { compressedBytes, originalBytes, meanRgbError=error/(a.Length*3), width=1600, height=900 };
        }
        finally
        {
            foreach (var texture in originals) UnityEngine.Object.DestroyImmediate(texture);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
    static Texture2D Render(Camera camera)
    {
        var target = RenderTexture.GetTemporary(1600,900,24,RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        try
        {
            camera.targetTexture=target; camera.Render(); RenderTexture.active=target;
            var result=new Texture2D(1600,900,TextureFormat.RGB24,false);
            result.ReadPixels(new Rect(0,0,1600,900),0,0); result.Apply(); return result;
        }
        finally { camera.targetTexture=null; RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target); }
    }
    public static object Inspect()
    {
        return new {
            playMode = EditorApplication.isPlaying,
            scenes = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(i => {
                var scene = EditorSceneManager.GetSceneAt(i);
                return new { scene.path, scene.isDirty };
            }).ToArray(),
            textures = AssetDatabase.GetDependencies("Assets/UnsoundedSea/Art/Models/SurveyVessel_LOD0.glb",true)
                .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<Texture2D>().Distinct().Select(t => new { t.name, t.width, t.height, format=t.format.ToString(), t.mipmapCount,
                    memory=Profiler.GetRuntimeMemorySizeLong(t) }).ToArray()
        };
    }
}
