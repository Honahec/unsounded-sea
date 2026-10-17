using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Reproducible balance probe: decisions only use BattleView and the policy's own commands.
// Deliberately heuristic, not an optimal player or a campaign win-rate estimate.
public static class CombatBalanceQA
{
    private static readonly Dictionary<string, int[]> Decks = new Dictionary<string, int[]>
    {
        ["starter"] = new[] { 1,2,12,19,20,21,22,37,39,41,55,71 },
        ["confirm"] = new[] { 1,2,14,16,19,20,21,39,41,45,50,55 },
        ["defend"] = new[] { 1,12,16,19,20,23,24,26,31,32,55,71 },
        ["source"] = new[] { 1,4,8,9,13,19,20,34,38,46,55,66 },
        ["retain"] = new[] { 2,3,14,16,17,19,20,28,45,51,54,55 },
        ["confirm-without-answers"] = new[] { 1,2,14,16,19,20,21,22,35,45,50,55 }
    };
    private sealed class Row
    {
        public string Encounter, Deck;
        public int Wins, Trials = 100;
        public double WilsonLow, WilsonHigh, MedianTurns, P90Turns, MedianHealthLoss, MedianSanityLoss, AverageCombos;
    }
    private sealed class Choice { public CardView Card; public string Target; public double Score; }
    private sealed class Policy
    {
        public readonly Dictionary<string, HashSet<int>> Hit = new Dictionary<string, HashSet<int>>();
        public readonly HashSet<string> Answer = new HashSet<string>();
        public readonly HashSet<int> Carried = new HashSet<int>();
        public readonly HashSet<int> Retain = new HashSet<int>();
        public int Combos;
        public bool BoundThisTurn, ObservationUsed;
        public readonly HashSet<string> Bound = new HashSet<string>();
        public bool OtherHit(string target, int instance) => target != null && Hit.TryGetValue(target, out var instances) && instances.Any(id => id != instance);
        public void RecordHit(string target, int instance) { if (!Hit.TryGetValue(target, out var instances)) Hit.Add(target, instances = new HashSet<int>()); instances.Add(instance); }
        public void End() { Hit.Clear(); Answer.Clear(); Carried.Clear(); foreach (var id in Retain) Carried.Add(id); Retain.Clear(); BoundThisTurn = ObservationUsed = false; }
    }

