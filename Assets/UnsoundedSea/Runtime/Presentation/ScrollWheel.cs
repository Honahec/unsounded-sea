using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    internal static class ScrollWheel
    {
        public static void ForwardFrom(VisualElement surface, ScrollView scroll)
        {
            surface.pickingMode = PickingMode.Position;
            void OnWheel(WheelEvent evt)
            {
                // Padding and non-pickable labels otherwise send the wheel past the scroll view.
                for (var element = evt.target as VisualElement; element != null && element != surface; element = element.parent)
                    if (element is ScrollView) return;
                if (!scroll.enabledInHierarchy || scroll.verticalScroller.highValue <= 0 || evt.delta.y == 0) return;
                scroll.verticalScroller.value += evt.delta.y * scroll.mouseWheelScrollSize;
                evt.StopPropagation();
            }
            surface.RegisterCallback<WheelEvent>(OnWheel);
            scroll.RegisterCallback<DetachFromPanelEvent>(_ => surface.UnregisterCallback<WheelEvent>(OnWheel));
        }
    }
}
