using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class ContentTests
    {
        [TestCase("ordinary")]
        [TestCase("unknown")]
        [TestCase("seal")]
        [TestCase("effects")]
        public void SampleEncounterAssetsCreateIndependentSessions(string id)
        {
            var asset = AssetDatabase.LoadAssetAtPath<EncounterDefinition>($"Assets/UnsoundedSea/Content/Encounters/{id}.asset");
            Assert.That(asset, Is.Not.Null, "Run Unsounded Sea/Create or Open Battle Scene first.");
            var first = new BattleSession(asset.Build(), 1); var second = new BattleSession(asset.Build(), 1);
            first.Execute(BattleCommand.EndTurn());
            Assert.That(second.View.Turn, Is.EqualTo(1)); Assert.That(first.View.Turn, Is.EqualTo(2));
        }

        [Test] public void AdditionalBossesHaveDistinctPlayableContracts()
        {
            var storm = AssetDatabase.LoadAssetAtPath<BattleScenario>("Assets/UnsoundedSea/Content/Scenarios/StormGauntlet.asset");
            var beacon = AssetDatabase.LoadAssetAtPath<BattleScenario>("Assets/UnsoundedSea/Content/Scenarios/HollowBeacon.asset");
            Assert.That(storm, Is.Not.Null, "Run Unsounded Sea/Content/Setup Voyage Campaign.");
            Assert.That(beacon, Is.Not.Null, "Run Unsounded Sea/Content/Setup Voyage Campaign.");
            var stormDefinition = storm.encounter.Build();
            var beaconDefinition = beacon.encounter.Build();
            Assert.That(storm.rules, Is.TypeOf<SurvivalRuleModule>());
            Assert.That(stormDefinition.ObjectiveTarget, Is.EqualTo(5));
            Assert.That(stormDefinition.Enemies.Single().Intents.Select(x => x.Amount), Is.EquivalentTo(new[] { 18, 9 }));
            Assert.That(stormDefinition.Enemies.Single().Intents.All(x => x.IsPublic && x.Interruptible), Is.True);
            Assert.That(beacon.rules, Is.Null);
            Assert.That(beaconDefinition.Enemies.Single().Health, Is.EqualTo(48));
            Assert.That(beaconDefinition.Enemies.Single().Intents.All(x => x.IsPublic && x.Interruptible), Is.True);
        }

        [Test] public void InvalidInvestigationTypeIsRejectedBeforeUse()
        { Assert.Throws<System.ArgumentException>(() => new ToolSpec("broken", "Broken", (InvestigationKind)999)); }
    }
}
