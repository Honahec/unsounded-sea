using System.Collections.Generic;

namespace UnsoundedSea.Core
{
    public sealed class CardView
    {
        public int InstanceId { get; }
        public CardSpec Definition { get; }
        public bool IsEncounter { get; }
        public int ActionCost { get; internal set; }
        public bool Enabled { get; internal set; }
        public string DisabledReason { get; internal set; }
        public IReadOnlyList<EncounterTargetView> Targets { get; internal set; }
        public CardView(int instanceId, CardSpec definition, bool isEncounter = false)
        {
            InstanceId = instanceId; Definition = definition; IsEncounter = isEncounter; Enabled = true; ActionCost = definition.Cost;
            DisabledReason = ""; Targets = new List<EncounterTargetView>().AsReadOnly();
        }
        internal CardView Snapshot()
        {
            var copy = new CardView(InstanceId, Definition, IsEncounter) { Enabled = Enabled, DisabledReason = DisabledReason, ActionCost = ActionCost };
            copy.Targets = new List<EncounterTargetView>(Targets).AsReadOnly();
            return copy;
        }
    }
    public enum MechanismState { None, Unmeasured, Measured, Open, Disabled, Off, Positive, Reverse }
    public sealed class EncounterTargetView
    {
        public string Id { get; }
        public string Name { get; }
        public string Detail { get; }
        public bool Enabled { get; }
        public MechanismState State { get; }
        public int? MeasuredValue { get; }
        public bool IsSeal { get; }
        public bool IsCurrent { get; }
        public EncounterTargetView(string id, string name, string detail, bool enabled,MechanismState state=MechanismState.None,int? measuredValue=null,bool isSeal=false,bool isCurrent=false)
        { Id = id; Name = name; Detail = detail; Enabled = enabled; State=state;MeasuredValue=measuredValue;IsSeal=isSeal;IsCurrent=isCurrent; }
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
        public int Surge { get; }
        public EnemyStyle Style { get; }
        internal EnemyView(string id, string name, int health, int maxHealth, string clue, string intent,
            bool confirmed, bool targetable = true, int surge = 0, EnemyStyle style = EnemyStyle.Boss)
        {
            Id = id; Name = name; Health = health; MaxHealth = maxHealth; Clue = clue; Intent = intent;
            IntentConfirmed = confirmed; Targetable = targetable; Surge = surge; Style = style;
        }
    }
    public sealed class ToolView
    {
        public ToolSpec Definition { get; }
        public int RemainingUses { get; }
        public bool Enabled { get; }
        public string DisabledReason { get; }
        public IReadOnlyList<EncounterTargetView> Targets { get; }
        internal ToolView(ToolSpec definition, int uses, bool enabled, string reason, IReadOnlyList<EncounterTargetView> targets)
        {
            Definition = definition; RemainingUses = uses; Enabled = enabled;
            DisabledReason = reason ?? ""; Targets = targets;
        }
    }
    public sealed class CrewSupportView
    {
        public CrewSupportSpec Definition { get; }
        public bool Used { get; }
        public bool Enabled { get; }
        public string DisabledReason { get; }
        public IReadOnlyList<EncounterTargetView> Targets { get; }
        internal CrewSupportView(CrewSupportSpec definition, bool used, bool enabled, string reason, IReadOnlyList<EncounterTargetView> targets)
        { Definition = definition; Used = used; Enabled = enabled; DisabledReason = reason ?? ""; Targets = targets; }
    }
    public sealed class PendingCardChoiceView
    {
        public string Prompt { get; }
        public IReadOnlyList<EncounterTargetView> Options { get; }
        internal PendingCardChoiceView(string prompt, IReadOnlyList<EncounterTargetView> options) { Prompt = prompt; Options = options; }
    }
    public enum EnemyActionKind { Attack, Ritual, Summon, Heal, Observe }
    public sealed class ResolvedEnemyAction
    {
        public string EnemyId { get; }
        public DamageChannel Channel { get; }
        public int Amount { get; }
        public int Health { get; }
        public int Sanity { get; }
        public int Block { get; }
        public int Ward { get; }
        public EnemyActionKind Kind { get; }
        public ResolvedEnemyAction(string enemyId, DamageChannel channel, int amount, int health = 0, int sanity = 0, int block = 0, int ward = 0, EnemyActionKind? kind = null)
        {
            EnemyId = enemyId; Channel = channel; Amount = amount;
            Health = health; Sanity = sanity; Block = block; Ward = ward;
            Kind = kind ?? (amount == 0 ? EnemyActionKind.Observe : channel == DamageChannel.Sanity ? EnemyActionKind.Ritual : EnemyActionKind.Attack);
        }
    }
    public sealed class BattleView
    {
        // Only actions already executed by the last successful EndTurn; never future intent.
        public IReadOnlyList<ResolvedEnemyAction> ResolvedEnemyActions { get; internal set; }
        public BossEncounterKind BossKind { get; internal set; }
        public int BossStage { get; internal set; }
        public string BossStatus { get; internal set; }
        public IReadOnlyList<EncounterTargetView> EncounterTargets { get; internal set; }
        public PendingCardChoiceView PendingChoice { get; internal set; }
        public BattlePhase Phase { get; internal set; }
        public int Turn { get; internal set; }
        public int Health { get; internal set; }
        public int Sanity { get; internal set; }
        public int HealthDamageTaken { get; internal set; }
        public int SanityDamageTaken { get; internal set; }
        public int Actions { get; internal set; }
        public int Block { get; internal set; }
        public int Ward { get; internal set; }
        public int Watched { get; internal set; }
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
        public IReadOnlyList<CrewSupportView> Supports { get; internal set; }
        public IReadOnlyList<Evidence> Evidence { get; internal set; }
        public IReadOnlyList<string> Log { get; internal set; }
        public IReadOnlyList<SupplyActionView> SupplyActions { get; internal set; }
        public IReadOnlyList<string> SpentConsumables { get; internal set; }
        public IReadOnlyList<string> Equipment { get; internal set; }
        public bool SurveyedBeacon { get; internal set; }
    }

    [System.Serializable]
    public sealed class BattleCommand
    {
        // DTO fields are intentionally serializable; Execute copies them before storing history.
        public CommandKind kind;
        public int cardInstanceId;
        public string targetId;
        public string toolId;
        public string supportId;
        public BattleCommand Copy() => new BattleCommand { kind = kind, cardInstanceId = cardInstanceId, targetId = targetId, toolId = toolId, supportId = supportId };
        public static BattleCommand Play(int card, string target = null) => new BattleCommand { kind = CommandKind.PlayCard, cardInstanceId = card, targetId = target };
        public static BattleCommand Investigate(string tool, string target) => new BattleCommand { kind = CommandKind.UseTool, toolId = tool, targetId = target };
        public static BattleCommand Support(string support, string target = null) => new BattleCommand { kind = CommandKind.UseCrewSupport, supportId = support, targetId = target };
        public static BattleCommand Consume(string item, string target = null) => new BattleCommand { kind = CommandKind.UseConsumable, toolId = item, targetId = target };
        public static BattleCommand Reorder(string orderId) => new BattleCommand { kind = CommandKind.ReorderDeck, targetId = orderId };
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
