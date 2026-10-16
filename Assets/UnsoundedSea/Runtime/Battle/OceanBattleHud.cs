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
    [RequireComponent(typeof(UIDocument), typeof(BattleDirector))]
    public sealed class OceanBattleHud : MonoBehaviour
    {
        public FontAsset font;
        public StyleSheet styleSheet;
        public StyleSheet menuStyle;
        public string menuScene = "MainMenu";
        private BattleDirector director;
        private VisualElement root, overlay;
        private readonly List<Button> nameplates = new List<Button>();
        private bool showSettings;
        private bool chartOpen, chartWasPaused;
        public int RenderedFrames { get; private set; }
        private void OnEnable()
        {
            director = GetComponent<BattleDirector>(); root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear(); root.AddToClassList("ocean-ui"); root.pickingMode = PickingMode.Ignore; root.focusable = true;
            if (menuStyle != null) root.styleSheets.Add(menuStyle);
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            director.Changed += Render;
            root.RegisterCallback<KeyDownEvent>(Key);
            Render();
        }
        private void OnDisable()
        {
            chartOpen = false; chartWasPaused = false; showSettings = false;
            director.Changed -= Render; root.UnregisterCallback<KeyDownEvent>(Key); root.Clear();
        }
        private void Key(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape && chartOpen) CloseChart();
            else if (evt.keyCode == KeyCode.M && !showSettings) ToggleChart();
            else if (evt.keyCode == KeyCode.Escape)
            {
                showSettings = false;
                director.SetPaused(!director.Paused);
            }
            else return;
            evt.StopPropagation();
        }
        private void ToggleChart()
        {
            if (chartOpen) { CloseChart(); return; }
            chartWasPaused = director.Paused;
            chartOpen = true;
            if (!director.Paused) director.SetPaused(true); else Render();
        }
        private void CloseChart()
        {
            chartOpen = false;
            if (director.Paused != chartWasPaused) director.SetPaused(chartWasPaused); else Render();
        }
        private static VisualElement Box(string cls) { var v = new VisualElement { pickingMode = PickingMode.Ignore }; v.AddToClassList(cls); return v; }
        private static Label Text(string text, string cls) { var l = new Label(text) { pickingMode = PickingMode.Ignore }; l.AddToClassList(cls); return l; }
        private static Button Button(string name, string text, Action action, string cls = "sea-button")
        { var b = new Button(action) { name = name, text = text }; b.AddToClassList(cls); return b; }
        private void Render()
        {
            root.Clear(); root.Focus(); root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion); nameplates.Clear(); var v = director.View; if (v == null) return;
            var scenario = director.scenario; bool active = v.Phase == BattlePhase.PlayerTurn && !director.Paused && !director.Busy;
            var surface = Box("battle-surface"); surface.name = "battle-surface"; surface.pickingMode = PickingMode.Position;
            surface.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (director.Stage == null || director.Paused || evt.button != 0) return;
                // Use the battle viewport: an offscreen reflection render can change Screen dimensions.
                var local = root.WorldToLocal(new Vector2(evt.position.x, evt.position.y));
                var point = new Vector3(local.x / root.resolvedStyle.width, 1 - local.y / root.resolvedStyle.height, 0);
                if (Physics.Raycast(director.Stage.battleCamera.ViewportPointToRay(point), out var hit))
                {
                    var actor = hit.collider.GetComponent<BattleActorView>(); if (actor != null) director.Select(actor.EnemyId);
                }
            }); root.Add(surface);
            var top = Box("sea-top"); root.Add(top);
            top.Add(Button("battle-chart-toggle", "海图 M", ToggleChart));
            top.Add(Button("battle-pause", "日志 / 设置", () => director.SetPaused(true)));

            var selected = v.Enemies.FirstOrDefault(x => x.Id == director.SelectedEnemy);

            var tools = Box("tools"); root.Add(tools);
            foreach (var tool in v.Tools)
            {
                var t = tool; var cost = t.Definition.SanityCost > 0 ? $"{t.Definition.SanityCost}理智" : "无代价";
                var b = Button("tool-" + t.Definition.Id, "", () => director.Submit(BattleCommand.Investigate(t.Definition.Id, director.SelectedEnemy)), "tool");
                b.Add(new SeaUiArt(t.Definition.Kind == InvestigationKind.SoundSource ? "ear" : t.Definition.Kind == InvestigationKind.VisibleAction ? "lantern" : "eye"));
                b.Add(Text(t.Definition.Name, "tool-name")); b.Add(Text(t.RemainingUses + (t.Definition.SanityCost > 0 ? $" · {t.Definition.SanityCost}理智" : ""), "tool-meta"));
                b.tooltip = $"{t.Definition.Name}：调查当前目标，剩余 {t.RemainingUses} 次，代价 {cost}";
                b.SetEnabled(active && selected != null && t.RemainingUses > 0 && v.Sanity > t.Definition.SanityCost); tools.Add(b);
            }

            var right = Box("objective-seal"); root.Add(right);
            right.Add(new SeaUiArt("seal"));
            right.Add(Text(scenario.encounter.objective == ObjectiveKind.Seal ? "封线" : scenario.encounter.objective == ObjectiveKind.Survive ? "撤离" : "压制", "objective-label"));
            right.Add(Text($"{v.ObjectiveProgress}/{v.ObjectiveTarget}", "objective-count"));
            right.tooltip = scenario.objectiveHeading + "：" + v.Objective;
            var seals = Box("seal-row"); seals.EnableInClassList("survival-progress", scenario.encounter.objective == ObjectiveKind.Survive); right.Add(seals);
            for (var i = 0; i < v.ObjectiveTarget; i++) { var s = Box("seal"); s.EnableInClassList("complete", i < v.ObjectiveProgress); seals.Add(s); }
            if (!string.IsNullOrWhiteSpace(scenario.taskLabel))
            {
                var task = Button("battle-task", scenario.taskLabel + "\n1行动", () => director.Submit(BattleCommand.Task()), "task-button");
                task.SetEnabled(active && v.CanAdvanceObjective && v.Actions > 0); root.Add(task);
            }

            var instruments = Box("instruments"); root.Add(instruments);
            Vital(instruments, "生命", v.Health, scenario.encounter.playerHealth, "health", "heart");
            Vital(instruments, "理智", v.Sanity, scenario.encounter.playerSanity, "sanity", "eye");
            var action = Box("action-instrument"); action.Add(new SeaUiArt("compass"));
            action.Add(Text($"{v.Actions}/{scenario.encounter.actionsPerTurn}", "instrument-value"));
            action.Add(Text("行动", "instrument-label")); instruments.Add(action);
            instruments.Add(Text($"格挡 {v.Block} · 防护 {v.Ward}\n牌库 {v.DrawCount} · 弃牌 {v.DiscardCount} · 消耗 {v.ExhaustCount}", "resource-note"));

            var hand = new ScrollView(ScrollViewMode.Horizontal); hand.name = "hand"; hand.AddToClassList("hand");
            // Ignore the full built-in scroll shell, including the content/vertical-scroll wrapper.
            // Only the cards added below should intercept clicks over the 3D scene.
            hand.pickingMode = PickingMode.Ignore;
            hand.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
            hand.horizontalScrollerVisibility = ScrollerVisibility.Hidden; hand.verticalScrollerVisibility = ScrollerVisibility.Hidden; root.Add(hand);
            var fan = Box("fan-track"); fan.style.width = Mathf.Max(620, 180 + Mathf.Max(0, v.Hand.Count - 1) * 110); hand.Add(fan);
            hand.RegisterCallback<GeometryChangedEvent>(_ => fan.style.marginLeft = Mathf.Max(0, (hand.contentViewport.resolvedStyle.width - fan.resolvedStyle.width) * .5f));
            var cardPresentation = new HandCardPresentation(hand);
            var cardIndex = 0;
            foreach (var card in v.Hand)
            {
                var c = card; var def = c.Definition; var type = def.Effects[0].Id;
                var b = Button("card-" + c.InstanceId, "", () => director.Submit(BattleCommand.Play(c.InstanceId, director.SelectedEnemy)), "card-anchor");
                b.tabIndex = cardIndex + 1; b.style.left = cardIndex * 110;
                b.AddToClassList("fan-" + Mathf.Clamp(cardIndex++ - (v.Hand.Count - 1) / 2, -3, 3));
                cardPresentation.Add(b);
                var face = Box("sea-card"); face.pickingMode = PickingMode.Position;
                var motif = type.Contains("damage") ? "gun" : type == "block" ? "shield" : type == "ward" ? "eye" : type.Contains("investigate") ? "lantern" : "scroll";
                var head = Box("card-head"); head.Add(Text(def.Cost.ToString(), "card-cost")); head.Add(Text(type.Contains("damage") ? "压制" : type == "block" ? "防御" : type == "ward" ? "镇定" : "调查", "card-type")); face.Add(head);
                face.Add(new SeaUiArt(motif)); face.Add(Text(def.Name, "card-name")); face.Add(Text(def.Description, "card-description"));
                if (def.SanityCost > 0 || def.Exhaust) face.Add(Text((def.SanityCost > 0 ? $"支付 {def.SanityCost} 理智 " : "") + (def.Exhaust ? "· 消耗" : ""), "fine-print"));
                b.Add(face); b.tooltip = def.Name + "：" + def.Description;
                b.SetEnabled(active && def.Cost <= v.Actions && def.SanityCost < v.Sanity); fan.Add(b);
            }
            var end = Button("battle-end-turn", "", () => director.Submit(BattleCommand.EndTurn()), "end-turn");
            end.Add(new SeaUiArt("seal")); end.Add(Text("结束\n回合", "end-label")); end.SetEnabled(active); root.Add(end);
            if (!string.IsNullOrWhiteSpace(director.Notice) && director.Notice != scenario.introduction)
                root.Add(Text(director.Notice, "battle-notice"));
            if (director.Stage != null)
                foreach (var actor in director.Stage.Actors)
                {
                    var a = actor; var enemy = v.Enemies.First(x => x.Id == a.EnemyId);
                    var status = string.IsNullOrWhiteSpace(scenario.enemyStatus) ? enemy.Health + " / " + enemy.MaxHealth : scenario.enemyStatus;
                    var plate = Button("actor-" + a.EnemyId, "", () => director.Select(a.EnemyId), "actor-label");
                    plate.Add(Text(enemy.Name, "actor-name"));
                    if (string.IsNullOrWhiteSpace(scenario.enemyStatus))
                    {
                        var hp = Box("actor-health"); var fill = Box("actor-health-fill");
                        fill.style.width = Length.Percent(enemy.MaxHealth > 0 ? 100f * enemy.Health / enemy.MaxHealth : 0); hp.Add(fill); plate.Add(hp);
                    }
                    plate.Add(Text(status, "actor-status"));
                    if (a.EnemyId == director.SelectedEnemy)
                        plate.Add(new EnemyIntentDetails(enemy));
                    plate.EnableInClassList("selected", a.EnemyId == director.SelectedEnemy); plate.SetEnabled(a.Alive && !director.Paused); root.Add(plate); nameplates.Add(plate);
                }
            if (chartOpen) ChartOverlay();
            else if (director.Paused || v.Phase == BattlePhase.Victory || v.Phase == BattlePhase.Defeat) Modal(v);
        }
        private static void Vital(VisualElement parent, string title, int value, int max, string cls, string motif)
        {
            var el = Box("vital"); el.AddToClassList(cls); el.Add(new SeaUiArt(motif));
            el.Add(Text(value.ToString("00"), "instrument-value")); el.Add(Text(title + " / " + max, "instrument-label"));
            var track = Box("vital-track"); var fill = Box("vital-fill"); fill.style.width = Length.Percent((float)value / max * 100); track.Add(fill); el.Add(track); parent.Add(el);
        }
        private void ChartOverlay()
        {
            overlay = Box("battle-chart-overlay"); overlay.pickingMode = PickingMode.Position;
            overlay.Add(new VoyageMapPanel(VoyageRuntime.Session, null, false));
            overlay.Add(Button("battle-chart-close", "返回战斗", CloseChart, "battle-chart-close"));
            root.Add(overlay);
        }
        private void Modal(BattleView view)
        {
            overlay = Box("ocean-modal"); overlay.pickingMode = PickingMode.Position; root.Add(overlay);
            var sheet = Box("ocean-sheet"); overlay.Add(sheet);
            if (showSettings) { sheet.Add(new SettingsPanel(() => { showSettings = false; Render(); })); return; }
            bool end = view.Phase == BattlePhase.Victory || view.Phase == BattlePhase.Defeat;
            sheet.Add(Text(end ? (view.Phase == BattlePhase.Victory ? "海面重新沉默" : "无人回应的呼号") : "航海日志", "modal-title"));
            sheet.Add(Text(end ? (view.Phase == BattlePhase.Victory ? director.scenario.victoryText : director.scenario.defeatText) : "你听见海水拍打船舷。", "modal-copy"));
            var log = new ScrollView(); log.AddToClassList("journal");
            foreach (var evidence in view.Evidence) log.Add(Text($"[{KnowledgeLabel(evidence.Level)}] {evidence.Text} / {evidence.Source}", "log-line"));
            var entries = view.Log.Where(x => !x.Contains("阶段：")).ToArray();
            foreach (var entry in entries.Skip(Math.Max(0, entries.Length - 12))) log.Add(Text(entry, "log-line"));
            sheet.Add(log);
            if (!end) sheet.Add(Button("battle-resume", "继续航行", () => director.SetPaused(false)));
            if (!end || !VoyageRuntime.OwnsEncounter(director))
                sheet.Add(Button("battle-retry", "重新开始这场遭遇", () => { chartOpen = false; chartWasPaused = false; showSettings = false; director.Restart(); }));
            sheet.Add(Button("battle-settings", "设置", () => { showSettings = true; Render(); }));
            sheet.Add(Button("battle-menu", "返回主菜单", () => SceneManager.LoadScene(menuScene)));
        }
        private static string KnowledgeLabel(KnowledgeLevel level) => level == KnowledgeLevel.Verified ? "已验证" : level == KnowledgeLevel.Observed ? "已观察" : level == KnowledgeLevel.Rumored ? "传闻" : "未知";
        private void LateUpdate()
        {
            RenderedFrames++;
            if (director.Stage == null || root.panel == null) return;
            for (var i = 0; i < nameplates.Count; i++)
            {
                var actor = director.Stage.Actors[i];
                var screen = director.Stage.battleCamera.WorldToViewportPoint(actor.LabelPosition);
                var p = new Vector2(screen.x * root.resolvedStyle.width, (1 - screen.y) * root.resolvedStyle.height);
                var plate = nameplates[i];
                var width = plate.resolvedStyle.width;
                var height = plate.resolvedStyle.height;
                plate.style.left = Mathf.Clamp(p.x - width * .5f, 18, Mathf.Max(18, root.resolvedStyle.width - width - 18));
                plate.style.top = Mathf.Max(64, p.y - height - 12);
                nameplates[i].style.display = screen.z > 0 && actor.Alive ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
