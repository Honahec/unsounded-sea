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
                Assert.That(byId[plan.GoalId].EncounterId,Is.EqualTo(act==1?"fog-visitors":act==3?"m05":region=="bones"?"m02":region=="inverted"?"m03":"m04"));
                Assert.That(plan.Stops.Where(x=>x.Kind==RunNodeKind.Battle).All(x=>x.EncounterId.StartsWith("voyage-"+region+"-",StringComparison.Ordinal)),Is.True);
            }
        }

        [Test]
        public void SameSeedIsDeterministicAndActsUseIndependentDomains()
        {
            Assert.That(Snapshot(VoyageMapGenerator.Generate(17,1,"fog")),Is.EqualTo(Snapshot(VoyageMapGenerator.Generate(17,1,"fog"))));
            Assert.That(Snapshot(VoyageMapGenerator.Generate(17,1,"fog")),Is.Not.EqualTo(Snapshot(VoyageMapGenerator.Generate(17,2,"bones"))));
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
