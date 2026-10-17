using System;
using System.Collections.Generic;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    // Pure decisions from public card metadata and already-resolved resource snapshots.
    public static class BattleAudioPolicy
    {
        public static string CardCue(CardSpec card)
        {
            if(card==null)return "card";
            switch(card.Id)
            {
                case "revolver":case "c001":case "c004":case "c006":case "c007":case "c008":case "c009":case "c011":case "c013":case "c014":case "c015":return "shot";
                case "reload":return "reload";
                case "E01-01":return "seal";
            }
            switch(card.Category)
            {
                case CardCategory.Armed:return "slash";
                case CardCategory.Defense:return "guard";
                case CardCategory.Investigation:return "insight";
                case CardCategory.Intervention:return "mechanism";
                case CardCategory.Forbidden:return "ritual";
                default:return "card";
            }
        }
        public static string[] ImpactCues(int health,int sanity,int block,int ward,ResolvedEnemyAction action)
        {
            var cues=new List<string>();
            if(action.Amount<=0 || action.Kind==EnemyActionKind.Heal || action.Kind==EnemyActionKind.Summon || action.Kind==EnemyActionKind.Observe)return cues.ToArray();
            if(action.Block<block && action.Channel==DamageChannel.Health)cues.Add("block");
            if(action.Ward<ward && action.Channel==DamageChannel.Sanity)cues.Add("ward");
            if(action.Health<health)cues.Add("hit_body");
            if(action.Sanity<sanity)cues.Add("sanity");
            return cues.ToArray();
        }
    }
}
