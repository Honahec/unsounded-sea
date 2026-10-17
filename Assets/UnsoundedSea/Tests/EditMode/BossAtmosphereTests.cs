using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnsoundedSea.Runtime;
using Object = UnityEngine.Object;

namespace UnsoundedSea.Tests
{
    public sealed class BossAtmosphereTests
    {
        static BattleScenario Scenario(int number) => AssetDatabase.LoadAssetAtPath<BattleScenario>("Assets/UnsoundedSea/Content/Scenarios/M0"+number+".asset");
        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void StageOwnsSkyReflectionAndRestoresEntryEnvironment(int number)
        {
            var sky = RenderSettings.skybox;
            var mode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientSkyColor;
            var reflection = RenderSettings.customReflectionTexture;
            var reflectionMode = RenderSettings.defaultReflectionMode;
            var intensity = RenderSettings.reflectionIntensity;
            var stage = Object.Instantiate(Scenario(number).stagePrefab);
            var environment = stage.GetComponent<BattleEnvironmentLighting>();
            // EditMode does not dispatch MonoBehaviour lifecycle messages. Exercise both explicitly.
            void Lifecycle(MonoBehaviour component, string method) => component.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(component,null);
            Lifecycle(stage,"OnEnable"); Lifecycle(environment,"OnEnable");
            try
            {
                var lighting = stage.GetComponent<BattleEnvironmentLighting>();
                Assert.That(stage.skybox, Is.Not.Null);
                Assert.That(stage.skybox.shader.isSupported, Is.True);
                Assert.That(RenderSettings.skybox, Is.EqualTo(stage.skybox));
                Assert.That(lighting.reflection, Is.Not.Null);
                Assert.That(RenderSettings.customReflectionTexture, Is.EqualTo(lighting.reflection));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
                Assert.That(lighting.reflection.GetPixels(CubemapFace.PositiveZ).Any(c=>c.maxColorComponent>.001f), Is.True, "Baked reflection must not be black");
            }
            finally { Lifecycle(environment,"OnDisable"); Lifecycle(stage,"OnDisable"); Object.DestroyImmediate(stage.gameObject); }
            Assert.That(RenderSettings.skybox, Is.EqualTo(sky));
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(mode));
            Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(ambient));
            Assert.That(RenderSettings.customReflectionTexture, Is.EqualTo(reflection));
            Assert.That(RenderSettings.defaultReflectionMode, Is.EqualTo(reflectionMode));
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(intensity));
        }
        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void SurfaceOverridesRetainOriginalTextures(int number)
        {
            var prefab = Scenario(number).ActorPrefab(0);
            var motion = prefab.GetComponentInChildren<GiantBossMotion>();
            var sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(motion.gameObject);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            var originals = source.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials).Distinct().ToDictionary(m=>m.name);
            foreach (var material in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(r=>r.sharedMaterials))
            {
                var original = originals[material.name];
                Assert.That(material, Is.Not.SameAs(original));
                Assert.That(material.shader.isSupported, Is.True);
                foreach (var property in original.GetTexturePropertyNames())
                    Assert.That(material.GetTexture(property), Is.EqualTo(original.GetTexture(property)), property);
                Assert.That(material.GetFloat("metallicFactor"), Is.LessThan(original.GetFloat("metallicFactor")));
            }
        }
        [Test] public void ChoirStaysAboveTheSeaWithoutWaterEmergence()
        {
            var scenario = Scenario(2); var anchor = scenario.stagePrefab.actorAnchors[0];
            var placement = anchor.GetComponent<BattleAnchor>();
            var bounds = BattleActorView.MeasureLocalBounds(scenario.ActorPrefab(0).transform);
            Assert.That(anchor.TransformPoint(new Vector3(0,bounds.min.y,0)).y, Is.GreaterThan(20));
            Assert.That(placement.emergeDepth, Is.Zero);
            Assert.That(placement.waterRadius, Is.Zero);
        }
        [Test] public void SonicFrontWaitsForArrivalAndUsesThePausedClock()
        {
            var go = new GameObject("Sonic test");
            try
            {
                var sonic = go.AddComponent<BossSonicEffect>(); var contacts = 0; sonic.Contact += ()=>contacts++;
                sonic.Launch(new Vector3(0,100,300),Vector3.zero);
                sonic.Tick(.6f,false); Assert.That(contacts, Is.Zero);
                var line=go.GetComponentInChildren<LineRenderer>();var position=line.GetPosition(0);
                sonic.Tick(0,false); Assert.That(line.GetPosition(0),Is.EqualTo(position));
                sonic.Tick(.21f,false); Assert.That(contacts,Is.EqualTo(1));
                sonic.Tick(1,false); Assert.That(contacts,Is.EqualTo(1));Assert.That(sonic.Busy,Is.False);
                Assert.That(go.GetComponentsInChildren<LineRenderer>().All(l=>!l.enabled),Is.True);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
