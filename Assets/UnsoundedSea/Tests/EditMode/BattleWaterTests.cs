using NUnit.Framework;
using UnityEngine;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class BattleWaterTests
    {
        private GameObject root;
        private Material source;
        private BattleWater water;
        private Camera camera;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Water test");
            source = new Material(Shader.Find("UberStylizedWater"));
            var surface = new GameObject("Surface");
            surface.transform.SetParent(root.transform);
            var renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = source;
            var view = new GameObject("Camera");
            view.transform.SetParent(root.transform);
            camera = view.AddComponent<Camera>();
            camera.enabled = false;
            water = root.AddComponent<BattleWater>();
            water.surface = renderer;
            water.reflectionLayers = 1 << 7;
            water.Bind(camera);
        }

        [TearDown]
        public void TearDown()
        {
            water.Unbind();
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(source);
        }

        [Test]
        public void PauseFreezesClockAndAccessibilityChangesPreservePhase()
        {
            water.Tick(2, false, false, Vector4.zero);
            water.Tick(10, true, false, Vector4.zero);
            Assert.That(water.SeaTime, Is.EqualTo(2));
            Assert.That(water.Reflections.suspended, Is.True);
            water.Tick(4, false, true, Vector4.zero);
            Assert.That(water.SeaTime, Is.EqualTo(3));
            water.Tick(1, false, false, Vector4.zero);
            Assert.That(water.SeaTime, Is.EqualTo(4));
            Assert.That(water.Reflections.suspended, Is.False);
            var block = new MaterialPropertyBlock();
            water.surface.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_SeaTime"), Is.EqualTo(4));
            Assert.That(source.GetFloat("_SeaTime"), Is.Zero, "Clock must not dirty the source material");
        }

        [Test]
        public void VesselSampleTracksMaterialWavePhaseAndPausedClock()
        {
            source.EnableKeyword("_ENABLEWAVE");
            source.SetFloat("_ENABLEWAVE", 1);
            source.SetFloat("_1st_Wave_Length", 2 * Mathf.PI);
            source.SetFloat("_1st_Wave_Height", 2);
            source.SetFloat("_1st_Wave_Sharpness", 0.4f);
            source.SetFloat("_1st_Wave_Speed", 1 / Mathf.Sqrt(9.8f));
            source.SetVector("_1st_Wave_Direction", Vector3.right);
            source.SetFloat("_2nd_Wave_Height", 0.1f);
            source.SetFloat("_2nd_Wave_Sharpness", 0);
            source.SetFloat("_2nd_Wave_Speed", 0);
            source.SetVector("_2nd_Wave_Direction", Vector3.forward);
            water.Bind(camera);
            Assert.That(water.SampleDisplacement(Vector3.zero).y, Is.EqualTo(2.1f).Within(0.0001f));
            water.Tick(Mathf.PI / 2, false, false, Vector4.zero);
            var quarter = water.SampleDisplacement(Vector3.zero);
            Assert.That(quarter.x, Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(quarter.y, Is.EqualTo(0.1f).Within(0.0001f));
            water.Tick(20, true, false, Vector4.zero);
            Assert.That(water.SampleDisplacement(Vector3.zero), Is.EqualTo(quarter));
            Assert.That(water.SampleDisplacement(Vector3.right * (Mathf.PI / 2)).y, Is.EqualTo(2.1f).Within(0.0001f));
            water.Unbind();
            Assert.That(water.SampleDisplacement(Vector3.zero), Is.EqualTo(Vector3.zero));
            source.DisableKeyword("_ENABLEWAVE");source.SetFloat("_ENABLEWAVE", 0);water.Bind(camera);
            Assert.That(water.SampleDisplacement(Vector3.zero), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ImpactTravelsToVesselThenExpiresAndRebindClearsIt()
        {
            source.EnableKeyword("_ENABLEWAVE");
            source.SetFloat("_ENABLEWAVE", 1);
            water.Bind(camera);
            var ship = Vector3.right * 135;
            water.EmitImpact(Vector3.zero);
            Assert.That(water.SampleImpactHeight(ship), Is.Zero);
            water.Tick(1.2f, false, false, Vector4.zero);
            Assert.That(water.SampleImpactHeight(ship), Is.Zero, "Ship must not react before the front arrives");
            water.Tick(1.8f, false, false, Vector4.zero);
            var crest = water.SampleImpactHeight(ship);
            Assert.That(crest, Is.InRange(48f, 80f));
            Assert.That(water.SampleImpactSurge(ship).x, Is.GreaterThan(0));
            water.Tick(10, true, false, Vector4.zero);
            Assert.That(water.SampleImpactHeight(ship), Is.EqualTo(crest));
            var block = new MaterialPropertyBlock();
            water.surface.GetPropertyBlock(block);
            Assert.That(block.GetVectorArray("_SeaImpacts"), Has.Length.EqualTo(SeaImpactWave.Capacity));
            water.Tick(10, false, false, Vector4.zero);
            Assert.That(water.SampleImpactHeight(ship), Is.Zero);
            water.EmitImpact(ship);
            water.Bind(camera);
            Assert.That(water.ImpactCount, Is.Zero);
            Assert.That(water.SampleImpactHeight(ship), Is.Zero);
        }

        [Test]
        public void ImpactCrestReachesQuarterOfBossWithoutUnboundedOverlap()
        {
            var impact = new Vector4(0, 0, 0, SeaImpactWave.DefaultAmplitude);
            float highest = 0;
            for (int frame = 1; frame <= 200; frame++)
                highest = Mathf.Max(highest, SeaImpactWave.Combine(SeaImpactWave.Height(new Vector2(45, 0), frame * 0.01f, impact)));
            Assert.That(highest / 208f, Is.InRange(0.25f, 0.4f));
            Assert.That(SeaImpactWave.Combine(800), Is.LessThan(SeaImpactWave.CombinedHeightLimit));
            var hull = new VesselWaveResponse();
            float previous = 0, worstLag = 0;
            for (int frame = 1; frame <= 800; frame++)
            {
                float clock = frame * 0.01f;
                float height = SeaImpactWave.Combine(SeaImpactWave.Height(new Vector2(180, 0), clock, impact) * 2);
                float speed = (height - previous) / 0.01f;
                hull.Tick(Vector3.up * height, Vector3.zero, 0.01f, Vector3.up * speed);
                worstLag = Mathf.Max(worstLag, height - hull.Offset.y);
                previous = height;
            }
            Assert.That(worstLag, Is.LessThan(1.5f), "The reinforced hull must follow a rising wall instead of being buried by spring lag");
        }

        [Test]
        public void BuoyancyHasInertiaAndSettlesWithoutFrameRateDependence()
        {
            var a = new VesselWaveResponse();
            var b = new VesselWaveResponse();
            var target = new Vector3(2, 5, -1);
            var tilt = new Vector3(10, 0, 15);
            a.Tick(target, tilt, 0.5f);
            for (int i = 0; i < 60; i++) b.Tick(target, tilt, 1f / 120);
            Assert.That(Vector3.Distance(a.Offset, b.Offset), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(a.Angles, b.Angles), Is.LessThan(0.0001f));
            var before = a.Offset;
            a.Tick(Vector3.zero, Vector3.zero, 0);
            Assert.That(a.Offset, Is.EqualTo(before), "Pause retains position and momentum");
            a.Tick(Vector3.zero, Vector3.zero, 0.01f);
            Assert.That(a.Offset.magnitude, Is.GreaterThan(1), "Hull cannot teleport to calm water");
            for (int i = 0; i < 600; i++) a.Tick(Vector3.zero, Vector3.zero, 1f / 60);
            Assert.That(a.Offset.magnitude, Is.LessThan(0.001f));
            Assert.That(a.Angles.magnitude, Is.LessThan(0.001f));
        }

        [Test]
        public void OpaqueSeaWritesDepthWithoutChangingTransparentWaterOrSource()
        {
            source.SetFloat("_ShoreFade", 0);
            source.SetColor("_Color_Shallow", Color.black);
            source.SetColor("_Color_Deep", Color.black);
            source.DisableKeyword("_ENABLEREFRACTION");
            water.Bind(camera);
            Assert.That(water.surface.sharedMaterial.GetFloat("_SeaZWrite"), Is.EqualTo(1));
            Assert.That(source.GetFloat("_SeaZWrite"), Is.Zero);
            source.SetColor("_Color_Shallow", new Color(0, 0, 0, 0.5f));
            water.Bind(camera);
            Assert.That(water.surface.sharedMaterial.GetFloat("_SeaZWrite"), Is.Zero);
        }

        [Test]
        public void RebindAndUnbindReleaseOwnedObjectsAndPreserveSourceAndMask()
        {
            var firstMaterial = water.surface.sharedMaterial;
            var firstManager = water.Reflections;
            Assert.That(firstMaterial, Is.Not.SameAs(source));
            Assert.That(firstManager.sourceCamera, Is.SameAs(camera));
            var volume = firstManager.GetComponent<PlanarReflectionVolume>();
            volume.enabled = false;
            volume.enabled = true;
            Assert.That(volume.reflectionLayer.value, Is.EqualTo(1 << 7));
            water.Bind(camera);
            Assert.That(firstMaterial == null && firstManager == null, Is.True);
            Assert.That(root.GetComponentsInChildren<PlanarReflectionManager>().Length, Is.EqualTo(1));
            var secondMaterial = water.surface.sharedMaterial;
            var secondManager = water.Reflections;
            water.Unbind();
            Assert.That(secondMaterial == null && secondManager == null, Is.True);
            Assert.That(water.surface.sharedMaterial, Is.SameAs(source));
            Assert.That(source.GetFloat("_UseSeaTime"), Is.Zero);
        }
    }
}
