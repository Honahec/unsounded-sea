using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public sealed partial class BattleSession
    {
        private static readonly Dictionary<string, int> LegacyCardNumbers = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["revolver"] = 1, ["contradiction"] = 2, ["cover"] = 19, ["steady"] = 20,
            ["intuition"] = 21, ["bandage"] = 22, ["notes"] = 37, ["reload"] = 38,
            ["light"] = 39, ["tear"] = 55
        };
        private List<CardView> pendingReorder;

        private static int CardNumber(CardSpec card)
        {
            if (LegacyCardNumbers.TryGetValue(card.Id, out var legacy)) return legacy;
            var id = card.Id;
            if (id.Length == 4 && (id[0] == 'c' || id[0] == 'C') && int.TryParse(id.Substring(1), out var value) && value >= 1 && value <= 90) return value;
            if (card.Effects.Count == 1)
            {
                id = card.Effects[0].Id;
                if (id.Length == 4 && id[0] == 'c' && int.TryParse(id.Substring(1), out value) && value >= 1 && value <= 90) return value;
            }
            return 0;
        }

        private IEnumerable<EnemyState> LivingSubjects => enemies.Where(x => x.Health > 0);
        private static bool IsCatalogCard(CardSpec card) => CardNumber(card) != 0;
        private static bool IsArmed(CardSpec card) => CardNumber(card) >= 1 && CardNumber(card) <= 18;
        private static bool IsInvestigation(CardSpec card) => CardNumber(card) >= 37 && CardNumber(card) <= 54;
        private static bool IsForbidden(CardSpec card) => CardNumber(card) >= 73;
        private IEnumerable<EnemyState> LivingTargets => enemies.Where(x => x.Health > 0 && IsTargetable(x.Definition.Id));
        private static EncounterTargetView Option(string id, string name, string detail = "") => new EncounterTargetView(id, name, detail, true);

        private void RefreshCardViews()
        {
            foreach (var card in hand.Where(x => !x.IsEncounter))
            {
                card.ActionCost = card.Definition.Cost;
                var targets = BuildCardTargets(card).ToList();
                card.Targets = targets.AsReadOnly();
                var reason = CardAvailability(card, targets);
                card.Enabled = reason == null;
                card.DisabledReason = reason ?? "";
            }
            foreach (var card in hand.Where(x => x.IsEncounter && x.Definition.Id == SealCardId))
            {
                var cost = Math.Max(0, objective.ActionCost - sceneDiscount);
                card.ActionCost = cost;
                card.Targets = EmptyTargets;
                card.Enabled = phase == BattlePhase.PlayerTurn && objective.AllowsTaskAction && !taskUsed && actions >= cost;
                card.DisabledReason = card.Enabled ? "" : "任务行动不可用（每回合一次，行动不足）。";
            }
        }

        private IEnumerable<EncounterTargetView> BuildCardTargets(CardView card)
        {
            var n = CardNumber(card.Definition);
            if (n == 0 && card.Definition.Effects.Any(x => effects.Get(x.Id).RequiresEnemy))
                return LivingTargets.Select(x => Option(x.Definition.Id, x.Definition.Name, x.Definition.Candidates));
            IEnumerable<EnemyState> enemyChoices = LivingTargets;
            var subjectChoices = LivingSubjects;
            if (n == 90) enemyChoices = enemyChoices.Where(x => IsSummon(x) && IsVerifiedSource(x));
            if (n == 39) return subjectChoices.Where(x => x.ActualIntent != null && (x.Confirmed || x.ActualIntent.VisibleAction)).Select(x => Option("enemy:" + x.Definition.Id, x.Definition.Name, x.Confirmed ? "已确认：抽 1" : "可观察"));
            if (n == 73) return subjectChoices.Select(x => Option("enemy:" + x.Definition.Id, x.Definition.Name, EnemyChoiceDetail(n, x)));
            if (new[] { 23, 37, 41, 50, 75 }.Contains(n))
                return subjectChoices.Where(x => x.ActualIntent != null && (n != 75 || !x.Confirmed)).Select(x => Option("enemy:" + x.Definition.Id, x.Definition.Name, EnemyChoiceDetail(n, x)));
            if (new[] { 1, 2, 3, 4, 5, 7, 8, 10, 11, 12, 14, 15, 16, 17, 18, 55, 76, 90 }.Contains(n))
                return enemyChoices.Select(x => Option("enemy:" + x.Definition.Id, x.Definition.Name, EnemyChoiceDetail(n, x)));
            if (n == 9)
            {
                var e = enemyChoices.ToList();
                var result = e.Select(x => Option("enemies:" + x.Definition.Id, x.Definition.Name));
                return result.Concat(from a in e from b in e where string.CompareOrdinal(a.Definition.Id, b.Definition.Id) < 0 select Option("enemies:" + a.Definition.Id + "," + b.Definition.Id, a.Definition.Name + "、" + b.Definition.Name));
            }
            if (new[] { 28, 45, 71 }.Contains(n)) return hand.Where(x => x.InstanceId != card.InstanceId && (n != 28 || IsInvestigation(x.Definition))).Select(HandOption);
            if (n == 51) return HandSelections(card.InstanceId, 2);
            if (n == 38) return discard.Where(x => IsArmed(x.Definition)).Select(DiscardOption);
            if (n == 47) return discard.Where(x => IsInvestigation(x.Definition) && !x.Definition.Exhaust).Select(DiscardOption);
            if (n == 83) return discard.Where(x => !IsForbidden(x.Definition)).Select(DiscardOption);
            if (n == 54) return Enumerable.Empty<EncounterTargetView>();
            if (n == 40) return definition.Archive.Where(x => x.Level == KnowledgeLevel.Verified && !knowledge.Entries.Any(e => e.Key == x.Key && e.Level == KnowledgeLevel.Verified)).Select(x => Option("evidence:" + x.Key, x.Text, x.Source));
            if (n == 48) return EvidencePairs();
            if (n == 53) return EnemySelections(subjectChoices.Where(x => x.ActualIntent?.VisibleAction == true && !x.Confirmed).ToList(), 2);
            if (n == 49) return FocusOptions();
            if (n == 56)
            {
                var scene = boss?.CardTargets(n) ?? Enumerable.Empty<EncounterTargetView>();
                return scene.Concat(LivingTargets.Select(x => Option("enemy:" + x.Definition.Id, x.Definition.Name, "无适用媒介时造成 7 伤害")));
            }
            if (n == 87 && boss == null && taskUsed) return new[] { Option("scene:task", "再次开放任务行动", "不重开一次性奖励") };
            if ((n == 58 || n == 72) && boss == null && objective.AllowsTaskAction) return new[] { Option("scene:objective", objective.Description, "普通任务进度") };
            if (n == 86 && boss == null && objective.AllowsTaskAction && knowledge.Entries.Any(x => x.Level == KnowledgeLevel.Verified)) return new[] { Option("scene:objective", objective.Description, "证据条件已满足") };
            if (n == 52) return boss?.GuardAnchorOptions() ?? Enumerable.Empty<EncounterTargetView>();
            if (new[] { 42, 43, 44, 46, 57, 58, 59, 60, 61, 62, 63, 65, 66, 67, 68, 69, 72, 78, 82, 84, 86, 87 }.Contains(n))
                return boss?.CardTargets(n) ?? Enumerable.Empty<EncounterTargetView>();
            if (n == 70) return LivingTargets.Where(x => x.Confirmed && x.ActualIntent?.Interruptible == true).Select(x => Option("enemy:" + x.Definition.Id, x.Definition.Name, "已确认且可打断"));
            return Enumerable.Empty<EncounterTargetView>();
        }

        private EncounterTargetView HandOption(CardView x) => Option("hand:" + x.InstanceId, x.Definition.Name, x.Definition.Description);
        private EncounterTargetView DiscardOption(CardView x) => Option("discard:" + x.InstanceId, x.Definition.Name, x.Definition.Description);
        private IEnumerable<EncounterTargetView> HandSelections(int excluded, int max)
        {
            var cards = hand.Where(x => x.InstanceId != excluded && !x.IsEncounter).ToList();
            foreach (var c in cards) yield return Option("hand:" + c.InstanceId, c.Definition.Name);
            if (max > 1) for (var i = 0; i < cards.Count; i++) for (var j = i + 1; j < cards.Count; j++)
                yield return Option($"hand:{cards[i].InstanceId},{cards[j].InstanceId}", cards[i].Definition.Name + "、" + cards[j].Definition.Name);
        }
        private static IEnumerable<EncounterTargetView> EnemySelections(List<EnemyState> choices, int max)
        {
            foreach (var enemy in choices) yield return Option("enemies:" + enemy.Definition.Id, enemy.Definition.Name);
            if (max > 1) for (var i = 0; i < choices.Count; i++) for (var j = i + 1; j < choices.Count; j++)
                yield return Option("enemies:" + choices[i].Definition.Id + "," + choices[j].Definition.Id, choices[i].Definition.Name + "、" + choices[j].Definition.Name);
        }
        private IEnumerable<EncounterTargetView> EvidencePairs()
        {
            var entries = knowledge.Entries.Where(x => x.Level >= KnowledgeLevel.Observed).ToList();
            for (var i = 0; i < entries.Count; i++) for (var j = i + 1; j < entries.Count; j++)
                if (EvidenceCompatible(entries[i], entries[j])) yield return Option("evidence:" + entries[i].Key + "|" + entries[j].Key, entries[i].Text + " + " + entries[j].Text);
        }
        private static bool EvidenceCompatible(Evidence a, Evidence b) => a.Key.Split('/')[0] == b.Key.Split('/')[0] && a.Key != b.Key;
        private IEnumerable<EncounterTargetView> FocusOptions()
        {
            var subjects = LivingSubjects.Where(x => x.ActualIntent != null).ToList();
            foreach (var action in subjects) foreach (var sound in subjects.Where(x => x != action))
            {
                var newAction = action.ActualIntent.VisibleAction && !action.Confirmed;
                var newSound = !knowledge.Entries.Any(e => e.Key == EvidenceKey(sound.Definition.Id, InvestigationKind.SoundSource));
                if (newAction || newSound) yield return Option("focus:" + action.Definition.Id + "|" + sound.Definition.Id, action.Definition.Name + "确认；" + sound.Definition.Name + "声源");
            }
        }
        private string EnemyChoiceDetail(int n, EnemyState e) => (n == 2 || n == 14 || n == 50) && e.Confirmed ? "意图已确认" : e.Definition.Candidates;

        private string CardAvailability(CardView card, List<EncounterTargetView> targets)
        {
            var n = CardNumber(card.Definition);
            if (phase != BattlePhase.PlayerTurn) return "当前不能出牌。";
            if (actions < card.Definition.Cost) return "行动点不足。";
            if (sanity <= card.Definition.SanityCost) return "理智不足以支付代价。";
            if ((n == 65 || n == 81) && health <= (n == 65 ? 4 : 5) || n == 88 && health <= 4) return "生命不足以支付代价。";
            if (n == 25 && directionalListeningUsed) return "本回合已使用定向听筒。";
            if (n == 85 && turnFlags.Contains("c085")) return "本回合同名效果已使用。";
            if (n == 74 && !LivingSubjects.Any(x => CanInvestigate(x.Definition.Id, InvestigationKind.DamageChannel) || CanInvestigate(x.Definition.Id, InvestigationKind.SoundSource)))
                return "当前没有可追加的问题。";
            if (n == 54 && draw.Count == 0) return "抽牌堆没有可校正的牌。";
            if (n == 86 && boss == null && !knowledge.Entries.Any(x => x.Level == KnowledgeLevel.Verified)) return "尚未满足证据条件。";
            if (RequiresChoice(n) && targets.Count == 0 && !HasFallback(n)) return "当前没有合法目标。";
            return null;
        }
        private static bool RequiresChoice(int n) => new[] { 1,2,3,4,5,7,8,9,10,11,12,14,15,16,17,18,23,28,37,38,39,41,42,43,44,45,46,47,48,49,50,51,52,53,55,56,57,58,59,60,61,62,63,65,66,67,68,69,70,71,72,73,75,76,78,82,83,84,86,87,90 }.Contains(n);
        private bool HasFallback(int n)
        {
            if (!new[] { 38,40,42,43,44,47,48,49,52,53,56,59,60,63,66,67,68,69,70 }.Contains(n)) return false;
            if (!new[] { 42,43,44,52,59,60,63,66,67,68,69 }.Contains(n)) return true;
            return n == 63 || boss?.HasCardMechanic(n) != true;
        }

        private string ValidateCardPlay(CardView card, string target)
        {
            RefreshCardViews();
            if (!card.Enabled) return card.DisabledReason;
            var options = card.Targets;
            if (options.Count > 0 && !options.Any(x => x.Enabled && x.Id == target)) return "请选择当前列出的合法目标。";
            if (options.Count == 0 && target != null) return "该卡牌不接受目标。";
            if (RequiresChoice(CardNumber(card.Definition)) && options.Count == 0 && !HasFallback(CardNumber(card.Definition))) return "当前没有合法目标。";
            return null;
        }

        private void ApplyCatalogCard(CardView card, string target)
        {
            var n = CardNumber(card.Definition); var amount = card.Definition.Effects[0].Amount;
            var enemyId = target != null && target.StartsWith("enemy:", StringComparison.Ordinal) ? target.Substring(6) : target;
            if (n >= 1 && n <= 18) ApplyArmed(n, amount, enemyId, target);
            else if (n >= 19 && n <= 36) ApplyDefense(n, amount, enemyId, target);
            else if (n >= 37 && n <= 54) ApplyInvestigation(n, amount, enemyId, target);
            else if (n >= 55 && n <= 72) ApplyIntervention(n, amount, enemyId, target);
            else ApplyForbidden(n, amount, enemyId, target);
        }

        private void ApplyArmed(int n, int amount, string enemyId, string selection)
        {
            if (n == 6 || n == 13) foreach (var e in LivingTargets.ToList()) { DamageEnemy(e.Definition.Id, n == 13 && IsSummon(e) ? amount + 3 : amount); turnFlags.Add("armed-hit:" + e.Definition.Id); }
            else if (n == 9) foreach (var id in selection.Substring("enemies:".Length).Split(',')) { DamageEnemy(id, amount); turnFlags.Add("armed-hit:" + id); }
            else
            {
                var e = Find(enemyId); var damage = amount;
                if (n == 2) damage = e.Confirmed ? amount : 5;
                if (n == 4) damage = IsSummon(e) ? 16 : 12;
                if (n == 8 && !IsVerifiedSource(e)) damage = 8;
                if (n == 10 && boss?.IsBoundGuard(enemyId) == true) damage += 6;
                if (n == 14 && !e.Confirmed) damage = 10;
                if (n == 16 && turnFlags.Contains("armed-hit:" + enemyId)) damage += 3;
                if (n == 17 && boss?.IsExposed(enemyId) != true) damage = 9;
                var otherKilled = turnFlags.Contains("enemy-defeated");
                DamageEnemy(enemyId, damage); turnFlags.Add("armed-hit:" + enemyId);
                if (n == 3 && !investigationUsed) AddBlock(5);
                if (n == 7) ReduceNextThreat(enemyId, DamageChannel.Health, 3, requireKnown: true);
                if (n == 11 && otherKilled) Draw(1);
                if (n == 12) AddBlock(4);
                if (n == 15) boss?.CancelSummon(enemyId, ordinaryInterruptOnly: true);
                if (n == 18) AddBlock(7);
            }
        }
        private void ApplyDefense(int n, int amount, string enemyId, string selection)
        {
            switch (n)
            {
                case 19: AddBlock(amount); break; case 20: AddWard(amount); break;
                case 21: AddBlock(amount); AddWard(3); break; case 22: Heal(amount); break;
                case 23: ReduceNextThreat(enemyId, DamageChannel.Sanity, 4, false); Draw(1); break;
                case 24: AddBlock(amount); turnFlags.Add("block-after-scene"); break;
                case 25: AddWard(amount); directionalListeningBlocked = true; break;
                case 26: AddBlock(5 + (crewSupportUsed ? 3 : 0)); AddWard(2 + (crewSupportUsed ? 2 : 0)); break;
                case 27: AddBlock(amount); break;
                case 28: AddBlock(6); Retain(selection); break;
                case 29: AddBlock(13); if (!sceneActionUsed) AddWard(4); break;
                case 30: PayHealth(0); Heal(6); break;
                case 31: AddBlock(8 + (sceneActionUsed ? 3 : 0)); break;
                case 32: AddWard(6); if (crewSupportUsed) Draw(1); break;
                case 33: Heal(3); AddBlock(4); break;
                case 34: AddBlock(6 + (enemies.Any(x => x.Health > 0 && IsSummon(x)) ? 3 : 0)); break;
                case 35: AddWard(3); break;
                case 36: AddBlock(8); sceneFailureReduction = Math.Max(sceneFailureReduction, 4); break;
            }
        }
        private void ApplyInvestigation(int n, int amount, string enemyId, string selection)
        {
            investigationUsed = true;
            switch (n)
            {
                case 37: RecordClue(enemyId); Draw(1); break;
                case 38: if (!MoveDiscardToHand(selection, IsArmed)) Draw(1); break;
                case 39: if (IsConfirmed(enemyId)) Draw(1); else Investigate(enemyId, InvestigationKind.VisibleAction, "临时照明"); break;
                case 40: if (selection == null) Draw(1); else VerifyEvidence(selection.Substring(9)); break;
                case 41: Investigate(enemyId, InvestigationKind.SoundSource, "听声辨位"); Draw(1); break;
                case 42: if (boss?.InspectConnection(selection) != true) Draw(1); break;
                case 43: if (boss?.MeasureCell(selection) != true) Draw(1); break;
                case 44: if (boss?.OpenProvenSafe(selection, countsQuota: true) != true && boss?.HasCardMechanic(n) != true) AddWard(3); break;
                case 45: Retain(selection); Draw(1); break;
                case 46: if (boss?.VerifySummonSource(enemyId) != true) Draw(1); break;
                case 47: if (!MoveDiscardToDraw(selection, x => IsInvestigation(x) && !x.Exhaust)) Draw(1); break;
                case 48: if (selection == null) Draw(1); else VerifyEvidencePair(selection); break;
                case 49: if (selection == null) AddWard(3); else ApplyFocus(selection); break;
                case 50: if (IsConfirmed(enemyId)) Draw(2); else Draw(1); break;
                case 51: Retain(selection); break;
                case 52: if (bossKind == BossEncounterKind.Watcher) { boss.VerifyGuardAnchor(selection); AddWard(4); } else Draw(1); break;
                case 53: if (selection == null) Draw(2); else foreach (var id in selection.Substring("enemies:".Length).Split(',')) Investigate(id, InvestigationKind.VisibleAction, "现场重建"); break;
                case 54: BeginReorder(); break;
            }
        }
        private void ApplyIntervention(int n, int amount, string enemyId, string selection)
        {
            switch (n)
            {
                case 55: DamageEnemy(enemyId, amount); Interrupt(enemyId); break;
                case 56: if (selection != null && selection.StartsWith("enemy:", StringComparison.Ordinal)) DamageEnemy(enemyId, 7); else boss?.ApplyCardAction(n, selection); break;
                case 57: case 59: case 60: case 61: case 62: case 63: case 65: case 66: case 67: case 68: case 69: if (boss?.ApplyCardAction(n, selection) != true) ApplyInterventionFallback(n); break;
                case 58: if (boss?.ApplyCardAction(n, selection) != true && boss == null && objective.AllowsTaskAction) { AdvanceObjective(1); MarkSceneAction(); } break;
                case 72: if (boss?.ApplyCardAction(n, selection) != true && boss == null && objective.AllowsTaskAction) { AdvanceObjective(2); MarkSceneAction(); } break;
                case 64: sceneDiscount = Math.Max(sceneDiscount, 1); break;
                case 70: if (enemyId != null) Interrupt(enemyId); else AddWard(2); break;
                case 71: Retain(selection); turnFlags.Add("draw-after-scene"); break;
            }
        }
        private void ApplyInterventionFallback(int n)
        {
            if (n == 59) AddBlock(5); else if (n == 60) AddBlock(7); else if (n == 63) AddBlock(4); else if (n == 66) AddBlock(5); else if (n == 67) AddBlock(8); else if (n == 68 || n == 69) AddBlock(6);
        }
        private void ApplyForbidden(int n, int amount, string enemyId, string selection)
        {
            switch (n)
            {
                case 73: RecordThreatCategories(enemyId); Draw(1); break;
                case 74: watchedInvestigationBonus = 1; watched = Math.Min(3, watched + 1); Record($"被注视 {watched}/3。"); break;
                case 75: Confirm(enemyId, "借来的视线"); break;
                case 76: DamageEnemy(enemyId, amount); break;
                case 77: AddBlock(amount); break;
                case 78: boss?.ApplyCardAction(n, selection); break;
                case 79: Draw(amount); break;
                case 80: nextHealthReduction = Math.Max(nextHealthReduction, 12); break;
                case 81: PayHealth(5); sanity = Math.Min(definition.Sanity, sanity + 3); Record("恢复 3 理智。"); break;
                case 82: boss?.VerifyGuardAnchor(selection); break;
                case 83: MoveDiscardToHand(selection, x => !IsForbidden(x)); break;
                case 84: boss?.BindThreat(enemyId, DamageChannel.Sanity); break;
                case 85: actions++; turnFlags.Add("c085"); break;
                case 86: if (boss?.ApplyCardAction(n, selection) != true && boss == null && objective.AllowsTaskAction) { AdvanceObjective(1); MarkSceneAction(); } break;
                case 87: if (boss?.ApplyCardAction(n, selection) != true && boss == null && taskUsed) taskUsed = false; break;
                case 88: PayHealth(4); foreach (var e in enemies.Where(IsSummon).ToList()) DamageEnemy(e.Definition.Id, 10); break;
                case 89: AddWard(amount); break;
                case 90: DamageEnemy(enemyId, amount); break;
            }
        }

        private bool IsSummon(EnemyState e) => e != null && boss != null && enemies.IndexOf(e) > 0;
        private bool IsVerifiedSource(EnemyState e) => e != null && (knowledge.Entries.Any(x => x.Key.StartsWith(e.Definition.Id + "/source", StringComparison.Ordinal) && x.Level == KnowledgeLevel.Verified) || boss?.IsVerifiedSource(e.Definition.Id) == true);
        private bool IsVerifiedSource(string id) => IsVerifiedSource(Find(id));
        private void PayHealth(int amount) { if (amount <= 0) return; health -= amount; Record($"支付 {amount} 生命。"); }
        private void Retain(string selection) { foreach (var id in ParseIds(selection, "hand:")) retained.Add(id); }
        private static IEnumerable<int> ParseIds(string selection, string prefix) { if (selection == null || !selection.StartsWith(prefix, StringComparison.Ordinal)) yield break; foreach (var s in selection.Substring(prefix.Length).Split(',')) if (int.TryParse(s, out var id)) yield return id; }
        private bool MoveDiscardToHand(string selection, Func<CardSpec, bool> predicate) { var id = ParseIds(selection, "discard:").FirstOrDefault(); var c = discard.FirstOrDefault(x => x.InstanceId == id && predicate(x.Definition)); if (c == null) return false; discard.Remove(c); hand.Add(c); return true; }
        private bool MoveDiscardToDraw(string selection, Func<CardSpec, bool> predicate) { var id = ParseIds(selection, "discard:").FirstOrDefault(); var c = discard.FirstOrDefault(x => x.InstanceId == id && predicate(x.Definition)); if (c == null) return false; discard.Remove(c); draw.Add(c); return true; }
        private void ReorderTop(string selection) { if (selection == null || !selection.StartsWith("deck:", StringComparison.Ordinal)) return; var ids = ParseIds(selection, "deck:").ToList(); var cards = ids.Select(id => draw.FirstOrDefault(x => x.InstanceId == id)).ToList(); if (cards.Any(x => x == null) || cards.Distinct().Count() != cards.Count) return; foreach (var c in cards) draw.Remove(c); foreach (var c in cards.AsEnumerable().Reverse()) draw.Add(c); }
        private void VerifyEvidence(string key) { var old = definition.Archive.FirstOrDefault(x => x.Key == key && x.Level == KnowledgeLevel.Verified); if (old != null) knowledge.Record(new Evidence(old.Key, old.Text, "对照旧档", KnowledgeLevel.Verified)); }
        private void VerifyEvidencePair(string selection) { var keys = selection.Substring("evidence:".Length).Split('|'); if (keys.Length == 2) knowledge.Record(new Evidence(keys[0].Split('/')[0] + "/connection/" + string.Join("+", keys.OrderBy(x => x)), "两项公开证据构成已验证联系", "复核证据", KnowledgeLevel.Verified)); }
        private void ApplyFocus(string selection) { var pair = selection.Substring("focus:".Length).Split('|'); if (pair.Length != 2) return; Investigate(pair[0], InvestigationKind.VisibleAction, "透镜聚焦"); Investigate(pair[1], InvestigationKind.SoundSource, "透镜聚焦"); }
        private void Confirm(string id, string source) { var e = Find(id); if (e?.ActualIntent == null) return; e.Confirmed = true; knowledge.Record(new Evidence(EvidenceKey(id, InvestigationKind.VisibleAction), Describe(e.ActualIntent), source, KnowledgeLevel.Observed)); }
        private void RecordThreatCategories(string id)
        {
            var e = Find(id);
            if (e == null) return;
            var categories = boss?.ThreatCategories(id);
            if (categories == null)
            {
                var ordinary = new List<string>(2);
                if (e.ActualIntent?.Amount > 0) ordinary.Add(e.ActualIntent.Channel == DamageChannel.Health ? "生命伤害" : "理智冲击");
                if (e.ActualIntent?.Name.StartsWith("召唤", StringComparison.Ordinal) == true) ordinary.Add("召唤");
                categories = ordinary.ToArray();
            }
            knowledge.Record(new Evidence(id + "/" + turn + "/categories", categories.Length == 0 ? "本回合无上述威胁类别" : string.Join("、", categories), "血色启示", KnowledgeLevel.Observed));
        }
        private void BeginReorder()
        {
            pendingReorder = draw.AsEnumerable().Reverse().Take(3).ToList();
            if (pendingReorder.Count == 0) pendingReorder = null;
        }
        private static IEnumerable<List<CardView>> Permute(List<CardView> cards, int index)
        {
            if (index == cards.Count) { yield return new List<CardView>(cards); yield break; }
            for (var i = index; i < cards.Count; i++)
            {
                var current = cards[index]; cards[index] = cards[i]; cards[i] = current;
                foreach (var order in Permute(cards, index + 1)) yield return order;
                current = cards[index]; cards[index] = cards[i]; cards[i] = current;
            }
        }
        private PendingCardChoiceView PendingChoiceView()
        {
            if (pendingReorder == null) return null;
            var options = Permute(new List<CardView>(pendingReorder), 0)
                .Select(p => Option("deck:" + string.Join(",", p.Select(x => x.InstanceId)), string.Join(" → ", p.Select(x => x.Definition.Name)))).ToList().AsReadOnly();
            return new PendingCardChoiceView("校正航记：选择抽牌顺序", options);
        }
        private string ResolvePendingReorder(string selection)
        {
            var view = PendingChoiceView();
            if (view == null || !view.Options.Any(x => x.Id == selection)) return "请选择列出的完整顺序。";
            ReorderTop(selection); pendingReorder = null; return null;
        }
        private void ReduceNextThreat(string id, DamageChannel channel, int amount, bool requireKnown) { var e = Find(id); if (e == null || requireKnown && !e.Confirmed || e.ActualIntent.Channel != channel) return; boss?.ReduceThreat(id, channel, amount); if (boss == null) e.ActualIntent = new IntentSpec(e.ActualIntent.Id, e.ActualIntent.Name, e.ActualIntent.Clue, e.ActualIntent.Channel, Math.Max(0, e.ActualIntent.Amount - amount), e.ActualIntent.IsPublic, e.ActualIntent.VisibleAction, e.ActualIntent.SoundSource, e.ActualIntent.Interruptible); }
        private void TriggerSceneAction() { if (turnFlags.Remove("block-after-scene")) AddBlock(6); if (turnFlags.Remove("draw-after-scene")) Draw(1); }

        internal void MarkEnemyDefeated() => turnFlags.Add("enemy-defeated");
        internal int ConsumeHealthReduction(int amount) { if (nextHealthReduction <= 0) return amount; var reduced = Math.Min(amount, nextHealthReduction); nextHealthReduction = 0; return amount - reduced; }
        internal int SceneFailureDamage(int amount) { var reduced = Math.Min(amount, sceneFailureReduction); sceneFailureReduction = 0; return amount - reduced; }
        internal void MarkSceneAction() { sceneActionUsed = true; TriggerSceneAction(); }
        internal void MarkCrewSupport() { crewSupportUsed = true; }
    }
}
