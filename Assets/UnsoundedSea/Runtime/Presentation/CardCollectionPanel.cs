using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class CardCollectionPanel : VisualElement
    {
        private readonly HashSet<string> selected;
        private readonly Action close;
        private readonly Label count;
        private readonly VisualElement list;
        private CardSpec inspected;
        private int page;
        private string selectedCrew;

        public CardCollectionPanel(Action close)
        {
            this.close = close;
            selected = new HashSet<string>(CardDeckPreparation.Prepared.Select(card => card.Id));
            selectedCrew = CardDeckPreparation.CrewId;
            AddToClassList("card-collection");

            var head = new VisualElement(); head.AddToClassList("collection-head");
            head.Add(Label("行动簿", "collection-title"));
            head.Add(Label("全部 96 种方案用于独立遭遇配牌。三幕远征从固定 12 张起步，途中学习与整备；此处船员选择用于新航程。", "collection-copy"));
            count = Label("", "collection-count"); head.Add(count); Add(head);

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
            RenderPage(); UpdateCount();
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
                button.EnableInClassList("selected", selected.Contains(card.Id));
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
            var crew = new VisualElement(); crew.AddToClassList("collection-crew");
            crew.Add(Label("初始船员", "collection-crew-title"));
            var medic = new Button(() => SelectCrew("medic")) { name = "crew-medic", text = "医护员 · 恢复生命" };
            var listener = new Button(() => SelectCrew("listener")) { name = "crew-listener", text = "听测员 · 记录声源" };
            crew.Add(medic); crew.Add(listener); detail.Add(crew); UpdateCrew(medic, listener);
            var toggle = new Button(Toggle) { name = "collection-toggle" }; toggle.AddToClassList("collection-toggle"); detail.Add(toggle);
            var save = new Button(SaveAndClose) { name = "collection-save", text = "保存行动簿" };
            save.SetEnabled(selected.Count == CardDeckPreparation.DeckSize); detail.Add(save);
            detail.Add(new Button(close) { name = "collection-close", text = "取消" });
            UpdateToggle(toggle); return detail;
        }

        private void Inspect(CardSpec card)
        {
            inspected = card;
            var old = this.Q<VisualElement>("collection-detail");
            var replacement = Detail(); old.parent.Insert(old.parent.IndexOf(old), replacement); old.RemoveFromHierarchy();
            replacement.Q<Button>("collection-toggle")?.Focus();
        }

        private void Toggle()
        {
            if (!selected.Remove(inspected.Id))
            {
                if (selected.Count >= CardDeckPreparation.DeckSize) return;
                selected.Add(inspected.Id);
            }
            this.Q<Button>("collection-" + inspected.Id)?.EnableInClassList("selected", selected.Contains(inspected.Id));
            UpdateToggle(this.Q<Button>("collection-toggle")); UpdateCount();
        }

        private void SaveAndClose()
        {
            if (selected.Count != CardDeckPreparation.DeckSize) return;
            CardDeckPreparation.Save(selected); CardDeckPreparation.SaveCrew(selectedCrew); close();
        }

        private void SelectCrew(string id)
        {
            selectedCrew = id;
            UpdateCrew(this.Q<Button>("crew-medic"), this.Q<Button>("crew-listener"));
        }

        private void UpdateCrew(Button medic, Button listener)
        {
            medic.EnableInClassList("selected", selectedCrew == "medic");
            listener.EnableInClassList("selected", selectedCrew == "listener");
        }

        private void UpdateCount()
        {
            count.text = $"已编入 {selected.Count} / {CardDeckPreparation.DeckSize}";
            this.Q<Button>("collection-save")?.SetEnabled(selected.Count == CardDeckPreparation.DeckSize);
        }
        private void UpdateToggle(Button button)
        {
            var included = selected.Contains(inspected.Id);
            button.text = included ? "移出行动簿" : selected.Count >= CardDeckPreparation.DeckSize ? "请先移出一张" : "编入行动簿";
            button.SetEnabled(included || selected.Count < CardDeckPreparation.DeckSize);
        }

        private static Label Label(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore }; label.AddToClassList(className); return label;
        }
    }
}
