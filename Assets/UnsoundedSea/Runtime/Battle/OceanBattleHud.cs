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
        public int RenderedFrames { get; private set; }
        private void OnEnable()
        {
            director = GetComponent<BattleDirector>(); root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear(); root.AddToClassList("ocean-ui"); root.pickingMode = PickingMode.Ignore;
            if (menuStyle != null) root.styleSheets.Add(menuStyle);
            if (styleSheet != null) root.styleSheets.Add(styleSheet);
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);
            director.Changed += Render;
            root.RegisterCallback<KeyDownEvent>(Key);
            Render();
        }
        private void OnDisable() { director.Changed -= Render; root.UnregisterCallback<KeyDownEvent>(Key); root.Clear(); }
        private void Key(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape) return;
            showSettings = false; director.SetPaused(!director.Paused); evt.StopPropagation();
        }
        private static VisualElement Box(string cls) { var v = new VisualElement { pickingMode = PickingMode.Ignore }; v.AddToClassList(cls); return v; }
        private static Label Text(string text, string cls) { var l = new Label(text) { pickingMode = PickingMode.Ignore }; l.AddToClassList(cls); return l; }
        private static Button Button(string name, string text, Action action, string cls = "sea-button")
        { var b = new Button(action) { name = name, text = text }; b.AddToClassList(cls); return b; }
        private void Render()
        {
            root.Clear(); nameplates.Clear(); var v = director.View; if (v == null) return;
            var scenario = director.scenario; bool active = v.Phase == BattlePhase.PlayerTurn && !director.Paused && !director.Busy;
            var top = Box("sea-top"); root.Add(top);
            var title = Box("sea-title"); title.Add(Text("U N S O U N D E D   S E A", "eyebrow")); title.Add(Text(scenario.chapter, "chapter")); top.Add(title);
            var vitals = Box("vitals"); top.Add(vitals);
            Vital(vitals, "船员生命", v.Health, scenario.encounter.playerHealth, "health");
            Vital(vitals, "理智", v.Sanity, scenario.encounter.playerSanity, "sanity");
            var pause = Button("battle-pause", "II  航海日志", () => director.SetPaused(true)); top.Add(pause);

            var left = Box("intel-panel"); root.Add(left); left.Add(Text("01   /   甲板上的来客", "eyebrow"));
            foreach (var enemy in v.Enemies)
            {
                var e = enemy; var b = Button("target-" + e.Id, e.Name + (e.Health <= 0 ? "  /  已倒下" : ""), () => director.Select(e.Id), "target-button");
                b.EnableInClassList("chosen", director.SelectedEnemy == e.Id); b.SetEnabled(e.Health > 0 && !director.Paused); left.Add(b);
            }
            var selected = v.Enemies.FirstOrDefault(x => x.Id == director.SelectedEnemy);
            if (selected != null)
            {
                left.Add(Text(selected.Clue, "clue"));
                left.Add(Text(selected.IntentConfirmed ? "已确认意图" : "未确认 · 可能的行动", "intel-label"));
                left.Add(Text(selected.Intent, "intel-text"));
                left.Add(Text("调查器具 / 每场有限次数", "intel-label"));
                foreach (var tool in v.Tools)
                {
                    var t = tool; var b = Button("tool-" + t.Definition.Id, t.Definition.Name + "  ×" + t.RemainingUses, () => director.Submit(BattleCommand.Investigate(t.Definition.Id, director.SelectedEnemy)));
                    b.SetEnabled(active && t.RemainingUses > 0 && v.Sanity > t.Definition.SanityCost); left.Add(b);
                }
            }
            var right = Box("objective-panel"); root.Add(right);
            right.Add(Text("航 行 目 标", "eyebrow")); right.Add(Text("不要试图杀死海。", "objective-title"));
            right.Add(Text(v.Objective, "objective-description"));
            var seals = Box("seal-row"); right.Add(seals);
            for (var i = 0; i < v.ObjectiveTarget; i++) { var s = Text((i + 1).ToString("00"), "seal"); s.EnableInClassList("complete", i < v.ObjectiveProgress); seals.Add(s); }
            var task = Button("battle-task", scenario.taskLabel + "  /  1 行动", () => director.Submit(BattleCommand.Task()), "task-button");
            task.SetEnabled(active && v.CanAdvanceObjective && v.Actions > 0); right.Add(task);
            right.Add(Text("每回合一次。格挡与理智防护在下回合清空。", "fine-print"));
            right.Add(Text($"格挡  {v.Block:00}     理智防护  {v.Ward:00}", "defenses"));

            var center = Box("stage-caption"); center.Add(Text(scenario.location.ToUpperInvariant(), "eyebrow")); root.Add(center);
            var bottom = Box("hand-area"); root.Add(bottom);
            var bar = Box("hand-heading"); bottom.Add(bar);
            bar.Add(Text($"第 {v.Turn:00} 回合  /  {(director.Busy ? "潮声正在回应" : "你的行动")}", "turn-label"));
            bar.Add(Text($"行动  {new string('◆', v.Actions)}{new string('◇', Math.Max(0, scenario.encounter.actionsPerTurn - v.Actions))}", "action-count"));
            bar.Add(Text($"牌库 {v.DrawCount}  /  弃牌 {v.DiscardCount}  /  消耗 {v.ExhaustCount}", "fine-print"));
            var handRow = Box("hand-row"); bottom.Add(handRow);
            var handScroll = new ScrollView(ScrollViewMode.Horizontal); handScroll.AddToClassList("hand-scroll"); handScroll.horizontalScrollerVisibility = ScrollerVisibility.Auto; handRow.Add(handScroll);
            foreach (var card in v.Hand)
            {
                var c = card; var def = c.Definition;
                var b = Button("card-" + c.InstanceId, "", () => director.Submit(BattleCommand.Play(c.InstanceId, director.SelectedEnemy)), "sea-card");
                var type = def.Effects[0].Id; b.EnableInClassList("attack-card", type.Contains("damage"));
                var head = Box("card-head"); head.Add(Text(def.Cost.ToString(), "card-cost")); head.Add(Text(type.Contains("damage") ? "压制" : type == "block" ? "防御" : type == "ward" ? "镇定" : "调查 / 技巧", "card-type")); b.Add(head);
                b.Add(new NauticalGlyph(type)); b.Add(Text(def.Name, "card-name")); b.Add(Text(def.Description, "card-description"));
                if (def.SanityCost > 0 || def.Exhaust) b.Add(Text((def.SanityCost > 0 ? $"支付 {def.SanityCost} 理智 " : "") + (def.Exhaust ? "· 消耗" : ""), "fine-print"));
                b.SetEnabled(active && def.Cost <= v.Actions && def.SanityCost < v.Sanity); handScroll.Add(b);
            }
            var end = Button("battle-end-turn", "结束回合\n让潮声靠近", () => director.Submit(BattleCommand.EndTurn()), "end-turn"); end.SetEnabled(active); handRow.Add(end);
            var notice = Text(director.Notice, "battle-notice"); root.Add(notice);
            if (director.Stage != null)
                foreach (var actor in director.Stage.Actors)
                {
                    var a = actor; var enemy = v.Enemies.First(x => x.Id == a.EnemyId);
                    var plate = Button("actor-" + a.EnemyId, enemy.Name + "\n" + enemy.Health + " / " + enemy.MaxHealth, () => director.Select(a.EnemyId), "actor-label");
                    plate.EnableInClassList("selected", a.EnemyId == director.SelectedEnemy); plate.SetEnabled(a.Alive && !director.Paused); root.Add(plate); nameplates.Add(plate);
                }
            if (director.Paused || v.Phase == BattlePhase.Victory || v.Phase == BattlePhase.Defeat) Modal(v);
        }
        private static void Vital(VisualElement parent, string title, int value, int max, string cls)
        {
            var el = Box("vital"); el.AddToClassList(cls);
            el.Add(Text(title + "   " + value.ToString("00") + " / " + max, "vital-value"));
            var track = Box("vital-track"); var fill = Box("vital-fill"); fill.style.width = Length.Percent((float)value / max * 100); track.Add(fill); el.Add(track); parent.Add(el);
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
            foreach (var entry in view.Log.Skip(Math.Max(0, view.Log.Count - 12))) log.Add(Text(entry, "log-line"));
            sheet.Add(log);
            if (!end) sheet.Add(Button("battle-resume", "继续航行", () => director.SetPaused(false)));
            sheet.Add(Button("battle-retry", "重新开始这场遭遇", () => { showSettings = false; director.Restart(); }));
            sheet.Add(Button("battle-settings", "设置", () => { showSettings = true; Render(); }));
            sheet.Add(Button("battle-menu", "返回主菜单", () => SceneManager.LoadScene(menuScene)));
        }
        private void LateUpdate()
        {
            RenderedFrames++;
            if (director.Stage == null || root.panel == null) return;
            for (var i = 0; i < nameplates.Count; i++)
            {
                var actor = director.Stage.Actors[i];
                var screen = director.Stage.battleCamera.WorldToScreenPoint(actor.LabelPosition);
                var p = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
                nameplates[i].style.left = p.x - 85; nameplates[i].style.top = p.y - 35;
                nameplates[i].style.display = screen.z > 0 && actor.Alive ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
        private sealed class NauticalGlyph : VisualElement
        {
            private readonly string type;
            public NauticalGlyph(string type) { this.type = type; AddToClassList("card-glyph"); pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
            private void Draw(MeshGenerationContext context)
            {
                var p = context.painter2D; p.strokeColor = new Color(0.64f, 0.55f, 0.35f, 0.7f); p.lineWidth = 1;
                var c = contentRect.center; float r = 18;
                p.BeginPath(); p.Arc(c, r, 0, 360); p.Stroke();
                int count = type.Contains("damage") ? 4 : type == "ward" ? 8 : 6;
                for (var i = 0; i < count; i++) { float a = i * Mathf.PI * 2 / count; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); p.BeginPath(); p.MoveTo(c + d * 6); p.LineTo(c + d * 25); p.Stroke(); }
                p.BeginPath(); p.MoveTo(c + Vector2.up * -12); p.LineTo(c + Vector2.right * 7); p.LineTo(c + Vector2.up * 12); p.LineTo(c + Vector2.left * 7); p.ClosePath(); p.Stroke();
            }
        }
    }
}
