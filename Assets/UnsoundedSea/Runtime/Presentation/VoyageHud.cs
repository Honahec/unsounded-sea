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
    public sealed partial class VoyageHud : MonoBehaviour
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
        private Action actionBookSave;
        private string selectedReward;
        private VisualElement choiceDetail;
        private bool replacingReward;
        private VoyageSession Session => VoyageRuntime.Session;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.focusable = true;
            try
            {
                if (interfaceFont == null || interfaceStyle == null || menuStyle == null || sailingPresentation == null)
                    throw new InvalidOperationException("Assign the voyage font, stylesheets and sailing presentation.");
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(UiFont.Get(interfaceFont));
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
            HotkeyRelay.Poll(root, HandleKey);
            sailingPresentation?.Tick(Time.unscaledDeltaTime,
                GameSceneLoader.IsLoading || paused || settingsOpen || journalOpen || chartOpen || deckOpen || supportOpen || replacingReward || Session?.DebriefPending==true || Session?.RegionIntroPending==true || !string.IsNullOrEmpty(saveError) || !string.IsNullOrEmpty(loadError),
                GameSettings.ReducedMotion);
        }
        private void OnDisable()
        {
            sailing?.Pause();
            sailingPresentation?.Finish();
            root?.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            root?.Clear();
        }
        private void OnKey(KeyDownEvent evt) { if (HotkeyRelay.Dispatch(root, evt.keyCode, HandleKey)) evt.StopImmediatePropagation(); }
        private bool HandleKey(KeyCode key)
        {
            if(Session?.DebriefPending==true||Session?.RegionIntroPending==true)
            {
                if(key==KeyCode.Escape)DismissFlow();
                return key!=KeyCode.Tab&&key!=KeyCode.Return&&key!=KeyCode.KeypadEnter&&key!=KeyCode.Space;
            }
            if(replacingReward)
            {
                if(key==KeyCode.Escape)CancelReplacement();
                return key!=KeyCode.Tab&&key!=KeyCode.Return&&key!=KeyCode.KeypadEnter&&key!=KeyCode.Space&&key!=KeyCode.LeftArrow&&key!=KeyCode.RightArrow&&key!=KeyCode.UpArrow&&key!=KeyCode.DownArrow;
            }
            if (root.panel?.focusController.focusedElement is TextField) return false;
            if (settingsOpen)
            {
                if (key != KeyCode.Escape) return false;
                settingsOpen = false; Refresh(); return true;
            }
            if (key == KeyCode.Escape)
            {
                if (supportOpen) supportOpen = false;
                else if (deckOpen) deckOpen = false;
                else if (journalOpen) journalOpen = false;
                else if (chartOpen && !paused) chartOpen = false;
                else paused = !paused;
            }
            else if (deckOpen && key == KeyCode.S)
            {
                var save = root.Q<Button>("action-book-save");
                if (save?.enabledSelf != true) return false;
                actionBookSave?.Invoke();
                return true;
            }
            else if (key == KeyCode.B)
            {
                if (deckOpen) deckOpen = false;
                else if (supportOpen || paused || journalOpen || chartOpen || Session?.Phase == VoyagePhase.Reward) return false;
                else deckOpen = true;
            }
            else if (deckOpen || supportOpen) return false;
            else if (key == KeyCode.M && !paused && !journalOpen && Session?.Phase != VoyagePhase.Sailing) chartOpen = !chartOpen;
            else if (key == KeyCode.J && !paused) journalOpen = !journalOpen;
            else return false;
            Refresh(); return true;
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
            var slash=title.LastIndexOf(" / ",StringComparison.Ordinal);
            if(slash>=0){var key=title.Substring(slash+3);var caption=title.Substring(0,slash);button.text=caption;KeyBadge.Attach(button,key);button.tooltip=caption+"（"+key+"）";}
            button.AddToClassList(style); button.SetEnabled(enabled); return button;
        }
        private void Change(Func<bool> transition, string feedback = null)
        {
            if (paused || settingsOpen || Session.DebriefPending || Session.RegionIntroPending || replacingReward || !string.IsNullOrEmpty(saveError)) return;
            if (!transition()) return;
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
            if (paused || Session.DebriefPending || Session.RegionIntroPending || replacingReward || !string.IsNullOrEmpty(saveError)) return;
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
            UiMotion.BeforeBuild(root);
            root.schedule.Execute(() => UiMotion.AfterBuild(root));
            var focus = (root.panel?.focusController.focusedElement as VisualElement)?.name;
            var scrollPositions = root.Query<ScrollView>().ToList().Where(s => !string.IsNullOrEmpty(s.name)).ToDictionary(s => s.name, s => s.scrollOffset);
            sailing?.Pause();
            root.Clear(); root.focusable = true; root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion);
            if (Session?.Phase != VoyagePhase.Reward) { selectedReward = null; replacingReward=false; }
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
                error.Add(Action("load-title", "返回标题", "primary", () => GameSceneLoader.Load(VoyageRuntime.MainMenuScene)));
                root.Add(error); return;
            }
            var session = Session;
            var header = Element("voyage-header");
            var status = Element("voyage-status");
            status.Add(Text($"第 {session.Act} / 3 幕  ·  {RegionName(session.RegionId)}  ·  航点 {session.VisitedThisAct} / {VoyageSession.ActLength}", "expedition-progress"));
            var resources = Element("resource-strip");
            Resource(resources, "heart", "生命", $"{session.Health}/60");
            SanityResource(resources, session);
            Resource(resources, "lantern", "补给", $"{session.Supplies}/8");
            if (session.Watched > 0) StatusSeal(resources, "eye", $"被注视 +{session.Watched}", "被注视越高，异常事件越容易回应航迹。");
            if (session.Markers.Any(marker => marker.Id == "surveyed-beacon")) StatusSeal(resources, "bearing", "已测航标", "下一场战斗结束后失效。");
            if (session.Markers.Any(marker => marker.Id == "docking-voucher")) StatusSeal(resources, "seal", "接驳凭证", "下次成功招聘少付 1 补给。");
            status.Add(resources);
            header.Add(status);
            var utilities = Element("voyage-utilities");
            if (session.Phase != VoyagePhase.Reward)
                utilities.Add(KeyBadge.Attach(Action("voyage-action-book", "行动簿", "secondary", () => { deckOpen = true; Refresh(); }), "B"));
            utilities.Add(KeyBadge.Attach(Action("voyage-chart-toggle", chartOpen ? "收起海图" : "海图", "secondary", () => { chartOpen = !chartOpen; Refresh(); }, session.Phase != VoyagePhase.Sailing), "M"));
            utilities.Add(KeyBadge.Attach(Action("voyage-journal", "日志", "secondary", () => { journalOpen = true; Refresh(); }), "J"));
            utilities.Add(KeyBadge.Attach(Action("voyage-menu", "菜单", "secondary", () => { paused = true; Refresh(); }), "Esc"));
            utilities.Q<Button>("voyage-action-book")?.SetEnabled(!paused && !journalOpen && !chartOpen);
            header.Add(utilities); root.Add(header);

            var workspace = Element("voyage-workspace");
            var location = Element("current-location");
            location.Add(Text(session.Current.Name, "location-name"));
            if (chartOpen) location.Add(Text($"{session.ElapsedHours} 小时  ·  {session.LegsSailed} 段", "fact"));
            workspace.Add(location);
            // Hovering or focusing a purchasable/node option explains it in the empty left-hand space.

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
            // Ordinary node states use one full-height column read top to bottom; sailing and rewards keep their centred layouts.
            var useColumn = session.Phase != VoyagePhase.Sailing && session.Phase != VoyagePhase.Reward;
            navigation.EnableInClassList("column", useColumn);
            VisualElement host = navigation;
            if (useColumn)
            {
                var column = new ScrollView(ScrollViewMode.Vertical) { name = "voyage-column-scroll" };
                column.AddToClassList("voyage-column-scroll");
                column.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                navigation.Add(column); ScrollWheel.ForwardFrom(navigation, column);
                host = column;
            }
            if (!chartOpen && session.Phase == VoyagePhase.ChoosingRoute)
            {
                var routes = Element("route-list");
                foreach (var stop in session.Choices)
                {
                    var destination = stop;
                    var choice = Action("route-" + stop.Id, string.Empty, "route-choice",
                        () => Change(() => Session.Select(destination.Id)), string.IsNullOrEmpty(saveError));
                    var chosen = session.Selected?.Id == stop.Id;
                    var style = session.PreviewStyle(stop);
                    var styleName = style == EnemyStyle.Rhythm ? "节奏" : style == EnemyStyle.Hidden ? "隐匿" : style == EnemyStyle.Boss ? "Boss" : "";
                    choice.tooltip = $"{KindName(stop.Kind)} · {stop.Clue}\n{PreviewRoute(session, stop)}";
                    var description = Element("route-description");
                    description.Add(Text(stop.Name, "route-name"));
                    description.Add(Text($"{KindName(stop.Kind)}{(string.IsNullOrEmpty(styleName) ? "" : " · " + styleName)}", "route-theme"));
                    if (stop.Perilous) description.Add(Text("双敌 · 两次收获", "perilous-badge"));
                    description.Add(Text($"{session.HeadingTo(stop)}  ·  {stop.TravelHours} 小时", "route-duration"));
                    choice.Add(description);
                    choice.EnableInClassList("selected", chosen);
                    routes.Add(choice);
                }
                host.Add(routes);
            }
            var destinationPanel = Element("destination-panel");
            BuildDestination(destinationPanel, session, chart);
            host.Add(destinationPanel);
            if (useColumn)
                foreach (var id in new[] { "set-sail", "arrival-investigate", "resume-encounter", "arrival-continue", "voyage-finish" })
                {
                    var primary = destinationPanel.Q<Button>(id);
                    if (primary == null) continue;
                    primary.RemoveFromHierarchy(); primary.AddToClassList("column-primary"); navigation.Add(primary);
                }
            workspace.Add(navigation);
            root.Add(workspace);
            // Hovering a node option or service explains it right beside that button.
            choiceDetail = Element("choice-detail"); choiceDetail.name = "choice-detail"; choiceDetail.style.display = DisplayStyle.None;
            root.Add(choiceDetail);
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
            else if(session.DebriefPending||session.RegionIntroPending)BuildFlow();
            else if(replacingReward)BuildRewardReplacement();
            else if (supportOpen) BuildSupportManifest();
            else if (deckOpen) BuildActionBook();
            else if (journalOpen) BuildJournal();
            else if (paused) BuildPause();
            root.schedule.Execute(() =>
            {
                foreach (var scroll in root.Query<ScrollView>().ToList())
                    if (scrollPositions.TryGetValue(scroll.name, out var offset)) scroll.scrollOffset = offset;
            });
            if (!string.IsNullOrEmpty(focus) && !session.DebriefPending && !session.RegionIntroPending && !replacingReward) root.schedule.Execute(() =>
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
            if (session.Phase != VoyagePhase.Reward && session.Phase != VoyagePhase.Sailing)
                panel.Add(Action("voyage-support-manifest", "船上清单", "secondary", () => OpenSupport(0), saved));
            var stop = session.Selected ?? session.Current;
            if (session.Phase == VoyagePhase.ChoosingRoute && session.Selected == null)
            {
                BuildNodeServices(panel,session,saved);
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
            if (session.Phase != VoyagePhase.Sailing && session.Phase != VoyagePhase.Reward)
                panel.Add(Text(session.Phase == VoyagePhase.Completed
                    ? session.IsDebug ? "本次独立试玩已结束。" : "归航绳已收回，调查记录带离深渊。"
                    : stop.Clue, "destination-clue"));
            // Once arrived, the choices themselves say what the stop offers.
            if (session.Phase == VoyagePhase.ChoosingRoute)
            {
                var preview = PreviewRoute(session, stop);
                if (!string.IsNullOrEmpty(preview))
                {
                    var summary = Text(preview.Split('\n')[0], "preparation-summary");
                    summary.tooltip = preview;
                    panel.Add(summary);
                }
            }
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
                        if (!string.IsNullOrEmpty(session.EventTitle))
                            panel.Add(Text(session.EventTitle, "event-title"));
                        var nodeChoices = session.GetNodeChoices();
                        AddNodeChoiceGroup(panel, "本节点选项", nodeChoices, saved, false);
                        if (nodeChoices.Count == 0 && session.GetNodeServices().Count == 0)
                            panel.Add(Action("arrival-continue", "记录并继续", "primary", () => Change(Session.Continue), saved && session.CanContinue));
                        BuildNodeServices(panel, session, saved);
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
                    BuildVoyageSummary(panel, session);
                    panel.Add(Action("voyage-finish", "返回标题", "primary", () => GameSceneLoader.Load(VoyageRuntime.MainMenuScene))); break;
            }
        }
        private static void BuildVoyageSummary(VisualElement panel, VoyageSession session)
        {
            var summary = session.Summary;
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "voyage-summary" };
            ScrollWheel.ForwardFrom(panel, scroll);
            scroll.AddToClassList("voyage-summary");
            if (!string.IsNullOrEmpty(summary.DeathCause)) scroll.Add(Text(summary.DeathCause, "body"));
            scroll.Add(Text($"生命 {summary.Health} · 理智 {summary.Sanity} · 补给 {summary.Supplies}\n航行 {summary.ElapsedHours} 小时 · 使用 {summary.SpentSupplies} 补给", "body"));
            scroll.Add(Text("航迹", "section-label"));
            foreach (var stop in summary.Route) scroll.Add(Text(stop, "body"));
            scroll.Add(Text("学到的行动", "section-label"));
            scroll.Add(Text(string.Join("、", summary.LearnedCards), "body"));
            scroll.Add(Text("使用的物资", "section-label"));
            scroll.Add(Text(summary.SpentItems.Count == 0 ? "无" : string.Join("、", summary.SpentItems), "body"));
            panel.Add(scroll);
        }
        private void BuildRewards(VisualElement panel, VoyageSession session, bool saved)
        {
            panel.Add(Text(session.RewardPicksRemaining > 1 ? $"选择收获 · 剩余 {session.RewardPicksRemaining}" : "选择收获", "destination-title"));
            var cards = Element("reward-cards");
            foreach (var reward in session.RewardCards)
            {
                var card = reward;
                var button = Action("reward-" + card.Id, string.Empty, "reward-card",
                    () => { selectedReward = card.Id; Refresh(); }, saved);
                var face = Element("reward-card-face"); IllustratedCard.Apply(face, card); button.Add(face);
                button.EnableInClassList("selected", selectedReward == card.Id);
                button.tooltip = UiRules.CardDescription(card);
                var applicability = BossApplicability.CardLabel(card);
                if (!string.IsNullOrEmpty(applicability)) UiKeywords.Attach(button, applicability);
                cards.Add(button);
            }
            panel.Add(cards);
            // Equipment has no card face, so hovering or focusing it shows its effect in the empty right-hand space.
            var detail = Element("reward-detail"); detail.name = "reward-detail";
            var detailKind = Text("装备", "reward-detail-kind"); var detailName = Text("", "reward-detail-name"); var detailBody = Text("", "reward-detail-body");
            detail.Add(detailKind); detail.Add(detailName); detail.Add(detailBody);
            void ShowDetail(ItemSpec shown)
            {
                if (shown == null) { detail.style.display = DisplayStyle.None; return; }
                detailName.text = shown.Name;
                var tag = BossApplicability.ItemLabel(shown);
                detailBody.text = shown.Description + (string.IsNullOrEmpty(tag) ? "" : "\n\n" + tag);
                detail.style.display = DisplayStyle.Flex;
            }
            var selectedItem = session.RewardItems.FirstOrDefault(x => selectedReward == "item:" + x.Id);
            ShowDetail(selectedItem);
            root.Add(detail);
            // Place beside the card row when the screen has room, otherwise below the reward actions (narrow ratios).
            void PlaceDetail(GeometryChangedEvent _)
            {
                var row = cards.worldBound; var width = root.resolvedStyle.width;
                if (float.IsNaN(width) || row.width <= 0) return;
                var room = width - row.xMax - 56;
                if (room >= 220)
                {
                    detail.style.left = row.xMax + 28; detail.style.width = Mathf.Min(320, room);
                    detail.style.top = row.center.y; detail.style.translate = new Translate(0, Length.Percent(-50));
                }
                else
                {
                    var below = panel.worldBound;
                    detail.style.width = 320; detail.style.left = Mathf.Max(24, row.center.x - 160);
                    detail.style.top = below.yMax + 14; detail.style.translate = new Translate(0, 0);
                }
            }
            cards.RegisterCallback<GeometryChangedEvent>(PlaceDetail);
            panel.RegisterCallback<GeometryChangedEvent>(PlaceDetail);
            foreach (var item in session.RewardItems)
            {
                var id = item.Id;
                var shownItem = item;
                var itemButton = Action("reward-item-" + id, string.Empty, "reward-item",
                    () => { selectedReward = "item:" + id; Refresh(); }, saved);
                itemButton.EnableInClassList("selected", selectedReward == "item:" + id);
                itemButton.Add(Text("装备", "reward-item-kind"));
                itemButton.Add(Text(item.Name, "reward-item-name"));
                itemButton.RegisterCallback<PointerEnterEvent>(_ => ShowDetail(shownItem));
                itemButton.RegisterCallback<FocusInEvent>(_ => ShowDetail(shownItem));
                itemButton.RegisterCallback<PointerLeaveEvent>(_ => ShowDetail(selectedItem));
                itemButton.RegisterCallback<FocusOutEvent>(_ => ShowDetail(selectedItem));
                var applicability = BossApplicability.ItemLabel(item);
                if (!string.IsNullOrEmpty(applicability)) UiKeywords.Attach(itemButton, applicability);
                panel.Add(itemButton);
            }
            if (selectedReward != null)
            {
                if(selectedReward.StartsWith("item:",StringComparison.Ordinal))panel.Add(Action("reward-confirm","领取装备","primary",()=>ClaimSelectedReward(),saved));
                else
                {
                    var tag=UiRules.BossTag(CardCatalog.Get(selectedReward));
                    if(tag!="")panel.Add(Text(tag,"boss-compatibility"));
                    panel.Add(Action("reward-replace","直接编入（替换一张）","primary",()=>{replacingReward=true;Refresh();},saved));
                    panel.Add(Action("reward-confirm","仅收入行动簿","secondary",()=>ClaimSelectedReward(),saved));
                }
            }
            var suppliesFull = session.Supplies >= 8;
            var skip = Action("reward-supplies", suppliesFull ? "放弃：补给已满" : "放弃：+1 补给", "secondary",
                () => {selectedReward=null;Change(() => Session.ClaimReward(null), suppliesFull ? "已跳过奖励；补给已满，没有额外收益。" : "已将所得换为 1 补给。");}, saved);
            skip.AddToClassList("reward-skip");
            panel.Add(skip);
        }

        private void AddNodeChoiceGroup(VisualElement panel, string title, IEnumerable<VoyageNodeChoice> choices, bool saved, bool service)
        {
            var list = choices.ToArray();
            if (list.Length == 0) return;
            panel.Add(Text(title, "section-label"));
            foreach (var nodeChoice in list)
            {
                var choice = nodeChoice;
                var button = Action((service ? "node-service-" : "node-choice-") + choice.Id, string.Empty, "node-choice",
                    () => Change(() => service ? Session.ManageSupport(choice.Id) : Session.ChooseNode(choice.Id), choice.Title),
                    saved && choice.Enabled);
                button.tooltip = choice.Description;
                button.Add(Text(choice.Title, "node-choice-title"));
                button.Add(Text(choice.Description, "node-choice-description"));
                var item = ChoiceItem(choice.Id);
                if (item != null)
                {
                    var applicability = BossApplicability.ItemLabel(item);
                    if (!string.IsNullOrEmpty(applicability)) UiKeywords.Attach(button, applicability);
                }
                AttachChoiceDetail(button, choice);
                panel.Add(button);
            }
        }



        private void BuildActChoice(VisualElement panel, VoyageSession session, bool saved)
        {
            panel.Add(Text(session.Act == 1 ? "选择第二幕航向" : "驶向最终海域", "destination-title"));
            if (session.Act == 1)
            {
                AddRegionChoice(panel, "bones", "尸骸之海", "残骸间的接舷遭遇。", saved);
                AddRegionChoice(panel, "inverted", "倒悬潮庭", "倒悬潮流中的接舷遭遇。", saved);
                AddRegionChoice(panel, "fleet", "无灯裂海", "无灯船队间的接舷遭遇。", saved);
            }
            else
                AddRegionChoice(panel, "abyss", "无星深渊", "终局航向：驶入没有星光回应的洋流。", saved);
        }

        private void BuildNodeServices(VisualElement panel,VoyageSession session,bool saved)
        {
            var services=session.GetNodeServices();
            if(services.Count==0)return;
            var heading = Text("接驳服务", "node-services-title");
            heading.name = "node-services-title";
            panel.Add(heading);
            foreach(var choice in services)
            {
                var entry=choice;
                var button=Action("node-service-"+choice.Id,"","node-choice",()=>Change(()=>Session.ManageSupport(entry.Id)),saved&&choice.Enabled);
                button.Add(Text(choice.Title,"node-choice-title"));
                var note=MaterialChoiceNote(choice);
                button.Add(Text(note,"node-choice-description"));button.tooltip=UiRules.Describe(choice.Description);
                var parts=choice.Id.Split(':');
                if(parts[0]=="buy"&&parts.Length>1)
                {
                    var tag=UiRules.BossTag(SupportCatalog.Item(parts[1].Split('@')[0]));if(tag!="")button.Add(Text(tag,"boss-compatibility"));
                }
                AttachChoiceDetail(button,entry);
                panel.Add(button);
            }
        }

        private void AttachChoiceDetail(Button button, VoyageNodeChoice choice)
        {
            void Show()
            {
                if (choiceDetail == null || chartOpen) return;
                choiceDetail.Clear();
                var (kind, name, effect) = DescribeChoice(choice);
                if (!string.IsNullOrEmpty(kind)) choiceDetail.Add(Text(kind, "choice-detail-kind"));
                choiceDetail.Add(Text(name, "choice-detail-name"));
                if (!string.IsNullOrEmpty(effect)) choiceDetail.Add(Text(effect, "choice-detail-body"));
                // The option line often embeds the full effect; keep only its cost/limit part here.
                var cost = choice.Description ?? "";
                var item = ChoiceItem(choice.Id);
                if (item != null) cost = cost.Replace(item.Description, "");
                cost = cost.Trim().TrimEnd('·').Trim();
                if (cost.Length > 0 && cost != effect) choiceDetail.Add(Text(cost, "choice-detail-cost"));
                if (!choice.Enabled) choiceDetail.Add(Text("当前不可选", "choice-detail-cost"));
                var bounds = button.worldBound; var height = root.resolvedStyle.height;
                choiceDetail.style.left = Mathf.Max(12, bounds.xMin - 14 - 340);
                choiceDetail.style.top = Mathf.Clamp(bounds.yMin, 12, Mathf.Max(12, height - 220));
                choiceDetail.style.display = DisplayStyle.Flex;
            }
            void Hide() { if (choiceDetail != null) choiceDetail.style.display = DisplayStyle.None; }
            button.RegisterCallback<PointerEnterEvent>(_ => Show());
            button.RegisterCallback<PointerLeaveEvent>(_ => Hide());
        }

        private static (string kind, string name, string effect) DescribeChoice(VoyageNodeChoice choice)
        {
            var item = ChoiceItem(choice.Id);
            if (item != null)
            {
                var kind = item.Kind == ItemKind.Equipment ? "装备" : item.Kind == ItemKind.Consumable ? "物资" : item.Kind == ItemKind.Tool ? "调查工具" : "航行工具";
                var tag = BossApplicability.ItemLabel(item);
                return (kind, item.Name, item.Description + (string.IsNullOrEmpty(tag) ? "" : "\n" + tag));
            }
            var parts = choice.Id.Split('@')[0].Split(':');
            var crewId = parts.Length > 1 && (parts[0] == "hire" || parts[0] == "rescue" || parts[0] == "treat" || parts[0] == "trust" || parts[0] == "risk") ? parts[1] : null;
            var crew = crewId == null ? null : SupportCatalog.Crew.FirstOrDefault(x => x.Id == crewId);
            if (crew != null) return ("船员", crew.Name, SupportCatalog.CrewDescription(crew.Kind));
            return ("", choice.Title, "");
        }

        private void ClaimSelectedReward(string replace=null)
        {
            if(paused||Session.DebriefPending||Session.RegionIntroPending||!string.IsNullOrEmpty(saveError)||selectedReward==null)return;
            var card=selectedReward;
            if(!Session.ClaimReward(card,replace))return;
            selectedReward=null;replacingReward=false;
            notice=replace!=null?"新行动已编入牌组。":card.StartsWith("item:",StringComparison.Ordinal)?"装备已收入船上清单。":"已收入行动簿；下个整备点可编入。";
            if(Session.RegionIntroPending){chartOpen=false;var current=Session.Current;sailingPresentation.SetLocation(new Vector2(current.X,current.Y),Vector2.up,current.Name);}
            Save();Refresh();
        }
        private void CancelReplacement(){replacingReward=false;Refresh();root.schedule.Execute(()=>root.Q<Button>("reward-replace")?.Focus());}
        private void BuildRewardReplacement()
        {
            PrepareModal();
            var scrim=Element("voyage-overlay");scrim.name="reward-replacement";scrim.pickingMode=PickingMode.Position;
            var sheet=Element("reward-replacement-sheet");sheet.Add(Text("选择要替换的旧牌","destination-title"));
            var scroll=new ScrollView(ScrollViewMode.Vertical){name="reward-replacement-scroll"};scroll.AddToClassList("action-book-scroll");
            var cards=Element("action-book-cards");
            foreach(var id in Session.DeckIds)
            {
                var oldId=id;var card=CardCatalog.Get(id);
                var button=Action("replace-deck-"+id,"","action-book-card",()=>ClaimSelectedReward(oldId));
                var face=Element("action-book-card-face");IllustratedCard.Apply(face,card);button.Add(face);button.tooltip=UiRules.Describe("替换 "+card.Name+"："+card.Description);cards.Add(button);
            }
            scroll.Add(cards);sheet.Add(scroll);ScrollWheel.ForwardFrom(sheet,scroll);
            sheet.Add(Action("reward-replacement-cancel","取消","secondary",CancelReplacement));
            scrim.RegisterCallback<PointerDownEvent>(evt=>{if(evt.button==1){CancelReplacement();evt.StopImmediatePropagation();}});
            scrim.Add(sheet);root.Add(scrim);root.schedule.Execute(()=>sheet.Q<Button>(className:"action-book-card")?.Focus());
        }
        private void DismissFlow()
        {
            if(!string.IsNullOrEmpty(saveError))return;
            if(Session.DebriefPending)Session.DismissDebrief();else Session.DismissRegionIntro();
            Save();Refresh();root.schedule.Execute(()=>{var first=root.Query<Button>().ToList().FirstOrDefault(x=>x.canGrabFocus);first?.Focus();});
        }
        private void BuildFlow()
        {
            PrepareModal();
            var scrim=Element("voyage-overlay");scrim.name="voyage-flow";scrim.pickingMode=PickingMode.Position;
            var sheet=Element(Session.DebriefPending?"debrief-sheet":"region-intro-sheet");
            if(Session.DebriefPending)
            {
                sheet.Add(Text("战后小结","destination-title"));
                var scroll=new ScrollView(ScrollViewMode.Vertical){name="debrief-scroll"};scroll.AddToClassList("debrief-scroll");
                var result=Session.Debrief;
                scroll.Add(Text("承伤","section-label"));
                scroll.Add(Text($"生命 {result.HealthDamage?.ToString()??"未记录"} · 理智 {result.SanityDamage?.ToString()??"未记录"}","body"));
                scroll.Add(Text("用掉的道具","section-label"));
                scroll.Add(Text(result.SpentItems.Count==0?"无":string.Join("、",result.SpentItems.GroupBy(x=>x).Select(x=>SupportCatalog.Item(x.Key).Name+" ×"+x.Count())),"body"));
                scroll.Add(Text("归档的证据","section-label"));
                if(result.ArchivedEvidence.Count==0)scroll.Add(Text("本场没有形成可归档的已验证证据。","body"));
                foreach(var evidence in result.ArchivedEvidence)scroll.Add(Text(evidence.Text+"\n来源："+evidence.Source,"body"));
                sheet.Add(scroll);ScrollWheel.ForwardFrom(sheet,scroll);
            }
            else
            {
                sheet.Add(Text("第 "+Session.Act+" 幕","section-label"));sheet.Add(Text(RegionName(Session.RegionId),"region-intro-title"));
                sheet.Add(Text(Session.Current.Clue,"body"));
                sheet.EnableInClassList("region-intro-motion",!GameSettings.ReducedMotion);
            }
            sheet.Add(Action("voyage-flow-continue",Session.DebriefPending?"继续":"进入海域","primary",DismissFlow));
            var skip=Action("voyage-flow-skip","跳过","secondary",DismissFlow);if(!Session.DebriefPending)sheet.Add(skip);
            scrim.Add(sheet);root.Add(scrim);root.schedule.Execute(()=>root.Q<Button>("voyage-flow-continue")?.Focus());
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
            sheet.Add(Text("远征行动簿", "journal-title"));
            var body = Element("action-book-body"); sheet.Add(body);
            var side = Element("action-book-side"); body.Add(side);
            var compatibility=Text("","boss-compatibility");side.Add(compatibility);
            var main = Element("action-book-main"); body.Add(main);
            var count = Text("", "collection-count"); main.Add(count);
            var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.AddToClassList("action-book-scroll");
            ScrollWheel.ForwardFrom(sheet, scroll);
            var cards = Element("action-book-cards"); scroll.Add(cards); main.Add(scroll);
            Button save = null;
            void UpdateState()
            {
                count.text = $"{selected.Count}/12";
                save?.SetEnabled(editable && selected.Count == 12);
            }
            foreach (var id in Session.LearnedCardIds)
            {
                var card = CardCatalog.Get(id);
                var button = Action("book-" + id, string.Empty, "action-book-card", null);
                void DescribeCard()=>compatibility.text=UiRules.BossTag(card);
                button.RegisterCallback<FocusInEvent>(_=>DescribeCard());
                button.RegisterCallback<PointerEnterEvent>(_=>DescribeCard());
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
                button.EnableInClassList("new-reward-card",editable&&Session.NewRewardCardIds.Contains(id)&&!Session.DeckIds.Contains(id));
                if(button.ClassListContains("new-reward-card"))button.Add(Text("新收获","new-reward-badge"));
                var face = Element("action-book-card-face"); IllustratedCard.Apply(face, card); button.Add(face);
                button.tooltip = UiRules.CardDescription(card); cards.Add(button);
            }
            var selectedTools = new HashSet<string>(Session.ToolIds, StringComparer.Ordinal);
            var toolTitle = Text($"调查工具 {selectedTools.Count}/2", "tool-count"); side.Add(toolTitle);
            var tools = Element("action-book-tools"); side.Add(tools);
            foreach (var id in Session.LearnedToolIds)
            {
                var toolId = id;
                var tool = Action("tool-" + id, ToolName(id), "action-book-tool", null);
                tool.tooltip = ToolName(id);
                tool.text = SupportCatalog.Item(id).Name + (id == "shell" ? " · 3 理智" : "");
                tool.clicked += () =>
                {
                    if (!editable) return;
                    if (!selectedTools.Remove(toolId))
                    {
                        if (selectedTools.Count >= 2) return;
                        selectedTools.Add(toolId);
                    }
                    tool.EnableInClassList("selected", selectedTools.Contains(toolId));
                    toolTitle.text = $"调查工具 {selectedTools.Count}/2";
                };
                tool.EnableInClassList("selected", selectedTools.Contains(id)); tools.Add(tool);
            }
            var actions = Element("modal-actions"); side.Add(actions);
            actionBookSave = () =>
            {
                if (!editable || selected.Count != 12 || !Session.SetDeck(selected) || !Session.SetTools(selectedTools)) return;
                deckOpen = false; notice = "行动簿与调查工具已整备（可不携带工具）。"; Save(); Refresh();
            };
            save = KeyBadge.Attach(Action("action-book-save", "保存", "primary", actionBookSave, editable && selected.Count == 12), "S");
            if (editable) actions.Add(save);
            actions.Add(KeyBadge.Attach(Action("action-book-close", editable ? "取消" : "关闭", "secondary", () => { deckOpen = false; Refresh(); }), "Esc"));
            scrim.Add(sheet); root.Add(scrim);
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
            id == "listener" ? "定向听筒 · 声源" : id == "shell" ? "听潮贝壳 · 伤害途径（支付 3 理智）" : SupportCatalog.Item(id).Name;

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

        private static void Resource(VisualElement parent, string motif, string title, string value)
        {
            var chip = Element("resource-chip"); chip.pickingMode = PickingMode.Position; chip.tooltip = title;
            var icon = new SeaUiArt(motif); icon.AddToClassList("resource-icon"); chip.Add(icon);
            chip.Add(Text(value, "resource-value"));
            parent.Add(chip);
        }

        private static void SanityResource(VisualElement parent, VoyageSession session)
        {
            var chip = Element("resource-chip"); chip.AddToClassList("sanity-chip");
            chip.pickingMode = PickingMode.Position; chip.tooltip = "理智\n" + SanityGauge.RuleTooltip;
            var icon = new SeaUiArt("eye"); icon.AddToClassList("resource-icon"); chip.Add(icon);
            chip.Add(Text($"{session.Sanity}/30", "resource-value"));
            var state = SanityGauge.StateTag(session.Sanity); if (state != null) chip.Add(state);
            parent.Add(chip);
        }

        private static void StatusSeal(VisualElement parent, string motif, string title, string tooltip)
        {
            var seal = Element("voyage-status-seal"); seal.pickingMode = PickingMode.Position; seal.tooltip = tooltip;
            var icon = new SeaUiArt(motif); icon.AddToClassList("status-seal-icon"); seal.Add(icon);
            seal.Add(Text(title, "status-seal-label"));
            parent.Add(seal);
        }

        private void PrepareModal()
        {
            root.focusable = false;
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
            ScrollWheel.ForwardFrom(sheet, entries);
            sheet.Add(KeyBadge.Attach(Action("journal-close", "收起日志", "primary", () => { journalOpen = false; Refresh(); }), "Esc"));
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
                pause.Add(Text("暂停", "pause-overline"));
                pause.Add(KeyBadge.Attach(Action("pause-resume", "继续", "pause-option", () => { paused = false; Refresh(); }), "Esc"));
                pause.Add(Action("pause-settings", "设置", "pause-option", () => { settingsOpen = true; Refresh(); }));
                pause.Add(Action("pause-title", "保存并返回标题", "pause-option", () => { if (GameSceneLoader.IsLoading) return; Save(); if (string.IsNullOrEmpty(saveError)) GameSceneLoader.Load(VoyageRuntime.MainMenuScene); else Refresh(); }));
                scrim.Add(pause);
            }
            root.Add(scrim); if (!settingsOpen) root.schedule.Execute(() => root.Q<Button>("pause-resume")?.Focus());
        }
    }
}
