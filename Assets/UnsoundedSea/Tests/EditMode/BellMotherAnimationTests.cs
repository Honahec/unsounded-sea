using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class BellMotherAnimationTests
    {
        const string PrefabPath = "Assets/UnsoundedSea/Art/Prefabs/BellMother.prefab";

        [Test]
        public void BangPrefabContainsRigidPresentationParts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, "Run BellMotherBangSetup.Build first.");
            var motion = prefab.GetComponent<BellMotherRigidMotion>();
            Assert.That(motion, Is.Not.Null);
            Assert.That(motion.body, Is.Not.Null);
            Assert.That(motion.crown, Is.Not.Null);
            Assert.That(motion.tentacles, Has.Length.EqualTo(6));
            Assert.That(motion.tentacles.All(x => x != null), Is.True);
            Assert.That(prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<MeshRenderer>(true).Length, Is.GreaterThanOrEqualTo(12));
        }

        [Test]
        public void BangPrefabStaysWithinRuntimeTriangleBudget()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var triangles = prefab.GetComponentsInChildren<MeshFilter>(true)
                .Where(x => x.sharedMesh != null)
                .Sum(x => x.sharedMesh.triangles.Length / 3);
            Assert.That(triangles, Is.LessThan(130000));
            Assert.That(BattleActorView.MeasureLocalBounds(prefab.transform).size.y, Is.GreaterThan(25));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AttackCarriesRootsWithSubtleBodyMotionAndRaisesOuterEnds(bool transformed)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            try
            {
                if (transformed)
                {
                    instance.transform.position = new Vector3(18, -4, 70);
                    instance.transform.rotation = Quaternion.Euler(0, 137, 0);
                    instance.transform.localScale = Vector3.one * 6.5f;
                }
                var motion = instance.GetComponent<BellMotherRigidMotion>();
                Assert.That(motion.tentacleRoots, Has.Length.EqualTo(6));
                Assert.That(motion.tentacleTips, Has.Length.EqualTo(6));
                var roots = motion.tentacles.Select((p, i) => p.TransformPoint(motion.tentacleRoots[i])).ToArray();
                var attachments = roots.Select(p => motion.body.InverseTransformPoint(p)).ToArray();
                var tips = motion.tentacles.Select((p, i) => p.TransformPoint(motion.tentacleTips[i])).ToArray();
                var lengths = roots.Select((p, i) => Vector3.Distance(p, tips[i])).ToArray();
                var bodyPosition = motion.body.position;
                var bodyRotation = motion.body.rotation;
                var crownAttachment = motion.body.InverseTransformPoint(motion.crown.position);
                var bodyHeight = instance.GetComponentsInChildren<Renderer>().Max(r => r.bounds.max.y) -
                    instance.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y);
                var contactBodyY = bodyPosition.y;
                motion.PlayAttack();
                // Verify the entire arc, including lift, contact and recovery;
                // moving transform origins upward cannot satisfy this test.
                for (var frame = 1; frame <= 96; frame++)
                {
                    motion.Tick(0.01f);
                    for (var i = 0; i < motion.tentacles.Length; i++)
                    {
                        var root = motion.tentacles[i].TransformPoint(motion.tentacleRoots[i]);
                        var tip = motion.tentacles[i].TransformPoint(motion.tentacleTips[i]);
                        Assert.That(Vector3.Distance(root, motion.body.TransformPoint(attachments[i])), Is.LessThan(lengths[i] * 0.0001f),
                            $"Tentacle {i + 1} attachment drift at frame {frame}");
                        Assert.That(Vector3.Distance(root, tip), Is.EqualTo(lengths[i]).Within(lengths[i] * 0.0001f));
                        if (frame == 35)
                            Assert.That(tip.y - tips[i].y, Is.GreaterThan(lengths[i] * 0.2f), $"Outer end {i + 1} must rise");
                        if (frame == 57)
                            Assert.That(tip.y, Is.LessThan(tips[i].y), $"Outer end {i + 1} must slap downward");
                    }
                    Assert.That(Vector3.Distance(motion.body.position, bodyPosition), Is.LessThan(bodyHeight * 0.02f));
                    Assert.That(Quaternion.Angle(motion.body.rotation, bodyRotation), Is.LessThan(1.1f));
                    Assert.That(Vector3.Distance(motion.crown.position, motion.body.TransformPoint(crownAttachment)),
                        Is.LessThan(bodyHeight * 0.0001f));
                    if (frame == 35)
                    {
                        Assert.That(motion.ActionImpact, Is.Zero);
                        Assert.That(motion.body.position.y, Is.GreaterThan(bodyPosition.y + bodyHeight * 0.001f));
                    }
                    if (frame == 56) contactBodyY = motion.body.position.y;
                    if (frame == 64) Assert.That(motion.body.position.y, Is.LessThan(contactBodyY));
                    if (frame == 57) Assert.That(motion.ActionImpact, Is.GreaterThan(0.8f));
                    if (motion.ActiveAction == "Idle") break;
                }
                Assert.That(motion.ActiveAction, Is.EqualTo("Idle"));
                Assert.That(Vector3.Distance(motion.body.position, bodyPosition), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(motion.body.rotation, bodyRotation), Is.LessThan(0.001f));
                for (var i = 0; i < motion.tentacles.Length; i++)
                    Assert.That(Vector3.Distance(motion.tentacles[i].TransformPoint(motion.tentacleTips[i]), tips[i]),
                        Is.LessThan(lengths[i] * 0.0001f));
                Assert.That(instance.GetComponentsInChildren<SkinnedMeshRenderer>(true), Is.Empty);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void HitUsesHeavyRecoilWithoutBouncyOvershoot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            try
            {
                var motion = instance.GetComponent<BellMotherRigidMotion>();
                var bodyRotationBefore = motion.body.localRotation;
                var bodyHeightBefore = motion.body.localPosition.y;
                motion.PlayHit();
                motion.Tick(0.12f);
                Assert.That(motion.body.localPosition.y, Is.LessThan(bodyHeightBefore));
                Assert.That(Quaternion.Angle(bodyRotationBefore, motion.body.localRotation), Is.LessThan(8f));
                motion.Tick(0.24f);
                Assert.That(motion.ActiveAction, Is.EqualTo("Idle"));
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
