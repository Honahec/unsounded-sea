using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UnsoundedSea.Editor
{
    [InitializeOnLoad]
    internal static class PlayModeEntry
    {
        const string MenuPath = "Unsounded Sea/Play From Main Menu";
        const string MainMenuPath = "Assets/UnsoundedSea/Scenes/MainMenu.unity";
        static string Preference => "UnsoundedSea.PlayFromMainMenu." + Application.dataPath;

        static PlayModeEntry()
        {
            EditorApplication.delayCall += Apply;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredEditMode) Apply();
            };
        }

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            EditorPrefs.SetBool(Preference, !EditorPrefs.GetBool(Preference, true));
            Apply();
        }

        [MenuItem(MenuPath, true)]
        static bool Validate()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(Preference, true));
            return !EditorApplication.isPlaying;
        }

        static void Apply()
        {
            if (EditorApplication.isPlaying) return;
            EditorSceneManager.playModeStartScene = EditorPrefs.GetBool(Preference, true)
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath) : null;
        }
    }
}
