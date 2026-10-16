using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace UnsoundedSea.Core
{
    [Serializable]
    public sealed class BattleReplay
    {
        public const int CurrentVersion = 1;
        public int version;
        public string contentHash;
        public uint seed;
        public List<BattleCommand> commands;

        public static BattleReplay Capture(BattleSession session) => new BattleReplay
        {
            version = CurrentVersion, contentHash = Fingerprint(session.Definition), seed = session.Seed,
            commands = new List<BattleCommand>(session.Commands)
        };

        public BattleSession Restore(BattleDefinition definition, EffectRegistry effects = null, IEncounterObjective objective = null)
        {
            if (version != CurrentVersion) throw new InvalidDataException("Unsupported replay version.");
            if (contentHash != Fingerprint(definition)) throw new InvalidDataException("Content changed; this replay cannot be restored safely.");
            if (commands == null || commands.Count > 100000) throw new InvalidDataException("Invalid command history.");
            var result = new BattleSession(definition, seed, effects, objective);
            for (var i = 0; i < commands.Count; i++)
            {
                var accepted = result.Execute(commands[i]);
                if (!accepted.Success) throw new InvalidDataException($"Replay command {i} rejected: {accepted.Error}");
            }
            return result;
        }

        // Length-prefixed binary data avoids delimiter collisions and platform/culture dependence.
        public static string Fingerprint(BattleDefinition definition)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(CurrentVersion); writer.Write(definition.Id); writer.Write(definition.Health);
                    writer.Write(definition.Sanity); writer.Write(definition.Actions); writer.Write(definition.DrawCount);
                    writer.Write((int)definition.Objective); writer.Write(definition.ObjectiveTarget);
                    writer.Write(definition.Deck.Count);
                    foreach (var card in definition.Deck)
                    {
                        writer.Write(card.Id); writer.Write(card.Name); writer.Write(card.Description); writer.Write(card.Cost);
                        writer.Write(card.SanityCost); writer.Write(card.Exhaust); writer.Write(card.Effects.Count);
                        foreach (var effect in card.Effects) { writer.Write(effect.Id); writer.Write(effect.Amount); }
                    }
                    writer.Write(definition.Enemies.Count);
                    foreach (var enemy in definition.Enemies)
                    {
                        writer.Write(enemy.Id); writer.Write(enemy.Name); writer.Write(enemy.Health); writer.Write(enemy.Candidates);
                        writer.Write(enemy.Intents.Count);
                        foreach (var intent in enemy.Intents)
                        {
                            writer.Write(intent.Id); writer.Write(intent.Name); writer.Write(intent.Clue); writer.Write(intent.SoundSource);
                            writer.Write((int)intent.Channel); writer.Write(intent.Amount); writer.Write(intent.IsPublic);
                            writer.Write(intent.VisibleAction); writer.Write(intent.Interruptible);
                        }
                    }
                    writer.Write(definition.Tools.Count);
                    foreach (var tool in definition.Tools)
                    { writer.Write(tool.Id); writer.Write(tool.Name); writer.Write((int)tool.Kind); writer.Write(tool.Uses); writer.Write(tool.SanityCost); }
                }
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }
    }
}
