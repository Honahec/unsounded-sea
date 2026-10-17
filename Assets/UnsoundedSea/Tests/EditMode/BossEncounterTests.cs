using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class BossEncounterTests
    {
        [Test] public void SaltGraphicsExposeOnlyPublishedCountsAndRelayGraphicsMatchCommands()
        {
            var salt=new BattleSession(Definition(BossEncounterKind.Surveyor),42,boss:BossEncounterKind.Surveyor);
            var unknown=salt.View.EncounterTargets.Where(x=>x.State==MechanismState.Unmeasured).ToArray();Assert.That(unknown,Is.Not.Empty);Assert.That(unknown.All(x=>x.MeasuredValue==null),Is.True);
            Play(salt,"E03-02",unknown[0].Id);var measured=salt.View.EncounterTargets.Single(x=>x.Id==unknown[0].Id);Assert.That(measured.State,Is.EqualTo(MechanismState.Measured));Assert.That(measured.MeasuredValue,Is.InRange(0,8));
            var relays=new BattleSession(Definition(BossEncounterKind.FlagBearer),42,boss:BossEncounterKind.FlagBearer);
            Assert.That(relays.View.EncounterTargets.Single(x=>x.Id=="R1:off").IsCurrent,Is.True);
            Play(relays,"E04-01","R1:positive");Assert.That(relays.View.EncounterTargets.Single(x=>x.Id=="R1:off").IsCurrent,Is.False);Assert.That(relays.View.EncounterTargets.Single(x=>x.Id=="R1:positive").IsCurrent,Is.True);
        }
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
            Assert.That(s.View.Hand.Where(x => x.IsEncounter).Select(x => x.Definition.Id), Is.EqualTo(new[] { "E02-02" }), "Turn two automatically completes the public order.");
            Assert.That(s.View.DrawCount + s.View.DiscardCount + s.View.ExhaustCount + s.View.Hand.Count(x => !x.IsEncounter), Is.EqualTo(20));
        }

        [Test] public void AllChoirOrdersCanBeSolvedFromFreePublicConstraintsAndReplay()
        {
            var labels = new[] { "潮匣", "骨匣", "息匣" };
            var ids = new[] { "choir-a", "choir-b", "choir-c" };
            for (uint seed = 0; seed < 6; seed++)
            {
                var definition = Definition(BossEncounterKind.Choirmaster);
                var s = new BattleSession(definition, seed, boss: BossEncounterKind.Choirmaster);
                Assert.That(s.View.BossStatus.Split('；').Length, Is.EqualTo(1));
                s.Execute(BattleCommand.EndTurn());
                var status = s.View.BossStatus;
                var orders = (from a in Enumerable.Range(0, 3) from b in Enumerable.Range(0, 3) from c in Enumerable.Range(0, 3)
                    where a != b && a != c && b != c
                    where status.Contains(labels[a] + "在" + labels[b] + "前") && status.Contains(labels[b] + "在" + labels[c] + "前")
                    select new[] { a, b, c }).ToArray();
                Assert.That(orders.Length, Is.EqualTo(1), status);
                foreach (var index in orders.Single())
                {
                    Play(s, "E02-02", ids[index]);
                    if (s.View.BossStage == 1) s.Execute(BattleCommand.EndTurn());
                }
                Assert.That(s.View.BossStage, Is.EqualTo(2));
                var restored = BattleReplay.Capture(s).Restore(definition);
                Assert.That(restored.View.Log, Is.EqualTo(s.View.Log));
                Assert.That(restored.View.Enemies.Select(e => e.Health), Is.EqualTo(s.View.Enemies.Select(e => e.Health)));
            }
        }

        [Test] public void EarlyListeningDoesNotRepeatOrChargeWhenTurnTwoRevealsInformation()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Choirmaster), 4, boss: BossEncounterKind.Choirmaster);
            Play(s, "E02-01"); var status = s.View.BossStatus;
            Assert.That(s.View.Actions, Is.EqualTo(2));
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.BossStatus, Is.EqualTo(status));
            Assert.That(s.View.Actions, Is.EqualTo(3));
            Assert.That(s.View.Hand.Any(c => c.Definition.Id == "E02-01"), Is.False);
        }

        [Test] public void MessengerHealingProducesAResolvedCueOnlyAfterItsSummonTurn()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Choirmaster), 0, boss: BossEncounterKind.Choirmaster);
            Shoot(s, "b02-choirmaster");
            while (s.View.Turn < 9) s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Enemies[2].Health, Is.GreaterThan(0));
            Assert.That(s.View.ResolvedEnemyActions.Any(a => a.Kind == EnemyActionKind.Heal), Is.False);
            var health = s.View.Enemies[0].Health;
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Enemies[0].Health, Is.EqualTo(health + 6));
            var cue = s.View.ResolvedEnemyActions.Single(a => a.Kind == EnemyActionKind.Heal);
            Assert.That(cue.EnemyId, Is.EqualTo("b02-tide-messenger"));
            Assert.That(cue.Amount, Is.Zero, "Healing cues are not player damage.");
        }

        [Test] public void RelayRequirementsAndCompletedLocksArePublicWithoutInspectingTheAnswer()
        {
            var s = new BattleSession(Definition(BossEncounterKind.FlagBearer), 0, boss: BossEncounterKind.FlagBearer);
            Assert.That(s.View.BossStatus, Does.Contain("R1 正接").And.Contain("R2 反接"));
            Assert.That(s.View.EncounterTargets.Single(t => t.Id == "R1:off").Detail, Does.Contain("铭牌要求正接"));
            Play(s, "E04-01", "R1:positive"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E04-01", "R2:reverse");
            Assert.That(s.View.BossStatus, Does.Contain("下回合"));
            s.Execute(BattleCommand.EndTurn());
            Play(s, "E04-01", "R1:off"); Play(s, "E04-02", "lock-I");
            var opened = s.View.EncounterTargets.Single(t => t.Id == "lock-I");
            Assert.That(opened.State, Is.EqualTo(MechanismState.Open));
            Assert.That(opened.Detail, Is.EqualTo("已释放"));
            Assert.That(s.View.BossStatus, Does.Contain("允许强开").And.Contain("警报 5"));
        }

        [TestCase(BossEncounterKind.Surveyor, 100, 1)]
        [TestCase(BossEncounterKind.FlagBearer, 12, 100)]
        public void FatalMainAttackStopsTheScheduledSummon(BossEncounterKind kind, int health, int sanity)
        {
            var s = new BattleSession(Definition(kind, health, sanity), 0, boss: kind);
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.Defeat));
            Assert.That(s.View.Enemies.Skip(1).All(e => e.Health == 0), Is.True);
            Assert.That(s.View.ResolvedEnemyActions.Count, Is.EqualTo(1));
        }

        [Test] public void EverySaltSymmetryHasAThreeTurnRouteProvenOnlyByPublicNumbers()
        {
            for (uint seed = 0; seed < 8; seed++)
            {
                var s = new BattleSession(Definition(BossEncounterKind.Surveyor), seed, boss: BossEncounterKind.Surveyor);
                for (var step = 0; step < 6 && s.View.Phase == BattlePhase.PlayerTurn; step++)
                {
                    var cells = s.View.EncounterTargets.ToArray();
                    var layouts = (from a in Enumerable.Range(0, 14) from b in Enumerable.Range(a + 1, 15 - a)
                        from c in Enumerable.Range(b + 1, 15 - b) select new[] { a, b, c }).Where(mines =>
                        cells.Select((cell, index) => new { cell, index }).All(x =>
                            (x.cell.State != MechanismState.Open || !mines.Contains(x.index)) &&
                            (!x.cell.MeasuredValue.HasValue || mines.Count(m => m != x.index && Math.Abs(m / 4 - x.index / 4) <= 1 && Math.Abs(m % 4 - x.index % 4) <= 1) == x.cell.MeasuredValue.Value))).ToArray();
                    Assert.That(layouts, Is.Not.Empty);
                    var safe = cells.Select((cell, index) => new { cell, index }).Where(x => x.cell.State == MechanismState.Unmeasured && layouts.All(m => !m.Contains(x.index)))
                        .OrderByDescending(x => x.cell.IsSeal).First();
                    Play(s, "E03-01", safe.cell.Id);
                    if (step % 2 == 1 && s.View.Phase == BattlePhase.PlayerTurn) s.Execute(BattleCommand.EndTurn());
                }
                Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.Victory), "seed " + seed);
                Assert.That(s.View.Turn, Is.EqualTo(3));
            }
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
            Assert.That(Encounter(s, "E05-01").Targets, Is.Empty);
            var actions = s.View.Actions; var commands = s.Commands.Count;
            Assert.That(s.Execute(BattleCommand.Play(Encounter(s, "E05-01").InstanceId, "b05-guard-anchor")).Success, Is.False);
            Assert.That(s.View.Actions, Is.EqualTo(actions)); Assert.That(s.Commands.Count, Is.EqualTo(commands));
            var control = new BattleSession(Definition(BossEncounterKind.Watcher), 0, boss: BossEncounterKind.Watcher);
            Play(s, "E05-01"); Play(control, "E05-01");
            Assert.That(s.View.Enemies.Select(x => x.Health), Is.EqualTo(control.View.Enemies.Select(x => x.Health)), "Rejected targets must not consume summon RNG.");
            Play(s, "E05-01"); Play(s, "E05-01");
            s.Execute(BattleCommand.EndTurn());
            Play(s, "E05-01"); Play(s, "E05-01");
            Assert.That(s.View.Enemies.Count(x => x.Id.Contains("guard") && x.Health > 0), Is.EqualTo(5));
            Assert.That(s.View.Hand.Any(x => x.Definition.Id == "E05-01"), Is.False);
            foreach (var target in s.View.Enemies.Where(x => x.Id.Contains("guard")).Select(x => x.Id).ToArray())
            {
                while (s.View.Enemies.Single(x => x.Id == target).Health > 0)
                {
                    if (s.View.Actions == 0 || !s.View.Hand.Any(x => !x.IsEncounter)) s.Execute(BattleCommand.EndTurn());
                    Shoot(s, target);
                }
                if (target != "b05-guard-eye") Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
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
            Assert.That(s.View.ResolvedEnemyActions.Single().Ward, Is.EqualTo(1));
            Assert.That(s.View.ResolvedEnemyActions.Single().Sanity, Is.EqualTo(30));
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Sanity, Is.EqualTo(28));
        }
        [Test] public void WatcherSanityPressureEscalatesAtPublishedTurnBoundariesAndCapsAtTen()
        {
            var expected = new[] { 2, 2, 2, 2, 2, 3, 3, 4, 4, 5, 6, 7, 8, 9, 10, 10 };
            var s = new BattleSession(Definition(BossEncounterKind.Watcher, sanity: 200), 0, boss: BossEncounterKind.Watcher);
            foreach (var damage in expected)
            {
                Assert.That(s.View.Enemies[0].Intent, Does.Contain($"{damage} 点理智冲击"));
                Assert.That(s.Execute(BattleCommand.EndTurn()).Success, Is.True);
                var action = s.View.ResolvedEnemyActions.Single(x => x.EnemyId == "b05-watcher");
                Assert.That(action.Amount, Is.EqualTo(damage));
            }
        }

        [Test] public void WatcherBossDoesNotAttackAfterAnEarlierGuardDefeatsThePlayer()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Watcher, health: 5), 12, boss: BossEncounterKind.Watcher);
            Play(s, "E05-01");
            s.Execute(BattleCommand.EndTurn());
            var sanity = s.View.Sanity;
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Phase, Is.EqualTo(BattlePhase.Defeat));
            Assert.That(s.View.Sanity, Is.EqualTo(sanity));
            Assert.That(s.View.ResolvedEnemyActions.Select(x => x.EnemyId), Is.EqualTo(new[] { "b05-guard-anchor" }));
        }


        [Test] public void WatcherGuardsTelegraphPhysicalPressureAndAlternatingBellBurst()
        {
            var s = new BattleSession(Definition(BossEncounterKind.Watcher), 3, boss: BossEncounterKind.Watcher);
            Play(s, "E05-01"); Play(s, "E05-01");
            Assert.That(s.View.Enemies.Where(x => x.Id.StartsWith("b05-guard") && x.Health > 0).Select(x => x.Intent), Is.All.Empty, "Freshly summoned guards show no intent.");
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Health, Is.EqualTo(100), "New guards do not attack on their summon turn.");
            Assert.That(s.View.Sanity, Is.EqualTo(98));
            Assert.That(s.View.Enemies.Single(x => x.Id.EndsWith("lamp")).Intent, Does.Contain("4 点生命伤害"));
            Assert.That(s.View.Enemies.Single(x => x.Id.EndsWith("bell")).Intent, Does.Contain("5 点理智冲击"));
            s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Health, Is.EqualTo(96));
            Assert.That(s.View.Sanity, Is.EqualTo(91));
            Assert.That(s.View.ResolvedEnemyActions.Select(x => x.EnemyId), Is.EqualTo(new[]
            {
                "b05-guard-lamp", "b05-guard-bell", "b05-watcher"
            }));
            Assert.That(s.View.ResolvedEnemyActions.Select(x => (x.Health, x.Sanity)), Is.EqualTo(new[]
            {
                (96, 98), (96, 93), (96, 91)
            }), "Each action publishes its exact post-mitigation resources in guard-then-boss order.");
            Assert.That(s.View.Enemies.Single(x => x.Id.EndsWith("bell")).Intent, Is.Empty);
        }

        [Test] public void ReplayIncludesBossIdentityAndRestoresSummonedGuardState()
        {
            var definition = Definition(BossEncounterKind.Watcher); var s = new BattleSession(definition, 7, boss: BossEncounterKind.Watcher);
            Play(s, "E05-01"); s.Execute(BattleCommand.EndTurn());
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

        [Test] public void RuntimeSuffixedEnemyIdsAreSummonedWithoutBecomingCardTargets()
        {
            var plain = Definition(BossEncounterKind.Watcher);
            var enemies = plain.Enemies.Select((x, i) => new EnemySpec(x.Id + ":" + i, x.Name, x.Health, x.Candidates, x.Intents));
            var definition = new BattleDefinition("suffixed", plain.Deck, enemies, plain.Tools, health: 100, sanity: 100);
            var s = new BattleSession(definition, 0, boss: BossEncounterKind.Watcher);
            Assert.That(Encounter(s, "E05-01").Targets, Is.Empty);
            Play(s, "E05-01");
            Assert.That(s.View.Enemies.Count(x => x.Id.StartsWith("b05-guard-", StringComparison.Ordinal) && x.Health > 0), Is.EqualTo(1));
        }
        
        [Test] public void WatcherSummonRandomnessIsSeededReplayableAndVariesAcrossSeeds()
        {
            var definition = Definition(BossEncounterKind.Watcher);
            var original = new BattleSession(definition, 0, boss: BossEncounterKind.Watcher);
            Play(original, "E05-01"); Play(original, "E05-01");
            var restored = BattleReplay.Capture(original).Restore(definition);
            Play(original, "E05-01"); Play(restored, "E05-01");
            original.Execute(BattleCommand.EndTurn()); restored.Execute(BattleCommand.EndTurn());
            Play(original, "E05-01"); Play(restored, "E05-01");
            Play(original, "E05-01"); Play(restored, "E05-01");
            Assert.That(restored.View.Enemies.Select(x => x.Health), Is.EqualTo(original.View.Enemies.Select(x => x.Health)));

            var orders = Enumerable.Range(0, 8).Select(seed =>
            {
                var session = new BattleSession(definition, (uint)seed, boss: BossEncounterKind.Watcher);
                Play(session, "E05-01");
                return session.View.Enemies.Single(x => x.Id.Contains("guard") && x.Health > 0).Id;
            });
            Assert.That(orders.Distinct().Count(), Is.GreaterThan(1));
        }
    }
}


