using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Tests
{
    public sealed class SeaBossTests
    {
        private static BattleScenario Scenario => AssetDatabase.LoadAssetAtPath<BattleScenario>("Assets/UnsoundedSea/Content/Scenarios/DrownedBell.asset");
        [Test] public void SeaCreatureLivesOutsideVesselAndDwarfsIt()
        {
            Assert.That(Scenario,Is.Not.Null);Assert.DoesNotThrow(()=>Scenario.ValidatePresentation());
            var stage=Scenario.stagePrefab;var anchor=stage.actorAnchors.Single();
            Assert.That(anchor.IsChildOf(stage.vessel),Is.False);
            Assert.That(anchor.GetComponent<BattleAnchor>().environment,Is.EqualTo(BattleEnvironment.Sea));
            Assert.That(BattleActorView.MeasureLocalBounds(Scenario.ActorPrefab(0).transform).size.y,Is.GreaterThan(25));
            Assert.That(Vector3.Distance(anchor.position,stage.vessel.position),Is.GreaterThan(30));
        }
        [Test] public void LocalHitBoundsIgnoreAnchorRotationAndScale()
        {
            var root=new GameObject("Transformed actor");
            try
            {
                var body=GameObject.CreatePrimitive(PrimitiveType.Cube);body.transform.SetParent(root.transform,false);
                body.transform.localScale=new Vector3(30,32,16);body.transform.localPosition=new Vector3(0,16,0);
                root.transform.position=new Vector3(15,-7,48);root.transform.rotation=Quaternion.Euler(12,137,8);root.transform.localScale=Vector3.one*2;
                var bounds=BattleActorView.MeasureLocalBounds(root.transform);
                Assert.That(Vector3.Distance(bounds.center,new Vector3(0,16,0)),Is.LessThan(0.001));
                Assert.That(Vector3.Distance(bounds.size,new Vector3(30,32,16)),Is.LessThan(0.001));
            }
            finally {Object.DestroyImmediate(root);}
        }
        [Test] public void SeaAnchorUnderVesselIsRejected()
        {
            var scenario=Object.Instantiate(Scenario);var stage=Object.Instantiate(Scenario.stagePrefab);
            try
            {
                scenario.stagePrefab=stage;stage.actorAnchors[0].SetParent(stage.vessel);
                Assert.Throws<System.InvalidOperationException>(()=>scenario.ValidatePresentation());
            }
            finally {Object.DestroyImmediate(stage.gameObject);Object.DestroyImmediate(scenario);}
        }
        [Test] public void SurvivalCannotBeAdvancedByTaskOrCardProgress()
        {
            var s=Scenario.CreateSession(20261016);var before=s.View;
            Assert.That(s.Execute(BattleCommand.Task()).Success,Is.False);Assert.That(s.View.Actions,Is.EqualTo(before.Actions));
            var a=Scenario.rules.CreateObjective(Scenario.encounter.Build());var b=Scenario.rules.CreateObjective(Scenario.encounter.Build());
            a.Advance(999);Assert.That(a.Progress,Is.Zero);a.OnTurnResolved();Assert.That(a.Progress,Is.EqualTo(1));Assert.That(b.Progress,Is.Zero);
        }
        [Test] public void IgnoringAllThreatsLosesBeforeEscaping()
        {
            for(uint seed=1;seed<=32;seed++)
            {
                var s=Scenario.CreateSession(seed);
                for(int i=0;i<5&&s.View.Phase==BattlePhase.PlayerTurn;i++)s.Execute(BattleCommand.EndTurn());
                Assert.That(s.View.Phase,Is.EqualTo(BattlePhase.Defeat),"seed "+seed);
                Assert.That(s.View.ObjectiveProgress,Is.LessThan(5));
                Assert.That(s.Execute(BattleCommand.EndTurn()).Success,Is.False);
            }
        }
        [Test] public void ReadingTelegraphsAndDefendingSurvivesWithoutTools()
        {
            for(uint seed=1;seed<=32;seed++)
            {
                var s=Scenario.CreateSession(seed);
                while(s.View.Phase==BattlePhase.PlayerTurn&&s.View.Turn<=5)
                {
                    for(int i=0;i<3;i++)
                    {
                        var v=s.View;var enemy=v.Enemies[0];Assert.That(enemy.IntentConfirmed,Is.True);
                        var protection = enemy.Intent.Contains("理智") ? "steady" : "cover";
                        var card = v.Hand.Where(x => x.Enabled && (x.Definition.Id == protection || x.Definition.Id == "intuition"))
                            .OrderByDescending(x => x.Definition.Id == protection).FirstOrDefault();
                        if(card==null)break;
                        Assert.That(s.Execute(BattleCommand.Play(card.InstanceId)).Success,Is.True);
                    }
                    Assert.That(s.Execute(BattleCommand.EndTurn()).Success,Is.True);
                }
                Assert.That(s.View.Phase,Is.EqualTo(BattlePhase.Victory),"seed "+seed);
                Assert.That(s.View.ObjectiveProgress,Is.EqualTo(5));Assert.That(s.View.Enemies[0].Health,Is.GreaterThan(0));
                Assert.That(s.View.Tools[0].RemainingUses,Is.EqualTo(1));Assert.That(s.Execute(BattleCommand.Task()).Success,Is.False);
            }
        }
    }
}
