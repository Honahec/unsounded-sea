using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public sealed class VoyageMarker
    {
        public string Id { get; }
        public string Source { get; }
        public string Expiry { get; }
        public VoyageMarker(string id,string source,string expiry){Id=id;Source=source;Expiry=expiry;}
    }
    public sealed partial class VoyageSession
    {
        private readonly List<string> inventory=new List<string>{"i10"},carriedItems=new List<string>{"i10"};
        private readonly List<string> ownedEquipment=new List<string>{"i17"},equippedItems=new List<string>{"i17"},crewIds=new List<string>();
        private readonly HashSet<string> injuredCrew=new HashSet<string>(),trustedCrew=new HashSet<string>(),supportServices=new HashSet<string>();
        private readonly Dictionary<string,VoyageMarker> markers=new Dictionary<string,VoyageMarker>();
        private readonly List<Evidence> runArchive=new List<Evidence>();
        private bool freeNavigationUsed,initialEquipmentChosen,navigatorUsed,repairDiscountUsed;
        private string escortedCrew;
        public int Watched { get; private set; }
        public IReadOnlyList<string> Inventory=>inventory.ToArray();
        public IReadOnlyList<string> CarriedItems=>carriedItems.ToArray();
        public IReadOnlyList<string> OwnedEquipment=>ownedEquipment.ToArray();
        public IReadOnlyList<string> Equipment=>equippedItems.ToArray();
        public IReadOnlyList<string> CrewIds=>crewIds.ToArray();
        public IReadOnlyList<string> InjuredCrew=>injuredCrew.ToArray();
        public IReadOnlyList<string> TrustedCrew=>trustedCrew.ToArray();
        public string EscortedCrew=>escortedCrew;
        public IReadOnlyList<VoyageMarker> Markers=>markers.Values.OrderBy(x=>x.Id,StringComparer.Ordinal).ToArray();
        public IReadOnlyList<Evidence> Archive=>runArchive.ToArray();
        public bool ActiveCrew(string id)=>crewIds.Contains(id)&&!injuredCrew.Contains(id);
        public BattleLoadout Loadout=>new BattleLoadout(carriedItems,equippedItems,markers.ContainsKey("surveyed-beacon"),Watched,echoRitualPrepared);
        private bool FreeNavigationUse(){if(!ActiveCrew("cartographer")||freeNavigationUsed)return false;freeNavigationUsed=true;return true;}
        private string ServiceKey(string service)=>Act+":"+CurrentFull.Id+":"+service;
        private bool ServiceAvailable(string service)=>!supportServices.Contains(ServiceKey(service));
        private void SpendService(string service)=>supportServices.Add(ServiceKey(service));
        private bool AtSupply=>(phase==VoyagePhase.Arrived||phase==VoyagePhase.ChoosingRoute&&initialPreparation)&&CurrentFull.Kind==RunNodeKind.Supply;
        private static string CrewRegion(string id)=>id=="medic"||id=="listener"||id=="cook"?"fog":id=="archivist"||id=="decoder"?"bones":id=="cartographer"?"inverted":id=="engineer"||id=="gunsmith"?"fleet":"abyss";
        private string RecruitCandidate=>RecruitCandidateAt(CurrentFull);
        private string RecruitCandidateAt(VoyageStop stop)=>SupportCatalog.Crew.Where(x=>!crewIds.Contains(x.Id)).OrderBy(x=>VoyageSeeds.Derive(Seed,"rescue",Act+":"+stop.Id+":"+x.Id)).Select(x=>x.Id).FirstOrDefault();
        private IEnumerable<string> RegionItems(bool forReward=false)
        {
            var region=forReward?nextRegion??RegionId:RegionId;
            var boss=forReward&&nextRegion!=null?VoyageMapGenerator.BossEncounter(Seed,Act+1,nextRegion):byId[plan.GoalId].EncounterId;
            return SupportCatalog.Items.Where(item=>item.Id.StartsWith("i",StringComparison.Ordinal)&&
                (item.Id=="i14"?boss=="fog-visitors"||region=="inverted"||region=="fleet":item.Id=="i16"?boss=="m03":item.Id=="i25"?region=="bones"||region=="abyss":item.BossIds.Count==0||item.BossIds.Any(id=>string.Equals(id,boss,StringComparison.OrdinalIgnoreCase)))).Select(item=>item.Id);
        }
        public IReadOnlyList<ItemSpec> ShopItems => AtSupply?ShopStock(CurrentFull):Array.Empty<ItemSpec>();
        public IReadOnlyList<ItemSpec> RewardItems => phase==VoyagePhase.Reward&&(CurrentFull.Kind==RunNodeKind.Reward||CurrentFull.Kind==RunNodeKind.Boss)?RegionItems(forReward:true).Where(id=>SupportCatalog.Item(id).Kind==ItemKind.Equipment&&!ownedEquipment.Contains(id)).OrderBy(id=>VoyageSeeds.Derive(Seed,"equipment-reward",Act+":"+CurrentFull.Id+":"+id)).Take(1).Select(SupportCatalog.Item).ToArray():Array.Empty<ItemSpec>();
        private bool CanGain(string id,string replace=null)
        {
            var item=SupportCatalog.Item(id);
            if(item.Kind==ItemKind.Tool)return replace==null&&!learnedTools.Contains(id);
            if(item.Kind==ItemKind.Equipment)return replace==null&&!ownedEquipment.Contains(id);
            if(item.Kind!=ItemKind.Consumable)return false;
            if(replace!=null&&!inventory.Contains(replace))return false;
            return inventory.Count-(replace==null?0:1)<6 && inventory.Count(x=>x==id)-(replace==id?1:0)<2;
        }
        private void Gain(string id,string replace=null)
        {
            if(replace!=null){inventory.Remove(replace);if(carriedItems.Count(x=>x==replace)>inventory.Count(x=>x==replace))carriedItems.Remove(replace);}
            var kind=SupportCatalog.Item(id).Kind;
            if(kind==ItemKind.Tool)learnedTools.Add(id);else if(kind==ItemKind.Equipment)ownedEquipment.Add(id);else inventory.Add(id);
        }
        private bool ClaimItemReward(string id){if(!RewardItems.Any(x=>x.Id==id)||!CanGain(id))return false;Gain(id);return true;}
        private void AddMarker(string id,string expiry)=>markers[id]=new VoyageMarker(id,Act+":"+CurrentFull.Id,expiry);
        private int HirePrice=>markers.ContainsKey("docking-voucher")?1:2;
        public static bool IsLoadoutChoice(VoyageNodeChoice choice)
            => choice.Id.StartsWith("equip:",StringComparison.Ordinal)||choice.Id.StartsWith("pack:",StringComparison.Ordinal)||choice.Id.StartsWith("unpack:",StringComparison.Ordinal);
        private bool IsNodeResolutionChoice(VoyageNodeChoice choice)
        {
            if(phase!=VoyagePhase.Arrived||nodeResolved)return false;
            if(CurrentFull.Kind==RunNodeKind.Event&&Watched==3&&ServiceAvailable("return-gaze"))return false;
            if(choice.Id=="ritual:learn")return true;
            var verb=choice.Id.Split(':')[0];
            return CurrentFull.Kind==RunNodeKind.Event&&(verb=="rescue"||verb=="voucher"||verb=="specimen"||verb=="repair-boat"||verb=="crew"||verb=="risk")
                ||CurrentFull.Kind==RunNodeKind.Archive&&(verb=="survey"||verb=="archive"||verb=="specimen");
        }
        public IReadOnlyList<VoyageNodeChoice> GetSupportChoices()=>GetCombinedChoices().Where(x=>!IsNodeService(x.Id)).ToArray();
        public IReadOnlyList<VoyageNodeChoice> GetNodeServices()
        {
            var services=GetCombinedChoices().Where(x=>IsNodeService(x.Id)||IsPreparationService(x.Id));
            if(nodeResolved&&!initialPreparation)services=escortedCrew==null?Enumerable.Empty<VoyageNodeChoice>():services.Where(x=>x.Id=="accept"||x.Id.StartsWith("accept@",StringComparison.Ordinal)||x.Id=="escort-decline");
            return services.ToArray();
        }
        private static bool IsNodeService(string id)=>id.StartsWith("buy:",StringComparison.Ordinal)||id.StartsWith("hire:",StringComparison.Ordinal)||
            id.StartsWith("treat:",StringComparison.Ordinal)||id.StartsWith("gunsmith:",StringComparison.Ordinal)||id=="accept"||
            id.StartsWith("accept@",StringComparison.Ordinal)||id=="escort-decline"||id=="gaze:hide"||id=="gaze:keep";
        private static bool IsPreparationService(string id)=>id.StartsWith("starter:",StringComparison.Ordinal)||id.StartsWith("trust:",StringComparison.Ordinal)||id=="tea"||id=="navigate"||id=="ritual:prepare";
        private string DisabledSupportReason(string action)
        {
            var split=action.Split('@');var parts=split[0].Split(':');
            var verb=parts[0];var id=parts.Length>1?parts[1]:null;
            string SupplyCost(int cost)=>$"需要 {cost} 补给（现有 {Supplies}）";
            string InventorySpace(string itemId,string replacement=null)
            {
                var item=SupportCatalog.Item(itemId);
                if(item.Kind==ItemKind.Tool)return "已取得这件工具";
                if(item.Kind==ItemKind.Equipment)return "已拥有这件装备";
                if(inventory.Count(x=>x==itemId)-(replacement==itemId?1:0)>=2)return item.Name+"已存放两份";
                return "物资箱已满（6/6）";
            }
            switch(verb)
            {
                case "equip":
                    var conflict=equippedItems.FirstOrDefault(x=>SupportCatalog.Item(x).Engine);
                    if(SupportCatalog.Item(id).Engine&&conflict!=null)return "先卸下"+SupportCatalog.Item(conflict).Name;
                    return "装备位已满（3/3）";
                case "pack":
                    if(carriedItems.Count(x=>x==id)>=inventory.Count(x=>x==id))return "已全部随身携带";
                    if(carriedItems.Count>=2)return "战斗物资已满（2/2）";
                    return "调查工具位已满，先卸下一件工具";
                case "buy":
                    var price=SupportCatalog.Item(id).Kind==ItemKind.Consumable?1:2;
                    return Supplies<price?SupplyCost(price):InventorySpace(id,split.Length>1?split[1]:null);
                case "hire":return SupplyCost(HirePrice);
                case "treat":return SupplyCost(2);
                case "tea":case "crew":return "理智已满";
                case "gunsmith":return id=="chart"?"海图次数已满（2/2）":id=="lookout"?"望远镜次数已满（2/2）":InventorySpace("i13");
                case "trust":return "暂无可学习的相关行动";
                case "repair-boat":return learnedTools.Contains("i08")?"已取得绝缘钳":SupplyCost(1);
                case "gaze":case "rescue":return SupplyCost(1);
                case "archive":return id=="card"?"已学会复核证据":"补给已满";
                case "specimen":
                    if(id=="archive")return "尚无已核实的来源记录";
                    if(id=="supply")return "补给已满";
                    return markers.ContainsKey("sealed-specimen")?"已携带封存标本":SupplyCost(1);
                case "risk":return id=="watch"?"补给已满":"需空出两份补给的位置";
                default:return "";
            }
        }

        private IReadOnlyList<VoyageNodeChoice> GetCombinedChoices()
        {
            var result=new List<VoyageNodeChoice>();
            void Add(string id,string title,string detail,bool enabled=true)=>result.Add(new VoyageNodeChoice(id,title,detail,enabled,enabled?"":DisabledSupportReason(id)));
            result.AddRange(GetRitualChoices());
            if(CanEditDeck)
            {
                if(Act==1&&initialPreparation&&!initialEquipmentChosen)
                    foreach(var id in new[]{"i17","i18","i19"})Add("starter:"+id,"起步装备 · "+SupportCatalog.Item(id).Name,"三选一，替换默认怀表；每航程一次。 "+SupportCatalog.Item(id).Description);
                foreach(var id in ownedEquipment){var equipped=equippedItems.Contains(id);Add("equip:"+id,(equipped?"卸下 · ":"装备 · ")+SupportCatalog.Item(id).Name,SupportCatalog.Item(id).Description,equipped||equippedItems.Count<3&&(!SupportCatalog.Item(id).Engine||!equippedItems.Any(x=>SupportCatalog.Item(x).Engine)));}
                foreach(var id in inventory.Distinct())
                {
                    if(id=="i12")continue;
                    Add("pack:"+id,"带入 · "+SupportCatalog.Item(id).Name,$"库存{inventory.Count(x=>x==id)} / 已带{carriedItems.Count(x=>x==id)} · "+SupportCatalog.Item(id).Description,carriedItems.Count<2&&carriedItems.Count(x=>x==id)<inventory.Count(x=>x==id)&&(id!="i09"||tools.Count+carriedItems.Count(x=>x=="i09")<2));
                    if(carriedItems.Contains(id))Add("unpack:"+id,"卸下物资 · "+SupportCatalog.Item(id).Name,"移回库存，不损失份数。");
                }
            }
            if(phase==VoyagePhase.ChoosingRoute&&ActiveCrew("navigator")&&!navigatorUsed&&HasUnknownRoutes(CurrentFull))Add("navigate","领航侦察","每航程一次，揭示当前所有相邻航点和出口。");
            if(AtSupply)
            {
                if(ServiceAvailable("shop"))foreach(var item in ShopItems)
                {
                    var price=item.Kind==ItemKind.Consumable?1:2;
                    Add("buy:"+item.Id,"购买 · "+item.Name,$"{price}补给，每节点买一次。 "+item.Description,Supplies>=price&&CanGain(item.Id));
                    if(!CanGain(item.Id)&&item.Kind==ItemKind.Consumable)foreach(var old in inventory.Distinct())Add("buy:"+item.Id+"@"+old,"换购 · "+item.Name+"（替换"+SupportCatalog.Item(old).Name+"）",$"明确丢弃一份旧物资并支付{price}补给。",Supplies>=price&&CanGain(item.Id,old));
                }
                if(ServiceAvailable("hire"))foreach(var crew in CrewStock(CurrentFull))
                {
                    var replacements=crewIds.Count>=4?crewIds.Cast<string>():new string[]{null};
                    foreach(var old in replacements)Add("hire:"+crew.Id+(old==null?"":"@"+old),"接驳 · "+crew.Name+(old==null?"":"（替换"+SupportCatalog.CrewMember(old).Name+"）"),$"{HirePrice}补给；"+SupportCatalog.CrewDescription(crew.Kind),Supplies>=HirePrice);
                }
                if(inventory.Contains("i12")&&ServiceAvailable("tea"))Add("tea","饮用干燥药茶","消耗1份，恢复5理智，每节点一次。",Sanity<GameBalance.MaxSanity);
                foreach(var id in crewIds.Where(injuredCrew.Contains))Add("treat:"+id,"治疗 · "+SupportCatalog.CrewMember(id).Name,"2补给；恢复支援与航程职责。",Supplies>=2);
                if(ActiveCrew("gunsmith")&&ServiceAvailable("gunsmith")){Add("gunsmith:item","修械补给","获得1份备用弹匣，与恢复工具二选一。",CanGain("i13"));Add("gunsmith:chart","修复海图","恢复1次，不超过2次。",ChartUses<2);Add("gunsmith:lookout","修复望远镜","恢复1次，不超过2次。",LookoutUses<2);}
                foreach(var id in crewIds.Where(x=>trustedCrew.Contains(x)&&!injuredCrew.Contains(x)&&!supportServices.Contains("trust:"+x)))Add("trust:"+id,"个人交谈 · "+SupportCatalog.CrewMember(id).Name,"信任奖励：学习一张相关未学方案；每人每航程一次。",TrustedCard(id)!=null);
            }
            if(phase==VoyagePhase.Arrived&&!nodeResolved&&CurrentFull.Kind==RunNodeKind.Archive)
            {
                Add("survey","核验航标","不领取补给；揭示全部后续路线，获得已测航标（下一场战斗后过期）。");
                Add("archive:supply","整理物资记录","获得1补给；与核验航标、档案选项互斥。",Supplies<GameBalance.MaxSupplies);
                if(ActiveCrew("archivist"))Add("archive:card","档案员交叉索引","额外候选：学习复核证据；仍只选一项。",!learned.Contains("c048"));
                if(markers.ContainsKey("sealed-specimen")){Add("specimen:supply","交付标本 · 补给","消耗标本，获得1补给。",Supplies<GameBalance.MaxSupplies);Add("specimen:archive","交付标本 · 来源索引","消耗标本，将已有稳定来源档案加入现场索引；没有已验证来源时不可选。",runArchive.Any(x=>x.Rule=="source-v1"));Add("specimen:hint","交付标本 · 处置建议","消耗标本，记录相关已学牌提示，不自动编入牌组。");}
            }
            if(phase==VoyagePhase.Arrived&&!nodeResolved&&CurrentFull.Kind==RunNodeKind.Event)
            {
                if(Watched==3&&ServiceAvailable("return-gaze")){Add("gaze:hide","遮蔽踪迹","花1补给，被注视降至2，然后继续选择事件。",Supplies>0);Add("gaze:keep","保留踪迹","保留被注视3，查看事件；拒绝风险选项没有惩罚。 ");return result;}
                var candidate=RecruitCandidate;
                if(candidate!=null&&escortedCrew==null)Add("rescue:"+candidate,"绞盘营救 · "+SupportCatalog.CrewMember(candidate).Name,"花1补给救援（可用潜水员免付）；候选加入待护送位，随后决定邀请。",Supplies>0||ActiveCrew("diver"));
                Add("voucher","接引遇险接驳船","获得接驳凭证；下次成功招聘少付1补给，幕末过期。");
                Add("specimen","封存漂浮标本","花1补给，获得封存标本；可在档案节点交付。",Supplies>0&&!markers.ContainsKey("sealed-specimen"));
                Add("repair-boat","修复漂流维修船","支付1补给（轮机长首次免付），获得绝缘钳；放弃其他事件收益。",!learnedTools.Contains("i08")&&(Supplies>0||ActiveCrew("engineer")&&!repairDiscountUsed));
                if(ActiveCrew("listener"))Add("crew:listen","比对录音","免费核对信号，恢复4理智并记录来源。",Sanity<GameBalance.MaxSanity);
                if(ActiveCrew("decoder"))Add("crew:decode","核对值班序列","获得本幕实际威胁简报并揭示后续航路。");
                if(Watched>0)Add("risk:watch","回应异常信号","获得1补给、被注视+1（最高3）；自愿选择。",Supplies<GameBalance.MaxSupplies);
                foreach(var id in crewIds.Where(x=>!injuredCrew.Contains(x)))Add("risk:"+id,"冒险取回漂箱 · "+SupportCatalog.CrewMember(id).Name,"确定获得2补给；该船员受伤，支援和航程职责暂停，休整需2补给治疗。",Supplies<=GameBalance.MaxSupplies-2);
            }
            if(escortedCrew!=null&&phase==VoyagePhase.Arrived&&(CurrentFull.Kind==RunNodeKind.Supply||CurrentFull.Kind==RunNodeKind.Event))
            {
                foreach(var old in crewIds.Count>=4?crewIds.Cast<string>():new string[]{null})Add("accept"+(old==null?"":"@"+old),"邀请 · "+SupportCatalog.CrewMember(escortedCrew).Name+(old==null?"":"（替换"+SupportCatalog.CrewMember(old).Name+"）"),"在安全接驳窗口加入，下场可支援；离船者安全转送。");
                Add("escort-decline","转送候选至安全船只","放弃此次邀请，无惩罚。");
            }
            return result;
        }
        private string TrustedCard(string id)=>new[]{id=="medic"||id=="cook"?"c093":id=="listener"||id=="archivist"?"c091":id=="gunsmith"?"c092":"c094","c050","c049"}.FirstOrDefault(x=>!learned.Contains(x)&&(x!="c091"||runArchive.Any(e=>e.Rule=="source-v1")));
        public bool ManageSupport(string action,string replacement=null)
        {
            if(action!=null&&action.StartsWith("ritual:",StringComparison.Ordinal))return replacement==null&&ManageRitual(action);
            if(replacement!=null)action+="@"+replacement;
            var choice=GetCombinedChoices().FirstOrDefault(x=>x.Id==action&&x.Enabled);if(choice==null)return false;
            var pair=action.Split('@');var main=pair[0];var old=pair.Length>1?pair[1]:null;var parts=main.Split(':');var verb=parts[0];var id=parts.Length>1?parts[1]:null;
            switch(verb)
            {
                case "starter":ownedEquipment.Remove("i17");equippedItems.Remove("i17");if(!ownedEquipment.Contains(id))ownedEquipment.Add(id);equippedItems.RemoveAll(x=>SupportCatalog.Item(x).Engine);equippedItems.Add(id);initialEquipmentChosen=true;break;
                case "equip":if(!equippedItems.Remove(id))equippedItems.Add(id);break;
                case "pack":carriedItems.Add(id);break;
                case "unpack":carriedItems.Remove(id);break;
                case "navigate":navigatorUsed=true;RevealAllRoutes(CurrentFull);break;
                case "buy":SpendSupplies(SupportCatalog.Item(id).Kind==ItemKind.Consumable?1:2);Gain(id,old);SpendService("shop");break;
                case "hire":SpendSupplies(HirePrice);markers.Remove("docking-voucher");if(old!=null)crewIds.Remove(old);crewIds.Add(id);SpendService("hire");break;
                case "tea":inventory.Remove("i12");Sanity=Math.Min(GameBalance.MaxSanity,Sanity+5);SpendService("tea");break;
                case "treat":SpendSupplies(2);injuredCrew.Remove(id);break;
                case "gunsmith":if(id=="item")Gain("i13");else if(id=="chart")ChartUses++;else LookoutUses++;SpendService("gunsmith");break;
                case "trust":learned.Add(TrustedCard(id));supportServices.Add("trust:"+id);break;
                case "survey":AddMarker("surveyed-beacon","下一场战斗结束");RevealAllRoutes(CurrentFull);nodeResolved=true;break;
                case "archive":if(id=="supply")Supplies++;else learned.Add("c048");nodeResolved=true;break;
                case "specimen":
                    if(id==null){SpendSupplies(1);AddMarker("sealed-specimen","交付或航程结束");}
                    else{markers.Remove("sealed-specimen");if(id=="supply")Supplies++;else if(id=="archive"){if(!learned.Contains("c040"))learned.Add("c040");AddKnowledge("来源索引已整理：用对照旧档激活，再以新声源与复核证据核验。");}else AddKnowledge("标本处置建议："+string.Join("、",learned.Where(x=>new[]{"c008","c046","c048","c090","contradiction"}.Contains(x)).Select(x=>CardCatalog.Get(x).Name)));}nodeResolved=true;break;
                case "rescue":if(!ActiveCrew("diver"))SpendSupplies(1);escortedCrew=id;nodeResolved=true;break;
                case "voucher":AddMarker("docking-voucher","本幕结束或下次成功招聘");nodeResolved=true;break;
                case "repair-boat":if(ActiveCrew("engineer")&&!repairDiscountUsed)repairDiscountUsed=true;else SpendSupplies(1);learnedTools.Add("i08");nodeResolved=true;break;
                case "crew":if(id=="listen"){Sanity=Math.Min(GameBalance.MaxSanity,Sanity+4);AddKnowledge("比对录音：重复信号来自自动发报机，未确认其后的实体。");}else{AddKnowledge(RegionBriefing(true));RevealAllRoutes(CurrentFull);}nodeResolved=true;break;
                case "gaze":if(id=="hide"){SpendSupplies(1);Watched=2;}SpendService("return-gaze");break;
                case "risk":if(id=="watch"){Supplies++;Watched=Math.Min(3,Watched+1);}else{Supplies+=2;injuredCrew.Add(id);}nodeResolved=true;break;
                case "accept":if(old!=null)crewIds.Remove(old);crewIds.Add(escortedCrew);escortedCrew=null;break;
                case "escort-decline":escortedCrew=null;break;
                default:return false;
            }
            log.Add(CurrentFull.Name+"："+choice.Title+"。"+choice.Description);Record(VoyageActionKind.ManageSupport,value:action);return true;
        }
    }
}
