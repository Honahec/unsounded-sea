using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;

// Run in MainMenu Play Mode via Unity CLI run_script, entry CardCollectionQA.Verify.
public static class CardCollectionQA
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static VisualElement Root => UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Submit(string name)
    {
        var button = Root.Q<Button>(name);
        Check(button != null && button.enabledInHierarchy, "Missing/disabled button: " + name);
        button.Focus();
        using (var evt = NavigationSubmitEvent.GetPooled()) { evt.target = button; button.SendEvent(evt); }
    }
    static bool Contains(Rect outer, Rect inner) => inner.width > 0 && inner.height > 0 &&
        inner.xMin >= outer.xMin - 1 && inner.yMin >= outer.yMin - 1 &&
        inner.xMax <= outer.xMax + 1 && inner.yMax <= outer.yMax + 1;
    static List<Button> CheckLayout()
    {
        var panel = Root.Q(className: "card-collection");
        var browser = panel.Q(className: "collection-browser");
        var footer = panel.Q(className: "collection-footer");
        Check(Contains(Root.worldBound, panel.worldBound), "Panel outside viewport");
        Check(Contains(panel.worldBound, footer.worldBound), "Footer outside panel");
        Check(!browser.worldBound.Overlaps(footer.worldBound), "Browser overlaps footer");
        var cards = panel.Query<Button>(className: "collection-card").ToList();
        Check(cards.Count > 0, "Empty page");
        foreach (var card in cards)
        {
            Check(Contains(browser.worldBound, card.worldBound), "Card outside browser: " + card.name);
            Check(!card.worldBound.Overlaps(footer.worldBound), "Card overlaps footer: " + card.name);
            var picked = Root.panel.Pick(card.worldBound.center);
            Check(picked == card || card.Contains(picked), "Card cannot be picked: " + card.name);
        }
        for (int i = 0; i < cards.Count; i++)
            for (int j = i + 1; j < cards.Count; j++)
                Check(!cards[i].worldBound.Overlaps(cards[j].worldBound), "Cards overlap");
        foreach (var button in footer.Query<Button>().ToList())
            Check(Contains(panel.worldBound, button.worldBound), "Footer button outside panel");
        return cards;
    }
    static void Resize(EditorWindow view, int width, int height)
    {
        var asm = typeof(Editor).Assembly;
        var sizesType = asm.GetType("UnityEditor.GameViewSizes");
        var sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance", All).GetValue(null);
        var group = sizesType.GetMethod("GetGroup", All).Invoke(sizes, new[] { sizesType.GetProperty("currentGroupType", All).GetValue(sizes) });
        int count = (int)group.GetType().GetMethod("GetBuiltinCount").Invoke(group, null) + (int)group.GetType().GetMethod("GetCustomCount").Invoke(group, null);
        int index = -1;
        for (int i = 0; i < count; i++)
        {
            var size = group.GetType().GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
            if ((int)size.GetType().GetProperty("width").GetValue(size) == width && (int)size.GetType().GetProperty("height").GetValue(size) == height) { index = i; break; }
        }
        if (index < 0)
        {
            var size = Activator.CreateInstance(asm.GetType("UnityEditor.GameViewSize"), All, null,
                new object[] { Enum.Parse(asm.GetType("UnityEditor.GameViewSizeType"), "FixedResolution"), width, height, "Collection QA " + width + "x" + height }, null);
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size }); index = count;
        }
        view.GetType().GetProperty("selectedSizeIndex", All).SetValue(view, index); view.Repaint();
    }
    public static async Task<object> Verify()
    {
        Check(EditorApplication.isPlaying && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu", "Run in MainMenu Play Mode");
        var view = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
        var originalSize = (int)view.GetType().GetProperty("selectedSizeIndex", All).GetValue(view);
        var save = Path.Combine(Application.persistentDataPath, "voyage.json");
        var before = File.Exists(save) ? File.ReadAllBytes(save) : null;
        var errors = new List<string>();
        Application.LogCallback capture = (message, stack, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); };
        Application.logMessageReceived += capture;
        var results = new List<object>();
        Directory.CreateDirectory("Logs/CardCollection");
        try
        {
            if (Root.Q("collection-close") == null) Submit("menu-collection");
            foreach (var size in new[] { new Vector2Int(3840,2160), new Vector2Int(1920,1080), new Vector2Int(2560,1080), new Vector2Int(1600,1200), new Vector2Int(1280,720), new Vector2Int(1919,1079) })
            {
                Resize(view, size.x, size.y); await Task.Delay(600);
                Check(Screen.width == size.x && Screen.height == size.y, "Game View size did not apply");
                CheckLayout(); // Resize while on the previous resolution's last page.
                while (Root.Q<Button>("collection-previous").enabledSelf) { Submit("collection-previous"); await Task.Delay(60); }
                var seen = new HashSet<string>(); int pages = 0;
                while (true)
                {
                    var cards = CheckLayout(); pages++;
                    foreach (var card in cards) Check(seen.Add(card.name), "Duplicate card across pages");
                    var chosen = cards.Last(); Submit(chosen.name); await Task.Delay(60);
                    var spec = CardCatalog.All.First(c => "collection-" + c.Id == chosen.name);
                    Check(Root.Q("collection-detail").Q<Label>("illustrated-description").text == spec.Description, "Wrong inspection details");
                    if (pages == 1)
                    {
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath($"Logs/CardCollection/{size.x}x{size.y}.png"));
                        await Task.Delay(100);
                    }
                    if (!Root.Q<Button>("collection-next").enabledSelf) break;
                    Submit("collection-next"); await Task.Delay(60);
                }
                Check(seen.SetEquals(CardCatalog.All.Select(c => "collection-" + c.Id)), "Catalog cards missing");
                results.Add(new { width = size.x, height = size.y, pages, cards = seen.Count });
            }
            Check(errors.Count == 0, string.Join("\n", errors));
            var after = File.Exists(save) ? File.ReadAllBytes(save) : null;
            Check(before == null ? after == null : after != null && before.SequenceEqual(after), "Voyage save changed");
            return new { resolutions = results, runtimeErrors = errors.Count, saveUnchanged = true };
        }
        finally
        {
            Application.logMessageReceived -= capture;
            view.GetType().GetProperty("selectedSizeIndex", All).SetValue(view, originalSize); view.Repaint();
        }
    }
}
