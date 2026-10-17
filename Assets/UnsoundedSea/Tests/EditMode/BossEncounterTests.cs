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
                default: ids = new[] { "b05-watcher", "b05-guard-anchor", "b05-guard-lamp", "b05-guard-bell", "b05-guard-sail", "b05-guard-eye" }; hitPoints = new[] { 1, 26, 20, 22, 28, 18 }; break;
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

        [Test] public void WatcherSummonsEachGuardOnceAndWinsOnlyAfterAllFiveAreDefeated()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Watcher), 0, boss: BossEncounterKind.Watcher);
            Assert.That(s.View.ObjectiveTarget, Is.EqualTo(5));
            Assert.That(s.View.ObjectiveProgress, Is.Zero, "Uncalled guards are not defeated.");
            Assert.That(s.View.Enemies[0].Targetable, Is.False);
            Play(s, "E05-01", "b05-guard-anchor");
            var actions = s.View.Actions; var commands = s.Commands.Count;
            Assert.That(s.Execute(BattleCommand.Play(Encounter(s, "E05-01").InstanceId, "b05-guard-anchor")).Success, Is.False);
            Assert.That(s.View.Actions, Is.EqualTo(actions)); Assert.That(s.Commands.Count, Is.EqualTo(commands));
            Play(s, "E05-01", "b05-guard-lamp"); Play(s, "E05-01", "b05-guard-bell");
            s.Execute(BattleCommand.EndTurn());
            Play(s, "E05-01", "b05-guard-sail"); Play(s, "E05-01", "b05-guard-eye");
            Assert.That(s.View.Enemies.Count(x => x.Id.Contains("guard") && x.Health > 0), Is.EqualTo(5));
            Assert.That(s.View.Hand.Any(x => x.Definition.Id == "E05-01"), Is.False);
            foreach (var id in new[] { "anchor", "lamp", "bell", "sail", "eye" })
            {
                var target = "b05-guard-" + id;
                while (s.View.Enemies.Single(x => x.Id == target).Health > 0)
                {
                    if (s.View.Actions == 0 || !s.View.Hand.Any(x => !x.IsEncounter)) s.Execute(BattleCommand.EndTurn());
                    Shoot(s, target);
                }
                if (id != "eye") Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            }
            Assert.That(s.View.ObjectiveProgress, Is.EqualTo(5));
            Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.Victory));
        }

        [Test] public void WatcherDealsTwoSanityFromFirstTurnAndWardAbsorbsIt()
        {
            var plain = Definition(BossEncounterKind.Watcher, sanity: 30);
            var definition = new BattleDefinition("watcher-ward", new[] { CardCatalog.Get("c035") }, plain.Enemies, plain.Tools, sanity: 30, drawCount: 1);
            var s = new BattleSession(definition, 0, boss: BossEncounterKind.Watcher);
            var ward = s.View.Hand.Single(x => x.Definition.Id == "c035");
            Assert.That(s.Execute(BattleCommand.Play(ward.InstanceId)).Success, Is.True); s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Sanity, Is.EqualTo(30), "3 ward must absorb the first-turn 2 sanity.");
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Sanity, Is.EqualTo(28));
        }

        [Test] public void WatcherGuardsTelegraphPhysicalPressureAndAlternatingBellBurst()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Watcher), 0, boss: BossEncounterKind.Watcher);
            Play(s, "E05-01", "b05-guard-lamp"); Play(s, "E05-01", "b05-guard-bell");
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Health, Is.EqualTo(100), "New guards do not attack on their summon turn.");
            Assert.That(s.View.Sanity, Is.EqualTo(98));
            Assert.That(s.View.Enemies.Single(x => x.Id.EndsWith("lamp")).Intent, Does.Contain("4 点生命伤害"));
            Assert.That(s.View.Enemies.Single(x => x.Id.EndsWith("bell")).Intent, Does.Contain("5 点理智冲击"));
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Health, Is.EqualTo(96));
            Assert.That(s.View.Sanity, Is.EqualTo(91));
            Assert.That(s.View.Enemies.Single(x => x.Id.EndsWith("bell")).Intent, Does.Contain("本回合停手"));
        }

        [Test] public void ReplayIncludesBossIdentityAndRestoresSummonedGuardState()
        {
            var definition = Definition(BossEncounterKind.Watcher); var s = new BattleSession(definition, 7, boss: BossEncounterKind.Watcher);
            Play(s, "E05-01", "b05-guard-anchor"); s.Execute(BattleCommand.EndTurn());
            var replay = BattleReplay.Capture(s); var json = Newtonsoft.Json.JsonConvert.SerializeObject(replay);
            var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<BattleReplay>(json).Restore(definition);
            Assert.That(replay.bossKind, Is.EqualTo(BossEncounterKind.Watcher));
            Assert.That(restored.View.BossStatus, Is.EqualTo(s.View.BossStatus));
            Assert.That(restored.View.Enemies.Select(x => x.Health), Is.EqualTo(s.View.Enemies.Select(x => x.Health)));
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
            Assert.That(Encounter(s, "E05-01").Targets.Any(x => x.Id == "b05-guard-anchor:1"), Is.True);
            Play(s, "E05-01", "b05-guard-anchor:1");
            Assert.That(s.View.Enemies.Single(x => x.Id == "b05-guard-anchor:1").Health, Is.EqualTo(26));
        }
    }
}


