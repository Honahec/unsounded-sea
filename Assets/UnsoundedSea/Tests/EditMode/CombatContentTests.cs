using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class CombatContentTests
    {
        private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>("Assets/UnsoundedSea/Content/" + path);
        private static void Play(BattleSession s, string id, string target = null)
        {
            var card = s.View.Hand.First(x => x.Definition.Id == id);
            var result = s.Execute(BattleCommand.Play(card.InstanceId, target));
            Assert.That(result.Success, Is.True, result.Error);
        }
        private static BattleSession Boss(BossEncounterKind kind, string[] ids, string[] cards, uint seed = 0)
        {
            var enemies = ids.Select(id => Load<EnemyDefinition>("Enemies/" + id + ".asset").Build(id));
            return new BattleSession(new BattleDefinition("asset-boss", cards.Select(CardCatalog.Get), enemies, Array.Empty<ToolSpec>(), drawCount: cards.Length), seed, boss: kind);
        }




        [Test] public void ChoirmasterUsesAssetHealthForProgressAndNewAttackForResolution()
        {
            var s = Boss(BossEncounterKind.Choirmaster, new[] { "b02-choirmaster", "b02-bone-servant", "b02-tide-messenger" }, new[] { "cover", "steady" });
            Play(s, "E02-02", "choir-a"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E02-02", "choir-b"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E02-02", "choir-c");
            Assert.That(s.View.ObjectiveTarget, Is.EqualTo(42)); Assert.That(s.View.ObjectiveProgress, Is.Zero);
            Assert.That(s.View.Enemies[0].Health, Is.EqualTo(42));Assert.That(s.View.Enemies[0].MaxHealth,Is.EqualTo(42)); s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Enemies[0].Intent, Does.Contain("9 点生命"));
            var hp = s.View.Health; s.Execute(BattleCommand.EndTurn());
            Assert.That(hp - s.View.Health, Is.EqualTo(12), "Main attack 9 plus living servant 3.");
        }

        [Test] public void SummonDefenseAndTrueNameApplyOnlyToLivingVerifiedSummons()
        {
            var s = Boss(BossEncounterKind.Watcher, new[] { "b05-watcher", "b05-guard-anchor", "b05-guard-lamp", "b05-guard-bell", "b05-guard-sail", "b05-guard-eye" }, new[] { "c034", "c046", "c090", "cover" }, seed: 12);
            Play(s, "c034"); Assert.That(s.View.Block, Is.EqualTo(6));
            Assert.That(s.View.Hand.Single(c => c.Definition.Id == "c090").Enabled, Is.False);
            Play(s, "E05-01"); s.Execute(BattleCommand.EndTurn());
            Play(s, "c034"); Assert.That(s.View.Block, Is.EqualTo(12));
            var name = s.View.Hand.Single(c => c.Definition.Id == "c090");
            Assert.That(name.Targets.All(t => !t.Id.Contains("b05-watcher")), Is.True);
            Play(s, "c046", "enemy:b05-guard-anchor");
            var health = s.View.Enemies.Single(e => e.Id == "b05-guard-anchor").Health;
            var sanity = s.View.Sanity;
            Play(s, "c090", "enemy:b05-guard-anchor");
            Assert.That(sanity - s.View.Sanity, Is.EqualTo(3)); Assert.That(s.View.ExhaustCount, Is.EqualTo(1));
            Assert.That(s.View.Enemies.Single(e => e.Id == "b05-guard-anchor").Health, Is.EqualTo(health - 18));
            Assert.That(s.View.ObjectiveProgress, Is.Zero, "A surviving guard must not count as defeated.");
        }

        [Test] public void ZeroDamageWeaponCannotEnableFollowup()
        {
            var zero = new CardSpec("zero", "空击", "测试零伤害", 1, new[] { new EffectSpec("c001", 0) });
            var enemy = new EnemySpec("a", "敌人", 100, "静止", new[] { new IntentSpec("idle", "静止", "静止", DamageChannel.Health, 0, true) });
            var s = new BattleSession(new BattleDefinition("zero", new[] { zero, CardCatalog.Get("c016") }, new[] { enemy }, Array.Empty<ToolSpec>(), drawCount: 2), 0);
            Play(s, "zero", "enemy:a"); Play(s, "c016", "enemy:a"); Assert.That(s.View.Enemies[0].Health, Is.EqualTo(97));
        }
    }
}
