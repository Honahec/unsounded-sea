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
    public sealed class VoyageHud : MonoBehaviour
    {
        [SerializeField] private FontAsset interfaceFont;
        [SerializeField] private StyleSheet interfaceStyle;
        [SerializeField] private StyleSheet menuStyle;
        [SerializeField] private VoyageSailingPresentation sailingPresentation;
        private VisualElement root;
        private IVisualElementScheduledItem sailing;
        private bool paused, settingsOpen, journalOpen, chartOpen, deckOpen;
        private string saveError, loadError, notice;
        private float lastTick;
        private VoyageSession Session => VoyageRuntime.Session;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            try
            {
                if (interfaceFont == null || interfaceStyle == null || menuStyle == null || sailingPresentation == null)
                    throw new InvalidOperationException("Assign the voyage font, stylesheets and sailing presentation.");
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(interfaceFont);
                if (!root.styleSheets.Contains(menuStyle)) root.styleSheets.Add(menuStyle);
                if (!root.styleSheets.Contains(interfaceStyle)) root.styleSheets.Add(interfaceStyle);
                root.AddToClassList("voyage-hud");
                root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
                if (Session == null)
                {
                    if (VoyageRuntime.HasSavedVoyage)
                    {
                        if (!VoyageRuntime.TryResume(out loadError)) { Refresh(); return; }
                    }
                    else VoyageRuntime.StartNew();
                }
                var current = Session.Current;
                var previous = Session.Visited.Count > 1
                    ? System.Linq.Enumerable.FirstOrDefault(Session.KnownStops, x => x.Id == Session.Visited[Session.Visited.Count - 2]) : null;
                sailingPresentation.SetLocation(new Vector2(current.X, current.Y), previous == null ? Vector2.up : new Vector2(current.X - previous.X, current.Y - previous.Y), current.Name);
                Refresh();
            }
            catch (Exception exception) { loadError = exception.Message; Refresh(); Debug.LogException(exception); }
        }
        private void Update()
        {
            sailingPresentation?.Tick(Time.unscaledDeltaTime,
                paused || settingsOpen || journalOpen || chartOpen || deckOpen || !string.IsNullOrEmpty(saveError) || !string.IsNullOrEmpty(loadError),
                GameSettings.ReducedMotion);
        }
        private void OnDisable()
        {
            sailing?.Pause();
            sailingPresentation?.Finish();
            root?.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            root?.Clear();
        }
        private void OnKey(KeyDownEvent evt)
        {
            if (settingsOpen) return;
            if (evt.keyCode == KeyCode.Escape)
            {
                if (deckOpen) deckOpen = false;
                else if (journalOpen) journalOpen = false;
                else if (chartOpen && !paused) chartOpen = false;
                else paused = !paused;
            }
            else if (deckOpen) return;
            else if (evt.keyCode == KeyCode.M && !paused && !journalOpen && Session?.Phase != VoyagePhase.Sailing) chartOpen = !chartOpen;
            else if (evt.keyCode == KeyCode.J && !paused) journalOpen = !journalOpen;
            else return;
            Refresh(); evt.StopPropagation();
        }
        private static VisualElement Element(string style)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore }; element.AddToClassList(style); return element;
        }
        private static Label Text(string value, string style)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(style); return label;
        }
        private static Button Action(string id, string title, string style, System.Action action, bool enabled = true)
        {
            var button = new Button(action) { name = id, text = title };
            button.AddToClassList(style); button.SetEnabled(enabled); return button;
        }
        private void Change(Func<bool> transition, string feedback = null)
        {
            if (paused || settingsOpen || !string.IsNullOrEmpty(saveError)) return;
            var previousAct = Session.Act;
            if (!transition()) return;
            if (Session.Act != previousAct)
            {
                chartOpen = false;
                var current = Session.Current;
                sailingPresentation.SetLocation(new Vector2(current.X, current.Y), Vector2.up, current.Name);
            }
            notice = feedback;
            Save(); Refresh();
        }
        private void Save()
        {
            try { VoyageRuntime.Checkpoint(); saveError = null; }
            catch (Exception exception) { saveError = exception.Message; }
        }
        private void EnterEncounter()
        {
            if (paused || !string.IsNullOrEmpty(saveError)) return;
            try { VoyageRuntime.EnterEncounter(); }
            catch (Exception exception) { notice = "无法进入遭遇 / " + exception.Message; Refresh(); }
        }
        private static string PhaseName(VoyagePhase phase)
        {
            switch (phase)
            {
                case VoyagePhase.ChoosingRoute: return "选择航向";
                case VoyagePhase.Sailing: return "航行中";
                case VoyagePhase.Arrived: return "已抵达";
                case VoyagePhase.Encounter: return "调查待续";
                case VoyagePhase.Reward: return "选择收获";
                case VoyagePhase.ActComplete: return "海域已穿越";
                case VoyagePhase.Completed: return "远征完成";
                default: return "远征终止";
            }
        }
        private void Refresh()
        {
            if (root == null) return;
            var focus = (root.panel?.focusController.focusedElement as VisualElement)?.name;
            sailing?.Pause();
            root.Clear(); root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion);
            if (Session?.Phase == VoyagePhase.Reward || Session?.Phase == VoyagePhase.ActComplete) chartOpen = false;
            root.EnableInClassList("chart-open", chartOpen);
            root.EnableInClassList("is-sailing", Session?.Phase == VoyagePhase.Sailing);
            root.EnableInClassList("reward-open", Session?.Phase == VoyagePhase.Reward);
            root.EnableInClassList("act-complete", Session?.Phase == VoyagePhase.ActComplete);
            if (!string.IsNullOrEmpty(loadError) || Session == null)
            {
                var error = Element("voyage-load-error");
                error.Add(Text("无法读取航海记录", "panel-title")); error.Add(Text(loadError ?? "航程尚未创建。", "body"));
                error.Add(Text("原记录未被覆盖。返回标题后可重试，或明确开始新航程。", "muted"));
                error.Add(Action("load-title", "返回标题", "primary", () => SceneManager.LoadScene(VoyageRuntime.MainMenuScene)));
                root.Add(error); return;
            }
            var session = Session;
            var header = Element("voyage-header");
            var status = Element("voyage-status");
            status.Add(Text($"第 {session.Act} / 3 幕  ·  {RegionName(session.RegionId)}  ·  航点 {session.VisitedThisAct} / {VoyageSession.ActLength}", "expedition-progress"));
            status.Add(Text($"生命 {session.Health} / 60    理智 {session.Sanity} / 30    补给 {session.Supplies} / 8", "resource-strip"));
            header.Add(status);
            var utilities = Element("voyage-utilities");
            if (session.CanEditDeck)
                utilities.Add(Action("voyage-action-book", "行动簿", "secondary", () => { deckOpen = true; Refresh(); }));
            utilities.Add(Action("voyage-chart-toggle", chartOpen ? "收起海图" : "海图", "secondary", () => { chartOpen = !chartOpen; Refresh(); }, session.Phase != VoyagePhase.Sailing));
            utilities.Add(Action("voyage-journal", "日志", "secondary", () => { journalOpen = true; Refresh(); }));
            utilities.Add(Action("voyage-menu", "菜单", "secondary", () => { paused = true; Refresh(); }));
            utilities.Q<Button>("voyage-chart-toggle").tooltip = "海图 / M";
            utilities.Q<Button>("voyage-journal").tooltip = "航海日志 / J";
            utilities.Q<Button>("voyage-menu").tooltip = "暂停 / Esc";
            header.Add(utilities); root.Add(header);

            var workspace = Element("voyage-workspace");
            var location = Element("current-location");
            location.Add(Text(session.Current.Name, "location-name"));
            if (chartOpen) location.Add(Text($"{session.ElapsedHours} 小时  ·  {session.LegsSailed} 段", "fact"));
            workspace.Add(location);

            VoyageChart chart = null;
            if (chartOpen)
            {
                var map = new VoyageMapPanel(session, id => Change(() => Session.Select(id)), string.IsNullOrEmpty(saveError));
                chart = map.Chart;
                workspace.Add(map);
                BuildNavigationTools(workspace, session);
            }

            var navigation = Element("voyage-navigation");
            navigation.EnableInClassList("choosing-route", session.Phase == VoyagePhase.ChoosingRoute);
            if (!chartOpen && session.Phase == VoyagePhase.ChoosingRoute)
            {
                var routes = Element("route-list");
                foreach (var stop in session.Choices)
                {
                    var destination = stop;
                    var choice = Action("route-" + stop.Id, string.Empty, "route-choice",
                        () => Change(() => Session.Select(destination.Id)), string.IsNullOrEmpty(saveError));
                    var chosen = session.Selected?.Id == stop.Id;
                    choice.tooltip = $"{KindName(stop.Kind)} · {stop.Clue}\n" +
                        (chosen ? "已选航向；确认启航后出发。" : "预览此航路，确认启航后出发。");
                    var description = Element("route-description");
                    description.Add(Text(stop.Name, "route-name"));
                    description.Add(Text($"{KindName(stop.Kind)}  ·  {stop.Clue}", "route-theme"));
                    description.Add(Text($"{session.HeadingTo(stop)}  ·  {stop.TravelHours} 小时", "route-duration"));
                    choice.Add(description);
                    choice.EnableInClassList("selected", chosen);
                    routes.Add(choice);
                }
                navigation.Add(routes);
            }
            var destinationPanel = Element("destination-panel");
            BuildDestination(destinationPanel, session, chart);
            navigation.Add(destinationPanel);
            workspace.Add(navigation);
            root.Add(workspace);
            if (!string.IsNullOrEmpty(notice))
            {
                var footer = Element("voyage-footer");
                footer.Add(Text(notice, "footer-note")); root.Add(footer);
            }
            if (!string.IsNullOrEmpty(saveError))
            {
                PrepareModal();
                var error = Element("save-error"); error.pickingMode = PickingMode.Position;
                error.Add(Text("航海记录尚未保存 / " + saveError, "body"));
                error.Add(Action("save-retry", "重试保存", "primary", () => { Save(); Refresh(); })); root.Add(error);
                root.schedule.Execute(() => root.Q<Button>("save-retry")?.Focus());
            }
            else if (deckOpen) BuildActionBook();
            else if (journalOpen) BuildJournal();
            else if (paused) BuildPause();
            if (!string.IsNullOrEmpty(focus)) root.schedule.Execute(() =>
            {
                var target = root.Q<Button>(focus);
                if (target == null || !target.canGrabFocus) target = root.Q<Button>("set-sail") ?? root.Q<Button>("voyage-chart-toggle");
                if (target != null && target.canGrabFocus) target.Focus();
            });
        }
        private void BuildNavigationTools(VisualElement workspace, VoyageSession session)
        {
            var tools = Element("navigation-tools");
            var chartTool = Action("reveal-routes", $"旧海图  ×{session.ChartUses}", "navigation-tool",
                () => Change(Session.RevealRoutes, "旧海图已补记此处岔路。"),
                session.CanRevealRoutes && string.IsNullOrEmpty(saveError));
            chartTool.tooltip = "旧海图：揭开当前位置未测出口；数字为剩余次数，没有新发现时不消耗。";
            tools.Add(chartTool);
            var lookoutTool = Action("scout-ahead", $"远望  ×{session.LookoutUses}", "navigation-tool",
                () => Change(Session.ScoutAhead, "远望记录已加入海图。"),
                session.CanScoutAhead && string.IsNullOrEmpty(saveError));
            lookoutTool.tooltip = "测绘望远镜：需先选择航向；侦察目的地再往前一段，数字为剩余次数，没有新发现时不消耗。";
            tools.Add(lookoutTool);
            workspace.Add(tools);
        }

        private void BuildDestination(VisualElement panel, VoyageSession session, VoyageChart chart)
        {
            bool saved = string.IsNullOrEmpty(saveError);
            var stop = session.Selected ?? session.Current;
            if (session.Phase == VoyagePhase.ChoosingRoute && session.Selected == null)
            {
                if (session.CanEditDeck)
                    panel.Add(Action("open-action-book", "整备行动簿", "primary", () => { deckOpen = true; Refresh(); }));
                return;
            }
            if (session.Phase == VoyagePhase.ChoosingRoute && chartOpen)
            {
                panel.Add(Text(stop.Name, "destination-title"));
                panel.Add(Text($"{KindName(stop.Kind)}  ·  {session.HeadingTo(stop)}  ·  {stop.TravelHours} 小时", "bearing"));
            }
            if (session.Phase == VoyagePhase.Completed)
                panel.Add(Text(session.IsDebug ? "独立航点完成" : "三幕远征胜利", "destination-title"));
            else if (session.Phase == VoyagePhase.Failed)
                panel.Add(Text("远征终止", "destination-title"));
            if (session.Phase != VoyagePhase.Sailing)
                panel.Add(Text(session.Phase == VoyagePhase.Completed
                    ? session.IsDebug ? "本次独立试玩已结束。" : "归航绳已收回，调查记录带离深渊。"
                    : stop.Clue, "destination-clue"));
            switch (session.Phase)
            {
                case VoyagePhase.ChoosingRoute:
                    panel.Add(Action("set-sail", "启航", "primary", () => { chartOpen = false; Change(Session.SetSail); }, saved));
                    break;
                case VoyagePhase.Sailing:
                    var track = Element("sailing-track"); var fill = Element("sailing-fill"); track.Add(fill); panel.Add(track);
                    panel.Add(Text("穿越海雾", "bearing"));
                    if (saved)
                    {
                        var veil = Element("sailing-fog"); veil.name = "sailing-fog";
                        root.Insert(0, veil); ScheduleArrival(chart, fill, veil);
                    }
                    break;
                case VoyagePhase.Arrived:
                    bool encounter = stop.Kind == RunNodeKind.Battle || stop.Kind == RunNodeKind.Boss;
                    if (encounter)
                        panel.Add(Action("arrival-investigate", "接近并调查", "primary", EnterEncounter, saved));
                    else
                    {
                        var nodeChoices = session.GetNodeChoices();
                        foreach (var nodeChoice in nodeChoices)
                        {
                            var choice = nodeChoice;
                            var button = Action("node-choice-" + choice.Id, string.Empty, "node-choice",
                                () => Change(() => Session.ChooseNode(choice.Id)), saved && choice.Enabled);
                            button.tooltip = choice.Description;
                            button.Add(Text(choice.Title, "node-choice-title"));
                            button.Add(Text(choice.Description, "node-choice-description"));
                            panel.Add(button);
                        }
                        if (session.CanEditDeck)
                            panel.Add(Action("arrival-action-book", "整备行动簿", "secondary", () => { deckOpen = true; Refresh(); }));
                        if (nodeChoices.Count == 0)
                            panel.Add(Action("arrival-continue", "记录并继续", "primary", () => Change(Session.Continue), saved));
                    }
                    break;
                case VoyagePhase.Encounter:
                    var resume = Action("resume-encounter", "继续调查", "primary", EnterEncounter, saved);
                    resume.tooltip = "调查尚未结算；继续时将返回本场调查。";
                    panel.Add(resume); break;
                case VoyagePhase.Reward:
                    BuildRewards(panel, session, saved); break;
                case VoyagePhase.ActComplete:
                    BuildActChoice(panel, session, saved); break;
                case VoyagePhase.Completed:
                case VoyagePhase.Failed:
                    panel.Add(Action("voyage-finish", "返回标题", "primary", () => SceneManager.LoadScene(VoyageRuntime.MainMenuScene))); break;
            }
        }
        private void BuildRewards(VisualElement panel, VoyageSession session, bool saved)
        {
            panel.Add(Text("选择一张加入行动库；下次整备时编入牌组，不会自动替换现有行动。", "reward-copy"));
            var cards = Element("reward-cards");
            foreach (var reward in session.RewardCards)
            {
                var card = reward;
                var button = Action("reward-" + card.Id, string.Empty, "reward-card",
                    () => Change(() => Session.ClaimReward(card.Id), $"已习得「{card.Name}」；可在整备点编入牌组。"), saved);
                var face = Element("reward-card-face"); IllustratedCard.Apply(face, card); button.Add(face);
                button.tooltip = card.Name + "：" + card.Description; cards.Add(button);
            }
            panel.Add(cards);
            panel.Add(Action("reward-action-book", "查看行动簿", "secondary", () => { deckOpen = true; Refresh(); }));
            var suppliesFull = session.Supplies >= 8;
            panel.Add(Action("reward-supplies", suppliesFull ? "跳过奖励 · 补给已满" : "放弃卡牌 · 获得 1 补给", "secondary",
                () => Change(() => Session.ClaimReward(null), suppliesFull ? "已跳过奖励；补给已满，没有额外收益。" : "已将所得换为 1 补给。"), saved));
        }

        private void BuildActChoice(VisualElement panel, VoyageSession session, bool saved)
        {
            panel.Add(Text(session.Act == 1 ? "选择第二幕航向" : "驶向最终海域", "destination-title"));
            if (session.Act == 1)
            {
                AddRegionChoice(panel, "bones", "尸骸之海", "追查召唤物的来源；战斗会出现可追溯的召唤链。", saved);
                AddRegionChoice(panel, "inverted", "倒悬潮庭", "在盐格中推理安全航路，以调查结果排除危险格。", saved);
                AddRegionChoice(panel, "fleet", "无灯裂海", "干预舰队装置，在短暂窗口内完成关键行动。", saved);
            }
            else
                AddRegionChoice(panel, "abyss", "无星深渊", "终局航向：驶入没有星光回应的洋流。", saved);
        }

        private void AddRegionChoice(VisualElement panel, string id, string title, string description, bool enabled)
        {
            var button = Action("region-" + id, string.Empty, "region-choice",
                () => Change(() => Session.ChooseNextAct(id)), enabled);
            button.Add(Text(title, "node-choice-title"));
            button.tooltip = description; button.Add(Text(description, "region-description")); panel.Add(button);
        }

        private void BuildActionBook()
        {
            PrepareModal();
            bool editable = Session.CanEditDeck;
            var selected = new HashSet<string>(Session.DeckIds, StringComparer.Ordinal);
            var scrim = Element("voyage-overlay"); scrim.pickingMode = PickingMode.Position;
            var sheet = Element("action-book");
            var head = Element("action-book-head");
            head.Add(Text("远征行动簿", "journal-title"));
            var count = Text("", "collection-count"); head.Add(count); sheet.Add(head);
            sheet.Add(Text(editable ? "仅显示本次远征已习得的行动。选满 12 张后保存；不会写入标题页的预设牌组。" : "高亮为已编入的 12 张牌；其余为已学方案。仅能在整备节点更换。", "muted"));
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("action-book-scroll");
            var cards = Element("action-book-cards"); scroll.Add(cards); sheet.Add(scroll);
            Button save = null;
            void UpdateState()
            {
                count.text = $"已编入 {selected.Count} / 12";
                save?.SetEnabled(editable && selected.Count == 12);
            }
            foreach (var id in Session.LearnedCardIds)
            {
                var card = CardCatalog.Get(id);
                var button = Action("book-" + id, string.Empty, "action-book-card", null);
                button.clicked += () =>
                {
                    if (!editable) return;
                    if (!selected.Remove(card.Id))
                    {
                        if (selected.Count >= 12) return;
                        selected.Add(card.Id);
                    }
                    button.EnableInClassList("selected", selected.Contains(card.Id)); UpdateState();
                };
                button.EnableInClassList("selected", selected.Contains(id));
                var face = Element("action-book-card-face"); IllustratedCard.Apply(face, card); button.Add(face);
                button.tooltip = card.Name + "：" + card.Description; cards.Add(button);
            }
            var selectedTools = new HashSet<string>(Session.ToolIds, StringComparer.Ordinal);
            var toolTitle = Text($"调查工具 {selectedTools.Count} / 2", "tool-count"); sheet.Add(toolTitle);
            var tools = Element("action-book-tools"); sheet.Add(tools);
            foreach (var id in Session.LearnedToolIds)
            {
                var toolId = id;
                var tool = Action("tool-" + id, ToolName(id), "action-book-tool", null);
                tool.clicked += () =>
                {
                    if (!editable) return;
                    if (!selectedTools.Remove(toolId))
                    {
                        if (selectedTools.Count >= 2) return;
                        selectedTools.Add(toolId);
                    }
                    tool.EnableInClassList("selected", selectedTools.Contains(toolId));
                    toolTitle.text = $"调查工具 {selectedTools.Count} / 2";
                };
                tool.EnableInClassList("selected", selectedTools.Contains(id)); tools.Add(tool);
            }
            var actions = Element("modal-actions");
            save = Action("action-book-save", "保存 12 张", "primary", () =>
            {
                if (!Session.SetDeck(selected) || !Session.SetTools(selectedTools)) return;
                deckOpen = false; notice = "行动簿与调查工具已整备（可不携带工具）。"; Save(); Refresh();
            }, editable && selected.Count == 12);
            if (editable) actions.Add(save);
            actions.Add(Action("action-book-close", "取消 / Esc", "secondary", () => { deckOpen = false; Refresh(); }));
            sheet.Add(actions); scrim.Add(sheet); root.Add(scrim);
            UpdateState();
            scrim.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                if (!(root.panel?.focusController.focusedElement is VisualElement focused) || !sheet.Contains(focused))
                {
                    root.Q<Button>("action-book-close")?.Focus(); evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            root.schedule.Execute(() => root.Q<Button>("action-book-close")?.Focus());
        }

        private static string RegionName(string id)
        {
            switch (id)
            {
                case "fog": return "灰雾航道";
                case "bones": return "尸骸之海";
                case "inverted": return "倒悬潮庭";
                case "fleet": return "无灯裂海";
                case "abyss": return "无星深渊";
                default: return id ?? "未知海域";
            }
        }

        private static string ToolName(string id) => id == "lantern" ? "防风提灯 · 可见行动" :
            id == "listener" ? "定向听筒 · 声源" : id == "shell" ? "听潮贝壳 · 伤害途径（支付 3 理智）" : id;

        private static string KindName(RunNodeKind kind)
        {
            switch (kind)
            {
                case RunNodeKind.Battle: return "调查遭遇";
                case RunNodeKind.Boss: return "区域威胁";
                case RunNodeKind.Supply: return "安全补给";
                case RunNodeKind.Archive: return "档案线索";
                case RunNodeKind.Reward: return "行动收获";
                default: return "海上事件";
            }
        }

        private void PrepareModal()
        {
            foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
        }

        private void ScheduleArrival(VoyageChart chart, VisualElement fill, VisualElement veil)
        {
            if (!sailingPresentation.IsPlaying)
            {
                var to = Session.Selected;
                sailingPresentation.Begin(new Vector2(to.X, to.Y), GameSettings.ReducedMotion, to.Name);
            }
            lastTick = Time.realtimeSinceStartup;
            sailing = root.schedule.Execute(() =>
            {
                var now = Time.realtimeSinceStartup; var delta = now - lastTick; lastTick = now;
                if (paused || settingsOpen || journalOpen || Session.Phase != VoyagePhase.Sailing) return;
                sailingPresentation.Advance(delta, GameSettings.ReducedMotion);
                var progress = sailingPresentation.Progress;
                fill.style.width = Length.Percent(progress * 100); chart?.SetProgress(progress);
                veil.style.opacity = sailingPresentation.TransitionOpacity;
                if (progress < 1) return;
                sailing.Pause(); sailingPresentation.Finish();
                Change(Session.Arrive);
            }).Every(33);
        }
        private void BuildJournal()
        {
            PrepareModal();
            var scrim = Element("voyage-overlay"); scrim.pickingMode = PickingMode.Position;
            var sheet = Element("voyage-journal");
            sheet.Add(Text("航海日志", "journal-title"));
            if (Session.Knowledge.Count > 0)
            {
                sheet.Add(Text("已确认情报", "muted"));
                foreach (var fact in Session.Knowledge) sheet.Add(Text(fact, "journal-entry"));
            }
            sheet.Add(Text("航迹与观测", "muted"));
            var entries = new ScrollView(); foreach (var entry in Session.Log) entries.Add(Text(entry, "journal-entry")); sheet.Add(entries);
            sheet.Add(Action("journal-close", "收起日志 / Esc", "primary", () => { journalOpen = false; Refresh(); }));
            scrim.Add(sheet); root.Add(scrim); root.schedule.Execute(() => root.Q<Button>("journal-close")?.Focus());
        }
        private void BuildPause()
        {
            PrepareModal();
            var scrim = Element("pause-scrim"); scrim.pickingMode = PickingMode.Position;
            if (settingsOpen)
                scrim.Add(new SettingsPanel(() => { settingsOpen = false; Refresh(); }, () => root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion)));
            else
            {
                var pause = Element("pause");
                pause.Add(Text("航行暂停", "pause-overline"));
                pause.Add(Text("暂停期间不会抵达目的地，也不会改变方向。", "pause-note"));
                pause.Add(Action("pause-resume", "继续航行", "pause-option", () => { paused = false; Refresh(); }));
                pause.Add(Action("pause-settings", "设置", "pause-option", () => { settingsOpen = true; Refresh(); }));
                pause.Add(Action("pause-title", "保存航程 / 返回标题", "pause-option", () => { Save(); if (string.IsNullOrEmpty(saveError)) SceneManager.LoadScene(VoyageRuntime.MainMenuScene); else Refresh(); }));
                scrim.Add(pause);
            }
            root.Add(scrim); if (!settingsOpen) root.schedule.Execute(() => root.Q<Button>("pause-resume")?.Focus());
        }
    }
}
