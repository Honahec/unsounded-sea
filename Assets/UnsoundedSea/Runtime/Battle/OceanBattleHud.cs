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
        private int renderedRevision = -1;
        private bool renderedPaused;
        private string renderedSelection, renderedNotice;
        private readonly List<(Button button, bool eligible)> actionButtons = new List<(Button, bool)>();

        private bool chartOpen, chartWasPaused;
        private int selectedEncounterCard;
        private int inspectedCard;
        private string selectedSupport;
        private BossEncounterKind presentedBoss;
        private readonly List<(Button button, Transform anchor, Vector2 offset)> encounterLabels = new List<(Button, Transform, Vector2)>();
        public int RenderedFrames { get; private set; }
        private void OnEnable()
        {
            director = GetComponent<BattleDirector>(); root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear(); root.AddToClassList("ocean-ui"); root.pickingMode = PickingMode.Ignore; root.focusable = true;
            if (menuStyle != null) root.styleSheets.Add(menuStyle);
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            director.Changed += Refresh;
            root.RegisterCallback<KeyDownEvent>(Key);
            Render();
        }
        private void OnDisable()
        {
            chartOpen = false; chartWasPaused = false; showSettings = false;
            director.Changed -= Refresh; root.UnregisterCallback<KeyDownEvent>(Key); root.Clear();
        }
        private void Key(KeyDownEvent evt)
        {
            if (director.View?.PendingChoice != null) { evt.StopPropagation(); return; }
            if (inspectedCard != 0)
            {
                if (evt.keyCode == KeyCode.Escape) CloseInspection();
                evt.StopPropagation(); return;
            }
            if (evt.keyCode == KeyCode.Escape && chartOpen) CloseChart();
            else if (evt.keyCode == KeyCode.Escape && !string.IsNullOrEmpty(selectedSupport)) { selectedSupport = null; Render(); }
            else if (evt.keyCode == KeyCode.Escape && selectedEncounterCard != 0) { selectedEncounterCard = 0; Render(); }
            else if (evt.keyCode == KeyCode.M && !showSettings) ToggleChart();
            else if (evt.keyCode == KeyCode.D)
            {
                var focused = root.panel?.focusController.focusedElement as VisualElement;
                if (focused == null || focused.name == null || !focused.name.StartsWith("card-", StringComparison.Ordinal) ||
                    !int.TryParse(focused.name.Substring(5), out var id) || !director.View.Hand.Any(card => card.InstanceId == id)) return;
                inspectedCard = id; Render();
            }
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
        private void Refresh()
        {
            // Finishing an animation changes input availability, not the view contents.
            if (renderedRevision == director.ViewRevision && renderedPaused == director.Paused &&
                renderedSelection == director.SelectedEnemy && renderedNotice == director.Notice)
            {
                foreach (var entry in actionButtons) ApplyActionAvailability(entry.button, entry.eligible);
                return;
            }
            Render();
        }
        private void ActionButton(Button button, bool eligible)
        {
            actionButtons.Add((button, eligible));
            ApplyActionAvailability(button, eligible);
        }
        private void ApplyActionAvailability(Button button, bool eligible)
        {
            // Temporary feedback locks should not flash every control's disabled tint.
            button.EnableInClassList("feedback-locked", eligible && director.Busy);
            button.SetEnabled(eligible && !director.Busy);
        }
        private void Render()
        {
            var oldHand = root.Q<ScrollView>("hand");
            var handWidth = oldHand?.contentViewport.resolvedStyle.width ?? 0;
            var oldPositions = nameplates.ToDictionary(p => p.name, p => new Vector2(p.resolvedStyle.left, p.resolvedStyle.top));
            renderedRevision = director.ViewRevision;
            renderedPaused = director.Paused; renderedSelection = director.SelectedEnemy; renderedNotice = director.Notice;
            actionButtons.Clear();
            root.Clear(); root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion); nameplates.Clear(); var v = director.View; if (v == null) return;
            root.focusable = v.PendingChoice == null && inspectedCard == 0;
            if (root.focusable) root.Focus();
            encounterLabels.Clear();
            presentedBoss = v.BossKind;
            if (!v.Hand.Any(c => c.InstanceId == selectedEncounterCard)) selectedEncounterCard = 0;
            var scenario = director.scenario; bool active = v.Phase == BattlePhase.PlayerTurn && !director.Paused;
            var surface = Box("battle-surface"); surface.name = "battle-surface"; surface.pickingMode = PickingMode.Position;
            surface.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1 && selectedEncounterCard != 0) { selectedEncounterCard = 0; Render(); evt.StopPropagation(); return; }
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
            top.Add(Button("battle-chart-toggle", "海图", ToggleChart));
            top.Add(Button("battle-pause", "日志", () => director.SetPaused(true)));
            top.Q<Button>("battle-chart-toggle").tooltip = "海图 / M";
            top.Q<Button>("battle-pause").tooltip = "航海日志、设置与暂停 / Esc";

            var selected = v.Enemies.FirstOrDefault(x => x.Id == director.SelectedEnemy);

            var tools = Box("tools"); root.Add(tools);
            foreach (var tool in v.Tools)
            {
                var t = tool; var cost = t.Definition.SanityCost > 0 ? $"{t.Definition.SanityCost}理智" : "无代价";
                var b = Button("tool-" + t.Definition.Id, "", () => director.Submit(BattleCommand.Investigate(t.Definition.Id, director.SelectedEnemy)), "tool");
                b.Add(Text(t.Definition.Name, "tool-name")); b.Add(Text($"×{t.RemainingUses}" + (t.Definition.SanityCost > 0 ? $" · {t.Definition.SanityCost} 理智" : ""), "tool-meta"));
                b.tooltip = $"{t.Definition.Name}：调查当前目标，剩余 {t.RemainingUses} 次，代价 {cost}";
                ActionButton(b, active && selected != null && t.RemainingUses > 0 && v.Sanity > t.Definition.SanityCost); tools.Add(b);
            }
            foreach (var support in v.Supports)
            {
                var crew = support;
                var button = Button("crew-" + crew.Definition.Id, "", () =>
                {
                    if (crew.Targets.Count == 0) director.Submit(BattleCommand.Support(crew.Definition.Id));
                    else { selectedSupport = selectedSupport == crew.Definition.Id ? null : crew.Definition.Id; selectedEncounterCard = 0; Render(); }
                }, "tool");
                button.Add(new SeaUiArt(crew.Definition.Kind == CrewSupportKind.Medic ? "heart" : "ear"));
                button.Add(Text(crew.Definition.Name, "tool-name"));
                button.Add(Text(crew.Used ? "本场已用" : "1行动", "tool-meta"));
                button.tooltip = crew.Enabled ? "船员支援 · 每回合最多一次，每人每场一次" : crew.DisabledReason;
                ActionButton(button, active && crew.Enabled); tools.Add(button);
            }

            var objective = Box("battle-objective"); root.Add(objective);
            objective.pickingMode = PickingMode.Position;
            objective.Add(Text(v.BossKind != BossEncounterKind.None ? "进度" : scenario.encounter.objective == ObjectiveKind.Seal ? "封线" : scenario.encounter.objective == ObjectiveKind.Survive ? "撤离" : "压制", "objective-label"));
            objective.Add(Text($"{v.ObjectiveProgress} / {v.ObjectiveTarget}", "objective-count"));
            objective.tooltip = scenario.objectiveHeading + "：" + v.Objective;

            var controls = Box("turn-controls"); root.Add(controls);
            if (!string.IsNullOrWhiteSpace(scenario.taskLabel))
            {
                var task = Button("battle-task", "", () => director.Submit(BattleCommand.Task()), "task-button");
                task.Add(Text(scenario.taskLabel, "task-label"));
                task.Add(Text("1 行动", "task-cost"));
                task.tooltip = "每回合一次，消耗 1 行动推进任务。";
                ActionButton(task, active && v.CanAdvanceObjective && v.Actions > 0); controls.Add(task);
            }
            if (v.BossKind != BossEncounterKind.None)
            {
                root.Add(Text(v.BossStatus, "boss-status"));
                RenderEncounterTargets(v, active);
            }
            controls.Add(Text($"第 {v.Turn} 回合", "turn-number"));
            var action = Box("action-budget");
            action.Add(Text("行动", "action-label"));
            var actionValue = Text($"{v.Actions} / {scenario.encounter.actionsPerTurn}", "action-value");
            actionValue.name = "battle-actions"; action.Add(actionValue); controls.Add(action);

            var vitals = Box("battle-vitals"); root.Add(vitals);
            Vital(vitals, "生命", v.Health, scenario.encounter.playerHealth, "health", v.Block, "格挡");
            Vital(vitals, "理智", v.Sanity, scenario.encounter.playerSanity, "sanity", v.Ward, "防护");

            var hand = new ScrollView(ScrollViewMode.Horizontal); hand.name = "hand"; hand.AddToClassList("hand");
            // Ignore the full built-in scroll shell, including the content/vertical-scroll wrapper.
            // Only the cards added below should intercept clicks over the 3D scene.
            hand.pickingMode = PickingMode.Ignore;
            hand.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
            hand.horizontalScrollerVisibility = ScrollerVisibility.Hidden; hand.verticalScrollerVisibility = ScrollerVisibility.Hidden; root.Add(hand);
            var fan = Box("fan-track"); fan.style.width = Mathf.Max(620, 180 + Mathf.Max(0, v.Hand.Count - 1) * 110); hand.Add(fan);
            if (handWidth > 0) fan.style.marginLeft = Mathf.Max(0, (handWidth - fan.style.width.value.value) * .5f);
            hand.RegisterCallback<GeometryChangedEvent>(_ => fan.style.marginLeft = Mathf.Max(0, (hand.contentViewport.resolvedStyle.width - fan.resolvedStyle.width) * .5f));
            var cardPresentation = new HandCardPresentation(hand);
            var cardIndex = 0;
            foreach (var card in v.Hand)
            {
                var c = card; var def = c.Definition;
                var b = Button("card-" + c.InstanceId, "", () =>
                {
                    if (!c.Enabled || def.Cost > v.Actions || def.SanityCost >= v.Sanity) { inspectedCard = c.InstanceId; Render(); return; }
                    selectedSupport = null;
                    if (c.Targets.Count == 0) { selectedEncounterCard = 0; director.Submit(BattleCommand.Play(c.InstanceId)); }
                    else
                    {
                        selectedEncounterCard = selectedEncounterCard == c.InstanceId ? 0 : c.InstanceId;
                        Render();
                        if (selectedEncounterCard != 0) root.schedule.Execute(() => root.Q<Button>(className: "target-ready")?.Focus());
                    }
                }, "card-anchor");
                b.EnableInClassList("encounter-card", c.IsEncounter);
                b.EnableInClassList("chosen-card", selectedEncounterCard == c.InstanceId);
                b.RegisterCallback<ContextClickEvent>(evt => { inspectedCard = c.InstanceId; Render(); evt.StopPropagation(); });
                b.tabIndex = cardIndex + 1; b.style.left = cardIndex * 110;
                b.AddToClassList("fan-" + Mathf.Clamp(cardIndex++ - (v.Hand.Count - 1) / 2, -3, 3));
                cardPresentation.Add(b);
                var face = Box("sea-card"); face.pickingMode = PickingMode.Position;
                IllustratedCard.Apply(face, def);
                face.Add(Text("D 详情", "card-detail-hint"));
                if (!c.Enabled) face.Add(Text(c.DisabledReason, "fine-print"));
                b.Add(face); b.tooltip = def.Name + "：" + def.Description + (!c.Enabled ? "\n" + c.DisabledReason : "") + "\n右键或聚焦后按 D 查看大图";
                b.EnableInClassList("card-disabled", !c.Enabled || def.Cost > v.Actions || def.SanityCost >= v.Sanity);
                ActionButton(b, active); fan.Add(b);
            }
            RenderCardChoices(v, active);
            RenderSupportChoices(v, active);
            var end = Button("battle-end-turn", "结束回合", () => director.Submit(BattleCommand.EndTurn()), "end-turn");
            ActionButton(end, active); controls.Add(end);
            if (!string.IsNullOrWhiteSpace(director.Notice) && director.Notice != scenario.introduction)
                root.Add(Text(director.Notice, "battle-notice"));
            if (director.Stage != null)
                foreach (var actor in director.Stage.Actors)
                {
                    var a = actor; var enemy = v.Enemies.First(x => x.Id == a.EnemyId);
                    var hideHealth = v.BossKind != BossEncounterKind.None ? !enemy.Targetable : !string.IsNullOrWhiteSpace(scenario.enemyStatus);
                    var status = hideHealth ? (v.BossKind != BossEncounterKind.None ? "不可直接伤害" : scenario.enemyStatus) : enemy.Health + " / " + enemy.MaxHealth;
                    var plate = Button("actor-" + a.EnemyId, "", () => director.Select(a.EnemyId), "actor-label");
                    if (oldPositions.TryGetValue(plate.name, out var previousPosition))
                    {
                        plate.style.left = previousPosition.x; plate.style.top = previousPosition.y;
                    }
                    plate.RegisterCallback<GeometryChangedEvent>(_ => PositionLabels());
                    plate.Add(Text(enemy.Name, "actor-name"));
                    if (!hideHealth)
                    {
                        var hp = Box("actor-health"); var fill = Box("actor-health-fill");
                        fill.style.width = Length.Percent(enemy.MaxHealth > 0 ? 100f * enemy.Health / enemy.MaxHealth : 0); hp.Add(fill); plate.Add(hp);
                    }
                    plate.Add(Text(status, "actor-status"));
                    var showIntent = a.EnemyId == director.SelectedEnemy || !enemy.Targetable;
                    plate.EnableInClassList("actor-intent", showIntent);
                    if (showIntent) plate.Add(new EnemyIntentDetails(enemy));
                    plate.EnableInClassList("selected", a.EnemyId == director.SelectedEnemy); plate.SetEnabled(a.Alive && !director.Paused); root.Add(plate); nameplates.Add(plate);
                }
            if (chartOpen) ChartOverlay();
            else if (director.Paused || v.Phase == BattlePhase.Victory || v.Phase == BattlePhase.Defeat) Modal(v);
            if (v.PendingChoice != null)
            {
                foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
                root.Add(PendingChoice(v.PendingChoice, option => director.Submit(BattleCommand.Reorder(option))));
                return;
            }
            var inspection = v.Hand.FirstOrDefault(card => card.InstanceId == inspectedCard);
            if (inspection != null)
            {
                foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
                root.Add(IllustratedCard.Inspection(inspection.Definition, CloseInspection));
            }
        }
        private VisualElement PendingChoice(PendingCardChoiceView choice, Action<string> resolve)
        {
            var modal = Box("pending-choice"); modal.pickingMode = PickingMode.Position;
            var sheet = Box("pending-choice-sheet"); sheet.pickingMode = PickingMode.Position;
            sheet.Add(Text(choice.Prompt, "pending-choice-title"));
            var options = new ScrollView(ScrollViewMode.Vertical); options.AddToClassList("pending-choice-options"); sheet.Add(options);
            var index = 0;
            foreach (var option in choice.Options)
            {
                var target = option;
                options.Add(Button("pending-choice-" + index++, target.Name + (string.IsNullOrWhiteSpace(target.Detail) ? "" : "\n" + target.Detail),
                    () => resolve(target.Id), "card-target-choice"));
            }
            modal.Add(sheet);
            modal.schedule.Execute(() => modal.Q<Button>(className: "card-target-choice")?.Focus());
            return modal;
        }

        private void CloseInspection()
        {
            var card = inspectedCard; inspectedCard = 0; Render();
            root.schedule.Execute(() => root.Q<Button>("card-" + card)?.Focus());
        }

        private void RenderSupportChoices(BattleView view, bool active)
        {
            var support = view.Supports.FirstOrDefault(item => item.Definition.Id == selectedSupport);
            if (support == null || support.Targets.Count == 0) return;
            var panel = Box("card-target-panel");
            panel.Add(Text("船员支援：" + support.Definition.Name, "card-target-title"));
            var options = new ScrollView(ScrollViewMode.Vertical); options.AddToClassList("card-target-options"); panel.Add(options);
            var index = 0;
            foreach (var option in support.Targets)
            {
                var target = option; var supportId = support.Definition.Id;
                var button = Button("support-target-" + index++, target.Name + (string.IsNullOrWhiteSpace(target.Detail) ? "" : "\n" + target.Detail), () =>
                {
                    var result = director.Submit(BattleCommand.Support(supportId, target.Id));
                    if (result.Success) { selectedSupport = null; Render(); }
                }, "card-target-choice");
                ActionButton(button, active && target.Enabled); options.Add(button);
            }
            root.Add(panel);
        }

        private void RenderCardChoices(BattleView view, bool active)
        {
            var selected = view.Hand.FirstOrDefault(card => card.InstanceId == selectedEncounterCard);
            if (selected == null || selected.Targets.Count == 0 || (selected.IsEncounter && view.BossKind != BossEncounterKind.None)) return;
            var panel = Box("card-target-panel");
            panel.Add(Text("选择：" + selected.Definition.Name, "card-target-title"));
            var options = new ScrollView(ScrollViewMode.Vertical); options.AddToClassList("card-target-options"); panel.Add(options);
            var index = 0;
            foreach (var option in selected.Targets)
            {
                var target = option;
                var cardId = selected.InstanceId;
                var button = Button("card-target-" + index++, target.Name + (string.IsNullOrWhiteSpace(target.Detail) ? "" : "\n" + target.Detail), () =>
                {
                    var result = director.Submit(BattleCommand.Play(cardId, target.Id));
                    if (result.Success) { selectedEncounterCard = 0; Render(); }
                }, "card-target-choice");
                button.EnableInClassList("target-ready", target.Enabled);
                ActionButton(button, active && target.Enabled);
                button.tooltip = target.Detail;
                options.Add(button);
            }
            root.Add(panel);
        }

        private void RenderEncounterTargets(BattleView view, bool active)
        {
            if (director.Stage == null) return;
            var anchors = director.Stage.GetComponentsInChildren<BossTargetAnchor>();
            var selected = view.Hand.FirstOrDefault(c => c.InstanceId == selectedEncounterCard);
            if (selected == null && (view.BossKind == BossEncounterKind.FlagBearer || view.BossKind == BossEncounterKind.Watcher)) return;
            var targets = selected == null ? view.EncounterTargets : selected.Targets;
            VisualElement saltBoard = null;
            if (view.BossKind == BossEncounterKind.Surveyor)
            {
                saltBoard = Box("salt-board");
                root.Add(saltBoard);
            }
            var offsets = new Dictionary<string, int>();
            var targetIndex = 0;
            foreach (var target in targets)
            {
                var id = target.Id;
                var anchorId = id;
                var pair = id.IndexOf('|');
                if (pair >= 0)
                {
                    if (selected != null && id.Substring(0, pair) != director.SelectedEnemy) continue;
                    anchorId = id.Substring(pair + 1);
                }
                var separator = anchorId.IndexOf(':');
                var anchor = anchors.FirstOrDefault(a => a.targetId == anchorId);
                if (anchor == null && separator >= 0)
                    anchor = anchors.FirstOrDefault(a => a.targetId == anchorId.Substring(0, separator));
                if (anchor == null) continue;
                var cardId = selectedEncounterCard;
                var button = Button("boss-target-" + id, target.Name + (string.IsNullOrEmpty(target.Detail) ? "" : "\n" + target.Detail), () =>
                {
                    var result = director.Submit(BattleCommand.Play(cardId, id));
                    if (result.Success) { selectedEncounterCard = 0; Render(); }
                }, "boss-target");
                button.tooltip = target.Name + "：" + target.Detail;
                if (view.BossKind == BossEncounterKind.Watcher) button.text = target.Name;
                button.EnableInClassList("target-ready", selected != null && target.Enabled);
                ActionButton(button, active && selected != null && target.Enabled);
                button.tabIndex = 100 + targetIndex++;
                if (view.BossKind == BossEncounterKind.FlagBearer)
                {
                    button.text = target.Name;
                    button.AddToClassList("mechanism-dial");
                    var relay = id.Contains(":");
                    var option = relay ? id.EndsWith(":off", StringComparison.Ordinal) ? 0 : id.EndsWith(":positive", StringComparison.Ordinal) ? 1 : 2 : id == "lock-I" ? 0 : id == "lock-II" ? 1 : 2;
                    button.style.left = relay ? (id.StartsWith("R1", StringComparison.Ordinal) ? 50 : 190) : 70 + option * 110;
                    button.style.top = relay ? 190 + option * 100 : 230;
                    root.Add(button);
                }
                else if (saltBoard != null)
                {
                    button.AddToClassList("salt-cell");
                    saltBoard.Add(button);
                }
                else
                {
                    offsets.TryGetValue(anchorId, out var index); offsets[anchorId] = index + 1;
                    root.Add(button);
                    var offset = view.BossKind == BossEncounterKind.Choirmaster ? new Vector2(id == "choir-a" ? -80 : id == "choir-c" ? 80 : 0, -80) : anchor.targetId != anchorId && separator >= 0 ? new Vector2(0, (index - 1) * 48) : Vector2.zero;
                    encounterLabels.Add((button, anchor.transform, offset));
                }
            }
            if (selected != null)
            {
                var prompt = Text(selected.Definition.Name + (selected.Targets.Any(t => t.Id.Contains("|")) ? " · 选择守卫与航标" : " · 选择目标") + " · Esc 取消", "boss-target-prompt");
                root.Add(prompt);
            }
        }

        private static void Vital(VisualElement parent, string title, int value, int max, string cls, int guard, string guardName)
        {
            var el = Box("vital"); el.AddToClassList(cls);
            var row = Box("vital-heading"); row.Add(Text(title, "vital-label"));
            var number = Text($"{value} / {max}", "vital-value"); number.name = "battle-" + cls; row.Add(number); el.Add(row);
            var track = Box("vital-track"); var fill = Box("vital-fill");
            fill.style.width = Length.Percent(max > 0 ? Mathf.Clamp01((float)value / max) * 100 : 0);
            track.Add(fill); el.Add(track);
            var protection = Text(guard > 0 ? $"{guardName}  {guard}" : string.Empty, "vital-protection");
            protection.name = "battle-" + cls + "-guard"; el.Add(protection);
            parent.Add(el);
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
            sheet.Add(Text($"牌库 {view.DrawCount}  ·  弃牌 {view.DiscardCount}  ·  消耗 {view.ExhaustCount}", "pile-summary"));
            var log = new ScrollView(); log.AddToClassList("journal");
            foreach (var evidence in view.Evidence) log.Add(Text($"[{KnowledgeLabel(evidence.Level)}] {evidence.Text} / {evidence.Source}", "log-line"));
            var entries = view.Log.Where(x => !x.Contains("阶段：")).ToArray();
            foreach (var entry in entries.Skip(Math.Max(0, entries.Length - 12))) log.Add(Text(entry, "log-line"));
            sheet.Add(log);
            if (!end) sheet.Add(Button("battle-resume", "继续战斗", () => director.SetPaused(false)));
            if (!VoyageRuntime.OwnsEncounter(director))
                sheet.Add(Button("battle-retry", "重新开始这场遭遇", () => { chartOpen = false; chartWasPaused = false; showSettings = false; director.Restart(); }));
            if (VoyageRuntime.OwnsEncounter(director))
            {
                if (!string.IsNullOrEmpty(VoyageRuntime.EncounterSaveError))
                    sheet.Add(Text("记录尚未保存：" + VoyageRuntime.EncounterSaveError, "modal-copy"));
                sheet.Add(Button("battle-voyage", end ? "返回航程 · 结算" : "保存并返回航程",
                    () => { VoyageRuntime.ReturnToVoyage(); Render(); }));
            }
            sheet.Add(Button("battle-settings", "设置", () => { showSettings = true; Render(); }));
            sheet.Add(Button("battle-menu", "返回主菜单", () => SceneManager.LoadScene(menuScene)));
        }
        private static string KnowledgeLabel(KnowledgeLevel level) => level == KnowledgeLevel.Verified ? "已验证" : level == KnowledgeLevel.Observed ? "已观察" : level == KnowledgeLevel.Rumored ? "传闻" : "未知";
        private void LateUpdate()
        {
            RenderedFrames++;
            PositionLabels();
        }
        private void PositionLabels()
        {
            if (director.Stage == null || root.panel == null) return;
            foreach (var label in encounterLabels)
            {
                if (label.anchor == null) continue;
                var position = director.Stage.battleCamera.WorldToViewportPoint(label.anchor.position);
                label.button.style.left = position.x * root.resolvedStyle.width - label.button.resolvedStyle.width * .5f + label.offset.x;
                label.button.style.top = (1 - position.y) * root.resolvedStyle.height - label.button.resolvedStyle.height * .5f + label.offset.y;
                label.button.style.display = position.z > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            var summonTop = presentedBoss == BossEncounterKind.Surveyor || presentedBoss == BossEncounterKind.FlagBearer ? 520f : 170f;
            for (var i = 0; i < nameplates.Count; i++)
            {
                var actor = director.Stage.Actors[i];
                var screen = director.Stage.battleCamera.WorldToViewportPoint(actor.LabelPosition);
                var p = new Vector2(screen.x * root.resolvedStyle.width, (1 - screen.y) * root.resolvedStyle.height);
                var plate = nameplates[i];
                var width = plate.resolvedStyle.width;
                var height = plate.resolvedStyle.height;
                if (float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0) continue;
                if (presentedBoss != BossEncounterKind.None && i > 0)
                {
                    plate.style.left = 32;
                    plate.style.top = summonTop;
                    if (actor.Alive) summonTop += height + 12;
                }
                else
                {
                    plate.style.left = Mathf.Clamp(p.x - width * .5f, 18, Mathf.Max(18, root.resolvedStyle.width - width - 18));
                    plate.style.top = Mathf.Max(64, p.y - height - 12);
                }
                plate.style.display = screen.z > 0 && actor.Alive ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
