using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class EnemyIntentDetails : VisualElement
    {
        // Matches "8 点" or "8" so the icon carries the channel and the number stands alone.
        private static readonly Regex Amount = new Regex(@"^(\d+)\s*点?\s*", RegexOptions.Compiled);

        public EnemyIntentDetails(EnemyView enemy)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("enemy-details");
            if (!string.IsNullOrWhiteSpace(enemy.Clue))
            {
                Add(Text(enemy.Clue.Replace("；", "\n").Replace(";", "\n").Trim(), "enemy-clue-lines"));
            }
            var alternatives = (enemy.Intent ?? "").Split(new[] { " / ", "／", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var alternative in alternatives)
            {
                var text = alternative.Replace("（公开）", "").Replace("(公开)", "").Trim();
                if (text.Length == 0) continue;
                var health = text.Contains("生命伤害");
                var sanity = text.Contains("理智冲击");
                text = text.Replace("点生命伤害", "").Replace("点理智冲击", "").Replace("生命伤害", "").Replace("理智冲击", "").Trim();
                var split = text.IndexOf('：');
                var separatorLength = 1;
                if (split < 0) { split = text.IndexOf(" · ", StringComparison.Ordinal); separatorLength = 3; }
                var action = split < 0 ? text : text.Substring(0, split).Trim();
                var effect = split < 0 ? "" : text.Substring(split + separatorLength).Trim();
                var match = Amount.Match(effect);
                var amount = match.Success ? int.Parse(match.Groups[1].Value) : -1;
                var tags = match.Success ? effect.Substring(match.Length).Trim().TrimStart('·').Trim() : "";
                if (match.Success) effect = match.Groups[1].Value;
                // A non-attack has nothing to warn about: confirmed ones vanish, candidates keep only their name.
                if (amount == 0)
                {
                    if (enemy.IntentConfirmed) continue;
                    health = sanity = false;
                    effect = "";
                }
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("enemy-intent-row");
                row.EnableInClassList("confirmed", enemy.IntentConfirmed);
                if (health) row.Add(Icon("heart"));
                if (sanity) row.Add(Icon("eye"));
                row.Add(Text(action, split < 0 && !health && !sanity ? "enemy-intent-effect" : "enemy-intent-action"));
                if (effect.Length > 0) row.Add(Text(effect, "enemy-intent-effect"));
                if (tags.Length > 0) row.Add(Text(tags.Replace(" · ", " "), "enemy-intent-tag"));
                Add(row);
            }
        }

        private static VisualElement Icon(string motif)
        {
            var icon = new SeaUiArt(motif); icon.AddToClassList("enemy-intent-icon"); return icon;
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
        public const string SanityThresholds="理智6–12为动摇，1–5为临界，归零时航程结束。状态不影响费用、效果或恢复。主动支付后至少剩1理智，不能用防护抵销。";
        private static readonly Dictionary<string,string> Terms=new Dictionary<string,string>
        {
            ["调查接力"]="临时照明、听声辨位、延长观察满足各自的新答案条件时，返还1行动。三种牌每回合合计触发一次，出牌仍需先付费。",
            ["新答案"]="本回合用调查牌首次查明目标的一类问题。重复调查、旧档、工具和船员支援不计入联动。",
            ["可打断"]="可用打断效果取消本回合行动。需要先确认意图的牌，只能在确认后使用。",
            ["可封缚"]="可用封缚效果阻止这只召唤物本回合的攻击。封缚不会击退它，也不会关闭锚点。",
            ["已验证"]="已通过查证的证据，可用于需要「已验证」的效果。观察、传闻和旧档仍需进一步查证。",
            ["保留"]="选中的手牌在回合结束时不弃置，留到下回合。打出或回收后，原有保留效果失效。",
            ["消耗"]="卡牌本场不再抽到；物资用一份少一份。",
            ["主动支付"]="直接扣除资源，不能用格挡或理智防护抵销。支付后至少剩1。",
            ["被注视"]="禁忌接触可增加被注视，最多3层。达到3层后，在事件航点需先处理回视；换幕降低1层。",
            ["蓄势"]="第二、三幕普通战中，敌方第4回合起，伤害+2，之后每回合再+1。不造成伤害的行动不变。"
        };
        public static string Describe(string text)=>text+string.Concat(Terms.Where(x=>text.Contains(x.Key)).Select(x=>"\n"+x.Key+"："+x.Value));
        // The catalog marks mechanism requirements; recommendations and fallback effects do not imply exclusivity.
        private static string BossTag(IReadOnlyList<string> ids)=>ids.Count==0?"":"适用："+string.Join(" / ",ids);
        public static string BossTag(CardSpec card)=>BossTag(card.BossIds);
        public static string BossTag(ItemSpec item)=>BossTag(item.BossIds);
        public static string CardDescription(CardSpec card)=>Describe(card.Name+"："+card.Description)+(BossTag(card)==""?"":"\n"+BossTag(card));
        public static void Status(VisualElement parent,string name,string motif,string text,string description)
        {
            var badge=new VisualElement {name=name,pickingMode=PickingMode.Position,tooltip=Describe(description)};badge.AddToClassList("status-badge");
            var icon=new SeaUiArt(motif);icon.AddToClassList("status-icon");badge.Add(icon);badge.Add(new Label(text));parent.Add(badge);
        }
    }
}
