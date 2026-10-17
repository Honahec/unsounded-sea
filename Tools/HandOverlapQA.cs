using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Layout stress fixture only: never changes encounter assets or formal saves.
public static class HandOverlapQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static async Task<string> Run()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        SceneManager.LoadScene("OceanBattle"); await Task.Delay(600);
        var director = UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
        var hud = director.GetComponent<OceanBattleHud>();
        var root = director.GetComponent<UIDocument>().rootVisualElement;
        var source = director.scenario.encounter.Build();
        var report = "";
        foreach (var count in new[] { 8, 12, 20 })
        {
            var cards = Enumerable.Repeat("cover", count - 1).Append("revolver").Select(CardCatalog.Get);
            var definition = new BattleDefinition(source.Id, cards, source.Enemies, source.Tools, ObjectiveKind.Defeat, 4, 60, 30, 10, count);
            var session = new BattleSession(definition, 1729);
            typeof(BattleDirector).GetField("session", Private).SetValue(director, session);
            var revision = typeof(BattleDirector).GetField("<ViewRevision>k__BackingField", Private);
            revision.SetValue(director, director.ViewRevision + 1);
            typeof(OceanBattleHud).GetMethod("Render", Private).Invoke(hud, null);
            await Task.Delay(1000);
            var hand = root.Q<ScrollView>("hand");
            var buttons = director.View.Hand.Select(c => root.Q<Button>("card-" + c.InstanceId)).ToArray();
            var slots = buttons.Select(b => b.resolvedStyle.left).ToArray();
            var viewport = hand.contentViewport.worldBound;
            foreach (var button in buttons)
            {
                var face = button.Q(className: "sea-card");
                Check(face.worldBound.xMin >= viewport.xMin - 1 && face.worldBound.xMax <= viewport.xMax + 1, "Resting card clipped at " + count);
                // The Editor's parked mouse can hover a newly drawn card; clear it before keyboard checks.
                foreach (var other in buttons)
                    using (var leave = PointerLeaveEvent.GetPooled()) { leave.target = other; other.SendEvent(leave); }
                button.Focus(); await Task.Delay(350);
                Check(button.ClassListContains("inspecting"), "Focus did not lift " + button.name + " at " + count + "; focused=" + root.focusController.focusedElement + "; attached=" + (button.panel != null) + "; previews=" + string.Join(",", buttons.Where(b => b.ClassListContains("inspecting")).Select(b => b.name)));
                var r = face.worldBound;
                Check(r.xMin >= viewport.xMin - 1 && r.xMax <= viewport.xMax + 1 && r.yMin >= viewport.yMin - 1 && r.yMax <= root.worldBound.yMax + 1, "Lifted card clipped at " + count + ": " + r);
            }
            Check(buttons.Select(b => b.resolvedStyle.left).SequenceEqual(slots), "Stacking changed logical card slots.");
            Check(hand.scrollOffset.x < 1, "Hand still requires horizontal scrolling.");
            // Reflow while the first card is on top: hierarchy order must not become hand order.
            buttons[0].Focus(); hand.style.right = 350; await Task.Delay(250);
            Check(buttons.Zip(buttons.Skip(1), (a, b) => a.resolvedStyle.left < b.resolvedStyle.left).All(v => v), "Resize reordered inspected cards.");
            hand.style.right = 310; await Task.Delay(250);
            report += count + " cards fit; all focus previews fit, no scrolling, resize preserves order.\n";
        }
        var shot = director.View.Hand.Single(c => c.Definition.Id == "revolver");
        var shotButton = root.Q<Button>("card-" + shot.InstanceId);
        foreach (var button in root.Query<Button>(className: "card-anchor").ToList())
        {
            button.Blur();
            using (var leave = PointerLeaveEvent.GetPooled()) { leave.target = button; button.SendEvent(leave); }
        }
        await Task.Delay(200);
        var handArea = root.Q<ScrollView>("hand").contentViewport.worldBound;
        Vector2? grab = null;
        for (var y = root.worldBound.yMax - 100; y < root.worldBound.yMax - 5 && grab == null; y += 4)
            for (var x = handArea.xMin; x < handArea.xMax && grab == null; x += 3)
            {
                var point = new Vector2(x, y); var hit = root.panel.Pick(point);
                if (hit == shotButton || hit?.GetFirstAncestorOfType<Button>() == shotButton) grab = point;
            }
        Check(grab.HasValue, "Overlapped card has no exposed pointer target.");
        var target = root.Q<Button>("actor-" + director.View.Enemies[0].Id).worldBound.center;
        var actions = director.View.Actions; var hp = director.View.Enemies[0].Health;
        Pointer(EventType.MouseDown, grab.Value, shotButton);
        Pointer(EventType.MouseDrag, grab.Value + Vector2.down * 30, root);
        await Task.Delay(100); Pointer(EventType.MouseDrag, target, root); await Task.Delay(150);
        Check((bool)typeof(OceanBattleHud).GetField("dragging", Private).GetValue(hud), "Crowded hand drag did not start.");
        Pointer(EventType.MouseUp, target, root); await Task.Delay(200);
        Check(director.View.Actions == actions - shot.ActionCost && director.View.Enemies[0].Health < hp, "Crowded hand drag did not play exactly once.");
        report += "20-card exposed strip can be picked and dragged to play once.\n";
        Directory.CreateDirectory("Logs/BossImplementation");
        var captureType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(t => t != null);
        var bytes = (byte[])captureType.GetMethod("EncodeScreenToPng", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { Screen.width, Screen.height });
        File.WriteAllBytes("Logs/BossImplementation/hand-after-drag-" + Screen.width + "x" + Screen.height + ".png", bytes);
        File.WriteAllText("Logs/BossImplementation/hand-" + Screen.width + "x" + Screen.height + ".txt", report);
        return report;
    }
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static void Pointer(EventType type, Vector2 point, VisualElement target)
    {
        var raw = new Event { type = type, mousePosition = point, button = 0 };
        if (type == EventType.MouseDown) { using (var e = PointerDownEvent.GetPooled(raw)) { e.target = target; target.SendEvent(e); } }
        else if (type == EventType.MouseUp) { using (var e = PointerUpEvent.GetPooled(raw)) { e.target = target; target.SendEvent(e); } }
        else { using (var e = PointerMoveEvent.GetPooled(raw)) { e.target = target; target.SendEvent(e); } }
    }
}
