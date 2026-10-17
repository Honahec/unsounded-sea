using System;
using System.Linq;

namespace UnsoundedSea.Core
{
    public enum EnemyStyle { None = -1, Rhythm = 0, Hidden = 1, Boss = 2 }

    public static class EnemyBehavior
    {
        public static EnemyStyle StyleForEncounter(string encounterId)
        {
            if (TryVoyage(encounterId, out var region, out var variant))
                return region == "fog" ? EnemyStyle.None : variant == 0 || variant == 3 ? EnemyStyle.Rhythm : EnemyStyle.Hidden;
            switch (encounterId)
            {
                case "fog-visitors": case "storm-gauntlet": case "hollow-beacon":
                case "m02": case "m03": case "m04": case "m05": case "drowned-bell":
                    return EnemyStyle.Boss;
                default: return EnemyStyle.None;
            }
        }

        public static EnemyStyle StyleForEnemy(string enemyId, IntentPolicy fallback)
        {
            if (!TryVoyage(enemyId, out var region, out var variant) || region == "fog")
                return fallback == IntentPolicy.Cycle ? EnemyStyle.Rhythm : EnemyStyle.Hidden;
            return variant == 0 || variant == 3 ? EnemyStyle.Rhythm : EnemyStyle.Hidden;
        }

        public static bool IsSecondaryObjective(string encounterId) =>
            TryVoyage(encounterId, out var region, out var variant) && region != "fog" && variant == 3;

        public static ObjectiveKind SecondaryObjectiveKind(string encounterId)
        {
            if (!IsSecondaryObjective(encounterId)) return ObjectiveKind.Defeat;
            return encounterId.IndexOf("bones", StringComparison.Ordinal) >= 0 ||
                   encounterId.IndexOf("abyss", StringComparison.Ordinal) >= 0
                ? ObjectiveKind.Survive : ObjectiveKind.Seal;
        }

        internal static bool IsNonFogOrdinary(string id) =>
            TryVoyage(id, out var region, out _) && region != "fog";

        internal static int Surge(int turn, string encounterId) =>
            IsNonFogOrdinary(encounterId) && turn >= 4 ? turn - 2 : 0;

        internal static int DamageBonus(int turn, string encounterId, int amount) =>
            amount > 0 ? Surge(turn, encounterId) : 0;

        internal static string Candidates(EnemySpec enemy, int surge, string encounterId, int healthReduction = 0, int sanityReduction = 0)
        {
            if (!IsNonFogOrdinary(encounterId)) return enemy.Candidates;
            return string.Join(" / ", enemy.Intents
                .OrderBy(intent => intent.Channel == DamageChannel.Health ? 0 : 1)
                .Select(intent =>
                {
                    var amount = Math.Max(0, intent.Amount + (intent.Amount > 0 ? surge : 0) -
                        (intent.Channel == DamageChannel.Health ? healthReduction : sanityReduction));
                    return amount == 0 ? intent.Name :
                        $"{intent.Name}：{amount} {(intent.Channel == DamageChannel.Health ? "生命伤害" : "理智冲击")}";
                }));
        }

        private static bool TryVoyage(string id, out string region, out int variant)
        {
            region = null; variant = -1;
            if (string.IsNullOrEmpty(id) || !id.StartsWith("voyage-", StringComparison.Ordinal)) return false;
            var parts = id.Split('-');
            if (parts.Length < 3) return false;
            region = parts[1];
            if (region != "fog" && region != "bones" && region != "inverted" && region != "fleet" && region != "abyss")
                return false;
            var token = parts[parts.Length - 1];
            var colon = token.IndexOf(':');
            if (colon >= 0) token = token.Substring(0, colon);
            return int.TryParse(token, out variant) && variant >= 0 && variant < 5;
        }
    }
}
