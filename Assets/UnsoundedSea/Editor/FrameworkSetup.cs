using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Editor
{
    public static class FrameworkSetup
    {
        private const string Root = "Assets/UnsoundedSea";
        public const string ScenePath = Root + "/Scenes/Battle.unity";
        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var split = path.LastIndexOf('/'); Folder(path.Substring(0, split));
            AssetDatabase.CreateFolder(path.Substring(0, split), path.Substring(split + 1));
        }
        private static T Asset<T>(string folder, string name, Action<T> initialize) where T : ScriptableObject
        {
            Folder(folder); var path = folder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path); if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<T>(); initialize(asset); AssetDatabase.CreateAsset(asset, path); return asset;
        }
        private static CardDefinition Card(string id, string title, string description, int cost, string effect, int amount, string second = null, int secondAmount = 0, int sanityCost = 0, bool exhaust = false)
        {
            return Asset<CardDefinition>(Root + "/Content/Cards", id, card =>
            {
                card.id = id; card.displayName = title; card.description = description; card.actionCost = cost;
                card.sanityCost = sanityCost; card.exhaust = exhaust;
                card.effects = second == null ? new[] { new CardEffectData { effectId = effect, amount = amount } } :
                    new[] { new CardEffectData { effectId = effect, amount = amount }, new CardEffectData { effectId = second, amount = secondAmount } };
            });
        }

        [MenuItem("Unsounded Sea/Create or Open Battle Scene")]
        public static string CreateOrOpen()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before setup.");
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits before opening the battle scene.");
            var shot = Card("revolver", "左轮射击", "对目标造成 6 伤害。", 1, "damage", 6);
            var cover = Card("cover", "寻找掩体", "获得 10 格挡。", 1, "block", 10);
            var calm = Card("steady", "稳住心神", "获得 6 理智防护；不抵消主动支付。", 1, "ward", 6);
            var notes = Card("notes", "观察笔记", "记录当前征兆，抽 1 张牌。", 0, "observe", 0, "draw", 1);
            var reload = Card("reload", "快速装填", "抽 2 张牌。", 1, "draw", 2);
            var light = Card("light", "临时照明", "揭示可见动作；若已知意图则抽 1 张。", 1, "investigate", 0);
            var contradiction = Card("contradiction", "记录矛盾", "意图已确认：11 伤害；\n否则造成 5 伤害。", 1, "conditional-damage", 11);
            var intuition = Card("intuition", "直觉防护", "获得 7 格挡。\n获得 3 理智防护。", 1, "block", 7, "ward", 3);
            var interrupt = Card("tear", "撕毁符纸", "造成 4 伤害，打断可中断施术。", 1, "damage", 4, "interrupt", 0);
            var bandage = Card("bandage", "应急包扎", "恢复 4 生命；使用后消耗。", 1, "heal", 4, exhaust: true);
            var lantern = Asset<ToolDefinition>(Root + "/Content/Tools", "lantern", t => { t.id = "lantern"; t.displayName = "防风提灯"; t.question = InvestigationKind.VisibleAction; });
            var listener = Asset<ToolDefinition>(Root + "/Content/Tools", "listener", t => { t.id = "listener"; t.displayName = "定向听筒"; t.question = InvestigationKind.SoundSource; });
            Asset<ToolDefinition>(Root + "/Content/Tools", "shell", t => { t.id = "shell"; t.displayName = "听潮贝壳"; t.question = InvestigationKind.DamageChannel; t.sanityCost = 3; });
            var ordinary = Asset<EnemyDefinition>(Root + "/Content/Enemies", "smuggler", e =>
            {
                e.id = "smuggler"; e.displayName = "持刀走私者"; e.health = 24; e.candidateDescription = "挥刃 · 8 生命伤害";
                e.intents = new[] { new IntentData { id = "slash", displayName = "挥刃", clue = "持刀前倾，准备突刺。", amount = 8, isPublic = true } };
            });
            var masked = Asset<EnemyDefinition>(Root + "/Content/Enemies", "prayer", e =>
            {
                e.id = "prayer"; e.displayName = "蒙面祷告者"; e.health = 30; e.candidateDescription = "挥刃：10 生命伤害 / 呼唤：6 理智冲击";
                e.intents = new[]
                {
                    new IntentData { id = "slash", displayName = "挥刃", clue = "垂首；袖内金属声；断续低语。", amount = 10 },
                    new IntentData { id = "call", displayName = "呼唤", clue = "垂首；袖内金属声；断续低语。", amount = 6, channel = DamageChannel.Sanity, soundSource = "袖内铜铃", interruptible = true }
                };
            });
            var echo = Asset<EnemyDefinition>(Root + "/Content/Enemies", "echo", e =>
            {
                e.id = "echo"; e.displayName = "灯塔下的回声"; e.health = 999; e.candidateDescription = "逼近：8 生命伤害 / 铃鸣：4 理智冲击";
                e.intents = new[]
                {
                    new IntentData { id = "approach", displayName = "沉重脚步", clue = "潜水服缓慢靠近；脚步声清晰。", amount = 8, isPublic = true },
                    new IntentData { id = "bells", displayName = "无风铃鸣", clue = "声源异常；潜水服静止不动。", amount = 4, channel = DamageChannel.Sanity, visibleAction = false, soundSource = "墙上的铜铃" }
                };
            });
            var starter = new[] { shot, shot, shot, shot, cover, cover, cover, cover, calm, notes };
            var equipped = new[] { lantern, listener };
            EncounterDefinition Encounter(string id, string title, EnemyDefinition enemy, ObjectiveKind objective, CardDefinition[] deck) =>
                Asset<EncounterDefinition>(Root + "/Content/Encounters", id, e =>
                { e.id = id; e.displayName = title; e.objective = objective; e.deck = deck; e.enemies = new[] { enemy }; e.equippedTools = equipped; });
            var encounters = new[]
            {
                Encounter("ordinary", "01 / 公开意图", ordinary, ObjectiveKind.Defeat, starter),
                Encounter("unknown", "02 / 有限调查", masked, ObjectiveKind.Defeat, starter),
                Encounter("seal", "03 / 封门目标", echo, ObjectiveKind.Seal, starter),
                Encounter("effects", "04 / 卡牌实验", masked, ObjectiveKind.Defeat, new[] { shot, cover, calm, notes, reload, light, contradiction, intuition, interrupt, bandage })
            };
            foreach (var encounter in encounters) new BattleSession(encounter.Build(), 1);
            Asset<PanelSettings>(Root + "/Settings", "BattlePanel", p =>
            { p.scaleMode = PanelScaleMode.ScaleWithScreenSize; p.referenceResolution = new Vector2Int(1440, 900); p.match = 0.5f; });
            AssetDatabase.SaveAssets();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Folder(Root + "/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cameraObject = new GameObject("Fixed Battle Camera");
                var camera = cameraObject.AddComponent<Camera>(); camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.11f, 0.18f, 0.21f);
                cameraObject.transform.position = new Vector3(0, 3, -8); cameraObject.transform.LookAt(Vector3.zero);
                var host = new GameObject("Battle Composition Root");
                // NewScene can unload assets; resolve persistent references after switching scenes.
                host.AddComponent<UIDocument>().panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/Settings/BattlePanel.asset");
                var hud = host.AddComponent<BattleHud>();
                var serialized = new SerializedObject(hud); var field = serialized.FindProperty("encounters");
                field.arraySize = encounters.Length;
                var ids = new[] { "ordinary", "unknown", "seal", "effects" };
                for (var i = 0; i < ids.Length; i++) field.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<EncounterDefinition>(Root + "/Content/Encounters/" + ids[i] + ".asset");
                serialized.FindProperty("interfaceFont").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/Fonts/default_zh.asset");
                serialized.FindProperty("interfaceStyle").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<StyleSheet>(Root + "/Runtime/Presentation/BattleHud.uss");
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            else EditorSceneManager.OpenScene(ScenePath);
            // Keep existing build scenes; insert the explicit framework entry point once.
            if (!EditorBuildSettings.scenes.Any(x => x.path == ScenePath))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(EditorBuildSettings.scenes).ToArray();
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        [MenuItem("Unsounded Sea/Validate All Encounters")]
        public static void ValidateContent()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:EncounterDefinition", new[] { Root }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<EncounterDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                new BattleSession(asset.Build(), 1);
            }
            Debug.Log("Unsounded Sea: all encounter definitions are valid.");
        }
    }
}
