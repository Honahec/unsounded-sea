namespace UnsoundedSea.Core
{
    public interface IEncounterObjective
    {
        string Description { get; }
        int Progress { get; }
        int Target { get; }
        bool AllowsTaskAction { get; }
        int ActionCost { get; }
        void Advance(int amount);
        void OnTurnResolved();
        bool IsComplete(bool allEnemiesDefeated);
    }

    public sealed class EncounterObjective : IEncounterObjective
    {
        public const int SealActionCost = 3;
        private readonly ObjectiveKind kind;
        private readonly bool defeatAlternative;
        public int Progress { get; private set; }
        public int Target { get; }
        public ObjectiveKind Kind => kind;
        public bool AllowsTaskAction => kind == ObjectiveKind.Seal;
        public int ActionCost => AllowsTaskAction ? SealActionCost : 0;
        public string Description => kind == ObjectiveKind.Defeat ? "击退所有敌人" :
            kind == ObjectiveKind.Seal ? $"封线 {Progress}/{Target}" : $"等待撤离 {Progress}/{Target} 回合";
        public EncounterObjective(ObjectiveKind kind, int target, bool defeatAlternative = false)
        {
            this.kind = Require.Defined(kind); Target = Require.Positive(target); this.defeatAlternative = defeatAlternative;
        }
        public void Advance(int amount)
        {
            if (kind != ObjectiveKind.Defeat) Progress = System.Math.Min(Target, Progress + Require.NonNegative(amount));
        }
        public void OnTurnResolved() { if (kind == ObjectiveKind.Survive) Progress = System.Math.Min(Target, Progress + 1); }
        public bool IsComplete(bool allEnemiesDefeated) =>
            kind == ObjectiveKind.Defeat ? allEnemiesDefeated : Progress >= Target || defeatAlternative && allEnemiesDefeated;
    }
}
