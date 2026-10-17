using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>Damped buoyancy response; independent of frame rate for a held water sample.</summary>
    public sealed class VesselWaveResponse
    {
        public Vector3 Offset { get; private set; }
        public Vector3 Angles { get; private set; }
        private Vector3 velocity, angularVelocity;

        public void Tick(Vector3 displacement, Vector3 angles, float delta, Vector3 surfaceVelocity = default)
        {
            if (delta <= 0) return;
            // Track a moving water surface using its velocity, retaining damped error.
            // A height-only spring lags metres behind a giant ascending wave and buries the hull.
            var relativeVelocity = velocity - surfaceVelocity;
            Offset = Advance(Offset, ref relativeVelocity, displacement - surfaceVelocity * delta, delta, 18f, 0.8f)
                + surfaceVelocity * delta;
            velocity = relativeVelocity + surfaceVelocity;
            Angles = Advance(Angles, ref angularVelocity, angles, delta, 4.5f, 0.7f);
        }

        private static Vector3 Advance(Vector3 value, ref Vector3 speed, Vector3 target, float dt, float omega, float damping)
        {
            var decay = omega * damping;
            var frequency = omega * Mathf.Sqrt(1 - damping * damping);
            var envelope = Mathf.Exp(-decay * dt);
            var sin = Mathf.Sin(frequency * dt);
            var cos = Mathf.Cos(frequency * dt);
            var error = value - target;
            var next = target + envelope * (error * cos + (speed + decay * error) * (sin / frequency));
            speed = envelope * (speed * cos - (decay * speed + omega * omega * error) * (sin / frequency));
            return next;
        }
    }
}
