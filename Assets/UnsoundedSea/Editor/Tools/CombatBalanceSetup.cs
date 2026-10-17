using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnsoundedSea.Core;
using UnsoundedSea.Runtime;

namespace UnsoundedSea.Editor
{
    // Numeric/content synchronization only; never rebuild scenes, models or prefabs.
    public static class CombatBalanceSetup
    {
        private const string Root = "Assets/UnsoundedSea/Content/";

        [MenuItem("Unsounded Sea/Content/Synchronize Combat Balance")]
        public static string Sync()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before synchronizing content.");
            var dirty = AssetDatabase.FindAssets("t:ScriptableObject", new[] { Root.TrimEnd('/') })
                .Select(guid => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(asset => asset != null && EditorUtility.IsDirty(asset)).Select(asset => asset.name).ToArray();
            if (dirty.Length > 0) throw new InvalidOperationException("Unsaved content edits: " + string.Join(", ", dirty));
            FrameworkSetup.SyncCardCatalog();
            VoyageCampaignSetup.SyncCombatBalance();
            SyncBoss(BossEncounterKind.Choirmaster, "b02-choirmaster", "b02-bone-servant", "b02-tide-messenger");
            SyncBoss(BossEncounterKind.FlagBearer, "b04-flag-bearer", "b04-flag-soldier");
            SyncBoss(BossEncounterKind.Watcher, "b05-watcher", "b05-guard-anchor", "b05-guard-lamp", "b05-guard-bell", "b05-guard-sail", "b05-guard-eye");
            var encounter = Load<EncounterDefinition>("Encounters/fog-visitors.asset");
            encounter.enemies = new[] { FogPrayer(), Load<EnemyDefinition>("Enemies/echo.asset") };
            Save(encounter);
            return "90 cards, 25 ordinary enemies, 5 independent companions, fog-visitors and Boss numeric assets synchronized.";
        }

        private static void SyncBoss(BossEncounterKind kind, params string[] ids)
        {
            for (var slot = 0; slot < ids.Length; slot++)
            {
                var enemy = Load<EnemyDefinition>("Enemies/" + ids[slot] + ".asset");
                enemy.health = BossHealth(kind, slot);
                enemy.candidateDescription = BossIntent(kind, slot);
                enemy.intents = BossIntents(kind, slot);
                Save(enemy);
            }
        }

        public static EnemyDefinition FogPrayer()
        {
            var source = Load<EnemyDefinition>("Enemies/prayer.asset");
            var path = Root + "Enemies/fog-prayer.asset";
            var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
            if (enemy == null) { enemy = ScriptableObject.CreateInstance<EnemyDefinition>(); AssetDatabase.CreateAsset(enemy, path); }
            enemy.id = "prayer"; // Preserve encounter target IDs; the original training asset stays independent.
            enemy.displayName = source.displayName; enemy.health = 24;
            enemy.presentationPrefab = source.presentationPrefab;
            enemy.intents = source.intents.Select(intent => CopyIntent(intent, intent.channel == DamageChannel.Health ? 8 : 4)).ToArray();
            enemy.candidateDescription = "挥刃：8 生命伤害 / 呼唤：4 理智冲击";
            Save(enemy);
            return enemy;
        }

        public static IntentData CopyIntent(IntentData source, int amount) => new IntentData
        {
            id = source.id, displayName = source.displayName, clue = source.clue, channel = source.channel,
            amount = amount, isPublic = source.isPublic, visibleAction = source.visibleAction,
            interruptible = source.interruptible, soundSource = source.soundSource
        };

        public static int BossHealth(BossEncounterKind kind, int slot)
        {
            if (slot == 0) return kind == BossEncounterKind.Choirmaster ? 42 : kind == BossEncounterKind.FlagBearer ? 54 : 1;
            if (kind == BossEncounterKind.Choirmaster) return slot == 1 ? 10 : 12;
            if (kind == BossEncounterKind.Surveyor) return 6;
            return kind == BossEncounterKind.FlagBearer ? 14 : 16;
        }

        public static string BossIntent(BossEncounterKind kind, int slot)
        {
            if (slot == 0) return "特殊遭遇规则控制公开意图";
            if (kind == BossEncounterKind.Choirmaster) return slot == 1 ? "每回合 3 生命" : "恢复发声囊 6";
            if (kind == BossEncounterKind.Surveyor) return "每回合 2 理智";
            if (kind == BossEncounterKind.FlagBearer) return "每回合 4 生命";
            return slot == 1 ? "每回合 5 生命" : slot == 2 ? "每回合 3 理智" : slot == 3 ? "奇数活动回合 4 理智" : slot == 4 ? "首次 6 生命，此后 4" : "首次 3 理智，此后 2";
        }

        public static IntentData[] BossIntents(BossEncounterKind kind, int slot)
        {
            var clue = BossIntent(kind, slot);
            var amount = slot == 0 ? 0 : kind == BossEncounterKind.Choirmaster ? (slot == 1 ? 3 : 0) : kind == BossEncounterKind.Surveyor ? 2 : kind == BossEncounterKind.FlagBearer ? 4 : slot == 1 ? 5 : slot == 2 ? 3 : slot == 3 ? 4 : slot == 4 ? 6 : 3;
            return new[] { new IntentData { id = "boss-script", displayName = slot == 0 ? "规则阶段" : "公开行动", clue = clue,
                channel = clue.Contains("理智") ? DamageChannel.Sanity : DamageChannel.Health, amount = amount,
                isPublic = true, visibleAction = true, soundSource = slot == 0 ? "舷外" : "甲板" } };
        }

        private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(Root + path) ?? throw new InvalidOperationException("Missing content: " + path);
        private static void Save(UnityEngine.Object asset) { EditorUtility.SetDirty(asset); AssetDatabase.SaveAssetIfDirty(asset); }
    }
}
