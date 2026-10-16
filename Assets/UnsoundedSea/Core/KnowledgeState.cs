using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public sealed class Evidence
    {
        public string Key { get; }
        public string Text { get; }
        public string Source { get; }
        public KnowledgeLevel Level { get; }
        public Evidence(string key, string text, string source, KnowledgeLevel level)
        { Key = Require.Id(key); Text = Require.Id(text); Source = Require.Id(source); Level = level; }
    }

    public sealed class KnowledgeState
    {
        private readonly Dictionary<string, Evidence> entries = new Dictionary<string, Evidence>();
        public IReadOnlyList<Evidence> Entries => entries.Values.OrderBy(x => x.Key, System.StringComparer.Ordinal).ToList().AsReadOnly();
        public void Record(Evidence evidence)
        {
            if (!entries.TryGetValue(evidence.Key, out var previous) || evidence.Level >= previous.Level)
                entries[evidence.Key] = evidence;
        }
    }
}
