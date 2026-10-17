using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public enum VoyagePhase { ChoosingRoute, Sailing, Arrived, Encounter, Reward, ActComplete, Completed, Failed }
    public enum VoyageActionKind { Select, SetSail, Arrive, Continue, BeginEncounter, ResolveEncounter, RevealRoutes, ScoutAhead, ChooseNode, ClaimReward, SetDeck, SetTools, ChooseNextAct, ManageSupport, DismissDebrief, DismissRegionIntro }

    [Serializable] public sealed class VoyageActionData
    {
        public VoyageActionKind Kind; public string StopId; public bool Victory; public int Health; public int Sanity; public string Value; public string[] Values;
        public Evidence[] Evidence;
        public int? HealthDamage; public int? SanityDamage;
    }
    [Serializable] public sealed class VoyageCheckpoint { public int Version; public VoyageMapData Map; public string CrewId; public VoyageActionData[] History; }

    public sealed class VoyageNodeChoice
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public bool Enabled { get; }
        public VoyageNodeChoice(string id,string title,string description,bool enabled=true) { Id=id; Title=title; Description=description; Enabled=enabled; }
    }
    public sealed class VoyageSummary
    {
        public string Outcome { get; internal set; }
        public string DeathCause { get; internal set; }
        public IReadOnlyList<string> Route { get; internal set; }
        public IReadOnlyList<string> LearnedCards { get; internal set; }
        public IReadOnlyList<string> SpentItems { get; internal set; }
        public int SpentSupplies { get; internal set; }
        public int Health { get; internal set; }
        public int Sanity { get; internal set; }
        public int Supplies { get; internal set; }
        public int ElapsedHours { get; internal set; }
    }

    public sealed class EncounterDebrief
    {
        public int? HealthDamage { get; }
        public int? SanityDamage { get; }
        public IReadOnlyList<string> SpentItems { get; }
        public IReadOnlyList<Evidence> ArchivedEvidence { get; }
        internal EncounterDebrief(int? healthDamage,int? sanityDamage,IEnumerable<string> spent,IEnumerable<Evidence> archived)
        {
            HealthDamage=healthDamage;SanityDamage=sanityDamage;
            SpentItems=Array.AsReadOnly(spent.ToArray());
            ArchivedEvidence=Array.AsReadOnly(archived.Select(x=>x.Archive(EvidenceScope.Run)).GroupBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Last()).ToArray());
        }
    }


    public sealed partial class VoyageSession
    {
        public const int CheckpointVersion=13;
        public const int ActLength=11;
        private const int InitialToolUses=2, MaxHistory=512;
        private static readonly string[] InitialTools={"lantern","listener","shell"};
        private static readonly int[] CommonRewards = { 3, 5, 7, 9, 14, 16, 18, 23, 24, 26, 27, 28, 29, 31, 32, 33, 35, 38, 40, 42, 45, 48, 49, 50, 51, 53, 54 };
        private static readonly Dictionary<string, int[]> BossRewards = new Dictionary<string, int[]>
        {
            ["fog-visitors"] = new[] { 6, 11, 16, 25, 32, 36, 47, 48, 58, 64, 70 },
            ["drowned-bell"] = new[] { 4, 8, 13, 15, 34, 56, 68, 88 },
            ["m02"] = new[] { 4, 8, 13, 15, 34, 46, 56, 61, 62, 68, 78, 88 },
            ["m03"] = new[] { 25, 36, 43, 44, 60, 67, 75, 77 },
            ["m04"] = new[] { 17, 25, 32, 57, 60, 63, 65, 70, 79, 85 },
            ["storm-gauntlet"] = new[] { 17, 25, 32, 60, 63, 70, 79, 85 },
            ["hollow-beacon"] = new[] { 6, 11, 16, 25, 32, 36, 47, 48, 70 },
            ["m05"] = new[] { 4, 8, 10, 13, 34, 46, 52, 62, 66, 69, 75, 81, 82, 84, 88, 90 }
        };
        private VoyagePlan plan; private Dictionary<string,VoyageStop> byId; private RunMap map;
        private readonly VoyageMapData initialMap; private readonly string initialCrew;
        private readonly List<string> visited=new List<string>(), log=new List<string>(), knowledge=new List<string>();
        private readonly List<VoyageActionData> history=new List<VoyageActionData>();
        private readonly HashSet<string> knownStopIds=new HashSet<string>(StringComparer.Ordinal), knownRouteKeys=new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> deck=new List<string>(CardCatalog.Starter.Select(card=>card.Id)), learned=new List<string>(CardCatalog.Starter.Select(card=>card.Id));
        private readonly List<string> tools=new List<string>{"lantern","listener"}, learnedTools=new List<string>(InitialTools);
        private VoyageStop selected; private VoyagePhase phase; private int elapsedHours, spentSupplies; private bool nodeResolved; private bool initialPreparation=true;
        private string nextRegion;
        private bool debug, restoring;
        private readonly HashSet<string> newRewardCards = new HashSet<string>(StringComparer.Ordinal);
        public IReadOnlyList<string> NewRewardCardIds => newRewardCards.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        public EncounterDebrief Debrief { get; private set; }
        public bool DebriefPending { get; private set; }
        public bool RegionIntroPending { get; private set; }
        public string EventTitle => CurrentFull.Kind != RunNodeKind.Event ? "" : new[] { "漂流船的残留物", "重复的求救信号", "逆流下的锚链" }[VoyageSeeds.Derive(Seed,"event",Act+":"+CurrentFull.Id)%3u];
        private VoyageSession(VoyagePlan first,string crew)
        {
            if(crew!="medic"&&crew!="listener") throw new ArgumentException("Unknown initial crew.",nameof(crew));
            initialMap=first.Export(); initialCrew=crew; CrewId=crew; Health=60; Sanity=30; Supplies=3;crewIds.Add(crew); SetPlan(first);
        }
        private VoyageSession(VoyagePlan plan,string crew,bool isDebug) : this(plan,crew)
        {
            debug=isDebug;
            phase=plan.Stops[0].Kind==RunNodeKind.Reward?VoyagePhase.Reward:VoyagePhase.Arrived;
            nodeResolved=false;
        }
        private void SetPlan(VoyagePlan value)
        {
            if(plan!=null&&plan.Act!=value.Act){markers.Remove("docking-voucher");Watched=Math.Max(0,Watched-1);}
            plan=value; byId=plan.Stops.ToDictionary(x=>x.Id,StringComparer.Ordinal);
            map=new RunMap(plan.Stops.Select(x=>new RunNode(x.Id,x.Kind,x.Clue,string.IsNullOrEmpty(x.EncounterId)?x.Id:x.EncounterId,x.Next)),plan.StartId);
            visited.Clear(); visited.Add(plan.StartId); knownStopIds.Clear(); knownRouteKeys.Clear(); knownStopIds.Add(plan.StartId); selected=null;
            map.CompleteCurrent(); phase=VoyagePhase.ChoosingRoute; nodeResolved=true; initialPreparation=true; ChartUses=InitialToolUses; LookoutUses=InitialToolUses; RevealInitialRoutes();
            log.Add(CurrentFull.Name+"：已记录第"+plan.Act+"幕出发位置。");
        }

        public uint Seed=>plan.Seed; public int Act=>plan.Act; public string RegionId=>plan.RegionId;
        public int Health{get;private set;} public int Sanity{get;private set;} public int Supplies{get;private set;} public string CrewId{get;private set;}
        public IReadOnlyList<string> DeckIds=>deck.AsReadOnly(); public IReadOnlyList<string> LearnedCardIds=>learned.AsReadOnly();
        public IReadOnlyList<string> ToolIds=>tools.AsReadOnly(); public IReadOnlyList<string> LearnedToolIds=>learnedTools.AsReadOnly();
        public bool CanEditDeck=>initialPreparation&&phase==VoyagePhase.ChoosingRoute&&visited.Count==1 || phase==VoyagePhase.Arrived&&CurrentFull.Kind==RunNodeKind.Supply;
        public IReadOnlyList<CardSpec> RewardCards=>phase==VoyagePhase.Reward?BuildRewards():Array.Empty<CardSpec>();
        public IReadOnlyList<VoyageStop> KnownStops=>plan.Stops.Where(x=>knownStopIds.Contains(x.Id)).Select(ToKnownStop).ToList().AsReadOnly();
        public IReadOnlyList<VoyageRoute> KnownRoutes=>plan.Stops.SelectMany(x=>x.Next.Where(y=>knownRouteKeys.Contains(RouteKey(x.Id,y))).Select(y=>new VoyageRoute(x.Id,y))).ToList().AsReadOnly();
        public IReadOnlyList<VoyageStop> Choices=>phase==VoyagePhase.ChoosingRoute?CurrentFull.Next.Where(IsKnownRouteFromCurrent).Where(x=>!visited.Contains(x)).Select(x=>ToKnownStop(byId[x])).ToList().AsReadOnly():Array.Empty<VoyageStop>();
        public VoyageStop Current=>ToKnownStop(CurrentFull); public VoyageStop Selected=>selected==null?null:ToKnownStop(selected); public VoyagePhase Phase=>phase;
        public int ElapsedHours=>elapsedHours; public int LegsSailed=>visited.Count-1; public int VisitedThisAct=>visited.Count;
        public IReadOnlyList<string> Visited=>visited.ToList().AsReadOnly(); public IReadOnlyList<string> Log=>log.ToList().AsReadOnly();
        public IReadOnlyList<string> Knowledge=>knowledge.ToList().AsReadOnly();
        public int ChartUses{get;private set;} public int LookoutUses{get;private set;}
        public bool CanRevealRoutes=>phase==VoyagePhase.ChoosingRoute&&ChartUses>0&&HasUnknownRoutes(CurrentFull);
        public bool CanScoutAhead=>phase==VoyagePhase.ChoosingRoute&&selected!=null&&LookoutUses>0&&HasUnknownRoutes(selected);
        public bool IsDebug=>debug;
        public VoyageSummary Summary=>new VoyageSummary
        {
            Outcome=phase==VoyagePhase.Completed?"远征完成":phase==VoyagePhase.Failed?"远征失败":"远征进行中",
            DeathCause=phase!=VoyagePhase.Failed?null:Health==0?"生命耗尽":Sanity==0?"理智崩溃":"战斗失利（未记录致命来源）",
            Route=log.Where(x=>x.StartsWith("离开",StringComparison.Ordinal)||x.StartsWith("抵达",StringComparison.Ordinal)).ToArray(),
            LearnedCards=learned.Select(id=>CardCatalog.Get(id).Name).ToArray(),
            SpentItems=history.Where(x=>x.Kind==VoyageActionKind.ResolveEncounter&&x.Values!=null).SelectMany(x=>x.Values).Select(id=>SupportCatalog.Item(id).Name).ToArray(),
            SpentSupplies=spentSupplies,Health=Health,Sanity=Sanity,Supplies=Supplies,ElapsedHours=elapsedHours
        };

        public static VoyageSession Create(uint seed,string crewId="medic")=>new VoyageSession(VoyageMapGenerator.Generate(seed),crewId);
        public static VoyageSession CreateDebug(uint seed,string regionId,RunNodeKind kind,string encounterId=null)
        {
            var act=regionId=="fog"?1:regionId=="abyss"?3:regionId=="bones"||regionId=="inverted"||regionId=="fleet"?2:0;
            if(act==0||!Enum.IsDefined(typeof(RunNodeKind),kind))throw new ArgumentException("Invalid debug voyage content.");
            var combat=kind==RunNodeKind.Battle||kind==RunNodeKind.Boss;
            if(combat==string.IsNullOrWhiteSpace(encounterId))throw new ArgumentException("Combat debug nodes require one encounter ID; noncombat nodes must not have one.");
            var place=kind==RunNodeKind.Supply?"补给母船":kind==RunNodeKind.Archive?"漂浮测量站":kind==RunNodeKind.Reward?"失联救生艇":"旧无线电船";
            var stop=new VoyageStop("debug-node",place,"调试入口","此航点不属于已保存的正常远征。",50,50,0,kind,encounterId??"",Array.Empty<string>(),0,VoyageSeeds.Derive(seed,"debug-battle",regionId+":"+kind+":"+encounterId));
            return new VoyageSession(new VoyagePlan(seed,VoyageMapGenerator.Version,act,regionId,stop.Id,stop.Id,new[]{stop}),"medic",true);
        }
        public bool Select(string id)
        {
            if(phase!=VoyagePhase.ChoosingRoute||string.IsNullOrEmpty(id)||selected?.Id==id)return false;
            var choice=Choices.FirstOrDefault(x=>x.Id==id); if(choice==null)return false; selected=byId[id]; Record(VoyageActionKind.Select,stopId:id); return true;
        }
        public bool SetSail()
        {
            if(phase!=VoyagePhase.ChoosingRoute||selected==null||!Choices.Any(x=>x.Id==selected.Id))return false;
            initialPreparation=false; phase=VoyagePhase.Sailing; log.Add("离开"+CurrentFull.Name+"，向"+HeadingTo(selected)+"航行。"); Record(VoyageActionKind.SetSail); return true;
        }
        public bool Arrive()
        {
            if(phase!=VoyagePhase.Sailing||selected==null)return false; var destination=selected; if(!map.TryMove(destination.Id))return false;
            visited.Add(destination.Id); elapsedHours+=destination.TravelHours; selected=null; phase=destination.Kind==RunNodeKind.Reward?VoyagePhase.Reward:VoyagePhase.Arrived; nodeResolved=false; RevealDefaultRoutes(destination); log.Add("抵达"+destination.Name+"："+destination.Description); Record(VoyageActionKind.Arrive); return true;
        }
        public bool Continue()
        {
            if(phase!=VoyagePhase.Arrived||IsEncounter(CurrentFull)||CurrentFull.Kind==RunNodeKind.Reward||!nodeResolved)return false;
            FinishNode(); Record(VoyageActionKind.Continue); return true;
        }
        public bool CanContinue=>phase==VoyagePhase.Arrived&&!IsEncounter(CurrentFull)&&CurrentFull.Kind!=RunNodeKind.Reward&&nodeResolved;
        public bool BeginEncounter()
        {
            if(phase!=VoyagePhase.Arrived||!IsEncounter(CurrentFull)||string.IsNullOrEmpty(CurrentFull.EncounterId))return false;
            phase=VoyagePhase.Encounter; Record(VoyageActionKind.BeginEncounter); return true;
        }
        public bool ResolveEncounter(bool victory,int health,int sanity,IEnumerable<string> spent=null,int? finalWatched=null,IEnumerable<Evidence> evidence=null,int? healthDamage=null,int? sanityDamage=null)
        {
            if(phase!=VoyagePhase.Encounter||health<0||health>60||sanity<0||sanity>30)return false;
            var used=(spent??Array.Empty<string>()).ToArray();var findings=(evidence??Array.Empty<Evidence>()).ToArray();
            if(used.Any(id=>!carriedItems.Contains(id))||used.GroupBy(x=>x).Any(g=>g.Count()>carriedItems.Count(x=>x==g.Key)||g.Count()>inventory.Count(x=>x==g.Key))||finalWatched<0||finalWatched>3||findings.Any(x=>x==null)||healthDamage<0||sanityDamage<0)return false;
            foreach(var id in used){inventory.Remove(id);carriedItems.Remove(id);}Watched=finalWatched??Watched;markers.Remove("surveyed-beacon");
            if(victory){foreach(var entry in findings.Where(x=>x.Stable)){var stored=entry.Archive(EvidenceScope.Run);runArchive.RemoveAll(x=>x.Key==stored.Key);runArchive.Add(stored);}if(CurrentFull.Kind==RunNodeKind.Boss)foreach(var id in crewIds.Where(id=>CrewRegion(id)==RegionId))trustedCrew.Add(id);}
            FinishRitualEncounter();
            Health=health; Sanity=sanity;
            var feedback=new List<string>();if(used.Length>0)feedback.Add("消耗 "+string.Join("、",used.GroupBy(x=>x).Select(x=>SupportCatalog.Item(x.Key).Name+"×"+x.Count())));var archived=victory?findings.Where(x=>x.Stable).Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count():0;if(archived>0)feedback.Add("归档 "+archived+" 条核验");if(feedback.Count>0)log.Add("战后："+string.Join("；",feedback)+"。");
            if(!victory||health==0||sanity==0) phase=VoyagePhase.Failed;
            else if(debug) phase=CurrentFull.Kind==RunNodeKind.Boss?VoyagePhase.Completed:VoyagePhase.Reward;
            else if(CurrentFull.Kind==RunNodeKind.Boss) phase=Act==3?VoyagePhase.Completed:VoyagePhase.ActComplete;
            else phase=VoyagePhase.Reward;
            if(phase==VoyagePhase.Completed||phase==VoyagePhase.Failed)markers.Clear();
            Debrief = new EncounterDebrief(healthDamage,sanityDamage,used,victory?findings.Where(x=>x.Stable):Array.Empty<Evidence>());
            DebriefPending = victory && health>0 && sanity>0;
            Record(VoyageActionKind.ResolveEncounter,victory:victory,health:health,sanity:sanity,value:Watched.ToString(System.Globalization.CultureInfo.InvariantCulture),values:used);
            var recorded=history[history.Count-1];recorded.Evidence=findings;recorded.HealthDamage=healthDamage;recorded.SanityDamage=sanityDamage;return true;
        }
        public bool DismissDebrief(){if(!DebriefPending)return false;DebriefPending=false;Record(VoyageActionKind.DismissDebrief);return true;}
        public bool DismissRegionIntro(){if(!RegionIntroPending)return false;RegionIntroPending=false;Record(VoyageActionKind.DismissRegionIntro);return true;}
        public bool RevealRoutes(){if(!CanRevealRoutes)return false;RevealAllRoutes(CurrentFull);if(!FreeNavigationUse())ChartUses--;Record(VoyageActionKind.RevealRoutes);return true;}
        public bool ScoutAhead(){if(!CanScoutAhead)return false;RevealAllRoutes(selected);if(!FreeNavigationUse())LookoutUses--;Record(VoyageActionKind.ScoutAhead);return true;}

        public IReadOnlyList<VoyageNodeChoice> GetNodeChoices()
            => GetBasicNodeChoices().Concat(GetSupportChoices().Where(IsNodeResolutionChoice)).ToArray();
        private IReadOnlyList<VoyageNodeChoice> GetBasicNodeChoices()
        {
            if(phase==VoyagePhase.ActComplete && Act==1) return new[]{new VoyageNodeChoice("bones","尸骸之海","鲸骸漂流带与密集接舷"),new VoyageNodeChoice("inverted","倒悬潮庭","逆向潮流与失衡航道"),new VoyageNodeChoice("fleet","无灯裂海","无灯舰群与狭窄水道")};
            if(phase!=VoyagePhase.Arrived||nodeResolved)return Array.Empty<VoyageNodeChoice>();
            if(CurrentFull.Kind==RunNodeKind.Event&&Watched==3&&ServiceAvailable("return-gaze"))return Array.Empty<VoyageNodeChoice>();
            switch(CurrentFull.Kind)
            {
                case RunNodeKind.Event:return EventChoices();
                case RunNodeKind.Archive:
                    var routesAvailable=HasUnknownRoutes(CurrentFull);
                    return new[]{new VoyageNodeChoice("routes","抄录航路",routesAvailable?"揭示此处全部后续航路，并把海况结论写入日志":"所有后续航路已经记录，没有新航路可抄录",routesAvailable||restoring),new VoyageNodeChoice("briefing","研读幕末简报","记录本幕实际威胁的真实机制线索"),new VoyageNodeChoice("seal","封存记录",Sanity<30?"恢复5理智":"理智已满，无需封存记录",Sanity<30)};
                case RunNodeKind.Supply:return new[]{new VoyageNodeChoice("rest","休整",Health<60||Sanity<30?"恢复12生命与5理智":"生命与理智均已恢复至上限",Health<60||Sanity<30),new VoyageNodeChoice("repair","深度治疗",Supplies==0?"补给不足；需要1补给":Health>=60?"生命已满，无需深度治疗":"消耗1补给，恢复24生命",Supplies>0&&Health<60),new VoyageNodeChoice("calm","长期安抚",Supplies==0?"补给不足；需要1补给":Sanity>=30?"理智已满，无需长期安抚":"消耗1补给，恢复11理智",Supplies>0&&Sanity<30),new VoyageNodeChoice("depart","不作整备","继续航行")};
                default:return Array.Empty<VoyageNodeChoice>();
            }
        }
        private IReadOnlyList<VoyageNodeChoice> EventChoices()
        {
            switch(VoyageSeeds.Derive(Seed,"event",Act+":"+CurrentFull.Id)%3u)
            {
                case 0:return new[]{new VoyageNodeChoice("salvage","登船拆取","获得2补给，失去5生命",Health>5&&Supplies<=6),new VoyageNodeChoice("observe","绕船观测","恢复3理智",Sanity<30),new VoyageNodeChoice("leave","保持航向","无资源变化")};
                case 1:return new[]{new VoyageNodeChoice("tow","拖回浮箱","获得1补给，耗时2小时",Supplies<8),new VoyageNodeChoice("listen","核对求救频率","恢复4理智",Sanity<30),new VoyageNodeChoice("leave","切断信号","无资源变化")};
                default:
                    var routesAvailable=HasUnknownRoutes(CurrentFull);
                    return new[]{new VoyageNodeChoice("dive","下潜检查锚链","获得2补给，失去3理智",Sanity>3&&Supplies<=6),new VoyageNodeChoice("mark","标记危险水流",routesAvailable?"揭示此处全部后续航路":"所有后续航路已经记录，没有新水流可标记",routesAvailable||restoring),new VoyageNodeChoice("leave","避开逆流","无资源变化")};
            }
        }
        public bool ChooseNode(string id)
        {
            var choice=GetBasicNodeChoices().FirstOrDefault(x=>x.Id==id&&x.Enabled);
            if(choice==null)return GetSupportChoices().Any(x=>x.Id==id&&x.Enabled&&IsNodeResolutionChoice(x))&&ManageSupport(id);
            if(phase==VoyagePhase.ActComplete)return ChooseNextAct(id);
            string outcome;
            switch(id)
            {
                case "salvage":Health-=5;Supplies+=2;outcome="拆取物资时船板坍塌：补给 +2，生命 -5。";break;
                case "observe":Sanity=Math.Min(30,Sanity+3);outcome="远距确认它只是随流漂移：理智 +3。";break;
                case "tow":Supplies++;elapsedHours+=2;outcome="浮箱拖回船侧：补给 +1，航时 +2。";break;
                case "listen":Sanity=Math.Min(30,Sanity+4);outcome="重复信号来自损坏的自动发报机：理智 +4。";break;
                case "dive":Sanity-=3;Supplies+=2;outcome="锚链下没有幸存者，只有密封物资：补给 +2，理智 -3。";break;
                case "mark":RevealAllRoutes(CurrentFull);outcome="逆流边界已标入海图，所有后续航路可见。";break;
                case "routes":RevealAllRoutes(CurrentFull);outcome="档案潮汐表与当前海况吻合，所有后续航路可见。";AddKnowledge("航路记录："+RegionBriefing(false));break;
                case "briefing":outcome=RegionBriefing(true);AddKnowledge(outcome);break;
                case "seal":Sanity=Math.Min(30,Sanity+5);outcome="记录已封存：理智 +5。";break;
                case "rest":Health=Math.Min(60,Health+12+(ActiveCrew("cook")?3:0));Sanity=Math.Min(30,Sanity+5+(ActiveCrew("medic")?2:0));outcome="完成短时休整与可用船员照护。";break;
                case "repair":SpendSupplies(1);Health=Math.Min(60,Health+24);outcome="消耗1补给完成深度治疗：生命 +24。";break;
                case "calm":SpendSupplies(1);Sanity=Math.Min(30,Sanity+11);outcome="消耗1补给完成长期安抚：理智 +11。";break;
                case "depart":outcome="未消耗补给，完成整备检查。";break;
                case "leave":outcome="保持距离离开，资源未变化。";break;
                default:return false;
            }
            log.Add(CurrentFull.Name+"："+outcome);nodeResolved=true; Record(VoyageActionKind.ChooseNode,value:id); return true;
        }
        public bool ClaimReward(string cardId,string replaceDeckCardId=null)
        {
            if(phase!=VoyagePhase.Reward)return false;
            if(replaceDeckCardId!=null&&(cardId==null||cardId.StartsWith("item:",StringComparison.Ordinal)||!deck.Contains(replaceDeckCardId)||deck.Contains(cardId)||!RewardCards.Any(x=>x.Id==cardId)))return false;
            if(cardId!=null&&cardId.StartsWith("item:",StringComparison.Ordinal)){if(!ClaimItemReward(cardId.Substring(5)))return false;}
            else if(cardId==null){Supplies=Math.Min(8,Supplies+1);} else {var card=RewardCards.FirstOrDefault(x=>x.Id==cardId);if(card==null)return false;learned.Add(card.Id);if(replaceDeckCardId==null)newRewardCards.Add(card.Id);else deck[deck.IndexOf(replaceDeckCardId)]=card.Id;}
            map.CompleteCurrent();
            if(debug)phase=VoyagePhase.Completed;
            else if(CurrentFull.Kind==RunNodeKind.Boss)
            {
                SetPlan(VoyageMapGenerator.Generate(Seed,Act+1,nextRegion));
                nextRegion=null;RegionIntroPending=true;
            }
            else phase=VoyagePhase.ChoosingRoute;
            Record(VoyageActionKind.ClaimReward,value:cardId,values:replaceDeckCardId==null?null:new[]{replaceDeckCardId}); return true;
        }
        public bool SetDeck(IEnumerable<string> ids)
        {
            var values=ids?.ToArray(); if(!CanEditDeck||values==null||values.Length!=12||values.Distinct(StringComparer.Ordinal).Count()!=12||values.Any(x=>!learned.Contains(x)))return false;
            if(deck.SequenceEqual(values,StringComparer.Ordinal)&&newRewardCards.Count==0)return true;deck.Clear();deck.AddRange(values);newRewardCards.Clear();Record(VoyageActionKind.SetDeck,values:values);return true;
        }
        public bool SetTools(IEnumerable<string> ids)
        {
            var values=ids?.ToArray();if(!CanEditDeck||values==null||values.Length+carriedItems.Count(x=>x=="i09")>2||values.Distinct(StringComparer.Ordinal).Count()!=values.Length||values.Any(x=>!learnedTools.Contains(x)))return false;
            if(tools.SequenceEqual(values,StringComparer.Ordinal))return true;tools.Clear();tools.AddRange(values);Record(VoyageActionKind.SetTools,values:values);return true;
        }
        public bool ChooseNextAct(string region)
        {
            if(phase!=VoyagePhase.ActComplete)return false; string next;
            if(Act==1){if(region!="bones"&&region!="inverted"&&region!="fleet")return false;next=region;}else if(Act==2&&(string.IsNullOrEmpty(region)||region=="abyss"))next="abyss";else return false;
            nextRegion=next; phase=VoyagePhase.Reward; Record(VoyageActionKind.ChooseNextAct,value:next); return true;
        }
        public string HeadingTo(VoyageStop target){if(target==null)throw new ArgumentNullException(nameof(target));var dx=target.X-CurrentFull.X;var dy=target.Y-CurrentFull.Y;var angle=Math.Atan2(dy,dx)*180/Math.PI;if(Math.Abs(dx)<.001&&Math.Abs(dy)<.001)return"原地";if(angle>=-22.5&&angle<22.5)return"东";if(angle<67.5&&angle>=22.5)return"东北";if(angle<112.5&&angle>=67.5)return"北";if(angle<157.5&&angle>=112.5)return"西北";if(angle>=157.5||angle< -157.5)return"西";if(angle< -112.5)return"西南";if(angle< -67.5)return"南";return"东南";}

        public VoyageCheckpoint Export()
        {
            if(debug)throw new InvalidOperationException("Debug voyages cannot be saved.");
            return new VoyageCheckpoint{Version=CheckpointVersion,Map=CloneMap(initialMap),CrewId=initialCrew,History=history.Select(CopyAction).ToArray()};
        }
        public static VoyageSession Restore(VoyageCheckpoint checkpoint)
        {
            if(checkpoint==null)throw new ArgumentNullException(nameof(checkpoint));if(checkpoint.Version!=CheckpointVersion||checkpoint.Map==null||checkpoint.History==null||checkpoint.History.Length>MaxHistory)throw new ArgumentException("Invalid voyage checkpoint.",nameof(checkpoint));
            var session=new VoyageSession(VoyageMapGenerator.Restore(checkpoint.Map),checkpoint.CrewId);session.restoring=true;try{foreach(var action in checkpoint.History)session.Replay(action);}finally{session.restoring=false;}return session;
        }
        private void Replay(VoyageActionData action)
        {
            ValidateAction(action);
            bool succeeded;
            switch(action.Kind)
            {
                case VoyageActionKind.Select: succeeded=(phase==VoyagePhase.ChoosingRoute&&selected?.Id==action.StopId)||Select(action.StopId); break;
                case VoyageActionKind.SetSail: succeeded=SetSail(); break;
                case VoyageActionKind.Arrive: succeeded=Arrive(); break;
                case VoyageActionKind.Continue: succeeded=Continue(); break;
                case VoyageActionKind.BeginEncounter: succeeded=BeginEncounter(); break;
                case VoyageActionKind.ResolveEncounter: succeeded=ResolveEncounter(action.Victory,action.Health,action.Sanity,action.Values,int.Parse(action.Value,System.Globalization.CultureInfo.InvariantCulture),action.Evidence,action.HealthDamage,action.SanityDamage); break;
                case VoyageActionKind.DismissDebrief:succeeded=DismissDebrief();break;
                case VoyageActionKind.DismissRegionIntro:succeeded=DismissRegionIntro();break;
                case VoyageActionKind.ManageSupport: succeeded=ManageSupport(action.Value,action.Values?.SingleOrDefault());break;
                case VoyageActionKind.RevealRoutes: succeeded=RevealRoutes(); break;
                case VoyageActionKind.ScoutAhead: succeeded=ScoutAhead(); break;
                case VoyageActionKind.ChooseNode: succeeded=ChooseNode(action.Value); break;
                case VoyageActionKind.ClaimReward: succeeded=ClaimReward(action.Value,action.Values?.SingleOrDefault()); break;
                case VoyageActionKind.SetDeck: succeeded=SetDeck(action.Values); break;
                case VoyageActionKind.SetTools: succeeded=SetTools(action.Values); break;
                case VoyageActionKind.ChooseNextAct: succeeded=ChooseNextAct(action.Value); break;
                default: succeeded=false; break;
            }
            if(!succeeded)throw new ArgumentException("Voyage history contains an illegal transition.");
        }
        private static void ValidateAction(VoyageActionData action)
        {
            if(action==null||!Enum.IsDefined(typeof(VoyageActionKind),action.Kind))throw new ArgumentException("Unknown voyage action.");
            var usesStop=action.Kind==VoyageActionKind.Select;
            var usesOutcome=action.Kind==VoyageActionKind.ResolveEncounter;
            var usesValue=action.Kind==VoyageActionKind.ChooseNode||action.Kind==VoyageActionKind.ClaimReward||action.Kind==VoyageActionKind.ChooseNextAct||usesOutcome||action.Kind==VoyageActionKind.ManageSupport;
            var usesValues=action.Kind==VoyageActionKind.SetDeck||action.Kind==VoyageActionKind.SetTools||usesOutcome||action.Kind==VoyageActionKind.ManageSupport||action.Kind==VoyageActionKind.ClaimReward;
            if(usesStop==string.IsNullOrEmpty(action.StopId)||(!usesOutcome&&(action.Victory||action.Health!=0||action.Sanity!=0))||
                (!usesValue&&action.Value!=null)||(!usesValues&&action.Values!=null)||(!usesOutcome&&(action.Evidence!=null||action.HealthDamage!=null||action.SanityDamage!=null))||usesOutcome&&!int.TryParse(action.Value,out _)||action.Kind==VoyageActionKind.ClaimReward&&action.Values!=null&&(action.Values.Length!=1||string.IsNullOrEmpty(action.Values[0])))
                throw new ArgumentException("Voyage history action has invalid fields.");
        }
        private IReadOnlyList<CardSpec> BuildRewards()
        {
            return CardCatalog.All.Where(card=>!learned.Contains(card.Id)&&UsefulForBoss(card))
                .OrderBy(card=>VoyageSeeds.Derive(Seed,"reward",Act+":"+CurrentFull.Id+":"+card.Id)).Take(CurrentFull.Kind==RunNodeKind.Reward?4:3).ToArray();
        }
        private bool UsefulForBoss(CardSpec card)
        {
            var number=CardCatalog.Number(card.Id);
            if(number>=91)return number==91?runArchive.Any(x=>x.Rule=="source-v1"):number==92||number==93||number==94||number==95&&inventory.Any(x=>x!="i12")||number==96&&markers.ContainsKey("surveyed-beacon");
            if ((Act >= 2 || nextRegion != null) && (number == 76 || number == 83)) return true;
            var boss=nextRegion==null?byId[plan.GoalId].EncounterId:VoyageMapGenerator.BossEncounter(Seed,Act+1,nextRegion);
            return Array.IndexOf(CommonRewards,number)>=0||BossRewards.TryGetValue(boss,out var rewards)&&Array.IndexOf(rewards,number)>=0;
        }
        private string RegionBriefing(bool boss)
        {
            var encounter=byId[plan.GoalId].EncounterId;
            var text=encounter=="fog-visitors"?"漂灯回声无法靠击倒终止：绘制封线基础消耗3行动，每回合一次；现有场景折扣仍可降低费用，累计4段撤离。":
                encounter=="drowned-bell"?"沉钟在敌方结算时推进撤离；巨浪造成21生命伤害，钟鸣造成11理智冲击且可打断，撑过五轮。":
                encounter=="m02"?"唱序约束可逐条公开；按正确顺序关闭三只唱诗匣后本体才会失去护持。":
                encounter=="m03"?"测读盐格只公开相邻八格危险数；激活四枚安全封栓即可完成调查。":
                encounter=="m04"?"先将双继电器校正为公开极性，再依序释放三段闸锁，最后击退执旗者。":
                encounter=="storm-gauntlet"?"黑潮每回合公开18点生命冲击或9点理智冲击，均可用对应防护或打断反制；撑过五轮即可脱离。":
                encounter=="hollow-beacon"?"空心航标本体有48生命；公开的10点生命攻击或5点理智冲击均可打断，击破本体获胜。":
                "本体每回合造成2点理智冲击；用遭遇牌分批召唤五名守卫，击退全部守卫即可获胜。";
            return (boss?"幕末威胁简报：":"海况结论：")+text;
        }
        private void AddKnowledge(string entry){if(!knowledge.Contains(entry))knowledge.Add(entry);}
        private void SpendSupplies(int amount){Supplies-=amount;spentSupplies+=amount;}
        private void FinishNode(){map.CompleteCurrent();phase=map.IsFinished?VoyagePhase.Completed:VoyagePhase.ChoosingRoute;}
        private void RevealInitialRoutes(){foreach(var id in CurrentFull.Next)RevealRoute(CurrentFull.Id,id);}
        private void RevealDefaultRoutes(VoyageStop stop){foreach(var id in stop.Next.Take(2))RevealRoute(stop.Id,id);}
        private void RevealAllRoutes(VoyageStop stop){foreach(var id in stop.Next)RevealRoute(stop.Id,id);}
        private void RevealRoute(string from,string to){knownRouteKeys.Add(RouteKey(from,to));knownStopIds.Add(to);}
        private VoyageStop CurrentFull=>byId[map.CurrentId];
        private VoyageStop ToKnownStop(VoyageStop s)=>new VoyageStop(s.Id,s.Name,s.Clue,s.Description,s.X,s.Y,s.TravelHours,s.Kind,s.EncounterId,s.Next.Where(x=>knownRouteKeys.Contains(RouteKey(s.Id,x))),s.Layer,s.BattleSeed);
        private bool HasUnknownRoutes(VoyageStop s)=>s.Next.Any(x=>!knownRouteKeys.Contains(RouteKey(s.Id,x)));
        private bool IsKnownRouteFromCurrent(string id)=>knownRouteKeys.Contains(RouteKey(CurrentFull.Id,id));
        private static string RouteKey(string a,string b)=>a+"\n"+b; private static bool IsEncounter(VoyageStop s)=>s.Kind==RunNodeKind.Battle||s.Kind==RunNodeKind.Boss;
        private void Record(VoyageActionKind kind,string stopId=null,bool victory=false,int health=0,int sanity=0,string value=null,string[] values=null)
        {
            if(kind==VoyageActionKind.Select||kind==VoyageActionKind.SetDeck||kind==VoyageActionKind.SetTools)
                for(var i=history.Count-1;i>=0;i--)
                {
                    var previous=history[i];
                    if(previous.Kind!=VoyageActionKind.Select&&previous.Kind!=VoyageActionKind.SetDeck&&previous.Kind!=VoyageActionKind.SetTools)break;
                    if(previous.Kind!=kind)continue;
                    previous.StopId=stopId;previous.Values=values?.ToArray();return;
                }
            history.Add(new VoyageActionData{Kind=kind,StopId=stopId,Victory=victory,Health=health,Sanity=sanity,Value=value,Values=values?.ToArray()});
        }
        private static VoyageActionData CopyAction(VoyageActionData a)=>new VoyageActionData{Kind=a.Kind,StopId=a.StopId,Victory=a.Victory,Health=a.Health,Sanity=a.Sanity,Value=a.Value,Values=a.Values?.ToArray(),Evidence=a.Evidence?.ToArray(),HealthDamage=a.HealthDamage,SanityDamage=a.SanityDamage};
        private static VoyageMapData CloneMap(VoyageMapData data)=>VoyageMapGenerator.Restore(data).Export();
    }
}
