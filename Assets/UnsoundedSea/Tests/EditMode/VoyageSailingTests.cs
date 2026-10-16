using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class VoyageSailingTests
    {
        [TestCase(-45)] [TestCase(45)] [TestCase(115)]
        public void PassageTurnsTowardItsDestinationAndStopsAtBothEnds(float turn)
        {
            var destination = Quaternion.Euler(0, turn, 0) * Vector3.forward * 70;
            var path = new VoyageSailingPath(Vector3.zero, destination, Vector3.forward);
            path.Evaluate(0, out var start, out var departure);
            path.Evaluate(1, out var end, out var arrival);
            Assert.That(start, Is.EqualTo(Vector3.zero));
            Assert.That(Vector3.Distance(end, destination), Is.LessThan(0.0001f));
            Assert.That(Vector3.Angle(departure, Vector3.forward), Is.LessThan(0.01f));
            Assert.That(Vector3.Angle(arrival, destination), Is.LessThan(0.01f));
            path.Evaluate(0.001f, out var departing, out _);
            path.Evaluate(0.999f, out var arriving, out _);
            Assert.That(Vector3.Distance(start, departing), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(end, arriving), Is.LessThan(0.001f));
            path.Evaluate(0.5f, out var middle, out var steering);
            Assert.That(Vector3.Cross(middle, destination).magnitude, Is.GreaterThan(10), "Path is a straight slide instead of a turn");
            Assert.That(Vector3.Dot(steering, destination.normalized), Is.GreaterThan(0));
            Assert.That(path.Duration, Is.GreaterThanOrEqualTo(12));
        }

        [Test]
        public void ChartNorthAndEastMatchWorldHeading()
        {
            var origin = VoyageSailingPath.WorldPoint(new Vector2(50,12));
            Assert.That(VoyageSailingPath.WorldPoint(new Vector2(51,12)) - origin, Is.EqualTo(Vector3.right * 3));
            Assert.That(VoyageSailingPath.WorldPoint(new Vector2(50,13)) - origin, Is.EqualTo(Vector3.forward * 3));
        }

        [Test]
        public void VoyageSceneHasIndependentWaterMaterialAndSharedShipAndMesh()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/UnsoundedSea/Scenes/Voyage.unity");
            try
            {
                VoyageSailingPresentation sailing = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = root.GetComponentInChildren<VoyageSailingPresentation>();
                    if (found != null) sailing = found;
                }
                Assert.That(sailing, Is.Not.Null);
                Assert.That(sailing.Vessel, Is.Not.Null);
                Assert.That(sailing.VoyageCamera.transform.IsChildOf(sailing.Vessel), Is.False);
                Assert.That(sailing.Water.surface.sharedMaterial.shader.name, Is.EqualTo("UberStylizedWater"));
                Assert.That(AssetDatabase.GetAssetPath(sailing.Water.surface.sharedMaterial), Does.Contain("Art/Voyage/"));
                Assert.That(AssetDatabase.GetAssetPath(sailing.Water.surface.GetComponent<MeshFilter>().sharedMesh), Does.Contain("Art/SeaBoss/DeepOceanMesh.asset"));
                Assert.That(sailing.Water.surface.sharedMaterial.GetVector("_Threat"), Is.EqualTo(Vector4.zero));
                var hull = sailing.Vessel.Find("SurveyVessel");
                Assert.That(hull, Is.Not.Null);
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(hull), Does.EndWith("SurveyVessel.prefab"));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
