using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    // Assets are configuration only. Factories MUST return fresh mutable objects per battle.
    public abstract class BattleRuleModule : ScriptableObject
    {
        public virtual EffectRegistry CreateEffects() => EffectRegistry.CreateDefault();
        public abstract IEncounterObjective CreateObjective(BattleDefinition definition);
    }
}
