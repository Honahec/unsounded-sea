using System;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [CreateAssetMenu(menuName = "Unsounded Sea/Battle Scenario")]
    public sealed class BattleScenario : ScriptableObject
    {
        public EncounterDefinition encounter;
        public BattleStage stagePrefab;
        [Tooltip("Optional per-slot visual overrides; otherwise uses the enemy definition prefab.")]
        public GameObject[] actorOverrides = Array.Empty<GameObject>();
        public BattleRuleModule rules;
        public string chapter = "01 / 雾海来客";
        public string location = "鸦号调查船 · 北纬 47°";
        [TextArea] public string introduction;
        public string objectiveHeading = "不要试图杀死海。";
        public string taskLabel = "刻下封线";
        [TextArea] public string victoryText;
        [TextArea] public string defeatText;

        public BattleSession CreateSession(uint seed)
        {
            if (encounter == null) throw new InvalidOperationException($"{name}: encounter is missing.");
            var definition = encounter.Build();
            return new BattleSession(definition, seed, rules == null ? null : rules.CreateEffects(),
                rules == null ? null : rules.CreateObjective(definition));
        }
        public GameObject ActorPrefab(int index) => actorOverrides != null && index < actorOverrides.Length && actorOverrides[index] != null
            ? actorOverrides[index] : encounter.enemies[index].presentationPrefab;
        public void ValidatePresentation()
        {
            if (encounter == null || stagePrefab == null) throw new InvalidOperationException($"{name}: encounter/stage is missing.");
            if (stagePrefab.battleCamera == null || stagePrefab.vessel == null || stagePrefab.actorAnchors == null || stagePrefab.actorAnchors.Length < encounter.enemies.Length)
                throw new InvalidOperationException($"{name}: stage needs a camera and an anchor for each enemy.");
            for (var i = 0; i < encounter.enemies.Length; i++)
                if (encounter.enemies[i] == null || ActorPrefab(i) == null || stagePrefab.actorAnchors[i] == null)
                    throw new InvalidOperationException($"{name}: missing visual or anchor at slot {i}.");
        }
    }
}
