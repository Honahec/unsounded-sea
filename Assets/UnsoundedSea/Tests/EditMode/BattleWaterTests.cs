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
