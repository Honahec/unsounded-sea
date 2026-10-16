using System.Collections.Generic;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    // Keep the resting fan stable; only the inspected card is drawn above its neighbours.
    internal sealed class HandCardPresentation
    {
        readonly ScrollView hand;
        readonly List<Button> cards = new List<Button>();
        Button hovered, focused, preview;

        public HandCardPresentation(ScrollView hand) { this.hand = hand; }

        public void Add(Button card)
        {
            cards.Add(card);
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
            // Restore canonical order before promoting the new preview. Explicit tabIndex
            // keeps keyboard navigation independent of this temporary drawing order.
            foreach (var card in cards) card.BringToFront();
            preview = next;
            if (preview == null) return;
            preview.AddToClassList("inspecting");
            preview.BringToFront();
        }
    }
}
