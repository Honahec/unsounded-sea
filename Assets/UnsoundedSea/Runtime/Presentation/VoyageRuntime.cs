using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    public static class VoyageRuntime
    {
        public const string VoyageScene = "Voyage";
        public const string BattleScene = "OceanBattle";
        public const string MainMenuScene = "MainMenu";

        public static VoyageSession Session { get; private set; }
        public static bool HasSavedVoyage => File.Exists(SavePath);
        public static string SavePath => Path.Combine(Application.persistentDataPath, "voyage.json");
        public static bool IsVoyageEncounter => Session != null && Session.Phase == VoyagePhase.Encounter;
        public static string EncounterSaveError { get; private set; }
        private static BattleScenario pendingScenario;
        private static BattleDirector encounterDirector;
        private static bool unsavedOutcome;

        internal static bool OwnsEncounter(BattleDirector director) => encounterDirector != null && encounterDirector == director;

        public static void StartNew(uint? seed = null)
        {
            var session = VoyageSession.Create(seed ?? NewSeed());
            Save(session.Export());
            Session = session;
            unsavedOutcome = false;
            EncounterSaveError = null;
        }

        private static uint NewSeed()
        {
            var bytes = new byte[4];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return (uint)bytes[0] | (uint)bytes[1] << 8 | (uint)bytes[2] << 16 | (uint)bytes[3] << 24;
        }

        public static bool TryResume(out string error)
        {
            try
            {
                if (unsavedOutcome) Checkpoint();
                if (!HasSavedVoyage)
                    throw new FileNotFoundException("未找到航程存档。", SavePath);
                var checkpoint = Load();
                var session = VoyageSession.Restore(checkpoint);
                Session = session;
                error = "";
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        public static void Checkpoint()
        {
            if (Session == null) throw new InvalidOperationException("没有可保存的航程。");
            Save(Session.Export());
            unsavedOutcome = false;
            EncounterSaveError = null;
        }

        public static void EnterEncounter(BattleScenario scenario)
        {
            if (Session == null) throw new InvalidOperationException("没有进行中的航程。");
            if (pendingScenario != null) return;
            if (scenario == null || scenario.encounter == null || scenario.encounter.id != Session.Current.EncounterId)
                throw new InvalidOperationException("航图节点与战斗场景配置不匹配。");
            scenario.ValidatePresentation();
            if (!Application.CanStreamedLevelBeLoaded(BattleScene))
                throw new InvalidOperationException("战斗场景未加入构建列表。");
            var previous = Session.Export();
            if (!IsVoyageEncounter && !Session.BeginEncounter())
                throw new InvalidOperationException("当前位置不能开始遭遇。");
            try { Checkpoint(); }
            catch
            {
                Session = VoyageSession.Restore(previous);
                throw;
            }
            pendingScenario = scenario;
            SceneManager.sceneLoaded -= ConfigureEncounter;
            SceneManager.sceneLoaded += ConfigureEncounter;
            SceneManager.LoadScene(BattleScene);
        }

        private static void ConfigureEncounter(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != BattleScene) return;
            SceneManager.sceneLoaded -= ConfigureEncounter;
            encounterDirector = UnityEngine.Object.FindAnyObjectByType<BattleDirector>();
            if (encounterDirector == null) throw new InvalidOperationException("OceanBattle 场景缺少 BattleDirector。");
            encounterDirector.scenario = pendingScenario;
            encounterDirector.seed = Session.Current.BattleSeed;
            pendingScenario = null;
            encounterDirector.Changed += RecordEncounterOutcome;
            SceneManager.sceneUnloaded += ReleaseEncounter;
        }

        private static void RecordEncounterOutcome()
        {
            if (encounterDirector == null || !IsVoyageEncounter) return;
            var view = encounterDirector.View;
            if (view == null) return;
            var phase = view.Phase;
            if (phase != BattlePhase.Victory && phase != BattlePhase.Defeat) return;
            if (!Session.ResolveEncounter(phase == BattlePhase.Victory)) return;
            unsavedOutcome = true;
            try { Checkpoint(); }
            catch (Exception exception)
            {
                EncounterSaveError = exception.Message;
                Debug.LogError("航程结算尚未保存，返回标题后选择继续航程重试：" + exception.Message);
            }
        }

        private static void ReleaseEncounter(Scene scene)
        {
            if (scene.name != BattleScene) return;
            SceneManager.sceneUnloaded -= ReleaseEncounter;
            if (encounterDirector != null) encounterDirector.Changed -= RecordEncounterOutcome;
            encounterDirector = null;
        }

        private static VoyageCheckpoint Load()
        {
            var info = new FileInfo(SavePath);
            if (info.Length > 1024 * 1024) throw new InvalidDataException("航程存档超过大小限制。");
            var checkpoint = JsonUtility.FromJson<VoyageCheckpoint>(File.ReadAllText(SavePath));
            return checkpoint ?? throw new InvalidDataException("航程存档为空。");
        }

        private static void Save(VoyageCheckpoint checkpoint)
        {
            if (checkpoint == null) throw new ArgumentNullException(nameof(checkpoint));
            var directory = Path.GetDirectoryName(SavePath);
            Directory.CreateDirectory(directory);
            var temporary = SavePath + ".tmp";
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(checkpoint, true));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(SavePath)) File.Replace(temporary, SavePath, SavePath + ".bak");
            else File.Move(temporary, SavePath);
        }
    }
}
