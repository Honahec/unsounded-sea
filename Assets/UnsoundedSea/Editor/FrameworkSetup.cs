using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Editor
{
    public static class FrameworkSetup
    {
        private const string Root = "Assets/UnsoundedSea";

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var split = path.LastIndexOf('/'); Folder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
        private static T Asset<T>(string folder, string name, Action<T> initialize) where T : ScriptableObject
        {
            Folder(folder); var path = folder + "/" + name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) { asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); }
            initialize(asset); EditorUtility.SetDirty(asset); return asset;
        }
        private static CardDefinition Card(string id) =>
            AssetDatabase.LoadAssetAtPath<CardDefinition>(Root + "/Content/Cards/" + id + ".asset");

        [MenuItem("Unsounded Sea/Content/Synchronize Card Catalog")]
        public static string SyncCardCatalog()
        {
            var folder = Root + "/Content/Cards";
            Folder(folder);
            foreach (var spec in CardCatalog.All)
            {
                var path = folder + "/" + spec.Id + ".asset";
                var card = AssetDatabase.LoadAssetAtPath<CardDefinition>(path);
                if (card == null)
                {
                    card = ScriptableObject.CreateInstance<CardDefinition>();
                    AssetDatabase.CreateAsset(card, path);
                }
                card.id = spec.Id;
                card.displayName = spec.Name;
                card.description = spec.Description;
                card.actionCost = spec.Cost;
                card.sanityCost = spec.SanityCost;
                card.exhaust = spec.Exhaust;
                card.effects = spec.Effects.Select(effect => new CardEffectData { effectId = effect.Id, amount = effect.Amount }).ToArray();
                EditorUtility.SetDirty(card);
                AssetDatabase.SaveAssetIfDirty(card);
            }
            return CardCatalog.All.Count + " card assets synchronized.";
        }

        [MenuItem("Unsounded Sea/Open Main Menu")]
        public static string OpenMainMenu()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode before opening a scene.");
            for(var i=0;i<SceneManager.sceneCount;i++)
                if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Save current scene edits before opening the main menu.");
            const string path=Root+"/Scenes/MainMenu.unity";
            EditorSceneManager.OpenScene(path);
            return path;
        }
        [MenuItem("Unsounded Sea/Validate All Encounters")]
        public static void ValidateContent()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:EncounterDefinition", new[] { Root }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<EncounterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                new BattleSession(asset.Build(), 1);
            }
            Debug.Log("Unsounded Sea: all encounter definitions are valid.");
        }
    }
}
