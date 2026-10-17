using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class BattleActionBookPanel : VisualElement
    {
        public BattleActionBookPanel(Action close, bool voyageEncounter, IEnumerable<ToolView> preparedTools)
        {
            name = "battle-action-book-overlay";
            AddToClassList("battle-action-book-overlay");
            var sheet = new VisualElement();
            sheet.AddToClassList("battle-action-book-sheet");
            var title = new Label("行动簿"); title.AddToClassList("battle-action-book-title"); sheet.Add(title);

            var voyage = voyageEncounter && !VoyageRuntime.IsDebugSession ? VoyageRuntime.Session : null;
            IEnumerable<string> equippedIds = voyage != null ? voyage.DeckIds : CardCatalog.Starter.Select(card => card.Id);
            var equipped = new HashSet<string>(equippedIds, StringComparer.Ordinal);
            IEnumerable<string> ids = voyage != null ? voyage.LearnedCardIds : equipped;
            var body = new VisualElement(); body.AddToClassList("battle-action-book-body"); sheet.Add(body);
            var side = new VisualElement(); side.AddToClassList("battle-action-book-side"); body.Add(side);
            var tools = (voyage != null
                ? voyage.ToolIds.Select(VoyageEncounterCatalog.ResolveTool)
                : (preparedTools ?? Array.Empty<ToolView>()).Select(tool => tool.Definition)).ToArray();
            var toolTitle = new Label($"调查工具 {tools.Length}/2"); toolTitle.AddToClassList("battle-action-book-tool-title"); side.Add(toolTitle);
            foreach (var tool in tools)
            {
                var label = new Label(tool.Name); label.AddToClassList("battle-action-book-tool"); side.Add(label);
            }
            var dismiss = new Button(close) { name = "battle-action-book-close", text = "返回 / B" };
            dismiss.AddToClassList("battle-action-book-close"); side.Add(dismiss);

            var main = new VisualElement(); main.AddToClassList("battle-action-book-main"); body.Add(main);
            var count = new Label($"{equipped.Count}/12"); count.AddToClassList("collection-count"); main.Add(count);
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("collection-scroll");
            ScrollWheel.ForwardFrom(sheet, scroll);
            var cards = new VisualElement(); cards.AddToClassList("collection-list"); scroll.Add(cards);
            foreach (var id in ids)
            {
                var card = CardCatalog.Get(id);
                var button = new Button { name = "book-" + id, tooltip = card.Name + "：" + card.Description };
                button.AddToClassList("collection-card"); button.EnableInClassList("selected", equipped.Contains(id));
                var face = new VisualElement(); face.AddToClassList("collection-card-face"); IllustratedCard.Apply(face, card);
                button.Add(face); cards.Add(button);
            }
            main.Add(scroll); Add(sheet);
            schedule.Execute(dismiss.Focus);
        }
    }
}
