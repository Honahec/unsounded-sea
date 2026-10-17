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
        public VoyageStop(string id, string name, string clue, string description, float x, float y, int travelHours,
            RunNodeKind kind, string encounterId, IEnumerable<string> next, int layer, uint battleSeed)
        {
            Id = id; Name = name; Clue = clue; Description = description; X = x; Y = y; TravelHours = travelHours;
            Kind = kind; EncounterId = encounterId;
            Next = new ReadOnlyCollection<string>((next ?? throw new ArgumentNullException(nameof(next))).ToArray());
            Layer = layer; BattleSeed = battleSeed;
        }
    }

    public sealed class VoyageRoute
    {
        public string FromId { get; }
        public string ToId { get; }
        public VoyageRoute(string fromId, string toId) { FromId = fromId ?? throw new ArgumentNullException(nameof(fromId)); ToId = toId ?? throw new ArgumentNullException(nameof(toId)); }
    }

    public static class VoyageSeeds
    {
        public static uint Derive(uint master, string scope, string id = "")
        {
            if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("Seed scope is required.", nameof(scope));
            if (id == null) throw new ArgumentNullException(nameof(id));
            var hash = 2166136261u;
            Mix(ref hash, (byte)master); Mix(ref hash, (byte)(master >> 8)); Mix(ref hash, (byte)(master >> 16)); Mix(ref hash, (byte)(master >> 24));
            Mix(ref hash, scope); Mix(ref hash, 0xff); Mix(ref hash, id);
            hash ^= hash >> 16; hash *= 0x7feb352du; hash ^= hash >> 15; hash *= 0x846ca68bu; hash ^= hash >> 16;
            return hash;
        }
        private static void Mix(ref uint hash, string value) { for (var i = 0; i < value.Length; i++) { Mix(ref hash, (byte)value[i]); Mix(ref hash, (byte)(value[i] >> 8)); } }
        private static void Mix(ref uint hash, byte value) { hash = (hash ^ value) * 16777619u; }
    }

    public sealed class VoyagePlan
    {
        public uint Seed { get; }
        public int Version { get; }
        public int Act { get; }
        public string RegionId { get; }
        public string StartId { get; }
        public string GoalId { get; }
        public IReadOnlyList<VoyageStop> Stops { get; }
        internal VoyagePlan(uint seed, int version, int act, string regionId, string startId, string goalId, IEnumerable<VoyageStop> stops)
        { Seed = seed; Version = version; Act = act; RegionId = regionId; StartId = startId; GoalId = goalId; Stops = new ReadOnlyCollection<VoyageStop>(stops.ToArray()); }
        public VoyageMapData Export() => new VoyageMapData
        {
            Version = Version, Seed = Seed, Act = Act, RegionId = RegionId, StartId = StartId, GoalId = GoalId,
            Stops = Stops.Select(x => new VoyageStopData { Id=x.Id, Name=x.Name, Clue=x.Clue, Description=x.Description, X=x.X, Y=x.Y,
                TravelHours=x.TravelHours, Kind=x.Kind, EncounterId=x.EncounterId, Next=x.Next.ToArray(), Layer=x.Layer, BattleSeed=x.BattleSeed }).ToArray()
        };
    }

    [Serializable] public sealed class VoyageMapData { public int Version; public uint Seed; public int Act; public string RegionId; public string StartId; public string GoalId; public VoyageStopData[] Stops; }
    [Serializable] public sealed class VoyageStopData
    { public string Id; public string Name; public string Clue; public string Description; public float X; public float Y; public int TravelHours; public RunNodeKind Kind; public string EncounterId; public string[] Next; public int Layer; public uint BattleSeed; }

    public static class VoyageMapGenerator
    {
        public const int Version = 8;
        public const int Layers = 11;
        private static readonly string[][] ActBosses =
        {
            new[] { "fog-visitors", "storm-gauntlet", "hollow-beacon" },
            new[] { "m02", "m03", "m04" },
            new[] { "m05", "drowned-bell" }
        };
        private static readonly RunNodeKind[] Schedule = { RunNodeKind.Supply, RunNodeKind.Battle, RunNodeKind.Event, RunNodeKind.Battle,
            RunNodeKind.Reward, RunNodeKind.Supply, RunNodeKind.Battle, RunNodeKind.Archive, RunNodeKind.Battle, RunNodeKind.Supply, RunNodeKind.Boss };
        private static readonly (string Name,string Clue,string Description)[] Texts =
        {
            ("漂流货船","舷侧传来木板摩擦声","帆布箱仍固定在甲板上。"),("无旗接驳艇","小艇顺着潮流转向","缆钩垂在舷外。"),
            ("失联救生艇","艇身编号被盐霜遮住","艇内留有蜡布木匣。"),("旧无线电船","无线电重复求救信号","天线仍连着接收舱。"),
            ("漂浮测量站","测深指针偏向一侧","旧航表压在玻璃下。"),("断桅渔船","折桅随涌浪露出","船尾水纹逆着潮流。"),
            ("封舱驳船","舱门绕着锁链","从外部无法确认舱内。"),("无人信号艇","信号灯反复亮起","灯号尚未核对。"),
            ("停摆引航船","引擎没有声音","航表摊在窗边。"),("漂浮修械船","甲板吊架缓缓摆动","工具柜仍在工棚里。"),
            ("潮流观测点","碎木沿水纹分开","深处海况被雾遮住。"),("远海浮标","浮标不规则碰响","反光勾出航路。")
        };

        public static VoyagePlan Generate(uint seed) => Generate(seed, 1, "fog");
        public static VoyagePlan Generate(uint seed, int act, string regionId)
        {
            ValidateActRegion(act, regionId);
            var widths = BuildWidths(seed, act, regionId);
            var edges = BuildEdges(seed, act, regionId, widths);
            var names = Enumerable.Range(0, Texts.Length).ToArray();
            new DeterministicRandom(VoyageSeeds.Derive(seed, "names", act + ":" + regionId)).Shuffle(names);
            var textIndex = 0;
            var stops = new List<VoyageStop>();
            for (var layer = 0; layer < Layers; layer++)
            {
                for (var index = 0; index < widths[layer]; index++)
                {
                    var id = Id(act, layer, index);
                    var derived = VoyageSeeds.Derive(seed, "content", id + ":" + regionId);
                    var kind = Schedule[layer];
                    var encounter = "";
                    string name, clue, description;
                    if (layer == 0)
                    {
                        name = "补给母船"; clue = "浮标指向不同航向"; description = "整备完成后离开母船。";
                    }
                    else if (layer == Layers - 1)
                    {
                        name = "无声灯船"; clue = "终点信号始终熄灭"; description = "决定本海域命运的事物正在前方。";
                        encounter = BossEncounter(seed, act, regionId);
                    }
                    else
                    {
                        var text = Texts[names[textIndex++ % names.Length]];
                        name = text.Name; clue = text.Clue; description = text.Description;
                        if (kind == RunNodeKind.Battle)
                            encounter = "voyage-" + regionId + "-" + EncounterVariant(seed, act, regionId, layer, index);
                    }
                    var x = SlotX(widths[layer], index) + (int)(derived % 3u) - 1;
                    var y = 8f + layer * 8.3f;
                    stops.Add(new VoyageStop(id, name, clue, description, x, y, layer == 0 ? 0 : 3 + (int)(derived % 6u),
                        kind, encounter, edges[id], layer, VoyageSeeds.Derive(seed, "battle", id + ":" + regionId)));
                }
            }
            var plan = new VoyagePlan(seed, Version, act, regionId, Id(act, 0, 0), Id(act, 10, 0), stops);
            Validate(plan.Export());
            return plan;
        }

        public static VoyagePlan Restore(VoyageMapData data)
        {
            Validate(data);
            return new VoyagePlan(data.Seed,data.Version,data.Act,data.RegionId,data.StartId,data.GoalId,data.Stops.Select(x=>new VoyageStop(x.Id,x.Name,x.Clue,x.Description,x.X,x.Y,x.TravelHours,x.Kind,x.EncounterId,x.Next,x.Layer,x.BattleSeed)));
        }

        private static void Validate(VoyageMapData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Version != Version) throw Invalid("Unsupported voyage map version.");
            ValidateActRegion(data.Act, data.RegionId);
            if (data.Stops == null || data.Stops.Length < 12 || data.Stops.Length > 29) throw Invalid("Map size is outside bounded layers.");
            var expectedWidths = BuildWidths(data.Seed, data.Act, data.RegionId);
            var expectedEdges = BuildEdges(data.Seed, data.Act, data.RegionId, expectedWidths);
            var byId = new Dictionary<string, VoyageStopData>(StringComparer.Ordinal);
            var counts = new int[Layers];
            foreach (var stop in data.Stops)
            {
                if (stop == null || !ValidId(stop.Id) || byId.ContainsKey(stop.Id)) throw Invalid("Invalid stop ID.");
                byId.Add(stop.Id, stop);
                if (stop.Layer < 0 || stop.Layer >= Layers) throw Invalid("Invalid layer.");
                counts[stop.Layer]++;
                if (!ValidText(stop.Name, 80) || !ValidText(stop.Clue, 256) || !ValidText(stop.Description, 512) ||
                    !Finite(stop.X) || !Finite(stop.Y) || stop.X < 10 || stop.X > 90 || stop.Y < 5 || stop.Y > 95)
                    throw Invalid("Invalid stop content.");
                if (stop.TravelHours < 0 || stop.TravelHours > 24 || !Enum.IsDefined(typeof(RunNodeKind), stop.Kind) ||
                    stop.EncounterId == null || stop.Next == null || stop.Next.Length > 3 ||
                    stop.Next.Distinct(StringComparer.Ordinal).Count() != stop.Next.Length)
                    throw Invalid("Invalid stop fields.");
                if (stop.Kind != Schedule[stop.Layer]) throw Invalid("Invalid act schedule.");
                var expectedEncounter = stop.Kind == RunNodeKind.Battle
                    ? "voyage-" + data.RegionId + "-" + EncounterVariant(data.Seed,data.Act,data.RegionId,stop.Layer,ParseIndex(stop.Id))
                    : stop.Kind == RunNodeKind.Boss ? BossEncounter(data.Seed, data.Act, data.RegionId) : "";
                if (stop.EncounterId != expectedEncounter ||
                    stop.BattleSeed != VoyageSeeds.Derive(data.Seed, "battle", stop.Id + ":" + data.RegionId))
                    throw Invalid("Invalid deterministic content.");
            }
            for (var layer = 0; layer < Layers; layer++)
            {
                if (counts[layer] != expectedWidths[layer]) throw Invalid("Invalid layer width.");
                for (var index = 0; index < expectedWidths[layer]; index++)
                    if (!byId.ContainsKey(Id(data.Act, layer, index))) throw Invalid("Missing stable stop ID.");
            }
            if (data.StartId != Id(data.Act, 0, 0) || data.GoalId != Id(data.Act, 10, 0)) throw Invalid("Invalid endpoints.");
            foreach (var stop in data.Stops)
                if (!stop.Next.SequenceEqual(expectedEdges[stop.Id]) || (stop.Layer == 0) != (stop.TravelHours == 0))
                    throw Invalid("Invalid routes.");
        }
        private static int[] BuildWidths(uint seed, int act, string region)
        {
            var widths = new int[Layers];
            widths[0] = 1; widths[1] = 2;
            for (var layer = 1; layer <= 8; layer++)
                widths[layer + 1] = Enumerable.Range(0, widths[layer]).Max(index => SampleDegree(seed, act, region, layer, index));
            widths[10] = 1;
            return widths;
        }
        private static Dictionary<string, IReadOnlyList<string>> BuildEdges(uint seed, int act, string region, int[] widths)
        {
            var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            result[Id(act, 0, 0)] = Array.AsReadOnly(new[] { Id(act, 1, 0), Id(act, 1, 1) });
            for (var layer = 1; layer <= 8; layer++)
            {
                for (var source = 0; source < widths[layer]; source++)
                {
                    var degree = SampleDegree(seed, act, region, layer, source);
                    var targets = Enumerable.Range(0, widths[layer + 1])
                        .OrderBy(target => Math.Abs(SlotX(widths[layer], source) - SlotX(widths[layer + 1], target)))
                        .ThenBy(target => target).Take(degree).OrderBy(target => target)
                        .Select(target => Id(act, layer + 1, target)).ToArray();
                    result[Id(act, layer, source)] = Array.AsReadOnly(targets);
                }
            }
            for (var source = 0; source < widths[9]; source++)
                result[Id(act, 9, source)] = Array.AsReadOnly(new[] { Id(act, 10, 0) });
            result[Id(act, 10, 0)] = Array.AsReadOnly(Array.Empty<string>());
            return result;
        }
        private static int SampleDegree(uint seed, int act, string region, int layer, int index)
        {
            var roll = VoyageSeeds.Derive(seed, "topology", act + ":" + region + ":" + layer + ":" + index) % 100u;
            return roll < 40u ? 1 : roll < 82u ? 2 : 3;
        }
        private static int EncounterVariant(uint seed,int act,string region,int layer,int index)
        {
            var offset=VoyageSeeds.Derive(seed,"encounter",act+":"+region)%5u;
            var battleOrdinal=layer==1?0u:layer==3?1u:layer==6?2u:3u;
            return (int)((offset+battleOrdinal+(uint)index)%5u);
        }
        private static float SlotX(int count, int index) => count == 1 ? 50f : count == 2 ? 38f + 24f * index : 34f + 16f * index;
        private static int ParseIndex(string id) => int.Parse(id.Substring(id.LastIndexOf('n') + 1));
        private static void ValidateActRegion(int act,string region) { var valid=act==1&&region=="fog"||act==2&&(region=="bones"||region=="inverted"||region=="fleet")||act==3&&region=="abyss"; if(!valid) throw new ArgumentException("Invalid act region."); }
        public static string BossEncounter(uint seed, int act, string region)
        {
            ValidateActRegion(act, region);
            var pool = ActBosses[act - 1];
            return pool[VoyageSeeds.Derive(seed, "boss", "act-" + act) % (uint)pool.Length];
        }
        private static string Id(int act,int layer,int index)=>"a"+act+"l"+layer+"n"+index;
        private static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        private static bool ValidText(string x,int max)=>!string.IsNullOrWhiteSpace(x)&&x.Length<=max;
        private static bool ValidId(string x)=>!string.IsNullOrWhiteSpace(x)&&x.Length<=64&&x.All(c=>char.IsLower(c)||char.IsDigit(c)||c=='_'||c=='-');
        private static ArgumentException Invalid(string message)=>new ArgumentException(message,"data");
    }
}
