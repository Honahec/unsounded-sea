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
        private bool choosingCrew;
        private bool debugSelectionOpen;
        private bool collectionOpen;
        private bool hasSavedVoyage;
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
                hasSavedVoyage = VoyageRuntime.HasSavedVoyage;
                Build(hasSavedVoyage ? "menu-continue" : "menu-start");
                root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            }
            catch (Exception exception) { root.Add(Text("菜单加载失败：" + exception.Message, "menu-error")); Debug.LogException(exception); }
        }
        private void Update() => HotkeyRelay.Poll(root, HandleKey);

        private void OnDisable()
        {
            root?.UnregisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            root?.Clear();
        }

        private void OnKey(KeyDownEvent evt) { if (HotkeyRelay.Dispatch(root, evt.keyCode, HandleKey)) evt.StopPropagation(); }
        private bool HandleKey(KeyCode key)
        {
            if (key != KeyCode.Escape) return false;
            if (settingsOpen) { settingsOpen = false; Build("menu-settings"); }
            else if (collectionOpen) { collectionOpen = false; Build("menu-collection"); }
            else if (choosingCrew) { choosingCrew = false; Build("menu-start"); }
            else if (debugSelectionOpen) { debugSelectionOpen = false; Build("menu-debug"); }
            else return false;
            return true;
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
                entries.Add(Entry("menu-confirm-new", "开始新航程", ChooseCrew, canStart));
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
                if (hasSavedVoyage)
                    entries.Add(Entry("menu-continue", "继续航程", ContinueGame));
                entries.Add(Entry("menu-start", hasSavedVoyage ? "新航程" : "启航", StartGame, canStart));
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
                entries.Add(Text("战斗结果保存失败。选择「继续航程」重试：" + VoyageRuntime.EncounterSaveError, "menu-error"));
            column.Add(entries);
            column.SetEnabled(!settingsOpen && !debugSelectionOpen && !choosingCrew);
            root.Add(column);


            if (choosingCrew)
            {
                column.SetEnabled(false);
                root.Add(BuildCrewPicker());
                root.schedule.Execute(() => root.Q<Button>("crew-" + CardDeckPreparation.CrewId)?.Focus());
            }
            else if (debugSelectionOpen)
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
            foreach (var group in VoyageEncounterCatalog.MenuEntries.GroupBy(entry => entry.isBoss ? "Boss" : entry.group))
            {
                scroll.Add(Text(group.Key, "debug-group"));
                foreach (var entry in group)
                {
                    var selected = entry;
                    var button = new Button(() => LaunchDebugEncounter(selected))
                    {
                        name = "debug-level-" + index++,
                        text = string.IsNullOrEmpty(selected.displayName) ? selected.LoadScenario().encounter.displayName : selected.displayName
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

        private VisualElement BuildCrewPicker()
        {
            var panel = new VisualElement { name = "crew-picker" };
            panel.AddToClassList("crew-picker");
            panel.Add(Text("选择开局船员", "crew-picker-title"));
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("crew-picker-row");
            foreach (var id in new[] { "medic", "listener" })
            {
                var crew = SupportCatalog.CrewMember(id);
                var crewId = id;
                var card = new Button(() => { CardDeckPreparation.CrewId = crewId; choosingCrew = false; StartConfirmed(); }) { name = "crew-" + id };
                card.AddToClassList("crew-card");
                card.EnableInClassList("selected", CardDeckPreparation.CrewId == id);
                card.Add(new SeaUiArt(crew.Kind == CrewSupportKind.Medic ? "heart" : "ear"));
                var names = crew.Name.Split(new[] { " · " }, StringSplitOptions.None);
                card.Add(Text(names[0], "crew-card-name"));
                if (names.Length > 1) card.Add(Text(names[1], "crew-card-role"));
                card.Add(Text(SupportCatalog.CrewDescription(crew.Kind), "crew-card-copy"));
                row.Add(card);
            }
            panel.Add(row);
            var back = new Button(() => { choosingCrew = false; Build("menu-start"); }) { name = "crew-back", text = "返回" };
            back.AddToClassList("crew-back");
            panel.Add(KeyBadge.Attach(back, "Esc"));
            return panel;
        }

        private void ChooseCrew()
        {
            confirmingNewVoyage = false;
            choosingCrew = true;
            error = null;
            Build(null);
        }

        private void StartGame()
        {
            if (GameSceneLoader.IsLoading) return;
            hasSavedVoyage = VoyageRuntime.HasSavedVoyage;
            if (hasSavedVoyage)
            {
                confirmingNewVoyage = true;
                error = null;
                Build("menu-confirm-new");
                return;
            }
            ChooseCrew();
        }
        private void StartConfirmed()
        {
            if (GameSceneLoader.IsLoading) return;
            try
            {
                VoyageRuntime.StartNew();
                Build(null);
                GameSceneLoader.Depart(voyageScene, FindAnyObjectByType<MainMenuSeaPresentation>());
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
            if (GameSceneLoader.IsLoading) return;
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
            GameSceneLoader.Load(voyageScene);
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
