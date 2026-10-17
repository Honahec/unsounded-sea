using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    // Keep the resting fan stable; hover and focus only lift a face without changing overlap order.
    internal sealed class HandCardPresentation
    {
        readonly ScrollView hand;
        Button hovered, focused, preview;

        public HandCardPresentation(ScrollView hand) { this.hand = hand; }

        public void Add(Button card)
        {
            card.RegisterCallback<PointerEnterEvent>(_ => { hovered = card; Refresh(); }, CallbackOptions.IncludeDisabled);
            card.RegisterCallback<PointerLeaveEvent>(_ => { if (hovered == card) hovered = null; Refresh(); }, CallbackOptions.IncludeDisabled);
            card.RegisterCallback<FocusInEvent>(_ =>
            {
                focused = card;
                // Vertical scrolling would pull the deliberately hidden lower half into view.
                var offset = hand.scrollOffset;
                hand.ScrollTo(card);
                hand.scrollOffset = new UnityEngine.Vector2(hand.scrollOffset.x, offset.y);
                Refresh();
            });
            card.RegisterCallback<FocusOutEvent>(_ => { if (focused == card) focused = null; Refresh(); });
        }

        void Refresh()
        {
            var next = hovered ?? focused;
            if (next == preview) return;
            preview?.RemoveFromClassList("inspecting");
            preview = next;
            preview?.AddToClassList("inspecting");
        }
    }
}
