using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class ContentTests
    {
        [TestCase("ordinary")]
        [TestCase("unknown")]
        [TestCase("seal")]
        [TestCase("effects")]
        public void SampleEncounterAssetsCreateIndependentSessions(string id)
        {
            var asset = AssetDatabase.LoadAssetAtPath<EncounterDefinition>($"Assets/UnsoundedSea/Content/Encounters/{id}.asset");
            Assert.That(asset, Is.Not.Null, "Run Unsounded Sea/Create or Open Framework Sandbox first.");
            var first = new BattleSession(asset.Build(), 1); var second = new BattleSession(asset.Build(), 1);
            first.Execute(BattleCommand.EndTurn());
            Assert.That(second.View.Turn, Is.EqualTo(1)); Assert.That(first.View.Turn, Is.EqualTo(2));
            Assert.That(asset.playerHealth, Is.EqualTo(60));
        }

        [Test] public void SavedSandboxSceneHasPersistentConfigurationReferences()
        {
            const string path = "Assets/UnsoundedSea/Scenes/FrameworkSandbox.unity";
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                BattleSandbox sandbox = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.TryGetComponent<BattleSandbox>(out var found)) sandbox = found;
                Assert.That(sandbox, Is.Not.Null);
                var array = new SerializedObject(sandbox).FindProperty("encounters");
                Assert.That(array.arraySize, Is.EqualTo(4));
                for (var i = 0; i < array.arraySize; i++) Assert.That(array.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null);
                Assert.That(sandbox.GetComponent<UIDocument>().panelSettings, Is.Not.Null);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        [Test] public void InvalidInvestigationTypeIsRejectedBeforeUse()
        { Assert.Throws<System.ArgumentException>(() => new ToolSpec("broken", "Broken", (InvestigationKind)999)); }
    }
}
