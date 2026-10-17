using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Editor
{
    public static class VoyageEncounterLoadingSetup
    {
        const string FolderPath = "Assets/UnsoundedSea/Resources/EncounterReferences";

        public static int Configure(VoyageEncounterCatalog catalog)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play before configuring encounter loading.");
            if (EditorUtility.IsDirty(catalog)) throw new InvalidOperationException("Save pending catalog edits first.");
            if (!AssetDatabase.IsValidFolder(FolderPath)) AssetDatabase.CreateFolder(Path.GetDirectoryName(FolderPath).Replace('\\', '/'), "EncounterReferences");
            var serialized = new SerializedObject(catalog);
            var entries = serialized.FindProperty("entries");
            for (var i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var scenarioField = entry.FindPropertyRelative("scenario");
                var id = entry.FindPropertyRelative("id").stringValue;
                var path = FolderPath + "/" + id + ".asset";
                var reference = AssetDatabase.LoadAssetAtPath<VoyageScenarioReference>(path);
                var scenario = scenarioField.objectReferenceValue as BattleScenario;
                if (scenario == null) scenario = reference != null ? reference.scenario : null;
                if (scenario == null || scenario.encounter == null || scenario.encounter.id != id)
                    throw new InvalidOperationException("Missing or mismatched scenario: " + id);
                if (reference == null)
                {
                    reference = ScriptableObject.CreateInstance<VoyageScenarioReference>();
                    AssetDatabase.CreateAsset(reference, path);
                }
                reference.scenario = scenario;
                EditorUtility.SetDirty(reference); AssetDatabase.SaveAssetIfDirty(reference);
                entry.FindPropertyRelative("resourcePath").stringValue = "EncounterReferences/" + id;
                entry.FindPropertyRelative("displayName").stringValue = scenario.encounter.displayName;
                scenarioField.objectReferenceValue = null;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
            return entries.arraySize;
        }
    }
}
