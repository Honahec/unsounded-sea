using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class BattleTests
    {
        private static CardSpec Card(string effect = "block", int amount = 10, int cost = 1, int sanityCost = 0, bool exhaust = false) =>
            new CardSpec(effect, effect, effect, cost, new[] { new EffectSpec(effect, amount) }, sanityCost, exhaust);
        private static BattleDefinition Definition(CardSpec[] deck = null, ObjectiveKind objective = ObjectiveKind.Defeat,
            DamageChannel channel = DamageChannel.Health, int damage = 10, int health = 60, int sanity = 30,
            bool visible = true, bool interruptible = true, ToolSpec[] tools = null, int draw = 5) =>
            new BattleDefinition("test", deck ?? Enumerable.Repeat(Card(), 10), new[]
            {
                new EnemySpec("enemy", "目标", 30, "挥刃 / 呼唤", new[]
                {
                    new IntentSpec("a", "挥刃", "相同征兆", channel, damage, visibleAction: visible, soundSource: "铜铃", interruptible: interruptible),
                    new IntentSpec("b", "呼唤", "相同征兆", channel, damage, visibleAction: visible, soundSource: "铜铃", interruptible: interruptible)
                })
            }, tools ?? new[] { new ToolSpec("lamp", "提灯", InvestigationKind.VisibleAction), new ToolSpec("ear", "听筒", InvestigationKind.SoundSource) },
                objective, 4, health, sanity, drawCount: draw);

        [Test] public void CarriedResourcesCanRecoverAboveEncounterStartingValues()
        {
            var source = Definition(new[] { Card("heal", 8), CardCatalog.Get("c081") });
            var definition = new BattleDefinition(source.Id, source.Deck, source.Enemies, source.Tools,
                initialHealth: 40, initialSanity: 12);
            var session = new BattleSession(definition, 42);
            Assert.That(session.View.Health, Is.EqualTo(40));
            Assert.That(session.View.Sanity, Is.EqualTo(12));
            Assert.That(session.Execute(BattleCommand.Play(session.View.Hand.Single(x => x.Definition.Id == "heal").InstanceId)).Success, Is.True);
            Assert.That(session.View.Health, Is.EqualTo(48));
            Assert.That(session.Execute(BattleCommand.Play(session.View.Hand.Single(x => x.Definition.Id == "c081").InstanceId)).Success, Is.True);
            Assert.That(session.View.Health, Is.EqualTo(43));
            Assert.That(session.View.Sanity, Is.EqualTo(15));
            var replay = BattleReplay.Capture(session);
            var restored = replay.Restore(definition);
            Assert.That(restored.View.Health, Is.EqualTo(43));
            Assert.That(restored.View.Sanity, Is.EqualTo(15));
            var differentStart = new BattleDefinition(source.Id, source.Deck, source.Enemies, source.Tools,
                initialHealth: 41, initialSanity: 12);
            Assert.Throws<InvalidDataException>(() => replay.Restore(differentStart));
        }

        [TestCase(0, 10)]
        [TestCase(61, 10)]
        [TestCase(10, 0)]
        [TestCase(10, 31)]
        public void InvalidCarriedResourcesAreRejected(int health, int sanity)
        {
            var source = Definition();
            Assert.Throws<ArgumentOutOfRangeException>(() => new BattleDefinition(source.Id, source.Deck,
                source.Enemies, source.Tools, initialHealth: health, initialSanity: sanity));
        }

        [Test] public void StartsWithFiveCardsThreeActionsAndLockedHiddenIntent()
        {
            var view = new BattleSession(Definition(), 42).View;
            Assert.That(view.Hand.Count, Is.EqualTo(5)); Assert.That(view.Actions, Is.EqualTo(3));
            Assert.That(view.Phase, Is.EqualTo(BattlePhase.PlayerTurn));
            Assert.That(view.Enemies[0].IntentConfirmed, Is.False);
            Assert.That(view.Enemies[0].Intent, Is.EqualTo("挥刃 / 呼唤"));
            Assert.That(view.Log.Any(x => x.Contains("IntentLocked")), Is.True);
        }

        [Test] public void InvalidTargetAndInsufficientActionsDoNotMutateBattle()
        {
            var session = new BattleSession(Definition(new[] { Card("damage", 6, 4), Card("damage", 6) }), 2);
            Assert.That(session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId, "missing")).Success, Is.False);
            var expensive = session.View.Hand.Single(x => x.Definition.Cost == 4);
            Assert.That(session.Execute(BattleCommand.Play(expensive.InstanceId, "enemy")).Success, Is.False);
            Assert.That(session.View.Actions, Is.EqualTo(3)); Assert.That(session.View.Hand.Count, Is.EqualTo(2));
            Assert.That(session.Commands, Is.Empty);
        }

        [Test] public void InvestigationNeverRerollsIntentOrChangesFutureIntentSequence()
        {
            var definition = Definition();
            var queried = new BattleSession(definition, 123); var baseline = new BattleSession(definition, 123);
            Assert.That(queried.Execute(BattleCommand.Investigate("lamp", "enemy")).Success, Is.True);
            var revealed = queried.View.Enemies[0].Intent;
            queried.Execute(BattleCommand.EndTurn()); baseline.Execute(BattleCommand.EndTurn());
            Assert.That(queried.View.Log.Single(x => x.Contains("执行")), Does.Contain(revealed));
            Assert.That(queried.View.Log.Single(x => x.Contains("执行")), Is.EqualTo(baseline.View.Log.Single(x => x.Contains("执行"))));
            Assert.That(queried.View.Enemies[0].IntentConfirmed, Is.False, "Turn-specific knowledge must expire.");
            queried.Execute(BattleCommand.EndTurn()); baseline.Execute(BattleCommand.EndTurn());
            Assert.That(queried.View.Log.Where(x => x.Contains("执行")), Is.EqualTo(baseline.View.Log.Where(x => x.Contains("执行"))));
        }

        [Test] public void ListeningRevealsOnlySoundSource()
        {
            var session = new BattleSession(Definition(), 8);
            Assert.That(session.Execute(BattleCommand.Investigate("ear", "enemy")).Success, Is.True);
            Assert.That(session.View.Enemies[0].IntentConfirmed, Is.False);
            Assert.That(session.View.Evidence.Single().Text, Does.Contain("铜铃"));
            Assert.That(session.View.Evidence.Single().Text, Does.Not.Contain("10"));
        }

        [Test] public void UnobservableActionDoesNotConsumeLantern()
        {
            var session = new BattleSession(Definition(visible: false), 3);
            Assert.That(session.Execute(BattleCommand.Investigate("lamp", "enemy")).Success, Is.False);
            Assert.That(session.View.Tools[0].RemainingUses, Is.EqualTo(1));
        }

        [Test] public void ActiveSanityCostBypassesWardWhileEnemyShockDoesNot()
        {
            var session = new BattleSession(Definition(new[] { Card("ward", 6, 0), Card("draw", 1, 0, 4) }, channel: DamageChannel.Sanity, damage: 6), 1);
            var ward = session.View.Hand.Single(x => x.Definition.Id == "ward");
            var ritual = session.View.Hand.Single(x => x.Definition.Id == "draw");
            session.Execute(BattleCommand.Play(ward.InstanceId)); session.Execute(BattleCommand.Play(ritual.InstanceId));
            Assert.That(session.View.Sanity, Is.EqualTo(26)); Assert.That(session.View.Ward, Is.EqualTo(6));
            session.Execute(BattleCommand.EndTurn()); Assert.That(session.View.Sanity, Is.EqualTo(26));
            Assert.That(session.View.Ward, Is.Zero);
        }

        [Test] public void CannotPayLastPointOfSanity()
        {
            var session = new BattleSession(Definition(new[] { Card("draw", 1, 0, 4) }, sanity: 4), 1);
            Assert.That(session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId)).Success, Is.False);
            Assert.That(session.View.Sanity, Is.EqualTo(4));
        }

        [Test] public void ProtectionIsConsumedAndResetsNextTurn()
        {
            var session = new BattleSession(Definition(new[] { Card("block", 7) }), 1);
            session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId)); session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.Health, Is.EqualTo(57)); Assert.That(session.View.Block, Is.Zero);
            Assert.That(session.View.Actions, Is.EqualTo(3));
        }

        [Test] public void PlayedDrawCardCannotDrawItself()
        {
            var session = new BattleSession(Definition(new[] { Card("draw", 2, 0) }), 1);
            session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId));
            Assert.That(session.View.Hand, Is.Empty); Assert.That(session.View.DiscardCount, Is.EqualTo(1));
            session.Execute(BattleCommand.EndTurn()); Assert.That(session.View.Hand.Count, Is.EqualTo(1));
        }

        [Test] public void ExhaustedCardsNeverReshuffle()
        {
            var session = new BattleSession(Definition(new[] { Card("heal", 4, exhaust: true) }), 1);
            session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId)); session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.ExhaustCount, Is.EqualTo(1)); Assert.That(session.View.Hand, Is.Empty);
        }

        [Test] public void CardInstancesRemainUniqueAndConservedAcrossReshuffles()
        {
            var session = new BattleSession(Definition(damage: 0), 1);
            for (var i = 0; i < 12; i++)
            {
                var view = session.View;
                Assert.That(view.Hand.Select(x => x.InstanceId).Distinct().Count(), Is.EqualTo(view.Hand.Count));
                Assert.That(view.Hand.Count + view.DrawCount + view.DiscardCount + view.ExhaustCount, Is.EqualTo(10));
                session.Execute(BattleCommand.Play(view.Hand[0].InstanceId)); session.Execute(BattleCommand.EndTurn());
            }
        }

        [Test] public void SealCardPersistsAndCompletesWithoutInvestigationOrKilling()
        {
            var definition = Definition(objective: ObjectiveKind.Seal);
            var session = new BattleSession(definition, 1);
            var seal = session.View.Hand.Single(x => x.IsEncounter);
            Assert.That(seal.Definition.Name, Is.EqualTo("绘制封线"));
            for (var i = 0; i < 4; i++)
            {
                Assert.That(session.Execute(BattleCommand.Play(seal.InstanceId)).Success, Is.True);
                Assert.That(session.Execute(BattleCommand.Play(seal.InstanceId)).Success, Is.False);
                Assert.That(session.View.Hand.Single(x => x.IsEncounter).InstanceId, Is.EqualTo(seal.InstanceId));
                if (i < 3) session.Execute(BattleCommand.EndTurn());
            }
            Assert.That(session.View.Phase, Is.EqualTo(BattlePhase.Victory));
            Assert.That(session.View.Enemies[0].Health, Is.EqualTo(30));
            Assert.That(session.View.Evidence, Is.Empty);
            var restored = BattleReplay.Capture(session).Restore(definition);
            Assert.That(restored.View.Phase, Is.EqualTo(BattlePhase.Victory));
            Assert.That(restored.View.ObjectiveProgress, Is.EqualTo(4));
        }

        [Test] public void SurviveObjectiveCountsResolvedTurns()
        {
            var session = new BattleSession(Definition(objective: ObjectiveKind.Survive, damage: 0), 1);
            Assert.That(session.Execute(BattleCommand.Task()).Success, Is.False);
            for (var i = 0; i < 4; i++) session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.Phase, Is.EqualTo(BattlePhase.Victory));
        }

        [TestCase(DamageChannel.Health)] [TestCase(DamageChannel.Sanity)]
        public void ResourceDepletionEndsBattleAndRejectsFurtherCommands(DamageChannel channel)
        {
            var session = new BattleSession(Definition(channel: channel, damage: 100), 1);
            session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.Phase, Is.EqualTo(BattlePhase.Defeat));
            Assert.That(session.Execute(BattleCommand.EndTurn()).Success, Is.False);
            Assert.That(session.Commands.Count, Is.EqualTo(1));
        }

        [Test] public void KillingLastEnemyWinsBeforeEnemyActs()
        {
            var session = new BattleSession(Definition(new[] { Card("damage", 30) }), 1);
            session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId, "enemy"));
            Assert.That(session.View.Phase, Is.EqualTo(BattlePhase.Victory)); Assert.That(session.View.Health, Is.EqualTo(60));
        }

        [Test] public void InterruptPreventsLockedActionWithoutRandomizingIt()
        {
            var session = new BattleSession(Definition(new[] { Card("interrupt", 0) }), 1);
            session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId, "enemy"));
            Assert.That(session.View.Enemies[0].Intent, Does.Contain("已打断"));
            session.Execute(BattleCommand.EndTurn()); Assert.That(session.View.Health, Is.EqualTo(60));
        }

        [Test] public void IdenticalSeedAndCommandsReplayExactlyAndCanContinue()
        {
            var definition = Definition(); var session = new BattleSession(definition, 777);
            session.Execute(BattleCommand.Investigate("lamp", "enemy"));
            session.Execute(BattleCommand.Play(session.View.Hand[0].InstanceId)); session.Execute(BattleCommand.EndTurn());
            var restored = BattleReplay.Capture(session).Restore(definition);
            Assert.That(restored.View.Log, Is.EqualTo(session.View.Log));
            Assert.That(restored.View.Hand.Select(x => x.InstanceId), Is.EqualTo(session.View.Hand.Select(x => x.InstanceId)));
            restored.Execute(BattleCommand.EndTurn()); session.Execute(BattleCommand.EndTurn());
            Assert.That(restored.View.Log, Is.EqualTo(session.View.Log));
        }

        [Test] public void ReplayRejectsChangedContentUnknownVersionAndInvalidHistory()
        {
            var definition = Definition(); var replay = BattleReplay.Capture(new BattleSession(definition, 1));
            Assert.Throws<InvalidDataException>(() => replay.Restore(Definition(damage: 11)));
            replay.version++; Assert.Throws<InvalidDataException>(() => replay.Restore(definition)); replay.version--;
            replay.commands.Add(BattleCommand.Play(-1)); Assert.Throws<InvalidDataException>(() => replay.Restore(definition));
        }

        [Test] public void CallerCannotMutateStoredCommands()
        {
            var session = new BattleSession(Definition(), 1); var command = BattleCommand.EndTurn();
            session.Execute(command); command.kind = CommandKind.PlayCard;
            session.Commands[0].kind = CommandKind.UseTool;
            Assert.That(session.Commands[0].kind, Is.EqualTo(CommandKind.EndTurn));
        }

        [Test] public void KnowledgeNeverDowngradesVerifiedFact()
        {
            var knowledge = new KnowledgeState();
            knowledge.Record(new Evidence("rule", "confirmed", "archive", KnowledgeLevel.Verified));
            knowledge.Record(new Evidence("rule", "rumor", "sailor", KnowledgeLevel.Rumored));
            Assert.That(knowledge.Entries.Single().Text, Is.EqualTo("confirmed"));
        }

        [Test] public void UnknownEffectsFailBeforeBattleStarts()
        { Assert.Throws<ArgumentException>(() => new BattleSession(Definition(new[] { Card("missing") }), 1)); }

        [Test] public void SaveStoreRoundTripsAndKeepsPreviousBackup()
        {
            var folder = Path.Combine(Path.GetTempPath(), "unsounded-tests-" + Guid.NewGuid());
            try
            {
                var definition = Definition(); var session = new BattleSession(definition, 12);
                var path = Path.Combine(folder, "battle.json"); var store = new JsonBattleSaveStore(path);
                store.Save(BattleReplay.Capture(session)); session.Execute(BattleCommand.EndTurn()); store.Save(BattleReplay.Capture(session));
                Assert.That(store.Load().Restore(definition).View.Log, Is.EqualTo(session.View.Log));
                Assert.That(File.Exists(path + ".bak"), Is.True);
                File.WriteAllText(path, "broken"); Assert.Throws<ArgumentException>(() => store.Load());
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        }

        [Test] public void MapRequiresCompletionAndOnlyAllowsConnectedBranches()
        {
            var map = new RunMap(new[] { Node("start", "left", "right"), Node("left", "end"), Node("right", "end"), Node("end") }, "start");
            Assert.That(map.TryMove("left"), Is.False); map.CompleteCurrent();
            Assert.That(map.Choices.Count, Is.EqualTo(2)); Assert.That(map.TryMove("end"), Is.False);
            Assert.That(map.TryMove("left"), Is.True); map.CompleteCurrent(); Assert.That(map.TryMove("right"), Is.False);
            map.TryMove("end"); map.CompleteCurrent(); Assert.That(map.IsFinished, Is.True);
        }
        [Test] public void MapRejectsCyclesAndDanglingEdges()
        {
            Assert.Throws<ArgumentException>(() => new RunMap(new[] { Node("a", "b"), Node("b", "a") }, "a"));
            Assert.Throws<ArgumentException>(() => new RunMap(new[] { Node("a", "missing") }, "a"));
        }
        private static RunNode Node(string id, params string[] next) => new RunNode(id, RunNodeKind.Battle, "clue", "content", next);
    }
}
