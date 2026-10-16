using System;
using System.Linq;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [Serializable]
    public sealed class IntentData
    {
        public string id;
        public string displayName;
        [TextArea] public string clue;
        public DamageChannel channel;
        [Min(0)] public int amount;
        public bool isPublic;
        public bool visibleAction = true;
        public bool interruptible;
        public string soundSource = "目标本体";
        public IntentSpec Build() => new IntentSpec(id, displayName, clue, channel, amount, isPublic, visibleAction, soundSource, interruptible);
    }

    [CreateAssetMenu(menuName = "Unsounded Sea/Enemy")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [Min(1)] public int health = 30;
        [TextArea] public string candidateDescription;
        public IntentData[] intents = Array.Empty<IntentData>();
        public GameObject presentationPrefab;
        public EnemySpec Build(string instanceId) => new EnemySpec(instanceId, displayName, health, candidateDescription, intents.Select(x => x.Build()));
    }
}
