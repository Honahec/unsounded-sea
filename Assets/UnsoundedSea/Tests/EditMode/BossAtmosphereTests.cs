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
                foreach (var property in new[] { "baseColorTexture", "metallicRoughnessTexture", "normalTexture" })
                    Assert.That(material.GetTexture(property), Is.Not.Null, "Required texture missing: " + property);
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
        [TestCase(16f/9)] [TestCase(21f/9)] [TestCase(4f/3)]
        public void ChoirSummonsAreFramedAboveTheHandWithReadableSize(float aspect)
        {
            var scenario=Scenario(2);var stage=Object.Instantiate(scenario.stagePrefab);
            try
            {
                stage.battleCamera.aspect=aspect;
                for(int i=1;i<3;i++)
                {
                    var anchor=stage.actorAnchors[i];
                    Assert.That(anchor.IsChildOf(stage.vessel),Is.True,"Summons must follow the deck.");
                    var actor=Object.Instantiate(scenario.ActorPrefab(i),anchor);
                    var bounds=BattleActorView.MeasureLocalBounds(actor.transform);
                    var bottom=stage.battleCamera.WorldToViewportPoint(actor.transform.TransformPoint(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z)));
                    var top=stage.battleCamera.WorldToViewportPoint(actor.transform.TransformPoint(new Vector3(bounds.center.x,bounds.max.y,bounds.center.z)));
                    Assert.That(top.z,Is.GreaterThan(0));
                    Assert.That(top.y-bottom.y,Is.GreaterThan(.045f),"Summon silhouette too small to read.");
                    Assert.That(bottom.y,Is.GreaterThan(.3f),"Summon hidden by the hand.");
                    Assert.That(top.y,Is.LessThan(.6f),"Summon intrudes into the giant's silhouette.");
                    Assert.That(top.x,Is.InRange(.25f,.75f),"Summon hidden behind side HUD.");
                }
            }
            finally { Object.DestroyImmediate(stage.gameObject); }
        }
        [Test] public void ChoirPressureOriginFollowsTheJawWhileChargeStaysAtTheGullet()
        {
            var actor=Object.Instantiate(Scenario(2).ActorPrefab(0));
            try
            {
                var motion=actor.GetComponentInChildren<GiantBossMotion>();
                var head=motion.joints.Single(j=>j.bone.name=="GB_Head").bone;
                Assert.That(motion.sonicOrigin,Is.Not.Null);Assert.That(motion.sonicOrigin.IsChildOf(head),Is.True);
                Assert.That(motion.effectOrigin.name,Is.EqualTo("GB_Gullet"));
                var mouth=motion.SonicOrigin;var charge=motion.Origin;
                head.localRotation*=Quaternion.Euler(12,0,0);
                Assert.That(Vector3.Distance(motion.SonicOrigin,mouth),Is.GreaterThan(.1f));
                Assert.That(motion.Origin,Is.EqualTo(charge));
            }
            finally { Object.DestroyImmediate(actor); }
        }
    }
}
