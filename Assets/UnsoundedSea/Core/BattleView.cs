using System.Collections.Generic;

namespace UnsoundedSea.Core
{
    public sealed class CardView
    {
        public int InstanceId { get; }
        public CardSpec Definition { get; }
        public bool IsEncounter { get; }
        public bool Enabled { get; internal set; }
        public string DisabledReason { get; internal set; }
        public IReadOnlyList<EncounterTargetView> Targets { get; internal set; }
        public CardView(int instanceId, CardSpec definition, bool isEncounter = false)
        {
            InstanceId = instanceId; Definition = definition; IsEncounter = isEncounter; Enabled = true;
            DisabledReason = ""; Targets = new List<EncounterTargetView>().AsReadOnly();
        }
        internal CardView Snapshot()
        {
            var copy = new CardView(InstanceId, Definition, IsEncounter) { Enabled = Enabled, DisabledReason = DisabledReason };
            copy.Targets = new List<EncounterTargetView>(Targets).AsReadOnly();
            return copy;
        }
    }
    public sealed class EncounterTargetView
    {
        public string Id { get; }
        public string Name { get; }
        public string Detail { get; }
        public bool Enabled { get; }
        public EncounterTargetView(string id, string name, string detail, bool enabled)
        { Id = id; Name = name; Detail = detail; Enabled = enabled; }
    }
    public sealed class EnemyView
    {
        public string Id { get; }
        public string Name { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public string Clue { get; }
        public string Intent { get; }
        public bool IntentConfirmed { get; }
        public bool Targetable { get; }
        internal EnemyView(string id, string name, int health, int maxHealth, string clue, string intent, bool confirmed, bool targetable = true)
        { Id = id; Name = name; Health = health; MaxHealth = maxHealth; Clue = clue; Intent = intent; IntentConfirmed = confirmed; Targetable = targetable; }
    }
    public sealed class ToolView
    {
        public ToolSpec Definition { get; }
        public int RemainingUses { get; }
        internal ToolView(ToolSpec definition, int uses) { Definition = definition; RemainingUses = uses; }
    }
    public sealed class BattleView
    {
        public BossEncounterKind BossKind { get; internal set; }
        public int BossStage { get; internal set; }
        public string BossStatus { get; internal set; }
        public IReadOnlyList<EncounterTargetView> EncounterTargets { get; internal set; }
        public BattlePhase Phase { get; internal set; }
        public int Turn { get; internal set; }
        public int Health { get; internal set; }
        public int Sanity { get; internal set; }
        public int Actions { get; internal set; }
        public int Block { get; internal set; }
        public int Ward { get; internal set; }
        public int DrawCount { get; internal set; }
        public int DiscardCount { get; internal set; }
        public int ExhaustCount { get; internal set; }
        public string Objective { get; internal set; }
        public int ObjectiveProgress { get; internal set; }
        public int ObjectiveTarget { get; internal set; }
        public bool CanAdvanceObjective { get; internal set; }
        public IReadOnlyList<CardView> Hand { get; internal set; }
        public IReadOnlyList<EnemyView> Enemies { get; internal set; }
        public IReadOnlyList<ToolView> Tools { get; internal set; }
        public IReadOnlyList<Evidence> Evidence { get; internal set; }
        public IReadOnlyList<string> Log { get; internal set; }
    }

    [System.Serializable]
    public sealed class BattleCommand
    {
        // DTO fields are intentionally serializable; Execute copies them before storing history.
        public CommandKind kind;
        public int cardInstanceId;
        public string targetId;
        public string toolId;
        public BattleCommand Copy() => new BattleCommand { kind = kind, cardInstanceId = cardInstanceId, targetId = targetId, toolId = toolId };
        public static BattleCommand Play(int card, string target = null) => new BattleCommand { kind = CommandKind.PlayCard, cardInstanceId = card, targetId = target };
        public static BattleCommand Investigate(string tool, string target) => new BattleCommand { kind = CommandKind.UseTool, toolId = tool, targetId = target };
        public static BattleCommand EndTurn() => new BattleCommand { kind = CommandKind.EndTurn };
        public static BattleCommand Task() => new BattleCommand { kind = CommandKind.AdvanceObjective };
    }
    public readonly struct CommandResult
    {
        public bool Success { get; }
        public string Error { get; }
        public CommandResult(bool success, string error = "") { Success = success; Error = error; }
    }
}
