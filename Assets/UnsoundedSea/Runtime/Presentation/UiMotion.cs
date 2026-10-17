using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    // Presentation only: no random numbers, commands, or delayed gameplay callbacks.
    internal static class UiMotion
    {
        sealed class State
        {
            public HashSet<string> visible = new HashSet<string>();
            public readonly Dictionary<string, VisualElement> outgoing = new Dictionary<string, VisualElement>();
        }
        static readonly ConditionalWeakTable<VisualElement, State> states = new ConditionalWeakTable<VisualElement, State>();
        static readonly string[] panels = { "ocean-sheet", "pending-choice-sheet", "card-inspection-face", "action-book", "settings",
            "pause", "menu-column", "debug-selection", "card-collection", "voyage-reward", "voyage-arrival", "voyage-options",
            "voyage-chart", "battle-chart-overlay", "battle-action-book-sheet", "card-target-panel", "support-sheet", "destination-panel", "reward-cards", "support-manifest", "voyage-overlay" };
        static readonly string[] overlays = { "ocean-modal", "battle-action-book-overlay", "battle-chart-overlay", "pending-choice", "card-inspection", "voyage-overlay", "pause-scrim", "settings", "card-collection" };

        public static void BeforeBuild(VisualElement root)
        {
            var state = states.GetOrCreateValue(root); state.outgoing.Clear();
            if (GameSettings.ReducedMotion) return;
            foreach (var cls in overlays)
            {
                var panel = root.Q(className: cls);
                if (panel?.parent == root && !panel.ClassListContains("ui-departing")) state.outgoing[cls] = panel;
            }
        }

        public static void AfterBuild(VisualElement root)
        {
            var state = states.GetOrCreateValue(root);
            var next = new HashSet<string>();
            foreach (var cls in panels)
            {
                var panel = root.Q(className: cls);
                if (panel == null) continue;
                next.Add(cls);
                if (!state.visible.Contains(cls)) Enter(panel);
                if (cls == "reward-cards" && !state.visible.Contains(cls))
                {
                    int index = 0;
                    panel.Query<Button>(className: "reward-card").ForEach(card => Enter(card, index++ * .055f));
                }
            }
            state.visible = next;
            foreach (var entry in state.outgoing)
            {
                if (root.Q(className: entry.Key) != null || GameSettings.ReducedMotion) continue;
                var panel = entry.Value;
                // Closing decoration must not intercept pointer, keyboard or scroll input.
                panel.AddToClassList("ui-departing"); panel.pickingMode = PickingMode.Ignore; panel.focusable = false;
                panel.Query<VisualElement>().ForEach(x => { x.pickingMode = PickingMode.Ignore; x.focusable = false; });
                root.Add(panel);
                float start = Time.unscaledTime;
                IVisualElementScheduledItem item = null;
                item = panel.schedule.Execute(() =>
                {
                    float t = GameSettings.ReducedMotion ? 1 : Mathf.Clamp01((Time.unscaledTime - start) / .12f);
                    panel.style.opacity = 1 - Ease(t);
                    if (t >= 1) { item?.Pause(); panel.RemoveFromHierarchy(); }
                }).Every(16);
            }
            state.outgoing.Clear();
        }

        public static void Enter(VisualElement element, float delay = 0)
        {
            if (GameSettings.ReducedMotion) return;
            float start = Time.unscaledTime;
            element.style.opacity = 0;
            // Only opacity here: panels may already own percentage-based centering transforms.
            IVisualElementScheduledItem item = null;
            item = element.schedule.Execute(() =>
            {
                var t = GameSettings.ReducedMotion ? 1 : Mathf.Clamp01((Time.unscaledTime - start - delay) / .2f);
                element.style.opacity = Ease(t);
                if (t >= 1) { element.style.opacity = StyleKeyword.Null; item?.Pause(); }
            }).Every(16);
        }

        public static float Ease(float t) => 1 - Mathf.Pow(1 - Mathf.Clamp01(t), 3);
        public static Vector2 Curve(Vector2 from, Vector2 to, float t, float lift)
        {
            var p = Vector2.LerpUnclamped(from, to, Ease(t));
            return p + Vector2.up * (-Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * lift);
        }
    }
}
