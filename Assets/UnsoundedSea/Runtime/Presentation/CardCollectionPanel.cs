using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class CardCollectionPanel : VisualElement
    {
        // Leave room for UI Toolkit's panel-to-pixel rounding at fractional scales.
        private const float CardMaxHeight = 225, CardMinHeight = 170, Gap = 10;
        private const float FitSlack = 1;
        private float cardWidth = 150, cardHeight = 225;
        private readonly Action close;
        private readonly VisualElement grid;
        private readonly Label pageLabel;
        private readonly Button previous, next;
        private CardSpec inspected;
        private int page, columns = 1, rows = 1;

        private int PageSize => columns * rows;
        private int PageCount => Math.Max(1, (CardCatalog.All.Count + PageSize - 1) / PageSize);

        public CardCollectionPanel(Action close)
        {
            this.close = close;
            AddToClassList("card-collection");

            var head = new VisualElement(); head.AddToClassList("collection-head");
            head.Add(Label("卡牌图鉴", "collection-title")); Add(head);

            var body = new VisualElement(); body.AddToClassList("collection-body");
            var browser = new VisualElement(); browser.AddToClassList("collection-browser");
            grid = new VisualElement { name = "collection-grid" }; grid.AddToClassList("collection-grid");
            browser.Add(grid); body.Add(browser);
            inspected = CardCatalog.All.First();
            body.Add(Detail()); Add(body);

            // One footer row: paging on the left/centre, leaving the collection on the right.
            var footer = new VisualElement(); footer.AddToClassList("collection-footer");
            previous = new Button(() => ChangePage(-1)) { name = "collection-previous", text = "上一页" };
            pageLabel = Label("", "collection-page-label");
            next = new Button(() => ChangePage(1)) { name = "collection-next", text = "下一页" };
            var back = new Button(close) { name = "collection-close", text = "返回" };
            back.AddToClassList("collection-back");
            footer.Add(previous); footer.Add(pageLabel); footer.Add(next); footer.Add(KeyBadge.Attach(back, "Esc"));
            Add(footer);

            browser.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            RegisterCallback<WheelEvent>(evt => { ChangePage(evt.delta.y > 0 ? 1 : -1); evt.StopPropagation(); });
            RenderPage();
        }

        private void Fit()
        {
            // Only the browser defines available space; card content must not determine page capacity.
            var width = grid.parent.contentRect.width; var height = grid.parent.contentRect.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0) return;
            var fitRows = Mathf.Max(1, Mathf.RoundToInt(height / (CardMaxHeight + Gap)));
            fitRows = Mathf.Min(fitRows, Mathf.Max(1, Mathf.FloorToInt(height / (CardMinHeight + Gap + FitSlack))));
            var fitHeight = Mathf.Floor(Mathf.Min(CardMaxHeight, height / fitRows - Gap - FitSlack));
            var fitWidth = Mathf.Floor(fitHeight * 2f / 3f);
            var fitColumns = Mathf.Max(1, Mathf.FloorToInt(width / (fitWidth + Gap + FitSlack)));
            var sized = !Mathf.Approximately(fitHeight, cardHeight) || !Mathf.Approximately(fitWidth, cardWidth);
            cardHeight = fitHeight; cardWidth = fitWidth;
            if (!sized && fitColumns == columns && fitRows == rows) return;
            var first = page * PageSize;
            columns = fitColumns; rows = fitRows;
            page = Mathf.Min(first / PageSize, PageCount - 1);
            RenderPage();
        }

        private void ChangePage(int direction)
        {
            var target = Math.Max(0, Math.Min(PageCount - 1, page + direction));
            if (target == page) return;
            page = target;
            RenderPage();
        }

        private void RenderPage()
        {
            grid.Clear();
            // Explicit rows avoid flex-wrap dropping a column when rounded margins exceed the grid width.
            VisualElement row = null;
            var index = 0;
            foreach (var current in CardCatalog.All.Skip(page * PageSize).Take(PageSize))
            {
                if (index++ % columns == 0)
                {
                    row = new VisualElement(); row.AddToClassList("collection-grid-row");
                    row.style.width = columns * (cardWidth + Gap + FitSlack); grid.Add(row);
                }
                var card = current;
                var applicability = BossApplicability.CardLabel(card);
                var button = new Button(() => Inspect(card)) { name = "collection-" + card.Id, tooltip = card.Name + "：" + card.Description + (string.IsNullOrEmpty(applicability) ? "" : "\n" + applicability) };
                UiKeywords.Attach(button, card.Description);
                button.AddToClassList("collection-card");
                button.style.width = cardWidth; button.style.height = cardHeight;
                button.EnableInClassList("selected", card == inspected);
                var face = new VisualElement(); face.AddToClassList("collection-card-face"); IllustratedCard.Apply(face, card); button.Add(face); row.Add(button);
            }
            pageLabel.text = $"{page + 1} / {PageCount}";
            previous.SetEnabled(page > 0);
            next.SetEnabled(page < PageCount - 1);
        }

        private VisualElement Detail()
        {
            var detail = new ScrollView(ScrollViewMode.Vertical) { name = "collection-detail" }; detail.AddToClassList("collection-detail");
            var face = new VisualElement(); face.AddToClassList("collection-detail-card"); IllustratedCard.Apply(face, inspected); detail.Add(face);
            face.Q<Label>("illustrated-description").text = inspected.Description;
            return detail;
        }

        private void Inspect(CardSpec card)
        {
            inspected = card;
            var old = this.Q<VisualElement>("collection-detail");
            var replacement = Detail(); old.parent.Insert(old.parent.IndexOf(old), replacement); old.RemoveFromHierarchy();
            foreach (var button in grid.Query<Button>(className: "collection-card").ToList())
                button.EnableInClassList("selected", button.name == "collection-" + card.Id);
        }

        private static Label Label(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(className); return label;
        }
    }
}
