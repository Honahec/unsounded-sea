using System;
using System.Collections.Generic;

namespace UnsoundedSea.Core
{
    public sealed partial class VoyageSession
    {
        private bool echoRitualLearned, echoRitualPrepared, echoRitualExpired;
        public bool KnowsEchoRitual => echoRitualLearned;

        public string RitualStatus => !echoRitualLearned ? "尚未取得仪式" : echoRitualPrepared ? "遮断回声 · 已备妥（下场战斗可用一次）" : echoRitualExpired ? "遮断回声 · 上次筹备已失效，可再次筹备" : "遮断回声 · 已掌握，需在安全补给点筹备";

        public IReadOnlyList<VoyageNodeChoice> GetRitualChoices()
        {
            var choices = new List<VoyageNodeChoice>();
            if (phase == VoyagePhase.Arrived && !nodeResolved && (CurrentFull.Kind == RunNodeKind.Archive || CurrentFull.Kind == RunNodeKind.Event) && !echoRitualLearned)
                choices.Add(new VoyageNodeChoice("ritual:learn", "抄录仪式 · 遮断回声", "放弃此航点其他收益；掌握后可在安全补给点筹备。战斗中支付3理智，压制一只正在攻击的召唤物。"));
            if (CanEditDeck && echoRitualLearned && !echoRitualPrepared)
                choices.Add(new VoyageNodeChoice("ritual:prepare", "筹备 · 遮断回声", "不消耗补给；为下一场战斗备妥一次，战后无论使用与否均失效。"));
            return choices.AsReadOnly();
        }

        public bool ManageRitual(string action)
        {
            VoyageNodeChoice choice = null;
            foreach (var candidate in GetRitualChoices()) if (candidate.Id == action && candidate.Enabled) { choice = candidate; break; }
            if (choice == null) return false;
            if (action == "ritual:learn") { echoRitualLearned = true; nodeResolved = true; }
            else if (action == "ritual:prepare") { echoRitualPrepared = true; echoRitualExpired = false; }
            else return false;
            log.Add(CurrentFull.Name + "：" + choice.Title + "。" + choice.Description);
            Record(VoyageActionKind.ManageSupport, value: action);
            return true;
        }

        internal void FinishRitualEncounter() { if (echoRitualPrepared) echoRitualExpired = true; echoRitualPrepared = false; }
    }
}
