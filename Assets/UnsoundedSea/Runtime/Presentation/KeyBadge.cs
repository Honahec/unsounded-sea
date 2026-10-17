using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    /// <summary>Shows a button's shortcut as a small keycap instead of "名称 / M" text.</summary>
    internal static class KeyBadge
    {
        public static Button Attach(Button button, string key)
        {
            var title = button.text;
            button.text = string.Empty;
            button.AddToClassList("has-key");
            var label = new Label(title) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("key-title");
            button.Add(label);
            button.Add(Cap(key));
            if (string.IsNullOrEmpty(button.tooltip)) button.tooltip = title + "（" + key + "）";
            return button;
        }

        public static Label Cap(string key)
        {
            var cap = new Label(key) { pickingMode = PickingMode.Ignore };
            cap.AddToClassList("key-badge");
            return cap;
        }
    }
}
