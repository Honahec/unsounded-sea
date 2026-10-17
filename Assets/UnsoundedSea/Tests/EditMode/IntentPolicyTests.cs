using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class IntentPolicyTests
    {
        private static BattleDefinition Definition(IntentPolicy policy, ToolSpec[] tools = null)
        {
            var card = new CardSpec("wait", "等待", "格挡", 0, new[] { new EffectSpec("block", 1) });
            var intents = new[]
            {
                new IntentSpec("load", "装填", "正在装填", DamageChannel.Health, 0, false),
                new IntentSpec("fire", "轰击", "火门冒烟", DamageChannel.Health, 1, false)
            };
            return new BattleDefinition("intent-policy", Enumerable.Repeat(card, 6),
                new[] { new EnemySpec("gunner", "炮手", 30, "装填 / 轰击", intents, policy) },
                tools ?? Array.Empty<ToolSpec>(), health: 20, drawCount: 1);
        }

        [Test] public void CyclePolicyAlternatesWithoutAdvancingIntentRandomness()
        {
            var first = new BattleSession(Definition(IntentPolicy.Cycle), 1);
            var second = new BattleSession(Definition(IntentPolicy.Cycle), 999);
            Assert.That(first.View.Enemies.Single().Clue, Is.EqualTo("正在装填"));
            Assert.That(second.View.Enemies.Single().Clue, Is.EqualTo("正在装填"));
            Assert.That(first.Execute(BattleCommand.EndTurn()).Success, Is.True);
            Assert.That(second.Execute(BattleCommand.EndTurn()).Success, Is.True);
            Assert.That(first.View.Enemies.Single().Clue, Is.EqualTo("火门冒烟"));
            Assert.That(second.View.Enemies.Single().Clue, Is.EqualTo("火门冒烟"));
            Assert.That(first.Execute(BattleCommand.EndTurn()).Success, Is.True);
            Assert.That(first.View.Enemies.Single().Clue, Is.EqualTo("正在装填"));
            Assert.That(BattleReplay.Fingerprint(Definition(IntentPolicy.Cycle)),
                Is.Not.EqualTo(BattleReplay.Fingerprint(Definition(IntentPolicy.Random))));
        }

        [Test] public void ToolViewUsesTheSameInvestigationLegalityAsExecution()
        {
            var lantern = new ToolSpec("lantern", "提灯", InvestigationKind.VisibleAction, 1);
            var session = new BattleSession(Definition(IntentPolicy.Cycle, new[] { lantern }), 1);
            var tool = session.View.Tools.Single();
            Assert.That(tool.Enabled, Is.True);
            Assert.That(tool.Targets.Single().Id, Is.EqualTo("gunner"));
            Assert.That(tool.Targets.Single().Enabled, Is.True);
            Assert.That(session.Execute(BattleCommand.Investigate("lantern", "gunner")).Success, Is.True);
            tool = session.View.Tools.Single();
            Assert.That(tool.Enabled, Is.False);
            Assert.That(tool.Targets.Single().Enabled, Is.False);
        }

        [Test] public void VoyageEnemiesHaveTwoInvestigableCyclicIntents()
        {
            var paths = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/UnsoundedSea/Content/Enemies/Voyage" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.Contains("voyage-") && path.Contains("-enemy-"))
                .ToArray();
            Assert.That(paths, Has.Length.EqualTo(25), "Run Unsounded Sea/Content/Setup Voyage Campaign.");
            foreach (var path in paths)
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
                Assert.That(enemy.intentPolicy, Is.EqualTo(IntentPolicy.Cycle), path);
                Assert.That(enemy.intents, Has.Length.GreaterThanOrEqualTo(2), path);
                Assert.That(enemy.intents.Any(intent => !intent.isPublic && intent.visibleAction), Is.True, path);
            }
        }
        [Test] public void EchoAlternatesAnUninterruptibleAndInterruptibleThreat()
        {
            var echo = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/UnsoundedSea/Content/Enemies/echo.asset");
            Assert.That(echo, Is.Not.Null, "Run Unsounded Sea/Content/Setup Voyage Campaign.");
            Assert.That(echo.intentPolicy, Is.EqualTo(IntentPolicy.Cycle));
            Assert.That(echo.intents.Select(intent => intent.interruptible), Is.EqualTo(new[] { false, true }));
            Assert.That(echo.intents.Select(intent => intent.amount), Is.EqualTo(new[] { 20, 10 }));
        }

    }
}
