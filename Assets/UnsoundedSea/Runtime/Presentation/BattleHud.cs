using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattleHud : MonoBehaviour
    {
        [SerializeField] private EncounterDefinition[] encounters;
        [SerializeField] private int initialEncounter = 1;
        [SerializeField] private int seed = 20261016;
        [SerializeField] private FontAsset interfaceFont;
        [SerializeField] private StyleSheet interfaceStyle;
        [SerializeField] private StyleSheet menuStyle;
        [SerializeField] private string menuScene = "MainMenu";
        private BattleSession session;
        private BattleDefinition definition;
        private VisualElement root;
        private string selectedEnemy;
        private string message;
        private int encounterIndex;
        private bool journalOpen;
        private bool menuOpen;
        private bool settingsOpen;
        private int[] previousHand = Array.Empty<int>();
        private readonly HashSet<string> revealed = new HashSet<string>();
        public BattleView CurrentView => session?.View;
        public int RenderedFrames { get; private set; }

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            try
            {
                if (interfaceFont == null || interfaceStyle == null)
                    throw new InvalidOperationException("Assign the project HUD font and stylesheet.");
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(interfaceFont);
                // Menu sheet first so the HUD sheet wins equal-specificity label rules.
                if (menuStyle != null && !root.styleSheets.Contains(menuStyle)) root.styleSheets.Add(menuStyle);
                if (!root.styleSheets.Contains(interfaceStyle)) root.styleSheets.Add(interfaceStyle);
                root.AddToClassList("battle-hud");
                root.RegisterCallback<GeometryChangedEvent>(Resize);
                root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
                StartEncounter(initialEncounter);
            }
            catch (Exception exception) { root.Add(Text("界面加载失败：" + exception.Message, "error")); Debug.LogException(exception); }
        }
        private void Update() { RenderedFrames++; }
        private void OnDisable()
        {
            root?.UnregisterCallback<GeometryChangedEvent>(Resize);
            root?.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            root?.Clear();
        }
        private void Resize(GeometryChangedEvent evt) { root.EnableInClassList("compact", evt.newRect.width < 1200 || evt.newRect.height < 740); }
        private void OnKey(KeyDownEvent evt)
        {
            if (session == null || settingsOpen) return;
            if (evt.keyCode == KeyCode.Escape && menuOpen) ToggleMenu();
            else if (menuOpen) return;
            else if (evt.keyCode == KeyCode.J) ToggleJournal();
            else if (evt.keyCode == KeyCode.Escape && journalOpen) ToggleJournal();
            else if (evt.keyCode == KeyCode.Escape) ToggleMenu();
            else return;
            evt.StopPropagation();
        }
        private void ToggleJournal()
        {
            journalOpen = !journalOpen;
            Refresh(journalOpen ? "journal-close" : "journal-toggle");
        }

        private void ToggleMenu()
        {
            menuOpen = !menuOpen; settingsOpen = false;
            Refresh(menuOpen ? "pause-resume" : "menu-toggle");
        }

        public void StartEncounter(int index)
        {
            if (encounters == null || index < 0 || index >= encounters.Length || encounters[index] == null)
                throw new InvalidOperationException("Assign encounter assets on BattleHud.");
            encounterIndex = index;
            definition = encounters[index].Build();
            session = new BattleSession(definition, unchecked((uint)seed));
            selectedEnemy = session.View.Enemies[0].Id;
            message = "";
            journalOpen = false;
            menuOpen = false; settingsOpen = false;
            previousHand = Array.Empty<int>();
            revealed.Clear();
            Refresh();
        }
        public CommandResult Send(BattleCommand command)
        {
            var result = session.Execute(command);
            message = result.Success ? "" : result.Error;
            Refresh();
            return result;
        }

        private static VisualElement Element(string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            return element;
        }
        private static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
        private static Button Button(string name, string text, string className, Action action, bool enabled = true)
        {
            var button = new Button(action) { name = name, text = text };
            button.AddToClassList(className);
            button.SetEnabled(enabled);
            return button;
        }
        private static VisualElement Pips(int filled, int total, string className)
        {
            var row = Element("pips"); row.AddToClassList(className);
            for (var i = 0; i < total; i++)
            {
                var pip = Element("pip");
                pip.EnableInClassList("filled", i < filled);
                row.Add(pip);
            }
            return row;
        }
        private static VisualElement Meter(string title, int value, int maximum, string className)
        {
            var meter = Element("vital"); meter.AddToClassList(className);
            var line = Element("vital-line");
            line.Add(Text(title, "vital-title"));
            var amount = Element("vital-amount");
            amount.Add(Text(value.ToString(), "vital-value"));
            amount.Add(Text("/ " + maximum, "vital-max"));
            line.Add(amount); meter.Add(line);
            var track = Element("vital-track");
            var fill = Element("vital-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01((float)value / maximum) * 100);
            track.Add(fill);
            for (var tick = 10; tick < maximum; tick += 10)
            {
                var mark = Element("vital-tick");
                mark.style.left = Length.Percent(100f * tick / maximum);
                track.Add(mark);
            }
            meter.Add(track);
            return meter;
        }
        private static string LevelName(KnowledgeLevel level) =>
            level == KnowledgeLevel.Verified ? "已验证" : level == KnowledgeLevel.Observed ? "已观察" : level == KnowledgeLevel.Rumored ? "传闻" : "未知";
        private static VisualElement LevelTag(KnowledgeLevel level)
        {
            var tag = Text(LevelName(level), "level-tag");
            tag.AddToClassList("level-" + level.ToString().ToLowerInvariant());
            return tag;
        }
        private static string ToolQuestion(InvestigationKind kind) =>
            kind == InvestigationKind.VisibleAction ? "确认目标的可见动作" :
            kind == InvestigationKind.SoundSource ? "判断声音来自何处" : "判断威胁肉体还是理智";

        // Phase transitions are internal bookkeeping; the HUD shows only what happened to the investigator.
        private static IEnumerable<(string turn, string text)> PlayerLog(BattleView view) =>
            view.Log.Where(entry => !entry.Contains("] 阶段：")).Select(entry =>
            {
                var close = entry.IndexOf("] ", StringComparison.Ordinal);
                return entry.StartsWith("[") && close > 0 ? ("第 " + entry.Substring(1, close - 1) + " 回合", entry.Substring(close + 2)) : ("", entry);
            });

        private void Refresh(string focusOverride = null)
        {
            if (root == null || session == null) return;
            var view = session.View;
            var focused = focusOverride ?? (root.panel?.focusController.focusedElement as VisualElement)?.name;
            var handOffset = root.Q<ScrollView>("hand")?.scrollOffset ?? Vector2.zero;
            var focusIndex = -1;
            if (focused != null && focused.StartsWith("card-") && int.TryParse(focused.Substring(5), out var focusedCard))
                focusIndex = Array.IndexOf(previousHand, focusedCard);
            var playing = view.Phase == BattlePhase.PlayerTurn;
            var concluded = view.Phase == BattlePhase.Victory || view.Phase == BattlePhase.Defeat;
            if (view.Enemies.All(e => e.Id != selectedEnemy || e.Health == 0))
                selectedEnemy = (view.Enemies.FirstOrDefault(e => e.Health > 0) ?? view.Enemies[0]).Id;
            root.Clear();
            root.EnableInClassList("concluded", concluded);
            root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion);

            // Upper left: the investigator's two separate survival resources.
            var vitals = Element("vitals");
            vitals.Add(Meter("生命", view.Health, definition.Health, "health"));
            vitals.Add(Meter("理智", view.Sanity, definition.Sanity, "sanity"));
            root.Add(vitals);

            // Upper centre: encounter identity, turn and objective.
            var heading = Element("encounter-heading");
            heading.Add(Text($"第 {view.Turn:00} 回合", "turn-label"));
            heading.Add(Text(view.Enemies.Count == 1 ? view.Enemies[0].Name : "未知海域的遭遇", "encounter-name"));
            var rule = Element("heading-rule");
            rule.Add(Element("rule-line")); rule.Add(Element("rule-mark")); rule.Add(Element("rule-line"));
            heading.Add(rule);
            if (definition.Objective == ObjectiveKind.Seal)
            {
                var seal = Element("objective-row");
                seal.Add(Text("目标 · 封住入口", "objective"));
                seal.Add(Pips(view.ObjectiveProgress, view.ObjectiveTarget, "seal-pips"));
                seal.Add(Text($"{view.ObjectiveProgress} / {view.ObjectiveTarget}", "objective-count"));
                heading.Add(seal);
            }
            else heading.Add(Text("目标 · " + view.Objective, "objective"));
            root.Add(heading);

            var toggle = Button("journal-toggle", "", "journal-toggle", ToggleJournal);
            toggle.EnableInClassList("open", journalOpen);
            toggle.Add(Text(journalOpen ? "收起档案" : "调查档案", "toggle-title"));
            toggle.Add(Text($"{view.Evidence.Count} 条记录 · J", "toggle-meta"));
            root.Add(toggle);
            var menuToggle = Button("menu-toggle", "", "menu-toggle", ToggleMenu);
            menuToggle.Add(Text("菜单", "toggle-title"));
            menuToggle.Add(Text("Esc", "toggle-meta"));
            root.Add(menuToggle);

            // Target nameplates float over the stage; selection is shown by brackets and a text label.
            var targetBar = Element("targets");
            foreach (var enemy in view.Enemies)
            {
                var id = enemy.Id;
                var alive = enemy.Health > 0;
                var target = Button("enemy-" + id, "", "target", () => { selectedEnemy = id; Refresh(); }, alive && !concluded);
                var isSelected = selectedEnemy == id;
                target.EnableInClassList("selected", isSelected);
                target.EnableInClassList("defeated", !alive);
                target.Add(Text(!alive ? "已击退" : isSelected ? "当前目标" : "点击选为目标", "target-state"));
                target.Add(Text(enemy.Name, "target-name"));
                var hp = Element("target-health");
                var track = Element("target-track"); var fill = Element("target-fill");
                fill.style.width = Length.Percent(100f * enemy.Health / enemy.MaxHealth);
                track.Add(fill); hp.Add(track);
                hp.Add(Text($"{enemy.Health} / {enemy.MaxHealth}", "target-value"));
                target.Add(hp);
                foreach (var corner in new[] { "tl", "tr", "bl", "br" }) target.Add(Element("bracket " + corner));
                targetBar.Add(target);
            }
            root.Add(targetBar);

            // Left edge: only what the investigator can currently see or has confirmed.
            var selected = view.Enemies.FirstOrDefault(e => e.Id == selectedEnemy);
            if (selected != null)
            {
                var intel = Element("intel");
                var header = Element("intel-header");
                header.Add(Text("现场观察", "section-label"));
                header.Add(Text(selected.Name, "intel-subject"));
                intel.Add(header);
                var prefix = $"{selected.Id}/{view.Turn}/";
                var findings = view.Evidence.Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                var clueRow = Element("intel-row");
                var clueHead = Element("intel-row-head");
                clueHead.Add(Text("征兆", "field-label"));
                clueHead.Add(LevelTag(KnowledgeLevel.Observed));
                clueRow.Add(clueHead);
                clueRow.Add(Text(selected.Clue, "clue"));
                if (findings.Any(e => e.Key.EndsWith("/clue", StringComparison.Ordinal)))
                    clueRow.Add(Text("已记入档案", "field-note"));
                intel.Add(clueRow);

                var intent = Element("intent");
                var confirmed = selected.IntentConfirmed;
                intent.EnableInClassList("confirmed", confirmed);
                var intentHead = Element("intel-row-head");
                intentHead.Add(Text("本回合意图", "field-label"));
                intentHead.Add(Text(confirmed ? "已确认" : "未确认", confirmed ? "status-confirmed" : "status-unconfirmed"));
                intent.Add(intentHead);
                if (confirmed || selected.Health == 0) intent.Add(Text(selected.Intent, "intent-text"));
                else
                {
                    intent.Add(Text("可能为以下之一：", "field-note"));
                    foreach (var option in selected.Intent.Split('/'))
                        if (option.Trim().Length > 0) intent.Add(Text("？ " + option.Trim(), "intent-option"));
                }
                var key = $"{prefix}intent";
                if (confirmed && revealed.Add(key))
                {
                    intent.AddToClassList("revealing");
                    intent.schedule.Execute(() => intent.RemoveFromClassList("revealing")).StartingIn(16);
                }
                intel.Add(intent);

                var partial = findings.Where(e => !e.Key.EndsWith("/clue", StringComparison.Ordinal) && !e.Key.EndsWith("/VisibleAction", StringComparison.Ordinal)).ToList();
                if (partial.Count > 0)
                {
                    var found = Element("intel-row");
                    found.AddToClassList("findings");
                    found.Add(Text("调查所得", "field-label"));
                    foreach (var entry in partial)
                    {
                        var item = Element("finding");
                        item.Add(LevelTag(entry.Level));
                        item.Add(Text(entry.Text, "finding-text"));
                        found.Add(item);
                    }
                    intel.Add(found);
                }
                root.Add(intel);
            }

            // Right edge: the two equipped instruments, each with uses and explicit price.
            var tools = Element("tools");
            tools.Add(Text("调查工具", "section-label"));
            foreach (var tool in view.Tools)
            {
                var id = tool.Definition.Id;
                var spent = tool.RemainingUses == 0;
                var button = Button("tool-" + id, "", "tool", () => Send(BattleCommand.Investigate(id, selectedEnemy)), !spent && playing);
                button.EnableInClassList("spent", spent);
                var head = Element("tool-head");
                head.Add(Text(tool.Definition.Name, "tool-name"));
                head.Add(Pips(tool.RemainingUses, tool.Definition.Uses, "use-pips"));
                button.Add(head);
                button.Add(Text(ToolQuestion(tool.Definition.Kind), "tool-question"));
                var foot = Element("tool-foot");
                var costly = tool.Definition.SanityCost > 0;
                var cost = Text(costly ? $"代价 · 支付 {tool.Definition.SanityCost} 理智" : "代价 · 不消耗行动", "tool-cost");
                cost.EnableInClassList("price", costly);
                foot.Add(cost);
                foot.Add(Text(spent ? "已用尽" : $"剩余 {tool.RemainingUses} 次", spent ? "tool-spent" : "tool-uses"));
                button.Add(foot);
                button.tooltip = $"{tool.Definition.Name}：{ToolQuestion(tool.Definition.Kind)}。对象为当前目标。";
                tools.Add(button);
            }
            root.Add(tools);

            if (journalOpen)
            {
                var journal = Element("journal");
                var top = Element("journal-head");
                var titles = Element("journal-titles");
                titles.Add(Text("调查档案", "journal-title"));
                titles.Add(Text($"截至第 {view.Turn:00} 回合 · 只记录你确实知道的事", "journal-subtitle"));
                top.Add(titles);
                top.Add(Button("journal-close", "收起", "journal-close", ToggleJournal));
                journal.Add(top);
                var entries = new ScrollView { name = "journal-entries" };
                entries.AddToClassList("journal-entries");
                entries.Add(Text("证据", "journal-section"));
                if (view.Evidence.Count == 0) entries.Add(Text("尚无证据。使用调查工具或调查类卡牌后会记录在此。", "journal-empty"));
                foreach (var evidence in view.Evidence.OrderByDescending(e => e.Level))
                {
                    var item = Element("evidence");
                    var line = Element("evidence-head");
                    line.Add(LevelTag(evidence.Level));
                    line.Add(Text("来源：" + evidence.Source, "evidence-source"));
                    item.Add(line);
                    item.Add(Text(evidence.Text, "evidence-text"));
                    entries.Add(item);
                }
                entries.Add(Text("航行日志", "journal-section"));
                foreach (var (turn, text) in PlayerLog(view))
                {
                    var line = Element("log-entry");
                    line.Add(Text(turn, "log-turn")); line.Add(Text(text, "log-text"));
                    entries.Add(line);
                }
                journal.Add(entries); root.Add(journal);
                journal.schedule.Execute(() => entries.scrollOffset = Vector2.zero);
            }

            // Lower left: action budget and the two separate protections.
            var player = Element("player-status");
            var actionLine = Element("action-line");
            var actionText = Element("action-text");
            actionText.Add(Text("行动", "section-label"));
            actionText.Add(Text($"{view.Actions} / {definition.Actions}", "action-value"));
            actionLine.Add(actionText);
            actionLine.Add(Pips(view.Actions, Mathf.Max(definition.Actions, view.Actions), "action-pips"));
            player.Add(actionLine);
            var defense = Element("defenses");
            var block = Element("defense"); block.AddToClassList("block"); block.EnableInClassList("active", view.Block > 0);
            block.Add(Text("格挡", "defense-label")); block.Add(Text(view.Block.ToString(), "defense-value")); block.Add(Text("抵挡生命伤害", "defense-note"));
            var ward = Element("defense"); ward.AddToClassList("ward"); ward.EnableInClassList("active", view.Ward > 0);
            ward.Add(Text("理智防护", "defense-label")); ward.Add(Text(view.Ward.ToString(), "defense-value")); ward.Add(Text("抵御理智冲击", "defense-note"));
            defense.Add(block); defense.Add(ward);
            player.Add(defense);
            root.Add(player);

            // Bottom centre: the drawn hand. Larger hands scroll horizontally.
            var hand = new ScrollView(ScrollViewMode.Horizontal) { name = "hand" };
            hand.AddToClassList("hand");
            hand.pickingMode = PickingMode.Ignore;
            hand.contentViewport.pickingMode = PickingMode.Ignore;
            hand.contentContainer.pickingMode = PickingMode.Ignore;
            hand.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            foreach (var card in view.Hand)
            {
                var id = card.InstanceId; var data = card.Definition;
                var shortAction = view.Actions < data.Cost;
                var shortSanity = view.Sanity <= data.SanityCost;
                var button = Button("card-" + id, "", "card", () => Send(BattleCommand.Play(id, selectedEnemy)), playing && !shortAction && !shortSanity);
                button.EnableInClassList("unaffordable", playing && (shortAction || shortSanity));
                button.tooltip = data.Name + "：" + data.Description;
                var top = Element("card-top");
                top.Add(Text(data.Cost.ToString(), "card-cost"));
                top.Add(Text("行动", "card-cost-label"));
                button.Add(top);
                button.Add(Text(data.Name, "card-name"));
                button.Add(Element("card-rule"));
                button.Add(Text(data.Description, "card-description"));
                if (data.SanityCost > 0) button.Add(Text($"代价 · 主动支付 {data.SanityCost} 理智", "card-price"));
                if (data.Exhaust) button.Add(Text("本场限用一次", "card-exhaust"));
                if (playing && (shortAction || shortSanity)) button.Add(Text(shortAction ? "行动不足" : "理智不足", "card-blocked"));
                hand.Add(button);
            }
            root.Add(hand);
            var piles = Element("piles");
            foreach (var (label, count) in new[] { ("抽牌堆", view.DrawCount), ("弃牌", view.DiscardCount), ("消耗", view.ExhaustCount) })
            {
                var pile = Element("pile");
                pile.Add(Text(label, "pile-label")); pile.Add(Text(count.ToString(), "pile-value"));
                piles.Add(pile);
            }
            player.Add(piles);

            // Lower right: the fixed encounter action stays separate from the random hand.
            var controls = Element("turn-controls");
            if (definition.Objective == ObjectiveKind.Seal)
            {
                var task = Button("objective-action", "", "task", () => Send(BattleCommand.Task()), view.CanAdvanceObjective);
                var drawnThisTurn = playing && !view.CanAdvanceObjective && view.Actions >= 1;
                task.EnableInClassList("spent", drawnThisTurn);
                var taskHead = Element("task-head");
                taskHead.Add(Text("绘制封线", "task-name"));
                taskHead.Add(Pips(view.ObjectiveProgress, view.ObjectiveTarget, "seal-pips"));
                task.Add(taskHead);
                task.Add(Text(drawnThisTurn ? "本回合已绘制" : playing && view.Actions < 1 ? "行动不足" : "1 行动 · 每回合一次 · 不占手牌", "task-note"));
                controls.Add(task);
            }
            var end = Button("end-turn", "", "end-turn", () => Send(BattleCommand.EndTurn()), playing);
            end.Add(Text("结束回合", "end-title"));
            end.Add(Text("敌方执行已锁定的行动", "end-note"));
            controls.Add(end);
            root.Add(controls);

            var latest = PlayerLog(view).Select(entry => entry.text).LastOrDefault() ?? "";
            var notice = Text(string.IsNullOrEmpty(message) ? latest : "无法执行 · " + message, "notice");
            notice.name = "battle-message";
            notice.EnableInClassList("error", !string.IsNullOrEmpty(message));
            notice.EnableInClassList("hidden", notice.text.Length == 0);
            root.Add(notice);

            if (concluded)
            {
                var victory = view.Phase == BattlePhase.Victory;
                var outcome = Element("outcome");
                outcome.AddToClassList(victory ? "victory" : "defeat");
                outcome.Add(Text(victory ? "遭遇结束 · 局部解决" : "航程终止", "outcome-overline"));
                outcome.Add(Text(victory ? "暂离深渊" : view.Health == 0 ? "生命耗尽" : "理智耗尽", "outcome-title"));
                outcome.Add(Text(victory
                    ? definition.Objective == ObjectiveKind.Seal ? "你封住了它进入这里的途径。至少现在如此。" : "这次遭遇已经结束。未知之物仍在海雾深处。"
                    : "打开调查档案，复盘最后的征兆与行动。", "outcome-description"));
                var actions = Element("outcome-actions");
                actions.Add(Button("restart", "重新遭遇", "restart", () => StartEncounter(encounterIndex)));
                if (!journalOpen) actions.Add(Button("outcome-journal", "查看档案", "outcome-journal", ToggleJournal));
                outcome.Add(actions);
                root.Add(outcome);
            }

            if (menuOpen)
            {
                var scrim = new VisualElement { name = "pause-scrim" };
                scrim.AddToClassList("pause-scrim");
                if (settingsOpen)
                    scrim.Add(new SettingsPanel(() => { settingsOpen = false; Refresh("pause-settings"); },
                        () => root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion)));
                else
                {
                    var pause = Element("pause");
                    pause.Add(Text("航行暂停", "pause-overline"));
                    pause.Add(Text($"第 {view.Turn:00} 回合 · 回合制遭遇不会在此期间推进", "pause-note"));
                    pause.Add(Button("pause-resume", "继续调查", "pause-option", ToggleMenu));
                    pause.Add(Button("pause-settings", "设置", "pause-option", () => { settingsOpen = true; Refresh(); }));
                    var canLeave = Application.CanStreamedLevelBeLoaded(menuScene);
                    var leave = Button("pause-title", canLeave ? "返回标题 · 放弃本场遭遇" : "返回标题（未加入构建列表）", "pause-option", () => SceneManager.LoadScene(menuScene), canLeave);
                    leave.AddToClassList("danger");
                    pause.Add(leave);
                    scrim.Add(pause);
                }
                root.Add(scrim);
            }

            var handIds = view.Hand.Select(c => c.InstanceId).ToArray();
            previousHand = handIds;
            if (settingsOpen) return;
            hand.schedule.Execute(() =>
            {
                hand.scrollOffset = handOffset;
                Focusable target = null;
                if (!string.IsNullOrEmpty(focused)) target = root.Q<Button>(focused);
                if ((target == null || !target.canGrabFocus) && focusIndex >= 0 && handIds.Length > 0)
                    target = root.Q<Button>("card-" + handIds[Mathf.Min(focusIndex, handIds.Length - 1)]);
                if ((target == null || !target.canGrabFocus) && focused != null && focused.StartsWith("tool-"))
                    target = root.Query<Button>(className: "tool").Where(b => b.enabledInHierarchy).First();
                if ((target == null || !target.canGrabFocus) && !string.IsNullOrEmpty(focused))
                    target = concluded ? root.Q<Button>("restart") : root.Q<Button>("end-turn");
                if (target != null && target.canGrabFocus) target.Focus();
            });
        }
    }
}
