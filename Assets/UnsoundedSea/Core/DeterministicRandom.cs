using System;
using System.Collections.Generic;

namespace UnsoundedSea.Core
{
    // Explicit algorithm: independent of UnityEngine.Random and System.Random versions.
    public sealed class DeterministicRandom
    {
        private uint state;
        public DeterministicRandom(uint seed) { state = seed == 0 ? 0x6D2B79F5u : seed; }
        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (int)(state % (uint)exclusiveMax);
        }
        public void Shuffle<T>(IList<T> items)
        {
            for (var i = items.Count - 1; i > 0; --i)
            { var j = Next(i + 1); var item = items[i]; items[i] = items[j]; items[j] = item; }
        }
    }
}
