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
        public bool Perilous { get; }
        public VoyageStop(string id, string name, string clue, string description, float x, float y, int travelHours,
            RunNodeKind kind, string encounterId, IEnumerable<string> next, int layer, uint battleSeed, bool perilous = false)
        {
            Id=id; Name=name; Clue=clue; Description=description; X=x; Y=y; TravelHours=travelHours; Kind=kind;
            EncounterId=encounterId; Next=Array.AsReadOnly((next??throw new ArgumentNullException(nameof(next))).ToArray());
            Layer=layer; BattleSeed=battleSeed; Perilous=perilous;
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
                TravelHours=x.TravelHours, Kind=x.Kind, EncounterId=x.EncounterId, Next=x.Next.ToArray(), Layer=x.Layer, BattleSeed=x.BattleSeed, Perilous=x.Perilous }).ToArray()
        };
    }

    [Serializable] public sealed class VoyageMapData { public int Version; public uint Seed; public int Act; public string RegionId; public string StartId; public string GoalId; public VoyageStopData[] Stops; }
    [Serializable] public sealed class VoyageStopData
    { public string Id; public string Name; public string Clue; public string Description; public float X; public float Y; public int TravelHours; public RunNodeKind Kind; public string EncounterId; public string[] Next; public int Layer; public uint BattleSeed; public bool Perilous; }

    public static class VoyageMapGenerator
    {
        public const int Version = 9;
        public const int Layers = 11;
        private static readonly string[][] ActBosses =
        {
            new[] { "fog-visitors", "storm-gauntlet", "hollow-beacon" },
            new[] { "m02", "m03", "m04" },
            new[] { "m05", "drowned-bell" }
        };
        private static readonly RunNodeKind[] Schedule = { RunNodeKind.Supply, RunNodeKind.Battle, RunNodeKind.Event, RunNodeKind.Battle,
            RunNodeKind.Reward, RunNodeKind.Supply, RunNodeKind.Battle, RunNodeKind.Archive, RunNodeKind.Battle, RunNodeKind.Supply, RunNodeKind.Boss };
        private static readonly string[] EventTitles =
        {
            "雾中空舱","迟到的求救","逆流锚链","骨潮遗货","白骨发报机","沉骨系泊","倒悬接驳","反时呼号","上浮锚链",
            "无灯货舱","舰列余响","交叉锚索","深渊回舱","下方呼号","无底锚链"
        };
        private static readonly (string Name,string Clue,string Description)[] EventTexts =
        {
            ("漂流货船","空舱里传来第二道脚步","盐雾把一艘无人货船推向航线。"),("失联救生艇","求救频率比船钟慢半拍","救生艇仍在重复最后一段呼号。"),("断桅渔船","锚链下的水纹逆向闭合","一截封死的锚链拖着密封浮箱。"),
            ("漂流货船","骨片在货舱外排列成环","货船正穿过鲸骸密集区。"),("旧无线电船","电报码夹着刮擦船壳的节拍","无线电船卡在肋骨般的礁脊间。"),("封舱驳船","锚链绷向看不见的深处","驳船在骨潮边缘原地打转。"),
            ("无旗接驳艇","小艇的影子浮在浪面上方","接驳艇沿反向潮沟靠近。"),("无人信号艇","同一句求救由结尾向开头播放","信号艇的灯号与声音互相矛盾。"),("断桅渔船","锚链从海面垂向低云","渔船被倒悬水流固定。"),
            ("漂流货船","舱内金属声随邻船灯号停顿","货船混入无灯舰列。"),("停摆引航船","求救信号在不同船壳间跳转","引航船仍向舰列发送旧坐标。"),("封舱驳船","两条锚索在水下交换方向","驳船挡住舰列间的窄道。"),
            ("失联救生艇","舱门内侧传来缓慢敲击","救生艇随黑色涌浪升降。"),("旧无线电船","求救声从船底更深处返回","无线电船的天线指向海面以下。"),("断桅渔船","放出的锚链始终没有尽头","渔船拖着仍在下坠的密封浮箱。")
        };
        private static readonly string[] BattleNames={"断桅渔船","封舱驳船","无人信号艇","停摆引航船"};
        private static readonly string[] ArchiveNames={"漂浮测量站","潮流观测点","远海浮标"};

        public static VoyagePlan Generate(uint seed) => Generate(seed, 1, "fog");
        public static VoyagePlan Generate(uint seed, int act, string regionId)
        {
            ValidateActRegion(act, regionId);
            var widths = BuildWidths(seed, act, regionId);
            var edges = BuildEdges(seed, act, regionId, widths);
            var stops = new List<VoyageStop>();
            for (var layer = 0; layer < Layers; layer++)
            for (var index = 0; index < widths[layer]; index++)
            {
                var id = Id(act, layer, index);
                var derived = VoyageSeeds.Derive(seed, "content", id + ":" + regionId);
                var kind = KindAt(seed, act, regionId, layer, index);
                var perilous = layer == 6 && index == PerilousIndex(seed, act, regionId);
                var encounter = kind == RunNodeKind.Battle ? "voyage-" + regionId + "-" + EncounterVariant(seed, act, regionId, layer, index, perilous) :
                    kind == RunNodeKind.Boss ? BossEncounter(seed, act, regionId) : "";
                string name, clue, description;
                if (layer == 0) { name="补给母船"; clue="浮标指向不同航向"; description="整备完成后离开母船。"; }
                else if (layer == Layers - 1) { name="无声灯船"; clue="终点信号始终熄灭"; description="决定本海域命运的事物正在前方。"; }
                else if (kind == RunNodeKind.Event)
                {
                    var text=EventTexts[EventTemplate(seed,act,regionId,id)];
                    name=text.Name;clue=text.Clue;description=text.Description;
                }
                else
                {
                    name=kind==RunNodeKind.Battle?BattleNames[(int)(derived%(uint)BattleNames.Length)]:kind==RunNodeKind.Reward?"失联救生艇":kind==RunNodeKind.Archive?ArchiveNames[(int)(derived%(uint)ArchiveNames.Length)]:"漂浮修械船";
                    clue=kind==RunNodeKind.Battle?(perilous?"两艘敌船同时切入航线":"不明轮廓正向船舷靠近"):kind==RunNodeKind.Reward?"封条仍未破损":kind==RunNodeKind.Archive?"测深指针偏向一侧":"甲板吊架缓缓摆动";
                    description=kind==RunNodeKind.Battle?(perilous?"双重威胁封死退路，但留下两次战利品窗口。":"必须处理靠近的海上威胁。"):kind==RunNodeKind.Reward?"艇内留有可用的行动记录。":kind==RunNodeKind.Archive?"旧航表压在玻璃下。":"工具柜与补给箱已经固定。";
                }
                var x=SlotX(widths[layer],index)+(int)(derived%3u)-1;
                var y=8f+layer*8.3f;
                stops.Add(new VoyageStop(id,name,clue,description,x,y,layer==0?0:3+(int)(derived%6u),kind,encounter,
                    edges[id],layer,VoyageSeeds.Derive(seed,"battle",id+":"+regionId),perilous));
            }
            var plan=new VoyagePlan(seed,Version,act,regionId,Id(act,0,0),Id(act,10,0),stops);
            Validate(plan.Export());
            return plan;
        }

        public static VoyagePlan Restore(VoyageMapData data)
        {
            Validate(data);
            return new VoyagePlan(data.Seed,data.Version,data.Act,data.RegionId,data.StartId,data.GoalId,data.Stops.Select(x=>new VoyageStop(x.Id,x.Name,x.Clue,x.Description,x.X,x.Y,x.TravelHours,x.Kind,x.EncounterId,x.Next,x.Layer,x.BattleSeed,x.Perilous)));
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
                if(!TryParseIndex(stop.Id,data.Act,stop.Layer,out var index))throw Invalid("Invalid stable stop ID.");
                var expectedKind=KindAt(data.Seed,data.Act,data.RegionId,stop.Layer,index);
                var expectedPerilous=stop.Layer==6&&index==PerilousIndex(data.Seed,data.Act,data.RegionId);
                if (stop.Kind != expectedKind || stop.Perilous != expectedPerilous) throw Invalid("Invalid act schedule.");
                var expectedEncounter = stop.Kind == RunNodeKind.Battle
                    ? "voyage-" + data.RegionId + "-" + EncounterVariant(data.Seed,data.Act,data.RegionId,stop.Layer,index,expectedPerilous)
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
            widths[0]=1;widths[1]=2;
            for(var layer=1;layer<=8;layer++)
            {
                widths[layer+1]=layer==2||layer==7?widths[layer]:Enumerable.Range(0,widths[layer]).Max(index=>SampleDegree(seed,act,region,layer,index));
                widths[layer+1]=Math.Max(2,widths[layer+1]);
            }
            widths[10]=1;
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
                    var targets = layer==2||layer==7
                        ? new[]{Id(act,layer+1,source)}
                        : Enumerable.Range(0,widths[layer+1])
                            .OrderBy(target=>Math.Abs(SlotX(widths[layer],source)-SlotX(widths[layer+1],target)))
                            .ThenBy(target=>target).Take(Math.Min(widths[layer+1],Math.Max(2,SampleDegree(seed,act,region,layer,source)))).OrderBy(target=>target)
                            .Select(target=>Id(act,layer+1,target)).ToArray();
                    result[Id(act,layer,source)]=Array.AsReadOnly(targets);
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
        private static int EncounterVariant(uint seed,int act,string region,int layer,int index,bool perilous)
        {
            if(perilous)return 4;
            var offset=VoyageSeeds.Derive(seed,"encounter",act+":"+region)%4u;
            var ordinal=layer==1?0u:layer==2||layer==3?1u:layer==6?2u:3u;
            return (int)((offset+ordinal)%4u);
        }
        private static RunNodeKind KindAt(uint seed,int act,string region,int layer,int index)
        {
            if(layer!=2&&layer!=3&&layer!=7&&layer!=8)return Schedule[layer];
            var first=layer==2||layer==7;
            var swap=(index+(int)(VoyageSeeds.Derive(seed,"branch-order",act+":"+region+":"+(layer<7?2:7))&1u))%2!=0;
            var early=layer<7?RunNodeKind.Event:RunNodeKind.Archive;
            return first^swap?early:RunNodeKind.Battle;
        }
        private static int PerilousIndex(uint seed,int act,string region)=>(int)(VoyageSeeds.Derive(seed,"perilous",act+":"+region)%2u);
        private static int RegionIndex(string region)=>region=="fog"?0:region=="bones"?1:region=="inverted"?2:region=="fleet"?3:4;
        internal static string EventTitle(uint seed,int act,string region,string stopId)=>EventTitles[EventTemplate(seed,act,region,stopId)];
        private static int EventTemplate(uint seed,int act,string region,string stopId)=>RegionIndex(region)*3+(int)(VoyageSeeds.Derive(seed,"event-template",stopId)%3u);
        private static float SlotX(int count, int index) => count == 1 ? 50f : count == 2 ? 38f + 24f * index : 34f + 16f * index;
        private static bool TryParseIndex(string id,int act,int layer,out int index)
        {
            index=-1;
            var prefix="a"+act+"l"+layer+"n";
            return id.StartsWith(prefix,StringComparison.Ordinal)&&int.TryParse(id.Substring(prefix.Length),out index)&&index>=0;
        }
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
