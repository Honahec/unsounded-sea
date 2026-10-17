using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Real BattleDirector commands, unchanged starter deck, no synthetic victory or resource writes.
public static class BossImplementationQA
{
    const string Dir = "Logs/BossImplementation";
    public static string RestoreEditorSettings()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        if (SessionState.GetBool("UiExperienceQA.sizeSaved", false))
        {
            var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            var view = EditorWindow.GetWindow(type);
            type.GetProperty("selectedSizeIndex", all).SetValue(view, SessionState.GetInt("UiExperienceQA.originalSize", 0));
            SessionState.EraseBool("UiExperienceQA.sizeSaved"); view.Repaint();
        }
        Application.runInBackground = SessionState.GetBool("BossQA.runInBackground", false);
        SessionState.EraseBool("BossQA.runInBackground");
        return "Restored original Game View selection and background setting.";
    }
    public static string Start()
    {
        Directory.CreateDirectory(Dir); File.WriteAllText(Dir + "/result.txt", "running");
        _ = Finish(); return "running; see Logs/BossImplementation/result.txt";
    }
    static async Task Finish()
    {
        try { await Run(); }
        catch (Exception error) { File.WriteAllText(Dir + "/result.txt", error.ToString()); }
    }
    static BattleDirector Director => UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    static async Task Ready()
    {
        for (var i = 0; i < 300; i++) { if (Director != null && Director.View != null && !Director.Busy) return; await Task.Delay(50); }
        throw new InvalidOperationException("Battle did not unlock.");
    }
    static async Task Submit(BattleCommand command)
    {
        await Ready(); var result = Director.Submit(command); Check(result.Success, result.Error); await Ready();
    }
    static async Task Capture(string name)
    {
        await Task.Delay(150); // Capture a composited frame after the HUD has painted its new state.
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.Pipeline.Editor.Commands.Capture.CaptureCommands")).First(t => t != null);
        var bytes = (byte[])type.GetMethod("EncodeScreenToPng", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { Screen.width, Screen.height });
        File.WriteAllBytes(Dir + "/" + name + ".png", bytes);
    }
    public static async Task<string> Review()
    {
        Directory.CreateDirectory(Dir);
        for (var number = 2; number <= 4; number++)
        {
            SceneManager.LoadScene("BossM0" + number); await Task.Delay(500); await Ready();
            Director.seed = 0; Director.Restart(); await Ready();
            await Capture("M0" + number + "-" + Screen.width + "x" + Screen.height);
            var root = Director.GetComponent<UIDocument>().rootVisualElement;
            foreach (var element in root.Query<VisualElement>().ToList().Where(e => e.ClassListContains("salt-coordinate") || e.ClassListContains("choir-name") || e.ClassListContains("relay-heading") || e.ClassListContains("lock-condition")))
            {
                var r = element.worldBound;
                Check(r.width > 0 && r.height > 0 && r.xMin >= 0 && r.yMin >= 0 && r.xMax <= root.worldBound.xMax && r.yMax <= root.worldBound.yMax, "Boss label outside viewport: " + element.name);
            }
        }
        return "M02-M04 HUD captures and label bounds passed at " + Screen.width + "x" + Screen.height;
    }
    public static async Task<string> Scope()
    {
        foreach (var scene in new[] { "OceanBattle", "BossM05", "SeaBossBattle" })
        {
            SceneManager.LoadScene(scene); await Task.Delay(500); await Ready();
            var root = Director.GetComponent<UIDocument>().rootVisualElement;
            Check(!new[] { "m02-battle", "m03-battle", "m04-battle", "boss-mechanics-battle" }.Any(root.ClassListContains), scene + " inherited boss-only styles.");
            Check(root.Q(className: "salt-coordinate") == null && root.Q(className: "choir-name") == null && root.Q(className: "relay-coordinate") == null, scene + " inherited boss-only decorations.");
        }
        return "Ordinary battle, M05 and Bell have no M02-M04 styles or decorations.";
    }
    public static async Task<string> Attacks()
    {
        Directory.CreateDirectory(Dir);
        var report = new System.Collections.Generic.List<string>();
        for (var number = 2; number <= 4; number++)
        {
            SceneManager.LoadScene("BossM0" + number); await Task.Delay(500); await Ready();
            Director.seed = 0; Director.Restart(); await Ready();
            var d = Director; var impacts = d.Stage.water.ImpactCount;
            var motion = d.Stage.Actors[0].GiantMotion;
            var contactSeen = false; var earlyPresentation = false; var presentations = 0;
            motion.VesselImpact += () => contactSeen = true;
            d.PresentedEnemyAction += action => { presentations++; if (!contactSeen) earlyPresentation = true; };
            var result = d.Submit(BattleCommand.EndTurn()); Check(result.Success, result.Error);
            Check(d.Stage.DelaysHudUntilImpact, "Boss HUD advanced before the hit.");
            Check(d.GetComponent<UIDocument>().rootVisualElement.Query<Label>().ToList().Any(l => l.text == "60 / 60"), "Health was shown before the physical hit landed.");
            var closest = float.PositiveInfinity;
            for (var frame = 0; frame < 40; frame++)
            {
                await Task.Delay(100);
                if (number == 4) closest = Mathf.Min(closest, Vector3.Distance(motion.PoleTip, motion.StrikeTarget));
                if (new[] { 0, 4, 8, 14, 20, 27, 34, 39 }.Contains(frame)) await Capture("M0" + number + "-attack-" + frame.ToString("D2"));
            }
            await Ready();
            var effects = motion.GetComponent<GiantBossEffects>();
            var waves = d.Stage.water.ImpactCount - impacts;
            Check(waves == 0, "Wrong physical effect for M0" + number + ": sea impacts=" + waves);
            Check(!earlyPresentation && presentations == 1, "Resources advanced before contact or more than once.");
            if (number == 2) Check(effects.SonicReleaseCount == 1, "Airborne sound front was not released.");
            if (number == 3) Check(effects.WindReleaseCount == 1, "Wind was not released.");
            if (number == 4) Check(effects.PoleContactCount == 1 && closest < 5, "Pole missed vessel: " + closest);
            report.Add("M0" + number + " waves=" + waves + " sonic=" + effects.SonicReleaseCount + " wind=" + effects.WindReleaseCount + " pole=" + effects.PoleContactCount + " nearestTip=" + closest);
        }
        var text = string.Join("\n", report); File.WriteAllText(Dir + "/attacks.txt", text); return text;
    }
    static string SafeCell(BattleView view)
    {
        var cells = view.EncounterTargets.ToArray();
        var layouts = (from a in Enumerable.Range(0, 14) from b in Enumerable.Range(a + 1, 15 - a)
            from c in Enumerable.Range(b + 1, 15 - b) select new[] { a, b, c }).Where(mines =>
            cells.Select((cell, index) => new { cell, index }).All(x =>
                (x.cell.State != MechanismState.Open || !mines.Contains(x.index)) &&
                (!x.cell.MeasuredValue.HasValue || mines.Count(m => m != x.index && Math.Abs(m / 4 - x.index / 4) <= 1 && Math.Abs(m % 4 - x.index % 4) <= 1) == x.cell.MeasuredValue.Value))).ToArray();
        Check(layouts.Length > 0, "Public salt constraints inconsistent.");
        return cells.Select((cell, index) => new { cell, index }).Where(x => x.cell.State == MechanismState.Unmeasured && layouts.All(m => !m.Contains(x.index)))
            .OrderByDescending(x => x.cell.IsSeal).Select(x => x.cell.Id).FirstOrDefault();
    }
    static BattleCommand TaskAction(BattleView view)
    {
        string id = null, target = null;
        if (view.BossKind == BossEncounterKind.Choirmaster && view.BossStage == 1 && view.Turn >= 2)
        {
            var pairs = Regex.Matches(view.BossStatus, @"(潮|骨|息)匣在(潮|骨|息)匣前").Cast<Match>().ToArray();
            var open = view.EncounterTargets.Where(t => t.State != MechanismState.Open).ToArray();
            target = open.First(t => !pairs.Any(p => t.Name == p.Groups[2].Value && open.Any(o => o.Name == p.Groups[1].Value))).Id;
            id = "E02-02";
        }
        if (view.BossKind == BossEncounterKind.Surveyor) { id = "E03-01"; target = SafeCell(view); }
        if (view.BossKind == BossEncounterKind.FlagBearer && view.BossStage < 3)
        {
            var wanted = view.BossStage == 1 ? new[] { "R1:positive", "R2:reverse" } : view.ObjectiveProgress == 0 ? new[] { "R1:off", "R2:reverse" } : view.ObjectiveProgress == 1 ? new[] { "R1:positive", "R2:reverse" } : new[] { "R1:positive", "R2:off" };
            target = wanted.FirstOrDefault(t => !view.EncounterTargets.Single(x => x.Id == t).IsCurrent);
            if (target != null) id = "E04-01";
            else if (view.BossStage == 2) { id = "E04-02"; target = "lock-" + new[] { "I", "II", "III" }[view.ObjectiveProgress]; }
        }
        var card = view.Hand.FirstOrDefault(c => c.Definition.Id == id && c.Enabled && c.ActionCost <= view.Actions);
        return card != null && card.Targets.Any(t => t.Id == target && t.Enabled) ? BattleCommand.Play(card.InstanceId, target) : null;
    }
    static BattleCommand Combat(BattleView view)
    {
        var physical = view.Enemies.Where(e => e.Health > 0).Sum(e => Regex.Match(e.Intent ?? "", @"(\d+) 点生命").Success ? int.Parse(Regex.Match(e.Intent, @"(\d+) 点生命").Groups[1].Value) : 0);
        var mental = view.Enemies.Where(e => e.Health > 0).Sum(e => Regex.Match(e.Intent ?? "", @"(\d+) 点理智").Success ? int.Parse(Regex.Match(e.Intent, @"(\d+) 点理智").Groups[1].Value) : 0);
        var target = view.Enemies.Where(e => e.Health > 0 && e.Targetable).OrderBy(e => e == view.Enemies[0] ? 1 : 0).FirstOrDefault();
        foreach (var id in new[] { "notes", "tear", "intuition", "cover", "steady", "bandage", "contradiction", "revolver", "c012" })
        {
            var card = view.Hand.FirstOrDefault(c => c.Definition.Id == id && c.Enabled && c.ActionCost <= view.Actions); if (card == null) continue;
            if (id == "cover" && physical <= view.Block || id == "steady" && mental <= view.Ward || id == "intuition" && physical <= view.Block && mental <= view.Ward || id == "bandage" && view.Health > 56) continue;
            if (id == "tear" && (target == null || !(target.Intent.Contains("召唤") || target.Intent.Contains("恢复")))) continue;
            if (card.Targets.Count == 0) return BattleCommand.Play(card.InstanceId);
            var option = target == null ? null : card.Targets.FirstOrDefault(t => t.Enabled && (t.Id == target.Id || t.Id == "enemy:" + target.Id));
            if (option != null) return BattleCommand.Play(card.InstanceId, option.Id);
        }
        return null;
    }
    public static async Task<string> Run()
    {
        Check(EditorApplication.isPlaying, "Play mode required."); Directory.CreateDirectory(Dir);
        var save = File.Exists(VoyageRuntime.SavePath) ? File.ReadAllBytes(VoyageRuntime.SavePath) : null;
        var results = new System.Collections.Generic.List<string>();
        for (var number = 2; number <= 4; number++)
        {
            SceneManager.LoadScene("BossM0" + number); await Task.Delay(500); await Ready();
            var d = Director; d.seed = 0; d.Restart(); await Ready();
            var frame = Time.frameCount; await Task.Delay(150); Check(Time.frameCount > frame, "Player loop frozen.");
            var motion = d.Stage.Actors[0].GiantMotion;
            d.SetPaused(true); var age = motion.Age; await Task.Delay(150); Check(motion.Age == age, "Pause did not freeze boss."); d.SetPaused(false);
            await Capture("M0" + number + "-start");
            if (number == 3)
            {
                var root = d.GetComponent<UIDocument>().rootVisualElement;
                var cell = d.View.EncounterTargets.First(t => t.State == MechanismState.Unmeasured);
                var before = d.CaptureReplay().commands.Count; var actions = d.View.Actions;
                var button = root.Q<Button>("boss-target-" + cell.Id); Check(button != null, "Salt target missing."); button.Focus();
                using (var e = KeyDownEvent.GetPooled(new Event { type = EventType.KeyDown, keyCode = KeyCode.F })) { e.target = button; button.SendEvent(e); }
                Check(root.Q<Button>("boss-target-" + cell.Id).Q(className: "salt-note") != null, "Salt note missing.");
                Check(d.View.Actions == actions && d.CaptureReplay().commands.Count == before, "Personal note changed game state.");
            }
            var stages = new System.Collections.Generic.HashSet<int> { d.View.BossStage };
            while (d.View.Phase == BattlePhase.PlayerTurn && d.View.Turn <= 22)
            {
                File.WriteAllText(Dir + "/progress.txt", "M0" + number + " turn=" + d.View.Turn + " stage=" + d.View.BossStage + " health=" + d.View.Health + " sanity=" + d.View.Sanity);
                for (var i = 0; i < 14 && d.View.Phase == BattlePhase.PlayerTurn; i++)
                {
                    var action = TaskAction(d.View) ?? Combat(d.View); if (action == null) break;
                    await Submit(action);
                    if (stages.Add(d.View.BossStage)) await Capture("M0" + number + "-stage-" + d.View.BossStage);
                }
                if (d.View.Phase == BattlePhase.PlayerTurn) await Submit(BattleCommand.EndTurn());
            }
            Check(d.View.Phase == BattlePhase.Victory, "M0" + number + " did not win: " + d.View.Phase + " turn=" + d.View.Turn);
            await Capture("M0" + number + "-victory");
            var restored = d.CaptureReplay().Restore(CardDeckPreparation.Apply(d.scenario.encounter.Build()));
            Check(restored.View.Log.SequenceEqual(d.View.Log), "Replay diverged.");
            results.Add("M0" + number + " victory turn=" + d.View.Turn + " health=" + d.View.Health + " sanity=" + d.View.Sanity);
        }
        Check(save == null ? !File.Exists(VoyageRuntime.SavePath) : File.ReadAllBytes(VoyageRuntime.SavePath).SequenceEqual(save), "Formal save changed.");
        foreach (var scene in new[] { "OceanBattle", "BossM05", "SeaBossBattle" })
        {
            SceneManager.LoadScene(scene); await Task.Delay(500); await Ready();
            var root = Director.GetComponent<UIDocument>().rootVisualElement;
            Check(!new[] { "m02-battle", "m03-battle", "m04-battle" }.Any(root.ClassListContains), scene + " inherited boss-only styles.");
            Check(root.Q(className: "salt-coordinate") == null && root.Q(className: "choir-name") == null && root.Q(className: "relay-coordinate") == null, scene + " inherited boss-only decorations.");
        }
        results.Add("Ordinary battle, M05 and Bell UI have no M02-M04 styles or decorations; formal save unchanged.");
        var report = string.Join("\n", results); File.WriteAllText(Dir + "/result.txt", report); return report;
    }
}
