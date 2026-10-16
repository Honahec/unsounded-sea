namespace UnsoundedSea.Core
{
    public interface IEncounterObjective
    {
        string Description { get; }
        int Progress { get; }
        int Target { get; }
        bool AllowsTaskAction { get; }
        void Advance(int amount);
        void OnTurnResolved();
        bool IsComplete(bool allEnemiesDefeated);
    }

    public sealed class EncounterObjective : IEncounterObjective
    {
        private readonly ObjectiveKind kind;
        public int Progress { get; private set; }
        public int Target { get; }
        public bool AllowsTaskAction => kind == ObjectiveKind.Seal;
        public string Description => kind == ObjectiveKind.Defeat ? "击退所有敌人" :
            kind == ObjectiveKind.Seal ? $"封门 {Progress}/{Target} · 绘制封线：1 行动，每回合一次" : $"等待撤离 {Progress}/{Target} 回合";
        public EncounterObjective(ObjectiveKind kind, int target) { this.kind = Require.Defined(kind); Target = Require.Positive(target); }
        public void Advance(int amount) { if (AllowsTaskAction) Progress = System.Math.Min(Target, Progress + Require.NonNegative(amount)); }
        public void OnTurnResolved() { if (kind == ObjectiveKind.Survive) Progress = System.Math.Min(Target, Progress + 1); }
        public bool IsComplete(bool allEnemiesDefeated) => kind == ObjectiveKind.Defeat ? allEnemiesDefeated : Progress >= Target;
    }
}
