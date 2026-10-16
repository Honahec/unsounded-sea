using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class VoyageMapTests
    {
        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(42u)]
        [TestCase(991u)]
        [TestCase(uint.MaxValue)]
        public void GeneratedMapsAreCompleteLocalLayeredRoutes(uint seed)
        {
            var plan = VoyageMapGenerator.Generate(seed);
            var stops = plan.Stops.ToDictionary(x => x.Id, StringComparer.Ordinal);
            Assert.That(plan.Stops.Count, Is.InRange(7, 14));
            Assert.That(plan.Stops.Count(x => x.Layer == 0), Is.EqualTo(1));
            Assert.That(plan.Stops.Count(x => x.Layer == 1), Is.EqualTo(2));
            Assert.That(plan.Stops.Count(x => x.Layer == 5), Is.EqualTo(1));
            Assert.That(plan.Stops.Select(x => x.Name), Is.Unique);
            Assert.That(stops[plan.StartId].Next.Count, Is.EqualTo(2));
            Assert.That(stops[plan.GoalId].Kind, Is.EqualTo(RunNodeKind.Boss));
            Assert.That(stops[plan.GoalId].EncounterId, Is.EqualTo("seal"));
            Assert.That(stops[plan.GoalId].Next, Is.Empty);

            var incoming = plan.Stops.ToDictionary(x => x.Id, x => 0, StringComparer.Ordinal);
            foreach (var stop in plan.Stops)
            {
                Assert.That(stop.X, Is.InRange(10f, 90f));
                Assert.That(stop.Y, Is.InRange(10f, 90f));
                var expectedDegree = stop.Layer >= 1 && stop.Layer <= 3
                    ? SampleDegree(seed, stop.Id) : stop.Layer == 0 ? 2 : stop.Layer == 4 ? 1 : 0;
                Assert.That(stop.Next.Count, Is.EqualTo(expectedDegree), stop.Id);
                foreach (var next in stop.Next)
                {
                    Assert.That(stops[next].Layer, Is.EqualTo(stop.Layer + 1));
                    Assert.That(stops[next].Y, Is.GreaterThan(stop.Y));
                    var dx = Math.Abs(stops[next].X - stop.X);
                    var dy = stops[next].Y - stop.Y;
                    Assert.That(dx, Is.LessThanOrEqualTo(34f));
                    Assert.That(Math.Sqrt(dx * dx + dy * dy), Is.LessThanOrEqualTo(40d));
                    incoming[next]++;
                }
            }
            Assert.That(plan.Stops.Where(x => x.Id != plan.StartId).All(x => incoming[x.Id] > 0), Is.True);
            Assert.That(plan.Stops.Any(x => x.Layer > 1 && x.Layer < 5 && incoming[x.Id] > 1), Is.True);
            for (var layer = 2; layer <= 4; layer++)
                Assert.That(plan.Stops.Count(x => x.Layer == layer),
                    Is.EqualTo(plan.Stops.Where(x => x.Layer == layer - 1).Max(x => x.Next.Count)));
            AssertEveryPathHasSixStops(plan, stops);
        }

        [Test]
        public void EligibleDegreesFollowIndependentProbabilityDistributionWithoutRepairs()
        {
            var degrees = new int[3, 4];
            const int seedCount = 2000;
            for (uint seed = 0; seed < seedCount; seed++)
            {
                var plan = VoyageMapGenerator.Generate(seed);
                foreach (var stop in plan.Stops.Where(x => x.Layer >= 1 && x.Layer <= 3))
                {
                    Assert.That(stop.Next.Count, Is.EqualTo(SampleDegree(seed, stop.Id)), stop.Id + " seed " + seed);
                    degrees[stop.Layer - 1, stop.Next.Count]++;
                }
            }

            var branching = 0;
            var triples = 0;
            for (var layer = 0; layer < 3; layer++)
            {
                var total = degrees[layer, 1] + degrees[layer, 2] + degrees[layer, 3];
                Assert.That((double)degrees[layer, 1] / total, Is.InRange(0.36d, 0.44d), "layer " + (layer + 1));
                Assert.That((double)degrees[layer, 2] / total, Is.InRange(0.38d, 0.46d), "layer " + (layer + 1));
                Assert.That((double)degrees[layer, 3] / total, Is.InRange(0.14d, 0.22d), "layer " + (layer + 1));
                branching += degrees[layer, 2] + degrees[layer, 3];
                triples += degrees[layer, 3];
            }
            Assert.That((double)triples / branching, Is.InRange(0.27d, 0.33d));
        }

        [Test]
        public void SameSeedAndVersionProduceIdenticalFullMap()
        {
            Assert.That(Snapshot(VoyageMapGenerator.Generate(908172u)), Is.EqualTo(Snapshot(VoyageMapGenerator.Generate(908172u))));
        }

        [Test]
        public void DifferentSeedsChangeTopologyLayoutContentAndBattleDomains()
        {
            var maps = new[] { 11u, 222u, 3333u, 44444u }.Select(VoyageMapGenerator.Generate).ToArray();
            Assert.That(maps.Select(Topology).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(maps.Select(x => string.Join("|", x.Stops.Select(s => s.Name + ":" + s.X + ":" + s.Y))).Distinct().Count(), Is.GreaterThan(1));
            Assert.That(maps.Select(x => string.Join("|", x.Stops.Select(s => s.BattleSeed))).Distinct().Count(), Is.EqualTo(maps.Length));
            Assert.That(VoyageSeeds.Derive(7, "battle", "l2n0"), Is.Not.EqualTo(VoyageSeeds.Derive(7, "visibility", "l2n0")));
        }

        [Test]
        public void RestoreUsesStoredFullMapAndExportIsDetached()
        {
            var original = VoyageMapGenerator.Generate(73).Export();
            original.Stops[1].Name = "存档中的旧地名";
            var restored = VoyageMapGenerator.Restore(original);
            Assert.That(restored.Stops.Single(x => x.Id == original.Stops[1].Id).Name, Is.EqualTo("存档中的旧地名"));

            var exported = restored.Export();
            exported.Stops[1].Name = "外部改写";
            exported.Stops[0].Next[0] = "l5n0";
            Assert.That(restored.Stops[1].Name, Is.EqualTo("存档中的旧地名"));
            Assert.That(restored.Stops[0].Next[0], Is.Not.EqualTo("l5n0"));
        }

        [Test]
        public void RestoreRejectsCorruptScalarAndContentFields()
        {
            Assert.Throws<ArgumentNullException>(() => VoyageMapGenerator.Restore(null));
            Corrupt(x => x.Version++);
            Corrupt(x => x.StartId = "missing");
            Corrupt(x => x.Stops[0].X = float.NaN);
            Corrupt(x => x.Stops[0].Y = float.PositiveInfinity);
            Corrupt(x => x.Stops[0].Layer = 9);
            Corrupt(x => x.Stops[0].Kind = (RunNodeKind)999);
            Corrupt(x => x.Stops[1].Name = " ");
            Corrupt(x => x.Stops[2].Name = x.Stops[1].Name);
            Corrupt(x => x.Stops[1].X = 91f);
            Corrupt(x => x.Stops[1].BattleSeed++);
            Corrupt(x => x.Stops[1].EncounterId = "seal");
            Corrupt(x => x.Stops[1].Id = "INVALID ID");
            Corrupt(x => x.Stops = new VoyageStopData[1000]);
        }

        [Test]
        public void RestoreRejectsCorruptGraphFields()
        {
            Corrupt(x => x.Stops[0].Next[0] = "missing");
            Corrupt(x => x.Stops[0].Next = new[] { x.Stops[0].Next[0], x.Stops[0].Next[0] });
            Corrupt(x => x.Stops[0].Next[0] = "l2n0");
            Corrupt(x => x.Stops[1].Next = new string[0]);
            Corrupt(x => x.Stops[0].Next = new[] { x.Stops[0].Next[0] });
            Corrupt(x =>
            {
                var target = x.Stops.First(s => s.Layer == 2);
                foreach (var stop in x.Stops.Where(s => s.Layer == 1))
                    stop.Next = stop.Next.Where(id => id != target.Id).ToArray();
            });
            Corrupt(x => x.Stops.First(s => s.Layer == 3).Next = new[] { x.GoalId });
            Corrupt(x => x.Stops.First(s => s.Layer == 1).X = 90f);
        }

        private static void Corrupt(Action<VoyageMapData> change)
        {
            var data = VoyageMapGenerator.Generate(991u).Export();
            change(data);
            Assert.Throws<ArgumentException>(() => VoyageMapGenerator.Restore(data));
        }

        private static void AssertEveryPathHasSixStops(VoyagePlan plan, IDictionary<string, VoyageStop> stops)
        {
            var stack = new Stack<Tuple<string, int>>();
            stack.Push(Tuple.Create(plan.StartId, 1));
            var paths = 0;
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (current.Item1 == plan.GoalId)
                {
                    Assert.That(current.Item2, Is.EqualTo(6));
                    paths++;
                    continue;
                }
                foreach (var next in stops[current.Item1].Next) stack.Push(Tuple.Create(next, current.Item2 + 1));
            }
            Assert.That(paths, Is.GreaterThan(1));
        }

        private static string Snapshot(VoyagePlan plan)
        {
            return plan.Version + ";" + plan.Seed + ";" + plan.StartId + ";" + plan.GoalId + ";" +
                string.Join("|", plan.Stops.Select(x => string.Join("~", x.Id, x.Layer, x.X, x.Y, x.Name, x.Clue,
                    x.Description, x.TravelHours, x.Kind, x.EncounterId, x.BattleSeed, string.Join(",", x.Next))));
        }

        private static int SampleDegree(uint seed, string id)
        {
            var roll = VoyageSeeds.Derive(seed, "topology", id) % 100u;
            return roll < 40u ? 1 : roll < 82u ? 2 : 3;
        }

        private static string Topology(VoyagePlan plan) => string.Join("|", plan.Stops.Select(x => x.Id + ">" + string.Join(",", x.Next)));
    }
}
