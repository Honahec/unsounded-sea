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
            if (art == null) return;
            face.Clear();
            face.style.paddingLeft = face.style.paddingRight = 0;
            face.style.paddingTop = face.style.paddingBottom = 0;
            face.style.overflow = Overflow.Hidden;
            face.style.backgroundImage = new StyleBackground(art);
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
            rules.style.minHeight = Length.Percent(35);
            rules.style.paddingLeft = rules.style.paddingRight = 8;
            rules.style.paddingTop = rules.style.paddingBottom = 8;
            rules.style.backgroundColor = (Color)new Color32(5, 15, 19, 168);
            rules.style.borderTopLeftRadius = rules.style.borderTopRightRadius = 9;
            rules.style.borderBottomLeftRadius = rules.style.borderBottomRightRadius = 9;
            rules.Add(Label(card.Name, 18, new Color32(246, 222, 171, 255)));
            var description = Label(card.Description, 14, new Color32(241, 239, 222, 255));
            description.name = "illustrated-description";
            description.style.marginTop = 5;
            rules.Add(description);
            if (card.SanityCost > 0 || card.Exhaust)
                rules.Add(Label((card.SanityCost > 0 ? $"支付 {card.SanityCost} 理智 " : "") + (card.Exhaust ? "· 消耗" : ""), 11, new Color32(235, 182, 153, 255)));
            face.Add(rules);
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
