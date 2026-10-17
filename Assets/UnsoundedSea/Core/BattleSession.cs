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
            public int CycleOffset;
            public int HealthThreatReduction, SanityThreatReduction;
        }
        private const string SealCardId = "E01-01";
        private static readonly CardSpec SealCard = new CardSpec(SealCardId, "绘制封线", "消耗3行动推进封线1段。每回合一次；合法场景折扣照常生效。", EncounterObjective.SealActionCost, new[] { new EffectSpec("objective", 1) });
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
        private readonly List<ResolvedEnemyAction> resolvedEnemyActions = new List<ResolvedEnemyAction>();
        private readonly List<string> log = new List<string>();
        private readonly KnowledgeState knowledge = new KnowledgeState();
        private readonly IEncounterObjective objective;
        private BattlePhase phase;
        private int turn, health, sanity, actions, block, ward;
        private bool taskUsed, investigationUsed, sceneActionUsed, crewSupportUsed, directionalListeningBlocked, directionalListeningUsed;
        private int nextInstanceId, watched, nextHealthReduction, sceneFailureReduction, sceneDiscount;
        private readonly HashSet<int> retained = new HashSet<int>();
        private readonly HashSet<int> carriedFromLastTurn = new HashSet<int>();
        private readonly HashSet<string> newCardAnswers = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<int>> weaponHits = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
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
            var secondary = EnemyBehavior.IsSecondaryObjective(definition.Id);
            var objectiveKind = secondary ? EnemyBehavior.SecondaryObjectiveKind(definition.Id) : definition.Objective;
            this.objective = objective ?? new EncounterObjective(objectiveKind, secondary ? 3 : definition.ObjectiveTarget, secondary);
            Seed = seed; deckRandom = new DeterministicRandom(seed); intentRandom = new DeterministicRandom(seed ^ 0x9E3779B9u);
            bossKind = Require.Defined(boss);
            health = definition.InitialHealth; sanity = definition.InitialSanity;
            watched=definition.Loadout.Watched;beaconAvailable=definition.Loadout.SurveyedBeacon;echoRitualAvailable=definition.Loadout.EchoRitual;
            enemies = definition.Enemies.Select(x => new EnemyState { Definition = x, Health = x.Health }).ToList();
            if (EnemyBehavior.StyleForEncounter(definition.Id) == EnemyStyle.Rhythm)
                foreach (var enemy in enemies) enemy.CycleOffset = intentRandom.Next(enemy.Definition.Intents.Count);
            toolUses = definition.Tools.ToDictionary(x => x.Id, x => x.Uses, StringComparer.Ordinal);
            for (var i = 0; i < definition.Deck.Count; i++) draw.Add(new CardView(i + 1, definition.Deck[i]));
            nextInstanceId = definition.Deck.Count + 100;
            deckRandom.Shuffle(draw);
            if (bossKind != BossEncounterKind.None) this.boss = new BossEncounterState(this, bossKind, seed);
            BeginTurn();
            if (this.boss == null && this.objective.AllowsTaskAction)
                hand.Add(new CardView(definition.Deck.Count + 1, SealCard, isEncounter: true));
        }

        public BattleView View
        {
            get
            {
                RefreshCardViews();
                return new BattleView
                {
                    ResolvedEnemyActions = resolvedEnemyActions.ToList().AsReadOnly(),
                    Phase = phase, Turn = turn, Health = health, Sanity = sanity, Actions = actions, Block = block, Ward = ward, Watched = watched,
                    BossKind = bossKind, BossStage = boss?.Stage ?? 0, BossStatus = boss?.Status ?? "",
                    EncounterTargets = boss?.AllTargets ?? EmptyTargets,
                    PendingChoice = PendingChoiceView(),
                    DrawCount = draw.Count, DiscardCount = discard.Count, ExhaustCount = exhaust.Count,
                    Objective = boss?.Objective ?? objective.Description, ObjectiveProgress = boss?.Progress ?? objective.Progress,
                    ObjectiveTarget = boss?.Target ?? objective.Target, CanAdvanceObjective = boss == null && phase == BattlePhase.PlayerTurn && objective.AllowsTaskAction && !taskUsed && actions >= Math.Max(0, objective.ActionCost - sceneDiscount),
                    Hand = hand.Select(x => x.Snapshot()).ToList().AsReadOnly(),
                    Enemies = enemies.Select(BuildEnemyView).ToList().AsReadOnly(),
                    Tools = definition.Tools.Select(BuildToolView).ToList().AsReadOnly(),
                    Supports = definition.Supports.Select(BuildSupportView).ToList().AsReadOnly(),
                    SupplyActions=SupplyViews().ToList().AsReadOnly(),SpentConsumables=spentConsumables.ToList().AsReadOnly(),Equipment=definition.Loadout.Equipment,SurveyedBeacon=beaconAvailable,
                    
                    Evidence = knowledge.Entries, Log = log.ToList().AsReadOnly()
                };
            }
        }

        public CommandResult Execute(BattleCommand input)
        {
            if (input == null) return Reject("缺少操作。");
            if (phase != BattlePhase.PlayerTurn) return Reject("战斗已结束或尚未进入玩家回合。");
            if ((pendingReorder != null || pendingRetain) && input.kind != CommandKind.ReorderDeck) return Reject("必须先完成卡牌选择。");
            var command = input.Copy();
            currentPaidActions=0;
            var beforeBound=boss?.BoundCount??0;var beforeEvac=boss?.EvacuationProgress??0;var beforeSafe=boss?.SafeOpenedCount??0;
            switch (command.kind)
            {
                case CommandKind.PlayCard:
                    var card = hand.FirstOrDefault(x => x.InstanceId == command.cardInstanceId);
                    if (card == null) return Reject("该卡牌不在手中。");
                    if (card.IsEncounter)
                    {
                        CommandResult encounterResult;
                        if (boss != null) encounterResult = boss.Play(card, command.targetId);
                        else if (card.Definition.Id == SealCardId && command.targetId == null) encounterResult = ExecuteObjectiveAction();
                        else encounterResult = Reject("该遭遇牌不可用。");
                        if (!encounterResult.Success) return encounterResult;
                        CheckOutcome();
                        break;
                    }
                    var cardError = ValidateCardPlay(card, command.targetId);
                    if (cardError != null) return Reject(cardError);
                    actions -= card.Definition.Cost; sanity -= card.Definition.SanityCost;
                    currentPaidActions=card.Definition.Cost;ActiveSanityEquipment(card.Definition.SanityCost);
                    var beforeDraw=cardsDrawn;
                    hand.Remove(card); // In-flight cards cannot retrieve themselves.
                    var wasCarried = carriedFromLastTurn.Remove(card.InstanceId);
                    retained.Remove(card.InstanceId);
                    Record($"使用 {card.Definition.Name}；支付 {card.Definition.Cost} 行动 / {card.Definition.SanityCost} 理智。");
                    if (IsCatalogCard(card.Definition))
                    {
                        ApplyCatalogCard(card, command.targetId, wasCarried);
                        EquipmentAfterCard(card,beforeDraw);
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
                    if (tool == null) return Reject("道具不可用。");
                    if ((int)tool.Kind>=3) { if (toolUses[tool.Id] <= 0) return Reject("道具不可用。"); var error=UseSupply(tool.Id,command.targetId,false);if(error!=null)return Reject(error);break; }
                    var investigationError = InvestigationError(tool, command.targetId);
                    if (investigationError != null) return Reject(investigationError);
                    toolUses[tool.Id]--; sanity -= tool.SanityCost;
                    ActiveSanityEquipment(tool.SanityCost);turnFlags.Add("tool-new-answer");
                    Record($"使用 {tool.Name}；支付 {tool.SanityCost} 理智。");
                    Investigate(command.targetId, tool.Kind, tool.Name);
                    break;
                case CommandKind.UseCrewSupport:
                    var support = definition.Supports.FirstOrDefault(x => x.Id == command.supportId);
                    var crewError=UseCrew(support,command.targetId);if(crewError!=null)return Reject(crewError);
                    break;
                case CommandKind.UseConsumable:
                    var supplyError=UseSupply(command.toolId,command.targetId,true);if(supplyError!=null)return Reject(supplyError);
                    break;
                case CommandKind.ReorderDeck:
                    var reorderError = ResolvePendingReorder(command.targetId);
                    if (reorderError != null) return Reject(reorderError);
                    Record("完成抽牌顺序调整。");
                    break;
                case CommandKind.AdvanceObjective:
                    // Kept for replay/checkpoint compatibility; current UI plays the persistent encounter card.
                    var taskResult = ExecuteObjectiveAction();
                    if (!taskResult.Success) return taskResult;
                    CheckOutcome();
                    break;
                case CommandKind.EndTurn:
                    resolvedEnemyActions.Clear();
                    ResolveTurn();
                    break;
                default: return Reject("未知操作。");
            }
            if (command.kind != CommandKind.EndTurn) resolvedEnemyActions.Clear();
            if(command.kind!=CommandKind.EndTurn)EquipmentTransitions(beforeBound,beforeEvac,beforeSafe);
            history.Add(command);
            return new CommandResult(true);
        }

        private CommandResult ExecuteObjectiveAction()
        {
            var taskCost = Math.Max(0, objective.ActionCost - sceneDiscount);
            if (!objective.AllowsTaskAction || taskUsed || actions < taskCost)
                return Reject("任务行动不可用（每回合一次，行动不足）。");
            actions -= taskCost; sceneDiscount = 0; taskUsed = true; sceneActionUsed = true;
            AdvanceObjective(1); TriggerSceneAction();
            return new CommandResult(true);
        }

        private static CommandResult Reject(string reason) => new CommandResult(false, reason);
        private static string Describe(IntentSpec intent, int surge = 0) => $"{intent.Name} · {intent.Amount + surge} 点{(intent.Channel == DamageChannel.Health ? "生命伤害" : "理智冲击")}{(intent.Interruptible ? " · 可打断" : "")}";
        private int CurrentThreatAmount(EnemyState enemy) =>
            Math.Max(0, enemy.ActualIntent.Amount +
                EnemyBehavior.DamageBonus(turn, definition.Id, enemy.ActualIntent.Amount) -
                (enemy.ActualIntent.Channel == DamageChannel.Health ? enemy.HealthThreatReduction : enemy.SanityThreatReduction));
        private string DescribeCurrent(EnemyState enemy) =>
            Describe(enemy.ActualIntent, CurrentThreatAmount(enemy) - enemy.ActualIntent.Amount);
        private void SetPhase(BattlePhase value) { phase = value; Record($"阶段：{value}"); }
        private void Record(string message) => log.Add($"[{turn}] {message}");
        private EnemyState Find(string id) => enemies.FirstOrDefault(x => x.Definition.Id == id);
        private bool IsLiving(string id) => Find(id)?.Health > 0;
        private bool IsTargetable(string id) => IsLiving(id) && (boss == null || boss.IsTargetable(id));
        internal bool IsConfirmed(string id) => Find(id)?.Confirmed == true;
        internal ObjectiveKind ObjectiveKind => objective is EncounterObjective encounter ? encounter.Kind : definition.Objective;
        internal bool HasSecondaryObjective => EnemyBehavior.IsSecondaryObjective(definition.Id);
        internal int ObjectiveProgress => objective.Progress;
        internal int ObjectiveTarget => objective.Target;
        internal bool AdvanceOrdinaryObjective(int amount)
        {
            if (!HasSecondaryObjective) return false;
            var before = objective.Progress;
            AdvanceObjective(amount);
            CheckOutcome();
            return objective.Progress != before;
        }

        private EnemyView BuildEnemyView(EnemyState enemy)
        {
            if (boss != null) return BossEnemyView(enemy);
            var surge = EnemyBehavior.Surge(turn, definition.Id);
            var style = EnemyBehavior.StyleForEncounter(definition.Id);
            if (style == EnemyStyle.None) style = EnemyBehavior.StyleForEnemy(enemy.Definition.Id, enemy.Definition.IntentPolicy);
            var hiddenIntent = enemy.Health == 0 ? "" : enemy.Interrupted ? "已打断" :
                !enemy.Confirmed ? EnemyBehavior.Candidates(enemy.Definition, surge, definition.Id, enemy.HealthThreatReduction, enemy.SanityThreatReduction) :
                CurrentThreatAmount(enemy) == 0 ? "" : DescribeCurrent(enemy);
            return new EnemyView(enemy.Definition.Id, enemy.Definition.Name, enemy.Health, enemy.Definition.Health,
                enemy.Health == 0 ? "已击退" : enemy.ActualIntent.Clue, hiddenIntent, enemy.Confirmed, true, surge, style);
        }


        private void BeginTurn()
        {
            turn++; SetPhase(BattlePhase.TurnStart); block = 0; ward = 0; taskUsed = false; actions = definition.Actions;
            investigationUsed = sceneActionUsed = crewSupportUsed = directionalListeningBlocked = directionalListeningUsed = false;
            watchedInvestigationBonus = sceneFailureReduction = sceneDiscount = 0; turnFlags.Clear();
            newCardAnswers.Clear(); weaponHits.Clear();cardQuestions.Clear();
            Draw(definition.DrawCount);
            boss?.BeginPlayerTurn();
            foreach (var enemy in enemies.Where(x => x.Health > 0))
            {
                if (boss != null) continue;
                var style = EnemyBehavior.StyleForEncounter(definition.Id);
                var intentIndex = style == EnemyStyle.Rhythm
                    ? (turn - 1 + enemy.CycleOffset) % enemy.Definition.Intents.Count
                    : style == EnemyStyle.Hidden
                        ? intentRandom.Next(enemy.Definition.Intents.Count)
                        : enemy.Definition.IntentPolicy == IntentPolicy.Cycle
                            ? (turn - 1) % enemy.Definition.Intents.Count
                            : intentRandom.Next(enemy.Definition.Intents.Count);
                enemy.ActualIntent = enemy.Definition.Intents[intentIndex];
                enemy.Confirmed = style == EnemyStyle.Rhythm || enemy.ActualIntent.IsPublic || enemy.Definition.Intents.Count == 1;
                enemy.Interrupted = false; enemy.HealthThreatReduction = enemy.SanityThreatReduction = 0;
                Record($"{enemy.Definition.Name}：{enemy.ActualIntent.Clue}");
            }
            SetPhase(BattlePhase.IntentLocked); SetPhase(BattlePhase.PlayerTurn);
        }

        private void ResolveTurn()
        {
            SetPhase(BattlePhase.Resolve);
            carriedFromLastTurn.Clear();
            foreach (var card in hand.Where(x => !x.IsEncounter).ToList())
            {
                if (retained.Remove(card.InstanceId)) { carriedFromLastTurn.Add(card.InstanceId); continue; }
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
                var amount = CurrentThreatAmount(enemy);
                ApplyEnemyThreat(enemy, enemy.ActualIntent.Channel, amount,
                    Describe(enemy.ActualIntent, amount - enemy.ActualIntent.Amount));
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
                var index = draw.Count - 1; hand.Add(draw[index]); draw.RemoveAt(index);cardsDrawn++;
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
        private ToolView BuildToolView(ToolSpec tool)
        {
            if ((int)tool.Kind >= 3)
                return new ToolView(tool, toolUses[tool.Id], toolUses[tool.Id] > 0,
                    toolUses[tool.Id] > 0 ? "" : "道具不可用。", new List<EncounterTargetView>().AsReadOnly());
            var targets = enemies.Where(x => x.Health > 0).Select(x =>
            {
                var error = InvestigationError(tool, x.Definition.Id);
                return new EncounterTargetView(x.Definition.Id, x.Definition.Name, error ?? "可调查", error == null);
            }).ToList().AsReadOnly();
            var firstError = phase != BattlePhase.PlayerTurn ? "战斗已结束或尚未进入玩家回合。"
                : pendingReorder != null || pendingRetain ? "必须先完成卡牌选择。"
                : toolUses[tool.Id] <= 0 ? "道具不可用。"
                : sanity <= tool.SanityCost ? "理智不足以支付代价。"
                : tool.Kind == InvestigationKind.SoundSource && directionalListeningBlocked ? "耳棉阻止了定向听取；未消耗次数。"
                : targets.Any(x => x.Enabled) ? "" : "当前没有可调查的新信息。";
            return new ToolView(tool, toolUses[tool.Id], firstError.Length == 0, firstError, targets);
        }

        private string InvestigationError(ToolSpec tool, string id)
        {
            if (toolUses[tool.Id] <= 0) return "道具不可用。";
            if (!IsLiving(id)) return "请选择存活目标。";
            if (sanity <= tool.SanityCost) return "理智不足以支付代价。";
            if (tool.Kind == InvestigationKind.SoundSource && directionalListeningBlocked) return "耳棉阻止了定向听取；未消耗次数。";
            return CanInvestigate(id, tool.Kind) ? null : "该问题已回答，或道具无法观察这一动作；未消耗次数。";
        }

        private string EvidenceKey(string id, InvestigationKind kind) => $"{id}/{turn}/{kind}";
        private bool CanInvestigate(string id, InvestigationKind kind)
        {
            var enemy = Find(id);
            if (enemy?.ActualIntent == null) return false;
            if (kind == InvestigationKind.VisibleAction) return enemy.ActualIntent.VisibleAction && !enemy.Confirmed;
            return !knowledge.Entries.Any(x => x.Key == EvidenceKey(id, kind));
        }

        internal void Investigate(string id, InvestigationKind kind, string source, bool fromInvestigationCard = false)
        {
            var enemy = Find(id);
            if (kind == InvestigationKind.SoundSource && directionalListeningBlocked) { Record("耳棉阻止了定向听取。"); return; }
            if (enemy == null || !CanInvestigate(id, kind)) { Record("没有可揭示的新信息。"); return; }
            investigationUsed = true;
            string information;
            switch (kind)
            {
                case InvestigationKind.VisibleAction:
                    enemy.Confirmed = true;
                    information = DescribeCurrent(enemy);
                    break;
                case InvestigationKind.SoundSource: information = $"声源：{enemy.ActualIntent.SoundSource}（仅确认来源，不确认完整意图）"; break;
                case InvestigationKind.DamageChannel: information = $"主要威胁：{(enemy.ActualIntent.Channel == DamageChannel.Health ? "肉体" : "理智")}"; break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
            knowledge.Record(new Evidence(EvidenceKey(id, kind), information, source, KnowledgeLevel.Observed,EvidenceScope.Turn,id,SubjectType(id),kind.ToString(),definition.Id,turn,fact:kind==InvestigationKind.SoundSource?enemy.ActualIntent.SoundSource:information));
            if (fromInvestigationCard) { newCardAnswers.Add(id);if(!cardQuestions.TryGetValue(id,out var questions))cardQuestions[id]=questions=new HashSet<InvestigationKind>();questions.Add(kind); }
            if(kind==InvestigationKind.VisibleAction)ConfirmationEquipment();
            Record($"{source}观察到：{information}");
            if (kind == InvestigationKind.SoundSource) directionalListeningUsed = true;
            if (watchedInvestigationBonus > 0)
            {
                var extras = new[] { InvestigationKind.DamageChannel, InvestigationKind.SoundSource, InvestigationKind.VisibleAction };
                var available = extras.Where(x => x != kind && CanInvestigate(id, x)).ToList();
                if (available.Count > 0)
                {
                    watchedInvestigationBonus = 0;
                    Investigate(id, available[0], source + "·追问", fromInvestigationCard);
                }
            }
        }
    }
}
