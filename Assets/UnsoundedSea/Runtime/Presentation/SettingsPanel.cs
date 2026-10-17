using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    // Shared by the title and in-game menus.
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
            var button = new Button(() => { action(); SeaAudio.Play("confirm", .45f, ui: true); Changed(id); }) { name = id, text = text };
            button.AddToClassList(className);
            return button;
        }
        private void Changed(string focus) { GameSettings.Apply(); onChanged?.Invoke(); Build(focus); }

        private VisualElement Row(string title, VisualElement control)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("settings-row");
            var labels = new VisualElement { pickingMode = PickingMode.Ignore };
            labels.AddToClassList("settings-labels");
            labels.Add(Text(title, "settings-name"));
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
        private VisualElement Volume(string id, int value, Action<int> set)
        {
            var volume = new VisualElement { pickingMode = PickingMode.Ignore };
            volume.AddToClassList("volume");
            volume.Add(Option(id + "-down", "-", "volume-step", () => set(value - 1)));
            var steps = new VisualElement { pickingMode = PickingMode.Ignore };
            steps.AddToClassList("volume-steps");
            for (var i = 0; i < GameSettings.VolumeSteps; i++)
            {
                var step = new VisualElement { pickingMode = PickingMode.Ignore };
                step.AddToClassList("volume-pip");
                step.EnableInClassList("filled", i < value);
                steps.Add(step);
            }
            volume.Add(steps);
            volume.Add(Option(id + "-up", "+", "volume-step", () => set(value + 1)));
            volume.Add(Text(value * 10 + "%", "volume-value"));
            return volume;
        }

        private void Build(string focus)
        {
            Clear();
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("settings-head");
            head.Add(Text("设置", "settings-title"));
            Add(head);

            var rows = new ScrollView(ScrollViewMode.Vertical) { name = "settings-scroll" };
            rows.AddToClassList("settings-scroll");
            Add(rows);
            ScrollWheel.ForwardFrom(this, rows);
            rows.Add(Row("总音量", Volume("volume", GameSettings.Volume, value => GameSettings.Volume = value)));
            rows.Add(Row("音乐", Volume("music-volume", GameSettings.MusicVolume, value => GameSettings.MusicVolume = value)));
            rows.Add(Row("环境", Volume("ambience-volume", GameSettings.AmbienceVolume, value => GameSettings.AmbienceVolume = value)));
            rows.Add(Row("动态范围", Choice("audio-dynamics", new[] { "标准", "柔和" }, GameSettings.NarrowDynamics ? 1 : 0, i => GameSettings.NarrowDynamics = i == 1)));
            rows.Add(Row("音效", Volume("effects-volume", GameSettings.EffectsVolume, value => GameSettings.EffectsVolume = value)));
            rows.Add(Row("分辨率", Choice("resolution", GameSettings.ResolutionLabels, GameSettings.Resolution, i => GameSettings.Resolution = i)));
            rows.Add(Row("显示", Choice("display", new[] { "全屏", "窗口" }, GameSettings.Fullscreen ? 0 : 1, i => GameSettings.Fullscreen = i == 0)));
            rows.Add(Row("垂直同步", Choice("vsync", new[] { "开", "关" }, GameSettings.VSync ? 0 : 1, i => GameSettings.VSync = i == 0)));
            rows.Add(Row("动效", Choice("motion", new[] { "标准", "减少" }, GameSettings.ReducedMotion ? 1 : 0, i => GameSettings.ReducedMotion = i == 1)));

            var foot = new VisualElement { pickingMode = PickingMode.Ignore };
            foot.AddToClassList("settings-foot");
            foot.Add(Option("settings-reset", "恢复默认", "settings-secondary", GameSettings.ResetDefaults));
            var back = KeyBadge.Attach(new Button(() => onClose?.Invoke()) { name = "settings-back", text = "返回" }, "Esc");
            back.AddToClassList("settings-primary");
            foot.Add(back);
            Add(foot);

            schedule.Execute(() => (this.Q<Button>(focus ?? "settings-back") ?? this.Q<Button>("settings-back"))?.Focus());
        }
    }
}
