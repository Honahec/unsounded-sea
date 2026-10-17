using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    // Lives with the loader so the same fog bank covers both scenes and their first rendered frames.
    public sealed class NewVoyageDeparture : MonoBehaviour
    {
        public static bool IsActive { get; private set; }
        public string Phase { get; private set; }
        public float VeilOpacity { get; private set; }
        private MainMenuSeaPresentation sea;
        private VisualElement menuRoot, arrivalRoot, veil, mist;
        private Texture2D noise;
        private VoyageSailingPresentation sailing;
        private float arrivalFog;
        private bool ownsArrivalFog;

        public void Configure(MainMenuSeaPresentation source)
        {
            sea = source; IsActive = true; Phase = "beacon";
            var original = FindObjectsByType<UIDocument>().First(d => d.gameObject.scene == source.gameObject.scene);
            menuRoot = original.rootVisualElement;
            var document = gameObject.AddComponent<UIDocument>(); document.panelSettings = original.panelSettings; document.sortingOrder = 10000;
            var root = document.rootVisualElement;
            for (int i = 0; i < menuRoot.styleSheets.count; i++) root.styleSheets.Add(menuRoot.styleSheets[i]);
            veil = new VisualElement { name = "new-voyage-veil", focusable = true }; veil.AddToClassList("departure-veil");
            mist = new VisualElement { pickingMode = PickingMode.Ignore }; mist.AddToClassList("departure-mist");
            noise = new Texture2D(128, 128, TextureFormat.RGBA32, false) { name = "Departure sea mist", wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color32[128 * 128];
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
            {
                var value = Mathf.PerlinNoise(x / 32f, y / 32f) * .7f + Mathf.PerlinNoise(x / 11f, y / 11f) * .3f;
                pixels[y * 128 + x] = new Color32(255, 255, 255, (byte)(value * 90));
            }
            noise.SetPixels32(pixels); noise.Apply(false, true); mist.style.backgroundImage = noise;
            veil.Add(mist); root.Add(veil);
            root.RegisterCallback<KeyDownEvent>(e => e.StopImmediatePropagation(), TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationSubmitEvent>(e => e.StopImmediatePropagation(), TrickleDown.TrickleDown);
            SetVeil(0); veil.Focus(); sea.BeginDeparture();
        }

        private void SetVeil(float value)
        {
            VeilOpacity = Mathf.Clamp01(value); veil.style.opacity = VeilOpacity;
        }

        public IEnumerator Leave()
        {
            float elapsed = 0; bool reduced = GameSettings.ReducedMotion;
            while (elapsed < (reduced ? .3f : 3.2f))
            {
                reduced |= GameSettings.ReducedMotion;
                elapsed += Mathf.Min(Time.unscaledDeltaTime, .1f);
                float t = Mathf.Clamp01(elapsed / (reduced ? .3f : 3.2f));
                sea.SetDepartureProgress(t);
                menuRoot.style.opacity = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .2f));
                SetVeil(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(reduced ? 0 : .45f, 1, t)));
                Phase = t < .2f ? "beacon" : t < .45f ? "mist" : "departure";
                if (!reduced) mist.style.translate = new Translate(Length.Percent(t * 6), Length.Percent(t * -3));
                yield return null;
            }
            SetVeil(1); Phase = "loading";
            yield return null; // Paint fully opaque fog before allowing scene activation.
        }

        public IEnumerator Arrive()
        {
            Phase = "arrival";
            var document = FindObjectsByType<UIDocument>().FirstOrDefault(d => d.gameObject.scene == SceneManager.GetActiveScene());
            arrivalRoot = document != null ? document.rootVisualElement : null;
            if (arrivalRoot != null) arrivalRoot.style.opacity = 0;
            arrivalFog = RenderSettings.fogDensity; ownsArrivalFog = true;
            sailing = FindAnyObjectByType<VoyageSailingPresentation>();
            bool reduced = GameSettings.ReducedMotion;
            if (!reduced) sailing?.BeginEntrance();
            float elapsed = 0;
            while (elapsed < (reduced ? .25f : 2.4f))
            {
                if (GameSettings.ReducedMotion && !reduced)
                {
                    SetVeil(1); sailing?.FinishEntrance(); reduced = true; elapsed = 0;
                    yield return null;
                }
                float dt = Mathf.Min(Time.unscaledDeltaTime, .1f); elapsed += dt;
                float t = Mathf.Clamp01(elapsed / (reduced ? .25f : 2.4f));
                if (!reduced) sailing?.AdvanceEntrance(Mathf.Clamp01(elapsed / 2), dt);
                SetVeil(1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.08f, .85f, t)));
                RenderSettings.fogDensity = Mathf.Lerp(.065f, arrivalFog, 1 - VeilOpacity);
                if (arrivalRoot != null) arrivalRoot.style.opacity = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.65f, 1, t));
                yield return null;
            }
            if (!reduced) sailing?.FinishEntrance();
            RenderSettings.fogDensity = arrivalFog; SetVeil(0); Phase = "complete";
            if (arrivalRoot != null) arrivalRoot.style.opacity = 1;
        }

        private void OnDestroy()
        {
            IsActive = false;
            if (sea != null) { sea.CancelDeparture(); if (menuRoot?.panel != null) menuRoot.style.opacity = 1; }
            if (sailing != null && sailing.IsPlaying) sailing.FinishEntrance();
            if (ownsArrivalFog) RenderSettings.fogDensity = arrivalFog;
            if (arrivalRoot?.panel != null) arrivalRoot.style.opacity = 1;
            if (noise != null) Destroy(noise);
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => IsActive = false;
    }
}