    public static string Main()
    {
        var rows = new List<Row>();
        Directory.CreateDirectory("Logs/CombatBalance");
        foreach (var region in new[] { "fog", "bones", "inverted", "fleet", "abyss" })
        for (var variant = 0; variant < 5; variant++)
        {
            var id = $"voyage-{region}-{variant}";
            var source = AssetDatabase.LoadAssetAtPath<EncounterDefinition>($"Assets/UnsoundedSea/Content/Encounters/Voyage/{id}.asset").Build();
            foreach (var deck in Decks)
            {
                var definition = new BattleDefinition(id, deck.Value.Select(n => CardCatalog.All[n - 1]), source.Enemies, Array.Empty<ToolSpec>());
                var turns = new List<int>(); var hp = new List<int>(); var sanity = new List<int>(); var combos = 0;
                var row = new Row { Encounter = id, Deck = deck.Key }; var savedFailure = false;
                for (uint seed = 0; seed < 100; seed++)
                {
                    var s = new BattleSession(definition, seed); var policy = new Policy();
                    while (s.View.Phase == BattlePhase.PlayerTurn && s.View.Turn <= 30)
                    {
                        Act(s, policy);
                        if (s.View.Phase == BattlePhase.PlayerTurn) { Must(s.Execute(BattleCommand.EndTurn())); policy.End(); }
                    }
                    if (s.View.Phase == BattlePhase.Victory) { row.Wins++; turns.Add(s.View.Turn); }
                    else if (!savedFailure) { File.WriteAllText($"Logs/CombatBalance/failure-{id}-{deck.Key}.json", JsonConvert.SerializeObject(BattleReplay.Capture(s), Formatting.Indented)); savedFailure = true; }
                    hp.Add(60 - s.View.Health); sanity.Add(30 - s.View.Sanity); combos += policy.Combos;
                }
                row.MedianTurns = Percentile(turns, .5); row.P90Turns = Percentile(turns, .9);
                row.MedianHealthLoss = Percentile(hp, .5); row.MedianSanityLoss = Percentile(sanity, .5); row.AverageCombos = combos / 100.0;
                var p = row.Wins / 100.0; var center = (p + 3.8416 / 200) / 1.038416;
                var margin = 1.96 * Math.Sqrt(p * (1-p) / 100 + 3.8416 / 40000) / 1.038416;
                row.WilsonLow = center - margin; row.WilsonHigh = center + margin; rows.Add(row);
            }
        }
        File.WriteAllText("Logs/CombatBalance/results.json", JsonConvert.SerializeObject(rows, Formatting.Indented));
        return JsonConvert.SerializeObject(rows.GroupBy(r => r.Deck).Select(g => new { Deck = g.Key, Wins = g.Sum(r => r.Wins), Trials = g.Sum(r => r.Trials), MeanEncounterMedianTurns = g.Average(r => r.MedianTurns), MeanEncounterMedianHealthLoss = g.Average(r => r.MedianHealthLoss), MeanEncounterMedianSanityLoss = g.Average(r => r.MedianSanityLoss) }));
    }
    private static double Percentile(List<int> values, double p) { if (values.Count == 0) return double.NaN; values.Sort(); return values[Math.Max(0, (int)Math.Ceiling(values.Count*p)-1)]; }
    public static string BossSmoke()
    {
        var rows = new List<object>(); Directory.CreateDirectory("Logs/CombatBalance");
        foreach (var id in new[] { "fog-visitors", "m02", "m03", "m04", "m05" })
        foreach (var name in new[] { "confirm", "source" })
        {
            var source = AssetDatabase.LoadAssetAtPath<EncounterDefinition>($"Assets/UnsoundedSea/Content/Encounters/{id}.asset").Build();
            var kind = id == "m02" ? BossEncounterKind.Choirmaster : id == "m03" ? BossEncounterKind.Surveyor : id == "m04" ? BossEncounterKind.FlagBearer : id == "m05" ? BossEncounterKind.Watcher : BossEncounterKind.None;
            var definition = new BattleDefinition(id, Decks[name].Select(n => CardCatalog.All[n-1]), source.Enemies, Array.Empty<ToolSpec>(), source.Objective, source.ObjectiveTarget);
            var wins = 0; var turns = new List<int>(); var hp = new List<int>(); var sanity = new List<int>();
            for (uint seed = 0; seed < 30; seed++)
            {
                var s = new BattleSession(definition, seed, boss: kind); var policy = new Policy();
                while (s.View.Phase == BattlePhase.PlayerTurn && s.View.Turn <= 30)
                {
                    if (kind == BossEncounterKind.None && s.View.CanAdvanceObjective) Must(s.Execute(BattleCommand.Task()));
                    Act(s, policy);
                    if (s.View.Phase == BattlePhase.PlayerTurn) { Must(s.Execute(BattleCommand.EndTurn())); policy.End(); }
                }
                if (s.View.Phase == BattlePhase.Victory) { wins++; turns.Add(s.View.Turn); }
                else File.WriteAllText($"Logs/CombatBalance/boss-failure-{id}-{name}-{seed}.json", JsonConvert.SerializeObject(BattleReplay.Capture(s), Formatting.Indented));
                hp.Add(60-s.View.Health); sanity.Add(30-s.View.Sanity);
                if (seed == 0 && !BattleReplay.Capture(s).Restore(definition).View.Log.SequenceEqual(s.View.Log)) throw new InvalidOperationException("Boss replay diverged");
            }
            rows.Add(new { Encounter = id, Deck = name, Wins = wins, Trials = 30, MedianTurns = Percentile(turns,.5), P90Turns = Percentile(turns,.9), MedianHealthLoss = Percentile(hp,.5), MedianSanityLoss = Percentile(sanity,.5) });
        }
        var json = JsonConvert.SerializeObject(rows, Formatting.Indented); File.WriteAllText("Logs/CombatBalance/boss-results.json",json); return json;
    }
    private static bool SceneAction(BattleSession s, Policy p)
    {
        var v = s.View; if (v.Actions < 1) return false;
        string cardId = null, target = null;
        if (v.BossKind == BossEncounterKind.Choirmaster && v.BossStage == 1)
        {
            var listen = v.Hand.FirstOrDefault(c => c.Definition.Id == "E02-01" && c.Enabled);
            if (listen != null) cardId = "E02-01";
            else
            {
                var pairs = Regex.Matches(v.BossStatus, @"(潮|骨|息)匣在(潮|骨|息)匣前").Cast<Match>().ToArray();
                var labels = new Dictionary<string,string> { ["潮"]="choir-a", ["骨"]="choir-b", ["息"]="choir-c" };
                var open = v.EncounterTargets.Where(t => t.Detail == "未关闭").Select(t => t.Id).ToArray();
                if (pairs.Length == 2) { target = open.FirstOrDefault(id => !pairs.Any(m => labels[m.Groups[2].Value] == id && open.Contains(labels[m.Groups[1].Value]))); cardId="E02-02"; }
            }
        }
        if (v.BossKind == BossEncounterKind.Surveyor)
        {
            // Published six-step proof, checked against visible constraints at each step.
            target = new[] { "C3","D2","D3","B4","C1","C4" }.FirstOrDefault(id => v.EncounterTargets.Any(t => t.Id == id && t.Detail.StartsWith("?")));
            if (target != null && ProvenSafe(v, target)) cardId="E03-01";
        }
        if (v.BossKind == BossEncounterKind.FlagBearer && v.BossStage < 3)
        {
            var wanted = v.BossStage == 1 ? new[] { "R1:positive","R2:reverse" } : v.ObjectiveProgress == 0 ? new[] { "R1:off","R2:reverse" } : v.ObjectiveProgress == 1 ? new[] { "R1:positive","R2:reverse" } : new[] { "R1:positive","R2:off" };
            target = wanted.FirstOrDefault(id => v.EncounterTargets.Single(t => t.Id == id).Detail != "当前极性");
            if (target != null) cardId="E04-01";
            else if (v.BossStage == 2) { cardId="E04-02"; target="lock-" + new[] { "I","II","III" }[v.ObjectiveProgress]; }
        }
        if (v.BossKind == BossEncounterKind.Watcher)
        {
            if (v.BossStage == 1) { cardId="E05-01"; target="winch"; }
            else if (v.BossStage == 3) { cardId="E05-03"; target="winch"; }
            else if (!p.BoundThisTurn)
            {
                var bind = v.Hand.First(c => c.Definition.Id == "E05-02");
                target = bind.Targets.Where(t => t.Enabled).OrderBy(t => v.Enemies.First(e => e.Id == t.Id.Split('|')[0]).Health).Select(t => t.Id).FirstOrDefault();
                if (target != null) cardId="E05-02";
            }
        }
        var card = v.Hand.FirstOrDefault(c => c.Definition.Id == cardId && c.Enabled);
        if (card == null || card.Targets.Count > 0 && !card.Targets.Any(t => t.Id == target && t.Enabled)) return false;
        Must(s.Execute(BattleCommand.Play(card.InstanceId,target)));
        if (cardId == "E05-02") { p.Bound.Add(target.Split('|')[0]); p.BoundThisTurn=true; }
        return true;
    }
    private static bool ProvenSafe(BattleView v, string target)
    {
        var cells=v.EncounterTargets; var possible=new List<HashSet<string>>();
        Func<string,string,bool> adjacent=(a,b)=>a!=b && Math.Abs(a[0]-b[0])<=1 && Math.Abs(a[1]-b[1])<=1;
        for(var a=0;a<16;a++) for(var b=a+1;b<16;b++) for(var c=b+1;c<16;c++)
        {
            var mines=new HashSet<string>(new[]{cells[a].Id,cells[b].Id,cells[c].Id});
            if(cells.All(cell=>cell.Detail.StartsWith("?") || int.TryParse(cell.Detail.Split('\n')[0],out var number) && !mines.Contains(cell.Id) && mines.Count(m=>adjacent(m,cell.Id))==number)) possible.Add(mines);
        }
        return possible.Count>0 && possible.All(mines=>!mines.Contains(target));
    }
    private static void Must(CommandResult result) { if (!result.Success) throw new InvalidOperationException(result.Error); }
    private static int Number(CardView card) => card.IsEncounter ? 0 : CardCatalog.Number(card.Definition.Id);
    private static bool Has(BattleView view, int n) => view.Hand.Any(c => !c.IsEncounter && Number(c) == n);
    private static int Threat(EnemyView enemy, bool sanity)
    {
        if (enemy.Health <= 0) return 0;
        if (!enemy.IntentConfirmed) return sanity ? 2 : 5; // Fixed prior; never inspect locked hidden intent.
        var match = Regex.Match(enemy.Intent, @"(\d+) 点(生命伤害|理智冲击)");
        return match.Success && (match.Groups[2].Value == "理智冲击") == sanity ? int.Parse(match.Groups[1].Value) : 0;
    }
    private static void Act(BattleSession s, Policy policy)
    {
        for (var step = 0; step < 24 && s.View.Phase == BattlePhase.PlayerTurn; step++)
        {
            var v = s.View;
            if (v.PendingChoice != null) { Must(s.Execute(BattleCommand.Reorder(v.PendingChoice.Options[0].Id))); continue; }
            if (SceneAction(s, policy)) continue;
            var options = v.Hand.Where(c => c.Enabled && !c.IsEncounter).SelectMany(c => c.Targets.Count == 0
                ? new[] { new Choice { Card = c, Score = Score(v, c, null, policy) } }
                : c.Targets.Where(t => t.Enabled).Select(t => new Choice { Card = c, Target = t.Id, Score = Score(v, c, t.Id, policy) })).OrderByDescending(c => c.Score).ThenBy(c => c.Card.InstanceId).ToList();
            if (options.Count == 0 || options[0].Score <= 0) break;
            var pick = options[0]; var number = Number(pick.Card);
            Must(s.Execute(BattleCommand.Play(pick.Card.InstanceId, pick.Target)));
            var after = s.View; var target = pick.Target?.Replace("enemy:", "");
            if (number == 50 && policy.Answer.Contains(target)) policy.ObservationUsed = true;
            var enemy = v.Enemies.FirstOrDefault(e => e.Id == target);
            if (number == 2 && enemy?.IntentConfirmed == true && policy.Answer.Contains(target) || number == 12 && v.Block >= 6 || number == 14 && enemy?.IntentConfirmed == true && policy.Carried.Contains(pick.Card.InstanceId) || number == 16 && policy.OtherHit(target, pick.Card.InstanceId)) policy.Combos++;
            if (number <= 18) foreach (var e in v.Enemies) if (after.Enemies.First(x => x.Id == e.Id).Health < e.Health) policy.RecordHit(e.Id, pick.Card.InstanceId);
            if ((number == 39 && enemy != null && !enemy.IntentConfirmed && after.Enemies.First(e => e.Id == target).IntentConfirmed) || number == 41 && after.Evidence.Count > v.Evidence.Count) policy.Answer.Add(target);
            if (new[] { 28,45,51,71 }.Contains(number) && pick.Target != null) foreach (var id in pick.Target.Substring(5).Split(',')) policy.Retain.Add(int.Parse(id));
            policy.Carried.Remove(pick.Card.InstanceId); policy.Retain.Remove(pick.Card.InstanceId);
        }
    }
    private static double Score(BattleView v, CardView card, string target, Policy p)
    {
        var n = Number(card); var e = v.Enemies.FirstOrDefault(x => target == "enemy:" + x.Id);
        var h = Math.Max(0, v.Enemies.Sum(x => Threat(x, false)) - v.Block);
        var s = Math.Max(0, v.Enemies.Sum(x => Threat(x, true)) - v.Ward);
        Func<int,double> block = amount => Math.Min(h, amount) * 1.1;
        Func<int,double> ward = amount => Math.Min(s, amount) * 1.6;
        Func<EnemyView,int,double> hit = (enemy, damage) => !enemy.Targetable ? 0 : (Math.Min(enemy.Health, damage) + (enemy.Health <= damage ? 8 + Threat(enemy,false) + Threat(enemy,true)*1.5 : 0)) * (v.BossKind == BossEncounterKind.Watcher && !p.Bound.Contains(enemy.Id) ? .25 : 1);
        double value = 0;
        if (n <= 18)
        {
            var damage = card.Definition.Effects[0].Amount;
            if (n == 6 || n == 13) value = v.Enemies.Where(x => x.Health > 0).Sum(x => hit(x, damage));
            else if (n == 9) value = v.Enemies.Where(x => target.Substring(8).Split(',').Contains(x.Id)).Sum(x => hit(x, 4));
            else if (e != null)
            {
                if (n == 2) damage = e.IntentConfirmed ? 9 + (p.Answer.Contains(e.Id) ? 3 : 0) : 5;
                var summon = v.BossKind != BossEncounterKind.None && e.Id != v.Enemies[0].Id;
                if (n == 4) damage = summon ? 16 : 12; if (n == 8) damage = summon && v.Evidence.Any(x => x.Key == e.Id + "/source") ? 17 : 8; if (n == 17) damage = v.BossKind == BossEncounterKind.FlagBearer && v.BossStage == 3 ? 18 : 9;
                if (n == 12 && v.Block >= 6) damage = 7;
                if (n == 14) damage = e.IntentConfirmed ? 15 + (p.Carried.Contains(card.InstanceId) ? 4 : 0) : 10;
                if (n == 16) damage = 3 + (p.OtherHit(e.Id, card.InstanceId) ? 5 : 0);
                value = hit(e, damage);
                if (n != 16 && Has(v, 16) && v.Actions >= card.Definition.Cost + 1 && !p.Hit.ContainsKey(e.Id)) value += 4;
                if (n == 3) value += block(5); if (n == 7 && e.IntentConfirmed) value += Math.Min(Threat(e, false), 3);
                if (n == 12) value += block(3); if (n == 18) value += block(7);
            }
        }
        else switch (n)
        {
            case 19: value = block(10); break; case 20: value = ward(6); break; case 21: value = block(7) + ward(3); break;
            case 22: value = Math.Min(4, 60-v.Health); break; case 23: value = Math.Min(s, e == null ? 0 : Threat(e,true)) * 1.6 + 3; break;
            case 24: value = block(6); break; case 26: value = block(5)+ward(2); break; case 28: value = block(6)+1; break;
            case 31: value = block(8); break; case 32: value = ward(6); break; case 34: value = block(v.BossKind != BossEncounterKind.None && v.Enemies.Skip(1).Any(x=>x.Health>0) ? 12 : 6); break; case 35: value = ward(3); break;
            case 37: value = 4; break; case 38: value = target == null ? 2 : v.Actions >= 2 ? 6 : 1; break;
            case 39: value = e != null && !e.IntentConfirmed ? 3 + (Has(v, 2) ? 4 : 0) + (Has(v,50) && !p.ObservationUsed && v.Actions >= 2 ? 12 : 0) : 2; break;
            case 41: value = 3 + (e != null && !p.Answer.Contains(e.Id) && e.IntentConfirmed ? (Has(v, 2) ? 3 : 0) + (Has(v,50) && !p.ObservationUsed && v.Actions >= 2 ? 12 : 0) : 0); break;
            case 45: case 51: case 71: value = target != null && target.Substring(5).Split(',').Select(int.Parse).Any(id => v.Hand.Any(c => c.InstanceId == id && Number(c) == 14) && !p.Carried.Contains(id)) ? 7 : 1; break;
            case 46: value = 2; break; case 50: value = e?.IntentConfirmed == true ? 5 + (p.Answer.Contains(e.Id) ? ward(3) + (!p.ObservationUsed ? 20 : 0) : 0) : 2; break;
            case 54: value = v.Actions > 1 ? 4 : 1; break;
            case 55: value = e == null ? 0 : hit(e,4) + (e.IntentConfirmed && e.Intent.Contains("可打断") ? Threat(e,false)+Threat(e,true)*1.6 : 0); break;
            case 66: value = block(5); break;
        }
        if (new[] {19,21,24,28,31,34,66}.Contains(n) && v.Block < 6 && Has(v,12) && v.Actions > 1) value += 2;
        return value / Math.Max(1, card.Definition.Cost);
    }
}
