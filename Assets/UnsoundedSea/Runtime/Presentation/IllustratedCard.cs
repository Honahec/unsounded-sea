using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal static class IllustratedCard
    {
        public static void Apply(VisualElement face, CardSpec card)
        {
            var art = Resources.Load<Texture2D>("CardArt/" + card.Id);
            face.Clear();
            face.AddToClassList("illustrated-card");
            if (art != null) face.style.backgroundImage = new StyleBackground(art);

            var cost = Label(card.Cost.ToString(), "illustrated-cost");
            cost.name = "illustrated-cost";
            face.Add(cost);

            var rules = new VisualElement { name = "illustrated-rules" };
            rules.AddToClassList("illustrated-rules");
            rules.Add(Label(card.Name, "illustrated-title"));
            var description = Label(card.Description, "illustrated-description");
            description.name = "illustrated-description";
            rules.Add(description);
            var applicability = BossApplicability.CardLabel(card);
            if (!string.IsNullOrEmpty(applicability)) rules.Add(Label(applicability, "boss-applicability"));
            face.Add(rules);

            face.tooltip = card.Name + "：" + card.Description + (string.IsNullOrEmpty(applicability) ? "" : "\n" + applicability);
            UiKeywords.Attach(face, card.Description);
        }
        public static VisualElement Inspection(CardSpec card, System.Action close)
        {
            var overlay = new VisualElement { name = "card-inspection" };
            overlay.AddToClassList("card-inspection");
            var face = new VisualElement(); face.AddToClassList("card-inspection-face"); Apply(face, card); overlay.Add(face);
            var dismiss = new Button(close) { name = "card-inspection-close", text = "返回" };
            dismiss.AddToClassList("card-inspection-close"); overlay.Add(dismiss);
            overlay.RegisterCallback<NavigationMoveEvent>(evt => { dismiss.Focus(); evt.StopPropagation(); });
            overlay.schedule.Execute(dismiss.Focus);
            return overlay;
        }

        private static Label Label(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
    }
}
