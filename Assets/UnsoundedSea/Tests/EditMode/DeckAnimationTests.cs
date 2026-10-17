using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class DeckAnimationTests
    {
        private static string[] Prefabs => AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/UnsoundedSea/Art/Prefabs/CuratedRigs" })
            .Select(AssetDatabase.GUIDToAssetPath).Concat(new[] { "Assets/UnsoundedSea/Art/Prefabs/Prayer.prefab", "Assets/UnsoundedSea/Art/Prefabs/Echo.prefab" }).ToArray();

        [Test] public void EveryDeckEnemyUsesASkinnedAnimatedPrefab()
        {
            Assert.That(Prefabs.Length, Is.EqualTo(27));
            foreach (var path in AssetDatabase.FindAssets("t:BattleScenario", new[] { "Assets/UnsoundedSea/Content" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var scenario = AssetDatabase.LoadAssetAtPath<BattleScenario>(path);
                for (var i = 0; i < scenario.encounter.enemies.Length; i++)
                {
                    var placement = scenario.stagePrefab.actorAnchors[i].GetComponent<BattleAnchor>();
                    if (placement != null && placement.environment == BattleEnvironment.Sea) continue;
                    Assert.That(scenario.ActorPrefab(i).GetComponent<DeckActorMotion>(), Is.Not.Null, path + " slot " + i);
                }
            }
        }

        [Test] public void AllSkinsDeformWithoutFootSlidingAndRecoverUnderRotatedScaledParents()
        {
            foreach (var path in Prefabs)
            {
                var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                var control = Object.Instantiate(root);
                var mesh = new Mesh();
                try
                {
                    var measured = BattleActorView.MeasureLocalBounds(root.transform);
                    Assert.That(measured.min.y, Is.EqualTo(0).Within(.015f), path + " selection floor");
                    Assert.That(measured.size.y, Is.InRange(1.55f, 2.15f), path + " selection height");
                    root.transform.SetPositionAndRotation(new Vector3(4, 7, -3), Quaternion.Euler(0, 137, 0));
                    root.transform.localScale *= 1.3f;
                    control.transform.SetPositionAndRotation(root.transform.position, root.transform.rotation);
                    control.transform.localScale = root.transform.localScale;
                    var motion = root.GetComponent<DeckActorMotion>(); var idle = control.GetComponent<DeckActorMotion>();
                    Assert.That(motion, Is.Not.Null, path); motion.Initialize("same"); idle.Initialize("same");
                    Assert.That(motion.DrivenJointCount, Is.GreaterThanOrEqualTo(10), path);
                    var skin = root.GetComponentInChildren<SkinnedMeshRenderer>();
                    var foot = skin.bones.Where(b => b.name == "LeftFoot" || b.name == "foot.L").Single();
                    var footPosition = foot.position;
                    skin.BakeMesh(mesh); var rest = mesh.vertices;
                    motion.PlayAttack(false, false);
                    float maxMotion = 0;
                    for (int i = 0; i < 24; i++)
                    {
                        motion.Tick(.1f, false, false); idle.Tick(.1f, false, false);
                        skin.BakeMesh(mesh); var vertices = mesh.vertices;
                        Assert.That(vertices.All(v => !float.IsNaN(v.sqrMagnitude) && !float.IsInfinity(v.sqrMagnitude)), Is.True, path);
                        for (int j = 0; j < vertices.Length; j += 37) maxMotion = Mathf.Max(maxMotion, Vector3.Distance(vertices[j], rest[j]));
                        Assert.That(Vector3.Distance(foot.position, footPosition), Is.LessThan(.0001f), path);
                    }
                    Assert.That(maxMotion, Is.GreaterThan(.005f), path);
                    Assert.That(motion.Busy, Is.False, path);
                    var bones = skin.bones;
                    var controls = control.GetComponentInChildren<SkinnedMeshRenderer>().bones;
                    for (int i = 0; i < bones.Length; i++) Assert.That(Quaternion.Angle(bones[i].localRotation, controls[i].localRotation), Is.LessThan(.06f), path);
                    motion.PlayHit(); motion.Tick(.08f, false, false);
                    var pose = bones.Select(b => b.localRotation).ToArray(); var age = motion.Age;
                    motion.Tick(2, true, false);
                    Assert.That(motion.Age, Is.EqualTo(age));
                    for (int i = 0; i < bones.Length; i++) Assert.That(Quaternion.Angle(bones[i].localRotation, pose[i]), Is.LessThan(.06f));
                    motion.PlayHit(); motion.Tick(0, false, false);
                    for (int i = 0; i < bones.Length; i++) Assert.That(Quaternion.Angle(bones[i].localRotation, pose[i]), Is.LessThan(.06f), "Repeated hit snapped " + path);
                    for (int i = 0; i < 20; i++) motion.Tick(.1f, false, true);
                    Assert.That(motion.Busy, Is.False);
                    skin.BakeMesh(mesh); var reduced = mesh.vertices;
                    Assert.That(reduced.Zip(rest, Vector3.Distance).Max(), Is.LessThan(.0002f), path);
                }
                finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(root); Object.DestroyImmediate(control); }
            }
        }

        [Test] public void PrayerGustMovesHemStronglyWithoutPullingBootsOrHands()
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UnsoundedSea/Art/Prefabs/Prayer.prefab"));
            var mesh = new Mesh();
            try
            {
                var skirt = go.GetComponent<PrayerSkirtMotion>();
                Assert.That(skirt.upperPanels, Has.Length.EqualTo(8)); Assert.That(skirt.hemPanels, Has.Length.EqualTo(8));
                var skin = go.GetComponentInChildren<SkinnedMeshRenderer>();
                skin.BakeMesh(mesh, true); var rest = mesh.vertices.Select(skin.transform.TransformPoint).ToArray();
                float hem = 0, protectedArea = 0;
                for (int frame = 0; frame <= 100; frame++)
                {
                    skirt.Tick(frame * .0165f, frame / 100f, 0, false, true);
                    skin.BakeMesh(mesh, true); var posed = mesh.vertices;
                    for (int v = 0; v < posed.Length; v++)
                    {
                        float delta = Vector3.Distance(rest[v], skin.transform.TransformPoint(posed[v]));
                        if (rest[v].y < .18f || rest[v].y > 1f) protectedArea = Mathf.Max(protectedArea, delta);
                        else hem = Mathf.Max(hem, delta);
                    }
                }
                Assert.That(hem, Is.InRange(.12f, .65f), "Gust must read clearly without tearing the robe");
                Assert.That(protectedArea, Is.LessThan(.003f), "Boots and upper body must not be dragged by wind");
                skirt.Tick(4, .4f, 0, true, true); skin.BakeMesh(mesh, true);
                Assert.That(mesh.vertices.Select(skin.transform.TransformPoint).Zip(rest, Vector3.Distance).Max(), Is.LessThan(.0002f));
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(go); }
        }
        [Test] public void OrdinaryOceanUsesSharedUberWaterWithSafeDeckWaveAmplitude()
        {
            var stage = AssetDatabase.LoadAssetAtPath<BattleStage>("Assets/UnsoundedSea/Art/OceanBattle/FogSeaStage.prefab");
            Assert.That(stage.water, Is.Not.Null);
            Assert.That(stage.water.surface, Is.EqualTo(stage.ocean));
            Assert.That(stage.ocean.sharedMaterial.shader.name, Is.EqualTo("UberStylizedWater"));
            Assert.That(stage.ocean.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Mesh>("Assets/UnsoundedSea/Art/SeaBoss/DeepOceanMesh.asset")));
            Assert.That(stage.ocean.gameObject.layer, Is.EqualTo(4));
            var m = stage.ocean.sharedMaterial;
            var max = m.GetFloat("_1st_Wave_Height") + m.GetFloat("_2nd_Wave_Height") + m.GetFloat("_StormStrength") *
                (m.GetVector("_StormWaveA").z + m.GetVector("_StormWaveB").z + m.GetVector("_StormWaveC").z + m.GetVector("_StormWaveD").z);
            Assert.That(max, Is.LessThan(stage.actorAnchors.Min(a => a.position.y) - .4f));
            Assert.That(stage.skybox, Is.Not.Null);
        }

        private static BattleDefinition Definition(int damage = 8, int health = 60) => new BattleDefinition("animation-events",
            new[] { new CardSpec("interrupt", "Interrupt", "", 0, new[] { new EffectSpec("interrupt", 1) }) },
            new[] { "first", "second" }.Select(id => new EnemySpec(id, id, 30, "hidden", new[] {
                new IntentSpec("intent", "Intent", "clue", DamageChannel.Health, damage, interruptible: true) })),
            new ToolSpec[0], health: health);

        [Test] public void ResolvedActionsDoNotLeakFutureIntentOrAnimateInterruptedEnemies()
        {
            var definition = Definition(); var session = new BattleSession(definition, 42);
            Assert.That(session.View.ResolvedEnemyActions, Is.Empty);
            Assert.That(session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId, "first")).Success, Is.True);
            Assert.That(session.View.ResolvedEnemyActions, Is.Empty);
            Assert.That(session.Execute(BattleCommand.EndTurn()).Success, Is.True);
            var snapshot = session.View;
            Assert.That(snapshot.ResolvedEnemyActions.Select(a => a.EnemyId), Is.EqualTo(new[] { "second" }));
            Assert.That(BattleReplay.Capture(session).Restore(definition).View.ResolvedEnemyActions.Select(a => a.EnemyId), Is.EqualTo(new[] { "second" }));
            Assert.That(session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId, "first")).Success, Is.True);
            Assert.That(session.View.ResolvedEnemyActions, Is.Empty);
            Assert.That(snapshot.ResolvedEnemyActions.Count, Is.EqualTo(1));
        }
        [Test] public void FatalFirstAttackDoesNotPresentTheSecondEnemy()
        {
            var session = new BattleSession(Definition(8, 5), 42);
            session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.Phase, Is.EqualTo(BattlePhase.Defeat));
            Assert.That(session.View.ResolvedEnemyActions.Select(a => a.EnemyId), Is.EqualTo(new[] { "first" }));
        }
        [Test] public void ObservationIsReportedAsZeroDamage()
        {
            var session = new BattleSession(Definition(0), 42);
            session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.ResolvedEnemyActions.Count, Is.EqualTo(2));
            Assert.That(session.View.ResolvedEnemyActions.All(a => a.Amount == 0), Is.True);
        }
    }
}
