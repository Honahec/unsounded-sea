using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;
using Object = UnityEngine.Object;

namespace UnsoundedSea.Tests
{
    public sealed class GiantBossAnimationTests
    {
        private static BattleScenario Scenario(int number) => AssetDatabase.LoadAssetAtPath<BattleScenario>(
            "Assets/UnsoundedSea/Content/Scenarios/M0" + number + ".asset");

        [TestCase(2)] [TestCase(3)] [TestCase(4)]
        public void RitualAndCallingPosesDifferFromPhysicalStrikesWithoutLaunchingWaves(int number)
        {
            var prefab = Scenario(number).ActorPrefab(0);
            var actors = Enumerable.Range(0, 3).Select(_ => Object.Instantiate(prefab)).ToArray();
            try
            {
                var motions = actors.Select(a => a.GetComponentInChildren<GiantBossMotion>()).ToArray();
                var waves = new int[3];
                for (var i = 0; i < 3; i++) { var slot = i; motions[i].SurfaceReleased += (_, __) => waves[slot]++; }
                motions[0].PlayAttack(); motions[1].PlayAttack(true); motions[2].PlayAttack(false, true);
                for (var frame = 0; frame < 16; frame++) foreach (var motion in motions) motion.Tick(.1f);
                for (var i = 1; i < 3; i++)
                    Assert.That(motions[i].joints.Select((joint, index) => Quaternion.Angle(joint.bone.localRotation, motions[0].joints[index].bone.localRotation)).Max(), Is.GreaterThan(1), "Action silhouettes must differ");
                for (var frame = 0; frame < 40; frame++) foreach (var motion in motions) motion.Tick(.1f);
                Assert.That(waves, Is.EqualTo(new[] { number == 2 ? 1 : 0, 0, 0 }));
                Assert.That(motions.All(m => !m.Busy), Is.True);
            }
            finally { foreach (var actor in actors) Object.DestroyImmediate(actor); }
        }

