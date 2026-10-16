using System;
using System.Linq;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [Serializable]
    public sealed class CardEffectData
    {
        public string effectId = "damage";
        [Min(0)] public int amount = 6;
        public EffectSpec Build() => new EffectSpec(effectId, amount);
    }

    [CreateAssetMenu(menuName = "Unsounded Sea/Card")]
    public sealed class CardDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;
        [Min(0)] public int actionCost = 1;
        [Min(0)] public int sanityCost;
        public bool exhaust;
        public CardEffectData[] effects = Array.Empty<CardEffectData>();
        public CardSpec Build() => new CardSpec(id, displayName, description, actionCost, effects.Select(x => x.Build()), sanityCost, exhaust);
    }
}
