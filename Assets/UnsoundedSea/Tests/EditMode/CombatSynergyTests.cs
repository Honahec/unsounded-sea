using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class CombatSynergyTests
    {
        private static BattleSession Session(string[] cards, bool known = true, int actions = 3, Evidence[] archive = null) => new BattleSession(Definition(cards, known, actions, archive), 7);
        private static BattleDefinition Definition(string[] cards, bool known = true, int actions = 3, Evidence[] archive = null)
        {
            var intent = new IntentSpec("hit", "攻击", "举起武器", DamageChannel.Health, 0, known, true);
            var enemies = new[] { "a", "b" }.Select(id => new EnemySpec(id, id, 100, "攻击", new[] { intent }));
            return new BattleDefinition("synergy", cards.Select(CardCatalog.Get), enemies,
                new[] { new ToolSpec("lamp", "提灯", InvestigationKind.VisibleAction), new ToolSpec("ear", "听筒", InvestigationKind.SoundSource) },
                actions: actions, drawCount: cards.Length, archive: archive);
        }
        private static CardView Card(BattleSession s, string id) => s.View.Hand.First(c => c.Definition.Id == id);
        private static void Play(BattleSession s, string id, string target = null) => Execute(s, BattleCommand.Play(Card(s, id).InstanceId, target));
        private static void Execute(BattleSession s, BattleCommand command)
        {
            var result = s.Execute(command);
            Assert.That(result.Success, Is.True, result.Error);
        }
        private static int Hp(BattleSession s, string id = "a") => s.View.Enemies.Single(e => e.Id == id).Health;

        [Test] public void LightThenSoundDrawsTwoOnlyForNewComplementaryAnswer()
        {
            var s = Session(new[] { "cover", "light", "c041" }, false, 6);
            Play(s, "cover"); Play(s, "light", "enemy:a"); Play(s, "c041", "enemy:a");
            Assert.That(s.View.Hand.Select(c => c.Definition.Id), Is.EquivalentTo(new[] { "cover", "light" }));
            // Recovered illumination is only a cantrip; a repeated sound cannot earn a second answer.
            Play(s, "light", "enemy:a"); Play(s, "c041", "enemy:a");
            Assert.That(s.View.Hand.Count, Is.EqualTo(2));
        }

        [Test] public void SoundThenLightAlsoRewardsCrossChecking()
        {
            var s = Session(new[] { "cover", "light", "c041" }, false);
            Play(s, "cover"); Play(s, "c041", "enemy:a"); Play(s, "light", "enemy:a");
            Assert.That(s.View.Hand.Select(c => c.Definition.Id), Is.EquivalentTo(new[] { "cover", "c041" }));
        }

        [Test] public void ObservationRefundIsSharedAcrossCopiesAndResetsNextTurn()
        {
            var s = Session(new[] { "c041", "c050", "c050" });
            Play(s, "c041", "enemy:a"); Play(s, "c050", "enemy:a"); Play(s, "c050", "enemy:a");
            Assert.That(s.View.Actions, Is.EqualTo(1));
            Execute(s, BattleCommand.EndTurn());
            Play(s, "c041", "enemy:a"); Play(s, "c050", "enemy:a");
            Assert.That(s.View.Actions, Is.EqualTo(2));
        }

        [Test] public void ObservationCannotBorrowItsFutureRefundToPayCost()
        {
            var s = Session(new[] { "c041", "c050" }, actions: 1);
            Play(s, "c041", "enemy:a");
            var result = s.Execute(BattleCommand.Play(Card(s, "c050").InstanceId, "enemy:a"));
            Assert.That(result.Success, Is.False); Assert.That(s.View.Actions, Is.Zero);
            Assert.That(s.Commands.Count, Is.EqualTo(1));
        }

        [TestCase(false, 5)] [TestCase(true, 9)]
        public void ContradictionHasUnknownAndPublicBaselines(bool known, int damage)
        {
            var s = Session(new[] { "contradiction" }, known);
            Play(s, "contradiction", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(100 - damage));
        }

        [Test] public void NewCardAnswerEnablesDamageAndWardForSameTargetAndReplays()
        {
            var s = Session(new[] { "light", "contradiction", "c050", "cover" }, false);
            Play(s, "light", "enemy:a");
            Play(s, "contradiction", "enemy:a");
            Play(s, "c050", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(88));
            Assert.That(s.View.Ward, Is.EqualTo(3));
            Assert.That(s.View.Actions, Is.EqualTo(1));
            var restored = BattleReplay.Capture(s).Restore(Definition(new[] { "light", "contradiction", "c050", "cover" }, false));
            Assert.That(restored.View.Log, Is.EqualTo(s.View.Log));
        }

        [Test] public void SoundAnswerDoesNotConfirmHiddenIntent()
        {
            var s = Session(new[] { "c041", "contradiction", "c050" }, false);
            Play(s, "c041", "enemy:a");
            Assert.That(s.View.Enemies[0].IntentConfirmed, Is.False);
            Play(s, "contradiction", "enemy:a"); Play(s, "c050", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(95)); Assert.That(s.View.Ward, Is.Zero);
        }

        [Test] public void NewSoundAnswerCanCombineWithAlreadyPublicIntent()
        {
            var s = Session(new[] { "c041", "contradiction" });
            Play(s, "c041", "enemy:a"); Play(s, "contradiction", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(88));
        }

        [Test] public void ToolAnswerAndRepeatedCardQueryDoNotGrantCardSynergy()
        {
            var s = Session(new[] { "c041", "contradiction", "c050" }, false);
            Execute(s, BattleCommand.Investigate("lamp", "a"));
            Execute(s, BattleCommand.Investigate("ear", "a"));
            Play(s, "c041", "enemy:a"); Play(s, "contradiction", "enemy:a"); Play(s, "c050", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(91)); Assert.That(s.View.Ward, Is.Zero);
        }

        [TestCase("notes")] [TestCase("light")] [TestCase("c040")]
        public void ClueOrDrawOnlyDoesNotCountAsNewAnswer(string setup)
        {
            var s = Session(new[] { setup, "contradiction" });
            Play(s, setup, setup == "c040" ? null : "enemy:a");
            Play(s, "contradiction", "enemy:a"); Assert.That(Hp(s), Is.EqualTo(91));
        }

        [Test] public void CardAnswersArePerTargetAndPerTurn()
        {
            var s = Session(new[] { "c041", "contradiction" });
            Play(s, "c041", "enemy:b"); Play(s, "contradiction", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(91));
            Execute(s, BattleCommand.EndTurn());
            Play(s, "contradiction", "enemy:b"); Assert.That(Hp(s, "b"), Is.EqualTo(91));
        }

        [TestCase(false, 5, 3)] [TestCase(true, 7, 13)]
        public void CounterattackChecksBlockBeforeAddingItsOwn(bool defend, int damage, int block)
        {
            var s = Session(new[] { "cover", "c012" });
            if (defend) Play(s, "cover");
            Play(s, "c012", "enemy:a"); Assert.That(Hp(s), Is.EqualTo(100 - damage)); Assert.That(s.View.Block, Is.EqualTo(block));
        }

        [TestCase(false, 15)] [TestCase(true, 19)]
        public void WeakPointNeedsActualPriorTurnRetention(bool crossTurn, int damage)
        {
            var s = Session(new[] { "c045", "c014" });
            var weak = Card(s, "c014"); Play(s, "c045", "hand:" + weak.InstanceId);
            if (crossTurn) Execute(s, BattleCommand.EndTurn());
            Play(s, "c014", "enemy:a"); Assert.That(Hp(s), Is.EqualTo(100 - damage));
            Assert.That(BattleReplay.Capture(s).Restore(Definition(new[] { "c045", "c014" })).View.Log, Is.EqualTo(s.View.Log));
        }

        [Test] public void RecycledWeakPointLosesRetentionAndUnknownTargetStillTakesTen()
        {
            var s = Session(new[] { "c045", "c014", "reload" }, actions: 5);
            var weak = Card(s, "c014"); Play(s, "c045", "hand:" + weak.InstanceId); Execute(s, BattleCommand.EndTurn());
            Play(s, "c014", "enemy:a"); Play(s, "reload", "discard:" + weak.InstanceId); Play(s, "c014", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(66));
            var unknown = Session(new[] { "c045", "c014" }, false);
            Play(unknown, "c045", "hand:" + Card(unknown, "c014").InstanceId); Execute(unknown, BattleCommand.EndTurn());
            Play(unknown, "c014", "enemy:a"); Assert.That(Hp(unknown), Is.EqualTo(90));
        }

        [TestCase("revolver", 6, 8)] [TestCase("c009", 4, 8)] [TestCase("c076", 11, 3)]
        public void FollowupRequiresWeaponDamageAndIncludesAreaHits(string opener, int openingDamage, int followup)
        {
            var s = Session(new[] { opener, "c016" });
            Play(s, opener, opener == "c009" ? "enemies:a,b" : "enemy:a"); Play(s, "c016", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(100 - openingDamage - followup));
        }

        [Test] public void SameWeaponInstanceCannotActivateItselfAfterRecovery()
        {
            var s = Session(new[] { "c016", "reload" }); var butt = Card(s, "c016");
            Play(s, "c016", "enemy:a"); Play(s, "reload", "discard:" + butt.InstanceId); Play(s, "c016", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(94));
            Execute(s, BattleCommand.EndTurn()); Play(s, "c016", "enemy:a"); Assert.That(Hp(s), Is.EqualTo(91));
        }

        [Test] public void FollowupTracksTargetAndDifferentInstancesOfSameCard()
        {
            var s = Session(new[] { "c016", "c016", "c016" });
            Play(s, "c016", "enemy:a"); Play(s, "c016", "enemy:b"); Play(s, "c016", "enemy:a");
            Assert.That(Hp(s), Is.EqualTo(89)); Assert.That(Hp(s, "b"), Is.EqualTo(97));
        }

        [Test] public void VerifiedSourceEnablesPiercingButUnverifiedEvidenceDoesNot()
        {
            var s = Session(new[] { "c040", "c008" }, archive: new[] { new Evidence("a/source/archive", "来源关系", "档案", KnowledgeLevel.Verified) });
            Play(s, "c040", "evidence:a/source/archive"); Play(s, "c008", "enemy:a"); Assert.That(Hp(s), Is.EqualTo(83));
            var other = Session(new[] { "c041", "c008" });
            Play(other, "c041", "enemy:a"); Play(other, "c008", "enemy:a"); Assert.That(Hp(other), Is.EqualTo(92));
        }

        [Test] public void RecoveryCostsOneActionTwoSanityAndCannotRetrieveExhaustOrForbidden()
        {
            var s = Session(new[] { "c005", "revolver", "c083", "c076" });
            Play(s, "c005", "enemy:a"); Play(s, "revolver", "enemy:a"); Play(s, "c076", "enemy:a");
            var recovery = Card(s, "c083");
            Assert.That(recovery.Targets.Count, Is.EqualTo(1));
            Play(s, "c083", recovery.Targets.Single().Id);
            Assert.That(s.View.Actions, Is.Zero); Assert.That(s.View.Sanity, Is.EqualTo(26));
            Assert.That(s.View.ExhaustCount, Is.EqualTo(2)); Assert.That(s.View.Hand.Any(c => c.Definition.Id == "revolver"), Is.True);
        }

        [Test] public void ReorderDrawsChosenTopExactlyOnceAndRejectsEmptyDeck()
        {
            var definition = new BattleDefinition("reorder-draw", new[] { "c054", "c054", "revolver", "cover", "steady" }.Select(CardCatalog.Get),
                new[] { new EnemySpec("a", "敌人", 100, "公开", new[] { new IntentSpec("idle", "静止", "静止", DamageChannel.Health, 0, true) }) }, Array.Empty<ToolSpec>(), drawCount: 3);
            var s = Enumerable.Range(0, 30).Select(seed => new BattleSession(definition, (uint)seed)).First(x => x.View.Hand.Any(c => c.Definition.Id == "c054"));
            var before = s.View.Hand.Count; Play(s, "c054");
            var order = s.View.PendingChoice.Options.Last().Id;
            var top = int.Parse(order.Substring(5).Split(',')[0]);
            var pendingReplay = BattleReplay.Capture(s).Restore(definition);
            Execute(s, BattleCommand.Reorder(order)); Execute(pendingReplay, BattleCommand.Reorder(order));
            Assert.That(s.View.Hand.Count, Is.EqualTo(before)); Assert.That(s.View.Hand.Last().InstanceId, Is.EqualTo(top));
            Assert.That(s.Execute(BattleCommand.Reorder(order)).Success, Is.False);
            Assert.That(pendingReplay.View.Hand.Select(c => c.InstanceId), Is.EqualTo(s.View.Hand.Select(c => c.InstanceId)));
            var empty = Session(new[] { "c054" }); Assert.That(Card(empty, "c054").Enabled, Is.False);
            Assert.That(empty.Execute(BattleCommand.Play(Card(empty, "c054").InstanceId)).Success, Is.False);
        }
    }
}
