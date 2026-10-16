using UnityEngine;

namespace UnsoundedSea.Runtime
{
    public enum BattleEnvironment { Deck, Sea }

    // Optional on an actor anchor. Unannotated anchors retain the original deck presentation.
    public sealed class BattleAnchor : MonoBehaviour
    {
        public BattleEnvironment environment;
        [Range(0, 1.2f)] public float labelHeight = 1;
        public Vector3 labelOffset = new Vector3(0, 0.28f, 0);
        public bool showSelectionRing = true;
        [Min(0)] public float heave = 0.2f;
        [Min(0)] public float waterRadius = 12;
    }
}
