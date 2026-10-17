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
                Assert.That(motion.attackTentacles.Select(p => p.name), Is.EquivalentTo(new[] { "Tentacle_02", "Tentacle_06" }));
                var roots = motion.tentacles.Select((p, i) => p.TransformPoint(motion.tentacleRoots[i])).ToArray();
                var attachments = roots.Select(p => motion.body.InverseTransformPoint(p)).ToArray();
                var tips = motion.tentacles.Select((p, i) => p.TransformPoint(motion.tentacleTips[i])).ToArray();
                var tipAttachments = tips.Select(p => motion.body.InverseTransformPoint(p)).ToArray();
                var lengths = roots.Select((p, i) => Vector3.Distance(p, tips[i])).ToArray();
                var bodyPosition = motion.body.position;
                var bodyRotation = motion.body.rotation;
                var crownAttachment = motion.body.InverseTransformPoint(motion.crown.position);
                var bodyHeight = instance.GetComponentsInChildren<Renderer>().Max(r => r.bounds.max.y) -
                    instance.GetComponentsInChildren<Renderer>().Min(r => r.bounds.min.y);
                var contactBodyY = bodyPosition.y;
                var selected = motion.attackTentacles[0];
                var previousRotation = selected.rotation;
                var earlyStrikeSpeed = 0f;
                var pressedRotation = Quaternion.identity;
                motion.PlayAttack();
                // Verify the entire arc, including lift, contact and recovery;
                // moving transform origins upward cannot satisfy this test.
                for (var frame = 1; frame <= 206; frame++)
                {
                    motion.Tick(0.01f);
                    for (var i = 0; i < motion.tentacles.Length; i++)
                    {
                        var root = motion.tentacles[i].TransformPoint(motion.tentacleRoots[i]);
                        var tip = motion.tentacles[i].TransformPoint(motion.tentacleTips[i]);
                        Assert.That(Vector3.Distance(root, motion.body.TransformPoint(attachments[i])), Is.LessThan(lengths[i] * 0.0001f),
                            $"Tentacle {i + 1} attachment drift at frame {frame}");
                        Assert.That(Vector3.Distance(root, tip), Is.EqualTo(lengths[i]).Within(lengths[i] * 0.0001f));
                        var strikes = motion.attackTentacles.Contains(motion.tentacles[i]);
                        if (!strikes)
                            Assert.That(Vector3.Distance(tip, motion.body.TransformPoint(tipAttachments[i])),
                                Is.LessThan(lengths[i] * 0.02f), $"Support limb {i + 1} must not join the slap");
                        if (strikes && frame == 80)
                            Assert.That(tip.y - tips[i].y, Is.GreaterThan(lengths[i] * 0.2f), $"Outer end {i + 1} must rise");
                        if (strikes && frame == 113)
                            Assert.That(tip.y, Is.LessThan(tips[i].y), $"Outer end {i + 1} must slap downward");
                    }
                    Assert.That(Vector3.Distance(motion.body.position, bodyPosition), Is.LessThan(bodyHeight * 0.02f));
                    Assert.That(Quaternion.Angle(motion.body.rotation, bodyRotation), Is.LessThan(1.1f));
                    Assert.That(Vector3.Distance(motion.crown.position, motion.body.TransformPoint(crownAttachment)),
                        Is.LessThan(bodyHeight * 0.0001f));
                    if (frame == 80)
                    {
                        Assert.That(motion.ActionImpact, Is.Zero);
                        Assert.That(motion.body.position.y, Is.GreaterThan(bodyPosition.y + bodyHeight * 0.001f));
                    }
                    if (frame == 112) contactBodyY = motion.body.position.y;
                    if (frame == 124) Assert.That(motion.body.position.y, Is.LessThan(contactBodyY));
                    if (frame == 113) Assert.That(motion.ActionImpact, Is.GreaterThan(0.8f));
                    var speed = Quaternion.Angle(previousRotation, selected.rotation) / 0.01f;
                    if (frame == 90) earlyStrikeSpeed = speed;
                    if (frame == 110) Assert.That(speed, Is.GreaterThan(earlyStrikeSpeed * 2f), "Strike must gain speed into contact");
                    if (frame == 125) pressedRotation = Quaternion.Inverse(motion.body.rotation) * selected.rotation;
                    if (frame == 140)
                        Assert.That(Quaternion.Angle(pressedRotation, Quaternion.Inverse(motion.body.rotation) * selected.rotation),
                            Is.LessThan(0.05f), "Hold pressure below the water before recovering; no instant rebound");
                    previousRotation = selected.rotation;
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
        public void EachOuterTentacleEmitsOnceOnActualWaterCrossingAndPauseCannotEmit()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                instance.transform.localScale = Vector3.one * 6.5f;
                var motion = instance.GetComponent<BellMotherRigidMotion>();
                var plane = motion.attackTentacles.Max(p => p.TransformPoint(motion.tentacleTips[System.Array.IndexOf(motion.tentacles, p)]).y) + 20;
                motion.SurfaceHeight = p => plane;
                int contacts = 0;
                motion.SurfaceStruck += p =>
                {
                    contacts++;
                    Assert.That(p.y, Is.EqualTo(plane).Within(0.001f));
                    Assert.That(motion.ActionElapsed, Is.InRange(0.84f, 1.12f), "Contact should occur before the submerged recovery pose");
                };
                for (int attack = 0; attack < 2; attack++)
                {
                    motion.PlayAttack();
                    motion.Tick(0.75f);
                    Assert.That(contacts, Is.EqualTo(attack * 2), "Rising out of water is not a slap");
                    motion.SetPaused(true);
                    motion.Tick(10);
                    Assert.That(contacts, Is.EqualTo(attack * 2));
                    motion.SetPaused(false);
                    for (int frame = 0; frame < 150; frame++) motion.Tick(0.01f);
                    Assert.That(contacts, Is.EqualTo((attack + 1) * 2));
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void HitAbsorbsSlowlyPreservesAttachmentsAndNeverRestartsIdle()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var instance = Object.Instantiate(prefab);
            var reference = Object.Instantiate(prefab);
            try
            {
                var motion = instance.GetComponent<BellMotherRigidMotion>();
                var idle = reference.GetComponent<BellMotherRigidMotion>();
                motion.Tick(0.7f); idle.Tick(0.7f);
                var before = motion.body.localRotation;
                motion.PlayHit();
                Assert.That(Quaternion.Angle(before, motion.body.localRotation), Is.LessThan(0.001f));
                float maximum = 0;
                for (int frame = 1; frame <= 200; frame++)
                {
                    motion.Tick(0.01f); idle.Tick(0.01f);
                    float angle = Quaternion.Angle(idle.body.localRotation, motion.body.localRotation);
                    maximum = Mathf.Max(maximum, angle);
                    if (frame <= 2) Assert.That(angle, Is.LessThan(0.05f), "No first-frame pose jump");
                    Assert.That(angle, Is.LessThan(0.7f), "A giant must not whip back several degrees");
                    Assert.That(motion.body.localPosition.y, Is.LessThanOrEqualTo(idle.body.localPosition.y + 0.00001f));
                    for (int i = 0; i < motion.tentacles.Length; i++)
                        Assert.That(Vector3.Distance(
                            motion.body.InverseTransformPoint(motion.tentacles[i].TransformPoint(motion.tentacleRoots[i])),
                            idle.body.InverseTransformPoint(idle.tentacles[i].TransformPoint(idle.tentacleRoots[i]))), Is.LessThan(0.001f));
                    Assert.That(Vector3.Distance(motion.body.InverseTransformPoint(motion.crown.position),
                        idle.body.InverseTransformPoint(idle.crown.position)), Is.LessThan(0.001f));
                    if (frame == 80) Assert.That(angle, Is.GreaterThan(0.3f), "Recovery must retain weight beyond the old 0.3 second recoil");
                }
                Assert.That(maximum, Is.GreaterThan(0.45f));
                Assert.That(motion.ActiveAction, Is.EqualTo("Idle"));
                Assert.That(Quaternion.Angle(idle.body.localRotation, motion.body.localRotation), Is.LessThan(0.01f));
            }
            finally { Object.DestroyImmediate(instance); Object.DestroyImmediate(reference); }
        }

        [Test]
        public void RepeatedHitsRemainContinuousAndDoNotInterruptAnAttackOrPause()
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            try
            {
                var motion = instance.GetComponent<BellMotherRigidMotion>();
                motion.PlayHit(); motion.Tick(0.45f);
                var before = motion.body.localRotation;
                motion.PlayHit(); motion.Tick(0.001f);
                Assert.That(Quaternion.Angle(before, motion.body.localRotation), Is.LessThan(0.03f));
                motion.SetPaused(true); before = motion.body.localRotation; motion.Tick(5);
                Assert.That(motion.body.localRotation, Is.EqualTo(before));
                motion.SetPaused(false); motion.PlayAttack();
                Assert.That(Quaternion.Angle(before, motion.body.localRotation), Is.LessThan(0.03f), "Starting an attack must not pop the hit overlay off");
                motion.Tick(0.5f);
                var attackElapsed = motion.ActionElapsed;
                motion.PlayHit(); motion.Tick(0.01f);
                Assert.That(motion.ActiveAction, Is.EqualTo("Attack"));
                Assert.That(motion.ActionElapsed, Is.EqualTo(attackElapsed + 0.01f).Within(0.0001f));
                motion.Tick(2.1f);
                Assert.That(motion.ActiveAction, Is.EqualTo("Idle"));
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
