using System.Runtime.CompilerServices;
using UnityEngine;

namespace UnsoundedSea.Runtime
{
    // Device-local presentation preferences. Not part of battle rules or save data.
    public static class GameSettings
    {
        private const string VolumeKey = "settings.volume";
        private const string MusicVolumeKey = "settings.musicVolume";
        private const string EffectsVolumeKey = "settings.effectsVolume";
        private const string FullscreenKey = "settings.fullscreen";
        private const string ResolutionKey = "settings.resolution";
        private const string VSyncKey = "settings.vsync";
        private const string ReducedMotionKey = "settings.reducedMotion";
        private static readonly Vector2Int[] Resolutions =
        {
            new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080)
        };
        private static readonly ConditionalWeakTable<AudioSource, SourceState> SourceVolumes = new ConditionalWeakTable<AudioSource, SourceState>();
        private sealed class SourceState
        {
            public readonly float Volume;
            public SourceState(AudioSource source) { Volume = source.volume; }
        }
        public static readonly string[] ResolutionLabels = { "1280×720", "1600×900", "1920×1080" };
        public const int VolumeSteps = 10;

        public static int Volume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(VolumeKey, 8), 0, VolumeSteps);
            set { PlayerPrefs.SetInt(VolumeKey, Mathf.Clamp(value, 0, VolumeSteps)); Save(); }
        }
        public static int MusicVolume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(MusicVolumeKey, VolumeSteps), 0, VolumeSteps);
            set { PlayerPrefs.SetInt(MusicVolumeKey, Mathf.Clamp(value, 0, VolumeSteps)); Save(); }
        }
        public static int EffectsVolume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(EffectsVolumeKey, VolumeSteps), 0, VolumeSteps);
            set { PlayerPrefs.SetInt(EffectsVolumeKey, Mathf.Clamp(value, 0, VolumeSteps)); Save(); }
        }
        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(FullscreenKey, 1) == 1;
            set { PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0); Save(); }
        }
        public static int Resolution
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ResolutionKey, 2), 0, Resolutions.Length - 1);
            set { PlayerPrefs.SetInt(ResolutionKey, Mathf.Clamp(value, 0, Resolutions.Length - 1)); Save(); }
        }
        public static bool VSync
        {
            get => PlayerPrefs.GetInt(VSyncKey, 1) == 1;
            set { PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0); Save(); }
        }
        public static bool ReducedMotion
        {
            get => PlayerPrefs.GetInt(ReducedMotionKey, 0) == 1;
            set { PlayerPrefs.SetInt(ReducedMotionKey, value ? 1 : 0); Save(); }
        }

        public static void ResetDefaults()
        {
            PlayerPrefs.DeleteKey(VolumeKey); PlayerPrefs.DeleteKey(MusicVolumeKey); PlayerPrefs.DeleteKey(EffectsVolumeKey);
            PlayerPrefs.DeleteKey(FullscreenKey); PlayerPrefs.DeleteKey(ResolutionKey);
            PlayerPrefs.DeleteKey(VSyncKey); PlayerPrefs.DeleteKey(ReducedMotionKey);
            Save();
        }

        public static void ApplyAudioSource(AudioSource source, bool music)
        {
            if (source == null) return;
            var state = SourceVolumes.GetValue(source, item => new SourceState(item));
            source.volume = state.Volume * (music ? MusicVolume : EffectsVolume) / VolumeSteps;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Apply()
        {
            AudioListener.volume = (float)Volume / VolumeSteps;
            foreach (var source in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                ApplyAudioSource(source, source.loop);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            if (Application.isEditor) return;
            var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var resolution = Resolutions[Resolution];
            if (Screen.width != resolution.x || Screen.height != resolution.y || Screen.fullScreenMode != mode)
                Screen.SetResolution(resolution.x, resolution.y, mode);
        }
        private static void Save() { PlayerPrefs.Save(); Apply(); }
    }
}
