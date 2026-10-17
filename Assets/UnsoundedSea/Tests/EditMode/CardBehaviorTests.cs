using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class CardBehaviorTests
    {
        private static readonly IntentSpec Hit = new IntentSpec("hit", "攻击", "举起武器", DamageChannel.Health, 6, true, true, interruptible: true);
        private static BattleDefinition Definition(CardSpec card, int health = 60, int sanity = 30, CrewSupportSpec[] supports = null, Evidence[] archive = null) =>
            new BattleDefinition("cards", Enumerable.Repeat(card, 6), new[] { new EnemySpec("enemy", "敌人", 100, "攻击", new[] { Hit }) }, Array.Empty<ToolSpec>(),
                health: health, sanity: sanity, drawCount: 5, supports: supports, archive: archive);
        private static BattleDefinition BossDefinition(BossEncounterKind kind, CardSpec card)
        {
            string[] ids;
            switch (kind)
            {
                case BossEncounterKind.Surveyor: ids = new[] { "b03-surveyor", "b03-moth-1", "b03-moth-2" }; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            var enemies = ids.Select(id => new EnemySpec(id, id, 100, "静默", new[] { Hit }));
            return new BattleDefinition("card-" + kind, Enumerable.Repeat(card, 12), enemies, Array.Empty<ToolSpec>(), health: 100, sanity: 100);
        }
        private static CardView Card(BattleSession session) => session.View.Hand.First();


        [Test] public void ExplicitHealthCostRejectsAtomically()
        {
            var session = new BattleSession(Definition(CardCatalog.Get("c081"), health: 5), 1);
            var before = session.View;
            var result = session.Execute(BattleCommand.Play(Card(session).InstanceId));
            Assert.That(result.Success, Is.False);
            Assert.That(session.View.Health, Is.EqualTo(before.Health));
            Assert.That(session.View.Actions, Is.EqualTo(before.Actions));
            Assert.That(session.Commands, Is.Empty);
        }

        [Test] public void MultiTargetChoiceIsStableAndRevalidated()
        {
            var second = new EnemySpec("other", "另一敌人", 100, "攻击", new[] { Hit });
            var card = CardCatalog.Get("c009");
            var definition = new BattleDefinition("multi", Enumerable.Repeat(card, 6), new[] { new EnemySpec("enemy", "敌人", 100, "攻击", new[] { Hit }), second }, Array.Empty<ToolSpec>(), drawCount: 5);
            var session = new BattleSession(definition, 1); var view = Card(session);
            var pair = view.Targets.Single(x => x.Id.Contains(","));
            Assert.That(session.Execute(BattleCommand.Play(view.InstanceId, pair.Id)).Success, Is.True);
            Assert.That(session.View.Enemies.All(x => x.Health == 96), Is.True);
        }

        [Test] public void CrewSupportUsesItsOwnCostAndRemainsOncePerEncounter()
        {
            var medic = new CrewSupportSpec("medic", "医务员", CrewSupportKind.Medic);
            var listener = new CrewSupportSpec("listener", "听测员", CrewSupportKind.Listener);
            var medicSession = new BattleSession(Definition(CardCatalog.Get("cover"), health: 10, supports: new[] { medic }), 1);
            Assert.That(medic.Cost, Is.EqualTo(1));
            Assert.That(medicSession.Execute(BattleCommand.EndTurn()).Success, Is.True);
            Assert.That(medicSession.View.Health, Is.EqualTo(4));
            Assert.That(medicSession.Execute(BattleCommand.Support("medic")).Success, Is.True);
            Assert.That(medicSession.View.Health, Is.EqualTo(6));
            Assert.That(medicSession.Execute(BattleCommand.Support("medic")).Success, Is.False);
            Assert.That(medicSession.View.Supports.Single().Enabled, Is.False);

            var listenerSession = new BattleSession(Definition(CardCatalog.Get("cover"), supports: new[] { listener }), 1);
            Assert.That(listener.Cost, Is.EqualTo(2));
            Assert.That(listenerSession.Execute(BattleCommand.Support("listener", "enemy")).Success, Is.True);
            Assert.That(listenerSession.View.Actions, Is.EqualTo(1));
            var insufficient = new BattleSession(Definition(CardCatalog.Get("cover"), supports: new[] { listener }), 1);
            Assert.That(insufficient.Execute(BattleCommand.Play(Card(insufficient).InstanceId)).Success, Is.True);
            Assert.That(insufficient.Execute(BattleCommand.Play(Card(insufficient).InstanceId)).Success, Is.True);
            Assert.That(insufficient.View.Actions, Is.EqualTo(1));
            Assert.That(insufficient.View.Supports.Single().Enabled, Is.False);
            Assert.That(insufficient.Execute(BattleCommand.Support("listener", "enemy")).Success, Is.False);
            Assert.That(insufficient.View.Actions, Is.EqualTo(1));
            Assert.That(insufficient.View.Supports.Single().Used, Is.False);
        }

        [Test] public void ArchiveFallbackUsesOnlySuppliedEvidence()
        {
            var archive = new Evidence("archive/fact", "已验证事实", "航程档案", KnowledgeLevel.Verified);
            var withArchive = new BattleSession(Definition(CardCatalog.Get("c040"), archive: new[] { archive }), 1);
            Assert.That(withArchive.View.Evidence, Is.Empty, "Archive facts activate only after consulting the archive.");
            var card = Card(withArchive); var target = card.Targets.Single();
            Assert.That(withArchive.Execute(BattleCommand.Play(card.InstanceId, target.Id)).Success, Is.True);
            Assert.That(withArchive.View.Evidence.Single(x => x.Key == archive.Key).Level, Is.EqualTo(KnowledgeLevel.Verified));
            Assert.That(withArchive.View.Hand.Count, Is.EqualTo(4), "Archive confirmation does not use the no-archive draw fallback.");
            card = Card(withArchive);
            Assert.That(card.Targets, Is.Empty);
            Assert.That(withArchive.Execute(BattleCommand.Play(card.InstanceId)).Success, Is.True);
            Assert.That(withArchive.View.Hand.Count, Is.EqualTo(4), "Already consulted facts use the draw fallback.");
            var withoutArchive = new BattleSession(Definition(CardCatalog.Get("c040")), 1);
            card = Card(withoutArchive); Assert.That(card.Targets, Is.Empty);
            Assert.That(withoutArchive.Execute(BattleCommand.Play(card.InstanceId)).Success, Is.True);
            Assert.That(withoutArchive.View.Hand.Count, Is.EqualTo(5), "No archive draws one after the played card leaves hand.");
        }

        [Test] public void RetainCanChooseAnyHandCardAndReconstructionChoosesTargets()
        {
            var hidden = new IntentSpec("hidden", "攻击", "征兆", DamageChannel.Health, 6, false, true);
            var hiddenOther = new IntentSpec("hidden-other", "戒备", "征兆", DamageChannel.Health, 4, false, true);
            var enemies = new[] { "a", "b", "c" }.Select(x => new EnemySpec(x, x, 20, "攻击 / 戒备", new[] { hidden, hiddenOther })).ToArray();
            var deck = new[] { CardCatalog.Get("c045"), CardCatalog.Get("revolver"), CardCatalog.Get("c053") };
            var session = new BattleSession(new BattleDefinition("retain", deck, enemies, Array.Empty<ToolSpec>(), drawCount: 3), 1);
            var retain = session.View.Hand.Single(x => x.Definition.Id == "c045");
            var revolver = session.View.Hand.Single(x => x.Definition.Id == "revolver");
            Assert.That(session.Execute(BattleCommand.Play(retain.InstanceId, retain.Targets.Single(x => x.Name == revolver.Definition.Name).Id)).Success, Is.True);
            var reconstruct = session.View.Hand.Single(x => x.Definition.Id == "c053");
            var pair = reconstruct.Targets.Single(x => x.Id == "enemies:b,c");
            Assert.That(session.Execute(BattleCommand.Play(reconstruct.InstanceId, pair.Id)).Success, Is.True);
            Assert.That(session.View.Enemies.Single(x => x.Id == "a").IntentConfirmed, Is.False);
            Assert.That(session.View.Enemies.Where(x => x.Id != "a").All(x => x.IntentConfirmed), Is.True);
            session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.Hand.Any(x => x.InstanceId == revolver.InstanceId), Is.True);

        }

        [Test] public void ZeroChoiceFallbackRejectsFabricatedTarget()
        {
            var archive = new BattleSession(Definition(CardCatalog.Get("c040")), 1);
            var card = Card(archive); var before = archive.View;
            Assert.That(card.Targets, Is.Empty);
            Assert.That(archive.Execute(BattleCommand.Play(card.InstanceId, "enemy")).Success, Is.False);
            Assert.That(archive.View.Actions, Is.EqualTo(before.Actions));

            var calm = new IntentSpec("calm", "静默", "静默", DamageChannel.Health, 0, true, true, interruptible: false);
            var signalDefinition = new BattleDefinition("signal", Enumerable.Repeat(CardCatalog.Get("c070"), 6), new[] { new EnemySpec("enemy", "敌人", 10, "静默", new[] { calm }) }, Array.Empty<ToolSpec>());
            var signal = new BattleSession(signalDefinition, 1);
            card = Card(signal);
            Assert.That(signal.Execute(BattleCommand.Play(card.InstanceId, "enemy")).Success, Is.False);
            Assert.That(signal.Execute(BattleCommand.Play(card.InstanceId)).Success, Is.True);
            Assert.That(signal.View.Ward, Is.EqualTo(2));
        }

        [Test] public void CourseCorrectionIsPaidBeforeRevealMandatoryAtZeroActionsAndReplayable()
        {
            var definition = new BattleDefinition("reorder", Enumerable.Repeat(CardCatalog.Get("c054"), 8),
                new[] { new EnemySpec("enemy", "敌人", 100, "攻击", new[] { Hit }) }, Array.Empty<ToolSpec>());
            var session = new BattleSession(definition, 7);
            for (var play = 0; play < 3; play++)
            {
                var card = Card(session);
                Assert.That(card.Targets, Is.Empty, "Deck identities stay hidden before payment.");
                Assert.That(session.Execute(BattleCommand.Play(card.InstanceId)).Success, Is.True);
                Assert.That(session.View.PendingChoice, Is.Not.Null);
                if (play == 2)
                {
                    Assert.That(session.View.Actions, Is.Zero);
                    Assert.That(session.Execute(BattleCommand.EndTurn()).Success, Is.False, "The paid reorder remains mandatory at zero actions.");
                }
                var order = session.View.PendingChoice.Options.Last().Id;
                Assert.That(session.Execute(BattleCommand.Reorder(order)).Success, Is.True);
            }
            var restored = BattleReplay.Capture(session).Restore(definition);
            Assert.That(restored.View.Actions, Is.EqualTo(session.View.Actions));
            Assert.That(restored.View.Hand.Select(x => x.InstanceId), Is.EqualTo(session.View.Hand.Select(x => x.InstanceId)));
            Assert.That(restored.View.DrawCount, Is.EqualTo(session.View.DrawCount));
        }

        [Test] public void ReconstructionDrawsTwoWhenNoVisibleUnknownIntentExists()
        {
            var hidden = new IntentSpec("hidden", "潜伏", "无可见动作", DamageChannel.Health, 6, false, false);
            var otherHidden = new IntentSpec("other-hidden", "等待", "无可见动作", DamageChannel.Health, 0, false, false);
            var definition = new BattleDefinition("reconstruct-fallback", Enumerable.Repeat(CardCatalog.Get("c053"), 8),
                new[] { new EnemySpec("enemy", "敌人", 20, "潜伏 / 等待", new[] { hidden, otherHidden }) }, Array.Empty<ToolSpec>(), drawCount: 5);
            var session = new BattleSession(definition, 2);
            var card = Card(session);
            Assert.That(card.Targets, Is.Empty);
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId)).Success, Is.True);
            Assert.That(session.View.Hand.Count, Is.EqualTo(6));
        }

        [Test] public void TemporaryBindingSkipsOnlyTheSelectedSummonAttack()
        {
            var session = new BattleSession(BossDefinition(BossEncounterKind.Surveyor, CardCatalog.Get("c062")), 1, boss: BossEncounterKind.Surveyor);
            session.Execute(BattleCommand.EndTurn());
            session.Execute(BattleCommand.EndTurn());
            var card = session.View.Hand.First(x => x.Definition.Id == "c062");
            var target = card.Targets.Single();
            var health = session.View.Health;
            var sanity = session.View.Sanity;
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, target.Id)).Success, Is.True);
            session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.Health, Is.LessThan(health), "The Surveyor's own attack still resolves.");
            Assert.That(session.View.Sanity, Is.EqualTo(sanity), "The selected moth's attack is bound for this turn.");
        }

        [Test] public void SurveyorExhaustedOpenQuotaRejectsBoundaryCardAtomically()
        {
            var session = new BattleSession(BossDefinition(BossEncounterKind.Surveyor, CardCatalog.Get("c044")), 9, boss: BossEncounterKind.Surveyor);
            var open = session.View.Hand.Single(x => x.IsEncounter && x.Definition.Id == "E03-01");
            var targets = open.Targets.Where(x => x.Enabled).Take(2).Select(x => x.Id).ToArray();
            Assert.That(session.Execute(BattleCommand.Play(open.InstanceId, targets[0])).Success, Is.True);
            open = session.View.Hand.Single(x => x.IsEncounter && x.Definition.Id == "E03-01");
            Assert.That(session.Execute(BattleCommand.Play(open.InstanceId, targets[1])).Success, Is.True);
            var card = session.View.Hand.First(x => x.Definition.Id == "c044");
            var before = session.View;
            var commands = session.Commands.Count;
            Assert.That(card.Targets, Is.Empty);
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId)).Success, Is.False);
            Assert.That(session.View.Actions, Is.EqualTo(before.Actions));
            Assert.That(session.View.Ward, Is.EqualTo(before.Ward));
            Assert.That(session.Commands.Count, Is.EqualTo(commands));
        }

        [Test] public void InvalidMediumSceneCannotFallThroughToEnemyDamage()
        {
            var session = new BattleSession(Definition(CardCatalog.Get("c056")), 3);
            var card = Card(session);
            var before = session.View;
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, "scene:not-a-medium")).Success, Is.False);
            Assert.That(session.View.Actions, Is.EqualTo(before.Actions));
            Assert.That(session.View.Enemies.Single().Health, Is.EqualTo(before.Enemies.Single().Health));
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, "enemy:enemy")).Success, Is.True);
            Assert.That(session.View.Enemies.Single().Health, Is.EqualTo(before.Enemies.Single().Health - 7));
        }

        [Test] public void ZeroAmountIntentDoesNotClaimDamageThreat()
        {
            var calm = new IntentSpec("calm", "停手", "停手", DamageChannel.Health, 0, true, true);
            var definition = new BattleDefinition("zero-threat", Enumerable.Repeat(CardCatalog.Get("c073"), 6),
                new[] { new EnemySpec("enemy", "敌人", 20, "停手", new[] { calm }) }, Array.Empty<ToolSpec>());
            var session = new BattleSession(definition, 1);
            var card = Card(session);
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, card.Targets.Single().Id)).Success, Is.True);
            Assert.That(session.View.Evidence.Single(x => x.Key.EndsWith("/categories", StringComparison.Ordinal)).Text, Does.Not.Contain("生命伤害"));
        }
        [Test] public void C087ReopensOrdinaryTaskButDoesNotPayItsCost()
        {
            var source = Definition(CardCatalog.Get("c087"));
            var session = new BattleSession(new BattleDefinition("task", source.Deck, source.Enemies, source.Tools,
                ObjectiveKind.Seal, 4, actions: 7), 1);
            var seal = session.View.Hand.Single(x => x.IsEncounter);
            Assert.That(session.Execute(BattleCommand.Play(seal.InstanceId)).Success, Is.True);
            var card = Card(session);
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, card.Targets.Single().Id)).Success, Is.True);
            Assert.That(session.View.Actions, Is.EqualTo(3));
            Assert.That(session.View.ObjectiveProgress, Is.EqualTo(1));
            Assert.That(session.Execute(BattleCommand.Play(seal.InstanceId)).Success, Is.True);
            Assert.That(session.View.Actions, Is.Zero);
            Assert.That(session.View.ObjectiveProgress, Is.EqualTo(2));
        }

        [TestCase("c075")]
        [TestCase("c086")]
        [TestCase("c090")]
        public void ForbiddenActionsRejectKnownIntentMissingTaskOrNonSummonAtomically(string id)
        {
            var archive = new[] { new Evidence("enemy/source", "已验证来源", "航程档案", KnowledgeLevel.Verified) };
            var session = new BattleSession(Definition(CardCatalog.Get(id), archive: archive), 1);
            var before = session.View;
            var card = Card(session);
            Assert.That(card.Enabled, Is.False);
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId)).Success, Is.False);
            Assert.That(session.View.Actions, Is.EqualTo(before.Actions));
            Assert.That(session.View.Sanity, Is.EqualTo(before.Sanity));
            Assert.That(session.Commands, Is.Empty);
        }

        [Test] public void DeterministicHiddenIntentCountsConfirmedButAmbiguousHiddenIntentDoesNot()
        {
            var hidden = new IntentSpec("hidden", "伏击", "黑影逼近", DamageChannel.Health, 6, false, false);
            var card = CardCatalog.Get("contradiction");
            var deterministicDefinition = new BattleDefinition("deterministic", Enumerable.Repeat(card, 6),
                new[] { new EnemySpec("enemy", "敌人", 30, "未知", new[] { hidden }) }, Array.Empty<ToolSpec>());
            var deterministic = new BattleSession(deterministicDefinition, 1);
            Assert.That(deterministic.View.Enemies.Single().IntentConfirmed, Is.True);
            var deterministicCard = Card(deterministic);
            Assert.That(deterministic.Execute(BattleCommand.Play(deterministicCard.InstanceId, deterministicCard.Targets.Single().Id)).Success, Is.True);
            var confirmedHealth = deterministic.View.Enemies.Single().Health;

            var other = new IntentSpec("other", "低语", "黑影逼近", DamageChannel.Sanity, 4, false, false);
            var ambiguousDefinition = new BattleDefinition("ambiguous", Enumerable.Repeat(card, 6),
                new[] { new EnemySpec("enemy", "敌人", 30, "伏击 / 低语", new[] { hidden, other }) }, Array.Empty<ToolSpec>());
            var ambiguous = new BattleSession(ambiguousDefinition, 1);
            Assert.That(ambiguous.View.Enemies.Single().IntentConfirmed, Is.False);
            Assert.That(ambiguous.View.Enemies.Single().Intent, Is.EqualTo("伏击 / 低语"));
            var ambiguousCard = Card(ambiguous);
            Assert.That(ambiguous.Execute(BattleCommand.Play(ambiguousCard.InstanceId, ambiguousCard.Targets.Single().Id)).Success, Is.True);
            Assert.That(ambiguous.View.Enemies.Single().Health, Is.LessThan(30));
            Assert.That(confirmedHealth, Is.LessThan(ambiguous.View.Enemies.Single().Health), "The same card must use its stronger confirmed-intent branch.");
        }
        [TestCase(true, 6, true)]
        [TestCase(false, 5, false)]
        public void DeterministicIntentControlsConditionalDrawAndKnownOnlyTargets(bool deterministic, int expectedHand, bool hasKnownTarget)
        {
            var hit = new IntentSpec("hit", "伏击", "黑影逼近", DamageChannel.Health, 6, false, false, interruptible: true);
            var intents = deterministic
                ? new[] { hit }
                : new[] { hit, new IntentSpec("whisper", "低语", "黑影逼近", DamageChannel.Sanity, 4, false, false, interruptible: true) };
            var enemy = new EnemySpec("enemy", "敌人", 30, "伏击 / 低语", intents);
            var draw = CardCatalog.Get("c050");
            var drawSession = new BattleSession(new BattleDefinition("conditional-draw-" + deterministic,
                Enumerable.Repeat(draw, 8), new[] { enemy }, Array.Empty<ToolSpec>()), 1);
            var drawCard = Card(drawSession);
            Assert.That(drawSession.Execute(BattleCommand.Play(drawCard.InstanceId, drawCard.Targets.Single().Id)).Success, Is.True);
            Assert.That(drawSession.View.Hand.Count, Is.EqualTo(expectedHand));

            var interrupt = CardCatalog.Get("c070");
            var targetSession = new BattleSession(new BattleDefinition("known-target-" + deterministic,
                Enumerable.Repeat(interrupt, 6), new[] { enemy }, Array.Empty<ToolSpec>()), 1);
            Assert.That(Card(targetSession).Targets.Any(x => x.Id == "enemy:enemy"), Is.EqualTo(hasKnownTarget));
        }


    }
}
