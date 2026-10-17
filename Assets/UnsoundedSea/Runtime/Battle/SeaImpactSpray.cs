using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>Owned, manually simulated spray. The water clock also freezes every droplet.</summary>
    public sealed class SeaImpactSpray
    {
        private readonly ParticleSystem particles;
        public SeaImpactSpray(Transform parent, Material material)
        {
            var host = new GameObject("Creature impact spray");
            host.transform.SetParent(parent, false);
            particles = host.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = 173;
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.8f;
            main.maxParticles = 2400;
            var emission = particles.emission; emission.enabled = false;
            var shape = particles.shape; shape.enabled = false;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(
                new Keyframe(0, 0.7f), new Keyframe(0.18f, 0.8f), new Keyframe(1, 1.3f)));
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(0.85f, 0.06f), new GradientAlphaKey(0.45f, 0.5f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.07f;
            renderer.lengthScale = 1.15f;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        public void Emit(Vector3 contact, int seed, float amplitude)
        {
            // Local RNG cannot advance gameplay or ambient random streams.
            var random = new System.Random(seed * 7919);
            var scale = Mathf.Clamp(Mathf.Sqrt(amplitude / 9f), 0.5f, 3.5f);
            for (var i = 0; i < 360; i++)
            {
                float a = (float)random.NextDouble() * Mathf.PI * 2;
                var radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float radius = 3 + (float)random.NextDouble() * 11;
                var mist = i % 3 == 0;
                var emit = new ParticleSystem.EmitParams
                {
                    position = contact + radial * radius * Mathf.Sqrt(scale),
                    velocity = radial * (5 + (float)random.NextDouble() * 17) + Vector3.up * (12 + (float)random.NextDouble() * 23) * Mathf.Pow(scale, 0.25f),
                    startLifetime = (mist ? 4.5f : 2.6f + (float)random.NextDouble()) * Mathf.Sqrt(scale),
                    startSize = (mist ? 9f : 2.8f + (float)random.NextDouble() * 4f) * Mathf.Sqrt(scale),
                    startColor = new Color(0.9f, 0.98f, 0.95f, mist ? 0.5f : 0.95f),
                    rotation = (float)random.NextDouble() * 360
                };
                particles.Emit(emit, 1);
            }
        }
        public void Tick(float delta) => particles.Simulate(Mathf.Max(0, delta), false, false, true);
        public void Dispose()
        {
            if (particles == null) return;
            particles.gameObject.SetActive(false);
            if (Application.isPlaying) Object.Destroy(particles.gameObject);
            else Object.DestroyImmediate(particles.gameObject);
        }
    }
}
