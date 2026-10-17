using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public enum VoyagePhase { ChoosingRoute, Sailing, Arrived, Encounter, Reward, ActComplete, Completed, Failed }
    public enum VoyageActionKind { Select, SetSail, Arrive, Continue, BeginEncounter, ResolveEncounter, RevealRoutes, ScoutAhead, ChooseNode, ClaimReward, SetDeck, SetTools, ChooseNextAct }

    [Serializable] public sealed class VoyageActionData
    {
        public VoyageActionKind Kind; public string StopId; public bool Victory; public int Health; public int Sanity; public string Value; public string[] Values;
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

    public sealed class VoyageSession
    {
        public const int CheckpointVersion=6;
        public const int ActLength=11;
        private const int InitialToolUses=2, MaxHistory=512;
        private static readonly string[] StarterDeck={"revolver","contradiction","c012","cover","steady","intuition","bandage","notes","light","c041","tear","c071"};
        private static readonly string[] InitialTools={"lantern","listener","shell"};
        private static readonly int[] CommonRewards = { 3, 5, 7, 9, 14, 16, 18, 23, 24, 26, 27, 28, 29, 31, 32, 33, 35, 38, 40, 45, 49, 50, 51, 53, 54 };
        private static readonly Dictionary<string, int[]> RegionRewards = new Dictionary<string, int[]>
        {
            ["fog"] = new[] { 6, 11, 16, 25, 32, 36, 47, 48, 58, 64, 70 },
            ["bones"] = new[] { 4, 8, 13, 15, 34, 46, 56, 61, 62, 68, 78, 88 },
            ["inverted"] = new[] { 25, 36, 43, 44, 60, 67, 75, 77 },
            ["fleet"] = new[] { 17, 25, 32, 57, 60, 63, 65, 70, 76, 79, 85 },
            ["abyss"] = new[] { 4, 8, 10, 13, 34, 46, 52, 62, 66, 69, 75, 81, 82, 84, 88, 90 }
        };
        private VoyagePlan plan; private Dictionary<string,VoyageStop> byId; private RunMap map;
        private readonly VoyageMapData initialMap; private readonly string initialCrew;
        private readonly List<string> visited=new List<string>(), log=new List<string>(), knowledge=new List<string>();
        private readonly List<VoyageActionData> history=new List<VoyageActionData>();
        private readonly HashSet<string> knownStopIds=new HashSet<string>(StringComparer.Ordinal), knownRouteKeys=new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> deck=new List<string>(StarterDeck), learned=new List<string>(StarterDeck);
        private readonly List<string> tools=new List<string>{"lantern","listener"}, learnedTools=new List<string>(InitialTools);
        private VoyageStop selected; private VoyagePhase phase; private int elapsedHours; private bool nodeResolved; private bool initialPreparation=true;
        private string nextRegion;
        private bool debug;
        private VoyageSession(VoyagePlan first,string crew)
        {
            if(crew!="medic"&&crew!="listener") throw new ArgumentException("Unknown initial crew.",nameof(crew));
            initialMap=first.Export(); initialCrew=crew; CrewId=crew; Health=60; Sanity=30; Supplies=3; SetPlan(first);
        }
        private VoyageSession(VoyagePlan plan,string crew,bool isDebug) : this(plan,crew)
        {
            debug=isDebug;
            phase=plan.Stops[0].Kind==RunNodeKind.Reward?VoyagePhase.Reward:VoyagePhase.Arrived;
            nodeResolved=false;
        }
        private void SetPlan(VoyagePlan value)
        {
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
        public bool BeginEncounter()
        {
            if(phase!=VoyagePhase.Arrived||!IsEncounter(CurrentFull)||string.IsNullOrEmpty(CurrentFull.EncounterId))return false;
            phase=VoyagePhase.Encounter; Record(VoyageActionKind.BeginEncounter); return true;
        }
        public bool ResolveEncounter(bool victory,int health,int sanity)
        {
            if(phase!=VoyagePhase.Encounter||health<0||health>60||sanity<0||sanity>30)return false;
            Health=health; Sanity=sanity;
            if(!victory||health==0||sanity==0) phase=VoyagePhase.Failed;
            else if(debug) phase=CurrentFull.Kind==RunNodeKind.Boss?VoyagePhase.Completed:VoyagePhase.Reward;
            else if(CurrentFull.Kind==RunNodeKind.Boss) phase=Act==3?VoyagePhase.Completed:VoyagePhase.ActComplete;
            else phase=VoyagePhase.Reward;
            Record(VoyageActionKind.ResolveEncounter,victory:victory,health:health,sanity:sanity); return true;
        }
        public bool RevealRoutes(){if(!CanRevealRoutes)return false;RevealAllRoutes(CurrentFull);ChartUses--;Record(VoyageActionKind.RevealRoutes);return true;}
        public bool ScoutAhead(){if(!CanScoutAhead)return false;RevealAllRoutes(selected);LookoutUses--;Record(VoyageActionKind.ScoutAhead);return true;}

        public IReadOnlyList<VoyageNodeChoice> GetNodeChoices()
        {
            if(phase==VoyagePhase.ActComplete && Act==1) return new[]{new VoyageNodeChoice("bones","尸骸之海","巨型鲸骸与召唤媒介"),new VoyageNodeChoice("inverted","倒悬潮庭","盐格推理与理智压力"),new VoyageNodeChoice("fleet","无灯裂海","舰队装置与承压作业")};
            if(phase!=VoyagePhase.Arrived||nodeResolved)return Array.Empty<VoyageNodeChoice>();
            switch(CurrentFull.Kind)
            {
                case RunNodeKind.Event:return EventChoices();
                case RunNodeKind.Archive:return new[]{new VoyageNodeChoice("routes","抄录航路","揭示此处全部后续航路，并把海况结论写入日志"),new VoyageNodeChoice("briefing","研读威胁简报","记录本海域区域威胁的真实机制线索"),new VoyageNodeChoice("seal","封存记录","恢复5理智",Sanity<30)};
                case RunNodeKind.Supply:return new[]{new VoyageNodeChoice("rest","休整","恢复12生命与5理智",Health<60||Sanity<30),new VoyageNodeChoice("repair","深度治疗","消耗1补给，恢复24生命",Supplies>0&&Health<60&&!CurrentFull.Id.EndsWith("n1",StringComparison.Ordinal)),new VoyageNodeChoice("calm","长期安抚","消耗1补给，恢复11理智",Supplies>0&&Sanity<30&&!CurrentFull.Id.EndsWith("n0",StringComparison.Ordinal)),new VoyageNodeChoice("depart","不作整备","继续航行")};
                default:return Array.Empty<VoyageNodeChoice>();
            }
        }
        private IReadOnlyList<VoyageNodeChoice> EventChoices()
        {
            switch(VoyageSeeds.Derive(Seed,"event",Act+":"+CurrentFull.Id)%3u)
            {
                case 0:return new[]{new VoyageNodeChoice("salvage","登船拆取","获得2补给，失去5生命",Health>5&&Supplies<=6),new VoyageNodeChoice("observe","绕船观测","恢复3理智",Sanity<30),new VoyageNodeChoice("leave","保持航向","无资源变化")};
                case 1:return new[]{new VoyageNodeChoice("tow","拖回浮箱","获得1补给，耗时2小时",Supplies<8),new VoyageNodeChoice("listen","核对求救频率","恢复4理智",Sanity<30),new VoyageNodeChoice("leave","切断信号","无资源变化")};
                default:return new[]{new VoyageNodeChoice("dive","下潜检查锚链","获得2补给，失去3理智",Sanity>3&&Supplies<=6),new VoyageNodeChoice("mark","标记危险水流","揭示此处全部后续航路"),new VoyageNodeChoice("leave","避开逆流","无资源变化")};
            }
        }
        public bool ChooseNode(string id)
        {
            var choice=GetNodeChoices().FirstOrDefault(x=>x.Id==id&&x.Enabled); if(choice==null)return false;
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
                case "rest":Health=Math.Min(60,Health+12);Sanity=Math.Min(30,Sanity+5);outcome="完成短时休整：生命 +12，理智 +5。";break;
                case "repair":Supplies--;Health=Math.Min(60,Health+24);outcome="消耗1补给完成深度治疗：生命 +24。";break;
                case "calm":Supplies--;Sanity=Math.Min(30,Sanity+11);outcome="消耗1补给完成长期安抚：理智 +11。";break;
                case "depart":outcome="未消耗补给，完成整备检查。";break;
                case "leave":outcome="保持距离离开，资源未变化。";break;
                default:return false;
            }
            log.Add(CurrentFull.Name+"："+outcome);nodeResolved=true; Record(VoyageActionKind.ChooseNode,value:id); return true;
        }
        public bool ClaimReward(string cardId)
        {
            if(phase!=VoyagePhase.Reward)return false;
            if(cardId==null)Supplies=Math.Min(8,Supplies+1); else {var card=RewardCards.FirstOrDefault(x=>x.Id==cardId);if(card==null)return false;learned.Add(card.Id);}
            map.CompleteCurrent();
            if(debug)phase=VoyagePhase.Completed;
            else if(CurrentFull.Kind==RunNodeKind.Boss)
            {
                SetPlan(VoyageMapGenerator.Generate(Seed,Act+1,nextRegion));
                nextRegion=null;
            }
            else phase=VoyagePhase.ChoosingRoute;
            Record(VoyageActionKind.ClaimReward,value:cardId); return true;
        }
        public bool SetDeck(IEnumerable<string> ids)
        {
            var values=ids?.ToArray(); if(!CanEditDeck||values==null||values.Length!=12||values.Distinct(StringComparer.Ordinal).Count()!=12||values.Any(x=>!learned.Contains(x)))return false;
            deck.Clear();deck.AddRange(values);Record(VoyageActionKind.SetDeck,values:values);return true;
        }
        public bool SetTools(IEnumerable<string> ids)
        {
            var values=ids?.ToArray();if(!CanEditDeck||values==null||values.Length>2||values.Distinct(StringComparer.Ordinal).Count()!=values.Length||values.Any(x=>!learnedTools.Contains(x)))return false;
            tools.Clear();tools.AddRange(values);Record(VoyageActionKind.SetTools,values:values);return true;
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
            var session=new VoyageSession(VoyageMapGenerator.Restore(checkpoint.Map),checkpoint.CrewId);foreach(var action in checkpoint.History)session.Replay(action);return session;
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
                case VoyageActionKind.ResolveEncounter: succeeded=ResolveEncounter(action.Victory,action.Health,action.Sanity); break;
                case VoyageActionKind.RevealRoutes: succeeded=RevealRoutes(); break;
                case VoyageActionKind.ScoutAhead: succeeded=ScoutAhead(); break;
                case VoyageActionKind.ChooseNode: succeeded=ChooseNode(action.Value); break;
                case VoyageActionKind.ClaimReward: succeeded=ClaimReward(action.Value); break;
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
            var usesValue=action.Kind==VoyageActionKind.ChooseNode||action.Kind==VoyageActionKind.ClaimReward||action.Kind==VoyageActionKind.ChooseNextAct;
            var usesValues=action.Kind==VoyageActionKind.SetDeck||action.Kind==VoyageActionKind.SetTools;
            if(usesStop==string.IsNullOrEmpty(action.StopId)||(!usesOutcome&&(action.Victory||action.Health!=0||action.Sanity!=0))||
                (!usesValue&&action.Value!=null)||(!usesValues&&action.Values!=null))
                throw new ArgumentException("Voyage history action has invalid fields.");
        }
        private IReadOnlyList<CardSpec> BuildRewards()
        {
            return CardCatalog.All.Where(card=>!learned.Contains(card.Id)&&UsefulInRegion(card))
                .OrderBy(card=>VoyageSeeds.Derive(Seed,"reward",Act+":"+CurrentFull.Id+":"+card.Id)).Take(CurrentFull.Kind==RunNodeKind.Reward?4:3).ToArray();
        }
        private bool UsefulInRegion(CardSpec card)
        {
            var number=CardCatalog.Number(card.Id);
            if ((Act >= 2 || nextRegion != null) && (number == 76 || number == 83)) return true;
            return Array.IndexOf(CommonRewards, number) >= 0 || Array.IndexOf(RegionRewards[nextRegion??RegionId], number) >= 0;
        }
        private string RegionBriefing(bool boss)
        {
            var text=RegionId=="fog"?"灰雾访客的封线目标不要求击倒显现体：每回合可支付1行动刻一段封线，累计4段获胜。":
                RegionId=="bones"?"唱序约束可逐条公开；按正确顺序关闭三只唱诗匣后本体才会失去护持。":
                RegionId=="inverted"?"测读盐格只公开相邻八格危险数；激活四枚安全封栓即可完成调查。":
                RegionId=="fleet"?"先将双继电器校正为公开极性，再依序释放三段闸锁，最后击退执旗者。":
                "先建立撤离绳，再牵引守卫并关闭五座锚点，最后收绞两次归航绳。";
            return (boss?"区域威胁简报：":"海况结论：")+text;
        }
        private void AddKnowledge(string entry){if(!knowledge.Contains(entry))knowledge.Add(entry);}
        private void FinishNode(){map.CompleteCurrent();phase=map.IsFinished?VoyagePhase.Completed:VoyagePhase.ChoosingRoute;}
        private void RevealInitialRoutes(){foreach(var id in CurrentFull.Next)RevealRoute(CurrentFull.Id,id);}
        private void RevealDefaultRoutes(VoyageStop stop){if(stop.Next.Count>0)RevealRoute(stop.Id,stop.Next[(int)(VoyageSeeds.Derive(Seed,"visibility",stop.Id)%stop.Next.Count)]);}
        private void RevealAllRoutes(VoyageStop stop){foreach(var id in stop.Next)RevealRoute(stop.Id,id);}
        private void RevealRoute(string from,string to){knownRouteKeys.Add(RouteKey(from,to));knownStopIds.Add(to);}
        private VoyageStop CurrentFull=>byId[map.CurrentId];
        private VoyageStop ToKnownStop(VoyageStop s)=>new VoyageStop(s.Id,s.Name,s.Clue,s.Description,s.X,s.Y,s.TravelHours,s.Kind,s.EncounterId,s.Next.Where(x=>knownRouteKeys.Contains(RouteKey(s.Id,x))),s.Layer,s.BattleSeed);
        private bool HasUnknownRoutes(VoyageStop s)=>s.Next.Any(x=>!knownRouteKeys.Contains(RouteKey(s.Id,x)));
        private bool IsKnownRouteFromCurrent(string id)=>knownRouteKeys.Contains(RouteKey(CurrentFull.Id,id));
        private static string RouteKey(string a,string b)=>a+"\n"+b; private static bool IsEncounter(VoyageStop s)=>s.Kind==RunNodeKind.Battle||s.Kind==RunNodeKind.Boss;
        private void Record(VoyageActionKind kind,string stopId=null,bool victory=false,int health=0,int sanity=0,string value=null,string[] values=null)
        {if(kind==VoyageActionKind.Select&&history.Count>0&&history[history.Count-1].Kind==VoyageActionKind.Select){history[history.Count-1].StopId=stopId;return;}history.Add(new VoyageActionData{Kind=kind,StopId=stopId,Victory=victory,Health=health,Sanity=sanity,Value=value,Values=values?.ToArray()});}
        private static VoyageActionData CopyAction(VoyageActionData a)=>new VoyageActionData{Kind=a.Kind,StopId=a.StopId,Victory=a.Victory,Health=a.Health,Sanity=a.Sanity,Value=a.Value,Values=a.Values?.ToArray()};
        private static VoyageMapData CloneMap(VoyageMapData data)=>VoyageMapGenerator.Restore(data).Export();
    }
}
