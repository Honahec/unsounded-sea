using System;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class EnemyIntentDetails : VisualElement
    {
        public EnemyIntentDetails(EnemyView enemy)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("enemy-details");
            if (!string.IsNullOrWhiteSpace(enemy.Clue))
            {
                Add(Text(enemy.Clue.Replace("；", "\n").Replace(";", "\n").Trim(), "enemy-clue-lines"));
            }
            var alternatives = enemy.Intent.Split(new[] { " / ", "／", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var alternative in alternatives)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("enemy-intent-row");
                row.EnableInClassList("confirmed", enemy.IntentConfirmed);
                var text = alternative.Trim();
                var health = text.Contains("生命伤害");
                var sanity = text.Contains("理智冲击");
                if (health)
                {
                    var icon = new SeaUiArt("heart"); icon.AddToClassList("enemy-intent-icon"); row.Add(icon);
                    text = text.Replace("生命伤害", "").Trim();
                }
                if (sanity)
                {
                    var icon = new SeaUiArt("eye"); icon.AddToClassList("enemy-intent-icon"); row.Add(icon);
                    text = text.Replace("理智冲击", "").Trim();
                }
                var split = text.IndexOf('：');
                var separatorLength = 1;
                if (split < 0) { split = text.IndexOf(" · ", StringComparison.Ordinal); separatorLength = 3; }
                if (split < 0) row.Add(Text(text, "enemy-intent-effect"));
                else
                {
                    row.Add(Text(text.Substring(0, split).Trim(), "enemy-intent-action"));
                    row.Add(Text(text.Substring(split + separatorLength).Trim(), "enemy-intent-effect"));
                }
                Add(row);
            }
        }

        private static Label Text(string value, string style)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(style);
            return label;
        }
    }
}
