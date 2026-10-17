using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnsoundedSea.Runtime;

public static class DeckOceanSetup
{
    public static string Apply()
    {
        if (Application.isPlaying || Enumerable.Range(0,SceneManager.sceneCount).Any(i=>SceneManager.GetSceneAt(i).isDirty))
            throw new InvalidOperationException("Exit Play and save scene edits first.");
        const string art="Assets/UnsoundedSea/Art/";
        var source=AssetDatabase.LoadAssetAtPath<Material>(art+"Voyage/VoyageOcean.mat");
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(art+"SeaBoss/DeepOceanMesh.asset");
        if(source==null||mesh==null)throw new InvalidOperationException("Voyage/Boss Uber water is required.");
        var path=art+"OceanBattle/DeckOcean.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(source);AssetDatabase.CreateAsset(material,path);}
        else material.CopyPropertiesFromMaterial(source);
        material.SetFloat("_1st_Wave_Height",.18f);material.SetFloat("_2nd_Wave_Height",.09f);
        material.SetFloat("_1st_Wave_Sharpness",.035f);material.SetFloat("_2nd_Wave_Sharpness",.025f);
        material.SetFloat("_StormStrength",.1f);material.SetVector("_Threat",Vector4.zero);
        material.SetFloat("_Normal_Strength",.85f);material.SetFloat("_Reflection_Strength",.42f);
        EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        path=art+"OceanBattle/FogSeaStage.prefab";
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var stage=root.GetComponent<BattleStage>();var surface=stage.ocean;
            surface.transform.SetLocalPositionAndRotation(Vector3.zero,Quaternion.identity);surface.transform.localScale=Vector3.one;
            surface.gameObject.layer=4;surface.GetComponent<MeshFilter>().sharedMesh=mesh;
            surface.sharedMaterial=material;surface.shadowCastingMode=ShadowCastingMode.Off;
            stage.water=root.GetComponent<BattleWater>()??root.AddComponent<BattleWater>();
            stage.water.surface=surface;stage.water.reflectionScale=.5f;stage.water.reflectionLayers=~(1<<4);
            stage.skybox=AssetDatabase.LoadAssetAtPath<Material>(art+"SeaBoss/StormSky.mat");
            stage.fogColor=new Color(.24f,.34f,.36f);stage.fogDensity=.007f;
            stage.battleCamera.farClipPlane=1800;stage.battleCamera.clearFlags=CameraClearFlags.Skybox;
            // Keep the close deck composition and bounded combat roll, with safe sub-metre waves.
            stage.vesselFollowsWater=false;
            PrefabUtility.SaveAsPrefabAsset(root,path);
        }
        finally {PrefabUtility.UnloadPrefabContents(root);}
        return "FogSeaStage now shares the Uber ocean mesh/shader with Voyage and Boss, with an independent calm deck material.";
    }
}
