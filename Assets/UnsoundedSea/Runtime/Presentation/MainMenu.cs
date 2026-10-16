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
        [SerializeField] private string battleScene = "Battle";
        private VisualElement root;
        private bool settingsOpen;

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
        private static Button Entry(string id, string index, string title, string note, Action action, bool enabled = true)
        {
            var button = new Button(action) { name = id };
            button.AddToClassList("menu-entry");
            button.SetEnabled(enabled);
            button.Add(Text(index, "entry-index"));
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("entry-text");
            text.Add(Text(title, "entry-title"));
            text.Add(Text(note, "entry-note"));
            button.Add(text);
            return button;
        }

        private void Build(string focus)
        {
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
            title.Add(Text("有些深渊，本就不该被探知。", "menu-tagline"));
            column.Add(title);

            var entries = new VisualElement { pickingMode = PickingMode.Ignore };
            entries.AddToClassList("menu-entries");
            var canStart = Application.CanStreamedLevelBeLoaded(battleScene);
            entries.Add(Entry("menu-start", "01", "开始调查", canStart ? "登船，前往不存在于海图上的坐标" : "战斗场景未加入构建列表", StartGame, canStart));
            var settings = Entry("menu-settings", "02", "设置", "音量、显示与界面动效", () => { settingsOpen = true; Build(null); });
            settings.EnableInClassList("open", settingsOpen);
            entries.Add(settings);
            entries.Add(Entry("menu-quit", "03", "退出", "离开调查船", Quit));
            column.Add(entries);
            column.Add(Text("方向键或鼠标选择 · Enter 确认", "menu-hint"));
            root.Add(column);

            var signal = new VisualElement { pickingMode = PickingMode.Ignore };
            signal.AddToClassList("menu-signal");
            signal.Add(Text("无线电记录 · 接收时间不明", "signal-label"));
            signal.Add(Text("来自已废弃航路的求救信号。坐标未载于任何官方海图，每位船员记得的内容都不一样。", "signal-text"));
            root.Add(signal);
            root.Add(Text("原型 v0.1", "menu-version"));

            if (settingsOpen)
                root.Add(new SettingsPanel(() => { settingsOpen = false; Build("menu-settings"); },
                    () => root.EnableInClassList("reduced-motion", GameSettings.ReducedMotion)));
            else if (focus != null)
                root.schedule.Execute(() => root.Q<Button>(focus)?.Focus());
        }

        private void StartGame() => SceneManager.LoadScene(battleScene);
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
