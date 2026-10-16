using System;
using System.Linq;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    public sealed class BattleDirector : MonoBehaviour
    {
        public BattleScenario scenario;
        public uint seed = 20261016;
        public BattleView View => session?.View;
        public BattleStage Stage { get; private set; }
        public string SelectedEnemy { get; private set; }
        public bool Paused { get; private set; }
        public bool Busy => Time.unscaledTime < feedbackUntil;
        public string Notice { get; private set; }
        public event Action Changed;
        public event Action<BattleView, BattleView, BattleCommand> Resolved;
        private BattleSession session;
        private float feedbackUntil;
        private bool wasBusy;

        private void Start() => Restart();
        public void Restart()
        {
            scenario.ValidatePresentation();
            session = scenario.CreateSession(seed);
            SelectedEnemy = View.Enemies.FirstOrDefault(x => x.Health > 0)?.Id;
            feedbackUntil = 0; wasBusy = false; Paused = false; Notice = scenario.introduction;
            if (Stage != null) { Stage.gameObject.SetActive(false); Destroy(Stage.gameObject); }
            Stage = Instantiate(scenario.stagePrefab);
            Stage.Bind(this);
            Changed?.Invoke();
        }
        private void Update()
        {
            if (wasBusy && !Busy) { wasBusy = false; Changed?.Invoke(); }
        }
        public void Select(string id)
        {
            if (Paused || View == null || !View.Enemies.Any(x => x.Id == id && x.Health > 0)) return;
            SelectedEnemy = id; Stage.RefreshSelection(); Changed?.Invoke();
        }
        public void SetPaused(bool value) { Paused = value; Changed?.Invoke(); }
        public CommandResult Submit(BattleCommand command)
        {
            if (session == null || Paused || Busy) return new CommandResult(false, "请等待当前动作结束。");
            var before = View;
            var result = session.Execute(command);
            if (!result.Success) { Notice = result.Error; Changed?.Invoke(); return result; }
            var after = View;
            if (!after.Enemies.Any(x => x.Id == SelectedEnemy && x.Health > 0))
                SelectedEnemy = after.Enemies.FirstOrDefault(x => x.Health > 0)?.Id;
            Notice = after.Log.LastOrDefault() ?? "";
            feedbackUntil = Time.unscaledTime + (GameSettings.ReducedMotion ? 0.12f : command.kind == CommandKind.EndTurn ? 1.05f : 0.4f);
            wasBusy = true;
            Resolved?.Invoke(before, after, command);
            Changed?.Invoke();
            return result;
        }
        private void OnDestroy() { if (Stage != null) Destroy(Stage.gameObject); }
    }
}
