using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnsoundedSea.Runtime;

// Reapply after SeaBossSetup.Build; only replaces the encounter's water configuration.
public static class UberWaterSetup
{
    const string Art = "Assets/UnsoundedSea/Art/SeaBoss";
    const string Vendor = "Assets/ThirdParty/UberStylizedWater/Textures/";

    public static object Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        var shader = Shader.Find("UberStylizedWater");
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("Import Uber Stylized Water first.");
        var materialPath = Art + "/DeepOcean.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, materialPath); }
        mat.shader = shader;
        foreach (var keyword in mat.shaderKeywords) mat.DisableKeyword(keyword);
        foreach (var keyword in new[] { "_ENABLENORMAL", "_ENABLEWAVE", "_ENABLEPLANERREFLECTION", "_ENABLEREFRACTION" })
        { mat.EnableKeyword(keyword); mat.SetFloat(keyword, 1); }
        mat.SetFloat("_UseSeaTime", 1);
        mat.SetFloat("_SeaTime", 0);
        mat.SetColor("_Color_Deep", new Color(0.018f, 0.065f, 0.08f, 1));
        mat.SetColor("_Color_Shallow", new Color(0.08f, 0.19f, 0.20f, 0.65f));
        mat.SetColor("_Wave_Top_Color", new Color(0.07f, 0.16f, 0.18f, 0.12f));
        mat.SetFloat("_Water_Depth", 2.5f);
        mat.SetFloat("_WorldSpaceDepth", 1);
        mat.SetFloat("_DistanceMask_Start", 20);
        mat.SetFloat("_DistanceMask_Fade", 100);
        mat.SetFloat("_ShoreFade", 1);
        mat.SetFloat("_ShoreFade_Smoothness", 0.25f);
        mat.SetTexture("_Normal_Map", Texture("Normal 1.png"));
        mat.SetFloat("_Normal_Scale", 8);
        mat.SetFloat("_Normal_Strength", 0.85f);
        mat.SetFloat("_Normal_DistanceStrength", 0.075f);
        mat.SetFloat("_Normal_Pan", 0.09f);
        mat.SetFloat("_Reflection_Strength", 0.95f);
        mat.SetFloat("_Reflection_Fresnel", 1.5f);
        mat.SetFloat("_Reflection_Distortion", 1.2f);
        mat.SetFloat("_Refraction_Strength", 0.04f);
        mat.SetFloat("_Refraction_Distance_Strength", 0.01f);
        mat.SetColor("_Specular_Color", new Color(1.3f, 1.8f, 1.9f, 1));
        mat.SetFloat("_Specular_Spread", 0.45f);
        mat.SetFloat("_Specular_Hardness", 0.15f);
        mat.SetFloat("_Specular_Size", 0.15f);
        mat.SetFloat("_1st_Wave_Length", 9);
        mat.SetFloat("_1st_Wave_Height", 0.27f);
        mat.SetFloat("_1st_Wave_Speed", 0.55f);
        mat.SetFloat("_1st_Wave_Sharpness", 0.18f);
        mat.SetVector("_1st_Wave_Direction", new Vector4(0.8f, 0, 0.6f, 0));
        mat.SetFloat("_2nd_Wave_Length", 4.5f);
        mat.SetFloat("_2nd_Wave_Height", 0.10f);
        mat.SetFloat("_2nd_Wave_Speed", 0.42f);
        mat.SetFloat("_2nd_Wave_Sharpness", 0.12f);
        mat.SetVector("_2nd_Wave_Direction", new Vector4(-0.4f, 0, 0.9f, 0));
        mat.SetTexture("_SurfFoam_Map", Texture("Foam 1.png"));
        mat.SetTexture("_SurfaceDistortion_Map", Texture("Noise 1.png"));
        mat.SetFloat("_Enable_SurfaceFoam", 0);
        mat.SetColor("_SurfFoam_Color", new Color(0.16f, 0.27f, 0.28f, 0.3f));
        mat.SetFloat("_SurfFoam_Scale", 8);
        mat.SetFloat("_SurfFoam_AlphaBlend", 0.75f);
        mat.SetFloat("_SurfFoam_Edge", 0.08f);
        mat.SetFloat("_SurfFoam_EdgeSmooth", 0.16f);
        mat.SetVector("_SurfFoam_Pan", new Vector4(0.015f, 0.025f, 0, 0));
        mat.SetFloat("_SurfaceDistortion_Strength", 0.18f);
        mat.SetVector("_SurfaceDistortion_Pan", new Vector4(0.04f, 0.02f, 0, 0));
        mat.SetFloat("_Enable_Intersection", 1);
        mat.SetTexture("_InterSec_Foam_Mask", Texture("Foam 1.png"));
        mat.SetColor("_InterSec_Color", new Color(0.28f, 0.39f, 0.38f, 0.75f));
        mat.SetFloat("_InterSec_Width", 1.1f);
        mat.SetFloat("_InterSec_Dissolve", 1.2f);
        mat.SetFloat("_InterSec_Foam_Scale", 2.4f);
        mat.SetFloat("_InterSec_Foam_Smooth", 0.18f);
        mat.SetFloat("_InterSec_Edge_Fade", 0.35f);
        mat.SetVector("_InterSec_Foam_Pan", new Vector4(0.035f, 0.015f, 0, 0));
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssetIfDirty(mat);

