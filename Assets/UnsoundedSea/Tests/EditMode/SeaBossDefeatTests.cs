using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class SeaBossDefeatTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void BellDeathPreservesAttachmentsAndWaitsForHeavyCollapse(bool reduced)
        {
            bool oldReduced = GameSettings.ReducedMotion;
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnsoundedSea/Art/Prefabs/BellMother.prefab"));
            try
            {
                GameSettings.ReducedMotion = reduced;
                root.transform.SetPositionAndRotation(new Vector3(18, -45, 260), Quaternion.Euler(0, 137, 0));
                root.transform.localScale = Vector3.one * 6.5f;
                var m = root.GetComponent<BellMotherRigidMotion>();
                m.PlayAttack(); for (int i = 0; i < 75; i++) m.Tick(.01f);
                var positions = root.GetComponentsInChildren<Transform>().Select(t => t.position).ToArray();
                var crown = m.body.InverseTransformPoint(m.crown.position);
                var attached = m.tentacles.Select((part, i) => m.body.InverseTransformPoint(part.TransformPoint(m.tentacleRoots[i]))).ToArray();
                var scale = root.transform.localScale;
                float startY = m.body.position.y;
                int contacts = 0; m.SurfaceHeight = _ => 0;
                m.CollapseImpact += (_, amplitude) => { contacts++; Assert.That(amplitude, Is.GreaterThan(30)); };
                m.PlayDeath(); m.Tick(0);
                var now = root.GetComponentsInChildren<Transform>().Select(t => t.position).ToArray();
                for (int i = 0; i < now.Length; i++) Assert.That(Vector3.Distance(now[i], positions[i]), Is.LessThan(.001f), "No pose reset at death");
                m.SetPaused(true); m.Tick(.1f); Assert.That(m.ActionElapsed, Is.Zero); m.SetPaused(false);
                for (int frame = 0; frame < 110; frame++)
                {
                    m.Tick(.1f);
                    if (frame == 4) Assert.That(Mathf.Abs(startY - m.body.position.y), Is.LessThan(2), "Do not vanish in the first half-second");
                    if (frame == 15) Assert.That(m.HasPendingRetirement, Is.True);
                    if (frame == 20) m.PlayDeath(); // Repeated dead snapshots must not restart the animation.
                    Assert.That(root.transform.localScale, Is.EqualTo(scale));
                    Assert.That(Vector3.Distance(m.crown.position, m.body.TransformPoint(crown)), Is.LessThan(.01f));
                    for (int i = 0; i < m.tentacles.Length; i++)
                        Assert.That(Vector3.Distance(m.tentacles[i].TransformPoint(m.tentacleRoots[i]), m.body.TransformPoint(attached[i])), Is.LessThan(.02f));
                }
                Assert.That(contacts, Is.EqualTo(1));
                Assert.That(m.HasPendingRetirement, Is.False);
                Assert.That(root.GetComponentsInChildren<Renderer>().Max(r => r.bounds.max.y), Is.LessThan(0));
            }
            finally { GameSettings.ReducedMotion = oldReduced; Object.DestroyImmediate(root); }
        }

        [TestCase("TideBonesChoirmaster", BossEncounterKind.Choirmaster)]
        [TestCase("InvertedSurveyor", BossEncounterKind.Surveyor)]
        [TestCase("UnlitFlagBearer", BossEncounterKind.FlagBearer)]
        [TestCase("StarlessWatcher", BossEncounterKind.Watcher)]
        public void AnatomicalRetirementStopsIdleAndPreservesSupportedHips(string name, BossEncounterKind kind)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnsoundedSea/Art/Prefabs/" + name + ".prefab"));
            try
            {
                var m = root.GetComponentInChildren<GiantBossMotion>();
                m.Initialize(); m.Tick(.1f);
                var body = m.joints.Single(j => j.bone.name == "GB_Body").bone;
                var hips = body.position; var startRoot = m.transform.position;
                m.Retire(); m.PlayAttack();
                m.SetPaused(true); m.Tick(.1f); Assert.That(m.RetirementProgress, Is.Zero); m.SetPaused(false);
                for (int i = 0; i < 110; i++) m.Tick(.1f);
                Assert.That(m.Busy, Is.False); Assert.That(m.ActionTime, Is.LessThan(0));
                if (kind == BossEncounterKind.FlagBearer)
                {
                    Assert.That(Vector3.Distance(body.position, hips), Is.LessThan(.001f), "Keep captive hips in the cradle");
                    Assert.That(m.transform.position, Is.EqualTo(startRoot));
                }
                var final = m.joints.Select(j => j.bone.localRotation).ToArray();
                m.Tick(.1f);
                for (int i = 0; i < final.Length; i++) Assert.That(Quaternion.Angle(final[i], m.joints[i].bone.localRotation), Is.LessThan(.001f), "Dead body must not keep breathing");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SurvivingBossWithdrawsWithoutDeathRoll()
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnsoundedSea/Art/Prefabs/StarlessWatcher.prefab"));
            try
            {
                var m = root.GetComponentInChildren<GiantBossMotion>(); m.Initialize();
                var rotation = m.transform.rotation;
                m.Retire(true); for (int i = 0; i < 60; i++) m.Tick(.1f);
                Assert.That(m.Busy, Is.False); Assert.That(m.DefeatLoad, Is.Zero);
                Assert.That(Quaternion.Angle(rotation, m.transform.rotation), Is.LessThan(.001f));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
