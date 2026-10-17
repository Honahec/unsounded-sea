using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    public static class CardDeckPreparation
    {
        public const int DeckSize = 12;
        private const string Preference = "unsounded-sea.prepared-deck";
        private const string ArchivePreference = "unsounded-sea.verified-archive-v2";
        private const string CrewPreference = "unsounded-sea.prepared-crew";
        public static string CrewId => PlayerPrefs.GetString(CrewPreference, "medic");
        public static void SaveCrew(string id)
        {
            if (id != "medic" && id != "listener") throw new ArgumentException("Unknown initial crew.", nameof(id));
            PlayerPrefs.SetString(CrewPreference, id); PlayerPrefs.Save();
        }
        private static readonly int[] StarterNumbers = { 1, 2, 12, 19, 20, 21, 22, 37, 39, 41, 55, 71 };
        [Serializable] private sealed class ArchiveData { public ArchiveEntry[] entries = Array.Empty<ArchiveEntry>(); }
        [Serializable] private sealed class ArchiveEntry { public string key; public string text; public string source; public string subjectType; public string question; public string rule; public string fact; }
        public static IReadOnlyList<Evidence> StoredArchive=>LoadArchive().ToArray();

        public static void StoreVerified(IEnumerable<Evidence> evidence)
        {
            var entries = LoadArchive().Concat(evidence.Where(item => item.Stable).Select(item=>item.Archive(EvidenceScope.Permanent)))
                .GroupBy(item => item.Key).Select(group => group.Last())
                .Select(item => new ArchiveEntry { key = item.Key, text = item.Text, source = item.Source,subjectType=item.SubjectType,question=item.Question,rule=item.Rule,fact=item.Fact }).ToArray();
            PlayerPrefs.SetString(ArchivePreference, JsonUtility.ToJson(new ArchiveData { entries = entries }));
            PlayerPrefs.Save();
        }

        private static IEnumerable<Evidence> LoadArchive()
        {
            var json = PlayerPrefs.GetString(ArchivePreference, "");
            if (string.IsNullOrEmpty(json)) return Array.Empty<Evidence>();
            try
            {
                var data = JsonUtility.FromJson<ArchiveData>(json);
                return (data?.entries ?? Array.Empty<ArchiveEntry>())
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.key) && !string.IsNullOrWhiteSpace(item.text) && !string.IsNullOrWhiteSpace(item.source))
                    .Select(item => new Evidence(item.key,item.text,item.source,KnowledgeLevel.Verified,EvidenceScope.Permanent,subjectType:item.subjectType,question:item.question,rule:item.rule,fact:item.fact)).Where(item=>item.Stable);
            }
            catch (Exception) { return Array.Empty<Evidence>(); }
        }

        public static IReadOnlyList<CardSpec> Prepared
        {
            get
            {
                var ids = PlayerPrefs.GetString(Preference, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var selected = ids.Select(TryGet).Where(card => card != null).GroupBy(card => card.Id).Select(group => group.First()).ToList();
                return selected.Count == DeckSize ? selected : CardCatalog.All.Where(card => StarterNumbers.Contains(CardCatalog.Number(card.Id))).ToList();
            }
        }

        public static void Save(IEnumerable<string> ids)
        {
            var selected = ids.Distinct().Select(CardCatalog.Get).ToArray();
            if (selected.Length != DeckSize) throw new InvalidOperationException($"行动簿必须恰好编入 {DeckSize} 张不同卡牌。");
            PlayerPrefs.SetString(Preference, string.Join(",", selected.Select(card => card.Id)));
            PlayerPrefs.Save();
        }

        public static BattleDefinition Apply(BattleDefinition source, VoyageSession voyage = null)
        {
            var deck = voyage == null ? Prepared : voyage.DeckIds.Select(CardCatalog.Get).ToArray();
            var supports = (voyage?.CrewIds??new[]{CrewId}).Select(id=>{var crew=SupportCatalog.CrewMember(id);return new CrewSupportSpec(crew.Id,crew.Name,crew.Kind,voyage?.InjuredCrew.Contains(id)==true);}).ToArray();
            var enemyIds = new HashSet<string>(source.Enemies.Select(enemy => enemy.Id.Split(':')[0]), StringComparer.Ordinal);
            var archive = source.Archive.Concat((voyage==null?LoadArchive():voyage.Archive).Where(item=>item.Stable&&enemyIds.Contains(item.SubjectType))).GroupBy(item=>item.Key).Select(group=>group.Last());
            var tools = voyage == null ? source.Tools : voyage.ToolIds.Select(SupportCatalog.Tool).ToArray();
            return new BattleDefinition(source.Id, deck, source.Enemies, tools, source.Objective,
                source.ObjectiveTarget, source.Health, source.Sanity, source.Actions, source.DrawCount, supports, archive,
                voyage?.Health ?? source.InitialHealth, voyage?.Sanity ?? source.InitialSanity,voyage?.Loadout??source.Loadout);
        }

        private static CardSpec TryGet(string id)
        {
            try { return CardCatalog.Get(id); }
            catch (Exception) { return null; }
        }
    }
}
