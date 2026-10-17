using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Narrow, repeatable retirement of the old sample HUD; never rebuilds a scene.
public static class UiExperienceSetup
{
    const string LegacyScript="Assets/UnsoundedSea/Runtime/Presentation/BattleHud.cs";
    const string LegacyStyle="Assets/UnsoundedSea/Runtime/Presentation/BattleHud.uss";
    public static object Plan()
    {
        var references=AssetDatabase.FindAssets("t:Scene",new[]{"Assets"}).Concat(AssetDatabase.FindAssets("t:Prefab",new[]{"Assets"}))
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(path=>AssetDatabase.GetDependencies(path).Any(x=>x==LegacyScript||x==LegacyStyle)).ToArray();
        return new{references,legacyAssets=new[]{LegacyScript,LegacyStyle},buildScenes=EditorBuildSettings.scenes.Select(x=>x.path).ToArray()};
    }
    public static object Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Refusing to touch unsaved scene edits.");
        var script=AssetDatabase.LoadAssetAtPath<MonoScript>(LegacyScript);
        var candidates=AssetDatabase.FindAssets("t:Scene",new[]{"Assets"}).Concat(AssetDatabase.FindAssets("t:Prefab",new[]{"Assets"}))
            .Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(path=>AssetDatabase.GetDependencies(path).Contains(LegacyScript)).ToArray();
        if(candidates.Any(x=>!x.EndsWith(".unity",StringComparison.Ordinal)))throw new InvalidOperationException("Unexpected prefab dependency; inspect before migrating: "+string.Join(",",candidates));
        int removed=0;
        foreach(var path in candidates)
        {
            var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                var before=removed;
                foreach(var component in scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<MonoBehaviour>(true)).Where(x=>x!=null&&MonoScript.FromMonoBehaviour(x)==script).ToArray())
                {UnityEngine.Object.DestroyImmediate(component);removed++;}
                if(removed!=before){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);}
            }
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
        EditorBuildSettings.scenes=EditorBuildSettings.scenes.Where(x=>x.path!="Assets/Scenes/SampleScene.unity"&&x.path!="Assets/UnsoundedSea/Scenes/Battle.unity").ToArray();
        AssetDatabase.Refresh();
        var remaining=AssetDatabase.FindAssets("t:Scene",new[]{"Assets"}).Concat(AssetDatabase.FindAssets("t:Prefab",new[]{"Assets"})).Select(AssetDatabase.GUIDToAssetPath)
            .Where(path=>AssetDatabase.GetDependencies(path).Any(x=>x==LegacyScript||x==LegacyStyle)).ToArray();
        if(remaining.Length>0)throw new InvalidOperationException("Legacy dependencies remain: "+string.Join(",",remaining));
        AssetDatabase.DeleteAsset(LegacyStyle);AssetDatabase.DeleteAsset(LegacyScript);
        return new{removed,scenes=candidates,remaining,buildScenes=EditorBuildSettings.scenes.Select(x=>x.path).ToArray()};
    }
}
