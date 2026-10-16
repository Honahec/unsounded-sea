using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    public interface IBattleSaveStore
    {
        void Save(BattleReplay replay);
        BattleReplay Load();
    }

    // Injectable path makes tests independent of the player's save directory.
    public sealed class JsonBattleSaveStore : IBattleSaveStore
    {
        private readonly string path;
        public JsonBattleSaveStore(string path) { this.path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path))); }
        public void Save(BattleReplay replay)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temporary = path + ".tmp";
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(replay, true));
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        public BattleReplay Load()
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Save exceeds size limit.");
            var replay = JsonUtility.FromJson<BattleReplay>(File.ReadAllText(path));
            return replay ?? throw new InvalidDataException("Empty save.");
        }
    }
}
