using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    public sealed partial class OceanBattleHud
    {
        BattleMotionLayer motion;
        bool pointerHeld, dragging, releasingPointer, submittingDrag;
        int pointerId, draggedCard;
        Vector2 pressPoint, pointerPoint, ghostPosition, returnPosition;
        VisualElement dragGhost, hoverTarget;
        readonly Dictionary<int, Vector2> handPositions = new Dictionary<int, Vector2>();
        readonly Dictionary<string, float> barWidths = new Dictionary<string, float>();
        bool resolvedMotion;
        int motionRevision = -1;
        float dragWidth, dragHeight, pressPanelY;
        string nextTurnBanner;
        BattleView impactView;
        int presentedHealth, presentedSanity, presentedBlock, presentedWard;

        void QueueCardEntry(VisualElement card, int index)
        {
            float readyAt = -1;
            IVisualElementScheduledItem entry = null;
            entry = card.schedule.Execute(() =>
            {
                if (director == null) { entry?.Pause(); return; }
                if (director.Paused || director.Busy) return;
                if (readyAt < 0) readyAt = Time.unscaledTime;
                if (!GameSettings.ReducedMotion && Time.unscaledTime - readyAt < index * .035f) return;
                card.RemoveFromClassList("card-entering"); entry?.Pause();
            }).Every(16);
        }

        void InitializeMotion()
        {
            motion = new BattleMotionLayer();
            root.RegisterCallback<PointerMoveEvent>(MovePointer, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(ReleasePointer, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerDownEvent>(CancelPointer, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCaptureOutEvent>(LostPointer);
            root.RegisterCallback<PointerCancelEvent>(CancelTouch);
        }
        void DisposeMotion()
        {
            ReleaseCapture();
            root?.UnregisterCallback<PointerMoveEvent>(MovePointer, TrickleDown.TrickleDown);
            root?.UnregisterCallback<PointerUpEvent>(ReleasePointer, TrickleDown.TrickleDown);
            root?.UnregisterCallback<PointerDownEvent>(CancelPointer, TrickleDown.TrickleDown);
            root?.UnregisterCallback<PointerCaptureOutEvent>(LostPointer);
            root?.UnregisterCallback<PointerCancelEvent>(CancelTouch);
            motion?.ClearMotion(); motion = null; dragGhost = null;
            impactView = null;
        }
        void BindCardDrag(Button button, CardView card)
        {
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || director.Paused || director.Busy || director.View.PendingChoice != null) return;
                evt.StopImmediatePropagation();
                if (pointerHeld) return;
                pointerHeld = true; dragging = false; pointerId = evt.pointerId; draggedCard = card.InstanceId;
                pressPoint = pointerPoint = root.WorldToLocal(evt.position);
                pressPanelY = evt.position.y;
                var face = button.Q(className: "sea-card");
                var bounds = face.worldBound;
                returnPosition = ghostPosition = root.WorldToLocal(bounds.position);
                dragWidth = 180; dragHeight = 245;
                root.CapturePointer(pointerId);
            }, TrickleDown.TrickleDown);
        }
        void CancelPointer(PointerDownEvent evt)
        {
            if (!pointerHeld || evt.button != 1) return;
            CancelDrag(); evt.StopImmediatePropagation();
        }
        void CancelTouch(PointerCancelEvent evt) { if (pointerHeld && evt.pointerId == pointerId) CancelDrag(); }
        void LostPointer(PointerCaptureOutEvent evt)
        {
            if (pointerHeld && !releasingPointer && evt.pointerId == pointerId && !root.HasPointerCapture(pointerId)) CancelDrag();
        }
        void ReleaseCapture()
        {
            pointerHeld = false;
            releasingPointer = true;
            if (root != null && root.HasPointerCapture(pointerId)) root.ReleasePointer(pointerId);
            releasingPointer = false;
        }
        void MovePointer(PointerMoveEvent evt)
        {
            if (!pointerHeld || evt.pointerId != pointerId) return;
            pointerPoint = root.WorldToLocal(evt.position);
            evt.StopImmediatePropagation();
            if (!dragging && Vector2.Distance(pointerPoint, pressPoint) >= 8)
            {
                var card = director.View.Hand.FirstOrDefault(c => c.InstanceId == draggedCard);
                if (card == null) { CancelDrag(); return; }
                if (!card.Enabled)
                {
                    ReleaseCapture(); director.ShowInputNotice(card.DisabledReason); draggedCard = 0; return;
                }
                dragging = true;
                if (selectedEncounterCard != card.InstanceId) enemySelection.Reset();
                selectedEncounterCard = card.InstanceId; selectedSupport = null;
                dragGhost = MakeGhost(card); motion.Add(dragGhost);
                Render();
            }
        }
        void ReleasePointer(PointerUpEvent evt)
        {
            if (!pointerHeld || evt.pointerId != pointerId || evt.button != 0) return;
            pointerPoint = root.WorldToLocal(evt.position);
            evt.StopImmediatePropagation();
            ReleaseCapture();
            if (!dragging) { draggedCard = 0; return; } // A mouse click inspects; it never plays a card.
            var card = director.View.Hand.FirstOrDefault(c => c.InstanceId == draggedCard);
            string target = null, enemy = null;
            bool success = false, staged = false;
            submittingDrag = true;
            try
            {
                if (card != null && !director.Paused && !director.Busy)
                {
                    if (card.Targets.Count == 0)
                    {
                        if (pressPanelY - evt.position.y >= 80)
                            success = director.Submit(BattleCommand.Play(card.InstanceId, null)).Success;
                    }
                    else if ((target = DropTarget(card, pointerPoint, out enemy, out _, out _)) != null)
                    {
                        if (enemy != null)
                        {
                            if (card.Targets.Any(t => t.Id.StartsWith(enemy + "|", StringComparison.Ordinal)))
                            {
                                director.Select(enemy); staged = true;
                            }
                            else if (enemySelection.Choose(card, enemy, out var choice))
                            {
                                if (choice == null) staged = true;
                                else success = director.Submit(BattleCommand.Play(card.InstanceId, choice)).Success;
                            }
                        }
                        else success = director.Submit(BattleCommand.Play(card.InstanceId, target == "@self" ? null : target)).Success;
                    }
                }
            }
            finally { submittingDrag = false; FinishDrag(success, staged); }
        }
        void CancelDrag()
        {
            ReleaseCapture(); FinishDrag(false, false);
        }
        void FinishDrag(bool success, bool staged)
        {
            motion.Aiming = false;
            hoverTarget?.RemoveFromClassList("drop-hover"); hoverTarget = null;
            var ghost = dragGhost; dragGhost = null; dragging = false; draggedCard = 0;
            if (ghost != null)
            {
                if (success) ghost.RemoveFromHierarchy();
                else
                {
                    var start = ghostPosition;
                    motion.Animate(ghost, GameSettings.ReducedMotion ? .08f : .22f, t =>
                    {
                        BattleMotionLayer.Place(ghost, Vector2.Lerp(start, returnPosition, UiMotion.Ease(t)));
                        ghost.style.opacity = 1 - t; BattleMotionLayer.Scale(ghost, 1 - .12f * t);
                    }, ghost.RemoveFromHierarchy);
                }
            }
            if (!staged) { selectedEncounterCard = 0; enemySelection.Reset(); }
            Render();
        }
        void OnApplicationFocus(bool focus) { if (!focus && pointerHeld) CancelDrag(); }

        string DropTarget(CardView card, Vector2 point, out string enemy, out VisualElement element, out Vector2 at)
        {
            enemy = null; element = null; at = point;
            if (card == null || !card.Enabled || point.x < 0 || point.y < 0 || point.x > root.resolvedStyle.width || point.y > root.resolvedStyle.height) return null;
            var world = root.LocalToWorld(point);
            // Resolve UI destinations before physics; a menu/tool/hand must never drop through to an actor.
            var picked = root.panel?.Pick(world);
            for (var item = picked; item != null && item != root; item = item.parent)
            {
                if (item.userData is EncounterTargetView option && item.enabledInHierarchy && option.Enabled)
                { element = item; at = root.WorldToLocal(item.worldBound.center); return option.Id; }
                if (item is Button b)
                {
                    if (b.name.StartsWith("actor-", StringComparison.Ordinal)) { enemy = b.name.Substring(6); element = b; break; }
                    if (b.name.StartsWith("enemy-choice-", StringComparison.Ordinal)) { enemy = b.name.Substring(13); element = b; break; }
                    return null;
                }
                if (item.ClassListContains("card-target-panel") || item.ClassListContains("salt-board")) return null;
            }
            if (enemy == null && director.Stage != null && point.y < root.resolvedStyle.height - 150)
            {
                var ray = director.Stage.battleCamera.ViewportPointToRay(new Vector3(point.x / root.resolvedStyle.width, 1 - point.y / root.resolvedStyle.height));
                if (Physics.Raycast(ray, out var hit)) enemy = hit.collider.GetComponentInParent<BattleActorView>()?.EnemyId;
                if (enemy == null) enemy = director.Stage.Actors.FirstOrDefault(a => a.HitSelectionRing(ray))?.EnemyId;
            }
            if (enemy != null)
            {
                var id = enemy;
                var legal = enemySelection.CanChoose(card, id) || card.Targets.Any(t => t.Enabled && t.Id.StartsWith(id + "|", StringComparison.Ordinal));
                if (!legal) { enemy = null; return null; }
                at = ActorPoint(id); return "@enemy";
            }
            return null;
        }

        VisualElement MakeGhost(CardView card)
        {
            var ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            ghost.AddToClassList("card-flight"); IllustratedCard.Apply(ghost, card.Definition);
            ghost.Query<VisualElement>().ForEach(x => x.pickingMode = PickingMode.Ignore);
            ghost.style.width = dragWidth > 0 ? dragWidth : 180; ghost.style.height = dragHeight > 0 ? dragHeight : 245;
            ghost.Q<Label>("illustrated-cost").text = card.ActionCost.ToString();
            BattleMotionLayer.Place(ghost, ghostPosition); return ghost;
        }
        Vector2 ActorPoint(string enemy)
        {
            var actor = director.Stage?.Actors.FirstOrDefault(x => x.EnemyId == enemy);
            if (actor == null) return new Vector2(root.resolvedStyle.width * .5f, root.resolvedStyle.height * .4f);
            var p = director.Stage.battleCamera.WorldToViewportPoint(Vector3.Lerp(actor.transform.position, actor.LabelPosition, .65f));
            return new Vector2(Mathf.Clamp(p.x * root.resolvedStyle.width, 40, root.resolvedStyle.width - 40),
                Mathf.Clamp((1 - p.y) * root.resolvedStyle.height, 90, root.resolvedStyle.height - 190));
        }
        Vector2 PlayerPoint => new Vector2(165, root.resolvedStyle.height - 100);
        Vector2 DiscardPoint => new Vector2(root.resolvedStyle.width - 230, root.resolvedStyle.height - 45);
        void BeforeMotionRender()
        {
            if (motion == null) return;
            UiMotion.BeforeBuild(root);
            motion.RemoveFromHierarchy();
            handPositions.Clear(); barWidths.Clear();
            foreach (var b in root.Query<Button>(className: "card-anchor").ToList())
                if (int.TryParse(b.name.Substring(5), out int id)) handPositions[id] = new Vector2(b.resolvedStyle.left, 0);
            foreach (var bar in root.Query<VisualElement>(className: "actor-health-fill").ToList())
                if (bar.parent?.parent is Button owner) barWidths[owner.name] = bar.resolvedStyle.width;
            foreach (var cls in new[] { "health", "sanity" })
            {
                var bar = root.Q(className: cls)?.Q(className: "vital-fill");
                if (bar != null) barWidths[cls] = bar.resolvedStyle.width;
            }
        }
        void AfterMotionRender()
        {
            if (motion == null || root.panel == null) return;
            if (motion.parent == null) root.Add(motion);
            motion.SetEnabled(true);
            // Effects stay below modals so a pause/inspection is never obscured.
            if (director.Paused || bookOpen || chartOpen || inspectedCard != 0 || director.View?.PendingChoice != null)
                motion.style.display = DisplayStyle.None;
            else motion.style.display = DisplayStyle.Flex;
            if (dragging) root.Q<Button>("card-" + draggedCard)?.AddToClassList("drag-source");
            UiMotion.AfterBuild(root);
            if (director.Stage != null && director.Stage.DelaysHudUntilImpact) return;
            if (motionRevision == director.ViewRevision) return;
            motionRevision = director.ViewRevision;
            foreach (var b in root.Query<Button>(className: "card-anchor").ToList())
            {
                if (!int.TryParse(b.name.Substring(5), out int id)) continue;
                float target = b.style.left.value.value;
                if (handPositions.TryGetValue(id, out var old) && !GameSettings.ReducedMotion)
                    motion.Animate(b, .24f, t => b.style.left = Mathf.Lerp(old.x, target, UiMotion.Ease(t)));
            }
            if (resolvedMotion)
            {
                var actions = root.Q("battle-actions");
                if (actions != null && !GameSettings.ReducedMotion)
                    motion.Animate(actions, .24f, t => BattleMotionLayer.Scale(actions, 1 + Mathf.Sin(t * Mathf.PI) * .12f));
                var widths = new Dictionary<string, float>(barWidths);
                int revision = motionRevision;
                root.schedule.Execute(() =>
                {
                    if (motion == null || revision != motionRevision) return;
                    foreach (var entry in widths)
                    {
                        var bar = entry.Key.StartsWith("actor-", StringComparison.Ordinal)
                            ? root.Q(entry.Key)?.Q(className: "actor-health-fill") : root.Q(className: entry.Key)?.Q(className: "vital-fill");
                        if (bar == null || float.IsNaN(entry.Value)) continue;
                        var target = bar.style.width;
                        float pixels = bar.parent.resolvedStyle.width * target.value.value / 100;
                        if (float.IsNaN(pixels) || Mathf.Abs(pixels - entry.Value) < .1f) continue;
                        motion.Animate(bar, GameSettings.ReducedMotion ? .01f : .32f, t => bar.style.width = Mathf.Lerp(entry.Value, pixels, UiMotion.Ease(t)), () => bar.style.width = target);
                    }
                });
            }
            resolvedMotion = false;
        }
        void TickMotion()
        {
            if (motion == null) return;
            if (pointerHeld && (director.Paused || bookOpen || chartOpen || inspectedCard != 0 || director.View?.PendingChoice != null)) CancelDrag();
            if (!director.Paused && impactView != null && director.Stage != null && !director.Stage.DelaysHudUntilImpact)
                impactView = null;
            if (!director.Paused) motion.Tick(Time.unscaledDeltaTime);
            if (!director.Paused && !director.Busy && nextTurnBanner != null)
            { motion.Banner(nextTurnBanner); nextTurnBanner = null; }
            if (!dragging || dragGhost == null) return;
            var desired = new Vector2(Mathf.Clamp(pointerPoint.x - 90, 8, root.resolvedStyle.width - 188),
                Mathf.Clamp(pointerPoint.y + 28, 8, root.resolvedStyle.height - 253));
            dragGhost.style.rotate = new Rotate(GameSettings.ReducedMotion ? 0 : Mathf.Clamp((desired.x - ghostPosition.x) * .04f, -7, 7));
            ghostPosition = GameSettings.ReducedMotion ? desired : Vector2.Lerp(ghostPosition, desired, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 32));
            BattleMotionLayer.Place(dragGhost, ghostPosition);
            var card = director.View.Hand.FirstOrDefault(c => c.InstanceId == draggedCard);
            if (card?.Targets.Count == 0)
            {
                hoverTarget?.RemoveFromClassList("drop-hover"); hoverTarget = null;
                motion.Aiming = false;
                return;
            }
            var target = DropTarget(card, pointerPoint, out _, out var element, out var at);
            if (hoverTarget != element) { hoverTarget?.RemoveFromClassList("drop-hover"); hoverTarget = element; hoverTarget?.AddToClassList("drop-hover"); }
            motion.Aiming = true; motion.ValidTarget = target != null;
            motion.AimFrom = ghostPosition + new Vector2(90, 0); motion.AimTo = at;
        }
        void PresentResolution(BattleView before, BattleView after, BattleCommand command)
        {
            resolvedMotion = true;
            var waitsForImpact = command.kind == CommandKind.EndTurn && after.BossKind != BossEncounterKind.None &&
                after.ResolvedEnemyActions.Count > 0;
            if (waitsForImpact)
            {
                impactView = before;
                presentedHealth = before.Health; presentedSanity = before.Sanity; presentedBlock = before.Block; presentedWard = before.Ward;
            }
            var card = before.Hand.FirstOrDefault(c => c.InstanceId == command.cardInstanceId);
            if (command.kind == CommandKind.PlayCard && card != null)
            {
                var face = root.Q<Button>("card-" + card.InstanceId)?.Q(className: "sea-card");
                var start = dragging ? ghostPosition : face != null ? root.WorldToLocal(face.worldBound.position) : PlayerPoint;
                var ghost = MakeGhost(card); motion.Add(ghost);
                var damaged = after.Enemies.FirstOrDefault(e => before.Enemies.Any(b => b.Id == e.Id && b.Health > e.Health));
                var destination = damaged != null ? ActorPoint(damaged.Id) : dragging ? pointerPoint : new Vector2(root.resolvedStyle.width * .52f, root.resolvedStyle.height * .4f);
                motion.Animate(ghost, GameSettings.ReducedMotion ? .12f : .5f, t =>
                {
                    var end = card.IsEncounter ? start : card.Definition.Exhaust ? destination + Vector2.up * -70 : DiscardPoint;
                    var p = t < .38f ? Vector2.Lerp(start, destination - new Vector2(90, 120), UiMotion.Ease(t / .38f))
                        : UiMotion.Curve(destination - new Vector2(90, 120), end, (t - .38f) / .62f, 45);
                    BattleMotionLayer.Place(ghost, GameSettings.ReducedMotion ? start : p);
                    BattleMotionLayer.Scale(ghost, Mathf.Lerp(1, .12f, UiMotion.Ease(t))); ghost.style.opacity = 1 - UiMotion.Ease(t);
                }, ghost.RemoveFromHierarchy);
            }
            foreach (var enemy in after.Enemies)
            {
                var old = before.Enemies.FirstOrDefault(x => x.Id == enemy.Id);
                if (old == null) continue;
                int loss = old.Health - enemy.Health;
                bool gunshot = card != null && !card.Definition.Name.Contains("枪托") && (card.Definition.Name.Contains("枪") || card.Definition.Name.Contains("射击") || card.Definition.Name.Contains("火线"));
                if (loss > 0) motion.Pulse(ActorPoint(enemy.Id), gunshot ? BattleMotionLayer.Cue.Shot : BattleMotionLayer.Cue.Strike, "−" + loss, .1f);
                if (!old.IntentConfirmed && enemy.IntentConfirmed) motion.Pulse(ActorPoint(enemy.Id), BattleMotionLayer.Cue.Insight, "已确认", .1f);
            }
            if (after.Block > before.Block) motion.Pulse(PlayerPoint, BattleMotionLayer.Cue.Guard, "格挡 +" + (after.Block - before.Block));
            if (after.Ward > before.Ward) motion.Pulse(PlayerPoint + new Vector2(0, -70), BattleMotionLayer.Cue.Ward, "防护 +" + (after.Ward - before.Ward), .08f);
            if (!waitsForImpact && after.Health != before.Health) motion.Pulse(PlayerPoint, after.Health > before.Health ? BattleMotionLayer.Cue.Heal : BattleMotionLayer.Cue.Impact, "生命 " + (after.Health - before.Health).ToString("+0;-0"), .15f);
            if (!waitsForImpact && after.Sanity != before.Sanity) motion.Pulse(PlayerPoint + new Vector2(0, -70), BattleMotionLayer.Cue.Ward, "理智 " + (after.Sanity - before.Sanity).ToString("+0;-0"), .2f);
            if (after.ObjectiveProgress > before.ObjectiveProgress) motion.Pulse(new Vector2(165, 65), BattleMotionLayer.Cue.Progress, "进度 +" + (after.ObjectiveProgress - before.ObjectiveProgress));
            if (after.Evidence.Count > before.Evidence.Count && !after.Enemies.Any(e => e.IntentConfirmed && before.Enemies.Any(b => b.Id == e.Id && !b.IntentConfirmed)))
                motion.Pulse(dragging ? pointerPoint : ActorPoint(director.SelectedEnemy), BattleMotionLayer.Cue.Insight, "获得线索", .1f);
            if (command.kind == CommandKind.EndTurn)
            {
                motion.Banner("敌方行动");
                nextTurnBanner = after.Phase == BattlePhase.PlayerTurn ? "第 " + after.Turn + " 回合" : null;
                foreach (var old in before.Hand.Where(c => !after.Hand.Any(n => n.InstanceId == c.InstanceId)).Take(10))
                {
                    var face = root.Q<Button>("card-" + old.InstanceId)?.Q(className: "sea-card");
                    if (face == null) continue;
                    var start = root.WorldToLocal(face.worldBound.position); var ghost = MakeGhost(old); motion.Add(ghost);
                    motion.Animate(ghost, .28f, t => { BattleMotionLayer.Place(ghost, GameSettings.ReducedMotion ? start : UiMotion.Curve(start, DiscardPoint, t, 35)); ghost.style.opacity = 1 - t; BattleMotionLayer.Scale(ghost, 1 - .85f * t); }, ghost.RemoveFromHierarchy);
                }
                // Use only executed actions, including blocked attacks; never peek at locked intent.
                int physical = after.ResolvedEnemyActions.Where(a => a.Channel == DamageChannel.Health).Sum(a => a.Amount);
                int blocked = Mathf.Min(before.Block, physical);
                if (!waitsForImpact && blocked > 0) motion.Pulse(PlayerPoint, BattleMotionLayer.Cue.Guard, "防御", .25f);
            }
        }
        void PresentEnemyActionImpact(ResolvedEnemyAction action)
        {
            if (impactView == null) return;
            var healthDelta = action.Health - presentedHealth;
            var sanityDelta = action.Sanity - presentedSanity;
            if (healthDelta != 0) motion.Pulse(PlayerPoint, BattleMotionLayer.Cue.Impact, "生命 " + healthDelta.ToString("+0;-0"));
            if (sanityDelta != 0) motion.Pulse(PlayerPoint + new Vector2(0, -70), BattleMotionLayer.Cue.Ward, "理智 " + sanityDelta.ToString("+0;-0"));
            SetPresentedVital("health", action.Health, director.scenario.encounter.playerHealth, action.Block, "格挡");
            SetPresentedVital("sanity", action.Sanity, director.scenario.encounter.playerSanity, action.Ward, "防护");
            presentedHealth = action.Health; presentedSanity = action.Sanity; presentedBlock = action.Block; presentedWard = action.Ward;
        }

        void SetPresentedVital(string name, int value, int max, int protection, string protectionName)
        {
            var label = root.Q<Label>("battle-" + name);
            if (label != null) label.text = $"{value} / {max}";
            var fill = root.Q(className: name)?.Q(className: "vital-fill");
            if (fill != null) fill.style.width = Length.Percent(max > 0 ? Mathf.Clamp01((float)value / max) * 100 : 0);
            var guard = root.Q<Label>("battle-" + name + "-guard");
            if (guard != null) guard.text = protection > 0 ? $"{protectionName}  {protection}" : string.Empty;
        }
    }
}
