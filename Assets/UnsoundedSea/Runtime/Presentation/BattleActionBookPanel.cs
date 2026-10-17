using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class BattleActionBookPanel : VisualElement
    {
        public BattleActionBookPanel(Action close, bool voyageEncounter)
        {
            name = "battle-action-book-overlay";
            AddToClassList("battle-action-book-overlay");
            var sheet = new VisualElement();
            sheet.AddToClassList("battle-action-book-sheet");
            var heading = new VisualElement();
            heading.AddToClassList("battle-action-book-heading");
            var title = new Label("行动簿"); title.AddToClassList("battle-action-book-title"); heading.Add(title);
            var dismiss = new Button(close) { name = "battle-action-book-close", text = "返回 / B" };
            dismiss.AddToClassList("battle-action-book-close"); heading.Add(dismiss); sheet.Add(heading);

            var voyage = voyageEncounter && !VoyageRuntime.IsDebugSession ? VoyageRuntime.Session : null;
            IEnumerable<string> equippedIds = voyage != null ? voyage.DeckIds : CardDeckPreparation.Prepared.Select(card => card.Id);
            var equipped = new HashSet<string>(equippedIds, StringComparer.Ordinal);
            IEnumerable<string> ids = voyage != null ? voyage.LearnedCardIds : equipped;
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("collection-scroll");
            var cards = new VisualElement(); cards.AddToClassList("collection-list"); scroll.Add(cards);
            foreach (var id in ids)
            {
                var card = CardCatalog.Get(id);
                var button = new Button { name = "book-" + id, tooltip = card.Name + "：" + card.Description };
                button.AddToClassList("collection-card"); button.EnableInClassList("selected", equipped.Contains(id));
                var face = new VisualElement(); face.AddToClassList("collection-card-face"); IllustratedCard.Apply(face, card);
                button.Add(face); cards.Add(button);
            }
            sheet.Add(scroll); Add(sheet);
            schedule.Execute(dismiss.Focus);
        }
    }
}
