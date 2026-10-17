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
        public int ViewRevision { get; private set; }
        public BattleStage Stage { get; private set; }
        public string SelectedEnemy { get; private set; }
        public bool Paused { get; private set; }
        public bool Busy => Time.unscaledTime < feedbackUntil || (Stage != null && (Stage.DelaysHudUntilImpact || Stage.HasPendingSeaAttack || Stage.HasPendingDeckMotion));
        public string Notice { get; private set; }
        public bool NoticeIsError { get; private set; }
        public event Action Changed;
        public event Action<BattleView, BattleView, BattleCommand> Resolved;
        public event Action<ResolvedEnemyAction> PresentedEnemyAction;
        private BattleSession session;
        private float feedbackUntil;
        private float noticeUntil;
        private bool wasBusy;
        public BattleReplay CaptureReplay() => session == null ? null : BattleReplay.Capture(session);

        private void Start() => Restart();
        public void Restart()
        {
            scenario.ValidatePresentation();
            session = scenario.CreateSession(seed, usePreparedDeck: true,
                voyage: VoyageRuntime.OwnsEncounter(this) && !VoyageRuntime.IsDebugSession ? VoyageRuntime.Session : null,
                replay: VoyageRuntime.OwnsEncounter(this) ? VoyageRuntime.PendingReplay : null);
            SelectedEnemy = View.Enemies.FirstOrDefault(x => x.Health > 0 && x.Targetable)?.Id;
            feedbackUntil = 0; wasBusy = false; Paused = false; Notice = scenario.introduction; NoticeIsError = false;
            if (Stage != null) { Stage.gameObject.SetActive(false); Destroy(Stage.gameObject); }
            Stage = Instantiate(scenario.stagePrefab);
            Stage.Bind(this);
            wasBusy = Busy;
            ViewRevision++;
            Changed?.Invoke();
        }
        private void Update()
        {
            if (NoticeIsError && Time.unscaledTime >= noticeUntil)
            {
                NoticeIsError = false; Notice = ""; Changed?.Invoke();
            }
            if (wasBusy && !Busy)
            {
                wasBusy = false;
                if (View.Phase == BattlePhase.Victory && VoyageRuntime.OwnsEncounter(this))
                {
                    VoyageRuntime.ReturnToVoyage();
                    if (!string.IsNullOrEmpty(VoyageRuntime.EncounterSaveError)) Changed?.Invoke();
                    return;
                }
                Changed?.Invoke();
            }
        }
        public void Select(string id)
        {
            if (Paused || Busy || View == null || !View.Enemies.Any(x => x.Id == id && x.Health > 0 && x.Targetable)) return;
            SelectedEnemy = id; Stage.RefreshSelection(); Changed?.Invoke();
        }
        public void SetPaused(bool value) { Paused = value; Changed?.Invoke(); }
        public void ShowInputNotice(string message)
        {
            Notice = string.IsNullOrWhiteSpace(message) ? "当前无法使用这张牌。" : message;
            NoticeIsError = true; noticeUntil = Time.unscaledTime + 2.5f; Changed?.Invoke();
        }
        public CommandResult Submit(BattleCommand command)
        {
            if (session == null || Paused || Busy || GameSceneLoader.IsLoading) return new CommandResult(false, "请等待当前动作结束。");
            var before = View;
            var result = session.Execute(command);
            if (!result.Success)
            {
                Notice = result.Error; NoticeIsError = true; noticeUntil = Time.unscaledTime + 2.5f;
                Changed?.Invoke(); return result;
            }
            ViewRevision++;
            var after = View;
            if (!VoyageRuntime.IsDebugSession && !VoyageRuntime.IsValidationSession && before.Phase != BattlePhase.Victory && after.Phase == BattlePhase.Victory)
                CardDeckPreparation.StoreVerified(after.Evidence);
            if (!after.Enemies.Any(x => x.Id == SelectedEnemy && x.Health > 0 && x.Targetable))
                SelectedEnemy = after.Enemies.FirstOrDefault(x => x.Health > 0 && x.Targetable)?.Id;
            Notice = after.Log.LastOrDefault(x => !x.Contains("阶段：")) ?? "";
            NoticeIsError = false;
            feedbackUntil = Time.unscaledTime + (GameSettings.ReducedMotion || !Stage.placeholderCombatMotion ? 0.12f : command.kind == CommandKind.EndTurn ? 1.05f : 0.4f);
            wasBusy = true;
            Resolved?.Invoke(before, after, command);
            if (!Stage.DelaysHudUntilImpact) Changed?.Invoke();
            return result;
        }
        internal void NotifyPresentationImpact(ResolvedEnemyAction action = null)
        {
            if (action != null) PresentedEnemyAction?.Invoke(action);
            if (Stage == null || !Stage.DelaysHudUntilImpact) Changed?.Invoke();
        }
        private void OnDestroy() { if (Stage != null) Destroy(Stage.gameObject); }
    }
}
