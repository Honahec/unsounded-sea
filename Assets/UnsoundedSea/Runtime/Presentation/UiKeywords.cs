using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    internal static class UiKeywords
    {
        private static readonly IReadOnlyDictionary<string, string> Rules = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["调查接力"] = "调查接力：临时照明、听声辨位与延长观察共享；满足各自的新答案条件后返还1行动，每回合最多触发一次，仍须先支付费用。",
            ["新答案"] = "新答案：本回合由调查牌首次取得该目标尚未回答的一类问题答案。",
            ["可打断"] = "可打断：该行动被确认后，可由写明“取消可打断行动”的效果取消本回合行动。",
            ["可封缚"] = "可封缚：该召唤物本回合的攻击可被封缚；封缚不算击退，也不关闭锚点。",
            ["已验证"] = "已验证：证据已由规则要求的来源或组合核验，可供明确要求已验证证据的效果使用。",
            ["保留"] = "保留：回合结束时留在手牌中，带到下回合；之后若未再次保留则照常弃置。",
            ["消耗"] = "消耗：使用后移入消耗区，本场战斗不再进入弃牌堆或重新抽到。",
            ["主动支付"] = "主动支付：直接扣除理智，理智防护不能抵销；支付后必须至少剩1理智。",
            ["被注视"] = "被注视：航程中保留的0–3层风险记录；部分自愿事件会增加它，换幕时降低1。",
            ["蓄势"] = "蓄势：敌人按其规则累积的公开层数；具体触发结果以该敌人的意图与提示为准。"
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
