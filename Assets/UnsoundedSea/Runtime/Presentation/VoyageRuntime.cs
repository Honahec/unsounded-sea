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
        public const string BattleScene = "Battle";
        public const string MainMenuScene = "MainMenu";

        public static VoyageSession Session { get; private set; }
        public static bool HasSavedVoyage => File.Exists(SavePath);
        public static string SavePath => Path.Combine(Application.persistentDataPath, "voyage.json");
        public static bool IsVoyageEncounter => Session != null && Session.Phase == VoyagePhase.Encounter;

        public static void StartNew(uint? seed = null)
        {
            var session = VoyageSession.Create(seed ?? NewSeed());
            Save(session.Export());
            Session = session;
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
        }

        public static void EnterEncounter()
        {
            if (Session == null) throw new InvalidOperationException("没有进行中的航程。");
            if (!Application.CanStreamedLevelBeLoaded(BattleScene))
                throw new InvalidOperationException("战斗场景未加入构建列表。");
            var previous = Session.Export();
            if (!Session.BeginEncounter())
                throw new InvalidOperationException("当前位置不能开始遭遇。");
            try { Checkpoint(); }
            catch
            {
                Session = VoyageSession.Restore(previous);
                throw;
            }
            SceneManager.LoadScene(BattleScene);
        }

        public static void ReturnFromBattle(bool victory)
        {
            if (Session == null || Session.Phase != VoyagePhase.Encounter)
                throw new InvalidOperationException("没有等待结算的航程遭遇。");
            if (!Application.CanStreamedLevelBeLoaded(VoyageScene))
                throw new InvalidOperationException("航程场景未加入构建列表。");
            var previous = Session.Export();
            if (!Session.ResolveEncounter(victory))
                throw new InvalidOperationException("航程遭遇无法结算。");
            try { Checkpoint(); }
            catch
            {
                Session = VoyageSession.Restore(previous);
                throw;
            }
            SceneManager.LoadScene(VoyageScene);
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
