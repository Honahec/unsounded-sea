using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal static class IllustratedCard
    {
        public static void Apply(VisualElement face, CardSpec card)
        {
            var art = Resources.Load<Texture2D>("CardArt/" + (card.Id == "E01-01" ? "c064" : card.Id));
            face.Clear();
            face.style.paddingLeft = face.style.paddingRight = 0;
            face.style.paddingTop = face.style.paddingBottom = 0;
            face.style.overflow = Overflow.Hidden;
            if (art != null) face.style.backgroundImage = new StyleBackground(art);
            face.style.backgroundColor = (Color)new Color32(15, 29, 33, 255);
            var cost = Label(card.Cost.ToString(), 23, new Color32(255, 238, 192, 255));
            cost.name = "illustrated-cost";
            cost.style.position = Position.Absolute;
            cost.style.left = 7; cost.style.top = 7;
            cost.style.width = 36; cost.style.height = 36;
            cost.style.backgroundColor = (Color)new Color32(15, 29, 33, 245);
            cost.style.borderTopLeftRadius = cost.style.borderTopRightRadius = 18;
            cost.style.borderBottomLeftRadius = cost.style.borderBottomRightRadius = 18;
            cost.style.unityTextAlign = TextAnchor.MiddleCenter;
            face.Add(cost);
            var rules = new VisualElement { name = "illustrated-rules" };
            rules.style.position = Position.Absolute;
            rules.style.left = rules.style.right = 9;
            rules.style.bottom = 12;
            rules.style.minHeight = Length.Percent(38);
            rules.style.paddingLeft = rules.style.paddingRight = 8;
            rules.style.paddingTop = rules.style.paddingBottom = 8;
            rules.style.backgroundColor = (Color)new Color32(5, 15, 19, 168);
            rules.style.borderTopLeftRadius = rules.style.borderTopRightRadius = 9;
            rules.style.borderBottomLeftRadius = rules.style.borderBottomRightRadius = 9;
            var title = Label(card.Name, 18, new Color32(246, 222, 171, 255));
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            rules.Add(title);
            var description = Label(card.Description, 14, new Color32(241, 239, 222, 255));
            description.name = "illustrated-description";
            description.style.marginTop = 5;
            description.style.flexShrink = 1;
            rules.Add(description);
            face.Add(rules);
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

        static Label Label(string text, int size, Color color)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginLeft = label.style.marginRight = 0;
            label.style.marginTop = label.style.marginBottom = 0;
            return label;
        }
    }
}
