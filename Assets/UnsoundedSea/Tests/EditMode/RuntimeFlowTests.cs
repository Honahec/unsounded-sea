using System;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using System.Linq;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class RuntimeFlowTests
    {
        private string crew;
        private int volume, music, effects, resolution;

        [SetUp]
        public void RememberPreferences()
        {
            crew = CardDeckPreparation.CrewId;
            volume = GameSettings.Volume;
            music = GameSettings.MusicVolume;
            effects = GameSettings.EffectsVolume;
            resolution = GameSettings.Resolution;
        }

        [TearDown]
        public void RestorePreferences()
        {
            CardDeckPreparation.CrewId = crew;
            GameSettings.Volume = volume;
            GameSettings.MusicVolume = music;
            GameSettings.EffectsVolume = effects;
            GameSettings.Resolution = resolution;
        }

        [Test]
        public void InitialCrewPreferencePersistsOnlySupportedCrew()
        {
            CardDeckPreparation.CrewId = "listener";
            Assert.That(CardDeckPreparation.CrewId, Is.EqualTo("listener"));
            CardDeckPreparation.CrewId = "medic";
            Assert.That(CardDeckPreparation.CrewId, Is.EqualTo("medic"));
            Assert.Throws<ArgumentException>(() => CardDeckPreparation.CrewId = "engineer");
        }

        [Test]
        public void MusicAndEffectsVolumesRouteIndependentlyWhileMasterRemainsGlobal()
        {
            var host = new GameObject("settings-audio-test");
            try
            {
                var source = host.AddComponent<AudioSource>();
                source.volume = 0.8f;
                GameSettings.MusicVolume = 5;
                GameSettings.ApplyAudioSource(source, true);
                Assert.That(source.volume, Is.EqualTo(0.4f).Within(0.001f));

                var effectsHost = new GameObject("settings-effects-test");
                try
                {
                    var effectsSource = effectsHost.AddComponent<AudioSource>();
                    effectsSource.volume = 0.6f;
                    GameSettings.EffectsVolume = 2;
                    GameSettings.ApplyAudioSource(effectsSource, false);
                    Assert.That(effectsSource.volume, Is.EqualTo(0.12f).Within(0.001f));
                }
                finally { UnityEngine.Object.DestroyImmediate(effectsHost); }

                GameSettings.Volume = 3;
                Assert.That(AudioListener.volume, Is.EqualTo(0.3f).Within(0.001f));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void ResolutionPreferenceIsClamped()
        {
            GameSettings.Resolution = -1;
            Assert.That(GameSettings.Resolution, Is.Zero);
            GameSettings.Resolution = int.MaxValue;
            Assert.That(GameSettings.Resolution, Is.EqualTo(GameSettings.ResolutionLabels.Length - 1));
        }

        [Test]
        public void MenuCatalogDoesNotLoadBattleModelsOrStages()
        {
            const string path = "Assets/UnsoundedSea/Resources/VoyageEncounterCatalog.asset";
            var dependencies = AssetDatabase.GetDependencies(path, true);
            Assert.That(dependencies.Any(p => p.EndsWith(".glb") || p.EndsWith(".prefab")), Is.False,
                "Reading menu metadata or tools must not depend on battle presentation.");
            Assert.That(VoyageEncounterCatalog.MenuEntries.Count, Is.EqualTo(33));
            foreach (var entry in VoyageEncounterCatalog.MenuEntries)
            {
                Assert.That(entry.scenario, Is.Null);
                Assert.That(entry.displayName, Is.Not.Empty);
                Assert.That(VoyageEncounterCatalog.Resolve(entry.id).encounter.id, Is.EqualTo(entry.id));
            }
        }

        [Test]
        public void InvalidSceneDoesNotLeaveLoadingLocked()
        {
            Assert.Throws<InvalidOperationException>(() => GameSceneLoader.Load("missing-performance-test-scene"));
            Assert.That(GameSceneLoader.IsLoading, Is.False);
        }
    }
}
