using System;
using System.Collections.Generic;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    internal static class BossApplicability
    {
        private static readonly IReadOnlyDictionary<string, string> Cards = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["c043"] = "M03", ["c044"] = "M03", ["c067"] = "M03",
            ["c042"] = "M05", ["c052"] = "M05", ["c066"] = "M05", ["c082"] = "M05",
            ["c056"] = "M02", ["c068"] = "M02",
            ["c057"] = "M04", ["c059"] = "M04", ["c060"] = "M04", ["c063"] = "M04", ["c065"] = "M04", ["c086"] = "M04"
        };

        private static readonly IReadOnlyDictionary<string, string> Items = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["i06"] = "M03", ["i16"] = "M03",
            ["i07"] = "M05", ["i09"] = "M05",
            ["i08"] = "M04"
        };

        public static string CardLabel(CardSpec card) => card != null && Cards.TryGetValue(card.Id, out var boss) ? "适用：" + boss : "";
        public static string ItemLabel(ItemSpec item) => item != null && Items.TryGetValue(item.Id, out var boss) ? "适用：" + boss : "";
    }
}
