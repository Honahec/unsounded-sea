using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class EnemyIntentDetails : VisualElement
    {
        public EnemyIntentDetails(EnemyView enemy)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("enemy-details");
            if (!string.IsNullOrWhiteSpace(enemy.Clue))
            {
                Add(Text(enemy.Clue.Replace("；", "\n").Replace(";", "\n").Trim(), "enemy-clue-lines"));
            }
            var alternatives = enemy.Intent.Split(new[] { " / ", "／", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var alternative in alternatives)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("enemy-intent-row");
                row.EnableInClassList("confirmed", enemy.IntentConfirmed);
                var text = alternative.Trim();
                var health = text.Contains("生命伤害");
                var sanity = text.Contains("理智冲击");
                if (health)
                {
                    var icon = new SeaUiArt("heart"); icon.AddToClassList("enemy-intent-icon"); row.Add(icon);
                    text = text.Replace("生命伤害", "").Trim();
                }
                if (sanity)
                {
                    var icon = new SeaUiArt("eye"); icon.AddToClassList("enemy-intent-icon"); row.Add(icon);
                    text = text.Replace("理智冲击", "").Trim();
                }
                var split = text.IndexOf('：');
                var separatorLength = 1;
                if (split < 0) { split = text.IndexOf(" · ", StringComparison.Ordinal); separatorLength = 3; }
                if (split < 0) row.Add(Text(text, "enemy-intent-effect"));
                else
                {
                    row.Add(Text(text.Substring(0, split).Trim(), "enemy-intent-action"));
                    row.Add(Text(text.Substring(split + separatorLength).Trim(), "enemy-intent-effect"));
                }
                Add(row);
            }
        }

        private static Label Text(string value, string style)
        {
            var label = new Label(value) { pickingMode = PickingMode.Position,tooltip=UiRules.Describe(value) };
            label.AddToClassList(style);
            return label;
        }
    }
    internal static class UiRules
    {
        public const string SanityThresholds="动摇（6–12）、临界（1–5）只提示风险，不改变费用、效果或恢复。主动支付后至少剩1理智；理智归零结束航程。防护只能抵御冲击，不能抵销主动支付。";
        private static readonly Dictionary<string,string> Terms=new Dictionary<string,string>
        {
            ["调查接力"]="临时照明、听声辨位、延长观察共享每回合一次返还1行动；先付费，且满足各牌的新答案条件。",
            ["新答案"]="本回合调查牌实际新增该目标的动作、声源或通道答案；重复查询、旧档、工具和船员不计入调查牌联动。",
            ["可打断"]="成功打断取消目标本回合标为可打断的动作；未确认或不可打断时以当前合法目标为准。",
            ["可封缚"]="仅可选择当前规则公开允许封缚的目标；不等同于所有敌人都能封缚。",
            ["已验证"]="已完成规则要求的证据核验；观察、传闻和历史档案不能自动替代当前证据。",
            ["保留"]="指定的非遭遇牌跨过本回合弃牌边界留在手中；打出或回收不延续原保留资格。",
            ["消耗"]="本场战斗从可循环牌堆移除；物资则扣除实际份数。",
            ["主动支付"]="由玩家承担的明确代价；不被格挡、理智防护抵销，支付后资源至少剩1。",
            ["被注视"]="0–3层；禁忌接触可累积，3层会在事件点出现回视处置，换幕降低1层。",
            ["蓄势"]="第二、三幕普通战第4个敌方回合起，正伤害+2；之后每回合再+1。零伤害不变；层数由回合决定，不揭示隐匿分支。"
        };
        public static string Describe(string text)=>text+string.Concat(Terms.Where(x=>text.Contains(x.Key)).Select(x=>"\n"+x.Key+"："+x.Value));
        // The catalog marks mechanism requirements; recommendations and fallback effects do not imply exclusivity.
        private static string BossTag(IReadOnlyList<string> ids)=>ids.Count==0?"":"适用："+string.Join(" / ",ids);
        public static string BossTag(CardSpec card)=>BossTag(card.BossIds);
        public static string BossTag(ItemSpec item)=>BossTag(item.BossIds);
        public static string CardDescription(CardSpec card)=>Describe(card.Name+"："+card.Description)+(BossTag(card)==""?"":"\n"+BossTag(card));
        public static void SanityTicks(VisualElement track,int max)
        {
            foreach(var threshold in new[]{12,5})
            {
                if(max<threshold)continue;
                var tick=new VisualElement {name="sanity-threshold-"+threshold,pickingMode=PickingMode.Position,tooltip=SanityThresholds};
                tick.AddToClassList("sanity-tick");tick.style.left=Length.Percent(100f*threshold/max);
                var label=new Label(threshold==12?"动摇":"临界");label.AddToClassList("sanity-tick-label");tick.Add(label);track.Add(tick);
            }
        }
        public static void Status(VisualElement parent,string name,string motif,string text,string description)
        {
            var badge=new VisualElement {name=name,pickingMode=PickingMode.Position,tooltip=Describe(description)};badge.AddToClassList("status-badge");
            var icon=new SeaUiArt(motif);icon.AddToClassList("status-icon");badge.Add(icon);badge.Add(new Label(text));parent.Add(badge);
        }
    }
    internal static class KeyBadge
    {
        public static void Attach(VisualElement button,string key)
        {
            button.AddToClassList("key-control");
            if(button is Button control&&!string.IsNullOrEmpty(control.text))
            {
                var text=new Label(control.text){pickingMode=PickingMode.Ignore};text.AddToClassList("key-control-title");control.text="";control.Add(text);
            }
            var badge=new Label(key){pickingMode=PickingMode.Ignore};badge.AddToClassList("key-badge");button.Add(badge);
        }
    }
}
