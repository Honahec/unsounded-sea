using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class VoyageTests
    {
        [Test] public void SynergyRewardsAreReachableAndForbiddenRecoveryStartsWithSecondAct()
        {
            var first = new System.Collections.Generic.HashSet<string>();
            var next = new System.Collections.Generic.HashSet<string>();
            for (uint seed = 0; seed < 100; seed++)
            {
                var s = VoyageSession.Create(seed);
                MoveToLayer(s, 4);
                foreach (var card in s.RewardCards) first.Add(card.Id);
                var damaged = true; CompleteAct(s, ref damaged);
                Assert.That(s.ChooseNextAct("bones"), Is.True);
                foreach (var card in s.RewardCards) next.Add(card.Id);
                Assert.That(VoyageSession.Restore(s.Export()).RewardCards.Select(c => c.Id), Is.EqualTo(s.RewardCards.Select(c => c.Id)));
            }
            Assert.That(first, Is.SupersetOf(new[] { "c016", "c026", "c032" }));
            Assert.That(first, Does.Not.Contain("c076")); Assert.That(first, Does.Not.Contain("c083"));
            Assert.That(next, Is.SupersetOf(new[] { "c016", "c026", "c032", "c076", "c083" }));
        }

        [Test]
        public void NewVoyageSupportsInitialCrewDeckToolsAndSafePreparation()
        {
            var s=VoyageSession.Create(1,"listener");
            Assert.That(s.CrewId,Is.EqualTo("listener"));Assert.That(s.Health,Is.EqualTo(60));Assert.That(s.Sanity,Is.EqualTo(30));Assert.That(s.Supplies,Is.EqualTo(3));
            Assert.That(s.Act,Is.EqualTo(1));Assert.That(s.RegionId,Is.EqualTo("fog"));Assert.That(s.VisitedThisAct,Is.EqualTo(1));Assert.That(s.CanEditDeck,Is.True);
            Assert.That(s.DeckIds,Has.Count.EqualTo(12));Assert.That(s.DeckIds,Is.Unique);Assert.That(s.LearnedToolIds,Is.EquivalentTo(new[]{"lantern","listener","shell"}));
            Assert.That(s.SetTools(new[]{"shell"}),Is.True);Assert.That(s.SetDeck(s.DeckIds.Reverse()),Is.True);
            Assert.That(s.SetDeck(s.DeckIds.Take(11)),Is.False);Assert.That(s.SetTools(new[]{"unknown"}),Is.False);
            Sail(s);Assert.That(s.CanEditDeck,Is.False);
        }

        [Test]
        public void EventsSupplyAndRewardsAreFiniteAndCannotBeSkippedOrReplayed()
        {
            var s=VoyageSession.Create(2); MoveToLayer(s,2);
            Assert.That(s.Phase,Is.EqualTo(VoyagePhase.Arrived));Assert.That(s.Continue(),Is.False);
            var id=s.GetNodeChoices().First(x=>x.Enabled).Id;Assert.That(s.ChooseNode(id),Is.True);Assert.That(s.ChooseNode(id),Is.False);Assert.That(s.Continue(),Is.True);
            MoveToLayer(s,4);Assert.That(s.Phase,Is.EqualTo(VoyagePhase.Reward));Assert.That(s.Continue(),Is.False);
            Assert.That(s.RewardCards.Count,Is.EqualTo(4));
            var reward=s.RewardCards[0].Id;Assert.That(s.ClaimReward(reward),Is.True);Assert.That(s.LearnedCardIds,Does.Contain(reward));Assert.That(s.ClaimReward(null),Is.False);
            MoveToLayer(s,5);var beforeHealth=s.Health;var beforeSanity=s.Sanity;Assert.That(s.ChooseNode("depart"),Is.True);Assert.That(s.ChooseNode("depart"),Is.False);
            Assert.That(s.Health,Is.EqualTo(beforeHealth));Assert.That(s.Sanity,Is.EqualTo(beforeSanity));Assert.That(s.CanEditDeck,Is.True);Assert.That(s.Continue(),Is.True);
        }

        [Test]
        public void ThreeActsHaveElevenStopsPersistentResourcesAndExplicitRegionChoice()
        {
            var s=VoyageSession.Create(33);var firstDamage=true;
            CompleteAct(s,ref firstDamage);Assert.That(s.Phase,Is.EqualTo(VoyagePhase.ActComplete));Assert.That(s.VisitedThisAct,Is.EqualTo(11));
            Assert.That(s.ChooseNextAct("abyss"),Is.False);Assert.That(s.ChooseNextAct("bones"),Is.True);
            Assert.That(s.Act,Is.EqualTo(1));Assert.That(s.Phase,Is.EqualTo(VoyagePhase.Reward));Assert.That(s.RewardCards.Count,Is.EqualTo(3));
            var restored=VoyageSession.Restore(s.Export());
            Assert.That(restored.RewardCards.Select(x=>x.Id),Is.EqualTo(s.RewardCards.Select(x=>x.Id)));
            Assert.That(s.ClaimReward(s.RewardCards[0].Id),Is.True);
            Assert.That(s.Act,Is.EqualTo(2));Assert.That(s.RegionId,Is.EqualTo("bones"));Assert.That(s.VisitedThisAct,Is.EqualTo(1));Assert.That(s.ChartUses,Is.EqualTo(2));
            CompleteAct(s,ref firstDamage);Assert.That(s.ChooseNextAct(null),Is.True);Assert.That(s.ClaimReward(null),Is.True);Assert.That(s.Act,Is.EqualTo(3));Assert.That(s.RegionId,Is.EqualTo("abyss"));
            CompleteAct(s,ref firstDamage);Assert.That(s.Phase,Is.EqualTo(VoyagePhase.Completed));Assert.That(s.VisitedThisAct,Is.EqualTo(11));Assert.That(s.Health,Is.EqualTo(41));Assert.That(s.Sanity,Is.EqualTo(19));
            Assert.That(s.RewardCards,Is.Empty);Assert.That(s.ClaimReward(null),Is.False);
        }

        [Test]
        public void DefeatAndResourceBoundsAreEnforced()
        {
            var s=VoyageSession.Create(4);MoveToLayer(s,1);Assert.That(s.BeginEncounter(),Is.True);
            Assert.That(s.ResolveEncounter(true,61,20),Is.False);Assert.That(s.ResolveEncounter(true,40,-1),Is.False);Assert.That(s.ResolveEncounter(false,12,3),Is.True);
            Assert.That(s.Phase,Is.EqualTo(VoyagePhase.Failed));Assert.That(s.Health,Is.EqualTo(12));Assert.That(s.ResolveEncounter(true,12,3),Is.False);
        }

        [Test]
        public void ReplayRestoresAllMutationsAndRejectsIllegalOrMalformedHistory()
        {
            var s=VoyageSession.Create(71,"listener");s.SetTools(new[]{"shell","lantern"});Sail(s);var checkpoint=s.Export();var restored=VoyageSession.Restore(checkpoint);
            Assert.That(restored.Phase,Is.EqualTo(VoyagePhase.Arrived));Assert.That(restored.Current.Id,Is.EqualTo(s.Current.Id));Assert.That(restored.ToolIds,Is.EqualTo(s.ToolIds));Assert.That(restored.KnownRoutes.Select(Key),Is.EqualTo(s.KnownRoutes.Select(Key)));
            var invalid=s.Export();invalid.History=invalid.History.Concat(new[]{new VoyageActionData{Kind=VoyageActionKind.Arrive}}).ToArray();Assert.Throws<ArgumentException>(()=>VoyageSession.Restore(invalid));
            invalid=s.Export();invalid.History[0].Kind=(VoyageActionKind)999;Assert.Throws<ArgumentException>(()=>VoyageSession.Restore(invalid));
            invalid=s.Export();invalid.CrewId="captain";Assert.Throws<ArgumentException>(()=>VoyageSession.Restore(invalid));
            invalid=s.Export();invalid.Version++;Assert.Throws<ArgumentException>(()=>VoyageSession.Restore(invalid));
        }

        [Test]
        public void CheckpointAcrossActTransitionReproducesCampaignExactly()
        {
            var s=VoyageSession.Create(99);var damaged=true;CompleteAct(s,ref damaged);Assert.That(s.ChooseNextAct("fleet"),Is.True);Assert.That(s.ClaimReward(null),Is.True);MoveToLayer(s,2);Assert.That(s.ChooseNode("leave"),Is.True);
            var restored=VoyageSession.Restore(s.Export());Assert.That(restored.Act,Is.EqualTo(2));Assert.That(restored.RegionId,Is.EqualTo("fleet"));Assert.That(restored.Current.Id,Is.EqualTo(s.Current.Id));
            Assert.That(restored.Health,Is.EqualTo(s.Health));Assert.That(restored.Sanity,Is.EqualTo(s.Sanity));Assert.That(restored.Supplies,Is.EqualTo(s.Supplies));Assert.That(restored.LearnedCardIds,Is.EqualTo(s.LearnedCardIds));Assert.That(restored.Phase,Is.EqualTo(s.Phase));
        }

        [Test]
        public void SelectingOriginalRouteAfterScoutingSurvivesRepeatedRestore()
        {
            var session = VoyageSession.Create(18);
            var first = session.Choices[0].Id;
            var other = session.Choices[1].Id;
            Assert.That(session.Select(first), Is.True);
            Assert.That(session.ScoutAhead(), Is.True);
            Assert.That(session.Select(other), Is.True);
            Assert.That(session.Select(first), Is.True);
            var restored = VoyageSession.Restore(session.Export());
            restored = VoyageSession.Restore(restored.Export());
            Assert.That(restored.Selected.Id, Is.EqualTo(first));
            Assert.That(restored.LookoutUses, Is.EqualTo(1));
            Assert.That(restored.SetSail(), Is.True);
            Assert.That(restored.Arrive(), Is.True);
            Assert.That(restored.Current.Id, Is.EqualTo(first));
        }

        [Test]
        public void ArchiveKnowledgeHasTruthfulEffectAndSurvivesRestore()
        {
            var routes=VoyageSession.Create(121);MoveToLayer(routes,7);
            var expected=routes.Export().Map.Stops.Single(x=>x.Id==routes.Current.Id).Next;
            Assert.That(routes.ChooseNode("routes"),Is.True);
            Assert.That(routes.Current.Next,Is.EquivalentTo(expected));
            Assert.That(routes.ChooseNode("routes"),Is.False);
            var restored=VoyageSession.Restore(routes.Export());
            Assert.That(restored.Knowledge,Is.EqualTo(routes.Knowledge));
            Assert.That(restored.KnownRoutes.Select(Key),Is.EqualTo(routes.KnownRoutes.Select(Key)));

            var briefing=VoyageSession.Create(122);MoveToLayer(briefing,7);
            Assert.That(briefing.ChooseNode("briefing"),Is.True);
            Assert.That(briefing.ChooseNode("routes"),Is.False);
            Assert.That(VoyageSession.Restore(briefing.Export()).Knowledge,Is.EqualTo(briefing.Knowledge));
        }

        [Test]
        public void DebugNodesAreIndependentPlayableAndCannotBeSaved()
        {
            var archive=VoyageSession.CreateDebug(3,"fleet",RunNodeKind.Archive);
            Assert.That(archive.IsDebug,Is.True);Assert.That(archive.Phase,Is.EqualTo(VoyagePhase.Arrived));
            Assert.That(archive.ChooseNode("briefing"),Is.True);Assert.That(archive.Continue(),Is.True);Assert.That(archive.Phase,Is.EqualTo(VoyagePhase.Completed));
            Assert.Throws<InvalidOperationException>(()=>archive.Export());

            var battle=VoyageSession.CreateDebug(4,"fog",RunNodeKind.Battle,"voyage-fog-4");
            Assert.That(battle.BeginEncounter(),Is.True);Assert.That(battle.ResolveEncounter(true,60,30),Is.True);
            Assert.That(battle.RewardCards.Count,Is.EqualTo(3));Assert.That(battle.ClaimReward(null),Is.True);Assert.That(battle.Phase,Is.EqualTo(VoyagePhase.Completed));
            var boss=VoyageSession.CreateDebug(5,"bones",RunNodeKind.Boss,"m02");
            Assert.That(boss.BeginEncounter(),Is.True);Assert.That(boss.ResolveEncounter(true,60,30),Is.True);Assert.That(boss.Phase,Is.EqualTo(VoyagePhase.Completed));
        }

        private static void CompleteAct(VoyageSession s,ref bool firstDamage)
        {
            while(s.Phase!=VoyagePhase.ActComplete&&s.Phase!=VoyagePhase.Completed)
            {
                if(s.Phase==VoyagePhase.ChoosingRoute)Sail(s);
                else if(s.Phase==VoyagePhase.Arrived)
                {
                    if(s.Current.Kind==RunNodeKind.Battle||s.Current.Kind==RunNodeKind.Boss){Assert.That(s.BeginEncounter(),Is.True);var h=firstDamage?41:s.Health;var sanity=firstDamage?19:s.Sanity;firstDamage=false;Assert.That(s.ResolveEncounter(true,h,sanity),Is.True);}
                    else {var choice=s.GetNodeChoices().FirstOrDefault(x=>x.Id=="depart"||x.Id=="leave")??s.GetNodeChoices().First(x=>x.Enabled);Assert.That(s.ChooseNode(choice.Id),Is.True);Assert.That(s.Continue(),Is.True);}
                }
                else if(s.Phase==VoyagePhase.Reward)Assert.That(s.ClaimReward(null),Is.True);
                else Assert.Fail("Unexpected phase "+s.Phase);
            }
        }
        private static void MoveToLayer(VoyageSession s,int layer)
        {
            while(s.Current.Layer<layer){if(s.Phase==VoyagePhase.ChoosingRoute)Sail(s);else if(s.Phase==VoyagePhase.Arrived){if(s.Current.Kind==RunNodeKind.Battle){s.BeginEncounter();s.ResolveEncounter(true,s.Health,s.Sanity);}else{var c=s.GetNodeChoices().First(x=>x.Enabled);s.ChooseNode(c.Id);s.Continue();}}else if(s.Phase==VoyagePhase.Reward)s.ClaimReward(null);}
        }
        private static void Sail(VoyageSession s){Assert.That(s.Select(s.Choices[0].Id),Is.True);Assert.That(s.SetSail(),Is.True);Assert.That(s.Arrive(),Is.True);}
        private static string Key(VoyageRoute r)=>r.FromId+">"+r.ToId;
    }
}
