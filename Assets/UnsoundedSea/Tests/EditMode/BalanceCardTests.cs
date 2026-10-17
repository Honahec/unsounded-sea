using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class BalanceCardTests
    {
        private static readonly IntentSpec Idle = new IntentSpec("idle", "静止", "静止", DamageChannel.Health, 0, true);
        private static BattleSession Session(string cardId, params EnemySpec[] enemies) => new BattleSession(
            new BattleDefinition("balance-cards", Enumerable.Repeat(CardCatalog.Get(cardId), 8), enemies, Array.Empty<ToolSpec>(), actions: 10), 1);
        private static EnemySpec Enemy(string id, int health = 100) => new EnemySpec(id, id, health, "静止", new[] { Idle });
        private static CardView Card(BattleSession session) => session.View.Hand.First();
        private static void Play(BattleSession session, string target = null)
        {
            var result = session.Execute(BattleCommand.Play(Card(session).InstanceId, target));
            Assert.That(result.Success, Is.True, result.Error);
        }

        [TestCase("c003", "孤注一击", 15)]
        [TestCase("c010", "捕鲸矛", 12)]
        [TestCase("c017", "压舱重击", 12)]
        public void RenamedAndSingleTargetWeaponsUseFinalBaseDamage(string id, string name, int damage)
        {
            var session = Session(id, Enemy("enemy"));
            Assert.That(CardCatalog.Get(id).Name, Is.EqualTo(name));
            Play(session, "enemy:enemy");
            Assert.That(session.View.Enemies.Single().Health, Is.EqualTo(100 - damage));
        }

        [Test] public void VolleyAndDispersalScaleOnlyWithLivingAttackableTargets()
        {
            var single = Session("c006", Enemy("a")); Play(single);
            Assert.That(single.View.Enemies.Single().Health, Is.EqualTo(91));
            var pair = Session("c006", Enemy("a"), Enemy("b")); Play(pair);
            Assert.That(pair.View.Enemies.Select(x => x.Health), Is.EqualTo(new[] { 95, 95 }));
            var dispersal = Session("c013", Enemy("a")); Play(dispersal);
            Assert.That(dispersal.View.Enemies.Single().Health, Is.EqualTo(92));
        }

        [Test] public void CrossfireDealsSixToOneOrFourToEachOfTwo()
        {
            var one = Session("c009", Enemy("a"), Enemy("b"));
            Play(one, "enemies:a");
            Assert.That(one.View.Enemies.Select(x => x.Health), Is.EqualTo(new[] { 94, 100 }));
            var two = Session("c009", Enemy("a"), Enemy("b"));
            Play(two, "enemies:a,b");
            Assert.That(two.View.Enemies.Select(x => x.Health), Is.EqualTo(new[] { 96, 96 }));
        }

        [Test] public void BlackSaltWithoutLivingSummonsIsDisabledAndAtomic()
        {
            var session = Session("c088", Enemy("enemy")); var before = session.View; var history = session.Commands.Count;
            Assert.That(Card(session).Enabled, Is.False);
            Assert.That(Card(session).DisabledReason, Does.Contain("召唤物"));
            Assert.That(session.Execute(BattleCommand.Play(Card(session).InstanceId)).Success, Is.False);
            Assert.That(session.View.Health, Is.EqualTo(before.Health));
            Assert.That(session.View.Actions, Is.EqualTo(before.Actions));
            Assert.That(session.Commands.Count, Is.EqualTo(history));
        }

        [Test] public void CatalogMetadataIsTypedCanonicalAndExcludedFromReplayFingerprint()
        {
            var source = CardCatalog.Get("c046");
            Assert.That(source.Category, Is.EqualTo(CardCategory.Investigation));
            Assert.That(source.RequiresTarget, Is.True); Assert.That(source.HasFallback, Is.False);
            Assert.That(source.BossIds, Is.EqualTo(new[] { "M02", "M03", "M04", "M05" }));
            var copied = new CardSpec(source.Id, source.Name, source.Description, source.Cost, source.Effects, source.SanityCost,
                source.Exhaust, CardCategory.Forbidden, false, true, new[] { "M99" });
            var enemy = Enemy("enemy");
            var original = new BattleDefinition("metadata", new[] { source }, new[] { enemy }, Array.Empty<ToolSpec>(), drawCount: 1);
            var changed = new BattleDefinition("metadata", new[] { copied }, new[] { enemy }, Array.Empty<ToolSpec>(), drawCount: 1);
            Assert.That(BattleReplay.Fingerprint(changed), Is.EqualTo(BattleReplay.Fingerprint(original)));
            var assetStyle = new CardSpec("asset-copy", source.Name, source.Description, source.Cost, source.Effects, source.SanityCost, source.Exhaust);
            Assert.That(assetStyle.Category, Is.EqualTo(source.Category));
            Assert.That(assetStyle.RequiresTarget, Is.EqualTo(source.RequiresTarget));
            Assert.That(assetStyle.BossIds, Is.EqualTo(source.BossIds));
        }

        [Test] public void BossAndItemApplicabilityMetadataIsAuthoritative()
        {
            Assert.That(CardCatalog.Get("c057").BossIds, Is.EqualTo(new[] { "M04" }));
            Assert.That(CardCatalog.Get("c061").BossIds, Is.EqualTo(new[] { "M02" }));
            Assert.That(CardCatalog.Get("c062").BossIds, Is.EqualTo(new[] { "M02", "M03", "M04", "M05" }));
            Assert.That(CardCatalog.Get("c065").BossIds, Is.EqualTo(new[] { "M04" }));
            Assert.That(CardCatalog.Get("c078").BossIds, Is.EqualTo(new[] { "M02", "M03", "M04" }));
            Assert.That(SupportCatalog.Item("i06").BossIds, Is.EqualTo(new[] { "M03" }));
            Assert.That(SupportCatalog.Item("i07").BossIds, Is.EqualTo(new[] { "M05" }));
            Assert.That(SupportCatalog.Item("i08").BossIds, Is.EqualTo(new[] { "M04" }));
            Assert.That(SupportCatalog.Item("i09").BossIds, Is.EqualTo(new[] { "M05" }));
            Assert.That(SupportCatalog.Item("i15").BossIds, Is.EqualTo(new[] { "M02", "M03", "M04", "M05" }));
            Assert.That(SupportCatalog.Item("i21").BossIds, Is.EqualTo(new[] { "M05" }));
            Assert.That(SupportCatalog.Item("i24").BossIds, Is.EqualTo(new[] { "M03" }));
            Assert.That(SupportCatalog.Item("i25").BossIds, Is.Empty);
        }
        private static BattleSession ObjectiveSession(string encounterId, string[] cards, string[] consumables = null, string[] equipment = null, Evidence[] archive = null)
        {
            var definition = new BattleDefinition(encounterId, cards.Select(CardCatalog.Get), new[] { Enemy("enemy", 500) },
                Array.Empty<ToolSpec>(), actions: 12, drawCount: cards.Length, archive: archive,
                loadout: new BattleLoadout(consumables, equipment));
            return new BattleSession(definition, 2);
        }

        [TestCase("voyage-inverted-3", "c058", 1)]
        [TestCase("voyage-inverted-3", "c072", 2)]
        [TestCase("voyage-abyss-3", "c069", 1)]
        [TestCase("voyage-abyss-3", "c072", 2)]
        public void OrdinarySecondaryCardsAdvanceOnlyTheirApplicableTarget(string encounterId, string cardId, int progress)
        {
            var session = ObjectiveSession(encounterId, new[] { cardId }); var card = Card(session);
            Assert.That(card.Targets, Is.Not.Empty); Play(session, card.Targets.Single().Id);
            Assert.That(session.View.ObjectiveProgress, Is.EqualTo(progress));
        }

        [Test] public void EvidenceCardAdvancesOrdinarySecondaryAndMissingEvidenceIsAtomic()
        {
            var archive = new Evidence("archive/proof", "工序已验证", "档案", KnowledgeLevel.Verified);
            var ordinary = ObjectiveSession("voyage-inverted-3", new[] { "c040", "c086" }, archive: new[] { archive });
            var consult = ordinary.View.Hand.Single(x => x.Definition.Id == "c040");
            Assert.That(ordinary.Execute(BattleCommand.Play(consult.InstanceId, consult.Targets.Single().Id)).Success, Is.True);
            var card = ordinary.View.Hand.Single(x => x.Definition.Id == "c086");
            Assert.That(ordinary.Execute(BattleCommand.Play(card.InstanceId, card.Targets.Single().Id)).Success, Is.True);
            Assert.That(ordinary.View.ObjectiveProgress, Is.EqualTo(1));
            var missing = ObjectiveSession("voyage-inverted-3", new[] { "c086" }); var before = missing.View;
            Assert.That(Card(missing).Enabled, Is.False);
            Assert.That(missing.Execute(BattleCommand.Play(Card(missing).InstanceId)).Success, Is.False);
            Assert.That(missing.View.Actions, Is.EqualTo(before.Actions)); Assert.That(missing.Commands, Is.Empty);
        }

        [Test] public void SealMudAdvancesOnceAcrossCopiesAndFailedRepeatIsAtomic()
        {
            var session = ObjectiveSession("voyage-inverted-3", new[] { "cover" }, new[] { "i14", "i14" });
            Assert.That(session.Execute(BattleCommand.Consume("i14")).Success, Is.True);
            Assert.That(session.View.ObjectiveProgress, Is.EqualTo(1));
            var before = session.View; var commands = session.Commands.Count;
            Assert.That(session.View.SupplyActions.Single(x => x.Id == "i14").Enabled, Is.False);
            Assert.That(session.Execute(BattleCommand.Consume("i14")).Success, Is.False);
            Assert.That(session.View.Actions, Is.EqualTo(before.Actions)); Assert.That(session.Commands.Count, Is.EqualTo(commands));
        }

        [Test] public void RescueBeltRefundsPaidEvacuationOnceAndTriggersDiverWard()
        {
            var definition = new BattleDefinition("voyage-abyss-3", new[] { CardCatalog.Get("c069"), CardCatalog.Get("c069") },
                new[] { Enemy("enemy", 500) }, Array.Empty<ToolSpec>(), actions: 8, drawCount: 2,
                supports: new[] { SupportCatalog.CrewMember("diver") }, loadout: new BattleLoadout(equipment: new[] { "i25" }));
            var session = new BattleSession(definition, 2);
            Assert.That(session.Execute(BattleCommand.Support("diver")).Success, Is.True);
            var first = session.View.Hand.First(x => x.Definition.Id == "c069");
            Assert.That(session.Execute(BattleCommand.Play(first.InstanceId, first.Targets.Single().Id)).Success, Is.True);
            Assert.That(session.View.Actions, Is.EqualTo(7)); Assert.That(session.View.Ward, Is.EqualTo(3));
            var second = session.View.Hand.First(x => x.Definition.Id == "c069");
            Assert.That(session.Execute(BattleCommand.Play(second.InstanceId, second.Targets.Single().Id)).Success, Is.True);
            Assert.That(session.View.Actions, Is.EqualTo(6)); Assert.That(session.View.Ward, Is.EqualTo(3));
        }

        [Test] public void ConcentratedWorkCannotSkipOrdinaryAreaFinalSeal()
        {
            var definition = new BattleDefinition("fog-final", new[] { CardCatalog.Get("c072") }, new[] { Enemy("enemy", 500) },
                Array.Empty<ToolSpec>(), ObjectiveKind.Seal, 3, actions: 8, drawCount: 1);
            var session = new BattleSession(definition, 3); var card = Card(session); var before = session.View;
            Assert.That(card.Enabled, Is.False); Assert.That(card.Targets, Is.Empty);
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId)).Success, Is.False);
            Assert.That(session.View.Actions, Is.EqualTo(before.Actions)); Assert.That(session.View.ObjectiveProgress, Is.Zero);
            Assert.That(session.Commands, Is.Empty);
        }

    }
}
