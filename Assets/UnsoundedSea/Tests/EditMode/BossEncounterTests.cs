using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class BossEncounterTests
    {
        private static readonly IntentSpec Idle = new IntentSpec("idle", "公开行动", "公开行动", DamageChannel.Health, 0, true);
        private static BattleDefinition Definition(BossEncounterKind kind, int health = 100, int sanity = 100)
        {
            string[] ids; int[] hitPoints;
            switch (kind)
            {
                case BossEncounterKind.Choirmaster: ids = new[] { "b02-choirmaster", "b02-bone-servant", "b02-tide-messenger" }; hitPoints = new[] { 36, 8, 10 }; break;
                case BossEncounterKind.Surveyor: ids = new[] { "b03-surveyor", "b03-moth-1", "b03-moth-2" }; hitPoints = new[] { 1, 6, 6 }; break;
                case BossEncounterKind.FlagBearer: ids = new[] { "b04-flag-bearer", "b04-flag-soldier" }; hitPoints = new[] { 48, 12 }; break;
                default: ids = new[] { "b05-watcher", "b05-guard-anchor", "b05-guard-lamp", "b05-guard-bell", "b05-guard-sail", "b05-guard-eye" }; hitPoints = new[] { 1, 12, 12, 12, 12, 12 }; break;
            }
            var deck = Enumerable.Range(0, 20).Select(i => new CardSpec("shot-" + i, "射击", "12 伤害", 1, new[] { new EffectSpec("damage", 12) }));
            var enemies = ids.Select((id, i) => new EnemySpec(id, id, hitPoints[i], "公开", new[] { Idle }));
            return new BattleDefinition("boss-" + kind, deck, enemies, Array.Empty<ToolSpec>(), health: health, sanity: sanity);
        }
        private static CardView Encounter(BattleSession s, string id) => s.View.Hand.Single(x => x.IsEncounter && x.Definition.Id == id);
        private static void Play(BattleSession s, string id, string target = null) => Assert.That(s.Execute(BattleCommand.Play(Encounter(s, id).InstanceId, target)).Success, Is.True);
        private static void Shoot(BattleSession s, string target) => Assert.That(s.Execute(BattleCommand.Play(s.View.Hand.First(x => !x.IsEncounter).InstanceId, target)).Success, Is.True);

        [Test] public void EncounterCardsFollowNormalDrawAndAreRetained()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Choirmaster), 0, boss: BossEncounterKind.Choirmaster);
            Assert.That(s.View.Hand.Take(5).All(x => !x.IsEncounter), Is.True);
            Assert.That(s.View.Hand.Skip(5).Select(x => x.Definition.Id), Is.EqualTo(new[] { "E02-01", "E02-02" }));
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Hand.Count(x => x.IsEncounter), Is.EqualTo(2), "Listening remains available until the player requests the second constraint.");
            Assert.That(s.View.Hand.Single(x => x.Definition.Id == "E02-01").Enabled, Is.True);
            Assert.That(s.View.DrawCount + s.View.DiscardCount + s.View.ExhaustCount + s.View.Hand.Count(x => !x.IsEncounter), Is.EqualTo(20));
        }

        [Test] public void ChoirmasterCanonicalRouteProtectsHealthThenWinsBeforeAttacks()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Choirmaster), 0, boss: BossEncounterKind.Choirmaster);
            Shoot(s, "b02-choirmaster"); Shoot(s, "b02-choirmaster"); Assert.That(s.View.Enemies[0].Health, Is.EqualTo(12));
            Play(s, "E02-02", "choir-a"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E02-02", "choir-b"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E02-02", "choir-c"); Assert.That(s.View.BossStage, Is.EqualTo(2));
            Shoot(s, "b02-choirmaster"); Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.Victory));
        }

        [Test] public void WrongChoirAttemptCostsOnceAndRejectedRepeatCostsNothing()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Choirmaster), 0, boss: BossEncounterKind.Choirmaster);
            Play(s, "E02-02", "choir-c"); Assert.That(s.View.Actions, Is.EqualTo(2)); Assert.That(s.View.Sanity, Is.EqualTo(96));
            var commands = s.Commands.Count; var result = s.Execute(BattleCommand.Play(Encounter(s, "E02-02").InstanceId, "choir-a"));
            Assert.That(result.Success, Is.False); Assert.That(s.View.Actions, Is.EqualTo(2)); Assert.That(s.Commands.Count, Is.EqualTo(commands));
        }

        [Test] public void SurveyorHasHiddenSeededMinesAndCanonicalSixStepVictory()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Surveyor), 0, boss: BossEncounterKind.Surveyor);
            Assert.That(s.View.BossStatus, Does.Not.Contain("A4")); Assert.That(s.View.Enemies[0].Targetable, Is.False);
            foreach (var cell in new[] { "C3", "D2" }) Play(s, "E03-01", cell); s.Execute(BattleCommand.EndTurn());
            foreach (var cell in new[] { "D3", "B4" }) Play(s, "E03-01", cell); s.Execute(BattleCommand.EndTurn());
            foreach (var cell in new[] { "C1", "C4" }) Play(s, "E03-01", cell);
            Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.Victory));
        }

        [Test] public void SurveyorMineDamageAndDuplicateValidationAreAtomic()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Surveyor), 0, boss: BossEncounterKind.Surveyor);
            Play(s, "E03-02", "A4"); Assert.That(s.View.Health, Is.EqualTo(100));
            Assert.That(s.View.EncounterTargets.Single(x => x.Id == "A4").Detail, Does.Contain("0"), "A mine does not count itself as adjacent.");
            var actions = s.View.Actions; var count = s.Commands.Count;
            Assert.That(s.Execute(BattleCommand.Play(Encounter(s, "E03-02").InstanceId, "A4")).Success, Is.False);
            Assert.That(s.View.Actions, Is.EqualTo(actions)); Assert.That(s.Commands.Count, Is.EqualTo(count));
        }

        [Test] public void FlagBearerCanonicalRelayLocksAndDamageProtection()
        {
            var s = new BattleSession(Definition(BossEncounterKind.FlagBearer), 1, boss: BossEncounterKind.FlagBearer);
            Shoot(s, "b04-flag-bearer"); Shoot(s, "b04-flag-bearer"); Assert.That(s.View.Enemies[0].Health, Is.EqualTo(24));
            Play(s, "E04-01", "R1:positive"); s.Execute(BattleCommand.EndTurn()); Play(s, "E04-01", "R2:reverse"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E04-01", "R1:off"); Play(s, "E04-02", "lock-I"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E04-01", "R1:positive"); Play(s, "E04-02", "lock-II"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E04-01", "R2:off"); Play(s, "E04-02", "lock-III"); Assert.That(s.View.BossStage, Is.EqualTo(3));
            Shoot(s, "b04-flag-bearer"); Assert.That(s.View.Actions, Is.Zero);
            var history = s.Commands.Count;
            Assert.That(s.Execute(BattleCommand.Play(s.View.Hand.First(x => !x.IsEncounter).InstanceId, "b04-flag-bearer")).Success, Is.False);
            Assert.That(s.Commands.Count, Is.EqualTo(history));
            s.Execute(BattleCommand.EndTurn());
            var health = s.View.Health; var sanity = s.View.Sanity;
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Health, Is.EqualTo(health - 12 - 4), "The first full phase-three turn deals 12 health plus the existing soldier.");
            Assert.That(s.View.Sanity, Is.EqualTo(sanity));
            Shoot(s, "b04-flag-bearer"); Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.Victory));
        }

        [Test] public void StageOneObjectiveProgressMatchesRelayAndRopeState()
        {
            var flag = new BattleSession(Definition(BossEncounterKind.FlagBearer), 1, boss: BossEncounterKind.FlagBearer);
            Assert.That(flag.View.ObjectiveProgress, Is.Zero);
            Assert.That(flag.View.ObjectiveTarget, Is.EqualTo(2));
            Play(flag, "E04-01", "R1:positive");
            Assert.That(flag.View.ObjectiveProgress, Is.EqualTo(1));

            var watcher = new BattleSession(Definition(BossEncounterKind.Watcher), 1, boss: BossEncounterKind.Watcher);
            Assert.That(watcher.View.ObjectiveTarget, Is.EqualTo(1));
            Play(watcher, "E05-01", "winch");
            Assert.That(watcher.View.BossStage, Is.EqualTo(2));
        }

        [Test] public void WatcherRequiresCorrectPairsRespawnsUnboundAndCapsActiveSlots()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Watcher), 0, boss: BossEncounterKind.Watcher);
            Assert.That(s.View.Enemies[0].Health, Is.GreaterThan(0)); Assert.That(s.View.Enemies[0].Targetable, Is.False);
            Play(s, "E05-01", "winch"); s.Execute(BattleCommand.EndTurn());
            var bad = BattleCommand.Play(Encounter(s, "E05-02").InstanceId, "b05-guard-anchor|anchor-lamp");
            Assert.That(s.Execute(bad).Success, Is.False); Assert.That(s.View.Actions, Is.EqualTo(3));
            Shoot(s, "b05-guard-anchor"); s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Enemies.Single(x => x.Id == "b05-guard-anchor").Health, Is.Zero);
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Enemies.Count(x => x.Id.Contains("guard") && x.Health > 0), Is.LessThanOrEqualTo(3));
            Assert.That(s.View.Enemies.Single(x => x.Id == "b05-guard-anchor").Health, Is.EqualTo(8));
        }

        [Test] public void ReplayIncludesBossIdentityAndRestoresRuleState()
        {
            var definition = Definition(BossEncounterKind.FlagBearer); var s = new BattleSession(definition, 7, boss: BossEncounterKind.FlagBearer);
            Play(s, "E04-01", "R1:positive"); s.Execute(BattleCommand.EndTurn());
            var replay = BattleReplay.Capture(s); var restored = replay.Restore(definition);
            Assert.That(replay.bossKind, Is.EqualTo(BossEncounterKind.FlagBearer));
            Assert.That(restored.View.BossKind, Is.EqualTo(BossEncounterKind.FlagBearer)); Assert.That(restored.View.BossStatus, Is.EqualTo(s.View.BossStatus));
            Assert.That(BattleReplay.Fingerprint(definition), Is.Not.EqualTo(BattleReplay.Fingerprint(definition, BossEncounterKind.FlagBearer)));
        }

        [Test] public void LethalEnemyResolutionStopsFurtherDamage()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Surveyor, health: 1), 1, boss: BossEncounterKind.Surveyor);
            s.Execute(BattleCommand.EndTurn()); Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.Defeat)); Assert.That(s.View.Health, Is.Zero);
        }

        [Test] public void PublicPuzzleStateAndSnapshotsAreComplete()
        {
            var choir = new BattleSession(Definition(BossEncounterKind.Choirmaster), 0, boss: BossEncounterKind.Choirmaster);
            var before = Encounter(choir, "E02-01");
            Assert.That(before.Targets, Is.Empty); Assert.That(choir.View.BossStatus, Does.Contain("潮匣在骨匣前"));
            Play(choir, "E02-01"); Assert.That(before.Enabled, Is.True, "Prior BattleView snapshots must not mutate.");
        }

        [Test] public void MeasuredSaltCellCanOpenAndOpeningQuotaResets()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Surveyor), 0, boss: BossEncounterKind.Surveyor);
            Play(s, "E03-02", "C3"); Play(s, "E03-01", "C3");
            s.Execute(BattleCommand.EndTurn()); Play(s, "E03-01", "D2"); Play(s, "E03-01", "D3");
            Assert.That(s.View.Actions, Is.EqualTo(1));
        }

        [Test] public void RuntimeSuffixedEnemyIdsAreAcceptedAndUsedByTargets()
        {
            var plain = Definition(BossEncounterKind.Watcher);
            var enemies = plain.Enemies.Select((x, i) => new EnemySpec(x.Id + ":" + i, x.Name, x.Health, x.Candidates, x.Intents));
            var definition = new BattleDefinition("suffixed", plain.Deck, enemies, plain.Tools, health: 100, sanity: 100);
            var s = new BattleSession(definition, 0, boss: BossEncounterKind.Watcher);
            Assert.That(s.View.EncounterTargets.Any(x => x.Id == "b05-guard-anchor:1|anchor-anchor"), Is.True);
            Play(s, "E05-01", "winch"); s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Enemies.Count(x => x.Health > 0), Is.EqualTo(3));
        }
    }
}


