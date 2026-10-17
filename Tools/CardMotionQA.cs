using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

// Run in Play Mode via Unity CLI. Sends UI Toolkit pointer events through the real HUD.
public static class CardMotionQA
{
    static BattleDirector Director => UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
    static OceanBattleHud Hud => Director.GetComponent<OceanBattleHud>();
    static VisualElement Root => Director.GetComponent<UIDocument>().rootVisualElement;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    static async Task Wait(Func<bool> condition)
    {
        for (int i = 0; i < 150; i++) { if (condition()) return; await Task.Delay(50); }
        throw new Exception("Timed out waiting for motion/UI.");
    }
    static string Save() => File.Exists(VoyageRuntime.SavePath) ? File.ReadAllText(VoyageRuntime.SavePath) : null;
    static CardView Card(string id) => Director.View.Hand.Single(c => c.Definition.Id == id);
    static void Pointer(EventType type, Vector2 point, VisualElement target, int button = 0)
    {
        var raw = new Event { type = type, mousePosition = point, button = button };
        if (type == EventType.MouseDown) { using (var e = PointerDownEvent.GetPooled(raw)) { e.target = target; target.SendEvent(e); } }
        else if (type == EventType.MouseUp) { using (var e = PointerUpEvent.GetPooled(raw)) { e.target = target; target.SendEvent(e); } }
        else { using (var e = PointerMoveEvent.GetPooled(raw)) { e.target = target; target.SendEvent(e); } }
    }
    static Vector2 Enemy(int index) => Root.Q<Button>("actor-" + Director.View.Enemies[index].Id).worldBound.center;
    static Vector2 Battlefield => Root.LocalToWorld(new Vector2(Root.resolvedStyle.width * .55f, 160));
    static bool Dragging => (bool)typeof(OceanBattleHud).GetField("dragging", Private).GetValue(Hud);
    static VisualElement DragGhost => (VisualElement)typeof(OceanBattleHud).GetField("dragGhost", Private).GetValue(Hud);
    static bool Aiming
    {
        get
        {
            var layer = typeof(OceanBattleHud).GetField("motion", Private).GetValue(Hud);
            return (bool)layer.GetType().GetField("Aiming").GetValue(layer);
        }
    }
    static void CheckNoDragInstructions()
    {
        var labels = Root.Query<Label>().ToList();
        Check(Root.Q("card-drag-hint") == null &&
              !labels.Any(x => x.text == "松手释放" || x.text.StartsWith("拖向", StringComparison.Ordinal) ||
                               x.text.StartsWith("守卫已锁定", StringComparison.Ordinal) || x.text.StartsWith("首个目标已锁定", StringComparison.Ordinal)),
            "Drag instruction text is visible.");
    }
    static async Task Lift(string cardId, Vector2 to)
    {
        await Task.Delay(60); // UI Toolkit lays out rebuilt controls on the next panel update.
        var card = Card(cardId); var button = Root.Q<Button>("card-" + card.InstanceId);
        var start = button.worldBound.center;
        Pointer(EventType.MouseDown, start, button);
        Pointer(EventType.MouseDrag, start + new Vector2(0, -30), Root);
        await Task.Delay(80);
        Pointer(EventType.MouseDrag, to, Root);
        await Task.Delay(130);
        bool targeted = card.Targets.Count > 0;
        Check(Dragging && DragGhost?.panel != null, "Drag never detached from hand: " + cardId + " held=" + typeof(OceanBattleHud).GetField("pointerHeld",Private).GetValue(Hud) + " paused=" + Director.Paused + " busy=" + Director.Busy);
        Check(Aiming == targeted, targeted ? "Targeted drag did not aim." : "Targetless drag entered aiming mode.");
        CheckNoDragInstructions();
    }
    static async Task Drop(string cardId, Vector2 to)
    {
        await Lift(cardId, to); Pointer(EventType.MouseUp, to, Root); await Task.Delay(80);
    }
    static async Task TargetlessThreshold(string cardId)
    {
        await Task.Delay(60);
        var card = Card(cardId);
        Check(card.Targets.Count == 0, cardId + " is not targetless.");
        var button = Root.Q<Button>("card-" + card.InstanceId);
        var start = button.worldBound.center;
        int actions = Director.View.Actions;
        int block = Director.View.Block;
        Pointer(EventType.MouseDown, start, button);
        Pointer(EventType.MouseDrag, start + Vector2.down * 79, Root);
        await Task.Delay(80);
        Check(Dragging && DragGhost?.panel != null && !Aiming, "Sub-threshold targetless drag state is wrong.");
        Pointer(EventType.MouseUp, start + Vector2.down * 79, Root);
        await Task.Delay(250);
        Check(Director.View.Actions == actions && Director.View.Block == block && !Dragging && DragGhost == null,
            "Targetless drag under 80px did not return.");
        CheckNoDragInstructions();
        button = Root.Q<Button>("card-" + card.InstanceId);
        start = button.worldBound.center;
        Pointer(EventType.MouseDown, start, button);
        Pointer(EventType.MouseDrag, start + Vector2.down * 80, Root);
        await Task.Delay(80);
        Check(Dragging && DragGhost?.panel != null && !Aiming, "Threshold targetless drag state is wrong.");
        Pointer(EventType.MouseUp, start + Vector2.down * 80, Root);
        await Task.Delay(80);
        Check(Director.View.Actions == actions - card.ActionCost, "Targetless drag at 80px did not pay the card cost exactly once.");
        CheckNoDragInstructions();
    }
    static async Task Prepare(params string[] cards)
    {
        await Wait(() => !Director.Busy);
        var source = Director.scenario.encounter.Build();
        var definition = new BattleDefinition(source.Id, cards.Select(CardCatalog.Get), source.Enemies, source.Tools,
            ObjectiveKind.Defeat, 4, 60, 30, 10, cards.Length);
        var session = new BattleSession(definition, 1729);
        typeof(BattleDirector).GetField("session", Private).SetValue(Director, session);
        var revision = typeof(BattleDirector).GetField("<ViewRevision>k__BackingField", Private);
        revision.SetValue(Director, Director.ViewRevision + 1);
        typeof(OceanBattleHud).GetMethod("Render", Private).Invoke(Hud, null);
        await Task.Delay(350);
    }
    static int Effects()
    {
        var motion = typeof(OceanBattleHud).GetField("motion", Private).GetValue(Hud);
        return (int)motion.GetType().GetProperty("ActiveCount").GetValue(motion);
    }
    static float MotionAge()
    {
        var motion = typeof(OceanBattleHud).GetField("motion", Private).GetValue(Hud);
        float age = 0;
        foreach (var field in new[] { "tweens", "pulses" })
            foreach (var item in (System.Collections.IEnumerable)motion.GetType().GetField(field, Private).GetValue(motion))
                age += (float)item.GetType().GetField("age").GetValue(item);
        return age;
    }
    public static async Task<object> Main()
    {
        assertions = 0;
        Check(Application.isPlaying, "Enter Play Mode first.");
        string saved = Save(); bool reduced = GameSettings.ReducedMotion;
        try
        {
            GameSettings.ReducedMotion = false;
            VoyageRuntime.StartDebugEncounter("voyage-fog-4", "fog", false);
            await Task.Delay(400); await Wait(() => Director?.View != null && Director.View.Enemies.Count == 2);
            await Prepare("revolver", "cover", "steady", "light", "c009", "c045");
            int actions = Director.View.Actions;
            var b = Root.Q<Button>("card-" + Card("revolver").InstanceId); var click = b.worldBound.center;
            Pointer(EventType.MouseDown, click, b); Pointer(EventType.MouseUp, click, Root);
            Check(Director.View.Actions == actions && Card("revolver") != null, "Click played a card.");
            await Drop("revolver", Root.LocalToWorld(new Vector2(500, Root.resolvedStyle.height - 10)));
            Check(Director.View.Actions == actions, "Invalid drop charged resources.");
            await Lift("revolver", Enemy(0));
            using (var e = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None)) { e.target = Root; Root.SendEvent(e); }
            Check(Director.View.Actions == actions && !Dragging && DragGhost == null && !Aiming, "Escape failed to cancel drag.");
            CheckNoDragInstructions();
            await Task.Delay(80);
            await Lift("revolver", Enemy(0)); Pointer(EventType.MouseDown, Enemy(0), Root, 1);
            Check(Director.View.Actions == actions && !Dragging && DragGhost == null && !Aiming, "Right click failed to cancel.");
            CheckNoDragInstructions();
            await Task.Delay(80);
            int hp = Director.View.Enemies[0].Health;
            await Drop("revolver", Enemy(0));
            Check(Director.View.Actions == actions - 1 && Director.View.Enemies[0].Health < hp, "Attack drop did not resolve exactly once: actions=" + Director.View.Actions + " hp=" + Director.View.Enemies[0].Health + " before=" + hp + " notice=" + Director.Notice);
            Check(Effects() > 0, "Attack feedback missing.");
            Check(Root.Q(className:"health").Q(className:"vital-fill").resolvedStyle.width > 0, "Health bar disappeared during a redraw.");
            Pointer(EventType.MouseUp, Enemy(0), Root); Check(Director.View.Actions == actions - 1, "Repeated release charged twice.");
            await Wait(() => !Director.Busy);
            await TargetlessThreshold("cover");
            Check(Effects() > 0, "Defence card feedback missing.");
            await Wait(() => !Director.Busy);
            await Drop("steady", Battlefield); Check(Director.View.Ward > 0, "Ward card did not resolve.");
            await Wait(() => !Director.Busy);
            await Drop("light", Enemy(0)); Check(Director.View.Enemies[0].IntentConfirmed, "Investigation drop failed.");
            await Wait(() => !Director.Busy);
            actions = Director.View.Actions;
            await Drop("c009", Enemy(0)); Check(Director.View.Actions == actions, "First target charged before confirmation.");
            await Drop("c009", Enemy(1)); Check(Director.View.Actions == actions - 1, "Two-stage drag did not charge once.");
            await Wait(() => !Director.Busy);
            await Prepare("c045", "cover", "revolver");
            await Lift("c045", Battlefield);
            var option = Root.Q<Button>(className: "card-target-choice"); Check(option != null, "Hand destinations missing.");
            var dest = option.worldBound.center; Pointer(EventType.MouseDrag, dest, Root); Pointer(EventType.MouseUp, dest, Root);
            Check(Director.View.Actions == 9, "Hand target drag did not resolve.");
            await Wait(() => !Director.Busy);
            await Lift("cover", Battlefield); Director.SetPaused(true); await Task.Delay(100);
            Check(!Dragging && DragGhost == null && !Aiming && Director.View.Actions == 9, "Pause did not safely cancel drag.");
            CheckNoDragInstructions();
            int frozen = Effects(); float frozenAge = MotionAge(); await Task.Delay(250); Check(Effects() == frozen && MotionAge() == frozenAge, "Combat effects advanced while paused.");
            Director.SetPaused(false); await Task.Delay(300);
            GameSettings.ReducedMotion = true;
            await Drop("cover", Battlefield); Check(Director.View.Block > 0, "Reduced-motion drop failed.");
            await Wait(() => !Director.Busy); await Task.Delay(1200);
            Check(Effects() == 0, "Transient animation objects leaked.");
            Check(saved == Save(), "QA changed the formal voyage save.");
            return new { assertions, pointerEvents = true, targetlessThreshold = 80, targetlessNoAim = true, targetedAim = true, noDragInstructions = true, attack = true, defence = true, investigation = true, pair = true, handTarget = true, cancel = true, pause = true, reducedMotion = true, saveUnchanged = true };
        }
        finally { GameSettings.ReducedMotion = reduced; }
    }
    static async Task StartBoss(string id, BossEncounterKind kind)
    {
        VoyageRuntime.StartDebugEncounter(id, id == "m04" ? "fleet" : id == "m03" ? "inverted" : "abyss", true);
        await Task.Delay(450); await Wait(() => Director?.View?.BossKind == kind && !Director.Busy); await Task.Delay(300);
    }
    static async Task BossDrop(string cardId)
    {
        int actions = Director.View.Actions;
        int cost = Card(cardId).ActionCost;
        await Lift(cardId, Battlefield);
        var option = Root.Query<Button>(className: "boss-target").ToList().FirstOrDefault(x => x.enabledInHierarchy);
        Check(option != null, "No enabled mechanism destination for " + cardId);
        var at = option.worldBound.center; Pointer(EventType.MouseDrag, at, Root); await Task.Delay(70); Pointer(EventType.MouseUp, at, Root);
        Check(Director.View.Actions == actions - cost, "Mechanism drag failed: " + cardId + " / " + Director.Notice);
        await Wait(() => !Director.Busy); await Task.Delay(80);
    }
    public static async Task<object> Bosses()
    {
        assertions = 0; var saved = Save();
        await StartBoss("m04", BossEncounterKind.FlagBearer);
        await BossDrop("E04-01");
        await StartBoss("m03", BossEncounterKind.Surveyor);
        await BossDrop("E03-01");
        await StartBoss("m05", BossEncounterKind.Watcher);
        await TargetlessThreshold("E05-01");
        await Wait(() => !Director.Busy);
        Check(Director.Submit(BattleCommand.EndTurn()).Success, "M05 turn failed."); await Wait(() => !Director.Busy);
        Check(Director.View.Enemies.Skip(1).Count(e => e.Health > 0) == 1, "Summoning must activate exactly one guard.");
        Check(Card("E05-01").Targets.Count == 0, "Random summoning must not expose a target picker.");
        Check(Director.View.ObjectiveProgress == 0, "Summoning alone cannot advance the defeat objective.");
        Check(saved == Save(), "Boss QA changed formal save.");
        return new { assertions, m03SaltGrid = true, m04Relay = true, m05CardSummon = true, saveUnchanged = true };
    }
    public static async Task<object> Preview()
    {
        VoyageRuntime.StartDebugEncounter("voyage-fog-4", "fog", false);
        await Task.Delay(400); await Wait(() => Director?.View != null && Director.View.Enemies.Count == 2);
        await Prepare("revolver", "cover", "steady", "light", "c009");
        var session = typeof(BattleDirector).GetField("session", Private).GetValue(Director);
        typeof(BattleSession).GetField("actions", Private).SetValue(session, 3);
        typeof(OceanBattleHud).GetMethod("Render", Private).Invoke(Hud, null); await Task.Delay(100);
        Directory.CreateDirectory("Logs/CardMotion");
        await Lift("revolver", Enemy(0));
        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/CardMotion/drag.png")); await Task.Delay(100);
        Pointer(EventType.MouseUp, Enemy(0), Root);
        await Task.Delay(180); ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/CardMotion/strike.png"));
        await Wait(() => !Director.Busy);
        await Lift("cover", Battlefield); Pointer(EventType.MouseUp, Battlefield, Root);
        await Task.Delay(100); ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/CardMotion/guard.png"));
        await Task.Delay(900); return new { captures = "Logs/CardMotion", frame = Time.frameCount };
    }
    static void Key(KeyCode key)
    {
        using (var e = KeyDownEvent.GetPooled('\0', key, EventModifiers.None)) { e.target = Root; Root.SendEvent(e); }
    }
    public static async Task<object> UiFlow()
    {
        assertions = 0; string saved = Save();
        VoyageRuntime.StartDebugEncounter("voyage-fog-0", "fog", false);
        await Task.Delay(450); await Wait(() => Director?.View != null && Director.View.Enemies.Count == 1);
        await Prepare("revolver", "cover", "light");
        Key(KeyCode.B); await Task.Delay(80);
        Check(Director.Paused && Root.Q(className: "battle-action-book-sheet") != null, "Action book did not open.");
        Key(KeyCode.Escape); await Task.Delay(250);
        Check(!Director.Paused && Root.Q(className:"battle-action-book-overlay") == null, "Action book exit did not clean up.");
        Key(KeyCode.Escape); await Task.Delay(80); Check(Director.Paused, "Pause failed.");
        Key(KeyCode.Escape); await Task.Delay(250); Check(!Director.Paused && Root.Q(className:"ocean-modal") == null, "Pause exit left an overlay.");
        // Single-target encounters still require the pointer drop; keyboard submit remains accessible.
        var card = Card("revolver"); var b = Root.Q<Button>("card-" + card.InstanceId); int actions = Director.View.Actions;
        Pointer(EventType.MouseDown, b.worldBound.center, b); Pointer(EventType.MouseUp, b.worldBound.center, Root);
        Check(Director.View.Actions == actions, "Single-enemy click auto-played.");
        await Drop("revolver", Enemy(0)); Check(Director.View.Actions == actions - 1, "Single-enemy drop failed.");
        await Wait(() => !Director.Busy);
        await Wait(() => Root.Q<Button>("card-" + Card("cover").InstanceId)?.enabledInHierarchy == true);
        b = Root.Q<Button>("card-" + Card("cover").InstanceId);
        using (var e = NavigationSubmitEvent.GetPooled()) { e.target = b; b.SendEvent(e); }
        Check(Director.View.Block > 0, "Keyboard play regressed.");
        await Wait(() => !Director.Busy); int frame = Time.frameCount;
        Check(Director.Submit(BattleCommand.EndTurn()).Success, "End turn failed.");
        await Wait(() => !Director.Busy); await Task.Delay(1500);
        Check(Time.frameCount > frame + 15, "Player loop stopped advancing.");
        Check(Effects() == 0 && Root.Query(className:"card-entering").ToList().Count == 0, "Turn animations failed to settle.");
        Check(!Root.Query(className:"ui-departing").ToList().Any(), "An exit overlay leaked.");
        foreach (var bar in Root.Query(className:"vital-fill").ToList()) Check(bar.resolvedStyle.width > 0, "Vital bar lost its layout.");
        Check(saved == Save(), "UI QA changed formal save.");
        return new { assertions, resolution = new[] { Screen.width, Screen.height }, modalExit = true, keyboard = true, singleEnemy = true, turnSequence = true, saveUnchanged = true };
    }
    public static async Task<object> Menus()
    {
        assertions = 0; var saved = Save();
        VisualElement Page() => UnityEngine.Object.FindAnyObjectByType<UIDocument>().rootVisualElement;
        void Click(string id)
        {
            var button = Page().Q<Button>(id); Check(button != null && button.enabledInHierarchy, "Missing menu button: " + id);
            using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); }
        }
        VoyageRuntime.StartDebugNode("fog", RunNodeKind.Supply); await Task.Delay(500);
        await Wait(() => UnityEngine.Object.FindAnyObjectByType<VoyageHud>() != null);
        Click("voyage-support-manifest"); await Task.Delay(250);
        Check(Page().Q(className:"action-book") != null, "Support sheet missing.");
        for (int i = 0; i < 5; i++) { Click("support-tab-" + i); await Task.Delay(80); }
        Click("support-manifest-close"); await Task.Delay(300);
        Check(!Page().Query(className:"ui-departing").ToList().Any(), "Support exit leaked.");
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu"); await Task.Delay(500);
        await Wait(() => UnityEngine.Object.FindAnyObjectByType<MainMenu>() != null);
        Click("menu-collection"); await Task.Delay(250); Click("collection-close"); await Task.Delay(300);
        Check(Page().Q(className:"card-collection") == null, "Collection exit leaked.");
        Click("menu-settings"); await Task.Delay(250); Click("settings-back"); await Task.Delay(300);
        Check(Page().Q("settings") == null, "Settings exit leaked.");
        Check(saved == Save(), "Menu QA changed formal save.");
        return new { assertions, supportTabs = 5, collection = true, settings = true, saveUnchanged = true };
    }
}
