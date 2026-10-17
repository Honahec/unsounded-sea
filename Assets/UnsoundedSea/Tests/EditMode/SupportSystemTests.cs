using System;
using System.Linq;
using NUnit.Framework;
using UnsoundedSea.Core;

namespace UnsoundedSea.Tests
{
    public sealed class SupportSystemTests
    {
        private static BattleDefinition Definition(string[] cards,string[] items=null,string[] equipment=null,CrewSupportSpec[] crew=null,bool known=false,Evidence[] archive=null,bool beacon=false,int draw=0) =>
            new BattleDefinition("support-tests",cards.Select(CardCatalog.Get),new[]{new EnemySpec("enemy:1","敌人",120,"攻击 / 低语",known
                ? new[]{new IntentSpec("hit","攻击","举手",DamageChannel.Sanity,5,true,true,"舷边")}
                : new[]{new IntentSpec("hit","攻击","举手",DamageChannel.Sanity,5,false,true,"舷边"),new IntentSpec("whisper","低语","俯身",DamageChannel.Sanity,5,false,true,"舷边")})},
                new[]{SupportCatalog.Tool("lantern"),SupportCatalog.Tool("listener")},actions:8,drawCount:draw==0?cards.Length:draw,supports:crew,archive:archive,initialHealth:40,loadout:new BattleLoadout(items,equipment,beacon));
        private static void Run(BattleSession s,BattleCommand c){var r=s.Execute(c);Assert.That(r.Success,Is.True,r.Error);}
        private static void Play(BattleSession s,string id,string target=null)=>Run(s,BattleCommand.Play(s.View.Hand.First(c=>c.Definition.Id==id).InstanceId,target));
        private static BattleSession Boss(BossEncounterKind kind,string[] cards,string[] tools=null,string[] items=null,string[] equipment=null)
        {
            var ids=kind==BossEncounterKind.Surveyor?new[]{"b03-surveyor","b03-moth-1","b03-moth-2"}:kind==BossEncounterKind.Watcher?new[]{"b05-watcher","b05-guard-anchor","b05-guard-lamp","b05-guard-bell","b05-guard-sail","b05-guard-eye"}:new[]{"b04-flag-bearer","b04-flag-soldier"};
            var enemies=ids.Select(id=>new EnemySpec(id,id,20,"攻击",new[]{new IntentSpec("hit","攻击","举手",DamageChannel.Health,3,true,true)}));
            return new BattleSession(new BattleDefinition("support-boss",cards.Select(CardCatalog.Get),enemies,(tools??Array.Empty<string>()).Select(SupportCatalog.Tool),actions:10,drawCount:cards.Length,loadout:new BattleLoadout(items,equipment)),2,boss:kind);
        }
        [Test] public void LoadoutRejectsThreeConsumablesDuplicateEquipmentAndTwoEngines()
        {
            Assert.Throws<ArgumentException>(()=>new BattleLoadout(new[]{"i10","i11","i13"}));
            Assert.Throws<ArgumentException>(()=>new BattleLoadout(equipment:new[]{"i17","i17"}));
            Assert.Throws<ArgumentException>(()=>new BattleLoadout(equipment:new[]{"i17","i22"}));
            Assert.Throws<ArgumentException>(()=>Definition(new[]{"cover"},new[]{"i09"}));
            Assert.Throws<ArgumentException>(()=>new BattleLoadout(new[]{"i12"}));
        }
        [Test] public void MedicineConsumesExactlyOnceAndReplayPreservesInventoryReceipt()
        {
            var d=Definition(new[]{"cover"},new[]{"i10","i10"});var s=new BattleSession(d,1);
            Run(s,BattleCommand.Consume("i10"));Run(s,BattleCommand.Consume("i10"));
            Assert.That(s.View.Health,Is.EqualTo(56));Assert.That(s.View.Actions,Is.EqualTo(6));
            Assert.That(s.Execute(BattleCommand.Consume("i10")).Success,Is.False);
            Assert.That(s.View.SpentConsumables.Count,Is.EqualTo(2));
            var copy=BattleReplay.Capture(s).Restore(d);Assert.That(copy.View.Log,Is.EqualTo(s.View.Log));Assert.That(copy.View.SpentConsumables,Is.EqualTo(s.View.SpentConsumables));
        }
        [Test] public void InvalidMagazineTargetIsAtomicAndEmergencyCardUsesActualConsumption()
        {
            var s=new BattleSession(Definition(new[]{"revolver","c095"},new[]{"i13"}),3);
            Assert.That(s.Execute(BattleCommand.Consume("i13","discard:999")).Success,Is.False);Assert.That(s.Commands,Is.Empty);
            Play(s,"revolver","enemy:enemy:1");var option=s.View.SupplyActions.Single().Targets.Single();Run(s,BattleCommand.Consume("i13",option.Id));
            Assert.That(s.View.Hand.Any(c=>c.Definition.Id=="revolver"),Is.True);
            Play(s,"c095");Assert.That(s.View.Block,Is.EqualTo(8));Assert.That(s.View.ExhaustCount,Is.EqualTo(1));
        }
        [Test] public void SaltAndEarmuffsDoNotPayActiveSanityCosts()
        {
            var s=new BattleSession(Definition(new[]{"c076"},new[]{"i11"},new[]{"i20","i23"}),3);
            Run(s,BattleCommand.Consume("i11"));Play(s,"c076","enemy:enemy:1");
            Assert.That(s.View.Sanity,Is.EqualTo(28));Assert.That(s.View.Ward,Is.EqualTo(6));Assert.That(s.View.Block,Is.EqualTo(5));
            Run(s,BattleCommand.EndTurn());Assert.That(s.View.Sanity,Is.EqualTo(28));
            Run(s,BattleCommand.EndTurn());Assert.That(s.View.Sanity,Is.EqualTo(23));
        }
        [Test] public void ToolReviewNeedsSuccessfulNewToolAnswerAndIsCappedAcrossCopies()
        {
            var s=new BattleSession(Definition(new[]{"cover","c094","c094","revolver"}),0);
            Play(s,"cover");Play(s,"revolver","enemy:enemy:1");Run(s,BattleCommand.Investigate("listener","enemy:1"));
            Play(s,"c094");Assert.That(s.View.Hand.Count,Is.EqualTo(3));
            Play(s,"c094");Assert.That(s.View.Hand.Count,Is.EqualTo(3));
            Assert.That(s.Execute(BattleCommand.Investigate("listener","enemy:1")).Success,Is.False);
        }
        [Test] public void DualQuestionWeaponExcludesToolsAndRewardsTwoDistinctCardQuestions()
        {
            var s=new BattleSession(Definition(new[]{"light","c041","c092"}),0);
            Play(s,"light","enemy:enemy:1");Play(s,"c041","enemy:enemy:1");Play(s,"c092","enemy:enemy:1");Assert.That(s.View.Enemies[0].Health,Is.EqualTo(102));
            s=new BattleSession(Definition(new[]{"c092"}),0);Run(s,BattleCommand.Investigate("lantern","enemy:1"));Run(s,BattleCommand.Investigate("listener","enemy:1"));Play(s,"c092","enemy:enemy:1");Assert.That(s.View.Enemies[0].Health,Is.EqualTo(116));
        }
        [Test] public void CrewIsOncePerTurnOncePerBattleAndInjuryDisablesIt()
        {
            var crew=new[]{SupportCatalog.CrewMember("medic"),SupportCatalog.CrewMember("cook"),new CrewSupportSpec("engineer","轮机长",CrewSupportKind.Engineer,true)};
            var s=new BattleSession(Definition(new[]{"c093","c014"},crew:crew),0);
            Assert.That(s.Execute(BattleCommand.Support("engineer")).Success,Is.False);
            Run(s,BattleCommand.Support("medic"));Assert.That(s.View.Health,Is.EqualTo(43));Assert.That(s.View.Block,Is.EqualTo(3));
            Assert.That(s.Execute(BattleCommand.Support("cook")).Success,Is.False);
            Play(s,"c093");Assert.That(s.View.PendingChoice,Is.Not.Null);Assert.That(s.Execute(BattleCommand.EndTurn()).Success,Is.False);
            Run(s,BattleCommand.Reorder(s.View.PendingChoice.Options.First(x=>x.Id.StartsWith("hand:")).Id));
            Run(s,BattleCommand.EndTurn());Assert.That(s.Execute(BattleCommand.Support("medic")).Success,Is.False);Run(s,BattleCommand.Support("cook"));Assert.That(s.View.Ward,Is.EqualTo(7));
        }
        [TestCase("engineer",11,0)] [TestCase("navigator",5,0)] [TestCase("diver",6,0)] [TestCase("cook",0,7)] [TestCase("decoder",0,7)]
        public void SupportCatalogEffectsAreReachable(string id,int block,int ward)
        {var s=new BattleSession(Definition(new[]{"cover"},crew:new[]{SupportCatalog.CrewMember(id)}),0);Run(s,BattleCommand.Support(id));Assert.That(s.View.Block,Is.EqualTo(block));Assert.That(s.View.Ward,Is.EqualTo(ward));}
        [Test] public void GunsmithCannotRecoverForbiddenOrExhaustedCards()
        {
            var s=new BattleSession(Definition(new[]{"revolver","c076","c095"},crew:new[]{SupportCatalog.CrewMember("gunsmith")}),0);
            Play(s,"revolver","enemy:enemy:1");Play(s,"c076","enemy:enemy:1");Play(s,"c095");
            Assert.That(s.View.Supports[0].Targets.Count,Is.EqualTo(1));Run(s,BattleCommand.Support("gunsmith",s.View.Supports[0].Targets.Single().Id));Assert.That(s.View.Hand.Single().Definition.Id,Is.EqualTo("revolver"));
        }
        [Test] public void EquipmentDrawsAreBoundedAndPublicIntentsDoNotTriggerWatch()
        {
            var s=new BattleSession(Definition(new[]{"cover","light","c041"},equipment:new[]{"i17"}),0);
            Play(s,"cover");Run(s,BattleCommand.Investigate("lantern","enemy:1"));Assert.That(s.View.Hand.Count,Is.EqualTo(3));
            Play(s,"light","enemy:enemy:1");Assert.That(s.View.Log.Count(x=>x.Contains("盐蚀怀表触发")),Is.EqualTo(1));
            var known=new BattleSession(Definition(new[]{"light"},equipment:new[]{"i17"},known:true),0);Play(known,"light","enemy:enemy:1");Assert.That(known.View.Log.Any(x=>x.Contains("盐蚀怀表触发")),Is.False);
        }
        [Test] public void BinderDoesNotTriggerOnFreeNotesAndNeverRecurses()
        {
            var s=new BattleSession(Definition(new[]{"cover","notes","c041","revolver"},equipment:new[]{"i22"}),0);
            Play(s,"cover");Play(s,"notes","enemy:enemy:1");Assert.That(s.View.Log.Any(x=>x.Contains("分栏记录夹触发")),Is.False);
            Play(s,"revolver","enemy:enemy:1");Play(s,"c041","enemy:enemy:1");Assert.That(s.View.Log.Count(x=>x.Contains("分栏记录夹触发")),Is.EqualTo(1));
        }
        [Test] public void HolsterOnlyBoostsDifferentWeaponAndCompassNeedsDefenseBeforeScene()
        {
            var s=new BattleSession(Definition(new[]{"revolver","c016"},equipment:new[]{"i18"}),0);Play(s,"revolver","enemy:enemy:1");Play(s,"c016","enemy:enemy:1");Assert.That(s.View.Enemies[0].Health,Is.EqualTo(104));
            var boss=Boss(BossEncounterKind.FlagBearer,new[]{"cover"},equipment:new[]{"i19"});Play(boss,"cover");var scene=boss.View.Hand.First(x=>x.IsEncounter&&x.Enabled);Run(boss,BattleCommand.Play(scene.InstanceId,scene.Targets.First(x=>x.Enabled).Id));Assert.That(boss.View.Block,Is.EqualTo(14));
        }
        [Test] public void ProbeCannotReadSameCellTwiceAndLeadSealRequiresPublicProof()
        {
            var s=Boss(BossEncounterKind.Surveyor,new[]{"cover"},new[]{"i06"},new[]{"i16"},new[]{"i24"});
            Assert.That(s.Execute(BattleCommand.Consume("i16","scene:A4")).Success,Is.False);Assert.That(s.View.SpentConsumables,Is.Empty);
            var probe=s.View.SupplyActions.Single(x=>x.Id=="i06");var target=probe.Targets.First().Id;Run(s,BattleCommand.Investigate("i06",target));Assert.That(s.Execute(BattleCommand.Investigate("i06",target)).Success,Is.False);
            var seal=s.View.SupplyActions.Single(x=>x.Id=="i16");Run(s,BattleCommand.Consume("i16",seal.Targets.First().Id));Assert.That(s.View.Ward,Is.EqualTo(4));
        }
        [Test] public void PliersConsumeSharedRelayQuota()
        {
            var s=Boss(BossEncounterKind.FlagBearer,new[]{"cover"},new[]{"i08"});var tool=s.View.SupplyActions.Single();Run(s,BattleCommand.Investigate("i08",tool.Targets.First().Id));Assert.That(s.View.Block,Is.EqualTo(4));Assert.That(s.View.Hand.Where(x=>x.IsEncounter&&x.Definition.Id=="E04-01").All(x=>!x.Enabled),Is.True);
        }
        [Test] public void BeaconHasExplicitOptionalConsumptionAndCannotBeSpentTwice()
        {
            var d=Definition(new[]{"c096","c096"},beacon:true);var s=new BattleSession(d,0);
            Play(s,"c096","marker:keep");Assert.That(s.View.SurveyedBeacon,Is.True);
            Play(s,"c096","marker:consume");Assert.That(s.View.SurveyedBeacon,Is.False);Assert.That(s.View.Ward,Is.EqualTo(3));Assert.That(BattleReplay.Capture(s).Restore(d).View.SurveyedBeacon,Is.False);
        }
        [Test] public void TypedArchiveRequiresCurrentMatchingObservationAndUnlocksIndex()
        {
            var archive=new Evidence("archive/test","稳定声源","测试标本",KnowledgeLevel.Verified,EvidenceScope.Run,subjectType:"enemy",question:"Source",rule:"source-v1",fact:"舷边");
            var d=Definition(new[]{"c040","c041","c048","c091","c014"},archive:new[]{archive});var s=new BattleSession(d,0);
            Play(s,"c040","evidence:archive/test");Play(s,"c041","enemy:enemy:1");var pair=s.View.Hand.Single(x=>x.Definition.Id=="c048").Targets.Single();Play(s,"c048",pair.Id);
            Assert.That(s.View.Evidence.Single(x=>x.Key=="enemy:1/source").Supports.Count,Is.EqualTo(2));
            Play(s,"c091");Assert.That(s.View.PendingChoice,Is.Not.Null);Run(s,BattleCommand.Reorder("retain:skip"));Assert.That(BattleReplay.Capture(s).Restore(d).View.Log,Is.EqualTo(s.View.Log));
        }
        [Test] public void MatchingStringPrefixIsNotAnEvidenceRule()
        {
            var archive=new Evidence("enemy/old","旧传闻","来源",KnowledgeLevel.Verified);
            var s=new BattleSession(Definition(new[]{"c040","c041","c048"},archive:new[]{archive}),0);Play(s,"c040","evidence:enemy/old");Play(s,"c041","enemy:enemy:1");Assert.That(s.View.Hand.Single(x=>x.Definition.Id=="c048").Targets,Is.Empty);
        }
        [Test] public void InvestigationRelaySharesOneRefundAcrossAllThreePayoffs()
        {
            var s=new BattleSession(Definition(new[]{"c041","light","c050"}),0);
            Play(s,"c041","enemy:enemy:1");Play(s,"light","enemy:enemy:1");Play(s,"c050","enemy:enemy:1");
            Assert.That(s.View.Actions,Is.EqualTo(6));Assert.That(s.View.Log.Count(x=>x.Contains("调查接力：")),Is.EqualTo(1));
            Run(s,BattleCommand.EndTurn());Play(s,"light","enemy:enemy:1");Play(s,"c041","enemy:enemy:1");Play(s,"c050","enemy:enemy:1");
            Assert.That(s.View.Actions,Is.EqualTo(6));Assert.That(s.View.Log.Count(x=>x.Contains("调查接力：")),Is.EqualTo(2));
        }
        [Test] public void OrdinaryConnectionAndFreshSoundProduceStableArchiveWithoutConfirmingIntent()
        {
            var d=Definition(new[]{"c042","c041","c048","c092"});var s=new BattleSession(d,0);
            Play(s,"c042","enemy:enemy:1");Assert.That(s.View.Enemies[0].IntentConfirmed,Is.False);
            Play(s,"c041","enemy:enemy:1");Play(s,"c092","enemy:enemy:1");Assert.That(s.View.Enemies[0].Health,Is.EqualTo(102));
            var pair=s.View.Hand.Single(x=>x.Definition.Id=="c048").Targets.Single();Play(s,"c048",pair.Id);
            var fact=s.View.Evidence.Single(x=>x.Stable);Assert.That(fact.Rule,Is.EqualTo("source-v1"));Assert.That(fact.Supports.Count,Is.EqualTo(2));
            Assert.That(BattleReplay.Capture(s).Restore(d).View.Evidence.Single(x=>x.Stable).Fact,Is.EqualTo(fact.Fact));
        }
        [Test] public void HistoricalSoundCannotVerifyTodaysConnection()
        {
            var s=new BattleSession(Definition(new[]{"c042","c041","c048"}),0);Play(s,"c042","enemy:enemy:1");Play(s,"c041","enemy:enemy:1");
            Run(s,BattleCommand.EndTurn());Assert.That(s.View.Hand.Single(x=>x.Definition.Id=="c048").Targets,Is.Empty);
            Play(s,"c041","enemy:enemy:1");Assert.That(s.View.Hand.Single(x=>x.Definition.Id=="c048").Targets.Count,Is.EqualTo(1));
        }
        [Test] public void ListenerDrawsOneAndCartographerObservationIsNotACardQuestion()
        {
            var listener=new BattleSession(Definition(new[]{"cover","cover"},crew:new[]{SupportCatalog.CrewMember("listener")},draw:1),0);
            var before=listener.View.Hand.Count;Run(listener,BattleCommand.Support("listener","enemy:1"));
            Assert.That(listener.View.Hand.Count,Is.EqualTo(before+1));Assert.That(listener.View.Evidence.Any(x=>x.SubjectId=="enemy:1"&&x.Question=="SoundSource"&&x.Scope==EvidenceScope.Turn),Is.True);

            var cartographer=new BattleSession(Definition(new[]{"c041","c092"},crew:new[]{SupportCatalog.CrewMember("cartographer")}),0);
            Run(cartographer,BattleCommand.Support("cartographer","enemy:1"));
            Assert.That(cartographer.View.Enemies[0].IntentConfirmed,Is.False);
            Play(cartographer,"c041","enemy:enemy:1");Play(cartographer,"c092","enemy:enemy:1");
            Assert.That(cartographer.View.Enemies[0].Health,Is.EqualTo(116),"Crew observation must not count as a card question.");
            var damage=new BattleSession(Definition(new[]{"contradiction"},crew:new[]{SupportCatalog.CrewMember("cartographer")}),0);
            Run(damage,BattleCommand.Support("cartographer","enemy:1"));
            Run(damage,BattleCommand.Investigate("lantern","enemy:1"));
            Play(damage,"contradiction","enemy:enemy:1");
            Assert.That(damage.View.Enemies[0].Health,Is.EqualTo(111),"Crew connection must not count as an investigation-card answer.");
        }
        [Test] public void ReplayFingerprintIncludesEquipmentAndTypedProvenance()
        {
            var d=Definition(new[]{"cover"},equipment:new[]{"i20"});var replay=BattleReplay.Capture(new BattleSession(d,0));Assert.Throws<System.IO.InvalidDataException>(()=>replay.Restore(Definition(new[]{"cover"},equipment:new[]{"i23"})));
        }
    }
}
