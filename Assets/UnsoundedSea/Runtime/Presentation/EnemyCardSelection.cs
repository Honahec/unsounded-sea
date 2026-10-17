using System;
using System.Linq;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal sealed class EnemyCardSelection
    {
        private int cardId;
        private string first;
        public void Reset() { cardId = 0; first = null; }
        public bool IsSelected(CardView card, string enemy) => cardId == card.InstanceId && first == enemy;
        public bool CanChoose(CardView card, string enemy) => card.Targets.Any(t => t.Enabled &&
            (t.Id == enemy || t.Id == "enemy:" + enemy ||
             t.Id.StartsWith("enemies:", StringComparison.Ordinal) && t.Id.Substring(8).Split(',').Contains(enemy) ||
             t.Id.StartsWith("focus:", StringComparison.Ordinal) && (first == null || cardId != card.InstanceId
                 ? t.Id.StartsWith("focus:" + enemy + "|", StringComparison.Ordinal)
                 : t.Id == "focus:" + first + "|" + enemy)));
        public static bool IsEnemyTarget(string id, BattleView view) =>
            id.StartsWith("enemy:", StringComparison.Ordinal) || id.StartsWith("enemies:", StringComparison.Ordinal) ||
            id.StartsWith("focus:", StringComparison.Ordinal) || view.Enemies.Any(enemy => enemy.Id == id);
        public static string AutomaticTarget(CardView card, BattleView view)
        {
            if (view.Enemies.Count(enemy => enemy.Health > 0) != 1) return null;
            var targets = card.Targets.Where(t => t.Enabled).ToArray();
            return targets.Length == 1 && IsEnemyTarget(targets[0].Id, view) ? targets[0].Id : null;
        }
        public bool Choose(CardView card, string enemy, out string target)
        {
            target = null;
            if (cardId != card.InstanceId) { cardId = card.InstanceId; first = null; }
            var single = card.Targets.FirstOrDefault(t => t.Enabled && (t.Id == enemy || t.Id == "enemy:" + enemy));
            if (single != null) { target = single.Id; Reset(); return true; }
            var options = card.Targets.Where(t => t.Enabled).ToArray();
            if (options.Any(t => t.Id.StartsWith("enemies:", StringComparison.Ordinal)))
            {
                if (first != null)
                {
                    var pair = options.FirstOrDefault(t => t.Id == "enemies:" + first + "," + enemy || t.Id == "enemies:" + enemy + "," + first || first == enemy && t.Id == "enemies:" + first);
                    if (pair == null) return true;
                    target = pair.Id; Reset(); return true;
                }
                if (!options.Any(t => t.Id == "enemies:" + enemy)) return true;
                if (options.Any(t => t.Id.StartsWith("enemies:", StringComparison.Ordinal) && t.Id.Substring(8).Split(',').Length > 1 && t.Id.Substring(8).Split(',').Contains(enemy))) first = enemy;
                else target = "enemies:" + enemy;
                return true;
            }
            if (options.Any(t => t.Id.StartsWith("focus:", StringComparison.Ordinal)))
            {
                if (first == null) { if (options.Any(t => t.Id.StartsWith("focus:" + enemy + "|", StringComparison.Ordinal))) first = enemy; }
                else { var pair = options.FirstOrDefault(t => t.Id == "focus:" + first + "|" + enemy); if (pair != null) { target = pair.Id; Reset(); } }
                return true;
            }
            return false;
        }
    }
}
