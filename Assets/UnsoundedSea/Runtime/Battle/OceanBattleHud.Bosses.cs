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
        private BattleStage notedStage;
        private readonly HashSet<string> saltNotes = new HashSet<string>(StringComparer.Ordinal);

        private void PrepareBossMechanisms(BattleView view)
        {
            root.EnableInClassList("m02-battle", view.BossKind == BossEncounterKind.Choirmaster);
            root.EnableInClassList("m03-battle", view.BossKind == BossEncounterKind.Surveyor);
            root.EnableInClassList("m04-battle", view.BossKind == BossEncounterKind.FlagBearer);
            root.EnableInClassList("m05-battle", view.BossKind == BossEncounterKind.Watcher);
            root.EnableInClassList("boss-mechanics-battle", view.BossKind == BossEncounterKind.Choirmaster || view.BossKind == BossEncounterKind.Surveyor || view.BossKind == BossEncounterKind.FlagBearer);
            if (notedStage != director.Stage) { saltNotes.Clear(); notedStage = director.Stage; }
        }
        private bool RenderSpecialBossMechanisms(BattleView view, bool active)
        {
            if (view.BossKind != BossEncounterKind.Choirmaster && view.BossKind != BossEncounterKind.Surveyor && view.BossKind != BossEncounterKind.FlagBearer) return false;
            var panel = Box("boss-mechanism-panel"); panel.name = "boss-mechanism-panel";
            panel.Add(Text(view.BossKind == BossEncounterKind.Choirmaster ? "唱诗匣" : view.BossKind == BossEncounterKind.Surveyor ? "盐格封栓" : "桅索控制台", "mechanism-title"));
            var selected = view.Hand.FirstOrDefault(c => c.InstanceId == selectedEncounterCard);
            Button Target(EncounterTargetView target, bool notes = false)
            {
                var legal = selected?.Targets.FirstOrDefault(t => t.Enabled && (t.Id == target.Id || t.Id == "scene:" + target.Id));
                var button = Button("boss-target-" + target.Id, "", () =>
                {
                    if (selected == null || !selected.Enabled || legal == null) return;
                    var result = director.Submit(BattleCommand.Play(selected.InstanceId, legal.Id));
                    if (result.Success) { selectedEncounterCard = 0; Render(); }
                }, "boss-target");
                button.AddToClassList("mechanism-tile");
                button.userData = legal ?? new EncounterTargetView(target.Id, target.Name, target.Detail, false);
                button.tooltip = target.Name + "：" + target.Detail;
                button.tabIndex = 100 + view.EncounterTargets.ToList().FindIndex(t => t.Id == target.Id);
                button.EnableInClassList("target-ready", selected != null && selected.Enabled && legal != null);
                button.EnableInClassList("mechanism-complete", target.State == MechanismState.Open);
                ActionButton(button, active && (notes || selected != null && selected.Enabled && legal != null));
                return button;
            }
            if (view.BossKind == BossEncounterKind.Choirmaster)
            {
                panel.Add(Text(view.BossStage == 1 ? "依唱序封匣 · 每回合一次" : "三匣已封 · 发声囊暴露", "mechanism-caption"));
                var row = Box("choir-row");
                foreach (var target in view.EncounterTargets)
                {
                    var button = Target(target);
                    button.Add(Text(target.Name, "choir-name"));
                    button.Add(Text(target.State == MechanismState.Open ? "已封" : "未封", "choir-state"));
                    row.Add(button);
                }
                panel.Add(row);
            }
            else if (view.BossKind == BossEncounterKind.Surveyor)
            {
                panel.Add(Text("总危险 3 · 每回合最多开两格", "mechanism-caption"));
                var board = Box("boss-salt-grid");
                foreach (var target in view.EncounterTargets)
                {
                    var unknown = target.State == MechanismState.Unmeasured || target.State == MechanismState.Measured;
                    var button = Target(target, unknown);
                    DecorateSaltCell(button, target);
                    button.Add(Text(target.Name, "salt-coordinate"));
                    DecorateSaltNotes(button, target);
                    board.Add(button);
                }
                panel.Add(board);
                panel.Add(Text("右键 / F 记号 · 仅作笔记", "mechanism-footnote"));
            }
            else
            {
                panel.Add(Text(view.BossStage == 1 ? "接线 → 开闸 → 击退执旗者" : view.BossStage == 2 ? "每回合可调校一次、释放一锁" : "护持解除 · 攻击本体", "mechanism-caption"));
                var relays = Box("boss-relays");
                foreach (var id in new[] { "R1", "R2" })
                {
                    var column = Box("boss-relay-column"); column.Add(Text(id == "R1" ? "R1\n前桅" : "R2\n尾桅", "relay-heading"));
                    foreach (var target in view.EncounterTargets.Where(t => t.Id.StartsWith(id + ":", StringComparison.Ordinal)))
                    {
                        var button = Target(target);
                        button.Add(Text(target.State == MechanismState.Off ? "断电" : target.State == MechanismState.Positive ? "正接  + / −" : "反接  − / +", "relay-option"));
                        button.EnableInClassList("relay-selected", target.IsCurrent);
                        if (target.IsCurrent) button.Add(Text("当前", "relay-current-note"));
                        column.Add(button);
                    }
                    relays.Add(column);
                }
                if (view.BossStage < 3) panel.Add(relays);
                if (view.BossStage == 1) panel.Add(Text("接线完成后进入三段闸锁", "mechanism-footnote"));
                foreach (var target in view.EncounterTargets.Where(t => view.BossStage >= 2 && t.Id.StartsWith("lock-", StringComparison.Ordinal)))
                {
                    var button = Target(target); button.AddToClassList("boss-lock-row");
                    button.Add(Text(target.Name, "lock-name"));
                    button.Add(Text(target.Detail.Replace("条件：", "").Replace("，", " · "), "lock-condition"));
                    panel.Add(button);
                }
            }
            root.Add(panel); return true;
        }
        private void DecorateSaltNotes(Button button, EncounterTargetView target)
        {
            var unknown = target.State == MechanismState.Unmeasured || target.State == MechanismState.Measured;
            if (!unknown) { saltNotes.Remove(target.Id); return; }
            // View-local notes cannot affect costs, legal targets, evidence or the solution.
            Action toggle = () => { if (!saltNotes.Add(target.Id)) saltNotes.Remove(target.Id); Render(); };
            button.tooltip += "；右键或 F 标记／取消标记（个人笔记）";
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1 || pointerHeld || director.Busy || director.Paused) return;
                toggle(); evt.StopPropagation();
            });
            button.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.F || director.Busy || director.Paused) return;
                toggle(); evt.StopPropagation();
            });
            if (saltNotes.Contains(target.Id)) button.Add(Text("旗", "salt-note"));
        }
    }
}
