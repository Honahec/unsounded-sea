using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    // One persistent loading panel also keeps keyboard focus away from the outgoing HUD.
    public sealed class GameSceneLoader : MonoBehaviour
    {
        public static bool IsLoading { get; private set; }
        private UnityEngine.ThreadPriority previousPriority;
        private NewVoyageDeparture departure;

        public static void Load(string scene)
            => Begin(scene, null);

        public static void Depart(string scene, MainMenuSeaPresentation sea)
            => Begin(scene, sea);

        private static void Begin(string scene, MainMenuSeaPresentation sea)
        {
            if (IsLoading) return;
            if (!Application.CanStreamedLevelBeLoaded(scene))
                throw new InvalidOperationException("场景未加入构建列表：" + scene);
            IsLoading = true;
            var host = new GameObject("Scene loading");
            DontDestroyOnLoad(host);
            var loader = host.AddComponent<GameSceneLoader>();
            loader.previousPriority = Application.backgroundLoadingPriority;
            try
            {
                if (sea != null)
                {
                    loader.departure = host.AddComponent<NewVoyageDeparture>();
                    loader.departure.Configure(sea);
                }
                else loader.ShowLoadingPanel();
                loader.StartCoroutine(loader.LoadScene(scene));
            }
            catch
            {
                IsLoading = false;
                Destroy(host);
                throw;
            }
        }

        private void ShowLoadingPanel()
        {
            var source = FindAnyObjectByType<UIDocument>();
            if (source == null || source.panelSettings == null) return;
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = source.panelSettings;
            document.sortingOrder = 10000;
            var root = document.rootVisualElement;
            root.style.unityFontDefinition = source.rootVisualElement.style.unityFontDefinition;
            for (var i = 0; i < source.rootVisualElement.styleSheets.count; i++)
                root.styleSheets.Add(source.rootVisualElement.styleSheets[i]);
            var panel = new VisualElement { name = "scene-loading", focusable = true };
            panel.AddToClassList("scene-loading");
            var label = new Label("正在驶入海域…");
            label.AddToClassList("scene-loading-label");
            panel.Add(label);
            root.Add(panel);
            root.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(evt => evt.StopImmediatePropagation(), TrickleDown.TrickleDown);
            panel.Focus();
        }

        private IEnumerator LoadScene(string scene)
        {
            // Present feedback before beginning IO. Lower priority limits background integration work per frame.
            yield return null;
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.BelowNormal;
            AsyncOperation operation;
            try
            {
                operation = SceneManager.LoadSceneAsync(scene);
                if (departure != null) operation.allowSceneActivation = false;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Destroy(gameObject);
                yield break;
            }
            if (departure != null)
            {
                yield return departure.Leave();
                while (operation.progress < .9f) yield return null;
                operation.allowSceneActivation = true;
            }
            yield return operation;
            // Keep input locked through the new scene's Start callbacks.
            yield return null;
            if (departure != null) yield return departure.Arrive();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Application.backgroundLoadingPriority = previousPriority;
            IsLoading = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() => IsLoading = false;
    }
}
