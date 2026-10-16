using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace UnsoundedSea.Core
{
    public sealed class VoyageStop
    {
        public string Id { get; }
        public string Name { get; }
        public string Clue { get; }
        public string Description { get; }
        public float X { get; }
        public float Y { get; }
        public int TravelHours { get; }
        public RunNodeKind Kind { get; }
        public string EncounterId { get; }
        public IReadOnlyList<string> Next { get; }
        public int Layer { get; }
        public uint BattleSeed { get; }

        public VoyageStop(string id, string name, string clue, string description, float x, float y,
            int travelHours, RunNodeKind kind, string encounterId, IEnumerable<string> next, int layer, uint battleSeed)
        {
            Id = id; Name = name; Clue = clue; Description = description; X = x; Y = y;
            TravelHours = travelHours; Kind = kind; EncounterId = encounterId;
            Next = new ReadOnlyCollection<string>((next ?? throw new ArgumentNullException(nameof(next))).ToArray());
            Layer = layer; BattleSeed = battleSeed;
        }
    }

    public sealed class VoyageRoute
    {
        public string FromId { get; }
        public string ToId { get; }
        public VoyageRoute(string fromId, string toId)
        {
            FromId = fromId ?? throw new ArgumentNullException(nameof(fromId));
            ToId = toId ?? throw new ArgumentNullException(nameof(toId));
        }
    }

    public static class VoyageSeeds
    {
        public static uint Derive(uint master, string scope, string id = "")
        {
            if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Seed scope is required.", nameof(scope));
            if (id == null) throw new ArgumentNullException(nameof(id));
            var hash = 2166136261u;
            Mix(ref hash, (byte)master); Mix(ref hash, (byte)(master >> 8));
            Mix(ref hash, (byte)(master >> 16)); Mix(ref hash, (byte)(master >> 24));
            Mix(ref hash, scope); Mix(ref hash, 0xff); Mix(ref hash, id);
            hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15;
            hash *= 0x846ca68bu; hash ^= hash >> 16;
            return hash;
        }

        private static void Mix(ref uint hash, string value)
        {
            for (var i = 0; i < value.Length; i++)
            {
                Mix(ref hash, (byte)value[i]);
                Mix(ref hash, (byte)(value[i] >> 8));
            }
        }

        private static void Mix(ref uint hash, byte value) { hash = (hash ^ value) * 16777619u; }
    }

    public sealed class VoyagePlan
    {
        public uint Seed { get; }
        public int Version { get; }
        public string StartId { get; }
        public string GoalId { get; }
        public IReadOnlyList<VoyageStop> Stops { get; }

        internal VoyagePlan(uint seed, int version, string startId, string goalId, IEnumerable<VoyageStop> stops)
        {
            Seed = seed; Version = version; StartId = startId; GoalId = goalId;
            Stops = new ReadOnlyCollection<VoyageStop>(stops.ToArray());
        }

        public VoyageMapData Export()
        {
            return new VoyageMapData
            {
                Version = Version, Seed = Seed, StartId = StartId, GoalId = GoalId,
                Stops = Stops.Select(x => new VoyageStopData
                {
                    Id = x.Id, Name = x.Name, Clue = x.Clue, Description = x.Description,
                    X = x.X, Y = x.Y, TravelHours = x.TravelHours, Kind = x.Kind,
                    EncounterId = x.EncounterId, Next = x.Next.ToArray(), Layer = x.Layer,
                    BattleSeed = x.BattleSeed
                }).ToArray()
            };
        }
    }

    [Serializable]
    public sealed class VoyageMapData
    {
        public int Version;
        public uint Seed;
        public string StartId;
        public string GoalId;
        public VoyageStopData[] Stops;
    }

    [Serializable]
    public sealed class VoyageStopData
    {
        public string Id;
        public string Name;
        public string Clue;
        public string Description;
        public float X;
        public float Y;
        public int TravelHours;
        public RunNodeKind Kind;
        public string EncounterId;
        public string[] Next;
        public int Layer;
        public uint BattleSeed;
    }

    public static class VoyageMapGenerator
    {
        public const int Version = 3;
        private const int Layers = 6;
        private static readonly (string Name, string Clue, string Description)[] StopTexts =
        {
            ("漂流货船", "货船舷侧传来间歇的木板摩擦声", "几只帆布箱仍固定在甲板上，船上没有回应呼叫。"),
            ("无旗接驳艇", "没有旗号的小艇顺着潮流缓缓转向", "缆钩垂在舷外，调查船可以保持距离观察接驳口。"),
            ("失联救生艇", "空救生艇随浪沉浮，艇身编号被盐霜遮住", "艇内放着一只蜡布包裹的木匣，周围没有其他船影。"),
            ("旧无线电船", "无线电重复播报一段时间不一致的求救信号", "天线仍连着接收舱，舱窗里偶尔有微弱反光。"),
            ("漂浮测量站", "测深架旁的指针始终偏向同一侧", "浮台上的仪器仍在记录，旧航表被压在防水玻璃下。"),
            ("断桅渔船", "折断的桅杆随涌浪间歇露出", "渔网挂在船舷，船尾水纹与附近潮流的方向不同。"),
            ("封舱驳船", "舱窗被木板遮住，舱门外绕着数道锁链", "驳船仍保持平衡；从外部无法确认舱内的情况。"),
            ("无人信号艇", "雾中的信号灯反复亮起同一组灯序", "艇上没有可辨认的人影，灯号与船籍记录尚未核对。"),
            ("停摆引航船", "引航船停在两股水流交汇处，没有引擎声", "驾驶舱的航表摊在窗边，船首指着外海。"),
            ("漂浮修械船", "甲板吊架随着涌浪缓缓摆动", "工具柜留在敞开的工棚里，接驳缆末端浸在海水中。"),
            ("潮流观测点", "漂浮碎木沿几条不同的水纹向前分开", "调查员留在船上记录流向，深处的海况仍被雾遮住。"),
            ("远海浮标", "锈蚀浮标在雾中发出不规则碰响", "浮标编号已磨损，附近水面的反光勾出一段可航行水路。")
        };
        private static readonly RunNodeKind[] OrdinaryKinds =
        {
            RunNodeKind.Event, RunNodeKind.Archive, RunNodeKind.Battle, RunNodeKind.Battle
        };

        public static VoyagePlan Generate(uint seed)
        {
            var counts = new[] { 1, 2, 0, 0, 0, 1 };
            var next = BuildEdges(seed, counts);
            var textOrder = Enumerable.Range(0, StopTexts.Length).ToArray();
            new DeterministicRandom(VoyageSeeds.Derive(seed, "names")).Shuffle(textOrder);
            var nameIndex = 0;
            var stops = new List<VoyageStop>(counts.Sum());
            for (var layer = 0; layer < Layers; layer++)
            {
                for (var index = 0; index < counts[layer]; index++)
                {
                    var id = Id(layer, index);
                    var layout = VoyageSeeds.Derive(seed, "layout", id);
                    var content = VoyageSeeds.Derive(seed, "content", id);
                    var x = counts[layer] == 1 ? 50f :
                        counts[layer] == 2 ? 38f + 24f * index : 34f + 16f * index;
                    x += Jitter(layout, 1);
                    var y = 12f + layer * 15f + Jitter(layout >> 8, 1);
                    var kind = OrdinaryKinds[(int)(content % (uint)OrdinaryKinds.Length)];
                    var encounter = kind == RunNodeKind.Battle ? "fog-visitors" : string.Empty;
                    string name, clue, description;
                    var hours = layer == 0 ? 0 : 3 + (int)((content >> 24) % 6);
                    if ((layer == 1 || layer == 3) && index == 0)
                    {
                        kind = RunNodeKind.Event;
                        encounter = string.Empty;
                    }
                    if (layer == 0)
                    {
                        name = "补给母船"; clue = "母船旁的两列浮标指向不同航向";
                        description = "调查船松开接驳缆，离开灰雾补给母船，前方仍是海与雾。"; kind = RunNodeKind.Event; encounter = string.Empty;
                    }
                    else if (layer == Layers - 1)
                    {
                        name = "无声灯船"; clue = "一艘旧灯船停在雾中，信号灯始终熄灭";
                        description = "灯船下层舱窗透出微弱反光，接驳口旁留着旧值班记录。";
                        kind = RunNodeKind.Boss; encounter = "fog-visitors";
                    }
                    else
                    {
                        var text = StopTexts[textOrder[nameIndex++]];
                        name = text.Name; clue = text.Clue; description = text.Description;
                    }
                    stops.Add(new VoyageStop(id, name, clue, description, x, y, hours, kind, encounter,
                        next[id], layer, VoyageSeeds.Derive(seed, "battle", id)));
                }
            }
            var plan = new VoyagePlan(seed, Version, Id(0, 0), Id(5, 0), stops);
            Validate(plan.Export());
            return plan;
        }

        public static VoyagePlan Restore(VoyageMapData data)
        {
            Validate(data);
            var stops = data.Stops.Select(x => new VoyageStop(x.Id, x.Name, x.Clue, x.Description, x.X, x.Y,
                x.TravelHours, x.Kind, x.EncounterId, x.Next, x.Layer, x.BattleSeed)).ToArray();
            return new VoyagePlan(data.Seed, data.Version, data.StartId, data.GoalId, stops);
        }

        private static Dictionary<string, IReadOnlyList<string>> BuildEdges(uint seed, int[] counts)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            result[Id(0, 0)] = new ReadOnlyCollection<string>(new[] { Id(1, 0), Id(1, 1) });
            for (var layer = 1; layer <= 3; layer++)
            {
                var degrees = new int[counts[layer]];
                var width = 1;
                for (var source = 0; source < degrees.Length; source++)
                {
                    degrees[source] = SampleDegree(seed, layer, source);
                    width = Math.Max(width, degrees[source]);
                }
                counts[layer + 1] = width;
                for (var source = 0; source < degrees.Length; source++)
                {
                    var targets = Enumerable.Range(0, width)
                        .OrderBy(target => Math.Abs(SlotX(counts[layer], source) - SlotX(width, target)))
                        .ThenBy(target => target)
                        .Take(degrees[source])
                        .OrderBy(target => target)
                        .Select(target => Id(layer + 1, target))
                        .ToArray();
                    result[Id(layer, source)] = new ReadOnlyCollection<string>(targets);
                }
            }
            for (var source = 0; source < counts[4]; source++)
                result[Id(4, source)] = new ReadOnlyCollection<string>(new[] { Id(5, 0) });
            result[Id(5, 0)] = new ReadOnlyCollection<string>(Array.Empty<string>());
            return result;
        }

        private static void Validate(VoyageMapData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Version != Version) throw new ArgumentException("Unsupported voyage map version.", nameof(data));
            if (data.Stops == null || data.Stops.Length < 7 || data.Stops.Length > 14) throw Invalid("Map must contain 7 to 14 stops.");
            if (!ValidId(data.StartId) || !ValidId(data.GoalId) || data.StartId == data.GoalId) throw Invalid("Invalid start or goal ID.");
            var byId = new Dictionary<string, VoyageStopData>(StringComparer.Ordinal);
            var layerCounts = new int[Layers];
            foreach (var stop in data.Stops)
            {
                if (stop == null || !ValidId(stop.Id) || byId.ContainsKey(stop.Id)) throw Invalid("Null, invalid, or duplicate stop.");
                byId.Add(stop.Id, stop);
                if (stop.Layer < 0 || stop.Layer >= Layers) throw Invalid("Invalid stop layer.");
                layerCounts[stop.Layer]++;
                if (!ValidText(stop.Name, 80) || !ValidText(stop.Clue, 256) || !ValidText(stop.Description, 512)) throw Invalid("Invalid stop text.");
                if (!Finite(stop.X) || !Finite(stop.Y) || stop.X < 10 || stop.X > 90 || stop.Y < 10 || stop.Y > 90) throw Invalid("Invalid chart coordinates.");
                if (stop.TravelHours < 0 || stop.TravelHours > 24) throw Invalid("Invalid travel time.");
                if (!Enum.IsDefined(typeof(RunNodeKind), stop.Kind) || stop.EncounterId == null || stop.Next == null || stop.Next.Length > 3) throw Invalid("Invalid stop kind, encounter, or routes.");
                if (stop.Next.Any(x => !ValidId(x)) || stop.Next.Distinct(StringComparer.Ordinal).Count() != stop.Next.Length) throw Invalid("Invalid or duplicate route.");
                var encounter = stop.Kind == RunNodeKind.Battle || stop.Kind == RunNodeKind.Boss;
                if ((encounter && stop.EncounterId != "fog-visitors") ||
                    (!encounter && !string.IsNullOrEmpty(stop.EncounterId))) throw Invalid("Invalid encounter content.");
            }
            if (data.Stops.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != data.Stops.Length) throw Invalid("Stop names must be unique.");
            if (data.Stops.Any(x => x.BattleSeed != VoyageSeeds.Derive(data.Seed, "battle", x.Id))) throw Invalid("Invalid battle seed domain.");
            if (!byId.TryGetValue(data.StartId, out var start) || start.Layer != 0 || layerCounts[0] != 1 || start.TravelHours != 0 || start.Kind == RunNodeKind.Battle || start.Kind == RunNodeKind.Boss || start.Next.Length != 2) throw Invalid("Invalid departure stop.");
            if (!byId.TryGetValue(data.GoalId, out var goal) || goal.Layer != 5 || layerCounts[5] != 1 || goal.Kind != RunNodeKind.Boss || goal.Next.Length != 0) throw Invalid("Invalid terminal lightship.");
            if (data.Stops.Any(x => x.Id != data.GoalId && x.Kind == RunNodeKind.Boss)) throw Invalid("Only the terminal may be a boss.");
            if (layerCounts[1] != 2) throw Invalid("The first route layer must contain two stops.");
            for (var layer = 2; layer < 5; layer++) if (layerCounts[layer] < 1 || layerCounts[layer] > 3) throw Invalid("Invalid intermediate layer size.");
            for (var layer = 0; layer < Layers; layer++)
                for (var index = 0; index < layerCounts[layer]; index++)
                    if (!byId.TryGetValue(Id(layer, index), out var expected) || expected.Layer != layer)
                        throw Invalid("Stop IDs must be stable within each layer.");
            if (data.Stops.Any(x => x.Id != data.StartId && x.TravelHours == 0)) throw Invalid("Only the departure stop may have zero travel time.");
            var incoming = data.Stops.ToDictionary(x => x.Id, x => 0, StringComparer.Ordinal);
            foreach (var stop in data.Stops)
            {
                var expectedDegree = stop.Layer >= 1 && stop.Layer <= 3
                    ? SampleDegree(data.Seed, stop.Layer, ParseIndex(stop.Id))
                    : stop.Layer == 0 ? 2 : stop.Layer == 4 ? 1 : 0;
                if (stop.Next.Length != expectedDegree) throw Invalid("Route degree does not match its deterministic sample.");
                foreach (var targetId in stop.Next)
                {
                    if (!byId.TryGetValue(targetId, out var target) || target.Layer != stop.Layer + 1) throw Invalid("Dangling or layer-skipping route.");
                    var deltaX = Math.Abs(target.X - stop.X);
                    var deltaY = target.Y - stop.Y;
                    if (deltaY <= 0 || deltaX > 34f || deltaX * deltaX + deltaY * deltaY > 1600f) throw Invalid("Route span exceeds local chart bounds.");
                    incoming[targetId]++;
                }
            }
            if (data.Stops.Any(x => x.Id != data.StartId && incoming[x.Id] == 0)) throw Invalid("Unreachable stop.");
            if (!data.Stops.Any(x => x.Layer > 1 && x.Layer < 5 && incoming[x.Id] > 1)) throw Invalid("Map requires an intermediate merge.");
            for (var layer = 2; layer <= 4; layer++)
            {
                var expectedWidth = data.Stops.Where(x => x.Layer == layer - 1).Max(x => x.Next.Length);
                if (layerCounts[layer] != expectedWidth) throw Invalid("Layer width does not match sampled routes.");
            }
            var reachable = new HashSet<string>(StringComparer.Ordinal) { data.StartId };
            for (var layer = 0; layer < 5; layer++)
                foreach (var stop in data.Stops.Where(x => x.Layer == layer && reachable.Contains(x.Id)))
                    foreach (var id in stop.Next) reachable.Add(id);
            if (reachable.Count != data.Stops.Length || !reachable.Contains(data.GoalId)) throw Invalid("Map contains unreachable stops.");
        }

        private static string Id(int layer, int index) => "l" + layer + "n" + index;
        private static int SampleDegree(uint seed, int layer, int index)
        {
            var roll = VoyageSeeds.Derive(seed, "topology", Id(layer, index)) % 100u;
            return roll < 40u ? 1 : roll < 82u ? 2 : 3;
        }
        private static float SlotX(int count, int index) => count == 1 ? 50f : count == 2 ? 38f + 24f * index : 34f + 16f * index;
        private static int ParseIndex(string id) => int.Parse(id.Substring(id.IndexOf('n') + 1));
        private static float Jitter(uint value, int radius) => (int)(value % (uint)(radius * 2 + 1)) - radius;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidText(string value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max;
        private static bool ValidId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 64) return false;
            for (var i = 0; i < value.Length; i++)
                if (!(value[i] >= 'a' && value[i] <= 'z') && !(value[i] >= '0' && value[i] <= '9') && value[i] != '_' && value[i] != '-') return false;
            return true;
        }
        private static ArgumentException Invalid(string message) => new ArgumentException(message, "data");
    }
}
