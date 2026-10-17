using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;
using Newtonsoft.Json;

namespace UnsoundedSea.Tests
{
    public sealed class VoyageSupportTests
    {
        private static void Move(VoyageSession s,int layer)
        {
            for(var step=0;step<100&&s.Current.Layer<layer;step++)
            {
                if(s.Phase==VoyagePhase.ChoosingRoute){Assert.That(s.Select(s.Choices[0].Id),Is.True);Assert.That(s.SetSail(),Is.True);Assert.That(s.Arrive(),Is.True);}
                else if(s.Phase==VoyagePhase.Reward)Assert.That(s.ClaimReward(null),Is.True);
                else if(s.Phase==VoyagePhase.Arrived)
                {
                    if(s.Current.Kind==RunNodeKind.Battle||s.Current.Kind==RunNodeKind.Boss){Assert.That(s.BeginEncounter(),Is.True);Assert.That(s.ResolveEncounter(true,50,25),Is.True);}
                    else{var choice=s.GetNodeChoices().FirstOrDefault(x=>x.Enabled&&(x.Id=="leave"||x.Id=="depart"))??s.GetNodeChoices().FirstOrDefault(x=>x.Enabled);if(choice!=null)Assert.That(s.ChooseNode(choice.Id),Is.True);Assert.That(s.Continue(),Is.True);}
                }
            }
            Assert.That(s.Current.Layer,Is.EqualTo(layer));
        }
        private static VoyageSession RoundTrip(VoyageSession s)=>VoyageSession.Restore(JsonConvert.DeserializeObject<VoyageCheckpoint>(JsonConvert.SerializeObject(s.Export())));
        [Test] public void InitialEquipmentChoiceIsExclusiveAndConfigurationSurvivesJsonReplay()
        {
            var s=VoyageSession.Create(1);Assert.That(s.ManageSupport("starter:i18"),Is.True);Assert.That(s.ManageSupport("starter:i19"),Is.False);
            Assert.That(s.Equipment,Is.EqualTo(new[]{"i18"}));Assert.That(s.OwnedEquipment,Is.EqualTo(new[]{"i18"}));
            Assert.That(s.ManageSupport("unpack:i10"),Is.True);Assert.That(s.ManageSupport("pack:i10"),Is.True);Assert.That(s.ManageSupport("pack:i10"),Is.False);
            var copy=RoundTrip(s);Assert.That(copy.Equipment,Is.EqualTo(s.Equipment));Assert.That(copy.CarriedItems,Is.EqualTo(s.CarriedItems));
        }
        [Test] public void InventoryDeductionIsAtomicAndBattleOutcomeCannotApplyTwice()
        {
            var s=VoyageSession.Create(2);Move(s,1);Assert.That(s.BeginEncounter(),Is.True);
            Assert.That(s.ManageSupport("unpack:i10"),Is.False);
            Assert.That(s.ResolveEncounter(true,45,23,new[]{"i10","i10"}),Is.False);Assert.That(s.Inventory.Count,Is.EqualTo(1));Assert.That(s.Phase,Is.EqualTo(VoyagePhase.Encounter));
            Assert.That(s.ResolveEncounter(true,45,23,new[]{"i10"},2),Is.True);Assert.That(s.Inventory,Is.Empty);Assert.That(s.CarriedItems,Is.Empty);Assert.That(s.Watched,Is.EqualTo(2));
            Assert.That(s.ResolveEncounter(true,45,23,new[]{"i10"}),Is.False);var copy=RoundTrip(s);Assert.That(copy.Inventory,Is.Empty);Assert.That(copy.Watched,Is.EqualTo(2));
        }
        [Test] public void ShopPurchaseAndHiringHaveIndependentOneTimeNodeBudgets()
        {
            var s=VoyageSession.Create(3);Move(s,5);
            var buy=s.GetSupportChoices().First(x=>x.Id.StartsWith("buy:")&&x.Enabled);
            Assert.That(s.ManageSupport(buy.Id),Is.True);Assert.That(s.ManageSupport(buy.Id),Is.False);
            var hire=s.GetSupportChoices().First(x=>x.Id=="hire:listener");Assert.That(hire.Enabled,Is.True);Assert.That(s.ManageSupport(hire.Id),Is.True);Assert.That(s.ManageSupport(hire.Id),Is.False);
            var copy=RoundTrip(s);Assert.That(copy.Inventory,Is.EqualTo(s.Inventory));Assert.That(copy.OwnedEquipment,Is.EqualTo(s.OwnedEquipment));Assert.That(copy.CrewIds,Is.EqualTo(s.CrewIds));Assert.That(copy.Supplies,Is.EqualTo(s.Supplies));
        }
        [Test] public void VoucherOnlyConsumesOnSuccessfulHiring()
        {
            var s=VoyageSession.Create(4);Move(s,2);Assert.That(s.ManageSupport("voucher"),Is.True);Assert.That(s.ManageSupport("voucher"),Is.False);Assert.That(s.ChooseNode("leave"),Is.False);
            Move(s,5);var before=s.Supplies;Assert.That(s.ManageSupport("hire:missing"),Is.False);Assert.That(s.Markers.Any(x=>x.Id=="docking-voucher"),Is.True);
            Assert.That(s.ManageSupport("hire:listener"),Is.True);Assert.That(s.Supplies,Is.EqualTo(before-1));Assert.That(s.Markers,Is.Empty);
        }
        [Test] public void BeaconExpiresAtNextBattleAndCannotBeClaimedAgain()
        {
            var s=VoyageSession.Create(5);Move(s,7);Assert.That(s.ManageSupport("survey"),Is.True);Assert.That(s.ManageSupport("survey"),Is.False);Assert.That(s.Loadout.SurveyedBeacon,Is.True);
            Assert.That(RoundTrip(s).Loadout.SurveyedBeacon,Is.True);Move(s,8);Assert.That(s.BeginEncounter(),Is.True);Assert.That(s.Loadout.SurveyedBeacon,Is.True);Assert.That(s.ResolveEncounter(true,50,25),Is.True);Assert.That(s.Markers.Any(x=>x.Id=="surveyed-beacon"),Is.False);
        }
        [Test] public void InjuryIsExplicitAndTreatmentDoesNotRequireMedicSupport()
        {
            var s=VoyageSession.Create(6);Move(s,2);Assert.That(s.ManageSupport("risk:medic"),Is.True);Assert.That(s.ActiveCrew("medic"),Is.False);Assert.That(s.CrewIds,Does.Contain("medic"));
            Move(s,5);var before=s.Supplies;Assert.That(s.ManageSupport("treat:medic"),Is.True);Assert.That(s.Supplies,Is.EqualTo(before-2));Assert.That(s.ActiveCrew("medic"),Is.True);Assert.That(s.ManageSupport("treat:medic"),Is.False);Assert.That(RoundTrip(s).InjuredCrew,Is.Empty);
        }
        [Test] public void RescueRequiresExplicitInvitationAndNoAutomaticFifthCrew()
        {
            var s=VoyageSession.Create(7);Move(s,2);var rescue=s.GetSupportChoices().First(x=>x.Id.StartsWith("rescue:")&&x.Enabled);Assert.That(s.ManageSupport(rescue.Id),Is.True);var rescued=s.EscortedCrew;
            Assert.That(s.CrewIds.Count,Is.EqualTo(1));Assert.That(s.ManageSupport("accept"),Is.True);Assert.That(s.CrewIds,Does.Contain(rescued));Assert.That(s.EscortedCrew,Is.Null);Assert.That(s.ManageSupport("accept"),Is.False);Assert.That(RoundTrip(s).CrewIds,Is.EqualTo(s.CrewIds));
        }
        [Test] public void TypedStableArchivePersistsButTurnObservationNeverBecomesRunKnowledge()
        {
            var s=VoyageSession.Create(8);Move(s,1);s.BeginEncounter();
            var stable=new Evidence("enemy/source","确认来源","实际核验",KnowledgeLevel.Verified,EvidenceScope.Encounter,"enemy:1","enemy","Source","enc",1,"source-v1","舷边",new[]{"a","b"});
            var temporary=new Evidence("enemy/1/VisibleAction","当前攻击","提灯",KnowledgeLevel.Observed,EvidenceScope.Turn,"enemy:1","enemy","VisibleAction","enc",1);
            Assert.That(s.ResolveEncounter(true,50,25,evidence:new[]{stable,temporary}),Is.True);Assert.That(s.Archive.Count,Is.EqualTo(1));Assert.That(s.Archive[0].Scope,Is.EqualTo(EvidenceScope.Run));Assert.That(s.Archive[0].SubjectId,Is.Empty);
            var copy=RoundTrip(s);Assert.That(copy.Archive[0].Rule,Is.EqualTo("source-v1"));Assert.That(copy.Archive[0].Fact,Is.EqualTo("舷边"));
        }
        [Test] public void SpecimenDeliveryIsMutuallyExclusiveAndConsumptive()
        {
            var s=VoyageSession.Create(9);Move(s,2);Assert.That(s.ManageSupport("specimen"),Is.True);Move(s,7);Assert.That(s.ManageSupport("specimen:hint"),Is.True);Assert.That(s.ManageSupport("specimen:supply"),Is.False);Assert.That(s.Markers.Any(x=>x.Id=="sealed-specimen"),Is.False);Assert.That(RoundTrip(s).Knowledge,Is.EqualTo(s.Knowledge));
        }
        [Test] public void IneligibleNewCardsAreNotOfferedBeforeTheirSystemsAreUsable()
        {
            for(uint seed=0;seed<30;seed++){var s=VoyageSession.Create(seed);Move(s,4);Assert.That(s.RewardCards.Select(c=>c.Id),Does.Not.Contain("c091"));Assert.That(s.RewardCards.Select(c=>c.Id),Does.Not.Contain("c096"));}
        }
        [Test] public void PreparationPreviewIsDeterministicAndMatchesStartServices()
        {
            var first=VoyageSession.Create(41);var second=VoyageSession.Create(41);
            Assert.That(first.PreparationBriefing,Is.EqualTo(second.PreparationBriefing));
            Assert.That(first.PreviewShopItems(first.Current.Id).Select(x=>x.Id),Is.EqualTo(first.ShopItems.Select(x=>x.Id)));
            Assert.That(first.PreviewCrew(first.Current.Id).Select(x=>x.Id),Is.EqualTo(second.PreviewCrew(second.Current.Id).Select(x=>x.Id)));
            Assert.Throws<ArgumentException>(()=>first.PreviewStop("a1l9n0"));
            var start=first.Current.Id;Move(first,1);
            Assert.That(first.PreviewShopItems(start),Is.Empty);
        }
        [Test] public void StartSupplyPurchaseAndRecruitRemainOptionalAndReplayExactly()
        {
            var s=VoyageSession.Create(42);var item=s.ShopItems.First(x=>x.Kind==ItemKind.Consumable);var supplies=s.Supplies;
            Assert.That(s.ManageSupport("buy:"+item.Id),Is.True);Assert.That(s.Supplies,Is.LessThan(supplies));
            var crew=s.PreviewCrew(s.Current.Id).First();Assert.That(s.ManageSupport("hire:"+crew.Id),Is.True);
            var copy=RoundTrip(s);Assert.That(copy.Inventory,Is.EqualTo(s.Inventory));Assert.That(copy.OwnedEquipment,Is.EqualTo(s.OwnedEquipment));Assert.That(copy.CrewIds,Is.EqualTo(s.CrewIds));
        }
        [Test] public void FinalSupplyGuaranteesActualRandomBossPreparationOutsideRegionPool()
        {
            VoyageSession s=null;
            for(uint seed=0;seed<200;seed++)if(VoyageMapGenerator.BossEncounter(seed,2,"bones")=="m03"){s=VoyageSession.Create(seed);break;}
            Assert.That(s,Is.Not.Null);Move(s,10);
            Assert.That(s.BeginEncounter(),Is.True);Assert.That(s.ResolveEncounter(true,50,25),Is.True);
            Assert.That(s.ChooseNextAct("bones"),Is.True);Assert.That(s.ClaimReward(null),Is.True);
            Move(s,9);
            var preview=s.PreviewShopItems(s.Current.Id).Select(x=>x.Id).ToArray();
            Assert.That(preview,Does.Contain("i06"));Assert.That(preview,Does.Contain("i16"));
            Assert.That(s.ShopItems.Select(x=>x.Id),Is.EqualTo(preview));
        }
        [Test] public void UniqueEquipmentRewardCannotBeClaimedTwiceAndSurvivesResume()
        {
            var s=VoyageSession.Create(44);Move(s,4);var item=s.RewardItems.Single();
            Assert.That(s.ClaimReward("item:"+item.Id),Is.True);Assert.That(s.OwnedEquipment,Does.Contain(item.Id));
            Assert.That(s.ClaimReward("item:"+item.Id),Is.False);
            Assert.That(RoundTrip(s).OwnedEquipment,Does.Contain(item.Id));
        }
    }
}
