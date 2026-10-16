using UnityEngine;

namespace UnsoundedSea.Runtime
{
    // Device-local presentation preferences. Not part of battle rules or save data.
    public static class GameSettings
    {
        private const string VolumeKey = "settings.volume";
        private const string FullscreenKey = "settings.fullscreen";
        private const string VSyncKey = "settings.vsync";
        private const string ReducedMotionKey = "settings.reducedMotion";
        public const int VolumeSteps = 10;

        public static int Volume
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(VolumeKey, 8), 0, VolumeSteps);
            set { PlayerPrefs.SetInt(VolumeKey, Mathf.Clamp(value, 0, VolumeSteps)); Save(); }
        }
        public static bool Fullscreen
        {
            get => PlayerPrefs.GetInt(FullscreenKey, 1) == 1;
            set { PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0); Save(); }
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
            PlayerPrefs.DeleteKey(VolumeKey); PlayerPrefs.DeleteKey(FullscreenKey);
            PlayerPrefs.DeleteKey(VSyncKey); PlayerPrefs.DeleteKey(ReducedMotionKey);
            Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Apply()
        {
            AudioListener.volume = (float)Volume / VolumeSteps;
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            var mode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (!Application.isEditor && Screen.fullScreenMode != mode) Screen.fullScreenMode = mode;
        }
        private static void Save() { PlayerPrefs.Save(); Apply(); }
    }
}