        [Test] public void ChoirPropsCloseFromPublicStateAndRestoreWithoutEditingSharedMaterials()
        {
            var scenario = Scenario(2);
            var stage = Object.Instantiate(scenario.stagePrefab);
            try
            {
                var target = stage.GetComponentsInChildren<BossTargetAnchor>().Single(t => t.targetId == "choir-a");
                var pipe = target.GetComponentsInChildren<Transform>().First(t => t.name == "Resonance pipe");
                var position = pipe.localPosition;
                var renderer = target.GetComponent<Renderer>(); var color = renderer.sharedMaterial.GetColor("_BaseColor");
                var session = scenario.CreateSession(0);
                target.Bind(session.View);
                var card = session.View.Hand.Single(c => c.Definition.Id == "E02-02");
                Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, "choir-a")).Success, Is.True);
                target.Present(session.View); target.Tick(1);
                Assert.That(target.PresentedState, Is.EqualTo(MechanismState.Open));
                Assert.That(pipe.localPosition.y, Is.LessThan(position.y));
                Assert.That(renderer.sharedMaterial.GetColor("_BaseColor"), Is.EqualTo(color));
                target.RestorePresentation();
                Assert.That(pipe.localPosition, Is.EqualTo(position));
            }
            finally { Object.DestroyImmediate(stage.gameObject); }
        }

        [TestCase(2, 7, 10, 18f)]
        [TestCase(3, 7, 9, 12f)]
        [TestCase(4, 7, 5, 3.8f)]
        [TestCase(5, 10, 5, 30f)]
        public void ImportedSkinsDeformAndRecoverWithoutMovingTheActorRoot(int number, int bones, int meshes, float size)
        {
            var scenario = Scenario(number);
            Assert.DoesNotThrow(scenario.ValidatePresentation);
            Assert.That(scenario.stagePrefab.actorAnchors[0].IsChildOf(scenario.stagePrefab.vessel), Is.False);
            var actor = Object.Instantiate(scenario.ActorPrefab(0));
            var control = Object.Instantiate(actor);
            var baked = new Mesh();
            try
            {
                var bounds = BattleActorView.MeasureLocalBounds(actor.transform);
                var extent = number == 2 ? Mathf.Max(bounds.size.x, bounds.size.z) : bounds.size.y;
                Assert.That(extent, Is.EqualTo(size).Within(.02f), "Nested importer overrides must not shrink the boss");
                Assert.That(bounds.min.y, Is.EqualTo(0).Within(.02f));
                actor.transform.SetPositionAndRotation(new Vector3(3, -55, 280), Quaternion.Euler(0, 205, 0));
                actor.transform.localScale = Vector3.one * 20;
                control.transform.SetPositionAndRotation(actor.transform.position, actor.transform.rotation);
                control.transform.localScale = actor.transform.localScale;
                var motion = actor.GetComponentInChildren<GiantBossMotion>();
                var idle = control.GetComponentInChildren<GiantBossMotion>();
                motion.Initialize(); idle.Initialize();
                Assert.That(motion.joints.Length, Is.EqualTo(bones));
                var skins = actor.GetComponentsInChildren<SkinnedMeshRenderer>();
                Assert.That(skins.Length, Is.EqualTo(meshes));
                Assert.That(skins.Sum(s => s.sharedMesh.triangles.Length / 3), Is.InRange(89000, 90000));
                foreach (var skin in skins)
                {
                    Assert.That(skin.bones.All(b => b != null), Is.True);
                    Assert.That(skin.sharedMaterials.All(m => m != null && m.shader.isSupported), Is.True);
                    Assert.That(skin.sharedMesh.uv.Length, Is.EqualTo(skin.sharedMesh.vertexCount));
                }
                var rest = skins.Select(s => { s.BakeMesh(baked); return baked.vertices; }).ToArray();
                var rootPosition = motion.transform.localPosition;
                int waves = 0; motion.SurfaceReleased += (_, amplitude) => { Assert.That(amplitude, Is.GreaterThan(0)); waves++; };
                motion.PlayAttack(number == 5);
                float displacement = 0;
                for (int frame = 0; frame < 50; frame++)
                {
                    motion.Tick(.1f); idle.Tick(.1f);
                    if (frame % 5 != 0) continue;
                    for (int part = 0; part < skins.Length; part++)
                    {
                        skins[part].BakeMesh(baked); var vertices = baked.vertices;
                        for (int v = 0; v < vertices.Length; v += 29)
                        {
                            Assert.That(float.IsNaN(vertices[v].sqrMagnitude) || float.IsInfinity(vertices[v].sqrMagnitude), Is.False);
                            displacement = Mathf.Max(displacement, Vector3.Distance(vertices[v], rest[part][v]));
                        }
                    }
                }
                Assert.That(displacement, Is.GreaterThan(.005f), "Bones must actually deform the mesh");
                Assert.That(motion.Busy, Is.False);
                Assert.That(waves, Is.EqualTo(number == 2 ? 1 : 0));
                Assert.That(Vector3.Distance(rootPosition, motion.transform.localPosition), Is.LessThan(.0001f), "No whole-boss jumping");
                for (int i = 0; i < bones; i++) Assert.That(Quaternion.Angle(motion.joints[i].bone.localRotation, idle.joints[i].bone.localRotation), Is.LessThan(.03f));
                motion.PlayHit(); motion.Tick(.1f);
                var pose = motion.joints.Select(j => j.bone.localRotation).ToArray(); var age = motion.Age;
                motion.SetPaused(true); motion.Tick(.1f);
                Assert.That(motion.Age, Is.EqualTo(age));
                motion.SetPaused(false); motion.PlayHit(); motion.Tick(0);
                for (int i = 0; i < bones; i++) Assert.That(Quaternion.Angle(pose[i], motion.joints[i].bone.localRotation), Is.LessThan(.03f));
                for (int i = 0; i < 20; i++) motion.Tick(.1f);
                Assert.That(motion.HitWeight, Is.Zero);
                int before = waves;
                motion.PlayAttack(true);
                for (int i = 0; i < 50; i++) motion.Tick(.1f);
                motion.PlayAttack(false, true);
                for (int i = 0; i < 50; i++) motion.Tick(.1f);
                Assert.That(waves, Is.EqualTo(before), "Rituals and summon cues must not launch physical waves");
                motion.Retire(); motion.PlayAttack();
                for (int i = 0; i < 50; i++) motion.Tick(.1f);
                Assert.That(motion.Busy, Is.False);
                Assert.That(waves, Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(actor); Object.DestroyImmediate(control); }
        }

        [Test] public void WatcherAttacksFromFirstTurnAndPlayerSummonIsReplayable()
        {
            var scenario = Scenario(5); var session = scenario.CreateSession(0);
            Assert.That(session.View.ResolvedEnemyActions, Is.Empty);
            Assert.That(session.Execute(BattleCommand.EndTurn()).Success, Is.True);
            var id = session.View.Enemies[0].Id;
            var gaze = session.View.ResolvedEnemyActions.Single(a => a.EnemyId == id);
            Assert.That(gaze.Channel, Is.EqualTo(DamageChannel.Sanity)); Assert.That(gaze.Amount, Is.EqualTo(2));
            var summon = session.View.Hand.Single(c => c.Definition.Id == "E05-01");
            Assert.That(summon.Targets, Is.Empty);
            Assert.That(session.Execute(BattleCommand.Play(summon.InstanceId)).Success, Is.True);
            Assert.That(session.View.Enemies.Skip(1).Count(e => e.Health > 0), Is.EqualTo(1));
            session.Execute(BattleCommand.EndTurn());
            var replay = BattleReplay.Capture(session).Restore(scenario.encounter.Build());
            Assert.That(replay.View.ResolvedEnemyActions.Select(a => a.Amount), Is.EqualTo(session.View.ResolvedEnemyActions.Select(a => a.Amount)));
            Assert.That(replay.View.Enemies.Select(e => e.Health), Is.EqualTo(session.View.Enemies.Select(e => e.Health)));
        }

        [TestCase(3)] [TestCase(4)]
        public void WindAndPoleHaveTheirOwnContactAndNeverEmitSeaWaves(int number)
        {
            var scenario = Scenario(number); var stage = Object.Instantiate(scenario.stagePrefab);
            var actor = Object.Instantiate(scenario.ActorPrefab(0), stage.actorAnchors[0]);
            var hadPreference = PlayerPrefs.HasKey("settings.reducedMotion"); var reduced = GameSettings.ReducedMotion;
            try
            {
                GameSettings.ReducedMotion = false;
                var motion = actor.GetComponentInChildren<GiantBossMotion>(); motion.BindEffects(null, stage.vessel);
                var waves = 0; motion.SurfaceReleased += (_, __) => waves++;
                var distanceAtContact = float.PositiveInfinity;
                motion.VesselImpact += () => distanceAtContact = Vector3.Distance(motion.PoleTip, motion.StrikeTarget);
                var position = actor.transform.localPosition;
                motion.PlayAttack();
                for (var i = 0; i < 200; i++) motion.Tick(.025f);
                var effects = motion.GetComponent<GiantBossEffects>();
                Assert.That(waves, Is.Zero);
                Assert.That(motion.Busy, Is.False);
                Assert.That(actor.transform.localPosition, Is.EqualTo(position));
                if (number == 3)
                {
                    Assert.That(effects.WindReleaseCount, Is.EqualTo(1));
                    Assert.That(actor.GetComponentsInChildren<LineRenderer>().Length, Is.EqualTo(7));
                }
                else
                {
                    Assert.That(effects.PoleContactCount, Is.EqualTo(1));
                    Assert.That(distanceAtContact, Is.LessThan(2), "The real pole tip must reach the vessel at contact.");
                }
            }
            finally
            {
                if (hadPreference) GameSettings.ReducedMotion = reduced; else { PlayerPrefs.DeleteKey("settings.reducedMotion"); PlayerPrefs.Save(); }
                Object.DestroyImmediate(actor); Object.DestroyImmediate(stage.gameObject);
            }
        }
    }
}
