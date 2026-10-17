using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    internal static class UiKeywords
    {
        private static readonly IReadOnlyDictionary<string, string> Rules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["调查接力"] = "调查接力：临时照明、听声辨位、延长观察满足各自的新答案条件时，返还1行动。三种牌每回合合计触发一次，出牌仍需先付费。",
            ["新答案"] = "新答案：本回合用调查牌首次查明目标的一类问题。重复调查、旧档、工具和船员支援不计入联动。",
            ["可打断"] = "可打断：可用打断效果取消本回合行动。需要先确认意图的牌，只能在确认后使用。",
            ["可封缚"] = "可封缚：该召唤物本回合的攻击可被封缚；封缚不算击退，也不关闭锚点。",
            ["已验证"] = "已验证：已通过查证的证据，可用于需要「已验证」的效果。观察、传闻和旧档仍需进一步查证。",
            ["保留"] = "保留：回合结束时留在手牌中，带到下回合；之后若未再次保留则照常弃置。",
            ["消耗"] = "消耗：使用后移入消耗区，本场不再抽到。物资用一份少一份。",
            ["主动支付"] = "主动支付：直接扣除理智，不能用理智防护抵销。支付后至少剩1理智。",
            ["被注视"] = "被注视：禁忌接触可增加被注视，最多3层。达到3层后，在事件航点需先处理回视；换幕降低1层。",
            ["蓄势"] = "蓄势：第二、三幕普通战中，敌方第4回合起，伤害+2，之后每回合再+1。不造成伤害的行动不变。"
        };

        public static void Attach(VisualElement element, string text)
        {
            if (element == null || string.IsNullOrEmpty(text)) return;
            var notes = new List<string>();
            foreach (var rule in Rules)
                if (text.IndexOf(rule.Key, StringComparison.Ordinal) >= 0) notes.Add(rule.Value);
            if (notes.Count == 0) return;
            var suffix = string.Join("\n", notes);
            element.tooltip = string.IsNullOrEmpty(element.tooltip) ? suffix : element.tooltip + "\n\n" + suffix;
        }
    }
}
