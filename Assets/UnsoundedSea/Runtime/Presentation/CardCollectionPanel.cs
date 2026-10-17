using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class CardCollectionPanel : VisualElement
    {
        private readonly Action close;
        private readonly VisualElement list;
        private CardSpec inspected;
        private int page;

        public CardCollectionPanel(Action close)
        {
            this.close = close;
            AddToClassList("card-collection");

            var head = new VisualElement(); head.AddToClassList("collection-head");
            head.Add(Label("卡牌图鉴", "collection-title")); Add(head);

            var body = new VisualElement(); body.AddToClassList("collection-body");
            var browser = new VisualElement(); browser.AddToClassList("collection-browser");
            var cardScroll = new ScrollView(ScrollViewMode.Vertical); cardScroll.AddToClassList("collection-scroll");
            list = new VisualElement(); list.AddToClassList("collection-list"); cardScroll.Add(list); browser.Add(cardScroll);
            var pages = new VisualElement(); pages.AddToClassList("collection-pages");
            pages.Add(new Button(() => ChangePage(-1)) { name = "collection-previous", text = "上一页" });
            pages.Add(new Button(() => ChangePage(1)) { name = "collection-next", text = "下一页" });
            browser.Add(pages); body.Add(browser);
            inspected = CardCatalog.All.First();
            body.Add(Detail()); Add(body);
            RenderPage();
        }

        private void ChangePage(int direction)
        {
            page = Math.Max(0, Math.Min((CardCatalog.All.Count - 1) / 9, page + direction));
            RenderPage();
            Resources.UnloadUnusedAssets();
        }

        private void RenderPage()
        {
            list.Clear();
            foreach (var current in CardCatalog.All.Skip(page * 9).Take(9))
            {
                var card = current;
                var button = new Button(() => Inspect(card)) { name = "collection-" + card.Id, tooltip = card.Name + "：" + card.Description };
                button.AddToClassList("collection-card");
                var face = new VisualElement(); face.AddToClassList("collection-card-face"); IllustratedCard.Apply(face, card); button.Add(face); list.Add(button);
            }
            var pages = this.Q<VisualElement>(className: "collection-pages");
            pages.Q<Button>("collection-previous").SetEnabled(page > 0);
            pages.Q<Button>("collection-next").SetEnabled((page + 1) * 9 < CardCatalog.All.Count);
        }

        private VisualElement Detail()
        {
            var detail = new ScrollView(ScrollViewMode.Vertical) { name = "collection-detail" }; detail.AddToClassList("collection-detail");
            var face = new VisualElement(); face.AddToClassList("collection-detail-card"); IllustratedCard.Apply(face, inspected); detail.Add(face);
            detail.Add(new Button(close) { name = "collection-close", text = "返回" });
            return detail;
        }

        private void Inspect(CardSpec card)
        {
            inspected = card;
            var old = this.Q<VisualElement>("collection-detail");
            var replacement = Detail(); old.parent.Insert(old.parent.IndexOf(old), replacement); old.RemoveFromHierarchy();
            replacement.Q<Button>("collection-close")?.Focus();
        }


        private static Label Label(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(className); return label;
        }
    }
}
