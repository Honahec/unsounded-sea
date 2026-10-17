using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class CardBossBehaviorTests
    {
        private static readonly IntentSpec Idle = new IntentSpec("idle", "公开行动", "公开行动", DamageChannel.Health, 0, true);

        private static BattleSession Session(BossEncounterKind kind, string cardId, uint seed = 0)
        {
            string[] ids;
            int[] health;
            switch (kind)
            {
                case BossEncounterKind.Choirmaster: ids = new[] { "b02-choirmaster", "b02-bone-servant", "b02-tide-messenger" }; health = new[] { 36, 8, 10 }; break;
                case BossEncounterKind.Surveyor: ids = new[] { "b03-surveyor", "b03-moth-1", "b03-moth-2" }; health = new[] { 1, 6, 6 }; break;
                case BossEncounterKind.FlagBearer: ids = new[] { "b04-flag-bearer", "b04-flag-soldier" }; health = new[] { 48, 12 }; break;
                default: ids = new[] { "b05-watcher", "b05-guard-anchor", "b05-guard-lamp", "b05-guard-bell", "b05-guard-sail", "b05-guard-eye" }; health = new[] { 1, 12, 12, 12, 12, 12 }; break;
            }
            var card = CardCatalog.Get(cardId);
            var enemies = ids.Select((id, i) => new EnemySpec(id, id, health[i], "公开", new[] { Idle }));
            return new BattleSession(new BattleDefinition("card-boss", Enumerable.Repeat(card, 20), enemies, Array.Empty<ToolSpec>(), health: 500, sanity: 500), seed, boss: kind);
        }

        private static CardView Card(BattleSession session, string id) => session.View.Hand.First(x => x.Definition.Id == id);
        private static CardView Encounter(BattleSession session, string id) => session.View.Hand.Single(x => x.IsEncounter && x.Definition.Id == id);
        private static void End(BattleSession session) => Assert.That(session.Execute(BattleCommand.EndTurn()).Success, Is.True);
        private static void Play(BattleSession session, CardView card, string target = null) => Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, target)).Success, Is.True);


        [TestCase("c045")]
        [TestCase("c071")]
        public void RetentionCardsNeverTargetEncounterCards(string cardId)
        {
            var session = Session(BossEncounterKind.Choirmaster, cardId);
            Assert.That(Card(session, cardId).Targets.Count, Is.EqualTo(4));
        }
        [Test] public void C056UsesVerifiedChoirOrderAndSharedAttempt()
        {
            var session = Session(BossEncounterKind.Choirmaster, "c056");
            Assert.That(Card(session, "c056").Targets.All(x => !x.Id.StartsWith("scene:")), Is.True);
            Play(session, Encounter(session, "E02-01"));
            var card = Card(session, "c056");
            var scene = card.Targets.Single(x => x.Id.StartsWith("scene:"));
            Play(session, card, scene.Id);
            Assert.That(Encounter(session, "E02-02").Enabled, Is.False);
            for (var remaining = 0; remaining < 2; remaining++)
            {
                End(session);
                card = Card(session, "c056");
                Play(session, card, card.Targets.Single(x => x.Id.StartsWith("scene:")).Id);
            }
            Assert.That(session.View.BossStage, Is.EqualTo(2));
        }

        [Test] public void C063CorrectsRelayThroughOrdinaryAdjustmentLimit()
        {
            var session = Session(BossEncounterKind.FlagBearer, "c063");
            Play(session, Encounter(session, "E04-01"), "R1:reverse");
            End(session);
            var correction = Card(session, "c063");
            Play(session, correction, correction.Targets.Single(x => x.Id == "scene:R1:positive").Id);
            Assert.That(session.View.BossStatus, Does.Contain("R1 正接"));
            Assert.That(Encounter(session, "E04-01").Enabled, Is.False);
        }

        [Test] public void C086CannotBypassSharedLockAllowance()
        {
            var session = Session(BossEncounterKind.FlagBearer, "c086");
            Play(session, Encounter(session, "E04-01"), "R1:positive");
            End(session);
            Play(session, Encounter(session, "E04-01"), "R2:reverse");
            End(session);
            Play(session, Encounter(session, "E04-01"), "R1:off");
            Play(session, Encounter(session, "E04-02"), "lock-I");
            Assert.That(Card(session, "c086").Targets, Is.Empty);
        }

        [Test] public void C078CancelsScheduledFlagSoldier()
        {
            var session = Session(BossEncounterKind.FlagBearer, "c078");
            End(session);
            var card = Card(session, "c078");
            Play(session, card, card.Targets.Single(x => x.Id == "scene:scheduled-soldier").Id);
            End(session);
            Assert.That(session.View.Enemies.Single(x => x.Id == "b04-flag-soldier").Health, Is.Zero);
        }

        [Test] public void C046FirstTracksPublicSummonSourceAndOnlyVerifiedRepeatDraws()
        {
            var session = Session(BossEncounterKind.Choirmaster, "c046");
            End(session);
            End(session);
            var card = Card(session, "c046");
            var target = card.Targets.Single(x => x.Id.Contains("bone-servant")).Id;
            var before = session.View.Hand.Count;
            Play(session, card, target);
            Assert.That(session.View.Hand.Count, Is.EqualTo(before - 1));
            Assert.That(session.View.Evidence.Single(x => x.Key.Contains("bone-servant/source")).Level, Is.EqualTo(KnowledgeLevel.Verified));
            card = Card(session, "c046");
            before = session.View.Hand.Count;
            Play(session, card, target);
            Assert.That(session.View.Hand.Count, Is.EqualTo(before), "Rechecking an already verified source replaces C046.");
        }
        private static void EnterLocks(BattleSession session)
        {
            Play(session, Encounter(session, "E04-01"), "R1:positive");
            End(session);
            Play(session, Encounter(session, "E04-01"), "R2:reverse");
            End(session);
        }

        [Test] public void C057DelaysAlarmDamageByOneTurn()
        {
            var delayed = Session(BossEncounterKind.FlagBearer, "c057");
            var baseline = Session(BossEncounterKind.FlagBearer, "c057");
            EnterLocks(delayed); EnterLocks(baseline);
            var card = Card(delayed, "c057");
            Play(delayed, card, card.Targets.Single().Id);
            for (var i = 0; i < 5; i++) { End(delayed); End(baseline); }
            Assert.That(delayed.View.Health, Is.EqualTo(baseline.View.Health + 12));
            End(delayed); End(baseline);
            Assert.That(delayed.View.Health, Is.EqualTo(baseline.View.Health));
        }

        [Test] public void C059CancelsOnlyTheDueAlarmPulse()
        {
            var protectedSession = Session(BossEncounterKind.FlagBearer, "c059");
            var baseline = Session(BossEncounterKind.FlagBearer, "c059");
            EnterLocks(protectedSession); EnterLocks(baseline);
            for (var i = 0; i < 4; i++) { End(protectedSession); End(baseline); }
            var card = Card(protectedSession, "c059");
            Play(protectedSession, card, card.Targets.Single().Id);
            End(protectedSession); End(baseline);
            Assert.That(protectedSession.View.Health, Is.EqualTo(baseline.View.Health + 12), "C059 cancels the alarm pulse but not the soldier's ordinary attack.");
        }

        [Test] public void C065ForcesOnlySecondLockWithoutConsumingNormalLockAllowance()
        {
            var session = Session(BossEncounterKind.FlagBearer, "c065");
            EnterLocks(session);
            Assert.That(Card(session, "c065").Enabled, Is.False);
            Play(session, Encounter(session, "E04-01"), "R1:off");
            Play(session, Encounter(session, "E04-02"), "lock-I");
            End(session);
            Play(session, Encounter(session, "E04-01"), "R2:off");
            End(session);
            Play(session, Encounter(session, "E04-01"), "R1:positive");
            var health = session.View.Health;
            var card = Card(session, "c065");
            Play(session, card, card.Targets.Single().Id);
            Assert.That(session.View.Health, Is.EqualTo(health - 4));
            Play(session, Encounter(session, "E04-02"), "lock-III");
            Assert.That(session.View.BossStage, Is.EqualTo(3));
        }

        [Test] public void C082TradesSanityForUnverifiedPublicAnchorAndRejectsRepeat()
        {
            var session = Session(BossEncounterKind.Watcher, "c082");
            var rope = Encounter(session, "E05-01");
            Play(session, rope, rope.Targets.First(x => x.Enabled).Id);
            End(session);
            var card = Card(session, "c082");
            var target = card.Targets.First();
            var before = session.View;
            Play(session, card, target.Id);
            Assert.That(session.View.Actions, Is.EqualTo(before.Actions));
            Assert.That(session.View.Sanity, Is.EqualTo(before.Sanity - 2));
            Assert.That(session.View.Evidence.Any(x => x.Key == target.Id.Substring(6).Split('|')[0] + "/anchor" && x.Level == KnowledgeLevel.Verified), Is.True);
            var next = Card(session, "c082");
            Assert.That(session.Execute(BattleCommand.Play(next.InstanceId, target.Id)).Success, Is.False);
            Assert.That(session.View.Sanity, Is.EqualTo(before.Sanity - 2));
        }
    }
}
