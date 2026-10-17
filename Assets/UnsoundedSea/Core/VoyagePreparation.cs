using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public sealed partial class VoyageSession
    {
        private static readonly Dictionary<string,string[]> BossPreparation = new Dictionary<string,string[]>(StringComparer.Ordinal)
        {
            ["fog-visitors"] = new[] { "i14" },
            ["drowned-bell"] = new[] { "i11" },
            ["m02"] = new[] { "i15" },
            ["m03"] = new[] { "i06", "i16" },
            ["m04"] = new[] { "i08", "i10" },
            ["storm-gauntlet"] = new[] { "i10", "i11" },
            ["hollow-beacon"] = new[] { "i13" },
            ["m05"] = new[] { "i07", "i09" }
        };

        public string PreparationBriefing
        {
            get
            {
                var boss = byId[plan.GoalId].EncounterId;
                if(!BossPreparation.TryGetValue(boss,out var preparation))return "当前调试航点没有完整幕末威胁；按已公开服务整备。";
                var offers = preparation.Select(id => SupportCatalog.Item(id).Name);
                return "威胁："+RegionBriefing(true).Replace("幕末威胁简报：","")+"\n准备："+string.Join("、", offers)+"（可选）\n基础解法：无需购买或消耗筹备物资。";
            }
        }

        public string PreviewStop(string stopId)
        {
            var stop = PreviewableStop(stopId);
            if(!KnownReachable(stopId))
                return visited.Contains(stopId)?stop.Name+"｜已访问\n征兆："+stop.Clue:stop.Name+"｜当前航路不可达";
            var services = stop.Kind == RunNodeKind.Supply ? "交易、招募、休整" : stop.Kind == RunNodeKind.Archive ? "航路、威胁简报、档案" : stop.Kind == RunNodeKind.Event ? "营救、物资或风险取舍" : stop.Kind == RunNodeKind.Reward ? "独立奖励" : stop.Kind == RunNodeKind.Boss ? "幕末威胁" : "普通接舷威胁";
            var details=stop.Kind==RunNodeKind.Supply
                ? "库存："+string.Join("、",ShopStock(stop).Select(x=>x.Name))+"\n候选船员："+string.Join("、",CrewStock(stop).Select(x=>x.Name))
                : stop.Kind==RunNodeKind.Event?"营救候选："+(RecruitCandidateAt(stop) is string candidate?SupportCatalog.CrewMember(candidate).Name:"无"):"威胁："+stop.Clue;
            return stop.Name+"｜"+services+"\n"+details;
        }

        public IReadOnlyList<ItemSpec> PreviewShopItems(string stopId)
        {
            var stop = PreviewableStop(stopId);
            return KnownReachable(stopId)&&stop.Kind == RunNodeKind.Supply ? ShopStock(stop) : Array.Empty<ItemSpec>();
        }

        public IReadOnlyList<CrewSupportSpec> PreviewCrew(string stopId)
        {
            var stop = PreviewableStop(stopId);
            return KnownReachable(stopId)&&stop.Kind == RunNodeKind.Supply ? CrewStock(stop) : Array.Empty<CrewSupportSpec>();
        }

        private VoyageStop PreviewableStop(string stopId)
        {
            if (string.IsNullOrEmpty(stopId) || !knownStopIds.Contains(stopId) || !byId.TryGetValue(stopId, out var stop))
                throw new ArgumentException("航点尚未公开。", nameof(stopId));
            return stop;
        }

        private bool KnownReachable(string stopId)
        {
            if (stopId == CurrentFull.Id) return true;
            var open = new Queue<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { CurrentFull.Id };
            open.Enqueue(CurrentFull.Id);
            while (open.Count > 0)
            {
                var from = open.Dequeue();
                foreach (var to in byId[from].Next.Where(x => knownRouteKeys.Contains(RouteKey(from, x))))
                {
                    if (!seen.Add(to)) continue;
                    if (to == stopId) return true;
                    open.Enqueue(to);
                }
            }
            return false;
        }

        private IReadOnlyList<ItemSpec> ShopStock(VoyageStop stop)
        {
            var bossItems = stop.Layer == VoyageMapGenerator.Layers - 2 && BossPreparation.TryGetValue(byId[plan.GoalId].EncounterId,out var preparation) ? preparation : Array.Empty<string>();
            var ordered = RegionItems().Concat(bossItems).Distinct(StringComparer.Ordinal)
                .Where(id => SupportCatalog.Item(id).Kind == ItemKind.Consumable || !ownedEquipment.Contains(id) && !learnedTools.Contains(id))
                .OrderBy(id => VoyageSeeds.Derive(Seed, "shop", Act + ":" + stop.Id + ":" + id)).ToList();
            var selected = ordered.GroupBy(id => SupportCatalog.Item(id).Kind).SelectMany(group => group.Take(2)).ToList();
            foreach(var id in bossItems)if(ordered.Contains(id)&&!selected.Contains(id))selected.Add(id);
            return selected.Select(SupportCatalog.Item).ToArray();
        }

        private IReadOnlyList<CrewSupportSpec> CrewStock(VoyageStop stop)
        {
            var ordered=SupportCatalog.Crew.Where(x=>!crewIds.Contains(x.Id)).OrderBy(x=>VoyageSeeds.Derive(Seed,"hire",Act+":"+stop.Id+":"+x.Id)).ToList();
            return ordered.Where(x=>x.Id=="medic"||x.Id=="listener").Concat(ordered.Where(x=>x.Id!="medic"&&x.Id!="listener")).Take(3).ToArray();
        }
    }
}
