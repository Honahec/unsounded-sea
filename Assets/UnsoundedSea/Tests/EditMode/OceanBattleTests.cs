using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class OceanBattleTests
    {
        private const string Path = "Assets/UnsoundedSea/Content/Scenarios/FogVisitors.asset";
        private static BattleScenario Scenario => AssetDatabase.LoadAssetAtPath<BattleScenario>(Path);
        private static BattleCommand Seal(BattleSession session) => BattleCommand.Play(session.View.Hand.Single(x => x.IsEncounter).InstanceId);
        [Test] public void ScenarioHasCompleteReplaceablePresentation()
        {
            Assert.That(Scenario, Is.Not.Null);
            Assert.DoesNotThrow(() => Scenario.ValidatePresentation());
            Assert.That(Scenario.encounter.enemies.Length, Is.EqualTo(2));
        }
        [Test] public void RestartCreatesIndependentObjectiveAndInvestigationState()
        {
            var a = Scenario.CreateSession(23); var b = Scenario.CreateSession(23);
            Assert.That(a.Execute(Seal(a)).Success, Is.True);
            var tool = a.View.Tools[0].Definition.Id;
            a.Execute(BattleCommand.Investigate(tool, a.View.Enemies[0].Id));
            Assert.That(b.View.ObjectiveProgress, Is.Zero);
            Assert.That(b.View.Tools[0].RemainingUses, Is.EqualTo(1));
            Assert.That(a.View.Tools[0].RemainingUses, Is.Zero);
        }
        [Test] public void FourSealsWinWithBothMonstersStillAlive()
        {
            var session = Scenario.CreateSession(20261016);
            for (var i = 0; i < 4; i++)
            {
                Assert.That(session.Execute(Seal(session)).Success, Is.True);
                Assert.That(session.Execute(Seal(session)).Success, Is.False, "Only one seal per turn.");
                if (i < 3) Assert.That(session.Execute(BattleCommand.EndTurn()).Success, Is.True);
            }
            Assert.That(session.View.Phase, Is.EqualTo(BattlePhase.Victory));
            Assert.That(session.View.Enemies.All(x => x.Health > 0), Is.True);
            Assert.That(session.Execute(BattleCommand.EndTurn()).Success, Is.False);
        }
        [Test] public void IgnoringTheObjectiveEventuallyLosesAndCannotActAfterwards()
        {
            var session = Scenario.CreateSession(20261016);
            for (var i = 0; i < 40 && session.View.Phase == BattlePhase.PlayerTurn; i++) session.Execute(BattleCommand.EndTurn());
            Assert.That(session.View.Phase, Is.EqualTo(BattlePhase.Defeat));
            Assert.That(session.Execute(BattleCommand.Task()).Success, Is.False);
        }
        [Test] public void CardTargetsOnlySelectedEnemy()
        {
            var definition = Scenario.encounter.Build();
            var session = new BattleSession(new BattleDefinition(definition.Id, new[] { CardCatalog.Get("revolver") },
                definition.Enemies, definition.Tools), 20261016);
            var card = session.View.Hand.First(x => x.Definition.Id == "revolver");
            var before = session.View; var target = before.Enemies[1];
            var targetOption = card.Targets.Single(x => x.Id.EndsWith(target.Id, System.StringComparison.Ordinal));
            Assert.That(session.Execute(BattleCommand.Play(card.InstanceId, targetOption.Id)).Success, Is.True);
            Assert.That(session.View.Enemies[0].Health, Is.EqualTo(before.Enemies[0].Health));
            Assert.That(session.View.Enemies[1].Health, Is.LessThan(target.Health));
        }
        [Test] public void RuleModuleNeverSharesMutableProgress()
        {
            var module = ScriptableObject.CreateInstance<SealingRuleModule>();
            try
            {
                var definition = Scenario.encounter.Build(); var a = module.CreateObjective(definition); var b = module.CreateObjective(definition);
                a.Advance(4);
                Assert.That(a.IsComplete(false), Is.True); Assert.That(b.IsComplete(true), Is.False);
            }
            finally { Object.DestroyImmediate(module); }
        }
    }
}
