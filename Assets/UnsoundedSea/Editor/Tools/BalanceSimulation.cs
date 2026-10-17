using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Deterministic, black-box balance simulation. Policy input is restricted to BattleView.
public static class BalanceSimulation
{
    private const int Trials = 100, TurnLimit = 45;
    private static readonly string[] Regions = { "fog", "bones", "inverted", "fleet", "abyss" };
    private static readonly string[] Bosses = { "fog-visitors", "m02", "m03", "m04", "m05", "drowned-bell", "storm-gauntlet", "hollow-beacon" };
    private static readonly string[] Equipment = { "i17" };
    private static readonly string[] Tools = { "lantern", "listener" };
    private static readonly string[] Supports = { "medic", "listener" };

    [Serializable] private sealed class Report
    {
        public string generatedUtc, simulatorVersion = "1";
        public string[] criteria = { "act1 starter >=90%", "act2 starter 85-95%", "act2 combo >=98%, median health loss 3-7, passive sanity loss 0-2", "act3 starter 70-85%", "act1 risky-safe ending health gap >=8" };
        public List<Row> encounters = new List<Row>();
        public List<Aggregate> pooledActs = new List<Aggregate>();
        public List<Campaign> campaigns = new List<Campaign>();
        public object focusedPrices, routeComparison;
    }
    [Serializable] private sealed class Row
    {
        public string encounter, region, cohort, definitionDigest; public bool boss;
        public string[] deck, tools, supports, consumables, equipment;
        public int trials, wins, timeouts; public double winPercent, medianTurns, p90Turns, medianHealthLoss, medianPassiveSanityDamage, medianActiveSanityPayment;
        public double averageToolUses, averageSupportUses, averageConsumableUses; public string turnStatistic = "victories only", criterion; public bool criterionMet;
        [JsonIgnore] public List<Trial> samples;
        public List<Failure> failures=new List<Failure>();
    }
    [Serializable] private sealed class Failure { public uint seed; public string outcome; public BattleReplay replay; public string definitionDigest; public string[] deck, tools, supports, consumables, equipment; }
    [Serializable] private sealed class Aggregate { public int act; public string cohort; public int trials, wins; public double winPercent, medianHealthLoss, medianPassiveSanityDamage, medianActiveSanityPayment; public bool criterionMet; public string criterion; }
    [Serializable] private sealed class Campaign
    {
        public uint seed; public string policy, outcome; public List<ActEnd> acts = new List<ActEnd>(); public List<string> route = new List<string>(), rewards = new List<string>(); public string[] finalDeck, finalTools, finalEquipment, finalItems; public int finalHealth, finalSanity, finalSupplies;
    }
    [Serializable] private sealed class ActEnd { public int act, health, sanity, supplies; public string region; }
    private sealed class Trial
    {
        public bool Win, Timeout; public int Turns, HealthLoss, PassiveSanity, ActiveSanity, ToolUses, SupportUses, ConsumableUses; public BattleSession Session; public readonly Dictionary<string,int> CardUses = new Dictionary<string,int>();
    }
    private sealed class Option { public CardView Card; public string Target; public double Score; }
    // Reuse the simulator's public-information card policy in the real CLI acceptance journey.
    public static BattleCommand SuggestCommand(BattleView view)
    {
        var options=view.Hand.Where(x=>x.Enabled&&!x.IsEncounter).SelectMany(card=>card.Targets.Count==0
            ?new[]{new Option{Card=card,Score=Score(view,card,null)}}
            :card.Targets.Where(x=>x.Enabled).Select(x=>new Option{Card=card,Target=x.Id,Score=Score(view,card,x.Id)}))
            .OrderByDescending(x=>x.Score).ThenBy(x=>x.Card.InstanceId).ToArray();
        return options.Length==0||options[0].Score<=0?BattleCommand.EndTurn():BattleCommand.Play(options[0].Card.InstanceId,options[0].Target);
    }

