using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class VoyageMapTests
    {
        [TestCase(0u)][TestCase(42u)][TestCase(uint.MaxValue)]
        public void EveryGeneratedRouteHasExactlyElevenVisitedStops(uint seed)
        {
            foreach(var region in new[]{"fog","bones","inverted","fleet","abyss"})
            {
                var act=region=="fog"?1:region=="abyss"?3:2; var plan=VoyageMapGenerator.Generate(seed,act,region); var byId=plan.Stops.ToDictionary(x=>x.Id);
                Assert.That(plan.Stops.Count,Is.InRange(12,29)); Assert.That(plan.Stops.Count(x=>x.Layer==0||x.Layer==10),Is.EqualTo(2));
                var paths=new Stack<Tuple<string,int>>();paths.Push(Tuple.Create(plan.StartId,1));var count=0;
                while(paths.Count>0){var item=paths.Pop();if(item.Item1==plan.GoalId){Assert.That(item.Item2,Is.EqualTo(11));count++;continue;}foreach(var next in byId[item.Item1].Next)paths.Push(Tuple.Create(next,item.Item2+1));}
                Assert.That(count,Is.GreaterThan(1));
                Assert.That(byId[plan.GoalId].EncounterId,Is.EqualTo(VoyageMapGenerator.BossEncounter(seed,act,region)));
                Assert.That(plan.Stops.Where(x=>x.Kind==RunNodeKind.Battle).All(x=>x.EncounterId.StartsWith("voyage-"+region+"-",StringComparison.Ordinal)),Is.True);
                var battles=plan.Stops.Where(x=>x.Kind==RunNodeKind.Battle).GroupBy(x=>x.Layer).Select(x=>x.First().EncounterId).ToArray();
                Assert.That(battles,Has.Length.EqualTo(4)); Assert.That(battles,Is.Unique);
                Assert.That(plan.Stops.Count(x=>x.Kind==RunNodeKind.Reward),Is.EqualTo(plan.Stops.Count(x=>x.Layer==4)));
                Assert.That(plan.Stops.Count(x=>x.Kind==RunNodeKind.Boss),Is.EqualTo(1));
            }
        }

        [Test]
        public void EachActUsesItsOwnSeededBossPool()
        {
            var pools=new[]{new[]{"fog-visitors","storm-gauntlet","hollow-beacon"},new[]{"m02","m03","m04"},new[]{"m05","drowned-bell"}};
            var regions=new[]{"fog","bones","abyss"};
            for(var act=1;act<=3;act++)
            {
                var seen=new HashSet<string>();
                for(uint seed=0;seed<256;seed++)
                {
                    var boss=VoyageMapGenerator.BossEncounter(seed,act,regions[act-1]);
                    Assert.That(pools[act-1],Does.Contain(boss));
                    var plan=VoyageMapGenerator.Generate(seed,act,regions[act-1]);
                    Assert.That(plan.Stops.Single(s=>s.Id==plan.GoalId).EncounterId,Is.EqualTo(boss));
                    if(act==2)Assert.That(VoyageMapGenerator.BossEncounter(seed,2,"fleet"),Is.EqualTo(boss),"Region choice must not bind the boss.");
                    seen.Add(boss);
                }
                Assert.That(seen,Is.EquivalentTo(pools[act-1]));
            }
        }

        [Test]
        public void SeededRoutesAverageAtLeastOneBranchPerTwoVisitedStopsAndEveryStopIsViable()
        {
            long branchStops=0,visitedStops=0;
            foreach(var region in new[]{"fog","bones","inverted","fleet","abyss"})
            for(uint seed=0;seed<64;seed++)
            {
                var act=region=="fog"?1:region=="abyss"?3:2;
                var plan=VoyageMapGenerator.Generate(seed,act,region);
                var byId=plan.Stops.ToDictionary(x=>x.Id);
                var reachable=new HashSet<string>{plan.StartId};
                var pending=new Queue<string>();pending.Enqueue(plan.StartId);
                while(pending.Count>0)foreach(var next in byId[pending.Dequeue()].Next)if(reachable.Add(next))pending.Enqueue(next);
                Assert.That(reachable,Is.EquivalentTo(byId.Keys),"Every generated waypoint needs an inbound route from the start.");
                Assert.That(plan.Stops.Where(x=>x.Id!=plan.GoalId).All(x=>x.Next.Count>0),Is.True);
                Assert.That(plan.Stops.Where(x=>x.Layer==1).Count(),Is.EqualTo(2));
                Assert.That(Enumerable.Range(1,9).Select(layer=>plan.Stops.Count(x=>x.Layer==layer)),Has.All.InRange(1,3));

                var paths=new Stack<Tuple<string,int,int>>();paths.Push(Tuple.Create(plan.StartId,0,0));
                while(paths.Count>0)
                {
                    var path=paths.Pop();var stop=byId[path.Item1];
                    var branches=path.Item2+(stop.Next.Count>1?1:0);var visits=path.Item3+1;
                    if(stop.Id==plan.GoalId){branchStops+=branches;visitedStops+=visits;continue;}
                    foreach(var next in stop.Next)paths.Push(Tuple.Create(next,branches,visits));
                }
            }
            Assert.That(branchStops*2,Is.GreaterThanOrEqualTo(visitedStops),
                $"Visible branch density was {branchStops/(double)visitedStops:P1}; expected at least 50.0% across complete 11-stop routes.");
        }

        [Test]
        public void SameSeedIsDeterministicAndActsUseIndependentDomains()
        {
            Assert.That(Snapshot(VoyageMapGenerator.Generate(17,1,"fog")),Is.EqualTo(Snapshot(VoyageMapGenerator.Generate(17,1,"fog"))));
            Assert.That(Snapshot(VoyageMapGenerator.Generate(17,1,"fog")),Is.Not.EqualTo(Snapshot(VoyageMapGenerator.Generate(17,2,"bones"))));
        }

        [Test]
        public void FifthOrdinaryVariantIsReachableWithoutRepeatingAFightWithinAnAct()
        {
            var variants=Enumerable.Range(0,32).SelectMany(seed=>VoyageMapGenerator.Generate((uint)seed).Stops)
                .Where(x=>x.Kind==RunNodeKind.Battle).Select(x=>x.EncounterId).ToArray();
            Assert.That(variants,Does.Contain("voyage-fog-4"));
        }

        [Test]
        public void RestorePreservesStoredPresentationButRejectsMalformedMaps()
        {
            var data=VoyageMapGenerator.Generate(9).Export();data.Stops[1].Name="旧航名";
            Assert.That(VoyageMapGenerator.Restore(data).Stops[1].Name,Is.EqualTo("旧航名"));
            Corrupt(x=>x.Version++); Corrupt(x=>x.Act=3); Corrupt(x=>x.RegionId="abyss"); Corrupt(x=>x.StartId="missing");
            Corrupt(x=>x.Stops[0].X=float.NaN); Corrupt(x=>x.Stops[1].Layer=8); Corrupt(x=>x.Stops[1].Kind=RunNodeKind.Boss);
            Corrupt(x=>x.Stops[1].EncounterId="m05"); Corrupt(x=>x.Stops[1].BattleSeed++); Corrupt(x=>x.Stops[0].Next=new[]{"missing"});
            Corrupt(x=>x.Stops=Array.Empty<VoyageStopData>());
        }

        private static void Corrupt(Action<VoyageMapData> change){var data=VoyageMapGenerator.Generate(9).Export();change(data);Assert.Throws<ArgumentException>(()=>VoyageMapGenerator.Restore(data));}
        private static string Snapshot(VoyagePlan p)=>p.Act+":"+p.RegionId+":"+string.Join("|",p.Stops.Select(x=>x.Id+"/"+x.X+"/"+x.Y+"/"+x.Kind+"/"+x.EncounterId+">"+string.Join(",",x.Next)));
    }
}
