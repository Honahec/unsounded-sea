using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class VoyageRulesRegressionTests
    {
        [Test] public void SameLayerBattlesDifferAndUseValidDeterministicEncounters()
        {
            for(uint seed=0;seed<64;seed++)
            {
                var first=VoyageMapGenerator.Generate(seed);var second=VoyageMapGenerator.Generate(seed);
                Assert.That(first.Stops.Select(x=>x.EncounterId),Is.EqualTo(second.Stops.Select(x=>x.EncounterId)));
                foreach(var battle in first.Stops.Where(x=>x.Kind==RunNodeKind.Battle))
                {
                    Assert.That(Enumerable.Range(0,5).Select(i=>"voyage-fog-"+i),Does.Contain(battle.EncounterId));
                }
            }
        }

        [Test] public void ShopAndCrewCandidatesRefillAfterOwnedEntriesAreFiltered()
        {
            var session=VoyageSession.Create(42);var control=VoyageSession.Create(42);
            var starter=session.GetSupportChoices().First(x=>x.Id.StartsWith("starter:",StringComparison.Ordinal));
            var unique=starter.Id.Substring("starter:".Length);
            Assert.That(session.ManageSupport(starter.Id),Is.True);
            var crewSession=VoyageSession.Create(42);var initialCrew=crewSession.PreviewCrew(crewSession.Current.Id);
            Assert.That(crewSession.ManageSupport("hire:"+initialCrew[0].Id),Is.True);
            Move(session,5);Move(control,5);Move(crewSession,5);
            Assert.That(crewSession.PreviewCrew(crewSession.Current.Id).Count(),Is.EqualTo(3));
            Assert.That(crewSession.PreviewCrew(crewSession.Current.Id).Select(x=>x.Id),Does.Not.Contain(initialCrew[0].Id));
            Assert.That(session.ShopItems.Select(x=>x.Id),Does.Not.Contain(unique));
            var kind=SupportCatalog.Item(unique).Kind;
            Assert.That(session.ShopItems.Count(x=>x.Kind==kind),Is.EqualTo(control.ShopItems.Count(x=>x.Kind==kind)));
        }

        [Test] public void BossRewardPoolsExcludeMechanicDeadCards()
        {
            var drowned=RewardUnion("drowned-bell",3,"abyss");var storm=RewardUnion("storm-gauntlet");var hollow=RewardUnion("hollow-beacon");
            Assert.That(drowned,Does.Not.Contain("c046"));Assert.That(drowned,Does.Not.Contain("c061"));
            Assert.That(drowned,Does.Not.Contain("c062"));Assert.That(drowned,Does.Not.Contain("c078"));
            Assert.That(storm,Does.Not.Contain("c057"));Assert.That(storm,Does.Not.Contain("c065"));
            Assert.That(hollow,Does.Not.Contain("c058"));Assert.That(hollow,Does.Not.Contain("c064"));
            Assert.That(RewardUnion("m02",2,"bones"),Does.Contain("c062"));
            Assert.That(RewardUnion("m04",2,"bones"),Does.Contain("c065"));
            Assert.That(RewardUnion("fog-visitors"),Does.Contain("c058"));
        }

        [Test] public void SummaryUsesReplayableFactsAndDoesNotInventFatalSource()
        {
            var session=VoyageSession.Create(7);
            var item=session.ShopItems.First(x=>x.Kind==ItemKind.Consumable);
            Assert.That(session.ManageSupport("buy:"+item.Id),Is.True);
            Move(session,1);Assert.That(session.BeginEncounter(),Is.True);
            Assert.That(session.ResolveEncounter(false,12,9),Is.True);
            var summary=session.Summary;
            Assert.That(summary.Outcome,Is.EqualTo("远征失败"));
            Assert.That(summary.DeathCause,Is.EqualTo("战斗失利（未记录致命来源）"));
            Assert.That(summary.SpentSupplies,Is.EqualTo(1));
            Assert.That(summary.Route,Is.Not.Empty);Assert.That(summary.LearnedCards,Is.Not.Empty);
            var restored=VoyageSession.Restore(session.Export()).Summary;
            Assert.That(restored.SpentSupplies,Is.EqualTo(summary.SpentSupplies));
            Assert.That(restored.DeathCause,Is.EqualTo(summary.DeathCause));
        }


        private static IReadOnlyCollection<string> RewardUnion(string boss,int act=1,string region="fog")
        {
            var result=new HashSet<string>(StringComparer.Ordinal);var matches=0;
            for(uint seed=0;seed<4096&&matches<128;seed++)
            {
                if(VoyageMapGenerator.BossEncounter(seed,act,region)!=boss)continue;
                var session=VoyageSession.Create(seed);
                while(session.Act<act)
                {
                    Move(session,10);
                    Assert.That(session.BeginEncounter(),Is.True);
                    Assert.That(session.ResolveEncounter(true,session.Health,session.Sanity),Is.True);
                    Assert.That(session.ChooseNextAct(session.Act==1?"bones":"abyss"),Is.True);
                    Assert.That(session.ClaimReward(null),Is.True);
                }
                Move(session,4);
                foreach(var card in session.RewardCards)result.Add(card.Id);
                matches++;
            }
            Assert.That(matches,Is.EqualTo(128));
            return result;
        }

        private static void Move(VoyageSession session,int layer)
        {
            while(session.Current.Layer<layer)
            {
                if(session.Phase==VoyagePhase.ChoosingRoute){Assert.That(session.Select(session.Choices[0].Id));Assert.That(session.SetSail());Assert.That(session.Arrive());}
                else if(session.Phase==VoyagePhase.Arrived)
                {
                    if(session.Current.Kind==RunNodeKind.Battle){Assert.That(session.BeginEncounter());Assert.That(session.ResolveEncounter(true,session.Health,session.Sanity));}
                    else{var choices=session.GetNodeChoices();var choice=choices.FirstOrDefault(x=>x.Enabled&&(x.Id=="leave"||x.Id=="depart"))??choices.First(x=>x.Enabled);Assert.That(session.ChooseNode(choice.Id));Assert.That(session.Continue());}
                }
                else if(session.Phase==VoyagePhase.Reward)Assert.That(session.ClaimReward(null));
            }
        }
    }
}
