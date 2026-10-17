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
        private bool showSettings, journalOpen;
        private bool chartOpen, chartWasPaused, bookOpen, bookWasPaused;
        private int renderedRevision = -1;
        private bool renderedPaused;
        private string renderedSelection, renderedNotice;
        private readonly List<(Button button, bool eligible)> actionButtons = new List<(Button, bool)>();

        private int selectedEncounterCard;
        private readonly EnemyCardSelection enemySelection = new EnemyCardSelection();
        private readonly List<(Button button, BattleActorView actor)> enemyChoices = new List<(Button, BattleActorView)>();
        private int inspectedCard;
        private string selectedSupport;
        private int[] previousHand = Array.Empty<int>();
        private string playedCardFeedback;
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
            director.Resolved += OnResolved;
            root.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            Render();
        }
        private void OnDisable()
        {
            chartOpen = false; chartWasPaused = false; bookOpen = false; bookWasPaused = false; showSettings = false; journalOpen = false;
            director.Changed -= Refresh; director.Resolved -= OnResolved; root.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown); root.Clear();
        }
        private void OnResolved(BattleView before, BattleView after, BattleCommand command)
        {
            if (command.kind != CommandKind.PlayCard) return;
            playedCardFeedback = before.Hand.FirstOrDefault(card => card.InstanceId == command.cardInstanceId)?.Definition.Name;
        }

        private void Key(KeyDownEvent evt)
        {
            if (director.View?.PendingChoice != null) { evt.StopPropagation(); return; }
            if (inspectedCard != 0)
            {
                if (evt.keyCode == KeyCode.Escape) CloseInspection();
                evt.StopPropagation(); return;
            }
            if (evt.keyCode == KeyCode.B) ToggleActionBook();
            else if (bookOpen) { if (evt.keyCode != KeyCode.Escape) return; ToggleActionBook(); }
            else if (evt.keyCode == KeyCode.Escape && chartOpen) CloseChart();
            else if (evt.keyCode == KeyCode.Escape && !string.IsNullOrEmpty(selectedSupport)) { selectedSupport = null; Render(); }
            else if (evt.keyCode == KeyCode.Escape && selectedEncounterCard != 0) { selectedEncounterCard = 0; Render(); }
            else if (evt.keyCode == KeyCode.M && !showSettings) ToggleChart();
            else if (evt.keyCode == KeyCode.J && !showSettings)
            {
                journalOpen = !director.Paused || !journalOpen;
                if (director.Paused != journalOpen) director.SetPaused(journalOpen); else Render();
            }
            else if (evt.keyCode == KeyCode.D)
            {
                var focused = root.panel?.focusController.focusedElement as VisualElement;
                if (focused == null || focused.name == null || !focused.name.StartsWith("card-", StringComparison.Ordinal) ||
                    !int.TryParse(focused.name.Substring(5), out var id) || !director.View.Hand.Any(card => card.InstanceId == id)) return;
                inspectedCard = id; Render();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                if (showSettings) { showSettings = false; Render(); }
                else { journalOpen = false; director.SetPaused(!director.Paused); }
            }
            else return;
            evt.StopPropagation();
        }
        private void ToggleActionBook()
        {
            if (bookOpen)
            {
                bookOpen = false;
                if (director.Paused != bookWasPaused) director.SetPaused(bookWasPaused); else Render();
                return;
            }
            bookWasPaused = director.Paused;
            bookOpen = true;
            if (!director.Paused) director.SetPaused(true); else Render();
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
        private static string CrewMotif(CrewSupportKind kind)
        {
            switch (kind)
            {
                case CrewSupportKind.Medic: return "heart";
                case CrewSupportKind.Listener: return "ear";
                case CrewSupportKind.Engineer: return "shield";
                case CrewSupportKind.Archivist: return "scroll";
                case CrewSupportKind.Navigator:
                case CrewSupportKind.Cartographer: return "compass";
                case CrewSupportKind.Gunsmith: return "gun";
                case CrewSupportKind.Diver: return "shell";
                case CrewSupportKind.Cook: return "lantern";
                default: return "eye";
            }
        }
        private void Refresh()
        {
            // Finishing an animation changes input availability, not the view contents.
            if (root.focusable && renderedRevision == director.ViewRevision && renderedPaused == director.Paused &&
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
            var focused = (root.panel?.focusController.focusedElement as VisualElement)?.name;
            var oldHand = root.Q<ScrollView>("hand");
            var handOffset = oldHand?.scrollOffset ?? Vector2.zero;
            var handWidth = oldHand?.contentViewport.resolvedStyle.width ?? 0;
            var toolOffset = root.Q<ScrollView>(className: "tools")?.scrollOffset ?? Vector2.zero;
            var journalOffset = root.Q<ScrollView>(className: "journal")?.scrollOffset ?? Vector2.zero;
            var oldPositions = nameplates.ToDictionary(p => p.name, p => new Vector2(p.resolvedStyle.left, p.resolvedStyle.top));
            renderedRevision = director.ViewRevision;
            renderedPaused = director.Paused; renderedSelection = director.SelectedEnemy; renderedNotice = director.Notice;
            actionButtons.Clear();
            root.Clear(); root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion); nameplates.Clear(); var v = director.View; if (v == null) return;
            root.focusable = v.PendingChoice == null && inspectedCard == 0 && !bookOpen && !chartOpen && !director.Paused && v.Phase == BattlePhase.PlayerTurn;
            encounterLabels.Clear();
            enemyChoices.Clear();
            if (selectedEncounterCard == 0) enemySelection.Reset();
            presentedBoss = v.BossKind;
            if (!v.Hand.Any(c => c.InstanceId == selectedEncounterCard && c.Targets.Count > 0)) selectedEncounterCard = 0;
            var scenario = director.scenario; bool active = v.Phase == BattlePhase.PlayerTurn && !director.Paused;
            var surface = Box("battle-surface"); surface.name = "battle-surface"; surface.pickingMode = PickingMode.Position;
            surface.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1 && selectedEncounterCard != 0) { selectedEncounterCard = 0; Render(); evt.StopPropagation(); return; }
                if (director.Stage == null || director.Paused || evt.button != 0) return;
                // Use the battle viewport: an offscreen reflection render can change Screen dimensions.
                var local = root.WorldToLocal(new Vector2(evt.position.x, evt.position.y));
                var point = new Vector3(local.x / root.resolvedStyle.width, 1 - local.y / root.resolvedStyle.height, 0);
                var ray = director.Stage.battleCamera.ViewportPointToRay(point);
                if (selectedEncounterCard != 0)
                    foreach (var candidate in director.Stage.Actors)
                        if (candidate.HitSelectionRing(ray) && TryPlaySelectedTarget(candidate.EnemyId)) { evt.StopPropagation(); return; }
                if (Physics.Raycast(ray, out var hit))
                {
                    var actor = hit.collider.GetComponentInParent<BattleActorView>();
                    if (actor == null) return;
                    if (selectedEncounterCard != 0 && TryPlaySelectedTarget(actor.EnemyId)) { evt.StopPropagation(); return; }
                    director.Select(actor.EnemyId);
                }
            }); root.Add(surface);
            var top = Box("sea-top"); root.Add(top);
            top.Add(Button("battle-chart-toggle", "海图 / M", ToggleChart));
            top.Add(Button("battle-action-book", "行动簿 / B", ToggleActionBook));

            var selected = v.Enemies.FirstOrDefault(x => x.Id == director.SelectedEnemy);

            var tools = new ScrollView(ScrollViewMode.Vertical); tools.AddToClassList("tools"); tools.style.maxHeight = new StyleLength(new Length(48, LengthUnit.Percent)); root.Add(tools);
            if (selected != null) tools.Add(Text("聚焦 · " + selected.Name, "tool-focus"));
            foreach (var tool in v.Tools.Where(x=>(int)x.Definition.Kind<3))
            {
                var t = tool; var cost = t.Definition.SanityCost > 0 ? $"{t.Definition.SanityCost}理智" : "无代价";
                var target = t.Targets.FirstOrDefault(x => x.Id == director.SelectedEnemy);
                var b = Button("tool-" + t.Definition.Id, "", () => director.Submit(BattleCommand.Investigate(t.Definition.Id, director.SelectedEnemy)), "tool");
                b.Add(Text(t.Definition.Name, "tool-name")); b.Add(Text($"×{t.RemainingUses}" + (t.Definition.SanityCost > 0 ? $" · {t.Definition.SanityCost} 理智" : ""), "tool-meta"));
                b.tooltip = target != null && target.Enabled ? $"{t.Definition.Name}：{target.Detail}，剩余 {t.RemainingUses} 次，代价 {cost}" :
                    target?.Detail ?? t.DisabledReason;
                ActionButton(b, active && t.Enabled && target?.Enabled == true); tools.Add(b);
            }
            foreach (var support in v.Supports)
            {
                var crew = support;
                var button = Button("crew-" + crew.Definition.Id, "", () =>
                {
                    if (crew.Targets.Count == 0) director.Submit(BattleCommand.Support(crew.Definition.Id));
                    else { selectedSupport = selectedSupport == crew.Definition.Id ? null : crew.Definition.Id; selectedEncounterCard = 0; Render(); }
                }, "tool");
                button.Add(new SeaUiArt(CrewMotif(crew.Definition.Kind)));
                button.Add(Text(crew.Definition.Name, "tool-name"));
                button.Add(Text(crew.Used ? "本场已用" : $"{crew.Definition.Cost}行动", "tool-meta"));
                button.tooltip = crew.Enabled ? SupportCatalog.CrewDescription(crew.Definition.Kind) + " 每回合最多一次，每人每场一次" : crew.DisabledReason;
                ActionButton(button, active && crew.Enabled); tools.Add(button);
            }

            tools.Add(BattleSupplyPanel.Build(v,command=>director.Submit(command),active,ActionButton));
            var objective = Box("battle-objective"); root.Add(objective);
            objective.pickingMode = PickingMode.Position;
            objective.Add(Text(v.BossKind != BossEncounterKind.None ? "进度" : scenario.encounter.objective == ObjectiveKind.Seal ? "封线" : scenario.encounter.objective == ObjectiveKind.Survive ? "撤离" : "压制", "objective-label"));
            objective.Add(Text($"{v.ObjectiveProgress} / {v.ObjectiveTarget}", "objective-count"));
            objective.tooltip = scenario.objectiveHeading + "：" + v.Objective;

            var controls = Box("turn-controls"); root.Add(controls);
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
            var animateDraw = !director.Paused && !chartOpen && v.PendingChoice == null && previousHand.Length > 0 && !GameSettings.ReducedMotion;
            var cardIndex = 0;
            foreach (var card in v.Hand)
            {
                var c = card; var def = c.Definition;
                var b = Button("card-" + c.InstanceId, "", () =>
                {
                    if (!c.Enabled || !c.IsEncounter && (def.Cost > v.Actions || def.SanityCost >= v.Sanity))
                    {
                        director.Submit(BattleCommand.Play(c.InstanceId)); return;
                    }
                    selectedSupport = null;
                    var automaticTarget = EnemyCardSelection.AutomaticTarget(c, v);
                    if (c.Targets.Count == 0 || automaticTarget != null) { selectedEncounterCard = 0; director.Submit(BattleCommand.Play(c.InstanceId, automaticTarget)); }
                    else
                    {
                        selectedEncounterCard = selectedEncounterCard == c.InstanceId ? 0 : c.InstanceId;
                        Render();
                        if (selectedEncounterCard != 0) root.schedule.Execute(() => root.Q<Button>(className: "target-ready")?.Focus());
                    }
                }, "card-anchor");
                b.EnableInClassList("encounter-card", c.IsEncounter);
                b.EnableInClassList("chosen-card", selectedEncounterCard == c.InstanceId);
                b.RegisterCallback<ContextClickEvent>(evt =>
                {
                    if (selectedEncounterCard == c.InstanceId) { selectedEncounterCard = 0; Render(); }
                    else { inspectedCard = c.InstanceId; Render(); }
                    evt.StopPropagation();
                });
                b.tabIndex = cardIndex + 1; b.style.left = cardIndex * 110;
                b.AddToClassList("fan-" + Mathf.Clamp(cardIndex++ - (v.Hand.Count - 1) / 2, -3, 3));
                cardPresentation.Add(b);
                if (animateDraw && !previousHand.Contains(c.InstanceId))
                {
                    b.AddToClassList("card-entering");
                    b.schedule.Execute(() => b.RemoveFromClassList("card-entering")).StartingIn(16);
                }
                var face = Box("sea-card"); face.pickingMode = PickingMode.Position;
                IllustratedCard.Apply(face, def);
                if (c.IsEncounter) face.Q<Label>("illustrated-cost").text = c.ActionCost.ToString();
                face.Add(Text("D 详情", "card-detail-hint"));
                b.Add(face); b.tooltip = def.Name + "：" + def.Description;
                b.EnableInClassList("card-disabled", !c.Enabled || !c.IsEncounter && (def.Cost > v.Actions || def.SanityCost >= v.Sanity));
                ActionButton(b, active); fan.Add(b);
            }
            previousHand = v.Hand.Select(card => card.InstanceId).ToArray();
            RenderCardChoices(v, active);
            RenderSupportChoices(v, active);
            var end = Button("battle-end-turn", "", () => director.Submit(BattleCommand.EndTurn()), "end-turn");
            end.tooltip = "结束回合";
            end.Add(new SeaUiArt("seal"));
            ActionButton(end, active); controls.Add(end);
            controls.Add(Text("结束回合", "end-title"));
            if (director.NoticeIsError)
                root.Add(Text(director.Notice, "battle-subtitle"));
            if (!string.IsNullOrEmpty(playedCardFeedback))
            {
                var feedback = Text("已打出 · " + playedCardFeedback, "card-play-feedback");
                root.Add(feedback);
                playedCardFeedback = null;
                if (!GameSettings.ReducedMotion)
                    feedback.schedule.Execute(() => feedback.AddToClassList("departing")).StartingIn(16);
            }
            if (director.Stage != null)
                foreach (var actor in director.Stage.Actors)
                {
                    var a = actor; var enemy = v.Enemies.First(x => x.Id == a.EnemyId);
                    var hideHealth = v.BossKind != BossEncounterKind.None ? !enemy.Targetable : !string.IsNullOrWhiteSpace(scenario.enemyStatus);
                    var status = hideHealth ? (v.BossKind != BossEncounterKind.None ? "不可直接伤害" : scenario.enemyStatus) : enemy.Health + " / " + enemy.MaxHealth;
                    var plate = Button("actor-" + a.EnemyId, "", () =>
                    {
                        if (selectedEncounterCard == 0) director.Select(a.EnemyId);
                        else TryPlaySelectedTarget(a.EnemyId);
                    }, "actor-label");
                    if (oldPositions.TryGetValue(plate.name, out var previousPosition))
                    {
                        plate.style.left = previousPosition.x; plate.style.top = previousPosition.y;
                    }
                    // LateUpdate positions labels after layout; changing position inside a
                    // GeometryChangedEvent can recursively invalidate the same layout pass.
                    plate.Add(Text(enemy.Name, "actor-name"));
                    if (!hideHealth)
                    {
                        var hp = Box("actor-health"); var fill = Box("actor-health-fill");
                        fill.style.width = Length.Percent(enemy.MaxHealth > 0 ? 100f * enemy.Health / enemy.MaxHealth : 0); hp.Add(fill); plate.Add(hp);
                    }
                    plate.Add(Text(status, "actor-status"));
                    plate.Add(new EnemyIntentDetails(enemy));
                    plate.EnableInClassList("selected", a.EnemyId == director.SelectedEnemy); plate.SetEnabled(a.Alive && !director.Paused); root.Add(plate); nameplates.Add(plate);
                    var selectedCard = v.Hand.FirstOrDefault(c => c.InstanceId == selectedEncounterCard);
                    if (selectedCard == null) a.Select(a.EnemyId == director.SelectedEnemy);
                    else a.Select(false);
                    if (selectedCard != null && a.Alive && enemySelection.CanChoose(selectedCard, a.EnemyId))
                    {
                        a.ShowCardTarget(enemySelection.IsSelected(selectedCard, a.EnemyId));
                        var choice = Button("enemy-choice-" + a.EnemyId, "",
                            () => TryPlaySelectedTarget(a.EnemyId), "enemy-choice");
                        choice.AddToClassList("target-ready");
                        choice.EnableInClassList("selected", enemySelection.IsSelected(selectedCard, a.EnemyId));
                        if (a.HasSelectionRing) { choice.style.opacity = 0; choice.pickingMode = PickingMode.Ignore; }
                        choice.SetEnabled(active && selectedCard.Enabled);
                        root.Add(choice); enemyChoices.Add((choice, a));
                    }
                }
            if (bookOpen)
            {
                foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
                root.Add(new BattleActionBookPanel(ToggleActionBook, VoyageRuntime.OwnsEncounter(director), v.Tools));
            }
            else if (chartOpen)
            {
                foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
                ChartOverlay();
            }
            else if (director.Paused || v.Phase == BattlePhase.Victory || v.Phase == BattlePhase.Defeat)
            {
                foreach (var child in root.Children().ToArray()) child.SetEnabled(false);
                if (v.Phase != BattlePhase.Victory || !VoyageRuntime.OwnsEncounter(director) || !string.IsNullOrEmpty(VoyageRuntime.EncounterSaveError))
                    Modal(v);
            }
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
            root.schedule.Execute(() =>
            {
                hand.scrollOffset = handOffset;
                tools.scrollOffset = toolOffset;
                var journal = root.Q<ScrollView>(className: "journal");
                if (journal != null) journal.scrollOffset = journalOffset;
                var target = string.IsNullOrEmpty(focused) ? null : root.Q<VisualElement>(focused);
                if (root.focusable && target != null && target.canGrabFocus) target.Focus();
                else if (root.focusable && string.IsNullOrEmpty(focused)) root.Focus();
            });
        }
        private bool TryPlaySelectedTarget(string enemyId)
        {
            var card = director.View?.Hand.FirstOrDefault(item => item.InstanceId == selectedEncounterCard);
            if (card == null) return false;
            if (!enemySelection.Choose(card, enemyId, out var target)) return false;
            if (target == null) { Render(); return true; }
            selectedEncounterCard = 0;
            var result = director.Submit(BattleCommand.Play(card.InstanceId, target));
            if (!result.Success) { selectedEncounterCard = card.InstanceId; Render(); }
            return true;
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
                    selectedSupport = null;
                    var result = director.Submit(BattleCommand.Support(supportId, target.Id));
                    if (!result.Success) { selectedSupport = supportId; Render(); }
                }, "card-target-choice");
                ActionButton(button, active && target.Enabled); options.Add(button);
            }
            root.Add(panel);
        }

        private void RenderCardChoices(BattleView view, bool active)
        {
            var selected = view.Hand.FirstOrDefault(card => card.InstanceId == selectedEncounterCard);
            if (selected == null || selected.Targets.Count == 0 || (selected.IsEncounter && view.BossKind != BossEncounterKind.None)) return;
            var otherTargets = selected.Targets.Where(t => !EnemyCardSelection.IsEnemyTarget(t.Id, view)).ToArray();
            if (otherTargets.Length == 0) return;
            var panel = Box("card-target-panel");
            panel.Add(Text("选择：" + selected.Definition.Name, "card-target-title"));
            var options = new ScrollView(ScrollViewMode.Vertical); options.AddToClassList("card-target-options"); panel.Add(options);
            var index = 0;
            foreach (var option in otherTargets)
            {
                var target = option;
                var cardId = selected.InstanceId;
                var button = Button("card-target-" + index++, target.Name + (string.IsNullOrWhiteSpace(target.Detail) ? "" : "\n" + target.Detail), () =>
                {
                    selectedEncounterCard = 0;
                    var result = director.Submit(BattleCommand.Play(cardId, target.Id));
                    if (!result.Success) { selectedEncounterCard = cardId; Render(); }
                }, "card-target-choice");
                button.EnableInClassList("target-ready", target.Enabled);
                ActionButton(button, active && target.Enabled);
                button.tooltip = target.Detail;
                options.Add(button);
            }
            panel.Add(Button("card-target-cancel", "取消选择 · Esc", () => { selectedEncounterCard = 0; Render(); }, "card-target-cancel"));
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
            overlay.schedule.Execute(() => root.Q<Button>("battle-chart-close")?.Focus());
        }
        private void Modal(BattleView view)
        {
            overlay = Box("ocean-modal"); overlay.pickingMode = PickingMode.Position; root.Add(overlay);
            var sheet = Box("ocean-sheet"); overlay.Add(sheet);
            if (view.Phase == BattlePhase.Victory && VoyageRuntime.OwnsEncounter(director))
            {
                sheet.Add(Text("保存失败", "modal-title"));
                sheet.Add(Text(VoyageRuntime.EncounterSaveError, "modal-copy"));
                sheet.Add(Button("battle-save-retry", "重试保存", () => { VoyageRuntime.ReturnToVoyage(); Render(); }));
                sheet.schedule.Execute(() => sheet.Q<Button>("battle-save-retry")?.Focus());
                return;
            }
            if (showSettings) { sheet.Add(new SettingsPanel(() => { showSettings = false; Render(); })); return; }
            bool end = view.Phase == BattlePhase.Victory || view.Phase == BattlePhase.Defeat;
            if (!end && !journalOpen)
            {
                sheet.AddToClassList("pause");
                sheet.Add(Text("暂停", "pause-overline"));
                sheet.Add(Button("battle-resume", "继续 / Esc", () => director.SetPaused(false), "pause-option"));
            }
            else
            {
                sheet.Add(Text(end ? (view.Phase == BattlePhase.Victory ? "胜利" : "失败") : "航海日志", "modal-title"));
                sheet.Add(Text($"牌库 {view.DrawCount}  ·  弃牌 {view.DiscardCount}  ·  消耗 {view.ExhaustCount}", "pile-summary"));
                if (journalOpen)
                {
                    var log = new ScrollView(); log.AddToClassList("journal");
                    foreach (var evidence in view.Evidence) log.Add(Text($"[{KnowledgeLabel(evidence.Level)}] {evidence.Text} / {BattleSupplyPanel.EvidenceLabel(evidence, view.Turn)}", "log-line"));
                    foreach (var entry in view.Log.Where(x => !x.Contains("阶段："))) log.Add(Text(entry, "log-line"));
                    sheet.Add(log);
                    ScrollWheel.ForwardFrom(sheet, log);
                }
                if (!end) sheet.Add(Button("battle-resume", "返回 / J", () => { journalOpen = false; director.SetPaused(false); }));
            }
            if (!VoyageRuntime.OwnsEncounter(director))
                sheet.Add(Button("battle-retry", "重新开始", () => { chartOpen = false; chartWasPaused = false; showSettings = false; journalOpen = false; director.Restart(); }, "pause-option"));
            if (VoyageRuntime.OwnsEncounter(director))
            {
                if (!string.IsNullOrEmpty(VoyageRuntime.EncounterSaveError))
                    sheet.Add(Text("记录尚未保存：" + VoyageRuntime.EncounterSaveError, "modal-copy"));
                sheet.Add(Button("battle-voyage", end ? "返回航程" : "保存并返回航程",
                    () => { VoyageRuntime.ReturnToVoyage(); Render(); }, "pause-option"));
            }
            sheet.Add(Button("battle-settings", "设置", () => { showSettings = true; Render(); }, "pause-option"));
            sheet.Add(Button("battle-menu", "返回标题", () => SceneManager.LoadScene(menuScene), "pause-option"));
            sheet.schedule.Execute(() => (sheet.Q<Button>(end ? "battle-voyage" : "battle-resume") ?? sheet.Q<Button>("battle-retry"))?.Focus());
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
                if ((presentedBoss != BossEncounterKind.None || director.scenario.encounter.objective != ObjectiveKind.Defeat) && i == 0)
                {
                    plate.style.left = Mathf.Max(18, (root.resolvedStyle.width - width) * .42f);
                    plate.style.top = 100;
                }
                else if (presentedBoss != BossEncounterKind.None)
                {
                    plate.style.left = 32;
                    plate.style.top = summonTop;
                    if (actor.Alive) summonTop += height + (enemyChoices.Any(c => c.actor == actor) ? 62 : 12);
                }
                else
                {
                    var right = nameplates.Count == 2 &&
                        screen.x > director.Stage.battleCamera.WorldToViewportPoint(director.Stage.Actors[1 - i].LabelPosition).x;
                    plate.style.left = Mathf.Clamp(right ? p.x + 70 : p.x - width - 70, 18, Mathf.Max(18, root.resolvedStyle.width - width - 18));
                    plate.style.top = Mathf.Clamp(p.y - height * .5f, 64, Mathf.Max(64, root.resolvedStyle.height - height - 170));
                }
                plate.style.display = screen.z > 0 && actor.Alive ? DisplayStyle.Flex : DisplayStyle.None;
            }
            foreach (var choice in enemyChoices)
            {
                var screen = director.Stage.battleCamera.WorldToViewportPoint(choice.actor.transform.position);
                var width = choice.button.resolvedStyle.width;
                choice.button.style.left = Mathf.Clamp(screen.x * root.resolvedStyle.width - width * .5f, 18, Mathf.Max(18, root.resolvedStyle.width - width - 18));
                choice.button.style.top = Mathf.Clamp((1 - screen.y) * root.resolvedStyle.height + 12, 100, Mathf.Max(100, root.resolvedStyle.height - 300));
                if (presentedBoss != BossEncounterKind.None)
                {
                    var plate = root.Q<Button>("actor-" + choice.actor.EnemyId);
                    choice.button.style.left = plate.resolvedStyle.left;
                    choice.button.style.top = plate.resolvedStyle.top + plate.resolvedStyle.height + 6;
                }
                choice.button.style.display = screen.z > 0 && choice.actor.Alive ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
