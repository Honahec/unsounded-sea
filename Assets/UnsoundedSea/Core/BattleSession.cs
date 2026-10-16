using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public sealed class BattleSession
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
        private readonly List<BattleCommand> history = new List<BattleCommand>();
        private readonly List<string> log = new List<string>();
        private readonly KnowledgeState knowledge = new KnowledgeState();
        private readonly IEncounterObjective objective;
        private BattlePhase phase;
        private int turn, health, sanity, actions, block, ward;
        private bool taskUsed;
        public uint Seed { get; }
        internal BattleDefinition Definition => definition;
        public IReadOnlyList<BattleCommand> Commands => history.Select(x => x.Copy()).ToList().AsReadOnly();

        public BattleSession(BattleDefinition definition, uint seed, EffectRegistry effects = null, IEncounterObjective objective = null)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.effects = (effects ?? EffectRegistry.CreateDefault()).Snapshot();
            foreach (var effect in definition.Deck.SelectMany(x => x.Effects)) this.effects.Get(effect.Id);
            this.objective = objective ?? new EncounterObjective(definition.Objective, definition.ObjectiveTarget);
            Seed = seed; deckRandom = new DeterministicRandom(seed); intentRandom = new DeterministicRandom(seed ^ 0x9E3779B9u);
            health = definition.Health; sanity = definition.Sanity;
            enemies = definition.Enemies.Select(x => new EnemyState { Definition = x, Health = x.Health }).ToList();
            toolUses = definition.Tools.ToDictionary(x => x.Id, x => x.Uses, StringComparer.Ordinal);
            for (var i = 0; i < definition.Deck.Count; i++) draw.Add(new CardView(i + 1, definition.Deck[i]));
            deckRandom.Shuffle(draw); BeginTurn();
        }

        public BattleView View => new BattleView
        {
            Phase = phase, Turn = turn, Health = health, Sanity = sanity, Actions = actions, Block = block, Ward = ward,
            DrawCount = draw.Count, DiscardCount = discard.Count, ExhaustCount = exhaust.Count,
            Objective = objective.Description, CanAdvanceObjective = phase == BattlePhase.PlayerTurn && objective.AllowsTaskAction && !taskUsed && actions >= 1,
            Hand = hand.ToList().AsReadOnly(),
            Enemies = enemies.Select(e => new EnemyView(e.Definition.Id, e.Definition.Name, e.Health, e.Definition.Health,
                e.Health == 0 ? "已击退" : e.ActualIntent.Clue,
                e.Health == 0 ? "不会行动" : e.Interrupted ? "已打断 · 本回合不行动" : e.Confirmed ? Describe(e.ActualIntent) : e.Definition.Candidates,
                e.Confirmed)).ToList().AsReadOnly(),
            Tools = definition.Tools.Select(t => new ToolView(t, toolUses[t.Id])).ToList().AsReadOnly(),
            Evidence = knowledge.Entries, Log = log.ToList().AsReadOnly()
        };

        public CommandResult Execute(BattleCommand input)
        {
            if (input == null) return Reject("缺少操作。");
            if (phase != BattlePhase.PlayerTurn) return Reject("战斗已结束或尚未进入玩家回合。");
            var command = input.Copy();
            switch (command.kind)
            {
                case CommandKind.PlayCard:
                    var card = hand.FirstOrDefault(x => x.InstanceId == command.cardInstanceId);
                    if (card == null) return Reject("该卡牌不在手中。");
                    if (actions < card.Definition.Cost) return Reject("行动点不足。");
                    // Active costs cannot be absorbed by Ward, and cannot pay the last point of sanity.
                    if (sanity <= card.Definition.SanityCost) return Reject("理智不足以支付代价。");
                    if (card.Definition.Effects.Any(x => effects.Get(x.Id).RequiresEnemy) && !IsLiving(command.targetId)) return Reject("请选择存活目标。");
                    actions -= card.Definition.Cost; sanity -= card.Definition.SanityCost;
                    hand.Remove(card); // In-flight cards cannot draw themselves from discard.
                    Record($"使用 {card.Definition.Name}；支付 {card.Definition.Cost} 行动 / {card.Definition.SanityCost} 理智。");
                    var context = new EffectContext(this, command.targetId);
                    foreach (var effect in card.Definition.Effects) effects.Get(effect.Id).Apply(context, effect.Amount);
                    (card.Definition.Exhaust ? exhaust : discard).Add(card);
                    CheckOutcome();
                    break;
                case CommandKind.UseTool:
                    var tool = definition.Tools.FirstOrDefault(x => x.Id == command.toolId);
                    if (tool == null || toolUses[tool.Id] <= 0) return Reject("道具不可用。");
                    if (!IsLiving(command.targetId)) return Reject("请选择存活目标。");
                    if (sanity <= tool.SanityCost) return Reject("理智不足以支付代价。");
                    if (!CanInvestigate(command.targetId, tool.Kind)) return Reject("该问题已回答，或道具无法观察这一动作；未消耗次数。");
                    toolUses[tool.Id]--; sanity -= tool.SanityCost;
                    Record($"使用 {tool.Name}；支付 {tool.SanityCost} 理智。");
                    Investigate(command.targetId, tool.Kind, tool.Name);
                    break;
                case CommandKind.AdvanceObjective:
                    if (!objective.AllowsTaskAction || taskUsed || actions < 1) return Reject("任务行动不可用（每回合一次，需要 1 行动）。");
                    actions--; taskUsed = true; AdvanceObjective(1); CheckOutcome();
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
        internal bool IsConfirmed(string id) => Find(id)?.Confirmed == true;

        private void BeginTurn()
        {
            turn++; SetPhase(BattlePhase.TurnStart); block = 0; ward = 0; taskUsed = false; actions = definition.Actions;
            Draw(definition.DrawCount);
            foreach (var enemy in enemies.Where(x => x.Health > 0))
            {
                enemy.ActualIntent = enemy.Definition.Intents[intentRandom.Next(enemy.Definition.Intents.Count)];
                enemy.Confirmed = enemy.ActualIntent.IsPublic; enemy.Interrupted = false;
                Record($"{enemy.Definition.Name}：{enemy.ActualIntent.Clue}");
            }
            SetPhase(BattlePhase.IntentLocked); SetPhase(BattlePhase.PlayerTurn);
        }

        private void ResolveTurn()
        {
            SetPhase(BattlePhase.Resolve); discard.AddRange(hand); hand.Clear();
            foreach (var enemy in enemies.Where(x => x.Health > 0))
            {
                if (enemy.Interrupted) { Record($"{enemy.Definition.Name}的行动已被打断。"); continue; }
                var intent = enemy.ActualIntent;
                var protection = intent.Channel == DamageChannel.Health ? block : ward;
                var absorbed = Math.Min(protection, intent.Amount);
                var damage = intent.Amount - absorbed;
                if (intent.Channel == DamageChannel.Health) { block -= absorbed; health = Math.Max(0, health - damage); }
                else { ward -= absorbed; sanity = Math.Max(0, sanity - damage); }
                Record($"{enemy.Definition.Name}执行 {Describe(intent)}；抵御 {absorbed}，实际承受 {damage}。");
                if (health == 0 || sanity == 0) { CheckOutcome(); return; }
            }
            SetPhase(BattlePhase.TurnEnd); objective.OnTurnResolved();
            if (!CheckOutcome()) BeginTurn();
        }

        private bool CheckOutcome()
        {
            if (health == 0 || sanity == 0) { SetPhase(BattlePhase.Defeat); return true; }
            if (objective.IsComplete(enemies.All(x => x.Health == 0))) { SetPhase(BattlePhase.Victory); return true; }
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
        { var enemy = Find(id); if (enemy != null) { enemy.Health = Math.Max(0, enemy.Health - amount); Record($"{enemy.Definition.Name}受到 {amount} 伤害。"); } }
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
            if (kind == InvestigationKind.VisibleAction) return enemy.ActualIntent.VisibleAction && !enemy.Confirmed;
            return !knowledge.Entries.Any(x => x.Key == EvidenceKey(id, kind));
        }
        internal void Investigate(string id, InvestigationKind kind, string source)
        {
            var enemy = Find(id);
            if (enemy == null || !CanInvestigate(id, kind)) { Record("没有可揭示的新信息。"); return; }
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
        }
    }
}
