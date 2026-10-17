using System.Collections.Generic;
using System.Linq;

namespace UnsoundedSea.Core
{
    public sealed partial class BattleSession
    {
        private bool echoRitualAvailable;

        private SupplyActionView EchoRitualView()
        {
            var targets = (boss?.CardTargets(62) ?? EmptyTargets).Where(x => x.Enabled && Find(x.Id.Substring(6))?.Interrupted == false).ToList();
            var enabled = phase == BattlePhase.PlayerTurn && echoRitualAvailable && sanity > 3 && targets.Count > 0;
            return new SupplyActionView("ritual:echo", "遮断回声 · 3 理智", "压制一只当前正在攻击的召唤物。本次筹备仅可使用一次。", echoRitualAvailable ? 1 : 0, true, enabled, targets);
        }

        private string UseEchoRitual(string target, bool consumable)
        {
            if (!consumable || !definition.Loadout.EchoRitual) return "该仪式未备妥。";
            var view = EchoRitualView();
            if (!view.Enabled) return "仪式已用、理智不足或当前没有正在攻击的召唤物。";
            if (!view.Targets.Any(x => x.Id == target && x.Enabled)) return "请选择列出的召唤物；未支付理智。";
            echoRitualAvailable = false;
            sanity -= 3;
            ActiveSanityEquipment(3);
            boss.BindThreat(target, null);
            Record("施行遮断回声；支付3理智，目标本回合攻击被压制。仪式已耗尽。");
            return null;
        }
    }
}
