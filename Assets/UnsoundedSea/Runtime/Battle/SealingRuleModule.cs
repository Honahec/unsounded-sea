using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [CreateAssetMenu(menuName = "Unsounded Sea/Rules/Sealing")]
    public sealed class SealingRuleModule : BattleRuleModule
    {
        public string objectiveDescription = "完成四段封线，驶离回声海域";
        public override IEncounterObjective CreateObjective(BattleDefinition definition) => new SealingObjective(objectiveDescription, definition.ObjectiveTarget);
        private sealed class SealingObjective : IEncounterObjective
        {
            public string Description { get; }
            public int Progress { get; private set; }
            public int Target { get; }
            public bool AllowsTaskAction => true;
            public int ActionCost => EncounterObjective.SealActionCost;
            public SealingObjective(string text, int target) { Description = text; Target = target; }
            public void Advance(int amount) { Progress = System.Math.Min(Target, Progress + amount); }
            public void OnTurnResolved() { }
            public bool IsComplete(bool allEnemiesDefeated) => Progress >= Target;
        }
    }
}
