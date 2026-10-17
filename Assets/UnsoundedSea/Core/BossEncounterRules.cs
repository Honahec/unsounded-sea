using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public enum BossEncounterKind { None, Choirmaster, Surveyor, FlagBearer, Watcher }

    public sealed partial class BattleSession
    {
        private static readonly IReadOnlyList<EncounterTargetView> EmptyTargets = new List<EncounterTargetView>().AsReadOnly();

        private EnemyView BossEnemyView(EnemyState enemy)
        {
            if (boss != null) return boss.EnemyView(enemy);
            return new EnemyView(enemy.Definition.Id, enemy.Definition.Name, enemy.Health, enemy.Definition.Health,
                enemy.Health == 0 ? "已击退" : enemy.ActualIntent.Clue,
                enemy.Health == 0 ? "不会行动" : enemy.Interrupted ? "已打断 · 本回合不行动" : enemy.Confirmed ? Describe(enemy.ActualIntent) : enemy.Definition.Candidates,
                enemy.Confirmed);
        }

        private void ApplyThreat(DamageChannel channel, int amount, string source, string description = null)
        {
            if (health == 0 || sanity == 0) return;
            if (channel == DamageChannel.Health) amount = ConsumeHealthReduction(amount);
            var protection = channel == DamageChannel.Health ? block : ward;
            var absorbed = Math.Min(protection, amount); var damage = amount - absorbed;
            if (channel == DamageChannel.Health) { block -= absorbed; health = Math.Max(0, health - damage); }
            else { ward -= absorbed; sanity = Math.Max(0, sanity - damage); }
            Record($"{source}执行 {description ?? amount + " 点" + (channel == DamageChannel.Health ? "生命伤害" : "理智冲击")}；抵御 {absorbed}，实际承受 {damage}。");
        }

        private sealed class BossEncounterState
        {
            private static readonly string[][] Orders =
            {
                new[] { "choir-a", "choir-b", "choir-c" }, new[] { "choir-a", "choir-c", "choir-b" },
                new[] { "choir-b", "choir-a", "choir-c" }, new[] { "choir-b", "choir-c", "choir-a" },
                new[] { "choir-c", "choir-a", "choir-b" }, new[] { "choir-c", "choir-b", "choir-a" }
            };
            private static readonly string[] B03Mines = { "A4", "C2", "D4" };
            private static readonly string[] B03Initial = { "A1", "A2", "A3", "B1", "B2", "B3", "D1" };
            private static readonly string[] B03Goals = { "B4", "C1", "C4", "D2" };
            private static readonly string[] GuardNames = { "anchor", "lamp", "bell", "sail", "eye" };
            private readonly BattleSession s;
            private readonly BossEncounterKind kind;
            private readonly List<EncounterTargetView> targets = new List<EncounterTargetView>();
            private readonly HashSet<string> completed = new HashSet<string>(StringComparer.Ordinal);
            private readonly HashSet<string> read = new HashSet<string>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> ages = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly Dictionary<string, int> dormantUntil = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly string[] order;
            private readonly string[] guardOrder;
            private int stage = 1, progress, known, summonStep, alarm = 5, opensThisTurn, flagFinalTurn;
            private bool victory, primaryUsed, secondaryUsed, initialSpawned, sailBurst, eyeBurst, cardsGranted, stagePending, cancelMainThisTurn, cancelSummon, evacuationJustEntered;
            public int Stage => stage;
            public bool IsVictory => victory;
            public int Progress => progress;
            public int Target => kind == BossEncounterKind.Choirmaster ? (stage == 1 ? 3 : 36) : kind == BossEncounterKind.Surveyor ? 4 : kind == BossEncounterKind.FlagBearer ? (stage == 2 ? 3 : stage == 3 ? 48 : 2) : stage == 3 ? 2 : 5;
            public string Objective => kind == BossEncounterKind.Choirmaster ? (stage == 1 ? "按序关闭唱诗匣" : "击破发声囊") : kind == BossEncounterKind.Surveyor ? "激活四枚安全封栓" : kind == BossEncounterKind.FlagBearer ? (stage == 1 ? "校正双继电器" : stage == 2 ? "依序释放三段闸锁" : "击退执旗者") : stage == 1 ? "建立撤离绳" : stage == 2 ? "牵引守卫并关闭五座锚点" : "收绞归航绳";
            public string Status => BuildStatus();
            public IReadOnlyList<EncounterTargetView> AllTargets { get { RefreshCards(); return targets.Select(x => new EncounterTargetView(x.Id, x.Name, TargetDetail(x.Id), true)).ToList().AsReadOnly(); } }

            public BossEncounterState(BattleSession session, BossEncounterKind encounter, uint seed)
            {
                s = session; kind = encounter; order = Orders[seed % Orders.Length]; guardOrder = GuardNames.Skip((int)(seed % 5)).Concat(GuardNames.Take((int)(seed % 5))).ToArray(); ValidateSlots();
                for (var i = 1; i < s.enemies.Count; i++) s.enemies[i].Health = 0;
                BuildTargets();
                if (kind == BossEncounterKind.Surveyor) foreach (var id in B03Initial) completed.Add(id);
            }

            private void ValidateSlots()
            {
                string[] expected;
                switch (kind)
                {
                    case BossEncounterKind.Choirmaster: expected = new[] { "b02-choirmaster", "b02-bone-servant", "b02-tide-messenger" }; break;
                    case BossEncounterKind.Surveyor: expected = new[] { "b03-surveyor", "b03-moth-1", "b03-moth-2" }; break;
                    case BossEncounterKind.FlagBearer: expected = new[] { "b04-flag-bearer", "b04-flag-soldier" }; break;
                    case BossEncounterKind.Watcher: expected = new[] { "b05-watcher", "b05-guard-anchor", "b05-guard-lamp", "b05-guard-bell", "b05-guard-sail", "b05-guard-eye" }; break;
                    default: throw new ArgumentOutOfRangeException();
                }
                if (!s.enemies.Select(x => BaseId(x.Definition.Id)).SequenceEqual(expected)) throw new ArgumentException($"{kind} requires enemy slots: {string.Join(", ", expected)}");
            }

            private void AddCards()
            {
                string[] ids; string[] names; string[] descriptions; int[] costs;
                switch (kind)
                {
                    case BossEncounterKind.Choirmaster: ids = new[] { "E02-01", "E02-02" }; names = new[] { "聆听唱序", "按序封匣" }; descriptions = new[] { "公开下一条尚未知的唱序约束", "尝试封匣；错误承受 4 理智冲击" }; costs = new[] { 1, 1 }; break;
                    case BossEncounterKind.Surveyor: ids = new[] { "E03-01", "E03-02" }; names = new[] { "开启盐格", "测读盐格" }; descriptions = new[] { "危险时承受 6 生命与 2 理智", "只测读相邻八格危险数" }; costs = new[] { 1, 2 }; break;
                    case BossEncounterKind.FlagBearer: ids = new[] { "E04-01", "E04-02" }; names = new[] { "调校继电器", "释放闸锁" }; descriptions = new[] { "设置一座继电器的公开极性", "条件满足时释放当前闸锁" }; costs = new[] { 1, 1 }; break;
                    default: ids = new[] { "E05-01", "E05-02", "E05-03" }; names = new[] { "建立撤离绳", "接合引索", "收绞归航绳" }; descriptions = new[] { "建立撤离绳并开始召唤", "接合守卫与对应航标", "收绞一次；2 次撤离" }; costs = new[] { 1, 1, 1 }; break;
                }
                for (var i = 0; i < ids.Length; i++) s.hand.Add(new CardView(s.definition.Deck.Count + i + 1,
                    new CardSpec(ids[i], names[i], descriptions[i], costs[i], new[] { new EffectSpec("boss-action", 0) }), isEncounter: true));
            }

            private void BuildTargets()
            {
                targets.Clear();
                if (kind == BossEncounterKind.Choirmaster) foreach (var id in new[] { "choir-a", "choir-b", "choir-c" }) targets.Add(T(id, id == "choir-a" ? "潮" : id == "choir-b" ? "骨" : "息"));
                else if (kind == BossEncounterKind.Surveyor) for (var r = 'A'; r <= 'D'; r++) for (var c = 1; c <= 4; c++) targets.Add(T($"{r}{c}", $"{r}{c}"));
                else if (kind == BossEncounterKind.FlagBearer)
                {
                    foreach (var relay in new[] { "R1", "R2" }) foreach (var value in new[] { "off", "positive", "reverse" }) targets.Add(T($"{relay}:{value}", $"{relay} {(value == "off" ? "断电" : value == "positive" ? "正接" : "反接")}"));
                    targets.Add(T("lock-I", "I 隔离锁")); targets.Add(T("lock-II", "II 导压锁")); targets.Add(T("lock-III", "III 泄压锁"));
                }
                else
                {
                    targets.Add(T("winch", "主绞盘"));
                    for (var i = 0; i < GuardNames.Length; i++) targets.Add(T($"{Slot("b05-guard-" + GuardNames[i])}|anchor-{GuardNames[i]}", $"{GuardLabel(GuardNames[i])}与{AnchorLabel(GuardNames[i])}"));
                }
            }
            private static EncounterTargetView T(string id, string name, string detail = "") => new EncounterTargetView(id, name, detail, true);

            public void BeginPlayerTurn()
            {
                if (!cardsGranted) { AddCards(); cardsGranted = true; }
                primaryUsed = secondaryUsed = false; opensThisTurn = 0; evacuationJustEntered = false;
                if (stagePending) { stage = 2; progress = 0; stagePending = false; }
                if (kind == BossEncounterKind.Choirmaster)
                {
                    known = Math.Max(known, Math.Min(2, s.turn)); if (known >= 2) RemoveCard("E02-01");
                    if (stage == 2) SetPublicIntent(s.enemies[0], DamageChannel.Health, 8, "发声囊撞击");
                    else if (s.turn % 3 == 2) SetPublicIntent(s.enemies[0], DamageChannel.Health, 0, s.turn > 6 && s.enemies[2].Health == 0 ? "召唤潮囊信使" : "召唤骨片侍从", true);
                    else SetPublicIntent(s.enemies[0], s.turn % 3 == 0 ? DamageChannel.Sanity : DamageChannel.Health, s.turn % 3 == 0 ? 4 : 6, s.turn % 3 == 0 ? "失谐合唱" : "骨潮冲击");
                    foreach (var e in s.enemies.Skip(1).Where(x => x.Health > 0)) SetPublicIntent(e, DamageChannel.Health, e.Definition.Id == s.enemies[2].Definition.Id ? 0 : 3, e.Definition.Id == s.enemies[2].Definition.Id ? "恢复发声囊 6" : "骨片切割", e.Definition.Id == s.enemies[2].Definition.Id);
                }
                else if (kind == BossEncounterKind.Surveyor) { SetPublicIntent(s.enemies[0], s.turn % 2 == 0 ? DamageChannel.Sanity : DamageChannel.Health, s.turn % 2 == 0 ? 3 : s.turn >= 7 ? 10 : 6, s.turn % 2 == 0 ? "召唤雾灯蛾群" : s.turn >= 7 ? "盐潮重压" : "盐浪拍击"); foreach (var e in s.enemies.Skip(1).Where(x => x.Health > 0)) SetPublicIntent(e, DamageChannel.Sanity, 2, "雾灯低鸣"); }
                else if (kind == BossEncounterKind.FlagBearer) { if (stage == 3) flagFinalTurn++; SetPublicIntent(s.enemies[0], stage == 1 ? DamageChannel.Health : stage == 2 ? DamageChannel.Sanity : flagFinalTurn % 2 == 1 ? DamageChannel.Health : DamageChannel.Sanity, stage == 1 ? 7 : stage == 2 ? 4 : flagFinalTurn % 2 == 1 ? 12 : 6, stage == 1 ? "旗杆横扫" : stage == 2 ? "无灯号令" : flagFinalTurn % 2 == 1 ? "断桅重击" : "舰队回声"); if (s.enemies[1].Health > 0) SetPublicIntent(s.enemies[1], DamageChannel.Health, 4, "空甲突刺"); }
                else BeginWatcherTurn();
                RefreshCards();
            }

            public CommandResult Play(CardView card, string target)
            {
                RefreshCards(); if (!card.Enabled) return new CommandResult(false, card.DisabledReason);
                var cost = Math.Max(0, card.Definition.Cost - s.sceneDiscount);
                if (s.actions < cost) return new CommandResult(false, "行动点不足。");
                var error = Validate(card.Definition.Id, target); if (error != null) return new CommandResult(false, error);
                s.actions -= cost; s.sceneDiscount = 0; s.Record($"使用 {card.Definition.Name}；支付 {cost} 行动。");
                Apply(card.Definition.Id, target); s.MarkSceneAction(); RefreshCards(); return new CommandResult(true);
            }

            private string Validate(string id, string target)
            {
                if (kind == BossEncounterKind.Choirmaster)
                {
                    if (id == "E02-01") return known >= 2 ? "没有新的顺序约束。" : null;
                    if (stage != 1 || primaryUsed) return "本回合封匣尝试已使用或阶段已完成。";
                    return targets.Any(x => x.Id == target) && !completed.Contains(target) ? null : "请选择未关闭唱诗匣。";
                }
                if (kind == BossEncounterKind.Surveyor)
                {
                    if (!targets.Any(x => x.Id == target) || completed.Contains(target) || id == "E03-02" && read.Contains(target)) return "该盐格不可重复操作。";
                    return id == "E03-01" && opensThisTurn >= 2 ? "本回合已开启两格。" : null;
                }
                if (kind == BossEncounterKind.FlagBearer)
                {
                    if (id == "E04-01") return stagePending || stage >= 3 || primaryUsed || !targets.Any(x => x.Id == target) || !target.Contains(":") || Relay(target.Split(':')[0]) == target.Split(':')[1] ? "继电器调校不可用。" : null;
                    if (stage != 2 || secondaryUsed) return "闸锁当前不可用。";
                    var expected = "lock-" + new[] { "I", "II", "III" }[progress]; if (target != expected) return "必须按公开顺序释放闸锁。";
                    return LockReady(progress) ? null : "公开极性条件未满足。";
                }
                if (id == "E05-01") return stage == 1 && target == "winch" ? null : "撤离绳已经建立或目标无效。";
                if (id == "E05-03") return stage == 3 && !primaryUsed && target == "winch" ? null : "尚未进入撤离或本回合已收绞。";
                if (stage != 2 || string.IsNullOrEmpty(target)) return "当前不能接合引索。";
                var pair = target.Split('|'); if (pair.Length != 2) return "需要选择守卫与航标。";
                var enemy = s.Find(pair[0]); if (enemy == null || enemy.Health == 0 || completed.Contains(pair[0] + ":bound")) return "守卫不在场或已牵引。";
                return BaseId(pair[0]).Replace("b05-guard-", "anchor-") == pair[1] ? null : "守卫与航标不对应；未消耗行动。";
            }

            private void Apply(string id, string target)
            {
                if (id == "E02-01") { known++; s.Record(Constraint(known - 1)); if (known >= 2) RemoveCard(id); return; }
                if (id == "E02-02") { primaryUsed = true; if (target == order[progress]) { completed.Add(target); progress++; s.Record("唱诗匣永久关闭。"); if (progress == 3) { stage = 2; cancelSummon = true; progress = 36 - s.enemies[0].Health; RemoveCard(id); s.Record("护持解除；尚未发生的新召唤取消。"); } } else { s.ApplyThreat(DamageChannel.Sanity, 4, "错误唱序"); } return; }
                if (id == "E03-01") { opensThisTurn++; completed.Add(target); if (B03Mines.Contains(target)) s.ApplyThreat(DamageChannel.Health, s.SceneFailureDamage(6), "危险盐格"); if (B03Mines.Contains(target)) s.ApplyThreat(DamageChannel.Sanity, 2, "危险盐格"); if (B03Goals.Contains(target)) progress++; if (progress == 4) { victory = true; RemoveCard("E03-01"); RemoveCard("E03-02"); } return; }
                if (id == "E03-02") { read.Add(target); s.Record($"{target} 相邻危险数：{AdjacentMines(target)}。"); return; }
                if (id == "E04-01") { primaryUsed = true; var p = target.Split(':'); completed.Remove(p[0] + ":off"); completed.Remove(p[0] + ":positive"); completed.Remove(p[0] + ":reverse"); completed.Add(target); if (stage == 1 && Relay("R1") == "positive" && Relay("R2") == "reverse") { stagePending = true; s.Record("双继电器完成；下一玩家回合进入闸锁阶段。"); } return; }
                if (id == "E04-02") { secondaryUsed = true; progress++; if (progress == 3) { stage = 3; flagFinalTurn = 0; cancelMainThisTurn = true; progress = 48 - s.enemies[0].Health; RemoveCard("E04-01"); RemoveCard("E04-02"); s.Record("第三锁完成；警报与本体理智攻击取消，护持解除。"); } return; }
                if (id == "E05-01") { stage = 2; summonStep = 0; RemoveCard(id); return; }
                if (id == "E05-02") { completed.Add(target.Split('|')[0] + ":bound"); return; }
                primaryUsed = true; progress++; if (progress == 2) { victory = true; RemoveCard(id); }
            }

            public int LimitDamage(string id, int amount, int current)
            {
                if (kind == BossEncounterKind.Surveyor && BaseId(id) == "b03-surveyor" || kind == BossEncounterKind.Watcher && BaseId(id) == "b05-watcher") return 0;
                if ((kind == BossEncounterKind.Choirmaster && stage == 1 || kind == BossEncounterKind.FlagBearer && stage < 3) && id == s.enemies[0].Definition.Id) return Math.Min(amount, Math.Max(0, current - 1));
                return amount;
            }

            public void EnemyDamaged(string id)
            {
                var e = s.Find(id); if (kind == BossEncounterKind.Choirmaster && BaseId(id) == "b02-choirmaster") { if (stage == 2) progress = 36 - e.Health; if (e.Health == 0 && stage == 2) victory = true; }
                else if (kind == BossEncounterKind.FlagBearer && BaseId(id) == "b04-flag-bearer") { if (stage == 3) progress = 48 - e.Health; if (e.Health == 0 && stage == 3) victory = true; }
                else if (kind == BossEncounterKind.Watcher && BaseId(id).StartsWith("b05-guard-") && e.Health == 0)
                {
                    if (completed.Contains(id + ":bound")) { completed.Add(id + ":closed"); progress++; dormantUntil.Remove(id); if (progress == 5) EnterEvacuation(); }
                    else dormantUntil[id] = s.turn + 1;
                }
            }

            public void ResolveEnemyTurn()
            {
                if (victory) return;
                if (kind == BossEncounterKind.Choirmaster) ResolveChoirmaster();
                else if (kind == BossEncounterKind.Surveyor) ResolveSurveyor();
                else if (kind == BossEncounterKind.FlagBearer) ResolveFlagBearer();
                else ResolveWatcher();
            }

            private void ResolveChoirmaster()
            {
                var activeSummons = s.enemies.Skip(1).Where(x => x.Health > 0).ToList();
                var main = s.enemies[0];
                if (main.Health > 0 && !main.Interrupted)
                {
                    if (main.ActualIntent.Name.StartsWith("召唤")) { if (!cancelSummon) SpawnB02(); }
                    else s.ApplyThreat(main.ActualIntent.Channel, main.ActualIntent.Amount, main.Definition.Name);
                }
                cancelSummon = false;
                foreach (var e in activeSummons.Where(x => !x.Interrupted)) { if (e.Definition.Id == s.enemies[2].Definition.Id) { main.Health = Math.Min(main.Definition.Health, main.Health + 6); s.Record("潮囊信使令发声囊恢复 6。"); } else s.ApplyThreat(DamageChannel.Health, 3, e.Definition.Name); }
            }
            private void SpawnB02()
            {
                if (s.enemies.Skip(1).Count(x => x.Health > 0) >= 2) return;
                var chosen = s.turn > 6 && s.enemies[2].Health == 0 ? s.enemies[2] : s.enemies[1];
                if (chosen.Health == 0) { chosen.Health = chosen.Definition.Health; RevealSummonSource(chosen); }
            }
            private void ResolveSurveyor()
            {
                var activeMoths = s.enemies.Skip(1).Where(x => x.Health > 0).ToList();
                var main = s.enemies[0];
                if (!main.Interrupted)
                {
                    if (s.turn % 2 == 1) s.ApplyThreat(DamageChannel.Health, main.ActualIntent.Amount, main.Definition.Name);
                    else { s.ApplyThreat(DamageChannel.Sanity, main.ActualIntent.Amount, main.Definition.Name); var slot = s.enemies.Skip(1).FirstOrDefault(x => x.Health == 0); if (slot != null) { slot.Health = slot.Definition.Health; RevealSummonSource(slot); } }
                }
                foreach (var e in activeMoths.Where(x => !x.Interrupted)) s.ApplyThreat(DamageChannel.Sanity, e.ActualIntent.Amount, e.Definition.Name);
            }
            private void ResolveFlagBearer()
            {
                var soldierWasActive = s.enemies[1].Health > 0;
                if (!cancelMainThisTurn && !s.enemies[0].Interrupted)
                    s.ApplyThreat(s.enemies[0].ActualIntent.Channel, s.enemies[0].ActualIntent.Amount, s.enemies[0].Definition.Name);
                cancelMainThisTurn = false;
                if (s.turn == 2 && s.enemies[1].Health == 0 && !cancelSummon) { s.enemies[1].Health = s.enemies[1].Definition.Health; RevealSummonSource(s.enemies[1]); }
                cancelSummon = false;
                if (soldierWasActive && s.enemies[1].Health > 0 && !s.enemies[1].Interrupted) s.ApplyThreat(DamageChannel.Health, s.enemies[1].ActualIntent.Amount, s.enemies[1].Definition.Name);
                if (stage == 2 && --alarm == 0) { s.ApplyThreat(DamageChannel.Health, 12, "闸廊警报"); alarm = 3; }
            }

            private void BeginWatcherTurn()
            {
                if (stage == 1) return;
                foreach (var e in s.enemies.Skip(1).Where(x => x.Health > 0))
                {
                    ages[e.Definition.Id] = ages.TryGetValue(e.Definition.Id, out var age) ? age + 1 : 1;
                    var name = BaseId(e.Definition.Id).Substring("b05-guard-".Length);
                    var amount = name == "anchor" ? 5 : name == "lamp" ? 3 : name == "bell" ? (ages[e.Definition.Id] % 2 == 1 ? 6 : 0) : name == "sail" ? (!sailBurst ? 6 : 4) : !eyeBurst ? 4 : 2;
                    SetPublicIntent(e, name == "lamp" || name == "bell" || name == "eye" ? DamageChannel.Sanity : DamageChannel.Health, amount, amount == 0 ? "本回合停手" : name == "anchor" ? "铁锚撞击" : name == "lamp" ? "裂灯低鸣" : name == "bell" ? "铜钟震响" : name == "sail" ? "折帆撕扯" : "闭眼凝视");
                }
            }
            private void ResolveWatcher()
            {
                if (stage == 1) return;
                if (stage == 3) { if (!evacuationJustEntered) s.ApplyThreat(DamageChannel.Sanity, 4, "无星余响"); return; }
                var activeAtStart = s.enemies.Skip(1).Count(x => x.Health > 0);
                foreach (var e in s.enemies.Skip(1).Where(x => x.Health > 0 && !x.Interrupted).ToList())
                {
                    var name = BaseId(e.Definition.Id).Substring("b05-guard-".Length);
                    sailBurst |= name == "sail"; eyeBurst |= name == "eye";
                    if (e.ActualIntent.Amount > 0) s.ApplyThreat(e.ActualIntent.Channel, e.ActualIntent.Amount, e.Definition.Name);
                }
                summonStep++; if (summonStep % 2 == 0) s.ApplyThreat(DamageChannel.Sanity, activeAtStart == 3 ? 6 : 3, "无星余响");
                SpawnWatcher(activeAtStart);
            }
            private void SpawnWatcher(int activeBefore)
            {
                var cap = 3; var active = s.enemies.Skip(1).Count(x => x.Health > 0);
                foreach (var due in dormantUntil.Where(x => x.Value <= s.turn).OrderBy(x => x.Value).ThenBy(x => Array.IndexOf(guardOrder, BaseId(x.Key).Substring("b05-guard-".Length))).ToList())
                { if (active >= cap) break; var e = s.Find(due.Key); e.Health = Math.Min(8, e.Definition.Health); RevealSummonSource(e); dormantUntil.Remove(due.Key); active++; }
                if (!initialSpawned && summonStep == 1)
                {
                    foreach (var name in guardOrder.Take(2)) { var e = s.Find(Slot("b05-guard-" + name)); e.Health = e.Definition.Health; RevealSummonSource(e); ages[e.Definition.Id] = 0; }
                    initialSpawned = true; return;
                }
                if (summonStep < 3 || summonStep % 2 == 0 || active >= cap) return;
                foreach (var name in guardOrder)
                {
                    var e = s.Find(Slot("b05-guard-" + name));
                    if (e.Health == 0 && !dormantUntil.ContainsKey(e.Definition.Id) && !completed.Contains(e.Definition.Id + ":closed"))
                    { e.Health = e.Definition.Health; RevealSummonSource(e); ages[e.Definition.Id] = 0; break; }
                }
            }
            private void EnterEvacuation()
            {
                stage = 3; progress = 0; evacuationJustEntered = true; RemoveCard("E05-02"); foreach (var e in s.enemies.Skip(1)) e.Health = 0; dormantUntil.Clear(); s.Record("五座锚点关闭；所有旧威胁取消，开始撤离。");
            }

            private string Relay(string relay) => completed.FirstOrDefault(x => x.StartsWith(relay + ":", StringComparison.Ordinal))?.Split(':')[1] ?? "off";
            private static string BaseId(string id) { var suffix = id.LastIndexOf(':'); return suffix < 0 ? id : id.Substring(0, suffix); }
            private string Slot(string baseId) => s.enemies.First(x => BaseId(x.Definition.Id) == baseId).Definition.Id;
            private bool LockReady(int index) => index == 0 ? Relay("R1") == "off" && Relay("R2") == "reverse" : index == 1 ? Relay("R1") == "positive" && Relay("R2") == "reverse" : Relay("R1") == "positive" && Relay("R2") == "off";
            private int AdjacentMines(string id) { char r = id[0], c = id[1]; return B03Mines.Count(m => m != id && Math.Abs(m[0] - r) <= 1 && Math.Abs(m[1] - c) <= 1); }
            private static void SetPublicIntent(EnemyState e, DamageChannel channel, int amount, string name, bool interruptible = false) { e.ActualIntent = new IntentSpec("boss", name, name, channel, amount, true, true, interruptible: interruptible); e.Confirmed = true; e.Interrupted = false; }
            private string Constraint(int index) => $"{ChoirLabel(order[index])}在{ChoirLabel(order[index + 1])}前";
            private static string ChoirLabel(string id) => id == "choir-a" ? "潮匣" : id == "choir-b" ? "骨匣" : "息匣";
            private void RevealSummonSource(EnemyState enemy)
            {
                completed.Add(enemy.Definition.Id + ":source");
                s.knowledge.Record(new Evidence(enemy.Definition.Id + "/source", "召唤来源已公开", "场景召唤", KnowledgeLevel.Observed));
            }
            private void RemoveCard(string id) => s.hand.RemoveAll(x => x.IsEncounter && x.Definition.Id == id);

            private void RefreshCards()
            {
                foreach (var card in s.hand.Where(x => x.IsEncounter))
                {
                    var reason = ValidateAvailability(card.Definition.Id); card.Enabled = reason == null; card.DisabledReason = reason ?? "";
                    card.Targets = card.Definition.Id == "E02-01" ? EmptyTargets : targets
                        .Where(x => card.Definition.Id == "E04-01" ? x.Id.Contains(":") : card.Definition.Id == "E04-02" ? x.Id.StartsWith("lock-", StringComparison.Ordinal) : card.Definition.Id == "E05-02" ? x.Id.Contains("|") : card.Definition.Id == "E05-01" || card.Definition.Id == "E05-03" ? x.Id == "winch" : true)
                        .Select(x => new EncounterTargetView(x.Id, x.Name, TargetDetail(x.Id), TargetEnabled(card.Definition.Id, x.Id))).ToList().AsReadOnly();
                }
            }
            private string ValidateAvailability(string id)
            {
                if (id == "E02-01" && known >= 2 || id == "E02-02" && (stage != 1 || primaryUsed)) return "下回合或职责已完成";
                if (id == "E03-01" && opensThisTurn >= 2) return "本回合已开启两格";
                if (id == "E04-01" && (stagePending || stage >= 3 || primaryUsed) || id == "E04-02" && (stage != 2 || secondaryUsed)) return "当前阶段或本回合不可用";
                if (id == "E05-01" && stage != 1 || id == "E05-02" && stage != 2 || id == "E05-03" && (stage != 3 || primaryUsed)) return "当前阶段不可用";
                return null;
            }
            private bool TargetEnabled(string card, string id)
            {
                if (card == "E02-02") return !completed.Contains(id) && stage == 1;
                if (card == "E03-01") return !completed.Contains(id);
                if (card == "E03-02") return !completed.Contains(id) && !read.Contains(id);
                if (card == "E04-01") return id.Contains(":") && Relay(id.Split(':')[0]) != id.Split(':')[1];
                if (card == "E04-02") return id == "lock-" + new[] { "I", "II", "III" }[Math.Min(progress, 2)] && LockReady(Math.Min(progress, 2));
                if (card == "E05-02") return id.Contains("|") && s.IsLiving(id.Split('|')[0]) && !completed.Contains(id.Split('|')[0] + ":bound");
                return id == "winch";
            }
            private string TargetDetail(string id)
            {
                if (kind == BossEncounterKind.Surveyor) return (completed.Contains(id) ? (B03Mines.Contains(id) ? "危险已停用" : AdjacentMines(id).ToString()) : read.Contains(id) ? $"测 {AdjacentMines(id)}" : "?") + (B03Goals.Contains(id) ? "\n封栓" : "");
                if (kind == BossEncounterKind.Choirmaster) return completed.Contains(id) ? "已关闭" : "未关闭";
                if (kind == BossEncounterKind.FlagBearer) return id.Contains(":") ? (Relay(id.Split(':')[0]) == id.Split(':')[1] ? "当前极性" : "可调校") : id == "lock-I" ? "条件：R1 断电，R2 反接" : id == "lock-II" ? "条件：I 已开，R1 正接，R2 反接" : "条件：II 已开，R1 正接，R2 断电";
                if (kind == BossEncounterKind.Watcher && id.Contains("|")) { var name = BaseId(id.Split('|')[0]).Replace("b05-guard-", ""); return $"{GuardLabel(name)}公开对应{AnchorLabel(name)}"; }
                return "";
            }
            private static string GuardLabel(string name) => name == "anchor" ? "锚守卫" : name == "lamp" ? "灯守卫" : name == "bell" ? "钟守卫" : name == "sail" ? "帆守卫" : "眼守卫";
            private static string AnchorLabel(string name) => name == "anchor" ? "铁锚" : name == "lamp" ? "裂灯" : name == "bell" ? "铜钟" : name == "sail" ? "折帆" : "闭眼";
            private string BuildStatus()
            {
                if (kind == BossEncounterKind.Choirmaster) return stage == 1 ? $"{(known == 0 ? "唱序未知" : string.Join("；", Enumerable.Range(0, known).Select(Constraint)))} · 封匣 {progress}/3" : $"护持解除 · 发声囊 {s.enemies[0].Health}/36";
                if (kind == BossEncounterKind.Surveyor) return $"封栓 {progress}/4 · 总危险 3" + (s.turn == 6 ? "\n下回合盐潮重压：10 生命" : "");
                if (kind == BossEncounterKind.FlagBearer)
                {
                    string Polarity(string relay) => Relay(relay) == "off" ? "断电" : Relay(relay) == "positive" ? "正接" : "反接";
                    var relays = $"R1 {Polarity("R1")} · R2 {Polarity("R2")}";
                    return stage == 1 ? relays : stage == 2 ? $"{relays}\n闸锁 {progress}/3 · 警报 {alarm}\n{TargetDetail("lock-" + new[] { "I", "II", "III" }[Math.Min(progress, 2)])}" : $"主桅暴露 · {s.enemies[0].Health}/48";
                }
                return stage == 3 ? $"撤离 {progress}/2" : (stage == 1 ? "撤离绳未建立" : $"锚点 {progress}/5 · 活动守卫 {s.enemies.Skip(1).Count(x => x.Health > 0)}/3") + "\n" + string.Join(" → ", guardOrder.Select(GuardLabel));
            }

            public IEnumerable<EncounterTargetView> CardTargets(int card)
            {
                if (card == 43) return kind == BossEncounterKind.Surveyor ? targets.Where(x => !completed.Contains(x.Id) && !read.Contains(x.Id)).Select(x => T("scene:" + x.Id, x.Name, "未测读")) : EmptyTargets;
                if (card == 44 || card == 67) return kind == BossEncounterKind.Surveyor && (card != 44 || opensThisTurn < 2) ? ProvenSafeCells().Select(id => T("scene:" + id, id, "由公开约束证明安全")) : EmptyTargets;
                if (card == 46) return s.enemies.Skip(1).Where(x => x.Health > 0).Select(x => T("enemy:" + x.Definition.Id, x.Definition.Name, completed.Contains(x.Definition.Id + ":source") ? "来源已公开：抽 1" : "验证召唤来源"));
                if (card == 42 || card == 52 || card == 66) return GuardAnchorOptions();
                if (card == 82) return GuardAnchorOptions().Where(x => !s.knowledge.Entries.Any(e => e.Key == SceneId(x.Id).Split('|')[0] + "/anchor" && e.Level == KnowledgeLevel.Verified));
                if (card == 56) return kind == BossEncounterKind.Choirmaster && stage == 1 && known >= 2 && !primaryUsed && progress < order.Length
                    ? new[] { T("scene:" + order[progress], ChoirLabel(order[progress]), "当前顺位已验证") } : EmptyTargets;
                if (card == 57 || card == 60) return kind == BossEncounterKind.FlagBearer && stage == 2 ? new[] { T("scene:alarm", "闸廊警报", $"剩余 {alarm}") } : EmptyTargets;
                if (card == 59) return EmptyTargets;
                if (card == 61) return s.enemies.Where(x => x.Health > 0 && x.Confirmed && IsSummonAction(x) && x.ActualIntent.Interruptible).Select(x => T("enemy:" + x.Definition.Id, x.Definition.Name));
                if (card == 78) { var ordinary = s.enemies.Where(x => x.Health > 0 && x.Confirmed && IsSummonAction(x)).Select(x => T("enemy:" + x.Definition.Id, x.Definition.Name)); return ScheduledSoldierAvailable ? ordinary.Concat(new[] { T("scene:scheduled-soldier", "空甲执旗兵召唤") }) : ordinary; }
                if (card == 62) return s.enemies.Skip(1).Where(IsBindableAttack).Select(x => T("enemy:" + x.Definition.Id, x.Definition.Name));
                if (card == 84) return s.enemies.Skip(1).Where(x => IsBindableAttack(x) && x.ActualIntent.Channel == DamageChannel.Sanity).Select(x => T("enemy:" + x.Definition.Id, x.Definition.Name));
                if (card == 63) return RelayCorrectionTargets();
                if (card == 65) return kind == BossEncounterKind.FlagBearer && stage == 2 && progress == 1 && s.health > 4 ? new[] { T("scene:lock-II", "强开 II 导压锁") } : EmptyTargets;
                if (card == 68) return kind == BossEncounterKind.Choirmaster && stage == 1 && known >= 2 && !primaryUsed ? new[] { T("scene:" + order[progress], ChoirLabel(order[progress]), "当前顺位已确定") } : EmptyTargets;
                if (card == 69) return kind == BossEncounterKind.Watcher && stage == 3 && progress < 2 ? new[] { T("scene:winch", "主绞盘") } : EmptyTargets;
                if (card == 86) return kind == BossEncounterKind.FlagBearer && stage == 2 && !secondaryUsed && LockReady(progress) ? new[] { T("scene:lock", "当前闸锁") } : EmptyTargets;
                if (card == 87) return EmptyTargets;
                return EmptyTargets;
            }
            public IEnumerable<EncounterTargetView> GuardAnchorOptions()
            {
                if (kind != BossEncounterKind.Watcher || stage != 2) return EmptyTargets;
                return targets.Where(x => x.Id.Contains("|") && s.IsLiving(x.Id.Split('|')[0]) && !completed.Contains(x.Id.Split('|')[0] + ":bound")).Select(x => T("scene:" + x.Id, x.Name, TargetDetail(x.Id)));
            }
            public bool HasCardMechanic(int card)
            {
                if (card == 43 || card == 44 || card == 67) return kind == BossEncounterKind.Surveyor;
                if (card == 42 || card == 52 || card == 66 || card == 69) return kind == BossEncounterKind.Watcher;
                if (card == 56 || card == 68) return kind == BossEncounterKind.Choirmaster;
                if (card == 57 || card == 60 || card == 63 || card == 65 || card == 86) return kind == BossEncounterKind.FlagBearer;
                if (card == 59) return false;
                return true;
            }

            private bool ScheduledSoldierAvailable => kind == BossEncounterKind.FlagBearer && s.turn == 2 && s.enemies[1].Health == 0 && !cancelSummon;
            private static bool IsSummonAction(EnemyState enemy) => enemy.ActualIntent != null && enemy.ActualIntent.Name.StartsWith("召唤", StringComparison.Ordinal);
            private bool IsBindableAttack(EnemyState enemy) => enemy.Health > 0 && enemy.ActualIntent != null && enemy.ActualIntent.Amount > 0;
            private IEnumerable<EncounterTargetView> RelayCorrectionTargets()
            {
                if (kind != BossEncounterKind.FlagBearer || primaryUsed || stage > 2 || stagePending) return EmptyTargets;
                var desiredR1 = stage == 1 || progress == 1 ? "positive" : progress == 0 ? "off" : "positive";
                var desiredR2 = stage == 1 || progress < 2 ? "reverse" : "off";
                var result = new List<EncounterTargetView>();
                if (Relay("R1") != desiredR1) result.Add(T("scene:R1:" + desiredR1, "校正 R1"));
                if (Relay("R2") != desiredR2) result.Add(T("scene:R2:" + desiredR2, "校正 R2"));
                return result;
            }
            private static string SceneId(string value) => value != null && value.StartsWith("scene:", StringComparison.Ordinal) ? value.Substring(6) : value;
            public bool ApplyCardAction(int card, string selection)
            {
                var id = SceneId(selection);
                if (card == 57 || card == 60) { if (kind != BossEncounterKind.FlagBearer || stage != 2) return false; alarm++; return true; }
                if (card == 59) return false;
                if (card == 61 || card == 78) return CancelSummon(selection, card == 61);
                if (card == 62) return BindThreat(id, null);
                if (card == 63)
                {
                    if (!RelayCorrectionTargets().Any(x => SceneId(x.Id) == id)) return false;
                    Apply("E04-01", id); s.MarkSceneAction(); return true;
                }
                if (card == 65) { if (kind != BossEncounterKind.FlagBearer || stage != 2 || progress != 1 || s.health <= 4) return false; s.PayHealth(4); progress++; s.MarkSceneAction(); return true; }
                if (card == 66) { if (!VerifyGuardAnchor(selection)) return false; completed.Add(SceneId(selection).Split('|')[0] + ":bound"); s.AddBlock(4); return true; }
                if (card == 67) return OpenProvenSafe(selection, false);
                if (card == 68) { if (kind != BossEncounterKind.Choirmaster || stage != 1 || known < 2 || primaryUsed || id != order[progress]) return false; Apply("E02-02", id); s.AddBlock(6); s.MarkSceneAction(); return true; }
                if (card == 69) { if (kind != BossEncounterKind.Watcher || stage != 3 || progress >= 2) return false; if (++progress >= 2) victory = true; s.MarkSceneAction(); return true; }
                if (card == 86) { if (kind != BossEncounterKind.FlagBearer || stage != 2 || secondaryUsed || !LockReady(progress)) return false; Apply("E04-02", "lock-" + new[] { "I", "II", "III" }[progress]); s.MarkSceneAction(); return true; }
                if (card == 87) return false;
                if (card == 56 && kind == BossEncounterKind.Choirmaster && stage == 1 && known >= 2 && !primaryUsed && id == order[progress]) { Apply("E02-02", id); s.MarkSceneAction(); return true; }
                return false;
            }
            public bool InspectConnection(string selection) { var id = SceneId(selection); if (kind != BossEncounterKind.Watcher || string.IsNullOrEmpty(id)) return false; s.knowledge.Record(new Evidence("scene/connection/" + id, "该物件存在可调查连接", "查验潮痕", KnowledgeLevel.Observed)); return true; }
            public bool MeasureCell(string selection) { var id = SceneId(selection); if (kind != BossEncounterKind.Surveyor || !targets.Any(x => x.Id == id) || completed.Contains(id) || read.Contains(id)) return false; read.Add(id); s.Record($"{id} 相邻危险数：{AdjacentMines(id)}。"); return true; }
            private IEnumerable<string> ProvenSafeCells()
            {
                if (kind != BossEncounterKind.Surveyor) yield break;
                var possible = new List<HashSet<string>>();
                foreach (var mines in Choose(targets.Select(x => x.Id).ToList(), 3, 0, new List<string>()))
                {
                    var set = new HashSet<string>(mines);
                    if (completed.Any(x => B03Mines.Contains(x) != set.Contains(x)) || completed.Where(x => !B03Mines.Contains(x)).Any(x => AdjacentCount(set, x) != AdjacentMines(x)) || read.Any(x => AdjacentCount(set, x) != AdjacentMines(x))) continue;
                    possible.Add(set);
                }
                foreach (var id in targets.Select(x => x.Id).Where(x => !completed.Contains(x))) if (possible.Count > 0 && possible.All(x => !x.Contains(id))) yield return id;
            }
            private static IEnumerable<List<string>> Choose(List<string> values, int left, int index, List<string> picked)
            {
                if (left == 0) { yield return new List<string>(picked); yield break; }
                for (var i = index; i <= values.Count - left; i++) { picked.Add(values[i]); foreach (var x in Choose(values, left - 1, i + 1, picked)) yield return x; picked.RemoveAt(picked.Count - 1); }
            }
            private static int AdjacentCount(HashSet<string> mines, string id) => mines.Count(m => m != id && Math.Abs(m[0] - id[0]) <= 1 && Math.Abs(m[1] - id[1]) <= 1);
            public bool OpenProvenSafe(string selection, bool countsQuota)
            {
                var id = SceneId(selection); if (kind != BossEncounterKind.Surveyor || !ProvenSafeCells().Contains(id) || countsQuota && opensThisTurn >= 2) return false;
                if (countsQuota) opensThisTurn++; completed.Add(id); if (B03Goals.Contains(id)) progress++; if (progress == 4) victory = true; s.MarkSceneAction(); return true;
            }
            public bool VerifySummonSource(string id) { var e = s.Find(id); if (e == null || e.Health == 0 || s.enemies.IndexOf(e) == 0) return false; var key = id + ":source"; var fresh = completed.Add(key); s.knowledge.Record(new Evidence(id + "/source", "召唤来源已验证", "追踪召唤", KnowledgeLevel.Verified)); return fresh; }
            public bool VerifyGuardAnchor(string selection) { var id = SceneId(selection); if (kind != BossEncounterKind.Watcher || string.IsNullOrEmpty(id)) return false; var p = id.Split('|'); if (p.Length != 2 || BaseId(p[0]).Replace("b05-guard-", "anchor-") != p[1]) return false; s.knowledge.Record(new Evidence(p[0] + "/anchor", "守卫与航标对应已验证", "核验印记", KnowledgeLevel.Verified)); return true; }
            public bool IsVerifiedSource(string id) => completed.Contains(id + ":source") || completed.Contains(id + ":bound") || s.knowledge.Entries.Any(e => e.Key == id + "/anchor" && e.Level == KnowledgeLevel.Verified);
            public bool IsBoundGuard(string id) => completed.Contains(id + ":bound");
            public bool IsExposed(string id) => (kind == BossEncounterKind.Choirmaster && stage == 2 || kind == BossEncounterKind.FlagBearer && stage == 3) && id == s.enemies[0].Definition.Id;
            public bool CancelSummon(string id, bool ordinaryInterruptOnly)
            {
                var scene = SceneId(id);
                if (scene == "scheduled-soldier")
                {
                    if (ordinaryInterruptOnly || !ScheduledSoldierAvailable) return false;
                    cancelSummon = true; return true;
                }
                if (id != null && id.StartsWith("enemy:", StringComparison.Ordinal)) id = id.Substring(6);
                var e = s.Find(id);
                if (e == null || e.Health == 0 || !e.Confirmed || !IsSummonAction(e) || ordinaryInterruptOnly && !e.ActualIntent.Interruptible) return false;
                e.Interrupted = true; if (e == s.enemies[0]) cancelSummon = true; return true;
            }
            public bool BindThreat(string id, DamageChannel? channel) { if (id != null && id.StartsWith("enemy:", StringComparison.Ordinal)) id = id.Substring(6); var e = s.Find(id); if (e == null || s.enemies.IndexOf(e) == 0 || !IsBindableAttack(e) || channel.HasValue && e.ActualIntent.Channel != channel.Value) return false; e.Interrupted = true; return true; }
            public void ReduceThreat(string id, DamageChannel channel, int amount) { var e = s.Find(id); if (e?.ActualIntent == null || e.ActualIntent.Channel != channel) return; var interrupted = e.Interrupted; SetPublicIntent(e, channel, Math.Max(0, e.ActualIntent.Amount - amount), e.ActualIntent.Name, e.ActualIntent.Interruptible); e.Interrupted = interrupted; }
            public string[] ThreatCategories(string id)
            {
                var e = s.Find(id); if (e == null) return Array.Empty<string>();
                var result = new List<string>();
                if (e.ActualIntent != null)
                {
                    if (e.ActualIntent.Amount > 0) result.Add(e.ActualIntent.Channel == DamageChannel.Health ? "生命伤害" : "理智冲击");
                    if (IsSummonAction(e) || kind == BossEncounterKind.Surveyor && e == s.enemies[0] && s.turn % 2 == 0) result.Add("召唤");
                }
                if (kind == BossEncounterKind.FlagBearer && e == s.enemies[0] && ScheduledSoldierAvailable) result.Add("召唤");
                if (kind == BossEncounterKind.Watcher && e == s.enemies[0] && stage == 2)
                {
                    var intent = WatcherIntent();
                    if (intent.Contains("生命")) result.Add("生命伤害");
                    if (intent.Contains("理智")) result.Add("理智冲击");
                    if (intent.Contains("召唤") || intent.Contains("复现")) result.Add("召唤");
                }
                return result.Distinct().ToArray();
            }

            public bool IsTargetable(string id)
            {
                var e = s.Find(id);
                return e != null && e.Health > 0 && !(kind == BossEncounterKind.Surveyor && BaseId(id) == "b03-surveyor") && !(kind == BossEncounterKind.Watcher && BaseId(id) == "b05-watcher");
            }

            private string WatcherIntent()
            {
                if (stage == 1) return "静默：等待撤离绳";
                if (stage == 3) return evacuationJustEntered ? "无星余响：本回合停息" : "无星余响：4 点理智冲击";
                var next = summonStep + 1;
                var lines = new List<string>();
                if (next % 2 == 0) lines.Add(s.enemies.Skip(1).Count(x => x.Health > 0) == 3 ? "无星余响：3–6 理智，清至两名则 3" : "无星余响：3 点理智冲击");
                if (!initialSpawned) lines.Add("召唤：" + string.Join("、", guardOrder.Take(2).Select(GuardLabel)));
                else if (next >= 3 && next % 2 == 1)
                {
                    var name = guardOrder.FirstOrDefault(n => { var e = s.Find(Slot("b05-guard-" + n)); return e.Health == 0 && !dormantUntil.ContainsKey(e.Definition.Id) && !completed.Contains(e.Definition.Id + ":closed"); });
                    if (name != null) lines.Add("召唤：" + GuardLabel(name) + "（需空位）");
                }
                foreach (var id in dormantUntil.Where(x => x.Value <= s.turn).OrderBy(x => x.Value).Select(x => x.Key)) lines.Add("复现：" + s.Find(id).Definition.Name + "（需空位）");
                return lines.Count == 0 ? "静默：本回合无本体攻击" : string.Join("\n", lines);
            }

            public EnemyView EnemyView(EnemyState e)
            {
                var dormant = e.Health == 0 && !completed.Contains(e.Definition.Id + ":closed") && !victory;
                var intent = e.Health == 0 ? dormant ? "未入场／休眠" : "已永久解除" : e.Interrupted ? "已打断：本回合不行动" : e.ActualIntent == null ? "公开规则行动" : e.ActualIntent.Amount == 0 ? e.ActualIntent.Name : Describe(e.ActualIntent);
                if (kind == BossEncounterKind.Watcher && e == s.enemies[0]) intent = WatcherIntent();
                return new EnemyView(e.Definition.Id, e.Definition.Name, e.Health, e.Definition.Health, "", intent, e.Health > 0, IsTargetable(e.Definition.Id));
            }
        }
    }
}
