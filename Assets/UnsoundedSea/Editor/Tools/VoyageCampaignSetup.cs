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
        private static readonly string[] RegionTitles = { "雾海", "潮骨海", "倒悬海", "无旗舰群", "无星深渊" };
        private static readonly EnemyPlan[,] Plans =
        {
            {
                Plan("FogRigger", "雾中缆工", 18, I("hook", "收绳甩钩", "湿绳骤然绷直，短钩正朝胸前。", 7, true)),
                Plan("LostSailor", "失向水手", 20, I("knife", "错身割刺", "身体朝左，持刀手却转向右舷。", 8, false)),
                Plan("WhiteLampSignaller", "白灯司号", 15, I("lamp", "白灯定向", "乳白灯罩转向你，短号尚未举起。", 4, true), S("horn", "短号惊鸣", "短号抵住唇边，灯罩朝向舷外。", 3, false, true, false, "铜号")),
                Plan("WetCoatPilot", "湿衣引航员", 17, I("sleeve", "袖下突袭", "双手仍藏在贴身湿袖里，重心突然前移。", 7, false, false, false)),
                Plan("BarrelThief", "漂桶窃贼", 16, I("barrel", "桶箍冲撞", "破桶压低，铁箍对准膝部。", 6, true), I("hide", "缩桶观望", "双手缩进桶沿，只拖动布袋。", 0, true))
            },
            {
                Plan("TideBoneScavenger", "潮骨拾荒者", 19, I("scrape", "刮骨钩", "短钩从骨筐侧面探出。", 8, true)),
                Plan("BoneFluteAttendant", "骨笛侍从", 18, S("flute", "骨笛低鸣", "颈侧骨管随吸气一起收紧。", 3, false, true, true, "骨笛"), I("needle", "笛端刺击", "骨笛横握，尖端朝向甲板。", 6, false)),
                Plan("SaltMarrowPriest", "盐髓司祭", 21, S("cup", "髓盏祷声", "双手托高髓盏，胸前骨板轻响。", 4, true, true, true, "髓盏")),
                Plan("EmptyHoldKnocker", "空舱敲击者", 17, S("knock", "空箱三敲", "短槌悬在胸前空箱上方。", 3, false, true, true, "胸前木箱"), I("door", "门板推击", "背后舱门板向前倾压。", 7, true)),
                Plan("BoneNetStitcher", "缝骨拖网客", 20, I("cast", "骨网投掷", "肩网展开，骨片碰撞声来自左侧。", 7, false, false, true, "肩背拖网"), I("stitch", "收针整网", "粗骨针回到网束，暂未朝向目标。", 0, true))
            },
            {
                Plan("InvertedSurveyHand", "倒悬测量员", 20, I("rule", "折尺量切", "折尺完全张开，长臂沿甲板刻度摆正。", 8, true)),
                Plan("ReverseNavigator", "反写领航员", 19, S("chart", "逆图误导", "空白海图被倒举，指北器持续刮擦纸面。", 3, false, true, false, "指北器"), I("edge", "卷图抽击", "卷边海图合拢成棍状。", 6, true)),
                Plan("MirrorTideScribe", "镜潮记录员", 18, S("mirror", "镜片凝视", "氧化镜片离开账本，正对你的眼睛。", 4, true, true, false, "面部镜片"), I("book", "账本砸击", "泡水账本被双臂高高抱起。", 7, true)),
                Plan("PlumbWatcher", "铅垂守望者", 23, I("plumb", "铅坠摆击", "胸前铅坠离开垂线，皮带已经绷紧。", 10, true), I("still", "校准垂线", "铅坠静止，双手只在调整肩带。", 0, true)),
                Plan("DisplacedWatchman", "错位值更人", 21, I("old-watch", "旧袖擒拿", "褪色旧袖抬起，另一侧仍垂落。", 7, false), I("new-watch", "新袖击打", "较新的袖口后撤蓄力，旧袖贴住胸前。", 9, false))
            },
            {
                Plan("UnflaggedBoarder", "无旗登船兵", 22, I("shield", "缺盾压进", "缺角舷盾封住身前，短刀压在盾后。", 8, true)),
                Plan("MisfireGunner", "哑火炮手", 20, I("load", "重新装填", "钝口铳朝下，火药筒已经打开。", 0, true), I("shot", "近舷轰击", "短铳抵肩，火门冒出细烟。", 11, true, true, true, "前装铳")),
                Plan("BlackSailMessenger", "黑帆传令", 18, S("flag", "黑旗传令", "短杆黑旗骤然横展，铜束具发出闷响。", 4, false, true, true, "信号旗"), I("staff", "旗杆横扫", "破旗收拢，短杆降到腰侧。", 7, true)),
                Plan("AbandonShipMedic", "弃舰医官", 19, I("case", "药箱重击", "锈药箱被单手提至肩高。", 7, true), S("bandage", "敷料低语", "敷料一圈圈松开，声音却来自箱内。", 3, false, true, false, "药箱")),
                Plan("RustChainEscort", "锈链押运者", 24, I("chain", "短链绞击", "两臂锈链同时收紧，链尾没有拖地。", 9, true), S("buckle", "铁扣震响", "腰间大铁扣反复撞击，双臂没有挥动。", 3, false, true, true, "腰间铁扣"))
            },
            {
                Plan("StarlessPeeker", "无星窥视者", 22, S("hollow", "空面注视", "凹陷面部转向你，双手仍垂在身侧。", 4, true, false, false)),
                Plan("AbyssRunner", "深渊听差", 19, S("tube", "传声筒回音", "空铜筒口朝向舱门，回声却来自舷外。", 4, false, true, true, "舷外海面"), I("rush", "低头奔撞", "传声筒贴回腰侧，窄肩压低。", 8, true)),
                Plan("LampOutBellkeeper", "熄灯司钟", 23, S("bell", "罩钟闷鸣", "头部铜钟罩轻颤，无焰提灯保持不动。", 4, true, true, false, "头部钟罩"), I("lantern", "无焰灯击", "提灯向后荡起，钟罩停止震动。", 8, true)),
                Plan("LastTideWitness", "末潮见证者", 20, S("witness", "无字见证", "腹前双手不动，下摆窄签逆风竖起。", 3, false, true, false, "衣摆布签"), I("reach", "长臂扼击", "收拢的双手分开，长袖直指胸前。", 7, true)),
                Plan("RailNameTaker", "舷外索名者", 25, S("ask", "舷外索名", "低垂衣领里传来问句，长臂仍搭在身前。", 4, false, true, false, "衣领内"), I("reach", "长臂攫取", "双臂离开身侧，长指贴近甲板。", 10, true))
            }
        };

        [MenuItem("Unsounded Sea/Content/Setup Voyage Campaign")]
        public static string Setup()
        {
            var stagePrefab = Load<GameObject>(Root + "/Art/OceanBattle/FogSeaStage.prefab");
            var stage = stagePrefab.GetComponent<BattleStage>();
            if (stage == null) throw new InvalidOperationException("FogSeaStage prefab is missing BattleStage.");
            if (stage.actorAnchors == null || stage.actorAnchors.Length < 2)
                throw new InvalidOperationException("FogSeaStage needs at least two actor anchors for voyage encounters.");
            DeckAnimationSetup.Apply();
            var allTools = new[]
            {
                Load<ToolDefinition>(Root + "/Content/Tools/lantern.asset"),
                Load<ToolDefinition>(Root + "/Content/Tools/listener.asset"),
                Load<ToolDefinition>(Root + "/Content/Tools/shell.asset")
            };
            var tools = allTools.Take(2).ToArray();
            var deck = StarterDeck();
            var entries = new List<(string id, BattleScenario scenario, string group, bool isBoss)>();

            for (var regionIndex = 0; regionIndex < Regions.Length; regionIndex++)
            {
                var enemies = new EnemyDefinition[5];
                for (var variant = 0; variant < 5; variant++)
                    enemies[variant] = CreateEnemy(regionIndex, variant);

                for (var variant = 0; variant < 5; variant++)
                {
                    var id = $"voyage-{Regions[regionIndex]}-{variant}";
                    var members = variant == 4 ? new[] { enemies[variant], enemies[2] } : new[] { enemies[variant] };
                    var encounter = Asset<EncounterDefinition>(EncounterFolder, id, value =>
                    {
                        value.id = id;
                        value.displayName = $"{RegionTitles[regionIndex]} / {Plans[regionIndex, variant].DisplayName}";
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
                        value.introduction = variant == 4
                            ? "两名目标从不同舷侧登船：近身压迫与精神威胁同时存在，先决定处置顺序。"
                            : "单一目标登上甲板。先读出它承诺的行动。";
                        value.objectiveHeading = "守住甲板，击退所有目标。";
                        value.taskLabel = string.Empty;
                        value.victoryText = "甲板恢复安静，航线得以继续。";
                        value.defeatText = "鸦号失去控制，航程在此中断。";
                    });
                    entries.Add((id, scenario, RegionTitles[regionIndex], false));
                }
            }

            AddExisting(entries, "fog-visitors", Root + "/Content/Scenarios/FogVisitors.asset", "雾海");
            AddExisting(entries, "m02", Root + "/Content/Scenarios/M02.asset", "潮骨海");
            AddExisting(entries, "m03", Root + "/Content/Scenarios/M03.asset", "倒悬海");
            AddExisting(entries, "m04", Root + "/Content/Scenarios/M04.asset", "无旗舰群");
            AddExisting(entries, "m05", Root + "/Content/Scenarios/M05.asset", "无星深渊");
            AddExisting(entries, "drowned-bell", Root + "/Content/Scenarios/DrownedBell.asset", "独立 Boss");
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
                field.GetArrayElementAtIndex(i).FindPropertyRelative("group").stringValue = entries[i].group;
                field.GetArrayElementAtIndex(i).FindPropertyRelative("isBoss").boolValue = entries[i].isBoss;
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

        private static EnemyDefinition CreateEnemy(int region, int variant)
        {
            var id = $"voyage-{Regions[region]}-enemy-{variant}";
            var plan = Plans[region, variant];
            var prefab = DeckAnimationSetup.LoadOrdinary(plan.ModelName);
            if (prefab == null) throw new InvalidOperationException($"Missing normalized static prefab for {plan.ModelName}.");
            return Asset<EnemyDefinition>(EnemyFolder, id, value =>
            {
                value.id = id;
                value.displayName = plan.DisplayName;
                value.health = plan.Health;
                value.candidateDescription = string.Join(" / ", plan.Intents.Select(intent =>
                    $"{intent.displayName}：{intent.amount} {(intent.channel == DamageChannel.Sanity ? "理智冲击" : "生命伤害")}{(intent.isPublic ? "（公开）" : string.Empty)}"));
                value.intents = plan.Intents;
                value.presentationPrefab = prefab;
            });
        }

        private sealed class EnemyPlan
        {
            public string ModelName;
            public string DisplayName;
            public int Health;
            public IntentData[] Intents;
        }

        private static EnemyPlan Plan(string model, string displayName, int health, params IntentData[] intents) =>
            new EnemyPlan { ModelName = model, DisplayName = displayName, Health = health, Intents = intents };

        private static IntentData I(string id, string name, string clue, int amount, bool isPublic,
            bool interruptible = false, bool visibleAction = true, string soundSource = "目标本体",
            DamageChannel channel = DamageChannel.Health) =>
            Intent(id, name, clue, channel, amount, isPublic, interruptible, visibleAction, soundSource);
        private static IntentData S(string id, string name, string clue, int amount, bool isPublic,
            bool interruptible = false, bool visibleAction = true, string soundSource = "目标本体") =>
            Intent(id, name, clue, DamageChannel.Sanity, amount, isPublic, interruptible, visibleAction, soundSource);


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

        private static void AddExisting(ICollection<(string id, BattleScenario scenario, string group, bool isBoss)> entries,
            string id, string path, string group)
        {
            var scenario = Load<BattleScenario>(path);
            if (scenario.encounter == null || scenario.encounter.id != id)
                throw new InvalidOperationException($"{path} does not contain encounter '{id}'.");
            entries.Add((id, scenario, group, true));
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
