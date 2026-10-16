using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;

// Run in Play mode in SeaBossBattle; complements SeaBossQA's real UI / win / loss flows.
public static class UberWaterQA
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static BattleDirector Director => UnityEngine.Object.FindAnyObjectByType<BattleDirector>();

    public static async Task<object> Lifecycle()
    {
        var director = Director;
        var water = director.Stage.water;
        Check(water != null, "Sea Boss has no water adapter");
        var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/UnsoundedSea/Art/SeaBoss/DeepOcean.mat");
        var saved = EditorJsonUtility.ToJson(source);
        var time = water.SeaTime;
        await Task.Delay(250);
        Check(water.SeaTime > time, "Water clock is not advancing");
        var rt = water.Reflections.ReflectionTexture;
        Check(rt != null && rt.IsCreated() && rt.width > 0, "No rendered reflection target");
        Check(Camera.allCamerasCount == 1, "Reflection camera must not render as an extra game camera");
        Check(!GL.invertCulling, "Reflection leaked inverted culling");
        director.SetPaused(true);
        await Task.Delay(100);
        time = water.SeaTime;
        var reflectionHash = TextureHash(rt);
        await Task.Delay(250);
        Check(water.SeaTime == time && water.Reflections.suspended, "Pause did not freeze water");
        Check(TextureHash(rt) == reflectionHash, "Paused reflection is still changing");
        director.SetPaused(false);
        await Task.Delay(200);
        Check(water.SeaTime > time, "Resume did not advance water");
        var oldManager = water.Reflections;
        var oldMaterial = water.surface.sharedMaterial;
        for (int i = 0; i < 3; i++)
        {
            director.Restart();
            await Task.Delay(200);
            Check(UnityEngine.Object.FindObjectsByType<PlanarReflectionManager>().Length == 1, "Manager leaked on restart");
            Check(UnityEngine.Object.FindObjectsByType<PlanarReflectionVolume>().Length == 1, "Volume leaked on restart");
            Check(UnityEngine.Resources.FindObjectsOfTypeAll<Camera>().Count(c => c.cameraType == CameraType.Reflection) == 1, "Helper camera leaked on restart");
        }
        Check(oldManager == null && oldMaterial == null, "Restart retained previous water resources");
        Check(saved == EditorJsonUtility.ToJson(source), "Runtime changed the saved source material");
        return new { restartCount = 3, pausedClockAndReflection = true, sourceUnchanged = true,
            reflectionSize = director.Stage.water.Reflections.ReflectionTexture.width + "x" + director.Stage.water.Reflections.ReflectionTexture.height };
    }

    public static object NoWaterLeaks()
    {
        Check(UnityEngine.Object.FindObjectsByType<BattleWater>().Length == 0, "Water survived menu / original battle transition");
        Check(UnityEngine.Object.FindObjectsByType<PlanarReflectionManager>().Length == 0, "Reflection manager survived scene exit");
        Check(UnityEngine.Object.FindObjectsByType<PlanarReflectionVolume>().Length == 0, "Reflection volume survived scene exit");
        Check(UnityEngine.Resources.FindObjectsOfTypeAll<Camera>().All(c => c.cameraType != CameraType.Reflection), "Helper camera survived scene exit");
        return "No water objects after scene exit";
    }

    static uint TextureHash(RenderTexture target)
    {
        var previous = RenderTexture.active;
        var sample = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target;
            sample.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            sample.Apply();
            uint hash = 2166136261;
            foreach (byte b in sample.GetRawTextureData<byte>()) hash = unchecked((hash ^ b) * 16777619);
            return hash;
        }
        finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(sample); }
    }
}
