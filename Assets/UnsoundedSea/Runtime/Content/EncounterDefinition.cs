using System;
using System.Linq;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [CreateAssetMenu(menuName = "Unsounded Sea/Encounter")]
    public sealed class EncounterDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public ObjectiveKind objective;
        [Min(1)] public int objectiveTarget = 4;
        [Min(1)] public int playerHealth = 60;
        [Min(1)] public int playerSanity = 30;
        [Min(1)] public int actionsPerTurn = 3;
        [Min(1)] public int drawPerTurn = 5;
        public CardDefinition[] deck = Array.Empty<CardDefinition>();
        public EnemyDefinition[] enemies = Array.Empty<EnemyDefinition>();
        public ToolDefinition[] equippedTools = Array.Empty<ToolDefinition>();

        public BattleDefinition Build()
        {
            if (deck == null || enemies == null || equippedTools == null || deck.Any(x => x == null) || enemies.Any(x => x == null) || equippedTools.Any(x => x == null))
                throw new InvalidOperationException($"{name}: content contains missing references.");
            var duplicateCards = deck.GroupBy(x => x.id).Any(g => g.Distinct().Count() > 1);
            if (duplicateCards) throw new InvalidOperationException($"{name}: different card assets share an ID.");
            return new BattleDefinition(id, deck.Select(x => x.Build()), enemies.Select((x, i) => x.Build($"{x.id}:{i}")),
                equippedTools.Select(x => x.Build()), objective, objectiveTarget, playerHealth, playerSanity, actionsPerTurn, drawPerTurn);
        }
    }
}
