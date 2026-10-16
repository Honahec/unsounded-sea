using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class BattleSandbox : MonoBehaviour
    {
        [SerializeField] private EncounterDefinition[] encounters;
        [SerializeField] private int seed = 20261016;
        [SerializeField] private FontAsset interfaceFont;
        private BattleSession session;
        private BattleDefinition definition;
        private IBattleSaveStore saveStore;
        private VisualElement root;
        private string selectedEnemy;
        private string message = "选择目标后点击卡牌。提灯确认可见动作；听筒仅确认声源。";
        private int encounterIndex;
        private FontAsset generatedFont;
        public BattleView CurrentView => session?.View;
        public int RenderedFrames { get; private set; }

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            var selectedFont = interfaceFont;
            if (selectedFont == null)
            {
                var installed = Font.GetOSInstalledFontNames();
                foreach (var family in new[] { "Arial Unicode MS", "Microsoft YaHei", "Noto Sans CJK SC", "PingFang SC" })
                {
                    if (!installed.Contains(family)) continue;
                    generatedFont = FontAsset.CreateFontAsset(family, "Regular", 64);
                    if (generatedFont != null) break;
                }
                selectedFont = generatedFont;
            }
            if (selectedFont != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(selectedFont);
            root.style.color = new Color(0.88f, 0.9f, 0.87f);
            root.style.fontSize = 16;
            root.style.paddingLeft = root.style.paddingRight = 28;
            root.style.paddingTop = root.style.paddingBottom = 20;
            root.style.backgroundColor = new Color(0.025f, 0.055f, 0.07f, 0.93f);
            try { StartEncounter(0); }
            catch (Exception exception) { root.Add(new Label("启动失败：" + exception.Message)); Debug.LogException(exception); }
        }
        private void Update() { RenderedFrames++; }
        private void OnDisable()
        {
            root?.Clear();
            if (generatedFont == null) return;
            foreach (var texture in generatedFont.atlasTextures) if (texture != null) Destroy(texture);
            if (generatedFont.material != null) Destroy(generatedFont.material);
            Destroy(generatedFont); generatedFont = null;
        }

        public void StartEncounter(int index)
        {
            if (encounters == null || index < 0 || index >= encounters.Length || encounters[index] == null)
                throw new InvalidOperationException("Assign encounter assets on BattleSandbox.");
            encounterIndex = index; definition = encounters[index].Build();
            session = new BattleSession(definition, unchecked((uint)seed));
            saveStore = new JsonBattleSaveStore(Path.Combine(Application.persistentDataPath, "battle-" + index + ".json"));
            selectedEnemy = session.View.Enemies[0].Id;
            message = "观察征兆，选择目标，再决定调查或直接应对。";
            Refresh();
        }
        public CommandResult Send(BattleCommand command)
        {
            var result = session.Execute(command);
            message = result.Success ? "操作已结算。" : result.Error;
            Refresh(); return result;
        }
        private void Save()
        {
            try { saveStore.Save(BattleReplay.Capture(session)); message = "已保存种子和完整操作记录。"; }
            catch (Exception exception) { message = "保存失败：" + exception.Message; }
            Refresh();
        }
        private void Load()
        {
            try { session = saveStore.Load().Restore(definition); message = "已重放并恢复战斗。"; }
            catch (Exception exception) { message = "读取失败：" + exception.Message; }
            Refresh();
        }
        private static VisualElement Row()
        { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap; return row; }
        private static Label Text(string text, int size = 16)
        { var label = new Label(text); label.style.fontSize = size; label.style.whiteSpace = WhiteSpace.Normal; label.style.marginBottom = 6; return label; }
        private static Button Button(string text, Action click, bool enabled = true)
        {
            var button = new Button(click) { text = text };
            button.SetEnabled(enabled); button.style.whiteSpace = WhiteSpace.Normal;
            button.style.paddingLeft = button.style.paddingRight = 12;
            button.style.paddingTop = button.style.paddingBottom = 8;
            button.style.marginRight = 8; button.style.marginBottom = 8;
            button.style.backgroundColor = new Color(0.13f, 0.2f, 0.23f);
            button.style.color = new Color(0.91f, 0.89f, 0.8f);
            return button;
        }
        private void Refresh()
        {
            if (root == null || session == null) return;
            var view = session.View;
            root.Clear();
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; root.Add(scroll);
            scroll.Add(Text("THE UNSOUNDED SEA  /  未测之海", 28));
            scroll.Add(Text("基础框架验证场 · 数据驱动战斗 / 有限信息 / 固定种子 " + seed, 14));
            var navigation = Row();
            for (var i = 0; i < encounters.Length; i++)
            { var index = i; navigation.Add(Button(encounters[i].displayName, () => StartEncounter(index))); }
            navigation.Add(Button("重开", () => StartEncounter(encounterIndex)));
            navigation.Add(Button("保存", Save)); navigation.Add(Button("读取", Load)); scroll.Add(navigation);
            scroll.Add(Text($"回合 {view.Turn}   |   生命 {view.Health}/{definition.Health}   理智 {view.Sanity}/{definition.Sanity}   行动 {view.Actions}   格挡 {view.Block}   理智防护 {view.Ward}", 20));
            scroll.Add(Text("目标：" + view.Objective));
            if (view.Phase == BattlePhase.Victory || view.Phase == BattlePhase.Defeat)
                scroll.Add(Text(view.Phase == BattlePhase.Victory ? "遭遇完成 · 你暂时封住了深渊。" : "航程失败 · 查看日志复盘，或重新开始。", 24));

            var columns = Row();
            var enemyPanel = new VisualElement(); enemyPanel.style.flexGrow = 1; enemyPanel.style.flexBasis = new Length(55, LengthUnit.Percent);
            foreach (var enemy in view.Enemies)
            {
                var id = enemy.Id;
                enemyPanel.Add(Button($"{(id == selectedEnemy ? "▸ " : "")}{enemy.Name}  {enemy.Health}/{enemy.MaxHealth}", () => { selectedEnemy = id; Refresh(); }, enemy.Health > 0));
                enemyPanel.Add(Text("征兆：" + enemy.Clue));
                enemyPanel.Add(Text((enemy.IntentConfirmed ? "已确认：" : "已知范围：") + enemy.Intent));
            }
            var tools = Row();
            foreach (var tool in view.Tools)
            {
                var id = tool.Definition.Id;
                tools.Add(Button($"{tool.Definition.Name} · {tool.RemainingUses} 次 / 支付 {tool.Definition.SanityCost} 理智",
                    () => Send(BattleCommand.Investigate(id, selectedEnemy)), tool.RemainingUses > 0 && view.Phase == BattlePhase.PlayerTurn));
            }
            enemyPanel.Add(tools);
            foreach (var evidence in view.Evidence.Skip(Math.Max(0, view.Evidence.Count - 3)))
                enemyPanel.Add(Text($"[{evidence.Level}] {evidence.Text} · 来源：{evidence.Source}", 14));
            columns.Add(enemyPanel);
            var journal = new VisualElement(); journal.style.flexGrow = 1; journal.style.flexBasis = new Length(40, LengthUnit.Percent);
            journal.style.paddingLeft = 16; journal.Add(Text("航海日志 · 最近结算", 18));
            foreach (var entry in view.Log.Skip(Math.Max(0, view.Log.Count - 7))) journal.Add(Text(entry, 13));
            columns.Add(journal); scroll.Add(columns);

            scroll.Add(Text($"手牌 {view.Hand.Count}   抽牌堆 {view.DrawCount}   弃牌堆 {view.DiscardCount}   消耗区 {view.ExhaustCount}", 17));
            var cards = Row();
            foreach (var card in view.Hand)
            {
                var id = card.InstanceId; var data = card.Definition;
                var button = Button($"{data.Cost} 行动 · {data.Name}\n{data.Description}" + (data.SanityCost > 0 ? $"\n主动支付 {data.SanityCost} 理智" : "") + (data.Exhaust ? "\n使用后消耗" : ""),
                    () => Send(BattleCommand.Play(id, selectedEnemy)), view.Phase == BattlePhase.PlayerTurn && view.Actions >= data.Cost && view.Sanity > data.SanityCost);
                button.style.width = 190; button.style.minHeight = 105; cards.Add(button);
            }
            scroll.Add(cards);
            var actions = Row();
            actions.Add(Button("绘制封线 · 1 行动", () => Send(BattleCommand.Task()), view.CanAdvanceObjective));
            actions.Add(Button("结束回合 →", () => Send(BattleCommand.EndTurn()), view.Phase == BattlePhase.PlayerTurn));
            scroll.Add(actions); scroll.Add(Text(message, 14));
        }
    }
}
