using System;
using System.Collections.Generic;

namespace UnsoundedSea.Core
{
    public interface ICardEffectHandler
    {
        bool RequiresEnemy { get; }
        void Apply(EffectContext context, int amount);
    }

    // Effects receive intentional mutation operations, never mutable state or the RNG.
    public sealed class EffectContext
    {
        private readonly BattleSession session;
        private readonly string target;
        internal EffectContext(BattleSession session, string target) { this.session = session; this.target = target; }
        public bool IntentConfirmed => session.IsConfirmed(target);
        public void Damage(int amount) => session.DamageEnemy(target, Require.NonNegative(amount));
        public void Block(int amount) => session.AddBlock(Require.NonNegative(amount));
        public void Ward(int amount) => session.AddWard(Require.NonNegative(amount));
        public void Draw(int amount) => session.Draw(Require.NonNegative(amount));
        public void Heal(int amount) => session.Heal(Require.NonNegative(amount));
        public void Investigate() => session.Investigate(target, InvestigationKind.VisibleAction, "卡牌调查");
        public void Interrupt() => session.Interrupt(target);
        public void AdvanceObjective(int amount) => session.AdvanceObjective(Require.NonNegative(amount));
        public void RecordClue() => session.RecordClue(target);
    }

    public sealed class EffectRegistry
    {
        private sealed class Handler : ICardEffectHandler
        {
            private readonly Action<EffectContext, int> apply;
            public bool RequiresEnemy { get; }
            public Handler(bool requiresEnemy, Action<EffectContext, int> apply) { RequiresEnemy = requiresEnemy; this.apply = apply; }
            public void Apply(EffectContext context, int amount) => apply(context, amount);
        }
        private readonly Dictionary<string, ICardEffectHandler> handlers = new Dictionary<string, ICardEffectHandler>(StringComparer.Ordinal);
        public void Register(string id, ICardEffectHandler handler)
        { handlers.Add(Require.Id(id), handler ?? throw new ArgumentNullException(nameof(handler))); }
        internal ICardEffectHandler Get(string id) => handlers.TryGetValue(id, out var handler) ? handler : throw new ArgumentException($"Unknown effect: {id}");
        internal EffectRegistry Snapshot()
        {
            var copy = new EffectRegistry();
            foreach (var entry in handlers) copy.Register(entry.Key, entry.Value);
            return copy;
        }
        public static EffectRegistry CreateDefault()
        {
            var result = new EffectRegistry();
            result.Register("damage", new Handler(true, (c, n) => c.Damage(n)));
            result.Register("block", new Handler(false, (c, n) => c.Block(n)));
            result.Register("ward", new Handler(false, (c, n) => c.Ward(n)));
            result.Register("draw", new Handler(false, (c, n) => c.Draw(n)));
            result.Register("heal", new Handler(false, (c, n) => c.Heal(n)));
            result.Register("observe", new Handler(true, (c, n) => c.RecordClue()));
            result.Register("investigate", new Handler(true, (c, n) => { if (c.IntentConfirmed) c.Draw(1); else c.Investigate(); }));
            result.Register("conditional-damage", new Handler(true, (c, n) => c.Damage(c.IntentConfirmed ? n : 5)));
            result.Register("interrupt", new Handler(true, (c, n) => c.Interrupt()));
            result.Register("objective", new Handler(false, (c, n) => c.AdvanceObjective(n)));
            // Catalog cards are executed transactionally by BattleSession; registration keeps definition validation strict.
            for (var i = 1; i <= 96; i++) result.Register("c" + i.ToString("000"), new Handler(false, (c, n) => { }));
            return result;
        }
    }
}
