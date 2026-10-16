using System;
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
        [SerializeField] private BattleScenario encounterScenario;
        private VisualElement root;
        private IVisualElementScheduledItem sailing;
        private bool paused, settingsOpen, journalOpen, chartOpen;
        private string saveError, loadError, notice;
        private float lastTick;
        private VoyageSession Session => VoyageRuntime.Session;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            try
            {
                if (interfaceFont == null || interfaceStyle == null || menuStyle == null || sailingPresentation == null ||
                    encounterScenario == null)
                    throw new InvalidOperationException("Assign the voyage font, stylesheets, sailing presentation and encounter entries.");
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
                paused || settingsOpen || journalOpen || chartOpen || !string.IsNullOrEmpty(saveError) || !string.IsNullOrEmpty(loadError),
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
            try { VoyageRuntime.EnterEncounter(encounterScenario); }
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
            var selected = session.Selected;
            var header = Element("voyage-header");
            var utilities = Element("voyage-utilities");
            utilities.Add(Action("voyage-chart-toggle", chartOpen ? "收起海图  M" : "海图  M", "secondary", () => { chartOpen = !chartOpen; Refresh(); }, session.Phase != VoyagePhase.Sailing));
            utilities.Add(Action("voyage-journal", "日志  J", "secondary", () => { journalOpen = true; Refresh(); }));
            utilities.Add(Action("voyage-menu", "菜单  Esc", "secondary", () => { paused = true; Refresh(); }));
            header.Add(utilities); root.Add(header);

            var workspace = Element("voyage-workspace");
            var location = Element("current-location");
            location.Add(Text("当前位置", "section-label"));
            location.Add(Text(session.Current.Name, "location-name"));
            location.Add(Text($"{session.ElapsedHours} 小时  ·  {session.LegsSailed} 段", "fact"));
            workspace.Add(location);

            var tools = Element("navigation-tools");
            var chartTool = Action("reveal-routes", $"旧海图  ×{session.ChartUses}", "navigation-tool", () =>
            {
                chartOpen = true; Change(Session.RevealRoutes, "旧海图已补记此处岔路。");
            }, session.CanRevealRoutes && string.IsNullOrEmpty(saveError));
            chartTool.tooltip = "揭开当前位置未测出口；没有新发现时不消耗次数。";
            chartTool.text = string.Empty;
            var chartArt = new SeaUiArt("scroll"); chartArt.AddToClassList("tool-art"); chartTool.Add(chartArt);
            chartTool.Add(Text($"旧海图  ×{session.ChartUses}", "tool-label")); tools.Add(chartTool);
            var lookoutTool = Action("scout-ahead", string.Empty, "navigation-tool", () =>
            {
                chartOpen = true; Change(Session.ScoutAhead, "远望记录已加入海图。");
            }, session.CanScoutAhead && string.IsNullOrEmpty(saveError));
            lookoutTool.tooltip = "需先选择航向；窥探所选航向下一段，没有新发现时不消耗次数。";
            var lookoutArt = new SeaUiArt("ear"); lookoutArt.AddToClassList("tool-art"); lookoutTool.Add(lookoutArt);
            lookoutTool.Add(Text($"远望  ×{session.LookoutUses}", "tool-label")); tools.Add(lookoutTool);
            workspace.Add(tools);

            VoyageChart chart = null;
            if (chartOpen)
            {
                var map = new VoyageMapPanel(session, id => Change(() => Session.Select(id)), string.IsNullOrEmpty(saveError));
                chart = map.Chart;
                workspace.Add(map);
            }
            else
            {
                var sea = Element("sea-window"); workspace.Add(sea);
                if (session.Phase == VoyagePhase.ChoosingRoute)
                {
                    var helm = Element("navigation-helm");
                    var compass = new SeaUiArt("compass"); compass.AddToClassList("helm-compass"); helm.Add(compass);
                    var index = 0;
                    foreach (var stop in session.Choices)
                    {
                        var destination = stop; var slot = Mathf.Min(index++, 2);
                        var choice = Action("route-" + stop.Id, string.Empty, "route-choice",
                            () => Change(() => Session.Select(destination.Id)), string.IsNullOrEmpty(saveError));
                        choice.AddToClassList("route-slot-" + slot);
                        var needle = new SeaUiArt("bearing"); needle.AddToClassList("route-needle");
                        needle.AddToClassList(slot == 0 ? "needle-left" : slot == 1 ? "needle-right" : "needle-center");
                        choice.Add(needle);
                        choice.Add(Text(session.HeadingTo(stop), "route-heading"));
                        choice.Add(Text(stop.Name, "route-name"));
                        choice.EnableInClassList("selected", selected?.Id == stop.Id); helm.Add(choice);
                    }
                    workspace.Add(helm);
                }
            }
            var destinationPanel = Element("destination-panel");
            BuildDestination(destinationPanel, session, chart); workspace.Add(destinationPanel); root.Add(workspace);
            if (!string.IsNullOrEmpty(notice))
            {
                var footer = Element("voyage-footer");
                footer.Add(Text(notice, "footer-note")); root.Add(footer);
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
                panel.Add(Text("择定雾中航向", "destination-empty"));
                var disabled = Action("set-sail", string.Empty, "seal-action", null, false);
                disabled.Add(new SeaUiArt("seal")); disabled.Add(Text("待定", "seal-label")); panel.Add(disabled); return;
            }
            panel.Add(Text(session.Phase == VoyagePhase.ChoosingRoute ? "已定航向" : PhaseName(session.Phase), "section-label"));
            panel.Add(Text(stop.Name, "destination-title"));
            if (session.Selected != null) panel.Add(Text($"{session.HeadingTo(stop)}  ·  {stop.TravelHours} 小时", "bearing"));
            panel.Add(Text(stop.Clue, "destination-clue"));
            switch (session.Phase)
            {
                case VoyagePhase.ChoosingRoute:
                    var sail = Action("set-sail", string.Empty, "seal-action", () => { chartOpen = false; Change(Session.SetSail); }, saved);
                    sail.Add(new SeaUiArt("seal")); sail.Add(Text("启航", "seal-label")); panel.Add(sail);
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
                    panel.Add(Action(encounter ? "arrival-investigate" : "arrival-continue", encounter ? "接近并调查" : "记录并继续", "primary",
                        encounter ? (System.Action)EnterEncounter : () => Change(Session.Continue), saved));
                    break;
                case VoyagePhase.Encounter:
                    var resume = Action("resume-encounter", "继续调查", "primary", EnterEncounter, saved);
                    resume.tooltip = "调查尚未结算；继续时从本场遭遇开始，局内操作不属于航海存档。";
                    panel.Add(resume); break;
                case VoyagePhase.Completed:
                    panel.Add(Action("voyage-finish", "返回标题", "primary", () => SceneManager.LoadScene(VoyageRuntime.MainMenuScene))); break;
                case VoyagePhase.Failed:
                    panel.Add(Action("voyage-finish", "返回标题", "primary", () => SceneManager.LoadScene(VoyageRuntime.MainMenuScene))); break;
            }
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
            var scrim = Element("voyage-overlay"); scrim.pickingMode = PickingMode.Position;
            var sheet = Element("voyage-journal");
            sheet.Add(Text("航海日志", "journal-title")); sheet.Add(Text("航迹与观测", "muted"));
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
}
