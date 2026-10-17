using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class VoyageBalanceTests
    {
        [Test]
        public void RewardReplacementIsAtomicUniqueAndRestoresFromJsonHistory()
        {
            var session=VoyageSession.Create(12);
            MoveTo(session,4);
            var reward=session.RewardCards[0].Id;
            var replaced=session.DeckIds[0];
            Assert.That(session.ClaimReward(reward,replaced),Is.True);
            Assert.That(session.DeckIds,Has.Count.EqualTo(12));
            Assert.That(session.DeckIds,Is.Unique);
            Assert.That(session.DeckIds,Does.Contain(reward));
            Assert.That(session.DeckIds,Does.Not.Contain(replaced));
            Assert.That(session.LearnedCardIds,Does.Contain(replaced));
            var restored=VoyageSession.Restore(JsonConvert.DeserializeObject<VoyageCheckpoint>(JsonConvert.SerializeObject(session.Export())));
            Assert.That(restored.DeckIds,Is.EqualTo(session.DeckIds));
            Assert.That(restored.LearnedCardIds,Is.EqualTo(session.LearnedCardIds));

            var invalid=VoyageSession.Create(13);MoveTo(invalid,4);
            var beforeDeck=invalid.DeckIds.ToArray();var beforeLearned=invalid.LearnedCardIds.ToArray();
            Assert.That(invalid.ClaimReward(invalid.RewardCards[0].Id,"missing"),Is.False);
            Assert.That(invalid.ClaimReward(null,beforeDeck[0]),Is.False);
            Assert.That(invalid.ClaimReward("item:"+invalid.RewardItems[0].Id,beforeDeck[0]),Is.False);
            Assert.That(invalid.DeckIds,Is.EqualTo(beforeDeck));Assert.That(invalid.LearnedCardIds,Is.EqualTo(beforeLearned));
        }

        [Test]
        public void PerilousBattleGrantsTwoDistinctDeterministicPicksAndStrictReplay()
        {
            var session=VoyageSession.Create(21);
            MoveTo(session,6,true);
            Assert.That(session.Current.Perilous,Is.True);
            Assert.That(session.BeginEncounter());Assert.That(session.ResolveEncounter(true,session.Health,session.Sanity));
            Assert.That(session.RewardPicksRemaining,Is.EqualTo(2));
            var mid=VoyageSession.Restore(JsonConvert.DeserializeObject<VoyageCheckpoint>(JsonConvert.SerializeObject(session.Export())));
            Assert.That(mid.RewardPicksRemaining,Is.EqualTo(2));Assert.That(mid.RewardCards.Select(x=>x.Id),Is.EqualTo(session.RewardCards.Select(x=>x.Id)));
            var first=session.RewardCards.Select(x=>x.Id).ToArray();
            Assert.That(first,Has.Length.EqualTo(3));
            Assert.That(session.ClaimReward(first[0]),Is.True);
            Assert.That(session.RewardPicksRemaining,Is.EqualTo(1));
            var second=session.RewardCards.Select(x=>x.Id).ToArray();
            Assert.That(second,Has.Length.EqualTo(3));Assert.That(second,Does.Not.Contain(first[0]));
            Assert.That(session.ClaimReward("item:i17",session.DeckIds[0]),Is.False);Assert.That(session.RewardPicksRemaining,Is.EqualTo(1));
            Assert.That(session.ClaimReward(null),Is.True);Assert.That(session.RewardPicksRemaining,Is.Zero);
            var checkpoint=JsonConvert.DeserializeObject<VoyageCheckpoint>(JsonConvert.SerializeObject(session.Export()));
            Assert.That(VoyageSession.Restore(checkpoint).LearnedCardIds,Is.EqualTo(session.LearnedCardIds));
            checkpoint.History.First(x=>x.Kind==VoyageActionKind.ClaimReward).Values=new[]{"a","b"};
            Assert.Throws<ArgumentException>(()=>VoyageSession.Restore(checkpoint));
        }

        [Test]
        public void VersionNineMapsKeepEveryPathAtElevenStopsWithAlternatingBranches()
        {
            foreach(var region in new[]{"fog","bones","inverted","fleet","abyss"})
            for(uint seed=0;seed<100;seed++)
            {
                var act=region=="fog"?1:region=="abyss"?3:2;
                var plan=VoyageMapGenerator.Generate(seed,act,region);var byId=plan.Stops.ToDictionary(x=>x.Id);
                Assert.That(plan.Version,Is.EqualTo(9));Assert.That(plan.Stops.Count(x=>x.Layer==6&&x.Perilous),Is.EqualTo(1));
                foreach(var stop in plan.Stops.Where(x=>x.Layer==2||x.Layer==7))
                foreach(var next in stop.Next)
                {
                    Assert.That(byId[next].Layer,Is.EqualTo(stop.Layer+1));
                    Assert.That(new[]{stop.Kind,byId[next].Kind},Is.EquivalentTo(stop.Layer==2?new[]{RunNodeKind.Event,RunNodeKind.Battle}:new[]{RunNodeKind.Archive,RunNodeKind.Battle}));
                }
                var paths=new Stack<Tuple<string,int,int,int,int>>();paths.Push(Tuple.Create(plan.StartId,0,0,0,0));
                while(paths.Count>0)
                {
                    var path=paths.Pop();var stop=byId[path.Item1];
                    var visits=path.Item2+1;var battles=path.Item3+(stop.Kind==RunNodeKind.Battle?1:0);var events=path.Item4+(stop.Kind==RunNodeKind.Event?1:0);var archives=path.Item5+(stop.Kind==RunNodeKind.Archive?1:0);
                    if(stop.Id==plan.GoalId){Assert.That(visits,Is.EqualTo(11));Assert.That(battles,Is.EqualTo(4));Assert.That(events,Is.EqualTo(1));Assert.That(archives,Is.EqualTo(1));continue;}
                    foreach(var next in stop.Next){Assert.That(byId[next].Layer,Is.EqualTo(stop.Layer+1));paths.Push(Tuple.Create(next,visits,battles,events,archives));}
                }
                Assert.That(plan.Stops.Where(x=>x.Kind==RunNodeKind.Battle).All(x=>new[]{"断桅渔船","封舱驳船","无人信号艇","停摆引航船"}.Contains(x.Name)),Is.True);
                Assert.That(plan.Stops.Where(x=>x.Kind==RunNodeKind.Archive).All(x=>new[]{"漂浮测量站","潮流观测点","远海浮标"}.Contains(x.Name)),Is.True);
                Assert.That(plan.Stops.Where(x=>x.Kind==RunNodeKind.Supply).All(x=>x.Name=="补给母船"||x.Name=="漂浮修械船"),Is.True);
            }
            var old=VoyageMapGenerator.Generate(1).Export();old.Version=8;Assert.Throws<ArgumentException>(()=>VoyageMapGenerator.Restore(old));
        }

        [Test]
        public void EventTitlesCoverThreeDeterministicTemplatesPerRegion()
        {
            foreach(var region in new[]{"fog","bones","inverted","fleet","abyss"})
            {
                var titles=new HashSet<string>();
                for(uint seed=0;seed<100;seed++)titles.Add(VoyageSession.CreateDebug(seed,region,RunNodeKind.Event).EventTitle);
                Assert.That(titles,Has.Count.EqualTo(3));
                Assert.That(titles.All(x=>!string.IsNullOrWhiteSpace(x)),Is.True);
            }
        }

        [Test]
        public void NodeServicesAreSeparateAndDoNotSettleSupplyNode()
        {
            var session=VoyageSession.Create(42);
            Assert.That(session.GetNodeServices().Any(x=>x.Id.StartsWith("buy:",StringComparison.Ordinal)),Is.True);
            Assert.That(session.GetNodeServices().Any(x=>x.Id.StartsWith("hire:",StringComparison.Ordinal)),Is.True);
            Assert.That(session.GetSupportChoices().Any(x=>x.Id.StartsWith("buy:",StringComparison.Ordinal)||x.Id.StartsWith("hire:",StringComparison.Ordinal)),Is.False);
            var buy=session.GetNodeServices().First(x=>x.Id.StartsWith("buy:",StringComparison.Ordinal)&&x.Enabled);
            Assert.That(session.ManageSupport(buy.Id),Is.True);Assert.That(session.Phase,Is.EqualTo(VoyagePhase.ChoosingRoute));
            MoveTo(session,5);var service=session.GetNodeServices().FirstOrDefault(x=>x.Enabled);
            if(service!=null)Assert.That(session.ManageSupport(service.Id),Is.True);
            Assert.That(session.ChooseNode("depart"),Is.True);Assert.That(session.CanContinue,Is.True);Assert.That(session.GetNodeServices(),Is.Empty);
        }

        [Test]
        public void ReturnGazeServiceOnlyUnlocksEventResolution()
        {
            var session=VoyageSession.Create(77);
            MoveTo(session,1);Assert.That(session.BeginEncounter());Assert.That(session.ResolveEncounter(true,session.Health,session.Sanity,finalWatched:3));Assert.That(session.ClaimReward(null));
            if(session.Phase==VoyagePhase.ChoosingRoute)
            {
                var eventStop=session.Choices.First(x=>x.Kind==RunNodeKind.Event);
                Assert.That(session.Select(eventStop.Id));Assert.That(session.SetSail());Assert.That(session.Arrive());
            }
            Assert.That(session.GetNodeChoices(),Is.Empty);
            Assert.That(session.GetNodeServices().Select(x=>x.Id),Does.Contain("gaze:keep"));
            Assert.That(session.ManageSupport("gaze:keep"),Is.True);
            Assert.That(session.GetNodeChoices(),Is.Not.Empty);
            Assert.That(session.CanContinue,Is.False);
        }

        [Test]
        public void ShopsOnlyExposeCommonOrActualBossEquipment()
        {
            for(uint seed=0;seed<100;seed++)
            {
                var session=VoyageSession.Create(seed);var boss=VoyageMapGenerator.BossEncounter(seed,1,"fog");
                foreach(var item in session.ShopItems.Where(x=>x.BossIds.Count>0))Assert.That(item.BossIds.Any(id=>string.Equals(id,boss,StringComparison.OrdinalIgnoreCase)),Is.True);
            }
        }

        [Test] public void ActEndEquipmentUsesNextActualBossRatherThanPreviousAct()
        {
            for(uint seed=0;seed<24;seed++)
            {
                var session=VoyageSession.Create(seed);
                MoveTo(session,10);
                Assert.That(session.BeginEncounter(),Is.True);
                Assert.That(session.ResolveEncounter(true,session.Health,session.Sanity),Is.True);
                Assert.That(session.ChooseNextAct("bones"),Is.True);
                var nextBoss=VoyageMapGenerator.BossEncounter(seed,2,"bones");
                foreach(var item in session.RewardItems.Where(x=>x.BossIds.Count>0))
                    Assert.That(item.BossIds.Any(id=>string.Equals(id,nextBoss,StringComparison.OrdinalIgnoreCase)),Is.True);
            }
        }

        private static void MoveTo(VoyageSession session,int layer,bool perilous=false)
        {
            for(var guard=0;guard<100&&session.Current.Layer<layer;guard++)
            {
                if(session.Phase==VoyagePhase.ChoosingRoute)
                {
                    var choice=perilous&&session.Current.Layer==5?session.Choices.Single(x=>x.Perilous):session.Choices[0];
                    Assert.That(session.Select(choice.Id));Assert.That(session.SetSail());Assert.That(session.Arrive());
                }
                else if(session.Phase==VoyagePhase.Reward)Assert.That(session.ClaimReward(null));
                else if(session.Phase==VoyagePhase.Arrived)
                {
                    if(session.Current.Kind==RunNodeKind.Battle){Assert.That(session.BeginEncounter());Assert.That(session.ResolveEncounter(true,session.Health,session.Sanity));}
                    else{var choice=session.GetNodeChoices().First(x=>x.Enabled&&(x.Id=="leave"||x.Id=="depart"||x.Id=="briefing"));Assert.That(session.ChooseNode(choice.Id));Assert.That(session.Continue());}
                }
            }
            Assert.That(session.Current.Layer,Is.EqualTo(layer));
        }
    }
}
