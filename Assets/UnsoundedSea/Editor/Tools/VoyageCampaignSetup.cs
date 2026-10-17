using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Editor
{
    public static class VoyageCampaignSetup
    {
        private const string Root = "Assets/UnsoundedSea";
        private const string EnemyFolder = Root + "/Content/Enemies/Voyage";
        private const string EncounterFolder = Root + "/Content/Encounters/Voyage";
        private const string ScenarioFolder = Root + "/Content/Scenarios/Voyage";
        private const string CatalogPath = Root + "/Resources/VoyageEncounterCatalog.asset";
        private static readonly string[] Regions = { "fog", "bones", "inverted", "fleet", "abyss" };
        private static readonly string[,] EnemyNames =
        {
            { "雾中缆工", "失向水手", "白灯司号", "湿衣引航员" },
            { "潮骨拾荒者", "骨笛侍从", "盐髓司祭", "空舱敲击者" },
            { "倒悬测量员", "反写领航员", "镜潮记录员", "铅垂守望者" },
            { "无旗登船兵", "哑火炮手", "黑帆传令", "弃舰医官" },
            { "无星窥视者", "深渊听差", "熄灯司钟", "末潮见证者" }
        };
        private static readonly string[] RegionTitles = { "雾海", "潮骨海", "倒悬海", "无旗舰群", "无星深渊" };

        [MenuItem("Unsounded Sea/Content/Setup Voyage Campaign")]
        public static string Setup()
        {
            var stagePrefab = Load<GameObject>(Root + "/Art/OceanBattle/FogSeaStage.prefab");
            var stage = stagePrefab.GetComponent<BattleStage>();
            if (stage == null) throw new InvalidOperationException("FogSeaStage prefab is missing BattleStage.");
            if (stage.actorAnchors == null || stage.actorAnchors.Length < 2)
                throw new InvalidOperationException("FogSeaStage needs at least two actor anchors for voyage encounters.");
            var prayer = Load<GameObject>(Root + "/Art/Prefabs/Prayer.prefab");
            var echo = Load<GameObject>(Root + "/Art/Prefabs/Echo.prefab");
            var allTools = new[]
            {
                Load<ToolDefinition>(Root + "/Content/Tools/lantern.asset"),
                Load<ToolDefinition>(Root + "/Content/Tools/listener.asset"),
                Load<ToolDefinition>(Root + "/Content/Tools/shell.asset")
            };
            var tools = allTools.Take(2).ToArray();
            var deck = StarterDeck();
            var entries = new List<(string id, BattleScenario scenario)>();

            for (var regionIndex = 0; regionIndex < Regions.Length; regionIndex++)
            {
                var enemies = new EnemyDefinition[4];
                for (var variant = 0; variant < 4; variant++)
                    enemies[variant] = CreateEnemy(regionIndex, variant, variant % 2 == 0 ? prayer : echo);

                for (var variant = 0; variant < 4; variant++)
                {
                    var id = $"voyage-{Regions[regionIndex]}-{variant}";
                    var members = variant < 2 ? new[] { enemies[variant] } : new[] { enemies[variant], enemies[variant - 2] };
                    var encounter = Asset<EncounterDefinition>(EncounterFolder, id, value =>
                    {
                        value.id = id;
                        value.displayName = $"{RegionTitles[regionIndex]} / {EnemyNames[regionIndex, variant]}";
                        value.objective = ObjectiveKind.Defeat;
                        value.objectiveTarget = 1;
                        value.playerHealth = 60;
                        value.playerSanity = 30;
                        value.actionsPerTurn = 3;
                        value.drawPerTurn = 5;
                        value.deck = deck;
                        value.enemies = members;
                        value.equippedTools = tools;
                    });
                    var scenario = Asset<BattleScenario>(ScenarioFolder, id, value =>
                    {
                        value.encounter = encounter;
                        value.stagePrefab = stage;
                        value.actorOverrides = Array.Empty<GameObject>();
                        value.rules = null;
                        value.chapter = $"航程 / {RegionTitles[regionIndex]}";
                        value.location = "鸦号调查船 · 外海航线";
                        value.enemyHeading = encounter.displayName;
                        value.objectiveHint = "击退登船者。公开行动可直接应对；其余征兆可调查伤害类型、动作或声源。";
                        value.enemyStatus = string.Empty;
                        value.introduction = variant < 2 ? "单一目标登上甲板。先读出它承诺的行动。" : "两名目标从不同舷侧登船；它们的行动分别结算。";
                        value.objectiveHeading = "守住甲板，击退所有目标。";
                        value.taskLabel = string.Empty;
                        value.victoryText = "甲板恢复安静，航线得以继续。";
                        value.defeatText = "鸦号失去控制，航程在此中断。";
                    });
                    entries.Add((id, scenario));
                }
            }

            AddExisting(entries, "fog-visitors", Root + "/Content/Scenarios/FogVisitors.asset");
            AddExisting(entries, "m02", Root + "/Content/Scenarios/M02.asset");
            AddExisting(entries, "m03", Root + "/Content/Scenarios/M03.asset");
            AddExisting(entries, "m04", Root + "/Content/Scenarios/M04.asset");
            AddExisting(entries, "m05", Root + "/Content/Scenarios/M05.asset");
            if (entries.Select(x => x.id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                throw new InvalidOperationException("Voyage encounter catalog contains duplicate IDs.");

            var catalog = Asset<VoyageEncounterCatalog>(Root + "/Resources", "VoyageEncounterCatalog", _ => { });
            var serialized = new SerializedObject(catalog);
            var field = serialized.FindProperty("entries");
            field.arraySize = entries.Count;
            for (var i = 0; i < entries.Count; i++)
            {
                field.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue = entries[i].id;
                field.GetArrayElementAtIndex(i).FindPropertyRelative("scenario").objectReferenceValue = entries[i].scenario;
            }
            var toolField = serialized.FindProperty("tools");
            toolField.arraySize = allTools.Length;
            for (var i = 0; i < allTools.Length; i++)
                toolField.GetArrayElementAtIndex(i).objectReferenceValue = allTools[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            foreach (var entry in entries) entry.scenario.ValidatePresentation();
            return $"Voyage campaign synchronized: {entries.Count} encounter scenarios.";
        }

        private static EnemyDefinition CreateEnemy(int region, int variant, GameObject prefab)
        {
            var id = $"voyage-{Regions[region]}-enemy-{variant}";
            var tier = region / 2;
            return Asset<EnemyDefinition>(EnemyFolder, id, value =>
            {
                value.id = id;
                value.displayName = EnemyNames[region, variant];
                value.presentationPrefab = prefab;
                switch (variant)
                {
                    case 0:
                        value.health = 18 + region;
                        value.candidateDescription = $"越舷强袭：{7 + tier} 生命伤害（公开）";
                        value.intents = new[] { Intent("assault", "越舷强袭", "目标压低重心，武器朝向甲板。", DamageChannel.Health, 7 + tier, true) };
                        break;
                    case 1:
                        value.health = 20 + region;
                        value.candidateDescription = $"整备：无伤害 / 重击：{9 + tier} 生命伤害（均公开）";
                        value.intents = new[]
                        {
                            Intent("prepare", "收紧缆索", "目标停在原地，重新收紧手中的缆索。", DamageChannel.Health, 0, true),
                            Intent("heavy", "沉重挥击", "武器已经高举，落点正对甲板。", DamageChannel.Health, 9 + tier, true)
                        };
                        break;
                    case 2:
                        value.health = 12 + tier;
                        value.candidateDescription = $"同一征兆：{5 + tier} 生命伤害，或至多 {Math.Min(4, 2 + tier)} 理智冲击（可打断）";
                        value.intents = new[]
                        {
                            Intent("feint", "贴舷突刺", "贴住舷墙；手臂藏在湿衣下；传来一次短促敲击。", DamageChannel.Health, 5 + tier, false),
                            Intent("whisper", "舷外耳语", "贴住舷墙；手臂藏在湿衣下；传来一次短促敲击。", DamageChannel.Sanity, Math.Min(4, 2 + tier), false, true, true, "舷外海面")
                        };
                        break;
                    default:
                        value.health = 10 + tier;
                        value.candidateDescription = "短鸣：2 理智冲击（可打断）/ 观望：无伤害 / 推击：3 生命伤害（均公开）";
                        value.intents = new[]
                        {
                            Intent("short-call", "短促呼号", "目标举起号具，呼气动作清晰可见。", DamageChannel.Sanity, 2, true, true),
                            Intent("idle", "守住舷边", "目标没有进攻，只守住登船位置。", DamageChannel.Health, 0, true),
                            Intent("shove", "推击", "目标肩部前压，准备把人推离舷边。", DamageChannel.Health, 3, true)
                        };
                        break;
                }
            });
        }

        private static IntentData Intent(string id, string name, string clue, DamageChannel channel, int amount,
            bool isPublic, bool interruptible = false, bool visibleAction = true, string soundSource = "目标本体") =>
            new IntentData
            {
                id = id, displayName = name, clue = clue, channel = channel, amount = amount,
                isPublic = isPublic, interruptible = interruptible, visibleAction = visibleAction, soundSource = soundSource
            };

        private static CardDefinition[] StarterDeck()
        {
            var numbers = new[] { 1, 2, 12, 19, 20, 21, 22, 37, 39, 41, 55, 71 };
            return numbers.Select(number =>
            {
                var spec = CardCatalog.All.Single(card => CardCatalog.Number(card.Id) == number);
                return Load<CardDefinition>($"{Root}/Content/Cards/{spec.Id}.asset");
            }).ToArray();
        }

        private static void AddExisting(ICollection<(string id, BattleScenario scenario)> entries, string id, string path)
        {
            var scenario = Load<BattleScenario>(path);
            if (scenario.encounter == null || scenario.encounter.id != id)
                throw new InvalidOperationException($"{path} does not contain encounter '{id}'.");
            entries.Add((id, scenario));
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"Required voyage asset is missing: {path}");
            return asset;
        }

        private static T Asset<T>(string folder, string name, Action<T> initialize) where T : ScriptableObject
        {
            Folder(folder);
            var path = $"{folder}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            initialize(asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            Folder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
