using System;
using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public enum BattlePhase { TurnStart, IntentLocked, PlayerTurn, Resolve, TurnEnd, Victory, Defeat }
    public enum DamageChannel { Health, Sanity }
    public enum InvestigationKind { VisibleAction, SoundSource, DamageChannel, Grid, Anchor, Relay, Connection }
    public enum KnowledgeLevel { Unknown, Rumored, Observed, Verified }
    public enum ObjectiveKind { Defeat, Seal, Survive }
    public enum CommandKind { PlayCard, UseTool, UseCrewSupport, ReorderDeck, AdvanceObjective, EndTurn, UseConsumable }
    public enum CrewSupportKind { Medic, Listener, Engineer, Archivist, Navigator, Gunsmith, Diver, Cook, Cartographer, Decoder }

    internal static class Require
    {
        public static string Id(string value) => !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("ID/text must not be empty.");
        public static int Positive(int value) => value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        public static int NonNegative(int value) => value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        public static T Defined<T>(T value) where T : struct => Enum.IsDefined(typeof(T), value) ? value : throw new ArgumentException($"Unknown {typeof(T).Name}.");
        public static IReadOnlyList<T> Copy<T>(IEnumerable<T> values)
        {
            var array = (values ?? throw new ArgumentNullException(nameof(values))).ToArray();
            if (array.Any(x => x == null)) throw new ArgumentException("Null definition.");
            return Array.AsReadOnly(array);
        }
    }

    public sealed class EffectSpec
    {
        public string Id { get; }
        public int Amount { get; }
        public EffectSpec(string id, int amount) { Id = Require.Id(id); Amount = Require.NonNegative(amount); }
    }

    public sealed class CardSpec
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public int Cost { get; }
        public int SanityCost { get; }
        public bool Exhaust { get; }
        public IReadOnlyList<EffectSpec> Effects { get; }
        public CardSpec(string id, string name, string description, int cost, IEnumerable<EffectSpec> effects, int sanityCost = 0, bool exhaust = false)
        {
            Id = Require.Id(id); Name = Require.Id(name); Description = description ?? "";
            Cost = Require.NonNegative(cost); SanityCost = Require.NonNegative(sanityCost);
            Exhaust = exhaust; Effects = Require.Copy(effects);
            if (Effects.Count == 0) throw new ArgumentException("Card needs an effect.");
        }
    }

    public sealed class IntentSpec
    {
        public string Id { get; }
        public string Name { get; }
        public string Clue { get; }
        public string SoundSource { get; }
        public DamageChannel Channel { get; }
        public int Amount { get; }
        public bool IsPublic { get; }
        public bool VisibleAction { get; }
        public bool Interruptible { get; }
        public IntentSpec(string id, string name, string clue, DamageChannel channel, int amount,
            bool isPublic = false, bool visibleAction = true, string soundSource = "目标本体", bool interruptible = false)
        {
            Id = Require.Id(id); Name = Require.Id(name); Clue = Require.Id(clue); Channel = Require.Defined(channel);
            Amount = Require.NonNegative(amount); IsPublic = isPublic; VisibleAction = visibleAction;
            SoundSource = Require.Id(soundSource); Interruptible = interruptible;
        }
    }

    public sealed class EnemySpec
    {
        public string Id { get; }
        public string Name { get; }
        public int Health { get; }
        public string Candidates { get; }
        public IReadOnlyList<IntentSpec> Intents { get; }
        public EnemySpec(string id, string name, int health, string candidates, IEnumerable<IntentSpec> intents)
        {
            Id = Require.Id(id); Name = Require.Id(name); Health = Require.Positive(health);
            Candidates = Require.Id(candidates); Intents = Require.Copy(intents);
            if (Intents.Count == 0 || Intents.Select(x => x.Id).Distinct().Count() != Intents.Count)
                throw new ArgumentException("Enemy needs unique intents.");
        }
    }

    public sealed class ToolSpec
    {
        public string Id { get; }
        public string Name { get; }
        public InvestigationKind Kind { get; }
        public int Uses { get; }
        public int SanityCost { get; }
        public ToolSpec(string id, string name, InvestigationKind kind, int uses = 1, int sanityCost = 0)
        { Id = Require.Id(id); Name = Require.Id(name); Kind = Require.Defined(kind); Uses = Require.Positive(uses); SanityCost = Require.NonNegative(sanityCost); }
    }

    public sealed class CrewSupportSpec
    {
        public string Id { get; }
        public string Name { get; }
        public CrewSupportKind Kind { get; }
        public int Cost { get; }
        public bool Injured { get; }
        public CrewSupportSpec(string id, string name, CrewSupportKind kind, bool injured = false)
        {
            Id = Require.Id(id); Name = Require.Id(name); Kind = Require.Defined(kind); Injured = injured;
            Cost = kind == CrewSupportKind.Listener ? 2 : 1;
        }
    }

    public sealed class BattleDefinition
    {
        public string Id { get; }
        public int Health { get; }
        public int Sanity { get; }
        public int InitialHealth { get; }
        public int InitialSanity { get; }
        public int Actions { get; }
        public int DrawCount { get; }
        public ObjectiveKind Objective { get; }
        public int ObjectiveTarget { get; }
        public IReadOnlyList<CardSpec> Deck { get; }
        public IReadOnlyList<EnemySpec> Enemies { get; }
        public IReadOnlyList<ToolSpec> Tools { get; }
        public IReadOnlyList<CrewSupportSpec> Supports { get; }
        public IReadOnlyList<Evidence> Archive { get; }
        public BattleLoadout Loadout { get; }
        public BattleDefinition(string id, IEnumerable<CardSpec> deck, IEnumerable<EnemySpec> enemies,
            IEnumerable<ToolSpec> tools, ObjectiveKind objective = ObjectiveKind.Defeat, int objectiveTarget = 4,
            int health = 60, int sanity = 30, int actions = 3, int drawCount = 5,
            IEnumerable<CrewSupportSpec> supports = null, IEnumerable<Evidence> archive = null,
            int? initialHealth = null, int? initialSanity = null, BattleLoadout loadout = null)
        {
            Id = Require.Id(id); Deck = Require.Copy(deck); Enemies = Require.Copy(enemies); Tools = Require.Copy(tools);
            Supports = Require.Copy(supports ?? Array.Empty<CrewSupportSpec>());
            Archive = Require.Copy(archive ?? Array.Empty<Evidence>());
            Loadout = loadout ?? new BattleLoadout();
            if (Tools.Count + Loadout.Consumables.Count(x=>x=="i09") > 2) throw new ArgumentException("Powder also occupies a tool slot.");
            Health = Require.Positive(health); Sanity = Require.Positive(sanity); Actions = Require.Positive(actions);
            InitialHealth = Require.Positive(initialHealth ?? Health);
            InitialSanity = Require.Positive(initialSanity ?? Sanity);
            if (InitialHealth > Health || InitialSanity > Sanity)
                throw new ArgumentOutOfRangeException(nameof(initialHealth), "Initial resources cannot exceed their maximum.");
            DrawCount = Require.Positive(drawCount); Objective = objective; ObjectiveTarget = Require.Positive(objectiveTarget);
            if (!Enum.IsDefined(typeof(ObjectiveKind), objective)) throw new ArgumentException("Unknown objective.");
            if (Deck.Count == 0 || Enemies.Count == 0) throw new ArgumentException("Encounter needs a deck and enemies.");
            if (Tools.Count > 2 || Tools.Select(x => x.Id).Distinct().Count() != Tools.Count)
                throw new ArgumentException("Equip at most two unique tools.");
            if (Enemies.Select(x => x.Id).Distinct().Count() != Enemies.Count)
                throw new ArgumentException("Enemy instance IDs must be unique.");
            if (Supports.Count > 4 || Supports.Select(x => x.Id).Distinct().Count() != Supports.Count)
                throw new ArgumentException("Crew support IDs must be unique.");
        }
    }
}
