using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [CreateAssetMenu(menuName = "Unsounded Sea/Rules/Survival")]
    public sealed class SurvivalRuleModule : BattleRuleModule
    {
        public string objectiveDescription = "撑过五轮，驶离沉钟海域";
        public override IEncounterObjective CreateObjective(BattleDefinition definition) => new SurvivalObjective(objectiveDescription, definition.ObjectiveTarget);

        private sealed class SurvivalObjective : IEncounterObjective
        {
            public string Description { get; }
            public int Progress { get; private set; }
            public int Target { get; }
            public bool AllowsTaskAction => false;
            public SurvivalObjective(string description, int target) { Description = description; Target = target; }
            public void Advance(int amount) { }
            public void OnTurnResolved() { Progress = System.Math.Min(Target, Progress + 1); }
            public bool IsComplete(bool allEnemiesDefeated) => Progress >= Target;
        }
    }
}
