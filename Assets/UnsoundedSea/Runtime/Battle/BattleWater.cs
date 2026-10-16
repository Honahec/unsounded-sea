using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>Owns the battle clock, material instance and planar reflection lifetime.</summary>
    public sealed class BattleWater : MonoBehaviour
    {
        public Renderer surface;
        [Range(0.1f, 1f)] public float reflectionScale = 0.5f;
        public LayerMask reflectionLayers = ~(1 << 4);
        public float SeaTime { get; private set; }
        public PlanarReflectionManager Reflections { get; private set; }
        private Material original, instance;
        private PlanarReflectionVolume volume;
        private MaterialPropertyBlock properties;
        private Wave firstWave, secondWave;
        private bool wavesEnabled;

        // Matches the pinned Uber shader's GersterWaveGenerator: cos height, negative sin horizontal offset.
        private struct Wave
        {
            public Vector3 direction;
            public float frequency, amplitude, speed, horizontal;
            public static Wave Read(Material material, string prefix)
            {
                float k = 2 * Mathf.PI / Mathf.Max(0.01f, material.GetFloat(prefix + "Length"));
                return new Wave { direction = ((Vector3)material.GetVector(prefix + "Direction")).normalized,
                    frequency = k, amplitude = material.GetFloat(prefix + "Height"),
                    speed = Mathf.Sqrt(9.8f * k) * material.GetFloat(prefix + "Speed"),
                    horizontal = material.GetFloat(prefix + "Sharpness") / k };
            }
            public Vector3 Sample(Vector3 restPosition, float time)
            {
                float phase = Vector3.Dot(direction, restPosition) * frequency - time * speed;
                return Vector3.up * (amplitude * Mathf.Cos(phase)) - direction * (horizontal * Mathf.Sin(phase));
            }
        }

        // A presentation sample at an undisplaced water point, not a physics buoyancy solver.
        public Vector3 SampleDisplacement(Vector3 restWorldPosition) => wavesEnabled
            ? firstWave.Sample(restWorldPosition, SeaTime) + secondWave.Sample(restWorldPosition, SeaTime)
            : Vector3.zero;

        public void Bind(Camera camera)
        {
            Unbind();
            if (surface == null || surface.sharedMaterial == null) return;
            SeaTime = 0;
            original = surface.sharedMaterial;
            instance = new Material(original) { name = original.name + " (battle)", hideFlags = HideFlags.DontSave };
            surface.sharedMaterial = instance;
            properties = new MaterialPropertyBlock();
            wavesEnabled = instance.IsKeywordEnabled("_ENABLEWAVE");
            firstWave = Wave.Read(instance, "_1st_Wave_");
            secondWave = Wave.Read(instance, "_2nd_Wave_");
            instance.SetFloat("_UseSeaTime", 1);

            var host = new GameObject("Battle water reflections");
            host.transform.SetParent(transform, false);
            Reflections = host.AddComponent<PlanarReflectionManager>();
            Reflections.sourceCamera = camera;
            Reflections.runOnEditMode = false;
            volume = host.AddComponent<PlanarReflectionVolume>();
            volume.isGlobal = true;
            volume.renderScale = reflectionScale;
            volume.reflectionLayer = reflectionLayers;
            volume.reflectSkybox = true;
            volume.hideReflectionCamera = true;
            volume.reflectionTargets.Add(surface.gameObject);
            volume.UpdateTargetMaterials();
            Tick(0, false, false, Vector4.zero);
        }

        public void Tick(float deltaTime, bool paused, bool reducedMotion, Vector4 threat)
        {
            if (instance == null) return;
            Reflections.suspended = paused;
            if (paused) return;
            // Integrating speed preserves phase when the accessibility setting changes.
            SeaTime += deltaTime * (reducedMotion ? 0.25f : 1f);
            surface.GetPropertyBlock(properties);
            properties.SetFloat("_SeaTime", SeaTime);
            properties.SetVector("_Threat", threat);
            surface.SetPropertyBlock(properties);
        }

        private void OnDisable() => Unbind();

        /// <summary>Release a manually bound surface, including editor previews. Safe to call twice.</summary>
        public void Unbind()
        {
            // Disable before deferred destruction so a restarted stage cannot reuse this singleton.
            if (volume != null) volume.enabled = false;
            if (Reflections != null)
            {
                Reflections.gameObject.SetActive(false);
                DestroyOwned(Reflections.gameObject);
            }
            volume = null;
            Reflections = null;
            if (instance != null)
            {
                if (surface != null)
                {
                    surface.SetPropertyBlock(null);
                    surface.sharedMaterial = original;
                }
                DestroyOwned(instance);
            }
            instance = null;
            original = null;
            wavesEnabled = false;
        }

        private static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
