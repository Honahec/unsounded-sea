using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public enum VoyagePhase { ChoosingRoute, Sailing, Arrived, Encounter, Completed, Failed }

    public enum VoyageActionKind
    {
        Select,
        SetSail,
        Arrive,
        Continue,
        BeginEncounter,
        ResolveEncounter,
        RevealRoutes,
        ScoutAhead
    }

    [Serializable]
    public sealed class VoyageActionData
    {
        public VoyageActionKind Kind;
        public string StopId;
        public bool Victory;
    }

    [Serializable]
    public sealed class VoyageCheckpoint
    {
        public int Version;
        public VoyageMapData Map;
        public VoyageActionData[] History;
    }

    public sealed class VoyageSession
    {
        public const int CheckpointVersion = 2;
        private const int InitialToolUses = 2;
        private const int MaxHistory = 512;
        private readonly VoyagePlan plan;
        private readonly Dictionary<string, VoyageStop> byId;
        private readonly RunMap map;
        private readonly List<string> visited = new List<string>();
        private readonly List<string> log = new List<string>();
        private readonly List<VoyageActionData> history = new List<VoyageActionData>();
        private readonly HashSet<string> knownStopIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> knownRouteKeys = new HashSet<string>(StringComparer.Ordinal);
        private VoyageStop selected;
        private VoyagePhase phase;
        private int elapsedHours;

        private VoyageSession(VoyagePlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            byId = plan.Stops.ToDictionary(x => x.Id, StringComparer.Ordinal);
            map = new RunMap(plan.Stops.Select(x => new RunNode(x.Id, x.Kind, x.Clue,
                string.IsNullOrEmpty(x.EncounterId) ? x.Id : x.EncounterId, x.Next)), plan.StartId);
            visited.Add(plan.StartId);
            knownStopIds.Add(plan.StartId);
            map.CompleteCurrent();
            phase = VoyagePhase.ChoosingRoute;
            ChartUses = InitialToolUses;
            LookoutUses = InitialToolUses;
            RevealInitialRoutes();
            log.Add($"{CurrentFull.Name}：已记录出发位置，等待选定航向。");
        }

        public uint Seed => plan.Seed;
        public IReadOnlyList<VoyageStop> KnownStops => plan.Stops.Where(x => knownStopIds.Contains(x.Id)).Select(ToKnownStop).ToList().AsReadOnly();
        public IReadOnlyList<VoyageRoute> KnownRoutes => plan.Stops
            .SelectMany(x => x.Next.Where(y => knownRouteKeys.Contains(RouteKey(x.Id, y))).Select(y => new VoyageRoute(x.Id, y)))
            .ToList().AsReadOnly();
        public IReadOnlyList<VoyageStop> Choices => phase == VoyagePhase.ChoosingRoute
            ? CurrentFull.Next.Where(IsKnownRouteFromCurrent).Where(x => !visited.Contains(x)).Select(x => ToKnownStop(byId[x])).ToList().AsReadOnly()
            : new List<VoyageStop>().AsReadOnly();
        public VoyageStop Current => ToKnownStop(CurrentFull);
        public VoyageStop Selected => selected == null ? null : ToKnownStop(selected);
        public VoyagePhase Phase => phase;
        public int ElapsedHours => elapsedHours;
        public int LegsSailed => visited.Count - 1;
        public IReadOnlyList<string> Visited => visited.ToList().AsReadOnly();
        public IReadOnlyList<string> Log => log.ToList().AsReadOnly();
        public int ChartUses { get; private set; }
        public int LookoutUses { get; private set; }
        public bool CanRevealRoutes => phase == VoyagePhase.ChoosingRoute && ChartUses > 0 && HasUnknownRoutes(CurrentFull);
        public bool CanScoutAhead => phase == VoyagePhase.ChoosingRoute && selected != null && LookoutUses > 0 && HasUnknownRoutes(selected);

        public static VoyageSession Create(uint seed) => new VoyageSession(VoyageMapGenerator.Generate(seed));

        public bool Select(string stopId)
        {
            if (phase != VoyagePhase.ChoosingRoute || string.IsNullOrEmpty(stopId)) return false;
            if (selected?.Id == stopId) return false;
            var choice = Choices.FirstOrDefault(x => string.Equals(x.Id, stopId, StringComparison.Ordinal));
            if (choice == null) return false;
            selected = byId[choice.Id];
            Record(VoyageActionKind.Select, stopId);
            return true;
        }

        public bool SetSail()
        {
            if (phase != VoyagePhase.ChoosingRoute || selected == null || !Choices.Any(x => x.Id == selected.Id)) return false;
            phase = VoyagePhase.Sailing;
            log.Add($"离开{CurrentFull.Name}，向{HeadingTo(selected)}航行，预计 {selected.TravelHours} 小时。 ");
            Record(VoyageActionKind.SetSail);
            return true;
        }

        public bool Arrive()
        {
            if (phase != VoyagePhase.Sailing || selected == null) return false;
            var destination = selected;
            if (!map.TryMove(destination.Id)) return false;
            visited.Add(destination.Id);
            elapsedHours += destination.TravelHours;
            selected = null;
            phase = VoyagePhase.Arrived;
            RevealDefaultRoutes(destination);
            log.Add($"抵达{destination.Name}：{destination.Description}");
            Record(VoyageActionKind.Arrive);
            return true;
        }

        public bool Continue()
        {
            if (phase != VoyagePhase.Arrived || IsEncounter(CurrentFull)) return false;
            map.CompleteCurrent();
            log.Add($"已在海图标记{CurrentFull.Name}。");
            phase = map.IsFinished ? VoyagePhase.Completed : VoyagePhase.ChoosingRoute;
            Record(VoyageActionKind.Continue);
            return true;
        }

        public bool BeginEncounter()
        {
            if (phase != VoyagePhase.Arrived || !IsEncounter(CurrentFull) || string.IsNullOrEmpty(CurrentFull.EncounterId)) return false;
            phase = VoyagePhase.Encounter;
            log.Add($"接近{CurrentFull.Name}，进入遭遇调查。");
            Record(VoyageActionKind.BeginEncounter);
            return true;
        }

        public bool ResolveEncounter(bool victory)
        {
            if (phase != VoyagePhase.Encounter) return false;
            if (!victory)
            {
                phase = VoyagePhase.Failed;
                log.Add($"航程止于{CurrentFull.Name}。");
                Record(VoyageActionKind.ResolveEncounter, victory: false);
                return true;
            }
            map.CompleteCurrent();
            log.Add($"已完成{CurrentFull.Name}的调查并更新海图。");
            phase = map.IsFinished ? VoyagePhase.Completed : VoyagePhase.ChoosingRoute;
            Record(VoyageActionKind.ResolveEncounter, victory: true);
            return true;
        }

        public bool RevealRoutes()
        {
            if (!CanRevealRoutes) return false;
            RevealAllRoutes(CurrentFull);
            ChartUses--;
            log.Add($"海图标出了{CurrentFull.Name}附近原本隐没的航线。");
            Record(VoyageActionKind.RevealRoutes);
            return true;
        }

        public bool ScoutAhead()
        {
            if (!CanScoutAhead) return false;
            RevealAllRoutes(selected);
            LookoutUses--;
            log.Add($"瞭望手报告了{selected.Name}之后可见的水路。");
            Record(VoyageActionKind.ScoutAhead);
            return true;
        }

        public string HeadingTo(VoyageStop target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var dx = target.X - CurrentFull.X;
            var dy = target.Y - CurrentFull.Y;
            if (Math.Abs(dx) < 0.001f && Math.Abs(dy) < 0.001f) return "原地";
            var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            if (angle >= -22.5 && angle < 22.5) return "东";
            if (angle >= 22.5 && angle < 67.5) return "东北";
            if (angle >= 67.5 && angle < 112.5) return "北";
            if (angle >= 112.5 && angle < 157.5) return "西北";
            if (angle >= 157.5 || angle < -157.5) return "西";
            if (angle >= -157.5 && angle < -112.5) return "西南";
            if (angle >= -112.5 && angle < -67.5) return "南";
            return "东南";
        }

        public VoyageCheckpoint Export() => new VoyageCheckpoint
        {
            Version = CheckpointVersion,
            Map = plan.Export(),
            History = history.Select(CopyAction).ToArray()
        };

        public static VoyageSession Restore(VoyageCheckpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            if (checkpoint.Version != CheckpointVersion) throw new ArgumentException("Unsupported voyage checkpoint version.", nameof(checkpoint));
            if (checkpoint.Map == null) throw new ArgumentException("Voyage checkpoint map is required.", nameof(checkpoint));
            if (checkpoint.History == null || checkpoint.History.Length > MaxHistory)
                throw new ArgumentException("Voyage checkpoint history is invalid.", nameof(checkpoint));

            var session = new VoyageSession(VoyageMapGenerator.Restore(checkpoint.Map));
            foreach (var action in checkpoint.History)
                session.Replay(action);
            return session;
        }

        private void Replay(VoyageActionData action)
        {
            if (action == null || !Enum.IsDefined(typeof(VoyageActionKind), action.Kind))
                throw new ArgumentException("Voyage history contains an unknown action.");
            var needsStop = action.Kind == VoyageActionKind.Select;
            var usesVictory = action.Kind == VoyageActionKind.ResolveEncounter;
            if (needsStop != !string.IsNullOrEmpty(action.StopId) || (!usesVictory && action.Victory))
                throw new ArgumentException("Voyage history action has invalid fields.");

            var succeeded = false;
            switch (action.Kind)
            {
                case VoyageActionKind.Select: succeeded = Select(action.StopId); break;
                case VoyageActionKind.SetSail: succeeded = SetSail(); break;
                case VoyageActionKind.Arrive: succeeded = Arrive(); break;
                case VoyageActionKind.Continue: succeeded = Continue(); break;
                case VoyageActionKind.BeginEncounter: succeeded = BeginEncounter(); break;
                case VoyageActionKind.ResolveEncounter: succeeded = ResolveEncounter(action.Victory); break;
                case VoyageActionKind.RevealRoutes: succeeded = RevealRoutes(); break;
                case VoyageActionKind.ScoutAhead: succeeded = ScoutAhead(); break;
            }
            if (!succeeded) throw new ArgumentException("Voyage history contains an illegal transition.");
        }

        private void RevealInitialRoutes()
        {
            foreach (var targetId in CurrentFull.Next.Take(2)) RevealRoute(CurrentFull.Id, targetId);
        }

        private void RevealDefaultRoutes(VoyageStop stop)
        {
            if (stop.Next.Count == 0) return;
            var index = (int)(VoyageSeeds.Derive(Seed, "visibility", stop.Id) % (uint)stop.Next.Count);
            RevealRoute(stop.Id, stop.Next[index]);
        }

        private void RevealAllRoutes(VoyageStop stop)
        {
            foreach (var targetId in stop.Next) RevealRoute(stop.Id, targetId);
        }

        private void RevealRoute(string fromId, string toId)
        {
            knownRouteKeys.Add(RouteKey(fromId, toId));
            knownStopIds.Add(toId);
        }

        private VoyageStop CurrentFull => byId[map.CurrentId];

        private VoyageStop ToKnownStop(VoyageStop stop) => new VoyageStop(stop.Id, stop.Name, stop.Clue, stop.Description,
            stop.X, stop.Y, stop.TravelHours, stop.Kind, stop.EncounterId,
            stop.Next.Where(x => knownRouteKeys.Contains(RouteKey(stop.Id, x))), stop.Layer, stop.BattleSeed);

        private bool HasUnknownRoutes(VoyageStop stop) => stop.Next.Any(x => !knownRouteKeys.Contains(RouteKey(stop.Id, x)));
        private bool IsKnownRouteFromCurrent(string targetId) => knownRouteKeys.Contains(RouteKey(CurrentFull.Id, targetId));
        private static string RouteKey(string fromId, string toId) => fromId + "\n" + toId;
        private static bool IsEncounter(VoyageStop stop) => stop.Kind == RunNodeKind.Battle || stop.Kind == RunNodeKind.Boss;

        private void Record(VoyageActionKind kind, string stopId = null, bool victory = false)
        {
            if (kind == VoyageActionKind.Select && history.Count > 0 && history[history.Count - 1].Kind == VoyageActionKind.Select)
            {
                history[history.Count - 1].StopId = stopId;
                return;
            }
            history.Add(new VoyageActionData { Kind = kind, StopId = stopId, Victory = victory });
        }

        private static VoyageActionData CopyAction(VoyageActionData action) => new VoyageActionData
        {
            Kind = action.Kind,
            StopId = action.StopId,
            Victory = action.Victory
        };
    }
}
