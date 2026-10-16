using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    // Paper settings sheet shared by the title menu and the in-battle pause menu.
    public sealed class SettingsPanel : VisualElement
    {
        private readonly Action onClose;
        private readonly Action onChanged;

        public SettingsPanel(Action onClose, Action onChanged = null)
        {
            this.onClose = onClose; this.onChanged = onChanged;
            name = "settings";
            AddToClassList("settings");
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                evt.StopPropagation(); onClose?.Invoke();
            });
            Build(null);
        }

        private static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
        private Button Option(string id, string text, string className, Action action)
        {
            var button = new Button(() => { action(); Changed(id); }) { name = id, text = text };
            button.AddToClassList(className);
            return button;
        }
        private void Changed(string focus) { GameSettings.Apply(); onChanged?.Invoke(); Build(focus); }

        private VisualElement Row(string title, string note, VisualElement control)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("settings-row");
            var labels = new VisualElement { pickingMode = PickingMode.Ignore };
            labels.AddToClassList("settings-labels");
            labels.Add(Text(title, "settings-name"));
            labels.Add(Text(note, "settings-note"));
            row.Add(labels); row.Add(control);
            return row;
        }
        private VisualElement Choice(string id, string[] options, int selected, Action<int> choose)
        {
            var group = new VisualElement { pickingMode = PickingMode.Ignore };
            group.AddToClassList("choice");
            for (var i = 0; i < options.Length; i++)
            {
                var index = i;
                var button = Option($"{id}-{i}", options[i], "choice-option", () => choose(index));
                button.EnableInClassList("selected", i == selected);
                group.Add(button);
            }
            return group;
        }

        private void Build(string focus)
        {
            Clear();
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("settings-head");
            head.Add(Text("设置", "settings-title"));
            head.Add(Text("更改立即生效，并保存在本机", "settings-subtitle"));
            Add(head);

            var volume = new VisualElement { pickingMode = PickingMode.Ignore };
            volume.AddToClassList("volume");
            volume.Add(Option("volume-down", "-", "volume-step", () => GameSettings.Volume--));
            var steps = new VisualElement { pickingMode = PickingMode.Ignore };
            steps.AddToClassList("volume-steps");
            for (var i = 0; i < GameSettings.VolumeSteps; i++)
            {
                var step = new VisualElement { pickingMode = PickingMode.Ignore };
                step.AddToClassList("volume-pip");
                step.EnableInClassList("filled", i < GameSettings.Volume);
                steps.Add(step);
            }
            volume.Add(steps);
            volume.Add(Option("volume-up", "+", "volume-step", () => GameSettings.Volume++));
            volume.Add(Text(GameSettings.Volume * 10 + "%", "volume-value"));
            Add(Row("主音量", "环境声、征兆音效与界面音", volume));
            Add(Row("显示模式", "仅在独立运行的游戏中生效", Choice("display", new[] { "全屏", "窗口" }, GameSettings.Fullscreen ? 0 : 1, i => GameSettings.Fullscreen = i == 0)));
            Add(Row("垂直同步", "避免画面撕裂", Choice("vsync", new[] { "开启", "关闭" }, GameSettings.VSync ? 0 : 1, i => GameSettings.VSync = i == 0)));
            Add(Row("界面动效", "减少后，卡牌与情报不再做过渡", Choice("motion", new[] { "标准", "减少" }, GameSettings.ReducedMotion ? 1 : 0, i => GameSettings.ReducedMotion = i == 1)));

            var foot = new VisualElement { pickingMode = PickingMode.Ignore };
            foot.AddToClassList("settings-foot");
            foot.Add(Option("settings-reset", "恢复默认", "settings-secondary", GameSettings.ResetDefaults));
            var back = new Button(() => onClose?.Invoke()) { name = "settings-back", text = "返回 · Esc" };
            back.AddToClassList("settings-primary");
            foot.Add(back);
            Add(foot);

            schedule.Execute(() => (this.Q<Button>(focus ?? "settings-back") ?? this.Q<Button>("settings-back"))?.Focus());
        }
    }
}
