using System;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattleHud : MonoBehaviour
    {
        [SerializeField] private EncounterDefinition[] encounters;
        [SerializeField] private int initialEncounter = 1;
        [SerializeField] private int seed = 20261016;
        [SerializeField] private FontAsset interfaceFont;
        [SerializeField] private StyleSheet interfaceStyle;
        private BattleSession session;
        private BattleDefinition definition;
        private VisualElement root;
        private string selectedEnemy;
        private string message;
        private int encounterIndex;
        private bool journalOpen;
        public BattleView CurrentView => session?.View;
        public int RenderedFrames { get; private set; }

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            try
            {
                if (interfaceFont == null || interfaceStyle == null)
                    throw new InvalidOperationException("Assign the project HUD font and stylesheet.");
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(interfaceFont);
                if (!root.styleSheets.Contains(interfaceStyle)) root.styleSheets.Add(interfaceStyle);
                root.AddToClassList("battle-hud");
                root.RegisterCallback<GeometryChangedEvent>(Resize);
                StartEncounter(initialEncounter);
            }
            catch (Exception exception) { root.Add(Text("界面加载失败：" + exception.Message, "error")); Debug.LogException(exception); }
        }
        private void Update() { RenderedFrames++; }
        private void OnDisable() { root?.UnregisterCallback<GeometryChangedEvent>(Resize); root?.Clear(); }
        private void Resize(GeometryChangedEvent evt) { root.EnableInClassList("compact", evt.newRect.width < 1200 || evt.newRect.height < 740); }

        public void StartEncounter(int index)
        {
            if (encounters == null || index < 0 || index >= encounters.Length || encounters[index] == null)
                throw new InvalidOperationException("Assign encounter assets on BattleHud.");
            encounterIndex = index;
            definition = encounters[index].Build();
            session = new BattleSession(definition, unchecked((uint)seed));
            selectedEnemy = session.View.Enemies[0].Id;
            message = "先观察征兆，再决定如何应对。";
            journalOpen = false;
            Refresh();
        }
        public CommandResult Send(BattleCommand command)
        {
            var result = session.Execute(command);
            message = result.Success ? "" : result.Error;
            Refresh();
            return result;
        }
        private static VisualElement Element(string className)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            return element;
        }
        private static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
        private static Button Button(string name, string text, string className, Action action, bool enabled = true)
        {
            var button = new Button(action) { name = name, text = text };
            button.AddToClassList(className);
            button.SetEnabled(enabled);
            return button;
        }
        private static VisualElement Meter(string title, int value, int maximum, string className)
        {
            var meter = Element("vital"); meter.AddToClassList(className);
            meter.Add(Text(title, "vital-title"));
            meter.Add(Text($"{value} / {maximum}", "vital-value"));
            var track = Element("vital-track");
            var fill = Element("vital-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01((float)value / maximum) * 100);
            track.Add(fill); meter.Add(track);
            return meter;
        }
        private void Refresh()
        {
            if (root == null || session == null) return;
            var view = session.View;
            var focused = (root.panel?.focusController.focusedElement as VisualElement)?.name;
            var handOffset = root.Q<ScrollView>("hand")?.scrollOffset ?? Vector2.zero;
            root.Clear();

            var vitals = Element("vitals");
            vitals.Add(Meter("生命", view.Health, definition.Health, "health"));
            vitals.Add(Meter("理智", view.Sanity, definition.Sanity, "sanity"));
            root.Add(vitals);
            var heading = Element("encounter-heading");
            heading.Add(Text("THE UNSOUNDED SEA", "overline"));
            heading.Add(Text(view.Enemies.Count == 1 ? view.Enemies[0].Name : "未知海域的遭遇", "encounter-name"));
            heading.Add(Text($"第 {view.Turn:00} 回合  /  {view.Objective}", "objective"));
            root.Add(heading);
            root.Add(Button("journal-toggle", journalOpen ? "收起档案" : "调查档案", "journal-toggle", () => { journalOpen = !journalOpen; Refresh(); }));

            var targetBar = Element("targets");
            foreach (var enemy in view.Enemies)
            {
                var id = enemy.Id;
                var target = Button("enemy-" + id, $"{enemy.Name}   {enemy.Health} / {enemy.MaxHealth}", "target", () => { selectedEnemy = id; Refresh(); }, enemy.Health > 0);
                target.EnableInClassList("selected", selectedEnemy == id);
                targetBar.Add(target);
            }
            root.Add(targetBar);
            var selected = view.Enemies.FirstOrDefault(e => e.Id == selectedEnemy);
            if (selected != null)
            {
                var intel = Element("intel");
                intel.Add(Text("眼前的征兆", "section-label"));
                intel.Add(Text(selected.Clue, "clue"));
                var intent = Element("intent");
                intent.EnableInClassList("confirmed", selected.IntentConfirmed);
                intent.Add(Text(selected.IntentConfirmed ? "意图已确认" : "可能的行动", "intent-label"));
                intent.Add(Text(selected.Intent, "intent-text")); intel.Add(intent);
                root.Add(intel);
            }
            var tools = Element("tools");
            tools.Add(Text("调查工具", "section-label"));
            foreach (var tool in view.Tools)
            {
                var id = tool.Definition.Id;
                var cost = tool.Definition.SanityCost > 0 ? $"支付 {tool.Definition.SanityCost} 理智" : "不消耗行动";
                tools.Add(Button("tool-" + id, $"{tool.Definition.Name}\n剩余 {tool.RemainingUses} 次 · {cost}", "tool", () => Send(BattleCommand.Investigate(id, selectedEnemy)),
                    tool.RemainingUses > 0 && view.Phase == BattlePhase.PlayerTurn));
            }
            root.Add(tools);
            if (journalOpen)
            {
                var journal = Element("journal");
                journal.Add(Text("调查档案", "journal-title"));
                var entries = new ScrollView { name = "journal-entries" };
                foreach (var evidence in view.Evidence)
                {
                    var level = evidence.Level == KnowledgeLevel.Verified ? "已验证" : evidence.Level == KnowledgeLevel.Observed ? "已观察" : evidence.Level == KnowledgeLevel.Rumored ? "传闻" : "未知";
                    entries.Add(Text($"{level} / {evidence.Text}\n来源：{evidence.Source}", "evidence"));
                }
                entries.Add(Text("行动记录", "section-label"));
                foreach (var entry in view.Log) entries.Add(Text(entry, "log-entry"));
                journal.Add(entries); root.Add(journal);
            }

            var player = Element("player-status");
            player.Add(Text("可用行动", "section-label"));
            player.Add(Text(view.Actions.ToString(), "action-value"));
            player.Add(Text($"格挡 {view.Block}  /  理智防护 {view.Ward}", "defense"));
            root.Add(player);
            var hand = new ScrollView(ScrollViewMode.Horizontal) { name = "hand" };
            hand.AddToClassList("hand");
            foreach (var card in view.Hand)
            {
                var id = card.InstanceId; var data = card.Definition;
                var button = Button("card-" + id, "", "card", () => Send(BattleCommand.Play(id, selectedEnemy)),
                    view.Phase == BattlePhase.PlayerTurn && view.Actions >= data.Cost && view.Sanity > data.SanityCost);
                button.tooltip = data.Name + "：" + data.Description;
                button.Add(Text(data.Cost.ToString(), "card-cost"));
                button.Add(Text(data.Name, "card-name"));
                button.Add(Text(data.Description, "card-description"));
                if (data.SanityCost > 0) button.Add(Text($"主动支付 {data.SanityCost} 理智", "card-price"));
                if (data.Exhaust) button.Add(Text("使用后消耗", "card-price"));
                hand.Add(button);
            }
            root.Add(hand);
            root.Add(Text($"抽牌 {view.DrawCount}   /   弃牌 {view.DiscardCount}   /   消耗 {view.ExhaustCount}", "piles"));
            var controls = Element("turn-controls");
            if (definition.Objective == ObjectiveKind.Seal)
                controls.Add(Button("objective-action", "绘制封线\n1 行动 · 每回合一次", "task", () => Send(BattleCommand.Task()), view.CanAdvanceObjective));
            controls.Add(Button("end-turn", "结束回合", "end-turn", () => Send(BattleCommand.EndTurn()), view.Phase == BattlePhase.PlayerTurn));
            root.Add(controls);
            var notice = Text(message, "notice"); notice.name = "battle-message"; root.Add(notice);
            if (view.Phase == BattlePhase.Victory || view.Phase == BattlePhase.Defeat)
            {
                var outcome = Element("outcome");
                outcome.Add(Text(view.Phase == BattlePhase.Victory ? "暂离深渊" : "航程终止", "outcome-title"));
                outcome.Add(Text(view.Phase == BattlePhase.Victory ? "这次遭遇已经结束。未知之物仍在海雾深处。" : "生命或理智耗尽。打开调查档案，复盘最后的征兆。", "outcome-description"));
                outcome.Add(Button("restart", "重新遭遇", "restart", () => StartEncounter(encounterIndex)));
                root.Add(outcome);
            }
            hand.schedule.Execute(() =>
            {
                hand.scrollOffset = handOffset;
                if (!string.IsNullOrEmpty(focused)) (root.Q<Button>(focused) ?? root.Q<Button>("end-turn"))?.Focus();
            });
        }
    }
}