    public static string Main()
    {
        var report = new Report { generatedUtc = DateTime.UtcNow.ToString("O") };
        foreach (var entry in VoyageEncounterCatalog.AllEntries.OrderBy(x => x.id, StringComparer.Ordinal))
        {
            var expected = entry.isBoss ? Bosses.Contains(entry.id) : Regions.Any(r => Enumerable.Range(0, 5).Select(i => $"voyage-{r}-{i}").Contains(entry.id));
            if (!expected) continue;
            foreach (var cohort in new[] { "starter", "current-act", "random-learned" }) report.encounters.Add(Simulate(entry.scenario, entry.id, entry.group, entry.isBoss, cohort));
        }
        if (report.encounters.Count != (25 + Bosses.Length) * 3)
            throw new InvalidOperationException($"Expected 99 encounter/cohort rows, found {report.encounters.Count}; refusing to hide missing content.");
        BuildAggregates(report);
        const uint campaignSeed = 20261017;
        report.campaigns.Add(RunCampaign(campaignSeed, false));
        report.campaigns.Add(RunCampaign(campaignSeed, true));
        var safeAct1 = report.campaigns[0].acts.FirstOrDefault(x => x.act == 1);
        var riskyAct1 = report.campaigns[1].acts.FirstOrDefault(x => x.act == 1);
        var routeGap = safeAct1 == null || riskyAct1 == null ? 0 : safeAct1.health - riskyAct1.health;
        report.routeComparison = new { seed = campaignSeed, safeAct1Health = safeAct1?.health, riskyAct1Health = riskyAct1?.health, healthGap = routeGap, criterion = ">=8", criterionMet = routeGap >= 8 };
        report.focusedPrices = JsonConvert.DeserializeObject(FocusedPrices());
        var directory = Path.Combine("Logs", "Balance"); Directory.CreateDirectory(directory);
        var json = JsonConvert.SerializeObject(report, Formatting.Indented);
        File.WriteAllText(Path.Combine(directory, "balance-simulation.json"), json);
        return JsonConvert.SerializeObject(new { path = "Logs/Balance/balance-simulation.json", encounters = report.encounters.Count, campaigns = report.campaigns.Count }, Formatting.Indented);
    }