        var mesh = WaterMesh();
        var path = Art + "/DrownedBellStage.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var stage = root.GetComponent<BattleStage>();
            stage.fogColor = new Color(0.3f, 0.45f, 0.48f);
            stage.fogDensity = 0.0025f;
            var water = stage.ocean;
            water.gameObject.layer = 4;
            water.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            water.transform.localScale = Vector3.one;
            water.GetComponent<MeshFilter>().sharedMesh = mesh;
            water.sharedMaterial = mat;
            water.shadowCastingMode = ShadowCastingMode.Off;
            stage.water = root.GetComponent<BattleWater>() ?? root.AddComponent<BattleWater>();
            stage.water.surface = water;
            stage.water.reflectionScale = 0.5f;
            stage.water.reflectionLayers = ~(1 << 4);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return new { materialPath, mesh.vertexCount, triangles = mesh.triangles.Length / 3 };
    }

    static Texture2D Texture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Vendor + name);

    static Mesh WaterMesh()
    {
        // Dense between vessel and Boss; extend behind and beside the exterior camera into fog.
        const int width = 320, depth = 256;
        var points = new Vector3[(width + 1) * (depth + 1)];
        var normals = new Vector3[points.Length];
        var tangents = new Vector4[points.Length];
        var uv = new Vector2[points.Length];
        for (int z = 0; z <= depth; z++)
        for (int x = 0; x <= width; x++)
        {
            float u = (x / (float)width - 0.5f) * 2;
            float v = z / (float)depth * 2 - 1;
            var p = new Vector3(50 * u + 2150 * Mathf.Pow(u, 5), 0,
                130 + 220 * v + 2050 * Mathf.Pow(v, 5));
            int i = z * (width + 1) + x;
            points[i] = p; normals[i] = Vector3.up; tangents[i] = new Vector4(1, 0, 0, -1); uv[i] = new Vector2(p.x, p.z);
        }
        var indices = new int[width * depth * 6];
        for (int z = 0, t = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
        {
            int i = z * (width + 1) + x;
            indices[t++] = i; indices[t++] = i + width + 1; indices[t++] = i + 1;
            indices[t++] = i + 1; indices[t++] = i + width + 1; indices[t++] = i + width + 2;
        }
        var path = Art + "/DeepOceanMesh.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
        mesh.Clear(); mesh.name = "DeepOceanMesh"; mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = points; mesh.normals = normals; mesh.tangents = tangents; mesh.uv = uv; mesh.triangles = indices;
        mesh.bounds = new Bounds(new Vector3(0, 0, 130), new Vector3(4404, 8, 4544));
        EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
        return mesh;
    }
}
