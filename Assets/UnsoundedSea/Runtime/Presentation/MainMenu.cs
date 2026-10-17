using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] private FontAsset interfaceFont;
        [SerializeField] private StyleSheet menuStyle;
        [SerializeField] private string voyageScene = VoyageRuntime.VoyageScene;
        private VisualElement root;
        private bool settingsOpen;
        private bool confirmingNewVoyage;
        private bool debugSelectionOpen;
        private bool collectionOpen;
        private string error;

        private void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            try
            {
                if (interfaceFont == null || menuStyle == null)
                    throw new InvalidOperationException("Assign the project menu font and stylesheet.");
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(UiFont.Get(interfaceFont));
                if (!root.styleSheets.Contains(menuStyle)) root.styleSheets.Add(menuStyle);
                root.AddToClassList("game-menu");
                Build("menu-start");
                root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            }
            catch (Exception exception) { root.Add(Text("菜单加载失败：" + exception.Message, "menu-error")); Debug.LogException(exception); }
        }
        private void OnDisable()
        {
            root?.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            root?.Clear();
        }

        private void OnKey(KeyDownEvent evt)
        {
            if (!debugSelectionOpen || evt.keyCode != KeyCode.Escape) return;
            debugSelectionOpen = false;
            Build("menu-debug");
            evt.StopPropagation();
        }

        private static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
        private static Button Entry(string id, string title, Action action, bool enabled = true)
        {
            var button = new Button(action) { name = id };
            button.AddToClassList("menu-entry");
            button.SetEnabled(enabled);
            var mark = new SeaUiArt("seal");
            mark.AddToClassList("entry-mark");
            button.Add(mark);
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("entry-text");
            text.Add(Text(title, "entry-title"));
            button.tooltip = enabled ? "" : "场景不可用";
            button.Add(text);
            return button;
        }

        private void Build(string focus)
        {
            UiMotion.BeforeBuild(root);
            root.schedule.Execute(() => UiMotion.AfterBuild(root));
            root.Clear();
            root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion);
            var apparition = new SeaUiArt("tentacle");
            apparition.AddToClassList("menu-apparition");
            root.Add(apparition);
            var column = new VisualElement { pickingMode = PickingMode.Ignore };
            column.AddToClassList("menu-column");
            var title = new VisualElement { pickingMode = PickingMode.Ignore };
            title.AddToClassList("menu-title-block");
            title.Add(Text("THE UNSOUNDED SEA", "menu-overline"));
            title.Add(Text("未测之海", "menu-title"));
            var rule = new VisualElement { pickingMode = PickingMode.Ignore };
            rule.AddToClassList("menu-rule");
            title.Add(rule);
            column.Add(title);

            var entries = new VisualElement { pickingMode = PickingMode.Ignore };
            entries.AddToClassList("menu-entries");
            var canStart = Application.CanStreamedLevelBeLoaded(voyageScene);
            if (confirmingNewVoyage)
            {
                entries.Add(Text("覆盖现有航程？此操作无法撤销。", "menu-error"));
                entries.Add(Entry("menu-confirm-new", "开始新航程", StartConfirmed, canStart));
                entries.Add(Entry("menu-cancel-new", "返回", () => { confirmingNewVoyage = false; error = null; Build("menu-start"); }));
            }
            else if (debugSelectionOpen)
            {
                // The selector owns focus while open.
            }
            else if (collectionOpen)
            {
                // The full collection owns focus while open; the sea scene remains visible behind it.
            }
            else
            {
                entries.Add(Entry("menu-start", "启航", StartGame, canStart));
                entries.Add(Entry("menu-crew", CardDeckPreparation.CrewId == "medic" ? "开局船员：林岚 · 医师" : "开局船员：祝遥 · 听音员", ToggleCrew));
                if (VoyageRuntime.HasSavedVoyage)
                    entries.Add(Entry("menu-continue", "继续航程", ContinueGame));
                if (Debug.isDebugBuild)
                    entries.Add(Entry("menu-debug", "调试选关", () => { debugSelectionOpen = true; error = null; Build(null); }));
                entries.Add(Entry("menu-collection", "卡牌图鉴", () => { collectionOpen = true; Build(null); }));
                var settings = Entry("menu-settings", "设置", () => { settingsOpen = true; error = null; Build(null); });
                settings.EnableInClassList("open", settingsOpen);
                entries.Add(settings);
                entries.Add(Entry("menu-quit", "退出", Quit));
            }
            if (!string.IsNullOrEmpty(error)) entries.Add(Text(error, "menu-error"));
            else if (!string.IsNullOrEmpty(VoyageRuntime.EncounterSaveError))
                entries.Add(Text("遭遇结果尚未保存，请选择继续航程重试：" + VoyageRuntime.EncounterSaveError, "menu-error"));
            column.Add(entries);
            column.SetEnabled(!settingsOpen && !debugSelectionOpen);
            root.Add(column);


            if (debugSelectionOpen)
            {
                root.Add(BuildDebugSelector());
                root.schedule.Execute(() => root.Q<Button>("debug-level-0")?.Focus());
            }
            else
            if (collectionOpen)
            {
                column.SetEnabled(false);
                root.Add(new CardCollectionPanel(() => { collectionOpen = false; Build("menu-collection"); }));
                root.schedule.Execute(() => root.Q<Button>("collection-close")?.Focus());
            }
            else
            if (settingsOpen)
            {
                var scrim = new VisualElement();
                scrim.AddToClassList("menu-settings-scrim");
                scrim.Add(new SettingsPanel(() => { settingsOpen = false; Build("menu-settings"); },
                    () => root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion)));
                root.Add(scrim);
            }
            else if (focus != null)
                root.schedule.Execute(() => root.Q<Button>(focus)?.Focus());
        }

        private VisualElement BuildDebugSelector()
        {
            var panel = new VisualElement { name = "debug-selector" };
            panel.AddToClassList("debug-selector");
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("debug-head");
            head.Add(Text("调试选关", "debug-title"));
            head.Add(Text("独立试玩 · 不读写正式航程存档", "debug-note"));
            panel.Add(head);
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "debug-levels" };
            scroll.AddToClassList("debug-levels");
            var index = 0;
            foreach (var group in VoyageEncounterCatalog.AllEntries.GroupBy(entry => entry.isBoss ? "Boss" : entry.group))
            {
                scroll.Add(Text(group.Key, "debug-group"));
                foreach (var entry in group)
                {
                    var selected = entry;
                    var button = new Button(() => LaunchDebugEncounter(selected))
                    {
                        name = "debug-level-" + index++,
                        text = selected.scenario.encounter.displayName
                    };
                    button.AddToClassList("debug-level");
                    scroll.Add(button);
                }
            }
            foreach (var region in new[]
            {
                ("fog", "雾海"), ("bones", "尸骸之海"), ("inverted", "倒悬潮庭"),
                ("fleet", "无灯裂海"), ("abyss", "无星深渊")
            })
            {
                scroll.Add(Text(region.Item2 + " · 非战斗", "debug-group"));
                AddDebugNode(scroll, ref index, region.Item1, RunNodeKind.Reward, "行动奖励");
                AddDebugNode(scroll, ref index, region.Item1, RunNodeKind.Event, "海上事件");
                AddDebugNode(scroll, ref index, region.Item1, RunNodeKind.Archive, "档案节点");
                AddDebugNode(scroll, ref index, region.Item1, RunNodeKind.Supply, "补给整备");
            }
            panel.Add(scroll);
            var close = new Button(() => { debugSelectionOpen = false; Build("menu-debug"); })
            {
                name = "debug-close",
                text = "返回标题 / Esc"
            };
            close.AddToClassList("debug-close");
            panel.Add(close);
            return panel;
        }

        private void AddDebugNode(VisualElement parent, ref int index, string region, RunNodeKind kind, string title)
        {
            var button = new Button(() => LaunchDebugNode(region, kind))
            {
                name = "debug-level-" + index++,
                text = title
            };
            button.AddToClassList("debug-level");
            parent.Add(button);
        }

        private void LaunchDebugEncounter(VoyageEncounterCatalog.Entry entry)
        {
            try { VoyageRuntime.StartDebugEncounter(entry.id, RegionId(entry), entry.isBoss); }
            catch (Exception exception)
            {
                error = "无法打开调试关卡：" + exception.Message;
                debugSelectionOpen = false;
                Build("menu-debug");
                Debug.LogException(exception);
            }
        }

        private void LaunchDebugNode(string region, RunNodeKind kind)
        {
            try { VoyageRuntime.StartDebugNode(region, kind); }
            catch (Exception exception)
            {
                error = "无法打开调试关卡：" + exception.Message;
                debugSelectionOpen = false;
                Build("menu-debug");
                Debug.LogException(exception);
            }
        }

        private static string RegionId(VoyageEncounterCatalog.Entry entry)
        {
            if (entry.id == "fog-visitors" || entry.id == "drowned-bell") return "fog";
            if (entry.id.StartsWith("m02", StringComparison.Ordinal) || entry.id.StartsWith("voyage-bones-", StringComparison.Ordinal)) return "bones";
            if (entry.id.StartsWith("m03", StringComparison.Ordinal) || entry.id.StartsWith("voyage-inverted-", StringComparison.Ordinal)) return "inverted";
            if (entry.id.StartsWith("m04", StringComparison.Ordinal) || entry.id.StartsWith("voyage-fleet-", StringComparison.Ordinal)) return "fleet";
            return "abyss";
        }

        private void ToggleCrew()
        {
            CardDeckPreparation.CrewId = CardDeckPreparation.CrewId == "medic" ? "listener" : "medic";
            Build("menu-crew");
        }

        private void StartGame()
        {
            if (VoyageRuntime.HasSavedVoyage)
            {
                confirmingNewVoyage = true;
                error = null;
                Build("menu-confirm-new");
                return;
            }
            StartConfirmed();
        }
        private void StartConfirmed()
        {
            try
            {
                VoyageRuntime.StartNew();
                SceneManager.LoadScene(voyageScene);
            }
            catch (Exception exception)
            {
                error = "无法开始航程：" + exception.Message;
                confirmingNewVoyage = false;
                Build("menu-start");
                Debug.LogException(exception);
            }
        }
        private void ContinueGame()
        {
            if (!VoyageRuntime.TryResume(out var resumeError))
            {
                error = "无法读取航程存档：" + resumeError;
                Build("menu-continue");
                return;
            }
            if (!Application.CanStreamedLevelBeLoaded(voyageScene))
            {
                error = "航程场景未加入构建列表。";
                Build("menu-continue");
                return;
            }
            SceneManager.LoadScene(voyageScene);
        }
        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
