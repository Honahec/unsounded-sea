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
    public sealed partial class OceanBattleHud : MonoBehaviour
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
        private readonly Dictionary<string, (int before, int after, float until)> surgeChanges = new Dictionary<string, (int, int, float)>();
        public int RenderedFrames { get; private set; }
        private void OnEnable()
        {
            director = GetComponent<BattleDirector>(); root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear(); root.AddToClassList("ocean-ui"); root.pickingMode = PickingMode.Ignore; root.focusable = true;
            if (menuStyle != null) root.styleSheets.Add(menuStyle);
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(UiFont.Get(font));
            director.Changed += Refresh;
            director.Resolved += OnResolved;
            director.PresentedEnemyAction += PresentEnemyActionImpact;
            root.RegisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown);
            InitializeMotion();
            Render();
        }
        private void OnDisable()
        {
            DisposeMotion();
            chartOpen = false; chartWasPaused = false; bookOpen = false; bookWasPaused = false; showSettings = false; journalOpen = false;
            director.Changed -= Refresh; director.Resolved -= OnResolved; director.PresentedEnemyAction -= PresentEnemyActionImpact;
            root.UnregisterCallback<KeyDownEvent>(Key, TrickleDown.TrickleDown); root.Clear();
        }
        private void OnResolved(BattleView before, BattleView after, BattleCommand command)
        {
            PresentResolution(before, after, command);
            foreach(var enemy in after.Enemies)
            {
                var previous=before.Enemies.FirstOrDefault(x=>x.Id==enemy.Id);
                if(previous!=null&&previous.Surge!=enemy.Surge)surgeChanges[enemy.Id]=(previous.Surge,enemy.Surge,Time.unscaledTime+1.6f);
            }
            if (command.kind != CommandKind.PlayCard) return;
            playedCardFeedback = before.Hand.FirstOrDefault(card => card.InstanceId == command.cardInstanceId)?.Definition.Name;
        }

        private void Key(KeyDownEvent evt)
        {
            if (pointerHeld) { if (evt.keyCode == KeyCode.Escape) CancelDrag(); evt.StopImmediatePropagation(); return; }
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
        {
            var b = new Button(action) { name = name, text = text }; b.AddToClassList(cls);
            var slash=text.LastIndexOf(" / ",StringComparison.Ordinal);
            if(slash>=0){var key=text.Substring(slash+3);var caption=text.Substring(0,slash);b.text=caption;KeyBadge.Attach(b,key);b.tooltip=caption+"（"+key+"）";}
            return b;
        }
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
            if (submittingDrag) return; // The release path rebuilds once after clearing its gesture state.
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
            BeforeMotionRender();
            var oldHand = root.Q<ScrollView>("hand");
            var handOffset = oldHand?.scrollOffset ?? Vector2.zero;
            var handWidth = oldHand?.contentViewport.resolvedStyle.width ?? 0;
            var toolOffset = root.Q<ScrollView>(className: "tools")?.scrollOffset ?? Vector2.zero;
            var journalOffset = root.Q<ScrollView>(className: "journal")?.scrollOffset ?? Vector2.zero;
            var oldPositions = nameplates.ToDictionary(p => p.name, p => new Vector2(p.resolvedStyle.left, p.resolvedStyle.top));
            var waitingForImpact = impactView != null && director.Stage != null && director.Stage.DelaysHudUntilImpact;
            renderedRevision = waitingForImpact ? -1 : director.ViewRevision;
            renderedPaused = director.Paused; renderedSelection = director.SelectedEnemy; renderedNotice = director.Notice;
            actionButtons.Clear();
            root.Clear(); root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion); nameplates.Clear(); var v = waitingForImpact ? impactView : director.View; if (v == null) return;
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
            top.Add(KeyBadge.Attach(Button("battle-chart-toggle", "海图", ToggleChart), "M"));
            top.Add(KeyBadge.Attach(Button("battle-action-book", "行动簿", ToggleActionBook), "B"));

            var tools = new ScrollView(ScrollViewMode.Vertical); tools.AddToClassList("tools"); tools.style.maxHeight = new StyleLength(new Length(48, LengthUnit.Percent)); root.Add(tools);
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
            var objectiveTitle = scenario.encounter.objective == ObjectiveKind.Seal ? "封线" :
                scenario.encounter.objective == ObjectiveKind.Survive ? "撤离" : "压制";
            objective.Add(Text(objectiveTitle, "objective-label"));
            objective.Add(Text($"{v.ObjectiveProgress} / {v.ObjectiveTarget}", "objective-count"));
            objective.tooltip = scenario.objectiveHeading + "：" + v.Objective;

            var controls = Box("turn-controls"); root.Add(controls);
            if (v.BossKind != BossEncounterKind.None)
            {
                var uniqueStatus = BossStatusWithoutProgress(v.BossStatus, v.ObjectiveProgress, v.ObjectiveTarget);
                if (!string.IsNullOrWhiteSpace(uniqueStatus)) root.Add(Text(uniqueStatus, "boss-status"));
                RenderEncounterTargets(v, active);
            }
            controls.Add(Text($"第 {v.Turn} 回合", "turn-number"));
            var action = Box("action-budget");
            action.Add(Text("行动", "action-label"));
            var actionValue = Text($"{v.Actions} / {scenario.encounter.actionsPerTurn}", "action-value");
            actionValue.name = "battle-actions"; action.Add(actionValue); controls.Add(action);

            var vitals = Box("battle-vitals"); root.Add(vitals);
            Vital(vitals, "生命", waitingForImpact ? presentedHealth : v.Health, scenario.encounter.playerHealth, "health", waitingForImpact ? presentedBlock : v.Block, "格挡");
            Vital(vitals, "理智", waitingForImpact ? presentedSanity : v.Sanity, scenario.encounter.playerSanity, "sanity", waitingForImpact ? presentedWard : v.Ward, "防护");
            if (v.Watched > 0)
            {
                var watched = Box("watched");
                watched.tooltip = UiRules.Describe("被注视");
                watched.Add(new SeaUiArt("eye"));
                watched.Add(Text(v.Watched.ToString(), "watched-value"));
                vitals.Add(watched);
            }

            var hand = new ScrollView(ScrollViewMode.Horizontal); hand.name = "hand"; hand.AddToClassList("hand");
            // Ignore the full built-in scroll shell, including the content/vertical-scroll wrapper.
            // Only the cards added below should intercept clicks over the 3D scene.
            hand.pickingMode = PickingMode.Ignore;
            hand.Query<VisualElement>().ForEach(element => element.pickingMode = PickingMode.Ignore);
            hand.horizontalScrollerVisibility = ScrollerVisibility.Hidden; hand.verticalScrollerVisibility = ScrollerVisibility.Hidden; root.Add(hand);
            var fan = Box("fan-track"); hand.Add(fan);
            var spacing = HandSpacing(handWidth, v.Hand.Count);
            fan.style.width = HandWidth(spacing, v.Hand.Count);
            if (handWidth > 0) fan.style.marginLeft = Mathf.Max(0, (handWidth - fan.style.width.value.value) * .5f);
            // Squeeze the fan instead of clipping cards when the panel is narrow (4:3, small windows).
            hand.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var width = hand.contentViewport.resolvedStyle.width;
                var fitted = HandSpacing(width, fan.childCount);
                if (!Mathf.Approximately(fitted, spacing))
                {
                    spacing = fitted;
                    var index = 0;
                    foreach (var card in fan.Children()) card.style.left = index++ * spacing;
                    fan.style.width = HandWidth(spacing, fan.childCount);
                }
                fan.style.marginLeft = Mathf.Max(0, (width - HandWidth(spacing, fan.childCount)) * .5f);
            });
            var cardPresentation = new HandCardPresentation(hand);
            var animateDraw = !director.Paused && !chartOpen && v.PendingChoice == null && !GameSettings.ReducedMotion;
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
                BindCardDrag(b, c);
                b.EnableInClassList("chosen-card", selectedEncounterCard == c.InstanceId);
                b.RegisterCallback<ContextClickEvent>(evt =>
                {
                    if (selectedEncounterCard == c.InstanceId) { selectedEncounterCard = 0; Render(); }
                    else { inspectedCard = c.InstanceId; Render(); }
                    evt.StopPropagation();
                });
                b.tabIndex = cardIndex + 1; b.style.left = cardIndex * spacing;
                b.AddToClassList("fan-" + Mathf.Clamp(cardIndex++ - (v.Hand.Count - 1) / 2, -3, 3));
                cardPresentation.Add(b);
                if (animateDraw && !previousHand.Contains(c.InstanceId))
                {
                    b.AddToClassList("card-entering");
                    QueueCardEntry(b, cardIndex);
                }
                var face = Box("sea-card"); face.pickingMode = PickingMode.Position;
                IllustratedCard.Apply(face, def);
                if (c.IsEncounter) face.Q<Label>("illustrated-cost").text = c.ActionCost.ToString();
                face.Add(Text("D 详情", "card-detail-hint"));
                b.Add(face);
                if (v.Hand.Count > 6)
                {
                    var summary = Text(CardCatalog.FaceDescription(def), "compact-card-effect");
                    summary.tooltip = CardCatalog.FaceDescription(def);
                    b.Add(summary);
                }
                b.tooltip = UiRules.CardDescription(def);
                b.EnableInClassList("card-disabled", !c.Enabled || !c.IsEncounter && (def.Cost > v.Actions || def.SanityCost >= v.Sanity));
                ActionButton(b, active); fan.Add(b);
            }
            previousHand = v.Hand.Select(card => card.InstanceId).ToArray();
            var piles = Box("battle-piles"); piles.name = "battle-piles";
            PileCount(piles, "牌库", v.DrawCount); PileCount(piles, "弃牌", v.DiscardCount); PileCount(piles, "消耗", v.ExhaustCount);
            controls.Insert(0, piles);
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
                    var heading = Box("actor-heading");
                    heading.Add(Text(enemy.Name, "actor-name"));
                    heading.Add(EnemyBadge(enemy.Style));
                    if (enemy.Surge > 0)
                    {
                        var surge = Text($"蓄势 {enemy.Surge}", "enemy-badge surge-badge");
                        surge.pickingMode = PickingMode.Position;
                        surge.tooltip = UiRules.Describe("蓄势");
                        heading.Add(surge);
                    }
                    plate.Add(heading);
                    if (surgeChanges.TryGetValue(enemy.Id, out var surgeChange) && surgeChange.until > Time.unscaledTime)
                    {
                        var feedback = Text($"蓄势 {surgeChange.before} → {surgeChange.after}", "surge-feedback");
                        plate.Add(feedback);
                        feedback.schedule.Execute(feedback.RemoveFromHierarchy).StartingIn((long)((surgeChange.until - Time.unscaledTime) * 1000));
                    }
                    if (!hideHealth)
                    {
                        var hp = Box("actor-health"); var fill = Box("actor-health-fill");
                        fill.style.width = Length.Percent(enemy.MaxHealth > 0 ? 100f * enemy.Health / enemy.MaxHealth : 0); hp.Add(fill); plate.Add(hp);
                    }
                    if (hideHealth && v.BossKind != BossEncounterKind.None)
                    {
                        var shield = new SeaUiArt("shield"); shield.AddToClassList("actor-immune");
                        plate.Add(shield); plate.tooltip = status;
                    }
                    else plate.Add(Text(status, "actor-status"));
                    AddCurrentTurnEvidence(plate, v, enemy);
                    plate.Add(new EnemyIntentDetails(enemy));
                    if(enemy.Id==director.SelectedEnemy)
                        foreach(var finding in v.Evidence.Where(x=>x.Scope==EvidenceScope.Turn&&x.SubjectId==enemy.Id&&x.Turn==v.Turn&&(x.Question==nameof(InvestigationKind.SoundSource)||x.Question==nameof(InvestigationKind.DamageChannel))))
                            plate.Add(Text(finding.Text,"enemy-current-answer"));
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
                AfterMotionRender();
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
            AfterMotionRender();
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
                button.userData = target;
                ActionButton(button, active && target.Enabled);
                button.tooltip = target.Detail;
                options.Add(button);
            }
            panel.Add(KeyBadge.Attach(Button("card-target-cancel", "取消", () => { selectedEncounterCard = 0; Render(); }, "card-target-cancel"), "Esc"));
            root.Add(panel);
        }

        private void RenderEncounterTargets(BattleView view, bool active)
        {
            if (director.Stage == null) return;
            var anchors = director.Stage.GetComponentsInChildren<BossTargetAnchor>();
            var selected = view.Hand.FirstOrDefault(c => c.InstanceId == selectedEncounterCard);
            if (selected == null && view.BossKind == BossEncounterKind.Watcher) return;
            var targets = selected == null || view.BossKind==BossEncounterKind.FlagBearer ? view.EncounterTargets : selected.Targets;
            VisualElement saltBoard = null;
            VisualElement relayBoard=null;
            if (view.BossKind == BossEncounterKind.Surveyor)
            {
                saltBoard = Box("salt-board");
                root.Add(saltBoard);
            }
            if(view.BossKind==BossEncounterKind.FlagBearer){relayBoard=Box("relay-board");root.Add(relayBoard);}
            var offsets = new Dictionary<string, int>();
            var targetIndex = 0;
            foreach (var target in targets)
            {
                var id = target.Id;
                var anchorId = id.StartsWith("scene:",StringComparison.Ordinal)?id.Substring(6):id;
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
                var legalTarget = selected?.Targets.FirstOrDefault(x => x.Enabled && (x.Id == id || x.Id == "scene:" + anchorId));
                var publicState = view.EncounterTargets.FirstOrDefault(x => x.Id == anchorId) ?? target;
                var button = Button("boss-target-" + id, "", () =>
                {
                    if(legalTarget==null)return;
                    var result = director.Submit(BattleCommand.Play(cardId, legalTarget.Id));
                    if (result.Success) { selectedEncounterCard = 0; Render(); }
                }, "boss-target");
                button.tooltip = UiRules.Describe(target.Name + "：" + publicState.Detail + (legalTarget == null ? "\n" + (selected == null ? "先选择对应机关行动牌。" : selected.DisabledReason == "" ? "该牌当前不可操作此目标。" : selected.DisabledReason) : ""));
                button.userData = target;
                if (view.BossKind == BossEncounterKind.Watcher) button.text = target.Name;
                button.EnableInClassList("target-ready", selected != null && target.Enabled);
                ActionButton(button, active && selected != null && selected.Enabled && legalTarget != null);
                button.tabIndex = 100 + targetIndex++;
                if (view.BossKind == BossEncounterKind.FlagBearer)
                {
                    button.AddToClassList("mechanism-dial");
                    var relay = anchorId.Contains(":");
                    if (relay)
                    {
                        var relayId = anchorId.Substring(0, anchorId.IndexOf(':'));
                        button.AddToClassList(relayId == "R1" ? "relay-one" : "relay-two");
                        var state = publicState.State == MechanismState.Off ? "off" :
                            publicState.State == MechanismState.Positive ? "positive" : "reverse";
                        button.AddToClassList("relay-" + state);
                        button.Add(Text(state == "off" ? "○" : state == "positive" ? "+" : "↶", "relay-symbol"));
                        button.tooltip = target.Name + "：切换至" + (state == "off" ? "断电" : state == "positive" ? "正接" : "反接") + "。" + target.Detail;
                    }
                    else
                    {
                        button.AddToClassList("relay-lock");
                        button.AddToClassList("relay-" + anchorId.ToLowerInvariant());
                        button.Add(Text(anchorId == "lock-I" ? "Ⅰ" : anchorId == "lock-II" ? "Ⅱ" : "Ⅲ", "relay-symbol"));
                    }
                    button.EnableInClassList("relay-current", publicState.IsCurrent);
                    root.Add(button);
                }
                else if (saltBoard != null)
                {
                    DecorateSaltCell(button, publicState);
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
        }

        private const float CardWidth = 180, CardStep = 110, HandPadding = 30;
        private static float HandSpacing(float width, int count) =>
            count < 2 || float.IsNaN(width) || width <= 0 ? CardStep : Mathf.Clamp((width - HandPadding - CardWidth) / (count - 1), 52, CardStep);
        private static float HandWidth(float spacing, int count) => CardWidth + Mathf.Max(0, count - 1) * spacing;
        private static void PileCount(VisualElement parent, string title, int count)
        {
            var pile = Box("pile-count"); pile.tooltip = title;
            pile.Add(Text(title, "pile-name")); pile.Add(Text(count.ToString(), "pile-value"));
            parent.Add(pile);
        }
        private static VisualElement EnemyBadge(EnemyStyle style)
        {
            var text = style == EnemyStyle.Rhythm ? "节奏" : style == EnemyStyle.Boss ? "首领" : "隐匿";
            return Text(text, "enemy-badge");
        }

        private static void AddCurrentTurnEvidence(VisualElement plate, BattleView view, EnemyView enemy)
        {
            foreach (var evidence in view.Evidence.Where(entry => entry.Scope == EvidenceScope.Turn &&
                entry.Turn == view.Turn && entry.SubjectId == enemy.Id &&
                (entry.Question == InvestigationKind.SoundSource.ToString() || entry.Question == InvestigationKind.DamageChannel.ToString())))
            {
                var motif = evidence.Question == InvestigationKind.SoundSource.ToString() ? "ear" :
                    evidence.Text.Contains("理智") ? "eye" : "heart";
                var row = Box("actor-evidence");
                row.Add(new SeaUiArt(motif));
                row.Add(Text(evidence.Question == InvestigationKind.SoundSource.ToString() ? "声源 " + evidence.Fact :
                    evidence.Text.Replace("主要威胁：", ""), "actor-evidence-text"));
                row.tooltip = evidence.Text;
                plate.Add(row);
            }
        }

        private static string BossStatusWithoutProgress(string status, int progress, int target)
        {
            if (string.IsNullOrWhiteSpace(status)) return "";
            var exact = new[] { $"封匣 {progress}/{target}", $"封栓 {progress}/{target}", $"闸锁 {progress}/{target}",
                $"已击退 {progress}/{target}" };
            foreach (var value in exact)
                status = status.Replace(" · " + value, "").Replace(value + " · ", "").Replace(value + "\n", "").Replace(value, "");
            return status.Trim(' ', '·', '\n');
        }

        private static void DecorateSaltCell(Button button, EncounterTargetView target)
        {
            button.AddToClassList("salt-cell");
            var lines = target.Detail.Split('\n');
            var state = lines[0];
            var sealedCell = lines.Skip(1).Any(line => line == "封栓");
            button.AddToClassList(state == "?" ? "salt-unknown" : state.StartsWith("测 ", StringComparison.Ordinal) ? "salt-measured" : "salt-open");
            if (state == "?") button.Add(Text("◇", "salt-symbol"));
            else if (state.StartsWith("测 ", StringComparison.Ordinal)) button.Add(Text(state.Substring(2), "salt-number"));
            else if (state == "危险已停用") button.Add(Text("×", "salt-danger"));
            else button.Add(Text(state, "salt-number"));
            if (sealedCell) button.Add(Text("◆", "salt-seal"));
            button.tooltip = target.Name + "：" + (state == "?" ? "未测" : state.StartsWith("测 ", StringComparison.Ordinal) ? "已测，数值 " + state.Substring(2) :
                state == "危险已停用" ? "已开，危险已停用" : "已开，邻近危险 " + state) + (sealedCell ? "；封栓" : "");
        }
        private static void Vital(VisualElement parent, string title, int value, int max, string cls, int guard, string guardName)
        {
            var el = Box("vital"); el.AddToClassList(cls);
            var row = Box("vital-heading"); row.Add(Text(title, "vital-label"));
            var number = Text($"{value} / {max}", "vital-value"); number.name = "battle-" + cls; row.Add(number); el.Add(row);
            var track = Box("vital-track"); var fill = Box("vital-fill");
            fill.style.width = Length.Percent(max > 0 ? Mathf.Clamp01((float)value / max) * 100 : 0);
            track.Add(fill);
            if (cls == "sanity")
            {
                UiRules.SanityTicks(track, max);
                el.tooltip = UiRules.SanityThresholds;
                row.Add(Text(SanityGauge.State(value), "sanity-state"));
            }
            el.Add(track);
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
                sheet.Add(KeyBadge.Attach(Button("battle-resume", "继续", () => director.SetPaused(false), "pause-option"), "Esc"));
            }
            else
            {
                sheet.Add(Text(end ? (view.Phase == BattlePhase.Victory ? "胜利" : "失败") : "航海日志", "modal-title"));
                if (!end) sheet.Add(Text($"牌库 {view.DrawCount}  ·  弃牌 {view.DiscardCount}  ·  消耗 {view.ExhaustCount}", "pile-summary"));
                if (journalOpen)
                {
                    var log = new ScrollView(); log.AddToClassList("journal");
                    foreach (var evidence in view.Evidence) log.Add(Text($"[{KnowledgeLabel(evidence.Level)}] {evidence.Text} / {BattleSupplyPanel.EvidenceLabel(evidence, view.Turn)}", "log-line"));
                    foreach (var entry in view.Log.Where(x => !x.Contains("阶段："))) log.Add(Text(entry, "log-line"));
                    sheet.Add(log);
                    ScrollWheel.ForwardFrom(sheet, log);
                }
                if (!end) sheet.Add(KeyBadge.Attach(Button("battle-resume", "返回", () => { journalOpen = false; director.SetPaused(false); }), "J"));
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
            TickMotion();
        }
        private void PositionLabels()
        {
            if (director.Stage == null || root.panel == null) return;
            var placed = new List<Rect>();
            foreach (var label in encounterLabels)
            {
                if (label.anchor == null) continue;
                var position = director.Stage.battleCamera.WorldToViewportPoint(label.anchor.position);
                // Keep anchored labels on screen at any aspect ratio.
                var w = label.button.resolvedStyle.width; var h = label.button.resolvedStyle.height;
                if (float.IsNaN(w) || float.IsNaN(h)) continue;
                var rect = new Rect(
                    Mathf.Clamp(position.x * root.resolvedStyle.width - w * .5f + label.offset.x, 12, Mathf.Max(12, root.resolvedStyle.width - w - 12)),
                    Mathf.Clamp((1 - position.y) * root.resolvedStyle.height - h * .5f + label.offset.y, 12, Mathf.Max(12, root.resolvedStyle.height - h - 12)), w, h);
                // Clamping can stack neighbours at the screen edge; slide them apart sideways.
                for (var guard = 0; guard < placed.Count && placed.Any(other => other.Overlaps(rect)); guard++)
                {
                    rect.x = placed.Where(other => other.Overlaps(rect)).Max(other => other.xMax) + 6;
                    if (rect.xMax > root.resolvedStyle.width - 12)
                    {
                        rect.x = Mathf.Max(12, root.resolvedStyle.width - rect.width - 12);
                        rect.y = Mathf.Clamp(placed.Where(other => other.Overlaps(rect)).Max(other => other.yMax) + 6, 12, Mathf.Max(12, root.resolvedStyle.height - rect.height - 12));
                    }
                }
                label.button.style.left = rect.x;
                label.button.style.top = rect.y;
                label.button.style.display = position.z > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            // The salt board and relay dials own the left column, so their summons stack on the right.
            var summonsRight = presentedBoss == BossEncounterKind.Surveyor || presentedBoss == BossEncounterKind.FlagBearer;
            var summonTop = summonsRight ? 110f : 170f;
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
                else if (presentedBoss == BossEncounterKind.Watcher)
                {
                    plate.style.width = 210;
                    var right = i >= 4;
                    plate.style.left = i == 1 ? Mathf.Max(18, p.x - width - 20) :
                        i == 5 ? Mathf.Min(p.x + 20, root.resolvedStyle.width - width - 18) :
                        i == 3 ? root.resolvedStyle.width * .34f - 105 : right ? root.resolvedStyle.width - 228 : 18;
                    plate.style.top = i == 1 || i == 5 ? Mathf.Max(100, p.y - height - 20) :
                        i == 3 ? Mathf.Max(100, root.resolvedStyle.height - height - 245) :
                        Mathf.Min(root.resolvedStyle.height * .55f, Mathf.Max(100, root.resolvedStyle.height - height - 245));
                }
                else if (presentedBoss != BossEncounterKind.None)
                {
                    plate.style.left = summonsRight ? Mathf.Max(18, root.resolvedStyle.width - width - 32) : 32;
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
                plate.style.left=Mathf.Clamp(plate.style.left.value.value,12,Mathf.Max(12,root.resolvedStyle.width-width-12));
                plate.style.top=Mathf.Clamp(plate.style.top.value.value,90,Mathf.Max(90,root.resolvedStyle.height-height-300));
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
