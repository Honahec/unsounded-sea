using UnityEngine;

namespace UnsoundedSea.Runtime
{
    // Each resource has its own dependency graph; the menu catalog stores only its path.
    public sealed class VoyageScenarioReference : ScriptableObject
    {
        public BattleScenario scenario;
    }
}
