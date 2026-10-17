using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public enum EvidenceScope { Turn, Encounter, Run, Permanent }
    public sealed class Evidence
    {
        public string Key { get; }
        public string Text { get; }
        public string Source { get; }
        public KnowledgeLevel Level { get; }
        public EvidenceScope Scope { get; }
        public string SubjectId { get; }
        public string SubjectType { get; }
        public string Question { get; }
        public string EncounterId { get; }
        public int Turn { get; }
        public string Rule { get; }
        public string Fact { get; }
        public IReadOnlyList<string> Supports { get; }
        public Evidence(string key, string text, string source, KnowledgeLevel level,
            EvidenceScope scope=EvidenceScope.Encounter,string subjectId="",string subjectType="",string question="",string encounterId="",int turn=0,string rule="",string fact="",IEnumerable<string> supports=null)
        {
            Key=Require.Id(key); Text=Require.Id(text); Source=Require.Id(source); Level=Require.Defined(level); Scope=Require.Defined(scope);
            SubjectId=subjectId??""; SubjectType=subjectType??""; Question=question??""; EncounterId=encounterId??""; Turn=turn; Rule=rule??""; Fact=fact??""; Supports=Require.Copy(supports??System.Array.Empty<string>());
        }
        public Evidence Archive(EvidenceScope scope) => new Evidence("archive/"+SubjectType+"/"+Question+"/"+Fact,Text,Source,Level,scope,"",SubjectType,Question,"",0,Rule,Fact,new[]{Key});
        public bool Stable => Level==KnowledgeLevel.Verified && (Rule=="source-v1"||Rule=="anchor-v1") && SubjectType!="" && Fact!="";
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
