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

        [Test] public void VoyageEnemiesCarryTheirSyncedStylePolicyAndInformationBoundary()
        {
            var paths = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/UnsoundedSea/Content/Enemies/Voyage" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.Contains("voyage-") && path.Contains("-enemy-"))
                .ToArray();
            Assert.That(paths, Has.Length.EqualTo(25), "Run SyncCombatBalance.");
            foreach (var path in paths)
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
                var encounterId = enemy.id.Replace("-enemy-", "-");
                if (encounterId.StartsWith("voyage-fog-", StringComparison.Ordinal))
                {
                    Assert.That(enemy.intentPolicy, Is.EqualTo(IntentPolicy.Cycle), path);
                    continue;
                }
                var style = EnemyBehavior.StyleForEncounter(encounterId);
                Assert.That(enemy.intentPolicy, Is.EqualTo(style == EnemyStyle.Rhythm ? IntentPolicy.Cycle : IntentPolicy.Random), path);
                Assert.That(enemy.intents, Has.Length.InRange(2, 3), path);
                if (style == EnemyStyle.Rhythm)
                {
                    Assert.That(enemy.intents.All(intent => intent.isPublic && intent.amount > 0), Is.True, path);
                }
                else
                {
                    Assert.That(enemy.intents.Where(intent => intent.amount > 0).Select(intent => intent.channel),
                        Is.EquivalentTo(new[] { DamageChannel.Health, DamageChannel.Sanity }), path);
                    Assert.That(enemy.intents.Select(intent => intent.clue).Distinct().Count(), Is.EqualTo(1), path);
                    Assert.That(enemy.candidateDescription.IndexOf("生命伤害", StringComparison.Ordinal),
                        Is.LessThan(enemy.candidateDescription.IndexOf("理智冲击", StringComparison.Ordinal)), path);
                    Assert.That(enemy.candidateDescription, Does.Not.Contain("公开"), path);
                }
            }
        }
        [Test] public void SecondaryEncounterAssetsAndFogDisplayContractAreSynchronized()
        {
            foreach (var pair in new[]
            {
                ("bones", ObjectiveKind.Survive), ("abyss", ObjectiveKind.Survive),
                ("inverted", ObjectiveKind.Seal), ("fleet", ObjectiveKind.Seal)
            })
            {
                var path = $"Assets/UnsoundedSea/Content/Encounters/Voyage/voyage-{pair.Item1}-3.asset";
                var encounter = AssetDatabase.LoadAssetAtPath<EncounterDefinition>(path);
                Assert.That(encounter.objective, Is.EqualTo(pair.Item2), path);
                Assert.That(encounter.objectiveTarget, Is.EqualTo(3), path);
                Assert.That(encounter.displayName, Does.StartWith(pair.Item1 == "bones" ? "尸骸之海" :
                    pair.Item1 == "inverted" ? "倒悬潮庭" : pair.Item1 == "fleet" ? "无灯裂海" : "无星深渊"), path);
            }
            var fog = AssetDatabase.LoadAssetAtPath<BattleScenario>("Assets/UnsoundedSea/Content/Scenarios/FogVisitors.asset");
            Assert.That(fog.enemyStatus, Is.Not.Empty, "The HUD uses this status plus objective progress instead of displaying placeholder 999 health.");
            var definition = fog.encounter.Build();
            var view = new BattleSession(definition, 1, fog.rules?.CreateEffects(), fog.rules?.CreateObjective(definition)).View;
            Assert.That(view.ObjectiveProgress, Is.Zero);
            Assert.That(view.ObjectiveTarget, Is.EqualTo(4));
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
