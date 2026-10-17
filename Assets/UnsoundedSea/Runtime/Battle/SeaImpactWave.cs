using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>A finite travelling crest and trailing trough, shared with BattleWaterWake.hlsl.</summary>
    public static class SeaImpactWave
    {
        public const int Capacity = 8;
        public const float Lifetime = 12f;
        public const float Speed = 45f;

        // origin = world XZ, birth clock, amplitude. Zero amplitude is an unused slot.
        public static float Height(Vector2 p, float clock, Vector4 origin)
        {
            var age = clock - origin.z;
            if (origin.w <= 0 || age <= 0 || age >= Lifetime) return 0;
            var delta = p - new Vector2(origin.x, origin.y);
            var distance = delta.magnitude;
            var angle = Mathf.Atan2(delta.y, delta.x);
            var warp = (Mathf.Sin(angle * 3f + 0.7f) * 3f + Mathf.Sin(angle * 7f) * 1.5f)
                * Mathf.Clamp01(distance / 30f);
            var front = Speed * age;
            var q = (distance + warp - front) / 22f;
            if (q >= 1 || q <= -2) return 0;
            // Compact support: no displacement ahead of the advancing front.
            var crest = Mathf.Pow(Mathf.Max(0, 1 - q * q), 3);
            var troughQ = (q + 1f) / 0.9f;
            var trough = Mathf.Pow(Mathf.Max(0, 1 - troughQ * troughQ), 3) * 0.38f;
            var fade = Mathf.SmoothStep(0, 1, age / 0.18f) * (1 - Mathf.SmoothStep(0, 1, (age - 9f) / 3f));
            return origin.w * (crest - trough) * fade / Mathf.Sqrt(1 + distance * 0.025f);
        }
    }
}
