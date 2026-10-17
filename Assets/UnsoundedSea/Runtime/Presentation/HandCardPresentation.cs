using System.Collections.Generic;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    // Restore the fan order beneath the selected and inspected cards.
    internal sealed class HandCardPresentation
    {
        readonly ScrollView hand;
        readonly List<Button> cards = new List<Button>();
        Button hovered, focused, preview;

        public HandCardPresentation(ScrollView hand)
        {
            this.hand = hand;
            hand.schedule.Execute(RefreshStack);
        }

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
            preview = next;
            preview?.AddToClassList("inspecting");
            RefreshStack();
        }

        void RefreshStack()
        {
            foreach (var card in cards) card.BringToFront();
            foreach (var card in cards)
                if (card.ClassListContains("chosen-card")) card.BringToFront();
            preview?.BringToFront();
        }
    }
}
