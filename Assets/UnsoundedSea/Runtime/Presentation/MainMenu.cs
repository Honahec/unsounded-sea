using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenu : MonoBehaviour
    {
        [SerializeField] private FontAsset interfaceFont;
        [SerializeField] private StyleSheet menuStyle;
        [SerializeField] private string voyageScene = VoyageRuntime.VoyageScene;
        [SerializeField] private string seaBossScene = "";
        private VisualElement root;
        private bool settingsOpen;
        private bool confirmingNewVoyage;
        private bool bossSelectionOpen;
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
                root.style.unityFontDefinition = FontDefinition.FromSDFFont(interfaceFont);
                if (!root.styleSheets.Contains(menuStyle)) root.styleSheets.Add(menuStyle);
                root.AddToClassList("game-menu");
                Build("menu-start");
            }
            catch (Exception exception) { root.Add(Text("菜单加载失败：" + exception.Message, "menu-error")); Debug.LogException(exception); }
        }
        private void OnDisable() { root?.Clear(); }

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
            else if (bossSelectionOpen)
            {
                var encounters = new[]
                {
                    ("BossM02", "骸潮领唱者"), ("BossM03", "倒悬司衡者"),
                    ("BossM04", "无灯执旗者"), ("BossM05", "无星守望者")
                };
                foreach (var encounter in encounters)
                {
                    var scene = encounter.Item1;
                    entries.Add(Entry("menu-" + scene, encounter.Item2, () => SceneManager.LoadScene(scene),
                        Application.CanStreamedLevelBeLoaded(scene)));
                }
                entries.Add(Entry("menu-boss-back", "返回", () => { bossSelectionOpen = false; Build("menu-boss-encounters"); }));
            }
            else if (collectionOpen)
            {
                // The full collection owns focus while open; the sea scene remains visible behind it.
            }
            else
            {
                entries.Add(Entry("menu-start", "启航", StartGame, canStart));
                if (VoyageRuntime.HasSavedVoyage)
                    entries.Add(Entry("menu-continue", "继续航程", ContinueGame));
                bool hasBoss = !string.IsNullOrWhiteSpace(seaBossScene);
                if (hasBoss)
                    entries.Add(Entry("menu-sea-boss", "沉钟海域", () => SceneManager.LoadScene(seaBossScene), Application.CanStreamedLevelBeLoaded(seaBossScene)));
                entries.Add(Entry("menu-boss-encounters", "禁航海域", () => { bossSelectionOpen = true; Build("menu-BossM02"); }));
                entries.Add(Entry("menu-collection", "行动图鉴 / 初始船员", () => { collectionOpen = true; Build(null); }));
                var settings = Entry("menu-settings", "设置", () => { settingsOpen = true; error = null; Build(null); });
                settings.EnableInClassList("open", settingsOpen);
                entries.Add(settings);
                entries.Add(Entry("menu-quit", "退出", Quit));
            }
            if (!string.IsNullOrEmpty(error)) entries.Add(Text(error, "menu-error"));
            else if (!string.IsNullOrEmpty(VoyageRuntime.EncounterSaveError))
                entries.Add(Text("遭遇结果尚未保存，请选择继续航程重试：" + VoyageRuntime.EncounterSaveError, "menu-error"));
            column.Add(entries);
            column.SetEnabled(!settingsOpen);
            root.Add(column);


            if (collectionOpen)
            {
                column.SetEnabled(false);
                root.Add(new CardCollectionPanel(() => { collectionOpen = false; Build("menu-collection"); }));
                root.schedule.Execute(() => root.Q<Button>("collection-toggle")?.Focus());
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
