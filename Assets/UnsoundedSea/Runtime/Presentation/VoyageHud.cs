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
        private bool paused, settingsOpen, journalOpen, chartOpen, arrivalFade;
        private string saveError, loadError, notice;
        private float travelElapsed, lastTick;
        private VoyageSession Session => VoyageRuntime.Session;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            try
            {
                if (interfaceFont == null || interfaceStyle == null || menuStyle == null || sailingPresentation == null)
                    throw new InvalidOperationException("Assign the voyage font, stylesheets and 3D sailing presentation.");
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
                Refresh();
            }
            catch (Exception exception) { loadError = exception.Message; Refresh(); Debug.LogException(exception); }
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
                if (journalOpen) journalOpen = false;
                else if (chartOpen && !paused) chartOpen = false;
                else paused = !paused;
            }
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
                case VoyagePhase.Completed: return "航程完成";
                default: return "航程终止";
            }
        }
        private void Refresh()
        {
            if (root == null) return;
            var focus = (root.panel?.focusController.focusedElement as VisualElement)?.name;
            sailing?.Pause();
            root.Clear(); root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion);
            root.EnableInClassList("chart-open", chartOpen);
            root.EnableInClassList("is-sailing", Session?.Phase == VoyagePhase.Sailing);
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
            var heading = Element("voyage-heading");
            heading.Add(Text("THE UNSOUNDED SEA / GREY MIST PASSAGE", "overline"));
            heading.Add(Text("灰雾航道", "voyage-title")); header.Add(heading);
            var status = Element("voyage-status");
            status.Add(Text(PhaseName(session.Phase), "phase-label"));
            status.Add(Text("自动节点航行 · 无需驾驶", "muted")); header.Add(status);
            var utilities = Element("voyage-utilities");
            utilities.Add(Action("voyage-chart-toggle", chartOpen ? "收起海图 / M" : "展开海图 / M", "secondary", () => { chartOpen = !chartOpen; Refresh(); }, session.Phase != VoyagePhase.Sailing));
            utilities.Add(Action("voyage-journal", "航海日志 / J", "secondary", () => { journalOpen = true; Refresh(); }));
            utilities.Add(Action("voyage-menu", "菜单 / Esc", "secondary", () => { paused = true; Refresh(); }));
            header.Add(utilities); root.Add(header);

            var workspace = Element("voyage-workspace");
            var report = Element("ship-report");
            report.Add(Text("调查船 / 当前位置", "section-label"));
            report.Add(Text(session.Current.Name, "panel-title"));
            report.Add(Text(session.Current.Clue, "body"));
            var facts = Element("voyage-facts");
            facts.Add(Text($"累计航行 {session.ElapsedHours} 小时 / {session.LegsSailed} 段", "fact"));
            facts.Add(Text($"已知地点 {session.KnownStops.Count} 处", "fact")); report.Add(facts);
            report.Add(Text("已知航向", "section-label"));
            if (session.Phase == VoyagePhase.ChoosingRoute)
                foreach (var stop in session.Choices)
                {
                    var destination = stop;
                    var choice = Action("route-" + stop.Id, session.HeadingTo(stop) + " / " + stop.Name, "route-choice",
                        () => Change(() => Session.Select(destination.Id)), string.IsNullOrEmpty(saveError));
                    choice.EnableInClassList("selected", session.Selected?.Id == stop.Id); report.Add(choice);
                }
            else if (session.Phase == VoyagePhase.Completed || session.Phase == VoyagePhase.Failed)
                report.Add(Text("本次航路已封存。可以查阅日志或返回标题。", "muted"));
            else report.Add(Text(session.Phase == VoyagePhase.Sailing ? "航向已确认。抵达前不能改变目的地。" : "先处理当前地点，再决定下一段航向。", "muted"));
            report.Add(Text("航海工具 / 本航程次数", "section-label"));
            report.Add(Action("reveal-routes", $"查阅旧海图 / {session.ChartUses} 次\n揭开当前位置未测的出口", "navigation-tool", () =>
            {
                chartOpen = true; Change(Session.RevealRoutes, "已对照旧海图，补记当前岔口的航路。");
            }, session.CanRevealRoutes && string.IsNullOrEmpty(saveError)));
            report.Add(Action("scout-ahead", $"测绘望远镜 / {session.LookoutUses} 次\n侦察选定航向再往前一段", "navigation-tool", () =>
            {
                chartOpen = true; Change(Session.ScoutAhead, "远望记录已加入海图，仅补记选定航向的下一段。");
            }, session.CanScoutAhead && string.IsNullOrEmpty(saveError)));
            report.Add(Text(session.Phase == VoyagePhase.Completed || session.Phase == VoyagePhase.Failed
                ? "本次航程已经结束，工具不再消耗。"
                : session.Selected == null ? "先选航向才能远望。没有新信息时不消耗次数。" : "道具只扩展已知航路，不改变地图或遭遇。", "fine-print"));
            workspace.Add(report);

            VoyageChart chart = null;
            if (chartOpen)
            {
                var chartFrame = Element("chart-frame");
                var chartHeading = Element("chart-heading");
                chartHeading.Add(Text("灰雾航道 · 海上航路图", "chart-title"));
                chartHeading.Add(Text("只记录已知航路", "chart-note")); chartFrame.Add(chartHeading);
                chart = new VoyageChart(session, id => Change(() => Session.Select(id)), string.IsNullOrEmpty(saveError)); chartFrame.Add(chart);
                var legend = Element("chart-legend");
                foreach (var item in new[] { new[] { "current", "在此" }, new[] { "available", "可航行" }, new[] { "selected", "已选" }, new[] { "visited", "已航经" }, new[] { "charted", "已测未抵达" } })
                {
                    var entry = Element("legend-item"); var mark = Element("legend-mark"); mark.AddToClassList(item[0]);
                    entry.Add(mark); entry.Add(Text(item[1], "legend-label")); legend.Add(entry);
                }
                chartFrame.Add(legend); workspace.Add(chartFrame);
            }
            else
            {
                var sea = Element("sea-window");
                sea.Add(Text(session.Selected == null ? "海雾以外，尚有未测之路" : "下一航向 / " + session.HeadingTo(session.Selected), "sea-bearing"));
                sea.Add(Text("展开海图规划航路，或从左侧选择已知航向。", "sea-note")); workspace.Add(sea);
            }
            var destinationPanel = Element("destination-panel");
            BuildDestination(destinationPanel, session, chart); workspace.Add(destinationPanel); root.Add(workspace);
            var footer = Element("voyage-footer");
            footer.Add(Text(notice ?? "选择航向只作预览。确认启航后，调查船沿已选路线自动前进。", "footer-note"));
            footer.Add(Text($"航程种子 {session.Seed} / 迷雾遮蔽知识，不改写航路", "footer-motto")); root.Add(footer);
            if (arrivalFade)
            {
                arrivalFade = false;
                var fog = Element("sailing-fog"); fog.AddToClassList("arrival-fog");
                root.Insert(0, fog);
                fog.schedule.Execute(() => fog.style.opacity = 0).StartingIn(16);
                fog.schedule.Execute(fog.RemoveFromHierarchy).StartingIn(350);
            }
            if (!string.IsNullOrEmpty(saveError))
            {
                var error = Element("save-error"); error.pickingMode = PickingMode.Position;
                error.Add(Text("航海记录尚未保存 / " + saveError, "body"));
                error.Add(Action("save-retry", "重试保存", "primary", () => { Save(); Refresh(); })); root.Add(error);
            }
            if (journalOpen) BuildJournal();
            if (paused) BuildPause();
            if (!string.IsNullOrEmpty(focus)) root.schedule.Execute(() =>
            {
                var target = root.Q<Button>(focus);
                if (target == null || !target.canGrabFocus) target = root.Q<Button>("set-sail") ?? root.Q<Button>("voyage-chart-toggle");
                if (target != null && target.canGrabFocus) target.Focus();
            });
        }
        private void BuildDestination(VisualElement panel, VoyageSession session, VoyageChart chart)
        {
            bool saved = string.IsNullOrEmpty(saveError);
            var stop = session.Selected ?? session.Current;
            if (session.Phase == VoyagePhase.ChoosingRoute && session.Selected == null)
            {
                panel.Add(Text("航向待定", "section-label")); panel.Add(Text("下一段未知", "destination-title"));
                panel.Add(Text("你只看得到已经测得的出口。远方和部分岔路仍藏在雾里。", "body"));
                panel.Add(Text("选择已知航路即可继续。旧海图可以找出当前位置隐藏的岔路；望远镜可查看选定航向的下一段。", "muted"));
                panel.Add(Action("set-sail", "先选择目的地", "primary", null, false)); return;
            }
            panel.Add(Text(session.Phase == VoyagePhase.ChoosingRoute ? "选定目的地" : PhaseName(session.Phase), "section-label"));
            panel.Add(Text(stop.Name, "destination-title"));
            if (session.Selected != null) panel.Add(Text($"航向 {session.HeadingTo(stop)} / 预计 {stop.TravelHours} 小时", "bearing"));
            panel.Add(Text(stop.Clue, "body"));
            if (session.Phase != VoyagePhase.ChoosingRoute && session.Phase != VoyagePhase.Sailing)
                panel.Add(Text(stop.Description, "muted"));
            switch (session.Phase)
            {
                case VoyagePhase.ChoosingRoute:
                    panel.Add(Action("set-sail", "确认航向 / 启航", "primary", () => { travelElapsed = 0; chartOpen = false; Change(Session.SetSail); }, saved));
                    panel.Add(Text("航行不会暗中扣除生命、理智或战斗行动。", "fine-print")); break;
                case VoyagePhase.Sailing:
                    var track = Element("sailing-track"); var fill = Element("sailing-fill"); track.Add(fill); panel.Add(track);
                    panel.Add(Text("正在穿越海雾。航行动画结束后抵达。", "bearing"));
                    if (saved)
                    {
                        var veil = Element("sailing-fog"); veil.name = "sailing-fog";
                        root.Insert(0, veil); ScheduleArrival(chart, fill, veil);
                    }
                    break;
                case VoyagePhase.Arrived:
                    panel.Add(Text("航海记录已更新", "arrival-label"));
                    bool encounter = stop.Kind == RunNodeKind.Battle || stop.Kind == RunNodeKind.Boss;
                    panel.Add(Action(encounter ? "arrival-investigate" : "arrival-continue", encounter ? "接近并调查" : "记录观测 / 继续航行", "primary",
                        encounter ? (System.Action)EnterEncounter : () => Change(Session.Continue), saved));
                    if (!encounter) panel.Add(Text("记录航路观测，不自动补充资源或发放奖励。", "fine-print")); break;
                case VoyagePhase.Encounter:
                    panel.Add(Text("调查尚未结算。继续时从本场遭遇开始，局内操作不属于航海存档。", "body"));
                    panel.Add(Action("resume-encounter", "继续遭遇调查", "primary", () => SceneManager.LoadScene(VoyageRuntime.BattleScene), saved)); break;
                case VoyagePhase.Completed:
                    panel.Add(Text("你带回了航路记录，而不是深渊的全部解释。", "body"));
                    panel.Add(Action("voyage-finish", "返回标题", "primary", () => SceneManager.LoadScene(VoyageRuntime.MainMenuScene))); break;
                case VoyagePhase.Failed:
                    panel.Add(Text("调查无法继续。日志仍保留这次走过的路线。", "body"));
                    panel.Add(Action("voyage-finish", "返回标题", "primary", () => SceneManager.LoadScene(VoyageRuntime.MainMenuScene))); break;
            }
        }
        private void ScheduleArrival(VoyageChart chart, VisualElement fill, VisualElement veil)
        {
            if (!sailingPresentation.IsPlaying)
            {
                var from = Session.Current; var to = Session.Selected;
                sailingPresentation.Begin(new Vector2(to.X - from.X, to.Y - from.Y).normalized, GameSettings.ReducedMotion);
            }
            lastTick = Time.realtimeSinceStartup;
            sailing = root.schedule.Execute(() =>
            {
                var now = Time.realtimeSinceStartup; var delta = now - lastTick; lastTick = now;
                if (paused || settingsOpen || journalOpen || Session.Phase != VoyagePhase.Sailing) return;
                travelElapsed += delta;
                var progress = Mathf.Clamp01(travelElapsed / (GameSettings.ReducedMotion ? 0.25f : sailingPresentation.Duration));
                fill.style.width = Length.Percent(progress * 100); chart?.SetProgress(progress);
                sailingPresentation.SetProgress(progress);
                veil.style.opacity = sailingPresentation.TransitionOpacity;
                if (progress < 1) return;
                sailing.Pause(); sailingPresentation.Finish(); arrivalFade = true;
                Change(Session.Arrive);
            }).Every(33);
        }
        private void BuildJournal()
        {
            var scrim = Element("voyage-overlay"); scrim.pickingMode = PickingMode.Position;
            var sheet = Element("voyage-journal");
            sheet.Add(Text("航海日志", "journal-title")); sheet.Add(Text("已走过的路线与当时观测", "muted"));
            var entries = new ScrollView(); foreach (var entry in Session.Log) entries.Add(Text(entry, "journal-entry")); sheet.Add(entries);
            sheet.Add(Action("journal-close", "收起日志 / Esc", "primary", () => { journalOpen = false; Refresh(); }));
            scrim.Add(sheet); root.Add(scrim); root.schedule.Execute(() => root.Q<Button>("journal-close")?.Focus());
        }
        private void BuildPause()
        {
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

    internal sealed class VoyageChart : VisualElement
    {
        private readonly VoyageStop departure, destination;
        private readonly string selectedId;
        private readonly IReadOnlyList<string> route;
        private readonly IReadOnlyList<VoyageRoute> links;
        private readonly HashSet<string> choices;
        private readonly Dictionary<string, VoyageStop> stops;
        private readonly VisualElement ship;
        private static readonly Vector2[][] CurrentLines =
        {
            new[] { new Vector2(3, 85), new Vector2(8, 72), new Vector2(3, 59), new Vector2(9, 45), new Vector2(3, 29), new Vector2(7, 15) },
            new[] { new Vector2(94, 18), new Vector2(89, 32), new Vector2(95, 48), new Vector2(90, 64), new Vector2(96, 80) }
        };
        public VoyageChart(VoyageSession session, System.Action<string> select, bool enabled)
        {
            departure = session.Current;
            destination = session.Phase == VoyagePhase.Sailing ? session.Selected : departure;
            selectedId = session.Selected?.Id;
            route = session.Visited; links = session.KnownRoutes;
            choices = new HashSet<string>(session.Choices.Select(x => x.Id));
            stops = session.KnownStops.ToDictionary(x => x.Id);
            AddToClassList("nautical-chart"); name = "voyage-chart"; pickingMode = PickingMode.Ignore;
            foreach (var stop in session.KnownStops)
            {
                var id = stop.Id; var node = new Button(() => select(id)) { name = "node-" + id };
                node.AddToClassList("chart-node"); bool current = id == departure.Id;
                var state = current ? "current" : selectedId == id ? "selected" : choices.Contains(id) ? "available" : route.Contains(id) ? "visited" : "charted";
                node.AddToClassList(state); node.SetEnabled(enabled && session.Phase == VoyagePhase.ChoosingRoute && choices.Contains(id));
                node.style.left = Length.Percent(stop.X); node.style.top = Length.Percent(100 - stop.Y);
                var marker = new VisualElement { pickingMode = PickingMode.Ignore }; marker.AddToClassList("node-marker"); node.Add(marker);
                var title = new Label(stop.Name) { pickingMode = PickingMode.Ignore }; title.AddToClassList("node-name"); node.Add(title);
                var label = new Label(current ? "在此" : state == "selected" ? "已选航向" : state == "available" ? "可航行" : state == "visited" ? "已航经" : "已测未抵达") { pickingMode = PickingMode.Ignore };
                label.AddToClassList("node-state"); node.Add(label); Add(node);
            }
            ship = new VisualElement { name = "chart-ship", pickingMode = PickingMode.Ignore }; ship.AddToClassList("chart-ship"); Add(ship); SetProgress(0);
            var compass = new VisualElement { pickingMode = PickingMode.Ignore }; compass.AddToClassList("chart-compass");
            var north = new Label("北 / N") { pickingMode = PickingMode.Ignore }; compass.Add(north);
            var needle = new VisualElement { pickingMode = PickingMode.Ignore }; needle.AddToClassList("compass-needle"); compass.Add(needle); Add(compass);
            if (session.KnownStops.All(x => x.Layer < 5))
            {
                var fog = new Label("未测海雾\n前方航路尚未记录") { pickingMode = PickingMode.Ignore };
                fog.AddToClassList("chart-fog"); Add(fog);
            }
            generateVisualContent += Draw;
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
        }
        public void SetProgress(float progress)
        {
            ship.style.left = Length.Percent(Mathf.Lerp(departure.X, destination.X, progress));
            ship.style.top = Length.Percent(100 - Mathf.Lerp(departure.Y, destination.Y, progress));
        }
        private Vector2 Point(float x, float y) => new Vector2(contentRect.width * x / 100, contentRect.height * (100 - y) / 100);
        private void Draw(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            painter.lineWidth = 1; painter.strokeColor = new Color(0.31f, 0.35f, 0.29f, 0.12f);
            for (int i = 10; i < 100; i += 10)
            {
                painter.BeginPath(); painter.MoveTo(Point(i, 0)); painter.LineTo(Point(i, 100)); painter.Stroke();
                painter.BeginPath(); painter.MoveTo(Point(0, i)); painter.LineTo(Point(100, i)); painter.Stroke();
            }
            foreach (var current in CurrentLines)
            {
                painter.BeginPath(); painter.MoveTo(Point(current[0].x, current[0].y));
                for (int i = 1; i < current.Length; i++) painter.LineTo(Point(current[i].x, current[i].y));
                painter.strokeColor = new Color(0.25f, 0.4f, 0.44f, 0.22f); painter.lineWidth = 1.5f; painter.Stroke();
            }
            foreach (var link in links)
            {
                var from = stops[link.FromId]; var to = stops[link.ToId]; bool travelled = false;
                for (int i = 1; i < route.Count; i++) if (route[i - 1] == from.Id && route[i] == to.Id) { travelled = true; break; }
                bool selected = from.Id == departure.Id && selectedId == to.Id;
                bool available = from.Id == departure.Id && choices.Contains(to.Id);
                painter.lineWidth = selected ? 3 : available || travelled ? 2 : 1.5f;
                painter.strokeColor = selected ? new Color(0.65f, 0.33f, 0.12f) : travelled ? new Color(0.2f, 0.4f, 0.34f) : available ? new Color(0.5f, 0.36f, 0.18f) : new Color(0.31f, 0.43f, 0.4f, 0.7f);
                painter.BeginPath(); painter.MoveTo(Point(from.X, from.Y)); painter.LineTo(Point(to.X, to.Y)); painter.Stroke();
            }
        }
    }
}
