using System;
using UnityEngine;

namespace UnsoundedSea.Runtime
{
    [CreateAssetMenu(menuName = "Unsounded Sea/Voyage Scene Catalog")]
    public sealed class VoyageSceneCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Matches the persisted location name; independent of event/battle kind.")]
            public string location;
            public VoyageSceneView prefab;
            public Vector3 offset = new Vector3(-20, 0, 25);
            public float heading = -65;
        }
        public Entry[] entries = Array.Empty<Entry>();
        public Entry Find(string location)
        {
            foreach (var entry in entries)
                if (entry != null && string.Equals(entry.location, location, StringComparison.Ordinal)) return entry;
            return null;
        }
    }
}
