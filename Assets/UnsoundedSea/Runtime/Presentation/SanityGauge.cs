using UnityEngine;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    internal static class SanityGauge
    {
        public const string RuleTooltip = "理智13–30为稳定，6–12为动摇，1–5为临界，0时无法继续。阈值只提供决策提示，不改变费用、效果或伤害。";

        public static void AddMarks(VisualElement track, int max)
        {
            if (track == null || max <= 0) return;
            AddMark(track, 12, max, "动摇");
            AddMark(track, 5, max, "临界");
            track.tooltip = RuleTooltip;
        }

        public static string State(int value) => value <= 0 ? "无法继续" : value <= 5 ? "临界" : value <= 12 ? "动摇" : "稳定";

        private static void AddMark(VisualElement track, int value, int max, string label)
        {
            var mark = new VisualElement { name = "sanity-mark-" + value, pickingMode = PickingMode.Ignore };
            mark.AddToClassList("sanity-threshold");
            mark.style.left = Length.Percent(Mathf.Clamp01(value / (float)max) * 100);
            var text = new Label(label) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("sanity-threshold-label");
            mark.Add(text);
            track.Add(mark);
        }
    }
}
