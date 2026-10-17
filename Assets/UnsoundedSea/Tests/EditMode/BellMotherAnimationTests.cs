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

        [Test]
        public void AttackMovesRigidPartsWithoutChangingMeshGeometry()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            try
            {
                var motion = instance.GetComponent<BellMotherRigidMotion>();
                var bodyBefore = motion.body.localRotation;
                var bodyHeightBefore = motion.body.localPosition.y;
                var tentacleBefore = motion.tentacles[0].localRotation;
                motion.PlayAttack();
                motion.Tick(0.30f);
                Assert.That(Quaternion.Angle(bodyBefore, motion.body.localRotation), Is.GreaterThan(1));
                Assert.That(Quaternion.Angle(tentacleBefore, motion.tentacles[0].localRotation), Is.GreaterThan(1));
                Assert.That(motion.body.localPosition.y, Is.LessThanOrEqualTo(bodyHeightBefore));
                Assert.That(motion.ActionImpact, Is.GreaterThan(0));
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
