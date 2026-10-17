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

        [Test] public void CardAssetsMatchRuntimeCatalogIncludingCostsAndDescriptions()
        {
            foreach (var spec in CardCatalog.All)
            {
                var actual = Load<CardDefinition>("Cards/" + spec.Id + ".asset").Build();
                Assert.That(actual.Description, Is.EqualTo(spec.Description), spec.Id);
                Assert.That(actual.Cost, Is.EqualTo(spec.Cost), spec.Id);
                Assert.That(actual.SanityCost, Is.EqualTo(spec.SanityCost), spec.Id);
                Assert.That(actual.Exhaust, Is.EqualTo(spec.Exhaust), spec.Id);
                Assert.That(actual.Effects.Select(x => x.Id + ":" + x.Amount), Is.EqualTo(spec.Effects.Select(x => x.Id + ":" + x.Amount)), spec.Id);
            }
        }

        [TestCase("fog", 40, 18)] [TestCase("bones", 48, 20)] [TestCase("inverted", 48, 20)] [TestCase("fleet", 50, 20)] [TestCase("abyss", 58, 24)]
        public void DoubleEncountersUseIndependentCompanionsAndKeepPresentation(string region, int total, int companionHealth)
        {
            var battle = Load<EncounterDefinition>($"Encounters/Voyage/voyage-{region}-4.asset");
            var solo = Load<EnemyDefinition>($"Enemies/Voyage/voyage-{region}-enemy-2.asset");
            Assert.That(battle.enemies.Sum(x => x.health), Is.EqualTo(total));
            Assert.That(battle.enemies[1], Is.Not.SameAs(solo)); Assert.That(battle.enemies[1].id, Is.Not.EqualTo(solo.id));
            Assert.That(battle.enemies[1].health, Is.EqualTo(companionHealth));
            Assert.That(battle.enemies[1].presentationPrefab, Is.SameAs(solo.presentationPrefab));
            Assert.That(battle.enemies[1].presentationPrefab, Is.Not.Null);
            Assert.That(battle.Build().Enemies.Select(e => e.Id), Is.Unique);
            Assert.That(battle.enemies[1].intents.Select(i => (i.isPublic, i.visibleAction, i.interruptible, i.soundSource)),
                Is.EqualTo(solo.intents.Select(i => (i.isPublic, i.visibleAction, i.interruptible, i.soundSource))));
        }

        [Test] public void FogPrayerIsScopedToVoyageAndLegacyTrainingRemainsIndependent()
        {
            var fog = Load<EncounterDefinition>("Encounters/fog-visitors.asset").enemies[0];
            var legacy = Load<EnemyDefinition>("Enemies/prayer.asset");
            Assert.That(fog, Is.Not.SameAs(legacy)); Assert.That(fog.health, Is.EqualTo(24)); Assert.That(legacy.health, Is.EqualTo(30));
            Assert.That(fog.intents.Select(i => i.amount), Is.EqualTo(new[] { 8,4 }));
            Assert.That(fog.presentationPrefab, Is.SameAs(legacy.presentationPrefab));
        }

        [Test] public void ChoirmasterUsesAssetHealthForProgressAndNewAttackForResolution()
        {
            var s = Boss(BossEncounterKind.Choirmaster, new[] { "b02-choirmaster", "b02-bone-servant", "b02-tide-messenger" }, new[] { "cover", "steady" });
            Play(s, "E02-02", "choir-a"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E02-02", "choir-b"); s.Execute(BattleCommand.EndTurn());
            Play(s, "E02-02", "choir-c");
            Assert.That(s.View.ObjectiveTarget, Is.EqualTo(42)); Assert.That(s.View.ObjectiveProgress, Is.Zero);
            Assert.That(s.View.BossStatus, Does.Contain("42/42")); s.Execute(BattleCommand.EndTurn());
            Assert.That(s.View.Enemies[0].Intent, Does.Contain("9 点生命"));
            var hp = s.View.Health; s.Execute(BattleCommand.EndTurn());
            Assert.That(hp - s.View.Health, Is.EqualTo(12), "Main attack 9 plus living servant 3.");
        }

        [Test] public void SummonDefenseAndTrueNameApplyOnlyToLivingVerifiedSummons()
        {
            var s = Boss(BossEncounterKind.Watcher, new[] { "b05-watcher", "b05-guard-anchor", "b05-guard-lamp", "b05-guard-bell", "b05-guard-sail", "b05-guard-eye" }, new[] { "c034", "c090", "cover" });
            Play(s, "c034"); Assert.That(s.View.Block, Is.EqualTo(6));
            Assert.That(s.View.Hand.Single(c => c.Definition.Id == "c090").Enabled, Is.False);
            Play(s, "E05-01", "winch"); s.Execute(BattleCommand.EndTurn());
            Play(s, "c034"); Assert.That(s.View.Block, Is.EqualTo(12));
            var name = s.View.Hand.Single(c => c.Definition.Id == "c090");
            Assert.That(name.Targets.All(t => !t.Id.Contains("b05-watcher")), Is.True);
            Play(s, "E05-02", "b05-guard-anchor|anchor-anchor");
            Play(s, "c090", "enemy:b05-guard-anchor");
            Assert.That(s.View.Actions, Is.Zero); Assert.That(s.View.Sanity, Is.EqualTo(27)); Assert.That(s.View.ExhaustCount, Is.EqualTo(1));
            Assert.That(s.View.Enemies.Single(e => e.Id == "b05-guard-anchor").Health, Is.Zero);
            Assert.That(s.View.ObjectiveProgress, Is.EqualTo(1));
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
