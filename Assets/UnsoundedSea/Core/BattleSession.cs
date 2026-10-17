using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public sealed partial class BattleSession
    {
        private sealed class EnemyState
        {
            public EnemySpec Definition;
            public int Health;
            public IntentSpec ActualIntent;
            public bool Confirmed;
            public bool Interrupted;
        }
        private readonly BattleDefinition definition;
        private readonly EffectRegistry effects;
        private readonly DeterministicRandom deckRandom;
        private readonly DeterministicRandom intentRandom;
        private readonly List<CardView> draw = new List<CardView>();
        private readonly List<CardView> hand = new List<CardView>();
        private readonly List<CardView> discard = new List<CardView>();
        private readonly List<CardView> exhaust = new List<CardView>();
        private readonly List<EnemyState> enemies;
        private readonly Dictionary<string, int> toolUses;
        private readonly HashSet<string> usedSupports = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<BattleCommand> history = new List<BattleCommand>();
        private readonly List<string> log = new List<string>();
        private readonly KnowledgeState knowledge = new KnowledgeState();
        private readonly IEncounterObjective objective;
        private BattlePhase phase;
        private int turn, health, sanity, actions, block, ward;
        private bool taskUsed, investigationUsed, sceneActionUsed, crewSupportUsed, directionalListeningBlocked, directionalListeningUsed;
        private int nextInstanceId, watched, nextHealthReduction, sceneFailureReduction, sceneDiscount;
        private readonly HashSet<int> retained = new HashSet<int>();
        private readonly HashSet<string> turnFlags = new HashSet<string>(StringComparer.Ordinal);
        private int watchedInvestigationBonus;
        private readonly BossEncounterKind bossKind;
        private BossEncounterState boss;
        public uint Seed { get; }
        internal BattleDefinition Definition => definition;
        internal BossEncounterKind BossKind => bossKind;
        public IReadOnlyList<BattleCommand> Commands => history.Select(x => x.Copy()).ToList().AsReadOnly();

        public BattleSession(BattleDefinition definition, uint seed, EffectRegistry effects = null, IEncounterObjective objective = null, BossEncounterKind boss = BossEncounterKind.None)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.effects = (effects ?? EffectRegistry.CreateDefault()).Snapshot();
            foreach (var effect in definition.Deck.SelectMany(x => x.Effects)) this.effects.Get(effect.Id);
            this.objective = objective ?? new EncounterObjective(definition.Objective, definition.ObjectiveTarget);
            Seed = seed; deckRandom = new DeterministicRandom(seed); intentRandom = new DeterministicRandom(seed ^ 0x9E3779B9u);
            bossKind = Require.Defined(boss);
            health = definition.InitialHealth; sanity = definition.InitialSanity;
            enemies = definition.Enemies.Select(x => new EnemyState { Definition = x, Health = x.Health }).ToList();
            toolUses = definition.Tools.ToDictionary(x => x.Id, x => x.Uses, StringComparer.Ordinal);
            for (var i = 0; i < definition.Deck.Count; i++) draw.Add(new CardView(i + 1, definition.Deck[i]));
            nextInstanceId = definition.Deck.Count + 100;
            deckRandom.Shuffle(draw);
            if (bossKind != BossEncounterKind.None) this.boss = new BossEncounterState(this, bossKind, seed);
            BeginTurn();
        }

        public BattleView View
        {
            get
            {
                RefreshCardViews();
                return new BattleView
                {
                    Phase = phase, Turn = turn, Health = health, Sanity = sanity, Actions = actions, Block = block, Ward = ward, Watched = watched,
                    BossKind = bossKind, BossStage = boss?.Stage ?? 0, BossStatus = boss?.Status ?? "",
                    EncounterTargets = boss?.AllTargets ?? EmptyTargets,
                    PendingChoice = PendingChoiceView(),
                    DrawCount = draw.Count, DiscardCount = discard.Count, ExhaustCount = exhaust.Count,
                    Objective = boss?.Objective ?? objective.Description, ObjectiveProgress = boss?.Progress ?? objective.Progress,
                    ObjectiveTarget = boss?.Target ?? objective.Target, CanAdvanceObjective = boss == null && phase == BattlePhase.PlayerTurn && objective.AllowsTaskAction && !taskUsed && actions >= Math.Max(0, 1 - sceneDiscount),
                    Hand = hand.Select(x => x.Snapshot()).ToList().AsReadOnly(),
                    Enemies = enemies.Select(BossEnemyView).ToList().AsReadOnly(),
                    Tools = definition.Tools.Select(t => new ToolView(t, toolUses[t.Id])).ToList().AsReadOnly(),
                    Supports = definition.Supports.Select(SupportView).ToList().AsReadOnly(),
                    
                    Evidence = knowledge.Entries, Log = log.ToList().AsReadOnly()
                };
            }
        }

        public CommandResult Execute(BattleCommand input)
        {
            if (input == null) return Reject("缺少操作。");
            if (phase != BattlePhase.PlayerTurn) return Reject("战斗已结束或尚未进入玩家回合。");
            if (pendingReorder != null && input.kind != CommandKind.ReorderDeck) return Reject("必须先完成抽牌顺序选择。");
            var command = input.Copy();
            switch (command.kind)
            {
                case CommandKind.PlayCard:
                    var card = hand.FirstOrDefault(x => x.InstanceId == command.cardInstanceId);
                    if (card == null) return Reject("该卡牌不在手中。");
                    if (card.IsEncounter)
                    {
                        var encounterResult = boss.Play(card, command.targetId);
                        if (!encounterResult.Success) return encounterResult;
                        CheckOutcome();
                        break;
                    }
                    var cardError = ValidateCardPlay(card, command.targetId);
                    if (cardError != null) return Reject(cardError);
                    actions -= card.Definition.Cost; sanity -= card.Definition.SanityCost;
                    hand.Remove(card); // In-flight cards cannot retrieve themselves.
                    Record($"使用 {card.Definition.Name}；支付 {card.Definition.Cost} 行动 / {card.Definition.SanityCost} 理智。");
                    if (IsCatalogCard(card.Definition))
                    {
                        ApplyCatalogCard(card, command.targetId);
                    }
                    else
                    {
                        var context = new EffectContext(this, command.targetId);
                        foreach (var effect in card.Definition.Effects) effects.Get(effect.Id).Apply(context, effect.Amount);
                    }
                    (card.Definition.Exhaust ? exhaust : discard).Add(card);
                    CheckOutcome();
                    break;
                case CommandKind.UseTool:
                    var tool = definition.Tools.FirstOrDefault(x => x.Id == command.toolId);
                    if (tool == null || toolUses[tool.Id] <= 0) return Reject("道具不可用。");
                    if (!IsLiving(command.targetId)) return Reject("请选择存活目标。");
                    if (sanity <= tool.SanityCost) return Reject("理智不足以支付代价。");
                    if (tool.Kind == InvestigationKind.SoundSource && directionalListeningBlocked) return Reject("耳棉阻止了定向听取；未消耗次数。");
                    if (!CanInvestigate(command.targetId, tool.Kind)) return Reject("该问题已回答，或道具无法观察这一动作；未消耗次数。");
                    toolUses[tool.Id]--; sanity -= tool.SanityCost;
                    Record($"使用 {tool.Name}；支付 {tool.SanityCost} 理智。");
                    Investigate(command.targetId, tool.Kind, tool.Name);
                    break;
                case CommandKind.UseCrewSupport:
                    var support = definition.Supports.FirstOrDefault(x => x.Id == command.supportId);
                    if (support == null || usedSupports.Contains(support.Id)) return Reject("该船员本场不可再支援。");
                    if (crewSupportUsed || actions < 1) return Reject("每回合至多一次船员支援，需要 1 行动。");
                    if (support.Kind == CrewSupportKind.Listener && (!IsLiving(command.targetId) || !CanInvestigate(command.targetId, InvestigationKind.SoundSource)))
                        return Reject("请选择有新声源信息的存活目标。");
                    actions--; crewSupportUsed = true; usedSupports.Add(support.Id);
                    Record($"{support.Name}提供支援；支付 1 行动。");
                    if (support.Kind == CrewSupportKind.Medic) Heal(3);
                    else Investigate(command.targetId, InvestigationKind.SoundSource, support.Name);
                    break;
                case CommandKind.ReorderDeck:
                    var reorderError = ResolvePendingReorder(command.targetId);
                    if (reorderError != null) return Reject(reorderError);
                    Record("完成抽牌顺序调整。");
                    break;
                case CommandKind.AdvanceObjective:
                    var taskCost = Math.Max(0, 1 - sceneDiscount);
                    if (!objective.AllowsTaskAction || taskUsed || actions < taskCost) return Reject("任务行动不可用（每回合一次，行动不足）。");
                    actions -= taskCost; sceneDiscount = 0; taskUsed = true; sceneActionUsed = true; AdvanceObjective(1); TriggerSceneAction(); CheckOutcome();
                    break;
                case CommandKind.EndTurn:
                    ResolveTurn();
                    break;
                default: return Reject("未知操作。");
            }
            history.Add(command);
            return new CommandResult(true);
        }

        private static CommandResult Reject(string reason) => new CommandResult(false, reason);
        private static string Describe(IntentSpec intent) => $"{intent.Name} · {intent.Amount} 点{(intent.Channel == DamageChannel.Health ? "生命伤害" : "理智冲击")}{(intent.Interruptible ? " · 可打断" : "")}";
        private void SetPhase(BattlePhase value) { phase = value; Record($"阶段：{value}"); }
        private void Record(string message) => log.Add($"[{turn}] {message}");
        private EnemyState Find(string id) => enemies.FirstOrDefault(x => x.Definition.Id == id);
        private bool IsLiving(string id) => Find(id)?.Health > 0;
        private bool IsTargetable(string id) => IsLiving(id) && (boss == null || boss.IsTargetable(id));
        internal bool IsConfirmed(string id) => Find(id)?.Confirmed == true;

        private void BeginTurn()
        {
            turn++; SetPhase(BattlePhase.TurnStart); block = 0; ward = 0; taskUsed = false; actions = definition.Actions;
            investigationUsed = sceneActionUsed = crewSupportUsed = directionalListeningBlocked = directionalListeningUsed = false;
            watchedInvestigationBonus = sceneFailureReduction = sceneDiscount = 0; turnFlags.Clear();
            Draw(definition.DrawCount);
            boss?.BeginPlayerTurn();
            foreach (var enemy in enemies.Where(x => x.Health > 0))
            {
                if (boss != null) continue;
                enemy.ActualIntent = enemy.Definition.Intents[intentRandom.Next(enemy.Definition.Intents.Count)];
                enemy.Confirmed = enemy.ActualIntent.IsPublic; enemy.Interrupted = false;
                Record($"{enemy.Definition.Name}：{enemy.ActualIntent.Clue}");
            }
            SetPhase(BattlePhase.IntentLocked); SetPhase(BattlePhase.PlayerTurn);
        }

        private void ResolveTurn()
        {
            SetPhase(BattlePhase.Resolve);
            foreach (var card in hand.Where(x => !x.IsEncounter).ToList())
            {
                if (retained.Remove(card.InstanceId)) continue;
                hand.Remove(card); discard.Add(card);
            }
            if (boss != null)
            {
                boss.ResolveEnemyTurn();
                if (health == 0 || sanity == 0) { CheckOutcome(); return; }
            }
            else foreach (var enemy in enemies.Where(x => x.Health > 0))
            {
                if (enemy.Interrupted) { Record($"{enemy.Definition.Name}的行动已被打断。"); continue; }
                ApplyThreat(enemy.ActualIntent.Channel, enemy.ActualIntent.Amount, enemy.Definition.Name, Describe(enemy.ActualIntent));
                if (health == 0 || sanity == 0) { CheckOutcome(); return; }
            }
            retained.Clear();
            nextHealthReduction = 0;
            SetPhase(BattlePhase.TurnEnd); objective.OnTurnResolved();
            if (!CheckOutcome()) BeginTurn();
        }

        private bool CheckOutcome()
        {
            if (health == 0 || sanity == 0) { SetPhase(BattlePhase.Defeat); return true; }
            if (boss != null && boss.IsVictory) { SetPhase(BattlePhase.Victory); return true; }
            if (boss == null && objective.IsComplete(enemies.All(x => x.Health == 0))) { SetPhase(BattlePhase.Victory); return true; }
            return false;
        }

        internal void Draw(int amount)
        {
            for (var i = 0; i < amount; i++)
            {
                if (draw.Count == 0) { draw.AddRange(discard); discard.Clear(); deckRandom.Shuffle(draw); }
                if (draw.Count == 0) break;
                var index = draw.Count - 1; hand.Add(draw[index]); draw.RemoveAt(index);
            }
        }
        internal void DamageEnemy(string id, int amount)
        {
            var enemy = Find(id);
            if (enemy == null || enemy.Health == 0) return;
            var dealt = boss?.LimitDamage(id, amount, enemy.Health) ?? amount;
            enemy.Health = Math.Max(0, enemy.Health - dealt); Record($"{enemy.Definition.Name}受到 {dealt} 伤害。");
            if (enemy.Health == 0) MarkEnemyDefeated();
            boss?.EnemyDamaged(id);
        }
        internal void AddBlock(int amount) { block += amount; Record($"获得 {amount} 格挡。"); }
        internal void AddWard(int amount) { ward += amount; Record($"获得 {amount} 理智防护。"); }
        internal void Heal(int amount) { health = Math.Min(definition.Health, health + amount); Record($"恢复至 {health} 生命。"); }
        internal void AdvanceObjective(int amount) { objective.Advance(amount); Record(objective.Description); }
        internal void Interrupt(string id)
        {
            var enemy = Find(id);
            if (enemy == null || enemy.Health == 0) return;
            if (enemy.ActualIntent.Interruptible) { enemy.Interrupted = true; Record($"{enemy.Definition.Name}的可中断施术已被打断。"); }
            else Record("目标行动无法打断。");
        }
        internal void RecordClue(string id)
        {
            var enemy = Find(id); if (enemy == null) return;
            knowledge.Record(new Evidence($"{id}/{turn}/clue", enemy.ActualIntent.Clue, "观察笔记", KnowledgeLevel.Observed));
            Record("记录当前可见征兆。");
        }
        private string EvidenceKey(string id, InvestigationKind kind) => $"{id}/{turn}/{kind}";
        private bool CanInvestigate(string id, InvestigationKind kind)
        {
            var enemy = Find(id);
            if (enemy?.ActualIntent == null) return false;
            if (kind == InvestigationKind.VisibleAction) return enemy.ActualIntent.VisibleAction && !enemy.Confirmed;
            return !knowledge.Entries.Any(x => x.Key == EvidenceKey(id, kind));
        }
        private CrewSupportView SupportView(CrewSupportSpec support)
        {
            var targets = support.Kind == CrewSupportKind.Listener
                ? enemies.Where(x => x.Health > 0 && CanInvestigate(x.Definition.Id, InvestigationKind.SoundSource))
                    .Select(x => new EncounterTargetView(x.Definition.Id, x.Definition.Name, "获取当前声源证据", true)).ToList()
                : new List<EncounterTargetView>();
            var used = usedSupports.Contains(support.Id);
            var reason = used ? "本场已使用。" : crewSupportUsed ? "本回合已使用船员支援。" : actions < 1 ? "行动点不足。" :
                support.Kind == CrewSupportKind.Listener && targets.Count == 0 ? "没有新的声源目标。" : "";
            return new CrewSupportView(support, used, phase == BattlePhase.PlayerTurn && reason == "", reason, targets.AsReadOnly());
        }

        internal void Investigate(string id, InvestigationKind kind, string source)
        {
            var enemy = Find(id);
            if (kind == InvestigationKind.SoundSource && directionalListeningBlocked) { Record("耳棉阻止了定向听取。"); return; }
            if (enemy == null || !CanInvestigate(id, kind)) { Record("没有可揭示的新信息。"); return; }
            investigationUsed = true;
            string information;
            switch (kind)
            {
                case InvestigationKind.VisibleAction: enemy.Confirmed = true; information = Describe(enemy.ActualIntent); break;
                case InvestigationKind.SoundSource: information = $"声源：{enemy.ActualIntent.SoundSource}（仅确认来源，不确认完整意图）"; break;
                case InvestigationKind.DamageChannel: information = $"主要威胁：{(enemy.ActualIntent.Channel == DamageChannel.Health ? "肉体" : "理智")}"; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            knowledge.Record(new Evidence(EvidenceKey(id, kind), information, source, KnowledgeLevel.Observed));
            Record($"{source}观察到：{information}");
            if (kind == InvestigationKind.SoundSource) directionalListeningUsed = true;
            if (watchedInvestigationBonus > 0)
            {
                var extras = new[] { InvestigationKind.DamageChannel, InvestigationKind.SoundSource, InvestigationKind.VisibleAction };
                var available = extras.Where(x => x != kind && CanInvestigate(id, x)).ToList();
                if (available.Count > 0)
                {
                    watchedInvestigationBonus = 0;
                    Investigate(id, available[0], source + "·追问");
                }
            }
        }
    }
}
