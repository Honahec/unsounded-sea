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
        private float messageUntil;
        private int encounterIndex;
        private bool journalOpen;
        private bool menuOpen;
        private bool settingsOpen;
        private bool chartOpen, chartWasMenuOpen, chartWasJournalOpen, bookOpen;
        private int selectedCard;
        private readonly EnemyCardSelection enemySelection = new EnemyCardSelection();
        private int inspectedCard;
        private string selectedSupport;
        private int[] previousHand = Array.Empty<int>();
        private string playedCardFeedback;
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
        private void Update()
        {
            RenderedFrames++;
            if (!string.IsNullOrEmpty(message) && Time.unscaledTime >= messageUntil)
            {
                message = ""; Refresh();
            }
        }
        private void OnDisable()
        {
            chartOpen = false; chartWasMenuOpen = false; chartWasJournalOpen = false; bookOpen = false;
            journalOpen = false; menuOpen = false; settingsOpen = false;
            root?.UnregisterCallback<GeometryChangedEvent>(Resize);
            root?.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            root?.Clear();
        }
        private void Resize(GeometryChangedEvent evt) { root.EnableInClassList("compact", evt.newRect.width < 1200 || evt.newRect.height < 740); }
        private void OnKey(KeyDownEvent evt)
        {
            if (inspectedCard != 0)
            {
                if (evt.keyCode == KeyCode.Escape) CloseInspection();
                evt.StopPropagation(); return;
            }
            if (session == null) return;
            if (session.View.PendingChoice != null) { evt.StopPropagation(); return; }
            if (evt.keyCode == KeyCode.B) ToggleActionBook();
            else if (bookOpen)
            {
                if (evt.keyCode != KeyCode.Escape) return;
                ToggleActionBook();
            }
            else if (chartOpen)
            {
                if (evt.keyCode != KeyCode.Escape && evt.keyCode != KeyCode.M) return;
                CloseChart();
            }
            else if (evt.keyCode == KeyCode.M && !settingsOpen) ToggleChart();
            else if (settingsOpen) return;
            else if (evt.keyCode == KeyCode.Escape && menuOpen) ToggleMenu();
            else if (menuOpen) return;
            else if (evt.keyCode == KeyCode.J) ToggleJournal();
            else if (evt.keyCode == KeyCode.Escape && !string.IsNullOrEmpty(selectedSupport)) { selectedSupport = null; Refresh(); }
            else if (evt.keyCode == KeyCode.Escape && selectedCard != 0) { selectedCard = 0; Refresh(); }
            else if (evt.keyCode == KeyCode.Escape && journalOpen) ToggleJournal();
            else if (evt.keyCode == KeyCode.D)
            {
                var focused = root.panel?.focusController.focusedElement as VisualElement;
                if (focused == null || focused.name == null || !focused.name.StartsWith("card-", StringComparison.Ordinal) ||
                    !int.TryParse(focused.name.Substring(5), out var id) || !session.View.Hand.Any(card => card.InstanceId == id)) return;
                inspectedCard = id; Refresh();
            }
            else if (evt.keyCode == KeyCode.Escape) ToggleMenu();
            else return;
            evt.StopPropagation();
        }
        private void ToggleActionBook() { bookOpen = !bookOpen; Refresh(bookOpen ? "battle-action-book-close" : "battle-action-book"); }
        private void ToggleChart()
        {
            if (chartOpen) { CloseChart(); return; }
            chartWasMenuOpen = menuOpen; chartWasJournalOpen = journalOpen;
            chartOpen = true; menuOpen = false; journalOpen = false;
            Refresh("battle-chart-close");
        }
        private void CloseChart()
        {
            chartOpen = false; menuOpen = chartWasMenuOpen; journalOpen = chartWasJournalOpen;
            chartWasMenuOpen = false; chartWasJournalOpen = false;
            Refresh(menuOpen ? "pause-resume" : journalOpen ? "journal-close" : "battle-chart-toggle");
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
            definition = CardDeckPreparation.Apply(encounters[index].Build());
            session = new BattleSession(definition, unchecked((uint)seed));
            selectedEnemy = session.View.Enemies[0].Id;
            message = "";
            journalOpen = false;
            menuOpen = false; settingsOpen = false;
            chartOpen = false; chartWasMenuOpen = false; chartWasJournalOpen = false; bookOpen = false;
            selectedCard = 0;
            previousHand = Array.Empty<int>();
            revealed.Clear();
            Refresh();
        }
        public CommandResult Send(BattleCommand command)
        {
            var before = session.View;
            var playedName = command.kind == CommandKind.PlayCard
                ? before.Hand.FirstOrDefault(card => card.InstanceId == command.cardInstanceId)?.Definition.Name
                : null;
            var result = session.Execute(command);
            var after = session.View;
            if (result.Success && before.Phase != BattlePhase.Victory && after.Phase == BattlePhase.Victory)
                CardDeckPreparation.StoreVerified(after.Evidence);
            if (result.Success && playedName != null) playedCardFeedback = playedName;
            message = result.Success ? "" : result.Error;
            messageUntil = Time.unscaledTime + 2.5f;
            Refresh();
            return result;
        }
        private CommandResult PlaySelectedCard(int cardId, string targetId)
        {
            var selected = selectedCard;
            selectedCard = 0;
            var result = Send(BattleCommand.Play(cardId, targetId));
            if (!result.Success) { selectedCard = selected; Refresh(); }
            return result;
        }

        private bool TryPlaySelectedTarget(string enemyId)
        {
            var card = session.View.Hand.FirstOrDefault(item => item.InstanceId == selectedCard);
            if (card == null) return false;
            if (!enemySelection.Choose(card, enemyId, out var target)) return false;
            if (target == null) Refresh();
            else PlaySelectedCard(card.InstanceId, target);
            return true;
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
            if (!view.Hand.Any(card => card.InstanceId == selectedCard && card.Targets.Count > 0)) selectedCard = 0;
            if (selectedCard == 0) enemySelection.Reset();
            var focused = focusOverride ?? (root.panel?.focusController.focusedElement as VisualElement)?.name;
            var handOffset = root.Q<ScrollView>("hand")?.scrollOffset ?? Vector2.zero;
            var focusIndex = -1;
            if (focused != null && focused.StartsWith("card-") && int.TryParse(focused.Substring(5), out var focusedCard))
                focusIndex = Array.IndexOf(previousHand, focusedCard);
            var playing = view.Phase == BattlePhase.PlayerTurn && !chartOpen && !menuOpen;
            var concluded = view.Phase == BattlePhase.Victory || view.Phase == BattlePhase.Defeat;
            if (view.Enemies.All(e => e.Id != selectedEnemy || e.Health == 0))
                selectedEnemy = (view.Enemies.FirstOrDefault(e => e.Health > 0) ?? view.Enemies[0]).Id;
            root.Clear();
            root.focusable = view.PendingChoice == null && inspectedCard == 0 && !bookOpen && !chartOpen && !menuOpen && !concluded;
            root.EnableInClassList("concluded", concluded);
            root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion);


            // Compact objective stays at the right edge and leaves the stage unobstructed.
            var heading = Element("encounter-heading");
            heading.Add(new SeaUiArt("seal"));
            heading.Add(Text(definition.Objective == ObjectiveKind.Seal ? "封线" : definition.Objective == ObjectiveKind.Survive ? "撤离" : "压制", "objective-label"));
            heading.Add(Text($"第 {view.Turn:00} 回合", "turn-label"));
            heading.Add(Text($"{view.ObjectiveProgress}/{view.ObjectiveTarget}", "objective-count"));
            heading.tooltip = view.Objective;
            root.Add(heading);

            var chartToggle = Button("battle-chart-toggle", "", "chart-toggle", ToggleChart);
            chartToggle.Add(Text("海图 / M", "toggle-title"));
            root.Add(chartToggle);
            var bookToggle = Button("battle-action-book", "", "action-book-toggle", ToggleActionBook);
            bookToggle.Add(Text("行动簿 / B", "toggle-title"));
            root.Add(bookToggle);

            // Target nameplates float over the stage; selection is shown by brackets and a text label.
            var targetBar = Element("targets");
            targetBar.EnableInClassList("split-targets", view.Enemies.Count == 2);
            foreach (var enemy in view.Enemies)
            {
                var id = enemy.Id;
                var alive = enemy.Health > 0;
                var target = Button("enemy-" + id, "", "target", () => { if (selectedCard == 0) { selectedEnemy = id; Refresh(); } else TryPlaySelectedTarget(id); }, alive && !concluded);
                var isSelected = selectedEnemy == id;
                target.EnableInClassList("selected", isSelected);
                target.EnableInClassList("defeated", !alive);
                target.Add(Text(enemy.Name, "target-name"));
                var hp = Element("target-health");
                var track = Element("target-track"); var fill = Element("target-fill");
                fill.style.width = Length.Percent(100f * enemy.Health / enemy.MaxHealth);
                track.Add(fill); hp.Add(track);
                hp.Add(Text($"{enemy.Health} / {enemy.MaxHealth}", "target-value"));
                target.Add(hp);
                target.Add(new EnemyIntentDetails(enemy));
                var group = Element("enemy-target-group");
                group.Add(target);
                var chosen = view.Hand.FirstOrDefault(c => c.InstanceId == selectedCard);
                if (chosen != null && alive && enemySelection.CanChoose(chosen, id))
                {
                    var choice = Button("enemy-choice-" + id, "",
                        "enemy-choice", () => TryPlaySelectedTarget(id), playing && chosen.Enabled);
                    choice.EnableInClassList("selected", enemySelection.IsSelected(chosen, id));
                    group.Add(choice);
                }
                targetBar.Add(group);
            }
            root.Add(targetBar);


            // Right edge: concise investigation tools retain uses, purpose and price.
            var tools = Element("tools");
            foreach (var tool in view.Tools)
            {
                var id = tool.Definition.Id;
                var spent = tool.RemainingUses == 0;
                var button = Button("tool-" + id, "", "tool", () => Send(BattleCommand.Investigate(id, selectedEnemy)), !spent && playing);
                button.EnableInClassList("spent", spent);
                var art = new SeaUiArt(tool.Definition.Kind == InvestigationKind.SoundSource ? "ear" : tool.Definition.Kind == InvestigationKind.VisibleAction ? "lantern" : "eye");
                button.Add(art);
                button.Add(Text(tool.Definition.Name, "tool-name"));
                var foot = Element("tool-foot");
                var costly = tool.Definition.SanityCost > 0;
                if (costly)
                {
                    var cost = Text($"{tool.Definition.SanityCost} 理智", "tool-cost");
                    cost.AddToClassList("price"); foot.Add(cost);
                }
                foot.Add(Text(spent ? "已用尽" : $"×{tool.RemainingUses}", spent ? "tool-spent" : "tool-uses"));
                button.Add(foot);
                button.tooltip = $"{tool.Definition.Name}：{ToolQuestion(tool.Definition.Kind)}。对象为当前目标。";
                tools.Add(button);
            }
            foreach (var support in view.Supports)
            {
                var crew = support;
                var button = Button("crew-" + crew.Definition.Id, "", "tool", () =>
                {
                    if (crew.Targets.Count == 0) Send(BattleCommand.Support(crew.Definition.Id));
                    else { selectedSupport = selectedSupport == crew.Definition.Id ? null : crew.Definition.Id; selectedCard = 0; Refresh(); }
                }, playing && crew.Enabled);
                button.EnableInClassList("spent", crew.Used);
                button.Add(new SeaUiArt(crew.Definition.Kind == CrewSupportKind.Medic ? "heart" : "ear"));
                button.Add(Text(crew.Definition.Name, "tool-name"));
                button.Add(Text(crew.Used ? "本场已用" : $"{crew.Definition.Cost}行动", crew.Used ? "tool-spent" : "tool-uses"));
                button.tooltip = crew.Enabled ? "每回合最多一次，每名船员每场一次。" : crew.DisabledReason;
                tools.Add(button);
            }
            root.Add(tools);

            if (journalOpen)
            {
                var journal = Element("journal");
                var top = Element("journal-head");
                var titles = Element("journal-titles");
                titles.Add(Text("调查档案", "journal-title"));
                titles.Add(Text($"第 {view.Turn:00} 回合", "journal-subtitle"));
                top.Add(titles);
                top.Add(Button("journal-close", "收起", "journal-close", ToggleJournal));
                journal.Add(top);
                var entries = new ScrollView { name = "journal-entries" };
                entries.AddToClassList("journal-entries");
                entries.Add(Text("证据", "journal-section"));
                if (view.Evidence.Count == 0) entries.Add(Text("暂无证据", "journal-empty"));
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

            // Independent instruments leave the stage and hand visually open.
            var player = Element("player-status");
            var vitals = Element("vitals");
            var health = Meter("生命", view.Health, definition.Health, "health"); health.Add(new SeaUiArt("heart"));
            var sanity = Meter("理智", view.Sanity, definition.Sanity, "sanity"); sanity.Add(new SeaUiArt("eye"));
            vitals.Add(health); vitals.Add(sanity); player.Add(vitals);
            var actionLine = Element("action-line");
            actionLine.Add(new SeaUiArt("compass"));
            actionLine.Add(Text($"{view.Actions}/{definition.Actions}", "action-value"));
            actionLine.Add(Text("行动", "section-label"));
            player.Add(actionLine);
            var defense = Element("defenses");
            var block = Element("defense"); block.AddToClassList("block"); block.EnableInClassList("active", view.Block > 0);
            block.Add(Text("格挡", "defense-label")); block.Add(Text(view.Block.ToString(), "defense-value"));
            var ward = Element("defense"); ward.AddToClassList("ward"); ward.EnableInClassList("active", view.Ward > 0);
            ward.Add(Text("理智防护", "defense-label")); ward.Add(Text(view.Ward.ToString(), "defense-value"));
            defense.Add(block); defense.Add(ward);
            player.Add(defense);
            root.Add(player);

            // Bottom centre: the drawn hand. Larger hands scroll horizontally.
            var hand = new ScrollView(ScrollViewMode.Horizontal) { name = "hand" };
            hand.AddToClassList("hand");
            hand.pickingMode = PickingMode.Ignore;
            hand.contentViewport.pickingMode = PickingMode.Ignore;
            hand.contentContainer.pickingMode = PickingMode.Ignore;
            hand.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            hand.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            var fan = Element("fan-track");
            fan.style.width = Mathf.Max(620, 180 + Mathf.Max(0, view.Hand.Count - 1) * 110);
            hand.Add(fan);
            hand.RegisterCallback<GeometryChangedEvent>(_ => fan.style.marginLeft = Mathf.Max(0, (hand.contentViewport.resolvedStyle.width - fan.resolvedStyle.width) * .5f));
            var cardPresentation = new HandCardPresentation(hand);
            var animateDraw = playing && previousHand.Length > 0 && !GameSettings.ReducedMotion;
            var cardIndex = 0;
            foreach (var card in view.Hand)
            {
                var id = card.InstanceId; var data = card.Definition;
                var shortAction = !card.IsEncounter && view.Actions < data.Cost;
                var shortSanity = !card.IsEncounter && view.Sanity <= data.SanityCost;
                var button = Button("card-" + id, "", "card-anchor", () =>
                {
                    if (!card.Enabled || shortAction || shortSanity) { Send(BattleCommand.Play(id)); return; }
                    selectedSupport = null;
                    var automaticTarget = EnemyCardSelection.AutomaticTarget(card, view);
                    if (card.Targets.Count == 0 || automaticTarget != null) { selectedCard = 0; Send(BattleCommand.Play(id, automaticTarget)); }
                    else { selectedCard = selectedCard == id ? 0 : id; Refresh(); }
                }, playing);
                button.tabIndex = cardIndex + 1; button.style.left = cardIndex * 110;
                button.AddToClassList("fan-" + Mathf.Clamp(cardIndex++ - (view.Hand.Count - 1) / 2, -3, 3));
                cardPresentation.Add(button);
                if (animateDraw && !previousHand.Contains(id))
                {
                    button.AddToClassList("card-entering");
                    button.schedule.Execute(() => button.RemoveFromClassList("card-entering")).StartingIn(16);
                }
                button.EnableInClassList("unaffordable", playing && (shortAction || shortSanity || !card.Enabled));
                button.EnableInClassList("chosen-card", selectedCard == id);
                button.RegisterCallback<ContextClickEvent>(evt =>
                {
                    if (selectedCard == id) { selectedCard = 0; Refresh(); }
                    else { inspectedCard = id; Refresh(); }
                    evt.StopPropagation();
                });
                button.tooltip = data.Name + "：" + data.Description;
                var face = Element("card"); face.pickingMode = PickingMode.Position;
                IllustratedCard.Apply(face, data);
                if (card.IsEncounter) face.Q<Label>("illustrated-cost").text = card.ActionCost.ToString();
                face.Add(Text("D 详情", "card-detail-hint"));
                button.Add(face); fan.Add(button);
            }
            root.Add(hand);
            if (!string.IsNullOrEmpty(playedCardFeedback))
            {
                var feedback = Text("已打出 · " + playedCardFeedback, "card-play-feedback");
                root.Add(feedback);
                playedCardFeedback = null;
                if (!GameSettings.ReducedMotion)
                    feedback.schedule.Execute(() => feedback.AddToClassList("departing")).StartingIn(16);
            }
            var choiceCard = view.Hand.FirstOrDefault(card => card.InstanceId == selectedCard);
            if (choiceCard != null && choiceCard.Targets.Any(t => !EnemyCardSelection.IsEnemyTarget(t.Id, view)))
            {
                var choices = Element("card-target-panel");
                choices.Add(Text("选择：" + choiceCard.Definition.Name, "card-target-title"));
                var options = new ScrollView(ScrollViewMode.Vertical); options.AddToClassList("card-target-options"); choices.Add(options);
                var choiceIndex = 0;
                foreach (var option in choiceCard.Targets.Where(t => !EnemyCardSelection.IsEnemyTarget(t.Id, view)))
                {
                    var target = option;
                    var cardId = choiceCard.InstanceId;
                    options.Add(Button("card-target-" + choiceIndex++, target.Name + (string.IsNullOrWhiteSpace(target.Detail) ? "" : "\n" + target.Detail),
                        "card-target-choice", () => PlaySelectedCard(cardId, target.Id), playing && target.Enabled));
                }
                choices.Add(Button("card-target-cancel", "取消选择 · Esc", "card-target-cancel", () => { selectedCard = 0; Refresh(); }));
                root.Add(choices);
                choices.schedule.Execute(() => choices.Q<Button>(className: "card-target-choice")?.Focus());
            }
            var supportChoice = view.Supports.FirstOrDefault(support => support.Definition.Id == selectedSupport);
            if (supportChoice != null && supportChoice.Targets.Count > 0)
            {
                var choices = Element("card-target-panel");
                choices.Add(Text("船员支援：" + supportChoice.Definition.Name, "card-target-title"));
                var options = new ScrollView(ScrollViewMode.Vertical); options.AddToClassList("card-target-options"); choices.Add(options);
                var choiceIndex = 0;
                foreach (var option in supportChoice.Targets)
                {
                    var target = option; var supportId = supportChoice.Definition.Id;
                    options.Add(Button("support-target-" + choiceIndex++, target.Name + (string.IsNullOrWhiteSpace(target.Detail) ? "" : "\n" + target.Detail),
                        "card-target-choice", () =>
                        {
                            selectedSupport = null;
                            var result = Send(BattleCommand.Support(supportId, target.Id));
                            if (!result.Success) { selectedSupport = supportId; Refresh(); }
                        }, playing && target.Enabled));
                }
                root.Add(choices);
            }
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
            var end = Button("end-turn", "", "end-turn", () => Send(BattleCommand.EndTurn()), playing);
            end.tooltip = "结束回合";
            end.Add(new SeaUiArt("seal"));
            controls.Add(end);
            controls.Add(Text("结束回合", "end-title"));
            root.Add(controls);

            if (!string.IsNullOrEmpty(message))
            {
                var notice = Text(message, "battle-subtitle");
                notice.name = "battle-message"; root.Add(notice);
            }

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

            if (bookOpen)
            {
                foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
                root.Add(new BattleActionBookPanel(ToggleActionBook, false, view.Tools));
            }

            else if (chartOpen)
            {
                foreach (var child in root.Children()) child.SetEnabled(false);
                var chart = new VisualElement { name = "battle-chart-overlay" };
                chart.AddToClassList("battle-chart-overlay");
                chart.Add(new VoyageMapPanel(VoyageRuntime.Session, null, false));
                chart.Add(Button("battle-chart-close", "返回战斗", "battle-chart-close", CloseChart));
                root.Add(chart);
            }

            if (menuOpen)
            {
                foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
                var scrim = new VisualElement { name = "pause-scrim" };
                scrim.AddToClassList("pause-scrim");
                if (settingsOpen)
                    scrim.Add(new SettingsPanel(() => { settingsOpen = false; Refresh("pause-settings"); },
                        () => root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion)));
                else
                {
                    var pause = Element("pause");
                    pause.Add(Text("暂停", "pause-overline"));
                    pause.Add(Button("pause-resume", "继续 / Esc", "pause-option", ToggleMenu));
                    pause.Add(Button("pause-settings", "设置", "pause-option", () => { settingsOpen = true; Refresh(); }));
                    var canLeave = Application.CanStreamedLevelBeLoaded(menuScene);
                    var leave = Button("pause-title", "放弃并返回标题", "pause-option", () => SceneManager.LoadScene(menuScene), canLeave);
                    leave.AddToClassList("danger");
                    pause.Add(leave);
                    scrim.Add(pause);
                }
                root.Add(scrim);
            }

            if (view.PendingChoice != null)
            {
                foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
                root.Add(PendingChoice(view.PendingChoice));
                return;
            }

            var inspection = view.Hand.FirstOrDefault(card => card.InstanceId == inspectedCard);
            if (inspection != null)
            {
                foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
                root.Add(IllustratedCard.Inspection(inspection.Definition, CloseInspection));
                return;
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
        private VisualElement PendingChoice(PendingCardChoiceView choice)
        {
            var modal = Element("pending-choice"); modal.pickingMode = PickingMode.Position;
            var sheet = Element("pending-choice-sheet"); sheet.pickingMode = PickingMode.Position;
            sheet.Add(Text(choice.Prompt, "pending-choice-title"));
            var options = new ScrollView(ScrollViewMode.Vertical); options.AddToClassList("pending-choice-options"); sheet.Add(options);
            var index = 0;
            foreach (var option in choice.Options)
            {
                var target = option;
                options.Add(Button("pending-choice-" + index++, target.Name + (string.IsNullOrWhiteSpace(target.Detail) ? "" : "\n" + target.Detail),
                    "card-target-choice", () => Send(BattleCommand.Reorder(target.Id))));
            }
            modal.Add(sheet);
            modal.schedule.Execute(() => modal.Q<Button>(className: "card-target-choice")?.Focus());
            return modal;
        }

        private void CloseInspection()
        {
            var card = inspectedCard; inspectedCard = 0; Refresh("card-" + card);
        }
    }
}
