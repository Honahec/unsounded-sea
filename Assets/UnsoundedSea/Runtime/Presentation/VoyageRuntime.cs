using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnsoundedSea.Core;
using Newtonsoft.Json;

namespace UnsoundedSea.Runtime
{
    public static class VoyageRuntime
    {
        public const string VoyageScene = "Voyage";
        public const string BattleScene = "OceanBattle";
        public const string MainMenuScene = "MainMenu";
        [Serializable] private sealed class SaveData
        {
            public int Version = 1;
            public VoyageCheckpoint Voyage;
            public BattleReplay Battle;
        }

        public static VoyageSession Session { get; private set; }
        public static bool HasSavedVoyage
        {
            get
            {
                if (!File.Exists(SavePath)) return false;
                try
                {
                    var phase = VoyageSession.Restore(Load().Voyage).Phase;
                    if (!Terminal(phase)) return true;
                    TryArchiveTerminalSave();
                    return false;
                }
                catch { return true; }
            }
        }
        private static string SaveDirectory => Application.persistentDataPath;
        public static string SavePath => Path.Combine(ValidationSaveDirectory ?? SaveDirectory, "voyage.json");
        private static string FinalSavePath => Path.Combine(ValidationSaveDirectory ?? SaveDirectory, "voyage-final.json");
        public static bool IsValidationSession => ValidationSaveDirectory != null;
        public static string ValidationSaveDirectory { get; private set; }
#if UNITY_EDITOR
        // CLI acceptance runs use a real save/checkpoint path without replacing the player's voyage.
        public static void BeginValidation(string directory)
        {
            var validationRoot=Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/UiExperience"))+Path.DirectorySeparatorChar;
            var path=Path.GetFullPath(directory);
            if(!path.StartsWith(validationRoot,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Validation storage must live below Logs/UiExperience.");
            ResetEncounterState();Session=null;ValidationSaveDirectory=path;
        }
        public static void EndValidation(){ResetEncounterState();Session=null;ValidationSaveDirectory=null;}
#endif
        public static bool IsVoyageEncounter => Session != null && Session.Phase == VoyagePhase.Encounter;
        public static string EncounterSaveError { get; private set; }
        public static bool IsDebugSession => Session != null && Session.IsDebug;
        internal static BattleReplay PendingReplay { get; private set; }
        private static BattleScenario pendingScenario;
        private static BattleDirector encounterDirector;
        private static bool unsavedOutcome;
        private static BattleView pendingOutcome;

        internal static bool OwnsEncounter(BattleDirector director) => encounterDirector != null && encounterDirector == director;

        public static void StartNew(uint? seed = null)
        {
            var completedSave = false;
            if (File.Exists(SavePath))
            {
                try { completedSave = Terminal(VoyageSession.Restore(Load().Voyage).Phase); }
                catch (Exception) { }
                if (completedSave) ArchiveTerminalSave();
            }
            var session = VoyageSession.Create(seed ?? NewSeed(), CardDeckPreparation.CrewId);
            Save(new SaveData { Voyage = session.Export() });
            Session = session;
            PendingReplay = null;
            pendingOutcome = null;
            unsavedOutcome = false;
            EncounterSaveError = null;
        }

        public static void StartDebugEncounter(string encounterId, string regionId, bool isBoss)
        {
            if (GameSceneLoader.IsLoading) return;
            var scenario = VoyageEncounterCatalog.Resolve(encounterId);
            scenario.ValidatePresentation();
            if (!Application.CanStreamedLevelBeLoaded(BattleScene))
                throw new InvalidOperationException("战斗场景未加入构建列表。");
            ResetEncounterState();
            Session = VoyageSession.CreateDebug(NewSeed(), regionId, isBoss ? RunNodeKind.Boss : RunNodeKind.Battle, encounterId);
            if (!Session.BeginEncounter())
                throw new InvalidOperationException("调试遭遇无法开始。");
            pendingScenario = scenario;
            SceneManager.sceneLoaded += ConfigureEncounter;
            GameSceneLoader.Load(BattleScene);
        }

        public static void StartDebugNode(string regionId, RunNodeKind kind)
        {
            if (GameSceneLoader.IsLoading) return;
            if (kind == RunNodeKind.Battle || kind == RunNodeKind.Boss)
                throw new ArgumentException("战斗调试必须指定遭遇。", nameof(kind));
            if (!Application.CanStreamedLevelBeLoaded(VoyageScene))
                throw new InvalidOperationException("航程场景未加入构建列表。");
            ResetEncounterState();
            Session = VoyageSession.CreateDebug(NewSeed(), regionId, kind);
            GameSceneLoader.Load(VoyageScene);
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
                if (!HasSavedVoyage) throw new FileNotFoundException("未找到航程存档。", SavePath);
                var saved = Load();
                var session = VoyageSession.Restore(saved.Voyage);
                if (saved.Battle != null)
                {
                    if (session.Phase != VoyagePhase.Encounter)
                        throw new InvalidDataException("非战斗节点包含战斗记录。");
                    var scenario = VoyageEncounterCatalog.Resolve(session.Current.EncounterId);
                    var battle = scenario.CreateSession(session.Current.BattleSeed, voyage: session, replay: saved.Battle);
                    if (battle.View.Phase != BattlePhase.PlayerTurn)
                        throw new InvalidDataException("已结束的战斗不能作为待续遭遇。");
                }
                Session = session;
                PendingReplay = saved.Battle;
                EncounterSaveError = null;
                error = "";
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void ResetEncounterState()
        {
            if (unsavedOutcome) Checkpoint();
            SceneManager.sceneLoaded -= ConfigureEncounter;
            SceneManager.sceneUnloaded -= ReleaseEncounter;
            if (encounterDirector != null) encounterDirector.Resolved -= RecordEncounterCommand;
            encounterDirector = null;
            pendingScenario = null;
            PendingReplay = null;
            pendingOutcome = null;
            unsavedOutcome = false;
            EncounterSaveError = null;
        }

        public static void Checkpoint()
        {
            if (Session == null) throw new InvalidOperationException("没有可保存的航程。");
            ResolvePendingOutcome();
            if (IsDebugSession)
            {
                unsavedOutcome = false;
                EncounterSaveError = null;
                return;
            }
            Save(new SaveData { Voyage = Session.Export(), Battle = PendingReplay });
            if (Terminal(Session.Phase)) ArchiveTerminalSave();
            unsavedOutcome = false;
            EncounterSaveError = null;
        }

        public static void EnterEncounter()
        {
            if (GameSceneLoader.IsLoading) return;
            if (Session == null) throw new InvalidOperationException("没有进行中的航程。");
            if (pendingScenario != null) return;
            var scenario = VoyageEncounterCatalog.Resolve(Session.Current.EncounterId);
            scenario.ValidatePresentation();
            if (!Application.CanStreamedLevelBeLoaded(BattleScene))
                throw new InvalidOperationException("战斗场景未加入构建列表。");
            if (PendingReplay != null)
                scenario.CreateSession(Session.Current.BattleSeed, voyage: Session, replay: PendingReplay);
            var previous = IsDebugSession ? null : Session.Export();
            if (!IsVoyageEncounter && !Session.BeginEncounter())
                throw new InvalidOperationException("当前位置不能开始遭遇。");
            try { Checkpoint(); }
            catch
            {
                if (previous != null) Session = VoyageSession.Restore(previous);
                throw;
            }
            pendingScenario = scenario;
            SceneManager.sceneLoaded -= ConfigureEncounter;
            SceneManager.sceneLoaded += ConfigureEncounter;
            GameSceneLoader.Load(BattleScene);
        }

        public static void ReturnToVoyage()
        {
            if (GameSceneLoader.IsLoading) return;
            if (IsDebugSession)
            {
                GameSceneLoader.Load(Session.Phase == VoyagePhase.Completed || Session.Phase == VoyagePhase.Failed
                    ? MainMenuScene : VoyageScene);
                return;
            }
            try
            {
                Checkpoint();
                GameSceneLoader.Load(VoyageScene);
            }
            catch (Exception exception)
            {
                EncounterSaveError = exception.Message;
                Debug.LogError("航程尚未保存：" + exception.Message);
            }
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
            encounterDirector.Resolved += RecordEncounterCommand;
            SceneManager.sceneUnloaded += ReleaseEncounter;
        }

        private static void RecordEncounterCommand(BattleView before, BattleView after, BattleCommand command)
        {
            if (encounterDirector == null || !IsVoyageEncounter) return;
            unsavedOutcome = true;
            try
            {
                if (after.Phase == BattlePhase.Victory || after.Phase == BattlePhase.Defeat)
                {
                    pendingOutcome = after;
                    ResolvePendingOutcome();
                }
                else PendingReplay = encounterDirector.CaptureReplay();
                Checkpoint();
            }
            catch (Exception exception)
            {
                EncounterSaveError = exception.Message;
                Debug.LogError("航程记录尚未保存，请重试保存：" + exception.Message);
            }
        }

        private static void ResolvePendingOutcome()
        {
            if (pendingOutcome == null) return;
            var outcome = pendingOutcome;
            if (!Session.ResolveEncounter(outcome.Phase == BattlePhase.Victory, outcome.Health, outcome.Sanity,
                    outcome.SpentConsumables, outcome.Watched, outcome.Evidence, outcome.HealthDamageTaken, outcome.SanityDamageTaken))
                throw new InvalidOperationException("航程拒绝了战斗结算。");
            PendingReplay = null;
            pendingOutcome = null;
        }

        private static void ReleaseEncounter(Scene scene)
        {
            if (scene.name != BattleScene) return;
            SceneManager.sceneUnloaded -= ReleaseEncounter;
            if (encounterDirector != null) encounterDirector.Resolved -= RecordEncounterCommand;
            encounterDirector = null;
        }

        private static bool Terminal(VoyagePhase phase) => phase == VoyagePhase.Completed || phase == VoyagePhase.Failed;

        private static void TryArchiveTerminalSave()
        {
            try { ArchiveTerminalSave(); }
            catch (Exception exception) { Debug.LogError("终局记录尚未归档：" + exception.Message); }
        }

        private static void ArchiveTerminalSave()
        {
            if (!File.Exists(SavePath)) return;
            if (File.Exists(FinalSavePath)) File.Delete(FinalSavePath);
            File.Move(SavePath, FinalSavePath);
        }

        private static SaveData Load()
        {
            var info = new FileInfo(SavePath);
            if (info.Length > 4 * 1024 * 1024) throw new InvalidDataException("航程存档超过大小限制。");
            var saved = JsonConvert.DeserializeObject<SaveData>(File.ReadAllText(SavePath));
            if (saved == null || saved.Version != 1 || saved.Voyage == null)
                throw new InvalidDataException("存档版本不兼容，原文件已保留。请返回标题开始新航程。");
            return saved;
        }

        private static void Save(SaveData saved)
        {
            var directory = Path.GetDirectoryName(SavePath);
            Directory.CreateDirectory(directory);
            var temporary = SavePath + ".tmp";
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(saved, Formatting.Indented));
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