    public static string FocusedPrices()
    {
        var rows = new List<object>();
        var encounters = new[] { "voyage-bones-0", "voyage-inverted-1", "voyage-fleet-2", "voyage-abyss-4" };
        foreach (var id in new[] { "c076", "c085" })
        foreach (var include in new[] { false, true })
        foreach (var encounter in encounters)
        {
            var region = Region(encounter, null); var legal = LegalLearnedPool(RegionAct(region), region).ToList();
            var cards = BalancedDeck(RegionAct(region), region).Where(x => x.Id != id).ToList();
            foreach (var card in legal) if (cards.Count < 12 && card.Id != id && cards.All(x => x.Id != card.Id)) cards.Add(card);
            if (include) cards[cards.Count - 1] = CardCatalog.Get(id);
            var scenario = VoyageEncounterCatalog.Resolve(encounter);
            var trials = Enumerable.Range(0, Trials).Select(seed => RunBattle(scenario, Clone(scenario.encounter.Build(), cards), (uint)seed)).ToList();
            rows.Add(new { card = id, included = include, encounter, sanityCost = CardCatalog.Get(id).SanityCost,
                wins = trials.Count(x => x.Win), uses = trials.Sum(x => x.CardUses.TryGetValue(id, out var count) ? count : 0),
                medianHealthLoss = Percentile(trials.Select(x => x.HealthLoss), .5),
                medianPassiveSanityDamage = Percentile(trials.Select(x => x.PassiveSanity), .5),
                medianActiveSanityPayment = Percentile(trials.Select(x => x.ActiveSanity), .5) });
        }
        var result = new { note = "Baseline comparison across four non-fog encounters; prices are not changed by this simulator.", rows };
        var directory = Path.Combine("Logs", "Balance"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "focused-c076-c085.json"), JsonConvert.SerializeObject(result, Formatting.Indented));
        return JsonConvert.SerializeObject(result, Formatting.Indented);
    }

    public static string SelfCheck()
    {
        var hidden = "可能：造成 4 点生命伤害；或造成 7 点生命伤害。可能：造成 2 点理智冲击；或造成 5 点理智冲击。";
        if (WorstCandidate(hidden, DamageChannel.Health) != 7 || WorstCandidate(hidden, DamageChannel.Sanity) != 5) throw new InvalidOperationException("Worst-candidate parser summed or missed mutually exclusive candidates.");
        var accounting = AccountSanity(30, 27, 23);
        if (accounting.Active != 3 || accounting.Passive != 4) throw new InvalidOperationException("Active/passive sanity accounting crossed channels.");
        return JsonConvert.SerializeObject(new { worstHealth = 7, worstSanity = 5, active = accounting.Active, passive = accounting.Passive });
    }

    private static (int Active, int Passive) AccountSanity(int beforeAction, int afterAction, int afterEnemyTurn) =>
        (Math.Max(0, beforeAction - afterAction), Math.Max(0, afterAction - afterEnemyTurn));

    private static Row Simulate(BattleScenario scenario, string id, string group, bool boss, string cohort)
    {
        var source = scenario.encounter.Build();
        var region = Region(id, group); var act = RegionAct(region);
        var deck = cohort == "starter" ? CardCatalog.Starter : cohort == "current-act" ? BalancedDeck(act, region) : RandomLearnedDeck(id, act);
        var definition = Clone(source, deck);
        var row = new Row
        {
            encounter = id, region = region, boss = boss, cohort = cohort, trials = Trials,
            deck = deck.Select(x => x.Id).ToArray(), tools = definition.Tools.Select(x => x.Id).ToArray(), supports = definition.Supports.Select(x => x.Id).ToArray(),
            consumables = definition.Loadout.Consumables.ToArray(), equipment = definition.Loadout.Equipment.ToArray(), definitionDigest = BattleReplay.Fingerprint(definition, BossKind(scenario))
        };
        var results = new List<Trial>();
        for (uint seed = 0; seed < Trials; seed++)
        {
            var trial = RunBattle(scenario, definition, seed); results.Add(trial);
            if (!trial.Win) row.failures.Add(new Failure { seed = seed, outcome = trial.Timeout ? "timeout" : trial.Session.View.Phase.ToString(), replay = BattleReplay.Capture(trial.Session), definitionDigest = row.definitionDigest, deck = row.deck, tools = row.tools, supports = row.supports, consumables = row.consumables, equipment = row.equipment });
        }
        row.wins = results.Count(x => x.Win); row.timeouts = results.Count(x => x.Timeout); row.winPercent = 100.0 * row.wins / row.trials;
        row.medianTurns = Percentile(results.Where(x => x.Win).Select(x => x.Turns), .5); row.p90Turns = Percentile(results.Where(x => x.Win).Select(x => x.Turns), .9);
        row.medianHealthLoss = Percentile(results.Select(x => x.HealthLoss), .5); row.medianPassiveSanityDamage = Percentile(results.Select(x => x.PassiveSanity), .5); row.medianActiveSanityPayment = Percentile(results.Select(x => x.ActiveSanity), .5);
        row.averageToolUses = results.Average(x => x.ToolUses); row.averageSupportUses = results.Average(x => x.SupportUses); row.averageConsumableUses = results.Average(x => x.ConsumableUses);
        row.samples = results;
        SetCriterion(row, act);
        return row;
    }

    private static Trial RunBattle(BattleScenario scenario, BattleDefinition definition, uint seed)
    {
        var boss = BossKind(scenario);
        var session = new BattleSession(definition, seed, scenario.rules == null ? null : scenario.rules.CreateEffects(), scenario.rules == null ? null : scenario.rules.CreateObjective(definition), boss);
        var result = new Trial { Session = session };
        var startHealth = session.View.Health;
        while (session.View.Phase == BattlePhase.PlayerTurn && session.View.Turn <= TurnLimit)
        {
            Act(session, result);
            if (session.View.Phase != BattlePhase.PlayerTurn) break;
            var before = session.View.Sanity;
            Must(session.Execute(BattleCommand.EndTurn()));
            result.PassiveSanity += Math.Max(0, before - session.View.Sanity);
        }
        result.Timeout = session.View.Phase == BattlePhase.PlayerTurn;
        result.Win = session.View.Phase == BattlePhase.Victory;
        result.Turns = session.View.Turn; result.HealthLoss = Math.Max(0, startHealth - session.View.Health);
        return result;
    }

    private static void Act(BattleSession session, Trial result)
    {
        for (var step = 0; step < 40 && session.View.Phase == BattlePhase.PlayerTurn; step++)
        {
            var view = session.View;
            if (view.PendingChoice != null) { Execute(session, BattleCommand.Reorder(view.PendingChoice.Options[0].Id), result); continue; }
            if (BossAction(session, result)) continue;
            var support = view.Supports.FirstOrDefault(x => x.Enabled);
            if (support != null && (view.Health <= 44 || view.Sanity <= 20)) { Execute(session, BattleCommand.Support(support.Definition.Id, support.Targets.FirstOrDefault(x => x.Enabled)?.Id), result); result.SupportUses++; continue; }
            var consumable = view.SupplyActions.FirstOrDefault(x => x.Enabled && (x.Id == "i10" && view.Health <= 48 || x.Id == "i11" && Incoming(view, DamageChannel.Sanity) > view.Ward || x.Id == "i14" && view.CanAdvanceObjective));
            if (consumable != null) { Execute(session, BattleCommand.Consume(consumable.Id, consumable.Targets.FirstOrDefault(x => x.Enabled)?.Id), result); result.ConsumableUses++; continue; }
            var tool = view.Tools.FirstOrDefault(x => x.Enabled && x.Targets.Any(t => t.Enabled));
            if (tool != null && view.Enemies.Any(x => x.Health > 0 && !x.IntentConfirmed)) { Execute(session, BattleCommand.Investigate(tool.Definition.Id, tool.Targets.First(x => x.Enabled).Id), result); result.ToolUses++; continue; }
            var options = view.Hand.Where(x => x.Enabled && !x.IsEncounter).SelectMany(card => card.Targets.Count == 0
                ? new[] { new Option { Card = card, Score = Score(view, card, null) } }
                : card.Targets.Where(t => t.Enabled).Select(t => new Option { Card = card, Target = t.Id, Score = Score(view, card, t.Id) }))
                .OrderByDescending(x => x.Score).ThenBy(x => x.Card.InstanceId).ToList();
            if (options.Count == 0 || options[0].Score <= 0) break;
            Execute(session, BattleCommand.Play(options[0].Card.InstanceId, options[0].Target), result);
        }
    }

    private static void Execute(BattleSession session, BattleCommand command, Trial result)
    {
        var beforeView = session.View;
        var played = command.kind == CommandKind.PlayCard ? beforeView.Hand.FirstOrDefault(x => x.InstanceId == command.cardInstanceId)?.Definition.Id : null;
        var before = beforeView.Sanity; Must(session.Execute(command));
        if (played != null) { result.CardUses.TryGetValue(played, out var uses); result.CardUses[played] = uses + 1; }
        if (command.kind != CommandKind.EndTurn) result.ActiveSanity += Math.Max(0, before - session.View.Sanity);
    }


    private static bool BossAction(BattleSession s, Trial result)
    {
        var v = s.View; string cardId = null, target = null;
        if (v.BossKind == BossEncounterKind.None)
        {
            var encounter = v.Hand.FirstOrDefault(x => x.IsEncounter && x.Enabled);
            if (encounter != null && v.CanAdvanceObjective) { Execute(s, BattleCommand.Play(encounter.InstanceId), result); return true; }
            return false;
        }
        if (v.BossKind == BossEncounterKind.Choirmaster && v.BossStage == 1)
        {
            if (v.Hand.Any(x => x.Definition.Id == "E02-01" && x.Enabled)) cardId = "E02-01";
            else
            {
                var pairs = Regex.Matches(v.BossStatus ?? "", @"(潮|骨|息)匣在(潮|骨|息)匣前").Cast<Match>().ToArray();
                var labels = new Dictionary<string, string> { ["潮"] = "choir-a", ["骨"] = "choir-b", ["息"] = "choir-c" };
                var open = v.EncounterTargets.Where(x => x.Detail == "未关闭").Select(x => x.Id).ToArray();
                if (pairs.Length == 2) { target = open.FirstOrDefault(id => !pairs.Any(m => labels[m.Groups[2].Value] == id && open.Contains(labels[m.Groups[1].Value]))); cardId = "E02-02"; }
            }
        }
        else if (v.BossKind == BossEncounterKind.Surveyor) { target = v.EncounterTargets.Where(x => x.Detail.StartsWith("?")).Select(x => x.Id).FirstOrDefault(id => ProvenSafe(v, id)); if (target != null) cardId = "E03-01"; }
        else if (v.BossKind == BossEncounterKind.FlagBearer && v.BossStage < 3)
        {
            var wanted = v.BossStage == 1 ? new[] { "R1:positive", "R2:reverse" } : v.ObjectiveProgress == 0 ? new[] { "R1:off", "R2:reverse" } : v.ObjectiveProgress == 1 ? new[] { "R1:positive", "R2:reverse" } : new[] { "R1:positive", "R2:off" };
            target = wanted.FirstOrDefault(id => v.EncounterTargets.Any(t => t.Id == id && t.Detail != "当前极性"));
            if (target != null) cardId = "E04-01"; else if (v.BossStage == 2) { cardId = "E04-02"; target = "lock-" + new[] { "I", "II", "III" }[v.ObjectiveProgress]; }
        }
        else if (v.BossKind == BossEncounterKind.Watcher && !v.Enemies.Skip(1).Any(x => x.Health > 0)) cardId = "E05-01";
        var card = v.Hand.FirstOrDefault(x => x.Definition.Id == cardId && x.Enabled);
        if (card == null || card.Targets.Count > 0 && !card.Targets.Any(x => x.Id == target && x.Enabled)) return false;
        Execute(s, BattleCommand.Play(card.InstanceId, target), result); return true;
    }

    private static bool ProvenSafe(BattleView view, string target)
    {
        var cells = view.EncounterTargets; var possibilities = new List<HashSet<string>>();
        Func<string, string, bool> adjacent = (a, b) => a != b && Math.Abs(a[0] - b[0]) <= 1 && Math.Abs(a[1] - b[1]) <= 1;
        for (var a = 0; a < cells.Count; a++) for (var b = a + 1; b < cells.Count; b++) for (var c = b + 1; c < cells.Count; c++)
        {
            var mines = new HashSet<string>(new[] { cells[a].Id, cells[b].Id, cells[c].Id });
            if (cells.All(cell => cell.Detail.StartsWith("?") || int.TryParse(cell.Detail.Split('\n')[0], out var number) && !mines.Contains(cell.Id) && mines.Count(m => adjacent(m, cell.Id)) == number)) possibilities.Add(mines);
        }
        return possibilities.Count > 0 && possibilities.All(x => !x.Contains(target));
    }

    private static double Score(BattleView view, CardView card, string target)
    {
        var spec = card.Definition; CardCatalog.TryNumber(spec, out var number);
        var living = view.Enemies.Where(x => x.Health > 0 && x.Targetable).ToList();
        var enemy = living.FirstOrDefault(x => target == x.Id || target == "enemy:" + x.Id);
        var amount = spec.Effects.Sum(x => x.Amount);
        double value;
        switch (spec.Category)
        {
            case CardCategory.Armed:
                Func<EnemyView,int,double> hit = (e, damage) => Math.Min(e.Health, damage) + (e.Health <= damage ? 8 : 0);
                var damage = number == 2 ? enemy?.IntentConfirmed == true ? 9 : 5 : number == 4 ? 12 : number == 8 ? 8 :
                    number == 9 ? 4 : number == 12 ? view.Block >= 6 ? 7 : 5 : number == 14 ? enemy?.IntentConfirmed == true ? 15 : 10 :
                    number == 16 ? 3 : number == 17 ? 9 : amount;
                if (number == 6 || number == 13) value = living.Sum(x => hit(x, damage));
                else if (number == 9 && target != null) value = living.Where(x => target.Contains(x.Id)).Sum(x => hit(x, damage));
                else value = enemy == null ? 0 : hit(enemy, damage);
                break;
            case CardCategory.Defense:
                var health = spec.Description.Contains("格挡") ? Math.Min(Math.Max(0, Incoming(view, DamageChannel.Health) - view.Block), amount) * 1.2 : 0;
                var sanity = spec.Description.Contains("理智") ? Math.Min(Math.Max(0, Incoming(view, DamageChannel.Sanity) - view.Ward), amount) * 1.5 : 0;
                var healing = spec.Description.Contains("恢复") && spec.Description.Contains("生命") ? Math.Min(GameBalance.MaxHealth - view.Health, amount) : 0;
                value = health + sanity + healing;
                break;
            case CardCategory.Investigation:
                value = enemy != null ? enemy.IntentConfirmed ? 1 : 7 : spec.HasFallback ? 2 : 0;
                break;
            case CardCategory.Intervention:
                value = enemy != null ? 3 + Math.Min(enemy.Health, amount) : spec.HasFallback ? 2 : 1 + amount;
                break;
            default:
                value = spec.BossIds.Count == 0 || spec.BossIds.Any(id => BossMatches(view.BossKind, id)) ? 3 + amount : -1;
                break;
        }
        if (spec.SanityCost >= view.Sanity) return -1;
        return value / Math.Max(1, card.ActionCost) - spec.SanityCost * .7;
    }

    // Unknown intents publish mutually-exclusive candidates; defend the worst one per channel, never their sum.
    private static int Incoming(BattleView view, DamageChannel channel) => view.Enemies.Where(x => x.Health > 0).Sum(x => x.IntentConfirmed ? Confirmed(x.Intent, channel) : WorstCandidate(x.Intent, channel));
    private static int Confirmed(string text, DamageChannel channel)
    {
        var label = channel == DamageChannel.Health ? "生命伤害" : "理智冲击";
        return Regex.Matches(text ?? "", @"(\d+)\s*(?:点)?\s*" + label).Cast<Match>().Select(x => int.Parse(x.Groups[1].Value)).DefaultIfEmpty().Max();
    }
    private static int WorstCandidate(string text, DamageChannel channel) => Confirmed(text, channel);

    private static BattleDefinition Clone(BattleDefinition source, IEnumerable<CardSpec> deck, int? health = null, int? sanity = null)
    {
        var toolSpecs = Tools.Select(VoyageEncounterCatalog.ResolveTool).ToArray();
        var consumables = source.Objective == ObjectiveKind.Seal ? new[] { "i10", "i14" } : new[] { "i10", "i11" };
        return new BattleDefinition(source.Id, deck, source.Enemies, toolSpecs, source.Objective, source.ObjectiveTarget, source.Health, source.Sanity, source.Actions, source.DrawCount,
            Supports.Select(SupportCatalog.CrewMember), source.Archive, health ?? source.InitialHealth, sanity ?? source.InitialSanity, new BattleLoadout(consumables, Equipment));
    }

    private static IReadOnlyList<CardSpec> BalancedDeck(int act, string region)
    {
        var legal = LegalLearnedPool(act, Regions.Contains(region) ? region : Regions.First(x => RegionAct(x) == act)).ToList();
        var preferred = act == 1 ? new[] { 1, 2, 12, 19, 20, 21, 22, 37, 39, 41, 55, 71 } :
            act == 2 ? new[] { 1, 2, 14, 16, 19, 20, 21, 39, 41, 45, 50, 55 } :
            new[] { 2, 4, 14, 16, 20, 21, 34, 41, 50, 55, 71, 76 };
        var result = preferred.Select(n => CardCatalog.All[n - 1]).Where(legal.Contains).Distinct().ToList();
        foreach (var card in legal.OrderBy(x => x.Category).ThenBy(x => x.Id, StringComparer.Ordinal))
            if (result.Count < 12 && !result.Contains(card)) result.Add(card);
        if (result.Count != 12) throw new InvalidOperationException($"Act {act} legal learned pool cannot form a 12-card deck.");
        return result;
    }

    private static IReadOnlyList<CardSpec> RandomLearnedDeck(string encounter, int act)
    {
        var region = Regions.FirstOrDefault(x => encounter.Contains(x)) ?? Regions.First(x => RegionAct(x) == act);
        var legal = LegalLearnedPool(act, region).Where(x => x.Category != CardCategory.Forbidden).ToList();
        var random = new System.Random(unchecked((int)StableHash(encounter + ":learned")));
        return legal.OrderBy(_ => random.Next()).Take(12).ToArray();
    }

    private static IEnumerable<CardSpec> LegalLearnedPool(int act, string region)
    {
        var result = new Dictionary<string,CardSpec>(StringComparer.Ordinal);
        foreach (var card in CardCatalog.Starter) result[card.Id] = card;
        foreach (var candidateRegion in Regions.Where(x => RegionAct(x) <= act).Concat(new[] { region }).Distinct())
        for (uint seed = 0; seed < 64; seed++)
            foreach (var card in VoyageSession.CreateDebug(seed, candidateRegion, RunNodeKind.Reward).RewardCards)
                if (card.BossIds.Count == 0) result[card.Id] = card;
        return result.Values;
    }

    private static Campaign RunCampaign(uint seed, bool risky)
    {
        var voyage = VoyageSession.Create(seed); var result = new Campaign { seed = seed, policy = risky ? "risky" : "safe" };
        for (var guard = 0; guard < 500 && voyage.Phase != VoyagePhase.Completed && voyage.Phase != VoyagePhase.Failed; guard++)
        {
            bool changed;
            switch (voyage.Phase)
            {
                case VoyagePhase.ChoosingRoute:
                    var ordered = voyage.Choices.OrderBy(x => x.Perilous == risky ? 0 : 1).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
                    var selected = ordered.FirstOrDefault();
                    changed = selected != null && voyage.Select(selected.Id) && voyage.SetSail();
                    if (changed) result.route.Add(selected.Id);
                    break;
                case VoyagePhase.Sailing: changed = voyage.Arrive(); break;
                case VoyagePhase.Arrived:
                    if (voyage.Current.Kind == RunNodeKind.Battle || voyage.Current.Kind == RunNodeKind.Boss) changed = voyage.BeginEncounter();
                    else if (voyage.CanContinue) changed = voyage.Continue();
                    else
                    {
                        var choices = voyage.GetNodeChoices().Concat(voyage.GetNodeServices()).Concat(voyage.GetSupportChoices()).Where(x => x.Enabled).GroupBy(x => x.Id).Select(x => x.First()).ToList();
                        var preferred = risky ? new[] { "rest", "repair", "calm", "risk:watch", "salvage", "dive", "depart", "leave" } : new[] { "rest", "repair", "calm", "seal", "observe", "listen", "depart", "leave", "briefing", "routes" };
                        var choice = preferred.Select(id => choices.FirstOrDefault(x => x.Id == id || x.Id.StartsWith(id + ":", StringComparison.Ordinal))).FirstOrDefault(x => x != null) ?? choices.FirstOrDefault();
                        changed = choice != null && (voyage.ChooseNode(choice.Id) || voyage.ManageSupport(choice.Id));
                        if (changed && voyage.CanContinue) changed = voyage.Continue();
                    }
                    break;
                case VoyagePhase.Encounter:
                    var scenario = VoyageEncounterCatalog.Resolve(voyage.Current.EncounterId);
                    var battle = scenario.CreateSession(voyage.Current.BattleSeed, voyage: voyage);
                    var trial = RunExisting(battle);
                    changed = voyage.ResolveEncounter(trial.Win, battle.View.Health, battle.View.Sanity, battle.View.SpentConsumables, battle.View.Watched, battle.View.Evidence); break;
                case VoyagePhase.Reward:
                    var reward = voyage.RewardCards.FirstOrDefault(x => x.BossIds.Count == 0 && x.Category != CardCategory.Forbidden);
                    var replacement = voyage.DeckIds.FirstOrDefault(id => voyage.LearnedCardIds.Contains(id) && reward != null && CardCatalog.Get(id).Category == reward.Category);
                    changed = voyage.ClaimReward(reward?.Id, replacement);
                    if (changed) result.rewards.Add((reward?.Id ?? "supplies") + (replacement == null ? "" : "@" + replacement));
                    break;
                case VoyagePhase.ActComplete:
                    result.acts.Add(new ActEnd { act = voyage.Act, region = voyage.RegionId, health = voyage.Health, sanity = voyage.Sanity, supplies = voyage.Supplies });
                    changed = voyage.ChooseNextAct(voyage.Act == 1 ? (risky ? "fleet" : "bones") : "abyss"); break;
                default: changed = false; break;
            }
            if (!changed) throw new InvalidOperationException($"Campaign {result.policy} stalled in {voyage.Phase} at {voyage.Current.Id}.");
        }
        if (result.acts.All(x => x.act != voyage.Act)) result.acts.Add(new ActEnd { act = voyage.Act, region = voyage.RegionId, health = voyage.Health, sanity = voyage.Sanity, supplies = voyage.Supplies });
        result.outcome = voyage.Phase.ToString(); result.finalDeck = voyage.DeckIds.ToArray(); result.finalTools = voyage.ToolIds.ToArray(); result.finalEquipment = voyage.Equipment.ToArray(); result.finalItems = voyage.Inventory.ToArray(); result.finalHealth = voyage.Health; result.finalSanity = voyage.Sanity; result.finalSupplies = voyage.Supplies;
        return result;
    }

    private static Trial RunExisting(BattleSession session)
    {
        var result = new Trial { Session = session }; var start = session.View.Health;
        while (session.View.Phase == BattlePhase.PlayerTurn && session.View.Turn <= TurnLimit)
        {
            Act(session, result); if (session.View.Phase != BattlePhase.PlayerTurn) break;
            var before = session.View.Sanity;
            Must(session.Execute(BattleCommand.EndTurn()));
            result.PassiveSanity += Math.Max(0, before - session.View.Sanity);
        }
        result.Timeout = session.View.Phase == BattlePhase.PlayerTurn; result.Win = session.View.Phase == BattlePhase.Victory; result.Turns = session.View.Turn; result.HealthLoss = start - session.View.Health; return result;
    }

    private static void SetCriterion(Row row, int act)
    {
        row.criterion = act == 1 && row.cohort == "starter" ? ">=90%" : act == 2 && row.cohort == "starter" ? "85-95%" :
            act == 2 && row.cohort == "current-act" ? ">=98%, median health loss 3-7, passive sanity loss 0-2" :
            act == 3 && row.cohort == "starter" ? "70-85%" : "reported; no target";
        row.criterionMet = row.criterion == "reported; no target" || act == 1 && row.cohort == "starter" && row.winPercent >= 90 ||
            act == 2 && row.cohort == "starter" && row.winPercent >= 85 && row.winPercent <= 95 ||
            act == 2 && row.cohort == "current-act" && row.winPercent >= 98 && row.medianHealthLoss >= 3 && row.medianHealthLoss <= 7 && row.medianPassiveSanityDamage <= 2 ||
            act == 3 && row.cohort == "starter" && row.winPercent >= 70 && row.winPercent <= 85;
    }

    private static void BuildAggregates(Report report)
    {
        foreach (var act in new[] { 1, 2, 3 }) foreach (var cohort in new[] { "starter", "current-act", "random-learned" })
        {
            var rows = report.encounters.Where(x => !x.boss && RegionAct(x.region) == act && x.cohort == cohort).ToList(); if (rows.Count == 0) continue;
            var samples = rows.SelectMany(x => x.samples).ToList();
            var aggregate = new Aggregate { act = act, cohort = cohort, trials = samples.Count, wins = samples.Count(x => x.Win), medianHealthLoss = Percentile(samples.Select(x => x.HealthLoss), .5), medianPassiveSanityDamage = Percentile(samples.Select(x => x.PassiveSanity), .5), medianActiveSanityPayment = Percentile(samples.Select(x => x.ActiveSanity), .5) };
            aggregate.winPercent = 100.0 * aggregate.wins / aggregate.trials;
            aggregate.criterion = act == 1 && cohort == "starter" ? ">=90%" : act == 2 && cohort == "starter" ? "85-95%" : act == 2 && cohort == "current-act" ? ">=98%, median health loss 3-7, passive sanity loss 0-2; active payments reported separately" : act == 3 && cohort == "starter" ? "70-85%" : "reported; no target";
            aggregate.criterionMet = aggregate.criterion == "reported; no target" || act == 1 && cohort == "starter" && aggregate.winPercent >= 90 || act == 2 && cohort == "starter" && aggregate.winPercent >= 85 && aggregate.winPercent <= 95 || act == 2 && cohort == "current-act" && aggregate.winPercent >= 98 && aggregate.medianHealthLoss >= 3 && aggregate.medianHealthLoss <= 7 && aggregate.medianPassiveSanityDamage >= 0 && aggregate.medianPassiveSanityDamage <= 2 || act == 3 && cohort == "starter" && aggregate.winPercent >= 70 && aggregate.winPercent <= 85;
            report.pooledActs.Add(aggregate);
        }
    }

    private static BossEncounterKind BossKind(BattleScenario scenario) => scenario.rules is BossRuleModule rule ? rule.bossKind : BossEncounterKind.None;
    private static string Region(string id, string group)
    {
        var region = Regions.FirstOrDefault(x => id.Contains("-" + x + "-"));
        if (region != null) return region;
        if (id == "m05" || id == "drowned-bell") return "abyss";
        if (id == "fog-visitors" || id == "storm-gauntlet" || id == "hollow-beacon") return "fog";
        return "bones";
    }
    private static int RegionAct(string region) => region == "fog" ? 1 : region == "abyss" ? 3 : 2;
    private static bool BossMatches(BossEncounterKind kind, string id) => string.Equals(kind.ToString(), id, StringComparison.OrdinalIgnoreCase) || id == "M02" && kind == BossEncounterKind.Choirmaster || id == "M03" && kind == BossEncounterKind.Surveyor || id == "M04" && kind == BossEncounterKind.FlagBearer || id == "M05" && kind == BossEncounterKind.Watcher;
    private static uint StableHash(string value) { unchecked { uint hash = 2166136261; foreach (var c in value) { hash ^= c; hash *= 16777619; } return hash; } }
    private static double Percentile(IEnumerable<int> source, double percentile) { var values = source.OrderBy(x => x).ToArray(); return values.Length == 0 ? double.NaN : values[Math.Max(0, (int)Math.Ceiling(values.Length * percentile) - 1)]; }
    private static double Median(IEnumerable<double> source) { var values = source.OrderBy(x => x).ToArray(); return values.Length == 0 ? double.NaN : values[(values.Length - 1) / 2]; }
    private static void Must(CommandResult result) { if (!result.Success) throw new InvalidOperationException(result.Error); }
}
