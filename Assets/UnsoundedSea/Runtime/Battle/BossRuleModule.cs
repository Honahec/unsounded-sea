using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [CreateAssetMenu(menuName = "Unsounded Sea/Boss Rules")]
    public sealed class BossRuleModule : BattleRuleModule
    {
        public BossEncounterKind bossKind;
        public override IEncounterObjective CreateObjective(BattleDefinition definition)
            => new EncounterObjective(definition.Objective, definition.ObjectiveTarget);
    }
}
