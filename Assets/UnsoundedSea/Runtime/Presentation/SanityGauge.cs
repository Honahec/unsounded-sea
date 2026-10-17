using UnityEngine;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    /// <summary>Single source for sanity thresholds: unlabeled ticks on a bar plus a state word when not stable.</summary>
    internal static class SanityGauge
    {
        public const string RuleTooltip = "理智 13–30 稳定，6–12 动摇，1–5 临界，归零时航程结束。状态不影响费用、效果或伤害；主动支付后至少剩 1。";

        public static void AddMarks(VisualElement track, int max)
        {
            if (track == null || max <= 0) return;
            foreach (var value in new[] { 12, 5 })
            {
                if (max < value) continue;
                var mark = new VisualElement { name = "sanity-mark-" + value, pickingMode = PickingMode.Ignore };
                mark.AddToClassList("sanity-threshold");
                mark.style.left = Length.Percent(Mathf.Clamp01(value / (float)max) * 100);
                track.Add(mark);
            }
            track.tooltip = RuleTooltip;
        }

        public static string State(int value) => value <= 0 ? "无法继续" : value <= 5 ? "临界" : value <= 12 ? "动摇" : "稳定";

        /// <summary>Only unstable states deserve screen space; 稳定 stays in the tooltip.</summary>
        public static Label StateTag(int value)
        {
            var state = State(value);
            if (state == "稳定") return null;
            var tag = new Label(state) { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("sanity-state");
            tag.EnableInClassList("critical", value <= 5);
            return tag;
        }
    }
}
