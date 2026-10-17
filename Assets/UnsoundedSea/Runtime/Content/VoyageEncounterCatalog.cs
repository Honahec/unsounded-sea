using System;
using System.Collections.Generic;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [CreateAssetMenu(menuName = "Unsounded Sea/Voyage Encounter Catalog")]
    public sealed class VoyageEncounterCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string id;
            public BattleScenario scenario;
            public string group;
            public bool isBoss;
        }

        private const string ResourcePath = "VoyageEncounterCatalog";
        private static readonly string[] RequiredIds =
        {
            "voyage-fog-0", "voyage-fog-1", "voyage-fog-2", "voyage-fog-3", "voyage-fog-4",
            "voyage-bones-0", "voyage-bones-1", "voyage-bones-2", "voyage-bones-3", "voyage-bones-4",
            "voyage-inverted-0", "voyage-inverted-1", "voyage-inverted-2", "voyage-inverted-3", "voyage-inverted-4",
            "voyage-fleet-0", "voyage-fleet-1", "voyage-fleet-2", "voyage-fleet-3", "voyage-fleet-4",
            "voyage-abyss-0", "voyage-abyss-1", "voyage-abyss-2", "voyage-abyss-3", "voyage-abyss-4",
            "fog-visitors", "m02", "m03", "m04", "m05", "drowned-bell"
        };
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        [SerializeField] private ToolDefinition[] tools = Array.Empty<ToolDefinition>();
        private Dictionary<string, BattleScenario> lookup;
        public static IReadOnlyList<Entry> AllEntries
        {
            get
            {
                var catalog = Resources.Load<VoyageEncounterCatalog>(ResourcePath);
                if (catalog == null)
                    throw new InvalidOperationException($"Missing Resources/{ResourcePath}.asset. Run Unsounded Sea/Content/Setup Voyage Campaign.");
                return Array.AsReadOnly((Entry[])catalog.entries.Clone());
            }
        }

        private Dictionary<string, ToolDefinition> toolLookup;

        public static BattleScenario Resolve(string encounterId)
        {
            if (string.IsNullOrWhiteSpace(encounterId))
                throw new ArgumentException("Voyage encounter ID is required.", nameof(encounterId));
            var catalog = Resources.Load<VoyageEncounterCatalog>(ResourcePath);
            if (catalog == null)
                throw new InvalidOperationException($"Missing Resources/{ResourcePath}.asset. Run Unsounded Sea/Content/Setup Voyage Campaign.");
            return catalog.ResolveEntry(encounterId);
        }

        public static ToolSpec ResolveTool(string toolId)
        {
            if (string.IsNullOrWhiteSpace(toolId))
                throw new ArgumentException("Voyage tool ID is required.", nameof(toolId));
            var catalog = Resources.Load<VoyageEncounterCatalog>(ResourcePath);
            if (catalog == null)
                throw new InvalidOperationException($"Missing Resources/{ResourcePath}.asset. Run Unsounded Sea/Content/Setup Voyage Campaign.");
            return catalog.ResolveToolEntry(toolId).Build();
        }

        private BattleScenario ResolveEntry(string encounterId)
        {
            if (lookup == null)
            {
                var validated = new Dictionary<string, BattleScenario>(StringComparer.Ordinal);
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.id) || entry.scenario == null)
                        throw new InvalidOperationException($"{name}: catalog contains a blank ID or missing scenario.");
                    if (!validated.TryAdd(entry.id, entry.scenario))
                        throw new InvalidOperationException($"{name}: duplicate encounter ID '{entry.id}'.");
                    if (entry.scenario.encounter == null || entry.scenario.encounter.id != entry.id)
                        throw new InvalidOperationException($"{name}: scenario for '{entry.id}' has a mismatched encounter.");
                }
                foreach (var requiredId in RequiredIds)
                    if (!validated.ContainsKey(requiredId))
                        throw new InvalidOperationException($"{name}: required encounter '{requiredId}' is missing. Run Unsounded Sea/Content/Setup Voyage Campaign.");
                lookup = validated;
            }
            if (!lookup.TryGetValue(encounterId, out var scenario))
                throw new InvalidOperationException($"Voyage encounter '{encounterId}' is missing from {name}. Run Unsounded Sea/Content/Setup Voyage Campaign.");
            return scenario;
        }

        private ToolDefinition ResolveToolEntry(string toolId)
        {
            if (toolLookup == null)
            {
                var validated = new Dictionary<string, ToolDefinition>(StringComparer.Ordinal);
                foreach (var tool in tools)
                {
                    if (tool == null || string.IsNullOrWhiteSpace(tool.id))
                        throw new InvalidOperationException($"{name}: catalog contains a missing tool or blank tool ID.");
                    if (!validated.TryAdd(tool.id, tool))
                        throw new InvalidOperationException($"{name}: duplicate tool ID '{tool.id}'.");
                }
                foreach (var requiredId in new[] { "lantern", "listener", "shell" })
                    if (!validated.ContainsKey(requiredId))
                        throw new InvalidOperationException($"{name}: required tool '{requiredId}' is missing. Run Unsounded Sea/Content/Setup Voyage Campaign.");
                toolLookup = validated;
            }
            if (!toolLookup.TryGetValue(toolId, out var resolved))
                throw new InvalidOperationException($"Voyage tool '{toolId}' is missing from {name}. Run Unsounded Sea/Content/Setup Voyage Campaign.");
            return resolved;
        }
    }
}
