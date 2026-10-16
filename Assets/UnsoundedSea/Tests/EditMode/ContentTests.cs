using NUnit.Framework;
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

        [Test] public void InvalidInvestigationTypeIsRejectedBeforeUse()
        { Assert.Throws<System.ArgumentException>(() => new ToolSpec("broken", "Broken", (InvestigationKind)999)); }
    }
}
