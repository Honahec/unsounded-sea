using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
public static class ImportSurveyVessel
{
    public static string Run()
    {
        const string path="Assets/UnsoundedSea/Art/Models/SurveyVessel_LOD0.glb";
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(source==null)throw new Exception("Model still importing");
        var root=new GameObject("SurveyVessel");var model=(GameObject)PrefabUtility.InstantiatePrefab(source);model.transform.SetParent(root.transform,false);
        var bounds=new Bounds();bool first=true;foreach(var r in model.GetComponentsInChildren<Renderer>()){if(first){bounds=r.bounds;first=false;}else bounds.Encapsulate(r.bounds);}
        var scale=18/bounds.size.z;model.transform.localScale=Vector3.one*scale;model.transform.localPosition=new Vector3(-bounds.center.x*scale,1.45f-0.47f*scale,-bounds.center.z*scale);
        var result="source bounds "+bounds+" scale "+scale+" position "+model.transform.localPosition;
        PrefabUtility.SaveAsPrefabAsset(root,"Assets/UnsoundedSea/Art/Prefabs/SurveyVessel.prefab");UnityEngine.Object.DestroyImmediate(root);AssetDatabase.SaveAssets();return result;
    }
}
