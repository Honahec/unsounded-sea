using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class EnemyBalanceTests
    {
        private static readonly CardSpec Wait = new CardSpec("wait", "等待", "等待", 0, new[] { new EffectSpec("block", 0) });

        private static BattleDefinition Definition(string id, IntentPolicy policy, params IntentSpec[] intents) =>
            new BattleDefinition(id, Enumerable.Repeat(Wait, 12),
                new[] { new EnemySpec("target", "目标", 100, "候选", intents, policy) },
                Array.Empty<ToolSpec>(), health: 200, sanity: 200, drawCount: 1);

        private static IntentSpec H(string id, int amount, string clue = "共同征兆") =>
            new IntentSpec(id, id, clue, DamageChannel.Health, amount, visibleAction: true);

        private static IntentSpec S(string id, int amount, string clue = "共同征兆") =>
            new IntentSpec(id, id, clue, DamageChannel.Sanity, amount, visibleAction: true);

        [Test] public void NonFogStylesHaveExactlyTwoRhythmsPerRegion()
        {
            foreach (var region in new[] { "bones", "inverted", "fleet", "abyss" })
            {
                var styles = Enumerable.Range(0, 5).Select(i => EnemyBehavior.StyleForEncounter($"voyage-{region}-{i}")).ToArray();
                Assert.That(styles.Count(x => x == EnemyStyle.Rhythm), Is.EqualTo(2), region);
                Assert.That(styles.Count(x => x == EnemyStyle.Hidden), Is.EqualTo(3), region);
            }
            Assert.That(EnemyBehavior.StyleForEncounter("m02"), Is.EqualTo(EnemyStyle.Boss));
            Assert.That(EnemyBehavior.StyleForEncounter("voyage-evil-99"), Is.EqualTo(EnemyStyle.None));
        }

        [Test] public void RhythmStartIsSeededOnceAndReplayKeepsItsCycle()
        {
            var definition = Definition("voyage-bones-0", IntentPolicy.Cycle, H("a", 4, "甲"), H("b", 6, "乙"), H("c", 8, "丙"));
            var starts = Enumerable.Range(1, 16).Select(seed => new BattleSession(definition, (uint)seed).View.Enemies[0].Clue).Distinct().ToArray();
            Assert.That(starts.Length, Is.GreaterThan(1));
            var session = new BattleSession(definition, 7);
            var first = session.View.Enemies[0].Clue;
            session.Execute(BattleCommand.EndTurn());
            var second = session.View.Enemies[0].Clue;
            Assert.That(second, Is.Not.EqualTo(first));
            var restored = BattleReplay.Capture(session).Restore(definition);
            Assert.That(restored.View.Enemies[0].Clue, Is.EqualTo(second));
            session.Execute(BattleCommand.EndTurn()); restored.Execute(BattleCommand.EndTurn());
            Assert.That(restored.View.Enemies[0].Clue, Is.EqualTo(session.View.Enemies[0].Clue));
        }

        [Test] public void HiddenThreatKeepsCommonClueAndSortsHealthBeforeSanity()
        {
            var definition = Definition("voyage-bones-1", IntentPolicy.Random, S("shock", 5), H("strike", 12), H("charge", 0));
            BattleSession charge = null;
            for (uint seed = 1; seed < 100 && charge == null; seed++)
            {
                var candidate = new BattleSession(definition, seed);
                candidate.Execute(BattleCommand.EndTurn());
                if (candidate.View.ResolvedEnemyActions.Count == 0) charge = candidate;
            }
            var view = new BattleSession(definition, 3).View.Enemies[0];
            Assert.That(view.Clue, Is.EqualTo("共同征兆"));
            Assert.That(view.Intent.IndexOf("strike", StringComparison.Ordinal), Is.LessThan(view.Intent.IndexOf("shock", StringComparison.Ordinal)));
            Assert.That(view.Intent, Does.Not.Contain("公开"));
            Assert.That(view.Intent, Does.Not.Contain("0 生命伤害"));
            Assert.That(charge, Is.Not.Null, "A zero-damage charge remains observable in resolved behavior.");
            var late = new BattleSession(definition, 8);
            for (var i = 0; i < 3; i++) late.Execute(BattleCommand.EndTurn());
            Assert.That(late.View.Enemies[0].Surge, Is.EqualTo(2), "The public surge badge must not reveal a hidden zero-damage branch.");
        }

        [Test] public void SurgeStartsOnTurnFourAndMatchesViewResolutionAndLog()
        {
            var definition = Definition("voyage-inverted-0", IntentPolicy.Cycle, H("cut", 5), S("gaze", 5));
            var session = new BattleSession(definition, 11);
            for (var turn = 1; turn <= 5; turn++)
            {
                var expected = turn < 4 ? 0 : turn - 2;
                var before = session.View.Enemies[0];
                Assert.That(before.Surge, Is.EqualTo(expected), $"turn {turn}");
                Assert.That(before.Intent, Does.Contain((5 + expected).ToString()), $"turn {turn}");
                session.Execute(BattleCommand.EndTurn());
                var action = session.View.ResolvedEnemyActions.Single();
                Assert.That(action.Amount, Is.EqualTo(5 + expected), $"turn {turn}");
                Assert.That(session.View.Log.Any(line => line.IndexOf($"{5 + expected} 点", StringComparison.Ordinal) >= 0), Is.True, $"turn {turn}");
                if (turn == 5) break;
            }
        }
        [Test] public void ThreatReductionAppliesAfterSurge()
        {
            var source = Definition("voyage-abyss-0", IntentPolicy.Cycle, S("gaze", 4));
            var definition = new BattleDefinition(source.Id, Enumerable.Repeat(CardCatalog.Get("c023"), 12),
                source.Enemies, source.Tools, health: 200, sanity: 200, drawCount: 1);
            var session = new BattleSession(definition, 4);
            for (var i = 0; i < 3; i++) session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.Enemies[0].Intent, Does.Contain("6"));
            var card = session.View.Hand.First(x => x.Definition.Id == "c023");
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, "enemy:target")).Success, Is.True);
            Assert.That(session.View.Enemies[0].Intent, Does.Contain("2"));
            session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.ResolvedEnemyActions.Single().Amount, Is.EqualTo(2));
        }

        [Test] public void BlindSanityReductionUpdatesCandidatesWithoutRevealingTheLockedChannel()
        {
            var source = Definition("voyage-bones-1", IntentPolicy.Random, H("strike", 12), S("shock", 5));
            var definition = new BattleDefinition(source.Id, Enumerable.Repeat(CardCatalog.Get("c023"), 12),
                source.Enemies, source.Tools, health: 200, sanity: 200, drawCount: 1);
            var candidates = new string[16];
            var channels = new DamageChannel[16];
            for (var seed = 0; seed < 16; seed++)
            {
                var session = new BattleSession(definition, (uint)seed);
                var card = session.View.Hand.Single();
                Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, "enemy:target")).Success, Is.True);
                candidates[seed] = session.View.Enemies[0].Intent;
                Assert.That(candidates[seed], Does.Contain("12 生命伤害"));
                Assert.That(candidates[seed], Does.Contain("1 理智冲击"));
                session.Execute(BattleCommand.EndTurn());
                var resolved = session.View.ResolvedEnemyActions.Single();
                channels[seed] = resolved.Channel;
                Assert.That(resolved.Amount, Is.EqualTo(resolved.Channel == DamageChannel.Health ? 12 : 1));
            }
            Assert.That(candidates.Distinct().Count(), Is.EqualTo(1));
            Assert.That(channels.Distinct().Count(), Is.EqualTo(2));
        }


        [Test] public void ZeroDamageShowsPublicSurgeWithoutIncreasingDamageAndFogNeverSurges()
        {
            var zero = new BattleSession(Definition("voyage-fleet-0", IntentPolicy.Cycle, H("charge", 0)), 2);
            for (var i = 0; i < 4; i++) zero.Execute(BattleCommand.EndTurn());
            Assert.That(zero.View.Enemies[0].Surge, Is.EqualTo(3));
            Assert.That(zero.View.Enemies[0].Intent, Is.Empty);
            Assert.That(zero.View.ResolvedEnemyActions, Is.Empty);

            var fog = new BattleSession(Definition("voyage-fog-0", IntentPolicy.Cycle, H("a", 2, "甲"), H("b", 2, "乙")), 9);
            Assert.That(fog.View.Enemies[0].Clue, Is.EqualTo("甲"));
            for (var i = 0; i < 4; i++) fog.Execute(BattleCommand.EndTurn());
            Assert.That(fog.View.Enemies[0].Surge, Is.Zero);
        }

        [TestCase("voyage-bones-3", ObjectiveKind.Survive)]
        [TestCase("voyage-abyss-3", ObjectiveKind.Survive)]
        [TestCase("voyage-inverted-3", ObjectiveKind.Seal)]
        [TestCase("voyage-fleet-3", ObjectiveKind.Seal)]
        public void SecondaryObjectivesExposeMetadataAndWinByTarget(string id, ObjectiveKind kind)
        {
            Assert.That(EnemyBehavior.IsSecondaryObjective(id), Is.True);
            Assert.That(EnemyBehavior.SecondaryObjectiveKind(id), Is.EqualTo(kind));
            var session = new BattleSession(Definition(id, IntentPolicy.Cycle, H("idle", 0)), 1);
            if (kind == ObjectiveKind.Survive)
                for (var i = 0; i < 3; i++) session.Execute(BattleCommand.EndTurn());
            else
                for (var i = 0; i < 3; i++) { session.Execute(BattleCommand.Task()); if (i < 2) session.Execute(BattleCommand.EndTurn()); }
            Assert.That(session.View.ObjectiveProgress, Is.EqualTo(3));
            Assert.That(session.View.ObjectiveTarget, Is.EqualTo(3));
            Assert.That(session.View.Phase, Is.EqualTo(BattlePhase.Victory));
            Assert.That(session.View.Enemies[0].Health, Is.EqualTo(100));
        }

        [Test] public void SecondaryObjectiveAlsoWinsByDefeatButOrdinarySurvivalDoesNot()
        {
            var kill = new CardSpec("kill", "击退", "击退", 0, new[] { new EffectSpec("damage", 100) });
            var source = Definition("voyage-bones-3", IntentPolicy.Cycle, H("idle", 0));
            var secondary = new BattleSession(new BattleDefinition(source.Id, new[] { kill }, source.Enemies, source.Tools), 1);
            secondary.Execute(BattleCommand.Play(secondary.View.Hand[0].InstanceId, "target"));
            Assert.That(secondary.View.Phase, Is.EqualTo(BattlePhase.Victory));

            var ordinary = new BattleSession(new BattleDefinition("survival-sample", new[] { kill }, source.Enemies, source.Tools,
                ObjectiveKind.Survive, 3), 1);
            ordinary.Execute(BattleCommand.Play(ordinary.View.Hand[0].InstanceId, "target"));
            Assert.That(ordinary.View.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
        }
    }
}
