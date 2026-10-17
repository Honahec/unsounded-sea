using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public sealed class SupplyActionView
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public int Uses { get; }
        public bool Consumable { get; }
        public bool Enabled { get; }
        public IReadOnlyList<EncounterTargetView> Targets { get; }
        internal SupplyActionView(string id,string name,string description,int uses,bool consumable,bool enabled,IEnumerable<EncounterTargetView> targets)
        { Id=id;Name=name;Description=description;Uses=uses;Consumable=consumable;Enabled=enabled;Targets=targets.ToList().AsReadOnly(); }
    }
    public sealed partial class BattleSession
    {
        private readonly List<string> spentConsumables = new List<string>();
        private readonly Dictionary<string,int> equipmentTriggers = new Dictionary<string,int>();
        private readonly Dictionary<string,HashSet<InvestigationKind>> cardQuestions = new Dictionary<string,HashSet<InvestigationKind>>();
        private bool beaconAvailable, pendingRetain, deferredDrawEquipment, currentWeaponSingle;
        private static string SubjectType(string id) => (id??"").Split(':')[0];
        private Evidence EvidenceRelation(Evidence a,Evidence b)
        {
            if(a.Key==b.Key)return null;
            if(a.Question=="SoundSource") { var swap=a;a=b;b=swap; }
            if(b.Question!="SoundSource"||b.Scope!=EvidenceScope.Turn||b.Turn!=turn||b.EncounterId!=definition.Id||!IsLiving(b.SubjectId))return null;
            var archive=a.Scope>=EvidenceScope.Run&&a.Stable&&a.Rule=="source-v1"&&a.SubjectType==b.SubjectType&&a.Fact==b.Fact;
            var connection=a.Scope==EvidenceScope.Encounter&&a.Question=="Connection"&&a.SubjectId==b.SubjectId&&a.EncounterId==definition.Id;
            if(!archive&&!connection)return null;
            var key=b.SubjectId+"/source";
            if(knowledge.Entries.Any(x=>x.Key==key&&x.Level==KnowledgeLevel.Verified))return null;
            return new Evidence(key,"声源与记录核验一致，来源联系已验证","复核证据",KnowledgeLevel.Verified,EvidenceScope.Encounter,b.SubjectId,b.SubjectType,"Source",definition.Id,turn,"source-v1",b.Fact,new[]{a.Key,b.Key});
        }
        private int cardsDrawn, currentPaidActions;
        private bool Equipped(string id) => definition.Loadout.Equipment.Contains(id);
        private void InvestigationRefund() { if(turnFlags.Add("investigation-refund")){actions++;Record("调查接力：返还1行动；三种接力牌每回合共享一次。");} }
        private bool TriggerEquipment(string id,int maximum,bool oncePerTurn=true)
        {
            if(!Equipped(id)||oncePerTurn&&turnFlags.Contains(id))return false;
            equipmentTriggers.TryGetValue(id,out var count); if(count>=maximum)return false;
            equipmentTriggers[id]=count+1;turnFlags.Add(id);Record(SupportCatalog.Item(id).Name+"触发。");return true;
        }
        private void ConfirmationEquipment() { if(TriggerEquipment("i17",2))Draw(1); }
        private void ActiveSanityEquipment(int paid) { if(paid>0&&TriggerEquipment("i23",1))AddBlock(5); }
        private void DrawEquipment() { if(TriggerEquipment("i22",2))Draw(1); }
        private void FinishDeferredDrawEquipment() { if(deferredDrawEquipment){deferredDrawEquipment=false;DrawEquipment();} }
        private void EquipmentAfterCard(CardView card,int beforeDraw)
        {
            var n=CardNumber(card.Definition);
            if(n>=19&&n<=36||n==93||n==95)turnFlags.Add("defense-played");
            if(IsInvestigation(card.Definition)&&card.Definition.Cost>0)
            {
                if(cardsDrawn>beforeDraw)DrawEquipment();
                else if(pendingReorder!=null)deferredDrawEquipment=true;
            }
        }
        private void EquipmentTransitions(int beforeBound,int beforeEvac,int beforeSafe)
        {
            if((boss?.SafeOpenedCount??0)>beforeSafe&&TriggerEquipment("i24",1))AddWard(4);
            if((boss?.EvacuationProgress??0)>beforeEvac&&turnFlags.Remove("diver-ward"))AddWard(3);
            if(currentPaidActions<=0||turnFlags.Contains("equipment-refund"))return;
            var id=(boss?.BoundCount??0)>beforeBound?"i21":(boss?.EvacuationProgress??0)>beforeEvac?"i25":null;
            if(id!=null&&TriggerEquipment(id,1)){actions++;turnFlags.Add("equipment-refund");Record("返还实际支付的1行动。");}
        }
        private IEnumerable<EncounterTargetView> SupplyTargets(string id)
        {
            if(id=="i06")return boss?.CardTargets(43)??EmptyTargets;
            if(id=="i07"||id=="i09")return boss?.CardTargets(82)??EmptyTargets;
            if(id=="i08")return boss?.CardTargets(63)??EmptyTargets;
            if(id=="i13")return discard.Where(c=>IsArmed(c.Definition)).Select(DiscardOption);
            if(id=="i15")return (boss?.CardTargets(62)??EmptyTargets).Where(x=>Find(x.Id.Substring(6))?.Interrupted==false);
            if(id=="i16")return boss?.CardTargets(67)??EmptyTargets;
            return EmptyTargets;
        }
        private static bool SupplyRequiresTarget(string id) => new[]{"i06","i07","i08","i09","i13","i15","i16"}.Contains(id);
        private IEnumerable<SupplyActionView> SupplyViews()
        {
            if(definition.Loadout.EchoRitual)yield return EchoRitualView();
            foreach(var id in definition.Loadout.Consumables.Distinct().Concat(definition.Tools.Where(x=>(int)x.Kind>=3).Select(x=>x.Id)))
            {
                var item=SupportCatalog.Item(id); var consumable=item.Kind==ItemKind.Consumable;
                var uses=consumable?definition.Loadout.Consumables.Count(x=>x==id)-spentConsumables.Count(x=>x==id):toolUses[id];
                var targets=SupplyTargets(id).Where(x=>x.Enabled).ToList();
                var enabled=phase==BattlePhase.PlayerTurn&&uses>0&&actions>=item.Cost&&(!SupplyRequiresTarget(id)||targets.Count>0)&&
                    (id!="i10"||health<definition.Health)&&(id!="i14"||boss==null&&definition.Objective==ObjectiveKind.Seal&&objective.Progress<objective.Target);
                yield return new SupplyActionView(id,item.Name,item.Description+" · "+item.Cost+"行动",uses,consumable,enabled,targets);
            }
        }
        private string UseSupply(string id,string target,bool consumable)
        {
            if(id=="ritual:echo")return UseEchoRitual(target,consumable);
            var view=SupplyViews().FirstOrDefault(x=>x.Id==id&&x.Consumable==consumable);
            if(view==null||!view.Enabled)return "该物资已耗尽、行动不足或当前没有适用目标。";
            if(view.Targets.Count>0?!view.Targets.Any(x=>x.Id==target&&x.Enabled):target!=null)return "请选择列出的合法物资目标；未扣次数。";
            var item=SupportCatalog.Item(id);
            if(consumable)spentConsumables.Add(id);else toolUses[id]--;
            actions-=item.Cost;currentPaidActions=item.Cost;
            switch(id)
            {
                case "i06":boss.MeasureCell(target);turnFlags.Add("tool-new-answer");break;
                case "i07":case "i09":boss.VerifyGuardAnchor(target);if(!consumable)turnFlags.Add("tool-new-answer");break;
                case "i08":boss.ApplyCardAction(63,target);AddBlock(4);break;
                case "i10":Heal(8);break;
                case "i11":AddWard(6);break;
                case "i13":MoveDiscardToHand(target,IsArmed);break;
                case "i14":AdvanceObjective(1);MarkSceneAction();break;
                case "i15":boss.BindThreat(target,null);break;
                case "i16":boss.OpenProvenSafe(target,false);break;
            }
            Record("使用"+item.Name+"；"+(consumable?"消耗1份。":"消耗1次。"));CheckOutcome();return null;
        }
        private IEnumerable<EncounterTargetView> CrewTargets(CrewSupportSpec support)
        {
            if(support.Kind==CrewSupportKind.Listener)return LivingSubjects.Where(x=>CanInvestigate(x.Definition.Id,InvestigationKind.SoundSource)&&!directionalListeningBlocked).Select(x=>Option(x.Definition.Id,x.Definition.Name,"新声源"));
            if(support.Kind==CrewSupportKind.Gunsmith)return discard.Where(x=>x.Definition.Cost==1&&!IsForbidden(x.Definition)).Select(DiscardOption);
            if(support.Kind==CrewSupportKind.Cartographer)return boss?.ConnectionTargets()??EmptyTargets;
            return EmptyTargets;
        }
        private CrewSupportView BuildSupportView(CrewSupportSpec support)
        {
            var targets=CrewTargets(support).ToList().AsReadOnly();var used=usedSupports.Contains(support.Id);
            var requires=support.Kind==CrewSupportKind.Listener||support.Kind==CrewSupportKind.Gunsmith||support.Kind==CrewSupportKind.Cartographer;
            var reason=support.Injured?"受伤，支援与航程职责暂停。":used?"本场已使用。":crewSupportUsed?"本回合已使用支援。":actions<support.Cost?"行动不足。":requires&&targets.Count==0?"没有合法目标。":"";
            return new CrewSupportView(support,used,phase==BattlePhase.PlayerTurn&&reason=="",reason,targets);
        }
        private string UseCrew(CrewSupportSpec support,string target)
        {
            if(support==null)return "没有该船员。";
            var view=BuildSupportView(support);if(!view.Enabled)return view.DisabledReason;
            if(view.Targets.Count>0?!view.Targets.Any(x=>x.Id==target):target!=null)return "请选择列出的支援目标。";
            actions-=support.Cost;currentPaidActions=support.Cost;crewSupportUsed=true;usedSupports.Add(support.Id);
            switch(support.Kind)
            {
                case CrewSupportKind.Medic:Heal(2);break;
                case CrewSupportKind.Listener:Investigate(target,InvestigationKind.SoundSource,support.Name);break;
                case CrewSupportKind.Engineer:AddBlock(11);break;
                case CrewSupportKind.Archivist:Draw(2);break;
                case CrewSupportKind.Navigator:AddBlock(5);sceneDiscount=Math.Max(sceneDiscount,1);break;
                case CrewSupportKind.Gunsmith:MoveDiscardToHand(target,x=>x.Cost==1&&!IsForbidden(x));break;
                case CrewSupportKind.Diver:AddBlock(6);turnFlags.Add("diver-ward");break;
                case CrewSupportKind.Cook:AddWard(7);break;
                case CrewSupportKind.Cartographer:boss.InspectConnection(target);break;
                case CrewSupportKind.Decoder:AddWard(boss?.RevealNextConstraint()==true?4:7);break;
            }
            Record(support.Name+"支援；支付"+support.Cost+"行动。");return null;
        }
        private void ApplySupportCard(CardView card,string target)
        {
            switch(CardNumber(card.Definition))
            {
                case 91:var verified=turnFlags.Contains("archive-verified");Draw(verified?2:1);if(verified)pendingRetain=true;break;
                case 92:var id=target.Substring(6);WeaponDamage(id,cardQuestions.TryGetValue(id,out var questions)&&questions.Count>=2?18:4,card.InstanceId);break;
                case 93:AddBlock(4);AddWard(2);if(crewSupportUsed)pendingRetain=true;break;
                case 94:Draw(turnFlags.Contains("tool-new-answer")&&turnFlags.Add("c094-bonus")?2:1);break;
                case 95:var used=spentConsumables.Count>0;AddBlock(used?8:5);if(used)Draw(1);break;
                case 96:AddBlock(6);if(target=="marker:consume"){beaconAvailable=false;Draw(1);AddWard(3);}break;
            }
        }
    }
}
