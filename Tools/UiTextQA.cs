using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnsoundedSea.Runtime;

public static class UiTextQA
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static string Hash() => Convert.ToBase64String(SHA256.Create().ComputeHash(File.ReadAllBytes("Assets/Fonts/default_zh.asset")));
    static FontAsset Inspect(string sample)
    {
        var root = UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
        var font = root.style.unityFontDefinition.value.fontAsset;
        Check(font != null && !UnityEditor.AssetDatabase.Contains(font), "UI must own a transient font.");
        Check(font.TryAddCharacters(sample, out var missing), "Missing Chinese glyphs: " + missing);
        Check(root.Query<UnityEngine.UIElements.TextElement>().ToList().Count(x => !string.IsNullOrWhiteSpace(x.text) && x.resolvedStyle.fontSize > 0) > 5, "Text layout missing.");
#pragma warning disable CS0618 // Regression checks atlas ownership, not glyph metrics.
        Check(font.glyphTable.All(g => g.atlasIndex < font.atlasTextures.Length && font.atlasTextures[g.atlasIndex] != null), "A glyph references a missing atlas.");
#pragma warning restore CS0618
        return font;
    }
    public static async Task<object> Main()
    {
        checks = 0;
        Check(Application.isPlaying && SceneManager.GetActiveScene().name == "MainMenu", "Play did not start at the main menu.");
        string before = Hash();
        Directory.CreateDirectory("Logs/UiText");
        await Task.Delay(500);
        var font = Inspect("未响之海启航继续航程设置退出调试选关");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/UiText/menu.png"));
        await Task.Delay(150);
        VoyageRuntime.StartDebugEncounter("voyage-fog-4", "fog", false);
        await Task.Delay(1500);
        Check(UnityEngine.Object.FindAnyObjectByType<BattleDirector>()?.View != null, "Battle did not load.");
        Check(Inspect("生命理智行动结束回合左轮射击寻找掩体") == font, "Scene transition lost the runtime font.");
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/UiText/battle.png"));
        await Task.Delay(150);
        SceneManager.LoadScene("MainMenu");
        await Task.Delay(600);
        Inspect("启航设置继续航程");
        Check(before == Hash(), "Runtime text mutated the imported font asset.");
        return new { checks, menuEntry = true, chineseGlyphs = true, sceneTransition = true, sourceUnchanged = true };
    }
}
