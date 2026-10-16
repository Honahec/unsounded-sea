using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class VoyageTests
    {
        [Test]
        public void NewVoyageExposesTwoUsableChoicesWithoutHiddenGraphData()
        {
            var session = VoyageSession.Create(12345u);

            Assert.That(session.Seed, Is.EqualTo(12345u));
            Assert.That(session.Phase, Is.EqualTo(VoyagePhase.ChoosingRoute));
            Assert.That(session.Choices, Has.Count.EqualTo(2));
            Assert.That(session.Choices.All(x => session.KnownStops.Any(y => y.Id == x.Id)), Is.True);
            AssertKnowledgeSafe(session);
            Assert.That(session.Select(session.Choices[0].Id), Is.True);
            Assert.That(session.Current.Id, Is.Not.EqualTo(session.Selected.Id));
            Assert.That(session.Visited, Is.EqualTo(new[] { session.Current.Id }));
            Assert.That(session.ElapsedHours, Is.Zero);
        }

        [Test]
        public void SailingCommitsAndArrivesExactlyOnce()
        {
            var session = VoyageSession.Create(71u);
            var destination = session.Choices[0];

            Assert.That(session.Select(destination.Id), Is.True);
            Assert.That(session.SetSail(), Is.True);
            Assert.That(session.Phase, Is.EqualTo(VoyagePhase.Sailing));
            Assert.That(session.Selected.Id, Is.EqualTo(destination.Id));
            Assert.That(session.SetSail(), Is.False);
            Assert.That(session.Arrive(), Is.True);
            Assert.That(session.Arrive(), Is.False);
            Assert.That(session.Current.Id, Is.EqualTo(destination.Id));
            Assert.That(session.Selected, Is.Null);
            Assert.That(session.ElapsedHours, Is.EqualTo(destination.TravelHours));
            Assert.That(session.LegsSailed, Is.EqualTo(1));
        }

        [Test]
        public void EncounterBoundaryCannotBeBypassedOrCompletedTwice()
        {
            var session = VoyageSession.Create(9001u);
            SailUntilEncounter(session);

            Assert.That(session.Continue(), Is.False);
            Assert.That(session.BeginEncounter(), Is.True);
            Assert.That(session.Select(session.KnownStops[0].Id), Is.False);
            Assert.That(session.SetSail(), Is.False);
            Assert.That(session.BeginEncounter(), Is.False);
            Assert.That(session.ResolveEncounter(true), Is.True);
            Assert.That(session.ResolveEncounter(true), Is.False);
            Assert.That(session.Phase, Is.EqualTo(VoyagePhase.ChoosingRoute).Or.EqualTo(VoyagePhase.Completed));
        }

        [Test]
        public void DefeatTerminatesWithoutChangingTravelHistory()
        {
            var session = VoyageSession.Create(9001u);
            SailUntilEncounter(session);
            var visited = session.Visited.ToArray();

            Assert.That(session.BeginEncounter(), Is.True);
            Assert.That(session.ResolveEncounter(false), Is.True);
            Assert.That(session.Phase, Is.EqualTo(VoyagePhase.Failed));
            Assert.That(session.ResolveEncounter(true), Is.False);
            Assert.That(session.Visited, Is.EqualTo(visited));
        }

        [Test]
        public void LocalChartAndSelectedLookoutHaveSeparateScopesAndCharges()
        {
            var session = VoyageSession.Create(18u);
            var selected = session.Choices[0];
            var other = session.Choices[1];
            Assert.That(session.Select(selected.Id), Is.True);

            var topology = MapShape(session.Export().Map);
            var battleSeeds = BattleSeeds(session.Export().Map);
            Assert.That(session.ScoutAhead(), Is.True);
            Assert.That(session.LookoutUses, Is.EqualTo(1));
            Assert.That(session.ChartUses, Is.EqualTo(2));
            Assert.That(session.KnownRoutes.Any(x => x.FromId == selected.Id), Is.True);
            Assert.That(session.KnownRoutes.Any(x => x.FromId == other.Id), Is.False);
            Assert.That(session.ScoutAhead(), Is.False);
            Assert.That(session.LookoutUses, Is.EqualTo(1));

            Assert.That(MapShape(session.Export().Map), Is.EqualTo(topology));
            Assert.That(BattleSeeds(session.Export().Map), Is.EqualTo(battleSeeds));

            var local = CreateAtLocalFog();
            var localTopology = MapShape(local.Export().Map);
            var localBattleSeeds = BattleSeeds(local.Export().Map);
            var currentId = local.Current.Id;
            var before = new HashSet<string>(local.KnownRoutes.Select(RouteKey));
            Assert.That(local.RevealRoutes(), Is.True);
            var added = local.KnownRoutes.Where(x => !before.Contains(RouteKey(x))).ToArray();
            Assert.That(added, Is.Not.Empty);
            Assert.That(added.All(x => x.FromId == currentId), Is.True);
            Assert.That(local.ChartUses, Is.EqualTo(1));
            Assert.That(local.LookoutUses, Is.EqualTo(2));
            Assert.That(local.RevealRoutes(), Is.False);
            Assert.That(local.ChartUses, Is.EqualTo(1));
            Assert.That(MapShape(local.Export().Map), Is.EqualTo(localTopology));
            Assert.That(BattleSeeds(local.Export().Map), Is.EqualTo(localBattleSeeds));
        }

        [Test]
        public void CheckpointRestoresPendingPhaseKnowledgeLogsAndVoyageToolUses()
        {
            var original = VoyageSession.Create(314159u);
            var destination = original.Choices[0];
            original.Select(destination.Id);
            Assert.That(original.ScoutAhead(), Is.True);
            original.SetSail();
            var checkpoint = original.Export();
            var restored = VoyageSession.Restore(checkpoint);

            Assert.That(restored.Phase, Is.EqualTo(VoyagePhase.Sailing));
            Assert.That(restored.Current.Id, Is.EqualTo(original.Current.Id));
            Assert.That(restored.Selected.Id, Is.EqualTo(destination.Id));
            Assert.That(restored.ElapsedHours, Is.Zero);
            Assert.That(restored.Visited, Is.EqualTo(original.Visited));
            Assert.That(restored.ChartUses, Is.EqualTo(original.ChartUses));
            Assert.That(restored.LookoutUses, Is.EqualTo(original.LookoutUses));
            Assert.That(restored.KnownStops.Select(x => x.Id), Is.EqualTo(original.KnownStops.Select(x => x.Id)));
            Assert.That(restored.KnownRoutes.Select(RouteKey), Is.EqualTo(original.KnownRoutes.Select(RouteKey)));
            Assert.That(restored.Log, Is.EqualTo(original.Log));
            Assert.That(restored.Arrive(), Is.True);
            Assert.That(restored.ElapsedHours, Is.EqualTo(destination.TravelHours));
            Assert.That(restored.LookoutUses, Is.EqualTo(1));
            Assert.That(restored.Arrive(), Is.False);
        }

        [Test]
        public void RestoreKeepsArrivalAndEncounterBoundariesExact()
        {
            var arrived = VoyageSession.Create(9001u);
            SailUntilEncounter(arrived);
            var restoredArrival = VoyageSession.Restore(arrived.Export());
            Assert.That(restoredArrival.Phase, Is.EqualTo(VoyagePhase.Arrived));
            Assert.That(restoredArrival.Arrive(), Is.False);
            Assert.That(restoredArrival.BeginEncounter(), Is.True);

            var restoredEncounter = VoyageSession.Restore(restoredArrival.Export());
            Assert.That(restoredEncounter.Phase, Is.EqualTo(VoyagePhase.Encounter));
            Assert.That(restoredEncounter.BeginEncounter(), Is.False);
            Assert.That(restoredEncounter.ResolveEncounter(true), Is.True);
            Assert.That(restoredEncounter.Phase, Is.EqualTo(VoyagePhase.ChoosingRoute).Or.EqualTo(VoyagePhase.Completed));
        }

        [Test]
        public void FailedTransitionsDoNotEnterHistoryOrMutateState()
        {
            var session = VoyageSession.Create(42u);
            var before = session.Export();

            Assert.That(session.Arrive(), Is.False);
            Assert.That(session.Continue(), Is.False);
            Assert.That(session.BeginEncounter(), Is.False);
            Assert.That(session.ResolveEncounter(true), Is.False);
            Assert.That(session.RevealRoutes(), Is.False);

            var after = session.Export();
            Assert.That(after.History, Has.Length.EqualTo(before.History.Length));
            Assert.That(session.Phase, Is.EqualTo(VoyagePhase.ChoosingRoute));
            Assert.That(session.ElapsedHours, Is.Zero);
            Assert.That(session.ChartUses, Is.EqualTo(2));
            Assert.That(session.LookoutUses, Is.EqualTo(2));
        }

        [Test]
        public void RestoreRejectsBadVersionMapAndOperations()
        {
            var valid = VoyageSession.Create(5u).Export();
            Assert.Throws<ArgumentException>(() => VoyageSession.Restore(new VoyageCheckpoint
            {
                Version = 99, Map = valid.Map, History = Array.Empty<VoyageActionData>()
            }));
            Assert.Throws<ArgumentException>(() => VoyageSession.Restore(new VoyageCheckpoint
            {
                Version = VoyageSession.CheckpointVersion, Map = null, History = Array.Empty<VoyageActionData>()
            }));
            var corruptMap = VoyageSession.Create(5u).Export();
            corruptMap.Map.Version = 99;
            Assert.Throws<ArgumentException>(() => VoyageSession.Restore(corruptMap));
            Assert.Throws<ArgumentException>(() => VoyageSession.Restore(new VoyageCheckpoint
            {
                Version = VoyageSession.CheckpointVersion, Map = valid.Map, History = new[]
                {
                    new VoyageActionData { Kind = VoyageActionKind.Arrive }
                }
            }));
            Assert.Throws<ArgumentException>(() => VoyageSession.Restore(new VoyageCheckpoint
            {
                Version = VoyageSession.CheckpointVersion, Map = valid.Map, History = new[]
                {
                    new VoyageActionData { Kind = (VoyageActionKind)999 }
                }
            }));
        }


        [Test]
        public void RepeatedRoutePreviewsRemainRestorableWithoutConsumingTravel()
        {
            var session = VoyageSession.Create(17u);
            var choices = session.Choices;
            for (var i = 0; i < 1024; i++) session.Select(choices[i % choices.Count].Id);
            var restored = VoyageSession.Restore(session.Export());
            Assert.That(restored.Selected.Id, Is.EqualTo(session.Selected.Id));
            Assert.That(restored.Visited, Is.EqualTo(session.Visited));
            Assert.That(restored.ElapsedHours, Is.Zero);
            Assert.That(restored.SetSail(), Is.True);
            Assert.That(restored.Arrive(), Is.True);
            Assert.That(restored.Current.Id, Is.EqualTo(session.Selected.Id));
        }

        private static void SailTo(VoyageSession session, string id)
        {
            Assert.That(session.Select(id), Is.True);
            Assert.That(session.SetSail(), Is.True);
            Assert.That(session.Arrive(), Is.True);
        }

        private static void SailUntilEncounter(VoyageSession session)
        {
            while (session.Phase == VoyagePhase.ChoosingRoute)
            {
                var next = session.Choices.FirstOrDefault(IsEncounter) ?? session.Choices[0];
                SailTo(session, next.Id);
                if (IsEncounter(session.Current)) return;
                Assert.That(session.Continue(), Is.True);
            }
            Assert.Fail("Generated route did not reach an encounter.");
        }

        private static VoyageSession CreateAtLocalFog()
        {
            for (uint seed = 1; seed <= 100; seed++)
            {
                var initial = VoyageSession.Create(seed);
                foreach (var id in initial.Choices.Select(x => x.Id))
                {
                    var candidate = VoyageSession.Create(seed);
                    SailTo(candidate, id);
                    FinishArrival(candidate);
                    if (candidate.CanRevealRoutes) return candidate;
                }
            }
            Assert.Fail("Representative generated voyages contained no local fog.");
            return null;
        }

        private static void FinishArrival(VoyageSession session)
        {
            if (IsEncounter(session.Current))
            {
                Assert.That(session.BeginEncounter(), Is.True);
                Assert.That(session.ResolveEncounter(true), Is.True);
            }
            else
            {
                Assert.That(session.Continue(), Is.True);
            }
        }

        private static void AssertKnowledgeSafe(VoyageSession session)
        {
            var routes = new HashSet<string>(session.KnownRoutes.Select(RouteKey));
            foreach (var stop in session.KnownStops)
                Assert.That(stop.Next.All(target => routes.Contains(stop.Id + ">" + target)), Is.True);
        }

        private static bool IsEncounter(VoyageStop stop) => stop.Kind == RunNodeKind.Battle || stop.Kind == RunNodeKind.Boss;
        private static string RouteKey(VoyageRoute route) => route.FromId + ">" + route.ToId;
        private static string MapShape(VoyageMapData map) => string.Join("|", map.Stops.Select(x => x.Id + ":" + string.Join(",", x.Next)));
        private static string BattleSeeds(VoyageMapData map) => string.Join("|", map.Stops.Select(x => x.Id + ":" + x.BattleSeed));
    }
}
