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
        public string enemyHeading = "01   /   甲板上的来客";
        [TextArea] public string objectiveHint = "每回合一次。格挡与理智防护在下回合清空。";
        public string enemyStatus;
        [TextArea] public string introduction;
        public string objectiveHeading = "不要试图杀死海。";
        public string taskLabel = "刻下封线";
        [TextArea] public string victoryText;
        [TextArea] public string defeatText;

        public BattleSession CreateSession(uint seed, bool usePreparedDeck = false)
        {
            if (encounter == null) throw new InvalidOperationException($"{name}: encounter is missing.");
            var definition = encounter.Build();
            if (usePreparedDeck) definition = CardDeckPreparation.Apply(definition);
            return new BattleSession(definition, seed, rules == null ? null : rules.CreateEffects(),
                rules == null ? null : rules.CreateObjective(definition),
                rules is BossRuleModule boss ? boss.bossKind : BossEncounterKind.None);
        }
        public GameObject ActorPrefab(int index) => actorOverrides != null && index < actorOverrides.Length && actorOverrides[index] != null
            ? actorOverrides[index] : encounter.enemies[index].presentationPrefab;
        public void ValidatePresentation()
        {
            if (encounter == null || stagePrefab == null) throw new InvalidOperationException($"{name}: encounter/stage is missing.");
            if (stagePrefab.battleCamera == null || stagePrefab.vessel == null || stagePrefab.actorAnchors == null || stagePrefab.actorAnchors.Length < encounter.enemies.Length)
                throw new InvalidOperationException($"{name}: stage needs a camera and an anchor for each enemy.");
            for (var i = 0; i < encounter.enemies.Length; i++)
            {
                if (encounter.enemies[i] == null || ActorPrefab(i) == null || stagePrefab.actorAnchors[i] == null)
                    throw new InvalidOperationException($"{name}: missing visual or anchor at slot {i}.");
                var anchor = stagePrefab.actorAnchors[i];
                var placement = anchor.GetComponent<BattleAnchor>();
                if (placement != null && placement.environment == BattleEnvironment.Sea && anchor.IsChildOf(stagePrefab.vessel))
                    throw new InvalidOperationException($"{name}: sea actors must be independent of the vessel rig.");
            }
        }
    }
}
