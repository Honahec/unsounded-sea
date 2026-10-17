using System;
using System.Text.RegularExpressions;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class EnemyIntentDetails : VisualElement
    {
        // Matches "8 点" or "8" so the icon carries the channel and the number stands alone.
        private static readonly Regex Amount = new Regex(@"^(\d+)\s*点?\s*", RegexOptions.Compiled);

        public EnemyIntentDetails(EnemyView enemy)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("enemy-details");
            if (!string.IsNullOrWhiteSpace(enemy.Clue))
            {
                Add(Text(enemy.Clue.Replace("；", "\n").Replace(";", "\n").Trim(), "enemy-clue-lines"));
            }
            var alternatives = (enemy.Intent ?? "").Split(new[] { " / ", "／", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var alternative in alternatives)
            {
                var text = alternative.Replace("（公开）", "").Replace("(公开)", "").Trim();
                if (text.Length == 0) continue;
                var health = text.Contains("生命伤害");
                var sanity = text.Contains("理智冲击");
                text = text.Replace("点生命伤害", "").Replace("点理智冲击", "").Replace("生命伤害", "").Replace("理智冲击", "").Trim();
                var split = text.IndexOf('：');
                var separatorLength = 1;
                if (split < 0) { split = text.IndexOf(" · ", StringComparison.Ordinal); separatorLength = 3; }
                var action = split < 0 ? text : text.Substring(0, split).Trim();
                var effect = split < 0 ? "" : text.Substring(split + separatorLength).Trim();
                var match = Amount.Match(effect);
                var amount = match.Success ? int.Parse(match.Groups[1].Value) : -1;
                var tags = match.Success ? effect.Substring(match.Length).Trim().TrimStart('·').Trim() : "";
                if (match.Success) effect = match.Groups[1].Value;
                // A non-attack has nothing to warn about: confirmed ones vanish, candidates keep only their name.
                if (amount == 0)
                {
                    if (enemy.IntentConfirmed) continue;
                    health = sanity = false;
                    effect = "";
                }
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("enemy-intent-row");
                row.EnableInClassList("confirmed", enemy.IntentConfirmed);
                if (health) row.Add(Icon("heart"));
                if (sanity) row.Add(Icon("eye"));
                row.Add(Text(action, split < 0 && !health && !sanity ? "enemy-intent-effect" : "enemy-intent-action"));
                if (effect.Length > 0) row.Add(Text(effect, "enemy-intent-effect"));
                if (tags.Length > 0) row.Add(Text(tags.Replace(" · ", " "), "enemy-intent-tag"));
                Add(row);
            }
        }

        private static VisualElement Icon(string motif)
        {
            var icon = new SeaUiArt(motif); icon.AddToClassList("enemy-intent-icon"); return icon;
        }

        private static Label Text(string value, string style)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(style);
            return label;
        }
    }
}
