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
        private static readonly string[] RegionTitles = { "灰雾航道", "尸骸之海", "倒悬潮庭", "无灯裂海", "无星深渊" };
        private static readonly EnemyPlan[,] Plans =
        {
            {
                Plan("FogRigger", "雾中缆工", 26, I("hook", "收绳甩钩", "湿绳骤然绷直，短钩正朝胸前。", 8, true), I("knot", "重结固缆", "短钩垂下，双手正把湿绳勒回桩头。", 0, false)),
                Plan("LostSailor", "失向水手", 28, I("knife", "错身割刺", "身体朝左，持刀手却转向右舷。", 9, false), I("search", "辨认舷向", "刀尖压低，空手反复摸索不存在的罗盘。", 0, false)),
                Plan("WhiteLampSignaller", "白灯司号", 22, I("lamp", "白灯定向", "乳白灯罩转向你，短号尚未举起。", 5, true), S("horn", "短号惊鸣", "短号抵住唇边，灯罩朝向舷外。", 4, false, true, true, "铜号")),
                Plan("WetCoatPilot", "湿衣引航员", 24, I("sleeve", "袖下突袭", "双手仍藏在贴身湿袖里，重心突然前移。", 8, false, false, false), S("breath", "溺息低语", "湿衣停在原地，袖口却传出溺水者的喘息。", 4, false, true, true, "湿袖")),
                Plan("BarrelThief", "漂桶窃贼", 22, I("barrel", "桶箍冲撞", "破桶压低，铁箍对准膝部。", 7, true), I("hide", "缩桶观望", "双手缩进桶沿，只拖动布袋。", 0, false))
            },
            {
                Plan("TideBoneScavenger", "潮骨拾荒者", 34, I("scrape", "刮骨钩", "短钩从骨筐侧面探出。", 10, true), I("sort", "翻拣骨筐", "短钩收回筐内，只剩骨片互相摩擦。", 0, false)),
                Plan("BoneFluteAttendant", "骨笛侍从", 30, S("flute", "骨笛低鸣", "颈侧骨管随吸气一起收紧。", 4, false, true, true, "骨笛"), I("needle", "笛端刺击", "骨笛横握，尖端朝向甲板。", 8, false)),
                Plan("SaltMarrowPriest", "盐髓司祭", 30, S("cup", "髓盏祷声", "双手托高髓盏，胸前骨板轻响。", 5, true, true, true, "髓盏"), I("shard", "盐骨划刺", "髓盏压回胸前，右手抽出一枚盐骨尖片。", 9, false)),
                Plan("EmptyHoldKnocker", "空舱敲击者", 32, S("knock", "空箱三敲", "短槌悬在胸前空箱上方。", 4, false, true, true, "胸前木箱"), I("door", "门板推击", "背后舱门板向前倾压。", 9, true)),
                Plan("BoneNetStitcher", "缝骨拖网客", 28, I("cast", "骨网投掷", "肩网展开，骨片碰撞声来自左侧。", 8, false, false, true, "肩背拖网"), I("stitch", "收针整网", "粗骨针回到网束，暂未朝向目标。", 0, true))
            },
            {
                Plan("InvertedSurveyHand", "倒悬测量员", 34, I("rule", "折尺量切", "折尺完全张开，长臂沿甲板刻度摆正。", 10, true), S("mark", "逆刻标记", "折尺收拢，炭笔在甲板刻下倒置方位。", 4, false, true, true, "炭笔")),
                Plan("ReverseNavigator", "反写领航员", 32, S("chart", "逆图误导", "空白海图被倒举，指北器持续刮擦纸面。", 4, false, true, true, "指北器"), I("edge", "卷图抽击", "卷边海图合拢成棍状。", 8, true)),
                Plan("MirrorTideScribe", "镜潮记录员", 30, S("mirror", "镜片凝视", "氧化镜片离开账本，正对你的眼睛。", 5, false, true, true, "面部镜片"), I("book", "账本砸击", "泡水账本被双臂高高抱起。", 9, true)),
                Plan("PlumbWatcher", "铅垂守望者", 38, I("plumb", "铅坠摆击", "胸前铅坠离开垂线，皮带已经绷紧。", 14, true), I("still", "校准垂线", "铅坠静止，双手只在调整肩带。", 0, false)),
                Plan("DisplacedWatchman", "错位值更人", 28, I("old-watch", "旧袖擒拿", "褪色旧袖抬起，另一侧仍垂落。", 8, false), I("new-watch", "新袖击打", "较新的袖口后撤蓄力，旧袖贴住胸前。", 10, false))
            },
            {
                Plan("UnflaggedBoarder", "无旗登船兵", 36, I("shield", "缺盾压进", "缺角舷盾封住身前，短刀压在盾后。", 11, true), I("brace", "收盾换步", "短刀垂下，缺角舷盾重新抵住肩窝。", 0, false)),
                Plan("MisfireGunner", "哑火炮手", 34, I("load", "重新装填", "钝口铳朝下，火药筒已经打开。", 0, false), I("shot", "近舷轰击", "短铳抵肩，火门冒出细烟。", 14, true, true, true, "前装铳")),
                Plan("BlackSailMessenger", "黑帆传令", 30, S("flag", "黑旗传令", "短杆黑旗骤然横展，铜束具发出闷响。", 5, false, true, true, "信号旗"), I("staff", "旗杆横扫", "破旗收拢，短杆降到腰侧。", 9, true)),
                Plan("AbandonShipMedic", "弃舰医官", 32, I("case", "药箱重击", "锈药箱被单手提至肩高。", 9, true), S("bandage", "敷料低语", "敷料一圈圈松开，声音却来自箱内。", 4, false, true, true, "药箱")),
                Plan("RustChainEscort", "锈链押运者", 30, I("chain", "短链绞击", "两臂锈链同时收紧，链尾没有拖地。", 10, true), S("buckle", "铁扣震响", "腰间大铁扣反复撞击，双臂没有挥动。", 4, false, true, true, "腰间铁扣"))
            },
            {
                Plan("StarlessPeeker", "无星窥视者", 40, S("hollow", "空面注视", "凹陷面部转向你，双手仍垂在身侧。", 5, true, false, false), I("grasp", "盲手攫取", "凹陷面部偏开，垂落的手指贴着甲板伸长。", 11, false)),
                Plan("AbyssRunner", "深渊听差", 38, S("tube", "传声筒回音", "空铜筒口朝向舱门，回声却来自舷外。", 5, false, true, true, "舷外海面"), I("rush", "低头奔撞", "传声筒贴回腰侧，窄肩压低。", 10, true)),
                Plan("LampOutBellkeeper", "熄灯司钟", 40, S("bell", "罩钟闷鸣", "头部铜钟罩轻颤，无焰提灯保持不动。", 5, false, true, true, "头部钟罩"), I("lantern", "无焰灯击", "提灯向后荡起，钟罩停止震动。", 11, true)),
                Plan("LastTideWitness", "末潮见证者", 38, S("witness", "无字见证", "腹前双手不动，下摆窄签逆风竖起。", 5, false, true, true, "衣摆布签"), I("reach", "长臂扼击", "收拢的双手分开，长袖直指胸前。", 10, true)),
                Plan("RailNameTaker", "舷外索名者", 34, S("ask", "舷外索名", "低垂衣领里传来问句，长臂仍搭在身前。", 5, false, true, true, "衣领内"), I("reach", "长臂攫取", "双臂离开身侧，长指贴近甲板。", 12, true))
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
                    var members = variant == 4 ? new[] { enemies[variant], CreateCompanion(regionIndex) } : new[] { enemies[variant] };
                    var encounter = Asset<EncounterDefinition>(EncounterFolder, id, value =>
                    {
                        value.id = id;
                        value.displayName = $"{RegionTitles[regionIndex]} / {Plans[regionIndex, variant].DisplayName}";
                        value.objective = ObjectiveKind.Defeat;
                        value.objectiveTarget = 1;
                        value.playerHealth = GameBalance.MaxHealth;
                        value.playerSanity = GameBalance.MaxSanity;
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

            ConfigureEcho();
            AddAdditionalBosses(entries, deck, tools);

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
            VoyageEncounterLoadingSetup.Configure(catalog);
            return $"Voyage campaign synchronized: {entries.Count} encounter scenarios.";
        }

        public static void SyncCombatBalance()
        {
            for (var region = 0; region < Regions.Length; region++)
            {
                for (var variant = 0; variant < 5; variant++)
                {
                    CreateEnemy(region, variant, preservePresentation: true);
                    var encounter = Load<EncounterDefinition>($"{EncounterFolder}/voyage-{Regions[region]}-{variant}.asset");
                    encounter.displayName = $"{RegionTitles[region]} / {Plans[region, variant].DisplayName}";
                    encounter.objective = region > 0 && variant == 3 ? EnemyBehavior.SecondaryObjectiveKind(encounter.id) : ObjectiveKind.Defeat;
                    encounter.objectiveTarget = region > 0 && variant == 3 ? 3 : 1;
                    encounter.playerHealth = GameBalance.MaxHealth;
                    encounter.playerSanity = GameBalance.MaxSanity;
                    if (variant == 4)
                        encounter.enemies = new[] { Load<EnemyDefinition>($"{EnemyFolder}/voyage-{Regions[region]}-enemy-4.asset"), CreateCompanion(region) };
                    EditorUtility.SetDirty(encounter);
                    AssetDatabase.SaveAssetIfDirty(encounter);
                    var scenario = Load<BattleScenario>($"{ScenarioFolder}/voyage-{Regions[region]}-{variant}.asset");
                    scenario.chapter = $"航程 / {RegionTitles[region]}";
                    scenario.enemyHeading = encounter.displayName;
                    EditorUtility.SetDirty(scenario);
                    AssetDatabase.SaveAssetIfDirty(scenario);
                }
            }
            var fog = Load<BattleScenario>(Root + "/Content/Scenarios/FogVisitors.asset");
            fog.enemyStatus = "封门后撤离";
            EditorUtility.SetDirty(fog);
            AssetDatabase.SaveAssetIfDirty(fog);
            var regionBosses = new[] { ("M02.asset", 1), ("M03.asset", 2), ("M04.asset", 3), ("M05.asset", 4) };
            foreach (var item in regionBosses)
            {
                var scenario = Load<BattleScenario>($"{Root}/Content/Scenarios/{item.Item1}");
                scenario.chapter = RegionTitles[item.Item2];
                EditorUtility.SetDirty(scenario);
                AssetDatabase.SaveAssetIfDirty(scenario);
            }
            var catalog = Load<VoyageEncounterCatalog>(CatalogPath);
            var serialized = new SerializedObject(catalog);
            var entries = serialized.FindProperty("entries");
            for (var i = 0; i < entries.arraySize; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                var id = entry.FindPropertyRelative("id").stringValue;
                var group = entry.FindPropertyRelative("group");
                for (var region = 0; region < Regions.Length; region++)
                    if (id.StartsWith($"voyage-{Regions[region]}-", StringComparison.Ordinal) ||
                        group.stringValue == new[] { "雾海", "潮骨海", "倒悬海", "无旗舰群", "无星深渊" }[region])
                        group.stringValue = RegionTitles[region];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
        }

        private static EnemyDefinition CreateCompanion(int region)
        {
            var plan = Plans[region, 2];
            var health = new[] { 18, 20, 20, 20, 20 }[region];
            var amounts = new[] { new[] { 5, 3 }, new[] { 4, 7 }, new[] { 4, 7 }, new[] { 4, 7 }, new[] { 5, 12 } }[region];
            var id = $"voyage-{Regions[region]}-companion";
            return Asset<EnemyDefinition>(EnemyFolder, id, value =>
            {
                var intents = plan.Intents.Select((intent, i) => CombatBalanceSetup.CopyIntent(intent, amounts[i])).ToArray();
                if (region > 0) intents = HiddenIntents(intents, false);
                value.id = id; value.displayName = plan.DisplayName + "（随行）"; value.health = health;
                value.intentPolicy = region == 0 ? IntentPolicy.Cycle : IntentPolicy.Random;
                value.intents = intents;
                value.candidateDescription = DescribeIntents(value.intents);
                value.presentationPrefab = Load<EnemyDefinition>($"{EnemyFolder}/voyage-{Regions[region]}-enemy-2.asset").presentationPrefab;
            });
        }

        private static string DescribeIntents(IntentData[] intents) => string.Join(" / ", intents
            .OrderBy(intent => intent.channel == DamageChannel.Health ? 0 : 1)
            .Select(intent => intent.amount == 0 ? intent.displayName :
                $"{intent.displayName}：{intent.amount} {(intent.channel == DamageChannel.Sanity ? "理智冲击" : "生命伤害")}"));

        private static EnemyDefinition CreateEnemy(int region, int variant, bool preservePresentation = false)
        {
            var id = $"voyage-{Regions[region]}-enemy-{variant}";
            var plan = Plans[region, variant];
            var prefab = preservePresentation ? null : DeckAnimationSetup.LoadOrdinary(plan.ModelName);
            if (!preservePresentation && prefab == null) throw new InvalidOperationException($"Missing animated prefab for {plan.ModelName}.");
            if (preservePresentation) Load<EnemyDefinition>($"{EnemyFolder}/{id}.asset");
            return Asset<EnemyDefinition>(EnemyFolder, id, value =>
            {
                var style = region == 0 ? EnemyStyle.Boss : EnemyBehavior.StyleForEncounter($"voyage-{Regions[region]}-{variant}");
                var intents = region == 0 ? plan.Intents : style == EnemyStyle.Rhythm
                    ? RhythmIntents(plan.Intents) : HiddenIntents(plan.Intents, variant == 1);
                if (region == 2 && variant == 4) intents[0].amount = 8;
                value.id = id;
                value.displayName = plan.DisplayName;
                value.health = plan.Health;
                value.candidateDescription = DescribeIntents(intents);
                value.intentPolicy = style == EnemyStyle.Hidden ? IntentPolicy.Random : IntentPolicy.Cycle;
                value.intents = intents;
                if (!preservePresentation) value.presentationPrefab = prefab;
            });
        }
        private static IntentData[] RhythmIntents(IntentData[] source) => source.Select((intent, index) =>
        {
            var copy = CombatBalanceSetup.CopyIntent(intent, intent.amount > 0 ? intent.amount : 8 + index * 2);
            copy.isPublic = true;
            return copy;
        }).ToArray();

        private static IntentData[] HiddenIntents(IntentData[] source, bool includeCharge)
        {
            var health = source.FirstOrDefault(intent => intent.channel == DamageChannel.Health) ?? source[0];
            var sanity = source.FirstOrDefault(intent => intent.channel == DamageChannel.Sanity) ?? source[source.Length - 1];
            var clue = "姿态与声响互相矛盾，尚不能判断它会伤及身体还是理智。";
            var result = new List<IntentData>
            {
                CombatBalanceSetup.CopyIntent(health, 12),
                CombatBalanceSetup.CopyIntent(sanity, 5)
            };
            result[0].channel = DamageChannel.Health; result[1].channel = DamageChannel.Sanity;
            result[0].clue = result[1].clue = clue;
            result[0].isPublic = result[1].isPublic = false;
            if (includeCharge)
                result.Add(Intent("charge", "蓄势观察", clue, DamageChannel.Health, 0, false, false, true));
            return result.ToArray();
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

        private static CardDefinition[] StarterDeck() =>
            CardCatalog.Starter.Select(spec => Load<CardDefinition>($"{Root}/Content/Cards/{spec.Id}.asset")).ToArray();

        private static void ConfigureEcho()
        {
            var echo = Load<EnemyDefinition>(Root + "/Content/Enemies/echo.asset");
            if (echo.intents == null || echo.intents.Length != 2)
                throw new InvalidOperationException("Echo requires its two established intent slots.");
            echo.candidateDescription = "沉重脚步：20 生命伤害（不可打断） / 无风铃鸣：10 理智冲击（可打断）；本回合明确预告";
            echo.intentPolicy = IntentPolicy.Cycle;
            echo.intents[0].amount = 20;
            echo.intents[0].isPublic = true;
            echo.intents[0].interruptible = false;
            echo.intents[1].amount = 10;
            echo.intents[1].isPublic = true;
            echo.intents[1].interruptible = true;
            EditorUtility.SetDirty(echo);
            AssetDatabase.SaveAssetIfDirty(echo);
            var scenario = Load<BattleScenario>(Root + "/Content/Scenarios/FogVisitors.asset");
            scenario.objectiveHint = "绘制封线基础消耗3行动、每回合一次；合法场景折扣照常生效。20生命重压不可打断，10理智铃鸣可打断。";
            scenario.introduction = "在回声的公开重压下完成四段封线；按预告安排防护或把打断留给铃鸣。";
            EditorUtility.SetDirty(scenario);
            AssetDatabase.SaveAssetIfDirty(scenario);
        }

        private static void AddAdditionalBosses(ICollection<(string id, BattleScenario scenario, string group, bool isBoss)> entries,
            CardDefinition[] deck, ToolDefinition[] tools)
        {
            var bellScenario = Load<BattleScenario>(Root + "/Content/Scenarios/DrownedBell.asset");
            var fogScenario = Load<BattleScenario>(Root + "/Content/Scenarios/FogVisitors.asset");
            var bellVisual = Load<EnemyDefinition>(Root + "/Content/Enemies/bell-mother.asset").presentationPrefab;
            var echoVisual = Load<EnemyDefinition>(Root + "/Content/Enemies/echo.asset").presentationPrefab;

            var stormEnemy = BossEnemy("storm-gauntlet", "黑潮压舱者", 72, bellVisual,
                I("breaker", "压舱巨浪", "黑潮脊线越过船艏，撞击方向已经锁定。", 18, true),
                S("undertow", "舱底回声", "压舱舱门向内鼓起，九次敲击清晰可数。", 9, true, true));
            var stormEncounter = BossEncounter("storm-gauntlet", "黑潮压舱战", ObjectiveKind.Survive, 5, stormEnemy, deck, tools);
            var stormRules = Asset<SurvivalRuleModule>(Root + "/Content/Scenarios", "StormGauntletRules",
                value => value.objectiveDescription = "在公开重压下撑过五轮，驶出黑潮");
            var storm = BossScenario("StormGauntlet", stormEncounter, bellScenario.stagePrefab, stormRules,
                "区域 Boss / 黑潮压舱者", "公开循环：18生命巨浪不可打断，9理智回声可打断；撑过五轮。",
                "巨浪回合防护，回声回合可打断或抢攻。", "第五轮潮头越过船尾，鸦号仍然浮在海面。");
            entries.Add(("storm-gauntlet", storm, "区域 Boss", true));

            var beaconEnemy = BossEnemy("hollow-beacon", "空心航标", 48, echoVisual,
                I("ram", "航标撞舷", "锈蚀浮筒侧倾，撞角正对船腰。", 10, true),
                S("call", "空腔呼名", "航标空腔吸入海风，即将喊出船员姓名。", 5, true, true));
            var beaconEncounter = BossEncounter("hollow-beacon", "空心航标", ObjectiveKind.Defeat, 1, beaconEnemy, deck, tools);
            var beacon = BossScenario("HollowBeacon", beaconEncounter, fogScenario.stagePrefab, null,
                "区域 Boss / 空心航标", "本体48生命；公开循环：10生命撞舷不可打断，5理智呼名可打断。",
                "它没有护持或无敌阶段：按行动预告选择防护、打断或直接击破。", "航标外壳裂开，里面只有倒灌的海水。");
            entries.Add(("hollow-beacon", beacon, "区域 Boss", true));
        }

        private static EnemyDefinition BossEnemy(string id, string name, int health, GameObject visual, params IntentData[] intents) =>
            Asset<EnemyDefinition>(EnemyFolder, id, value =>
            {
                value.id = id;
                value.displayName = name;
                value.health = health;
                value.candidateDescription = DescribeIntents(intents);
                value.intentPolicy = IntentPolicy.Cycle;
                value.intents = intents;
                value.presentationPrefab = visual;
            });

        private static EncounterDefinition BossEncounter(string id, string name, ObjectiveKind objective, int target,
            EnemyDefinition enemy, CardDefinition[] deck, ToolDefinition[] tools) =>
            Asset<EncounterDefinition>(EncounterFolder, id, value =>
            {
                value.id = id;
                value.displayName = name;
                value.objective = objective;
                value.objectiveTarget = target;
                value.playerHealth = GameBalance.MaxHealth;
                value.playerSanity = GameBalance.MaxSanity;
                value.actionsPerTurn = 3;
                value.drawPerTurn = 5;
                value.deck = deck;
                value.enemies = new[] { enemy };
                value.equippedTools = tools;
            });

        private static BattleScenario BossScenario(string assetName, EncounterDefinition encounter, BattleStage stage,
            BattleRuleModule rules, string heading, string hint, string introduction, string victory) =>
            Asset<BattleScenario>(Root + "/Content/Scenarios", assetName, value =>
            {
                value.encounter = encounter;
                value.stagePrefab = stage;
                value.actorOverrides = Array.Empty<GameObject>();
                value.rules = rules;
                value.chapter = "区域 Boss";
                value.location = "鸦号调查船 · 禁航水域";
                value.enemyHeading = heading;
                value.objectiveHint = hint;
                value.enemyStatus = string.Empty;
                value.introduction = introduction;
                value.objectiveHeading = encounter.objective == ObjectiveKind.Survive ? "保持船体与神志，撑过重压。" : "击破可受伤的本体。";
                value.taskLabel = string.Empty;
                value.victoryText = victory;
                value.defeatText = "鸦号在公开威胁下失去控制，航程中断。";
            });

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
