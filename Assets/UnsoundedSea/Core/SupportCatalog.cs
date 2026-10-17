using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public enum ItemKind { Tool, Navigation, Consumable, Equipment }
    public sealed class ItemSpec
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public ItemKind Kind { get; }
        public int Cost { get; }
        public bool Engine { get; }
        public IReadOnlyList<string> BossIds { get; }
        public ItemSpec(string id, string name, string description, ItemKind kind, int cost = 0, bool engine = false, IEnumerable<string> bossIds = null)
        {
            Id=id; Name=name; Description=description; Kind=kind; Cost=cost; Engine=engine;
            BossIds=Require.Copy(bossIds??Array.Empty<string>());
        }
    }
    public static class SupportCatalog
    {
        public static readonly IReadOnlyList<ItemSpec> Items = Array.AsReadOnly(new[]
        {
            new ItemSpec("lantern","防风提灯","每场一次，确认可观察的本回合动作。",ItemKind.Tool),
            new ItemSpec("listener","定向听筒","每场一次，取得当前声源答案，不确认意图。",ItemKind.Tool),
            new ItemSpec("shell","听潮贝壳","每场一次，支付3理智查询伤害类别。",ItemKind.Tool),
            new ItemSpec("chart","旧海图","每幕2次，揭示当前航点的出口。",ItemKind.Navigation),
            new ItemSpec("lookout","测绘望远镜","每幕2次，侦察选定航点的后续出口。",ItemKind.Navigation),
            new ItemSpec("i06","黄铜探针","每场一次，测读一个未测盐格，不打开。",ItemKind.Tool,bossIds:new[]{"M03"}),
            new ItemSpec("i07","铭牌拓印器","每场一次，验证公开的守卫与锚点对应。",ItemKind.Tool,bossIds:new[]{"M05"}),
            new ItemSpec("i08","绝缘钳","每场一次，校正错误继电器并获得4格挡；占操作额度。",ItemKind.Tool,1,bossIds:new[]{"M04"}),
            new ItemSpec("i09","显影粉","验证一组已公开且尚未验证的守卫—锚点对应；带入时另占一个工具槽。",ItemKind.Consumable,bossIds:new[]{"M05"}),
            new ItemSpec("i10","医疗敷料","恢复8生命。",ItemKind.Consumable,1),
            new ItemSpec("i11","清醒盐","获得6理智防护，不抵销主动支付。",ItemKind.Consumable),
            new ItemSpec("i12","干燥药茶","仅休整节点：恢复5理智，每节点一份。",ItemKind.Consumable),
            new ItemSpec("i13","备用弹匣","选择弃牌堆一张武装牌回手。",ItemKind.Consumable),
            new ItemSpec("i14","速凝封泥","普通封线进度+1；不能推进Boss阶段。",ItemKind.Consumable,1),
            new ItemSpec("i15","海标烟罐","取消一只召唤物本回合的可封缚攻击。",ItemKind.Consumable,bossIds:new[]{"M02","M03","M04","M05"}),
            new ItemSpec("i16","测深铅封","打开一格由公开约束证明安全的盐格，不占常驻次数。",ItemKind.Consumable,1),
            new ItemSpec("i17","盐蚀怀表","主动把未知意图确认为已知时抽1；每回合1次，每场2次。",ItemKind.Equipment,engine:true),
            new ItemSpec("i18","配重枪套","不同武装牌接续命中同一目标，单体武装伤害+2；每回合1次，每场3次。",ItemKind.Equipment,engine:true),
            new ItemSpec("i19","测量罗盘","打出防护牌后完成场景行动，获得4格挡；每回合1次，每场2次。",ItemKind.Equipment,engine:true),
            new ItemSpec("i20","隔音耳罩","每场第一次正数被动理智冲击减少3。",ItemKind.Equipment),
            new ItemSpec("i21","绳钩腰带","每场首次付费正确牵引守卫后返还1行动。",ItemKind.Equipment,bossIds:new[]{"M05"}),
            new ItemSpec("i22","分栏记录夹","付费调查牌实际抽牌后再抽1；每回合1次，每场2次，不递归。",ItemKind.Equipment,engine:true),
            new ItemSpec("i23","铅衬笔记匣","每场首次主动支付理智后获得5格挡。",ItemKind.Equipment),
            new ItemSpec("i24","折叠测量镜","每场首次安全打开非初始盐格后获得4理智防护。",ItemKind.Equipment,bossIds:new[]{"M03"}),
            new ItemSpec("i25","救生牵引带","每场首次付费推进撤离后返还1行动。",ItemKind.Equipment)
        });
        public static ItemSpec Item(string id) => Items.FirstOrDefault(x=>x.Id==id) ?? throw new ArgumentException("Unknown item: "+id);
        public static readonly IReadOnlyList<CrewSupportSpec> Crew = Array.AsReadOnly(new[]
        {
            new CrewSupportSpec("medic","林岚 · 医师",CrewSupportKind.Medic),
            new CrewSupportSpec("listener","祝遥 · 听音员",CrewSupportKind.Listener),
            new CrewSupportSpec("engineer","唐砾 · 轮机长",CrewSupportKind.Engineer),
            new CrewSupportSpec("archivist","季白 · 档案员",CrewSupportKind.Archivist),
            new CrewSupportSpec("navigator","赫沙 · 领航员",CrewSupportKind.Navigator),
            new CrewSupportSpec("gunsmith","顾弦 · 修械师",CrewSupportKind.Gunsmith),
            new CrewSupportSpec("diver","萨迦 · 潜水员",CrewSupportKind.Diver),
            new CrewSupportSpec("cook","闻澜 · 炊事员",CrewSupportKind.Cook),
            new CrewSupportSpec("cartographer","伊文 · 罗盘匠",CrewSupportKind.Cartographer),
            new CrewSupportSpec("decoder","穆恩 · 译码员",CrewSupportKind.Decoder)
        });
        public static CrewSupportSpec CrewMember(string id) => Crew.FirstOrDefault(x=>x.Id==id) ?? throw new ArgumentException("Unknown crew: "+id);
        public static string CrewDescription(CrewSupportKind kind)
        {
            switch(kind)
            {
                case CrewSupportKind.Medic:return "支付1行动，恢复3生命并获得3格挡；安全休整额外恢复2理智。";
                case CrewSupportKind.Listener:return "支付2行动，查询一个新声源并抽1；事件可免费比对录音。";
                case CrewSupportKind.Engineer:return "获得11格挡；首次维修事件少付1补给。";
                case CrewSupportKind.Archivist:return "抽2；档案节点增加一项可选奖励。";
                case CrewSupportKind.Navigator:return "获得5格挡，下一次常驻场景行动少付1；每航程一次额外侦察。";
                case CrewSupportKind.Gunsmith:return "回收一张费用1的非禁忌弃牌；补给站可选物资或恢复航行工具。";
                case CrewSupportKind.Diver:return "获得6格挡，下次推进撤离再获得3理智防护；水下营救免额外补给。";
                case CrewSupportKind.Cook:return "获得7理智防护；安全休整额外恢复3生命。";
                case CrewSupportKind.Cartographer:return "查询场景是否存在连接；航程首次航行工具不耗次。";
                default:return "查询一条允许公开的新唱序并获得4理智防护；没有则获得7；事件可核对序列。";
            }
        }
        public static ToolSpec Tool(string id)
        {
            var item=Item(id); if(item.Kind!=ItemKind.Tool)throw new ArgumentException("Not a battle tool.");
            var kind=id=="lantern"?InvestigationKind.VisibleAction:id=="listener"?InvestigationKind.SoundSource:id=="shell"?InvestigationKind.DamageChannel:id=="i06"?InvestigationKind.Grid:id=="i07"?InvestigationKind.Anchor:InvestigationKind.Relay;
            return new ToolSpec(id,item.Name,kind,sanityCost:id=="shell"?3:0);
        }
    }
    public sealed class BattleLoadout
    {
        public IReadOnlyList<string> Consumables { get; }
        public IReadOnlyList<string> Equipment { get; }
        public bool SurveyedBeacon { get; }
        public bool EchoRitual { get; }
        public int Watched { get; }
        public BattleLoadout(IEnumerable<string> consumables=null,IEnumerable<string> equipment=null,bool surveyedBeacon=false,int watched=0,bool echoRitual=false)
        {
            Consumables=Require.Copy(consumables??Array.Empty<string>()); Equipment=Require.Copy(equipment??Array.Empty<string>());
            if(Consumables.Count>2||Consumables.Any(id=>SupportCatalog.Item(id).Kind!=ItemKind.Consumable||id=="i12"))throw new ArgumentException("Carry at most two battle consumables.");
            if(Equipment.Count>3||Equipment.Distinct().Count()!=Equipment.Count||Equipment.Any(id=>SupportCatalog.Item(id).Kind!=ItemKind.Equipment)||Equipment.Count(id=>SupportCatalog.Item(id).Engine)>1)throw new ArgumentException("Equip three unique items, at most one engine.");
            if(watched<0||watched>3)throw new ArgumentOutOfRangeException(nameof(watched));
            SurveyedBeacon=surveyedBeacon; Watched=watched; EchoRitual=echoRitual;
        }
    }
}
