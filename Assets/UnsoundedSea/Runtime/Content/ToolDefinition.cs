using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [CreateAssetMenu(menuName = "Unsounded Sea/Investigation Tool")]
    public sealed class ToolDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public InvestigationKind question;
        [Min(1)] public int uses = 1;
        [Min(0)] public int sanityCost;
        public ToolSpec Build() => new ToolSpec(id, displayName, question, uses, sanityCost);
    }
}
