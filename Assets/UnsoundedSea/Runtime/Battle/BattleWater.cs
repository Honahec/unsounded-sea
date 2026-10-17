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
        private Vector4 stormA, stormB, stormC, stormD, currentThreat;
        private float stormStrength;
        public Material impactSprayMaterial;
        private readonly Vector4[] impacts = new Vector4[SeaImpactWave.Capacity];
        private int nextImpact;
        private SeaImpactSpray spray;
        public int ImpactCount { get; private set; }

        public float SurfaceHeight(Vector3 worldPosition) =>
            (surface != null ? surface.transform.position.y : 0) + SampleDisplacement(worldPosition).y;

        public float SampleImpactHeight(Vector3 worldPosition, float secondsAhead = 0)
        {
            float height = 0;
            var p = new Vector2(worldPosition.x, worldPosition.z);
            foreach (var impact in impacts) height += SeaImpactWave.Height(p, SeaTime + secondsAhead, impact);
            return SeaImpactWave.Combine(height);
        }

        public Vector3 SampleImpactSurge(Vector3 worldPosition)
        {
            var p = new Vector2(worldPosition.x, worldPosition.z);
            var surge = Vector2.zero;
            foreach (var impact in impacts)
                surge += (p - new Vector2(impact.x, impact.y)).normalized * SeaImpactWave.Height(p, SeaTime, impact) * 0.3f;
            surge = Vector2.ClampMagnitude(surge, 8f);
            return new Vector3(surge.x, 0, surge.y);
        }

        public void EmitImpact(Vector3 worldPosition, float amplitude = SeaImpactWave.DefaultAmplitude)
        {
            if (instance == null || !wavesEnabled) return;
            impacts[nextImpact] = new Vector4(worldPosition.x, worldPosition.z, SeaTime, amplitude);
            nextImpact = (nextImpact + 1) % impacts.Length;
            ImpactCount++;
            spray?.Emit(worldPosition, ImpactCount, amplitude);
            Publish();
        }

        private void Publish()
        {
            surface.GetPropertyBlock(properties);
            properties.SetFloat("_SeaTime", SeaTime);
            properties.SetVector("_Threat", currentThreat);
            properties.SetVectorArray("_SeaImpacts", impacts);
            surface.SetPropertyBlock(properties);
        }

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
        public Vector3 SampleDisplacement(Vector3 restWorldPosition)
        {
            if (!wavesEnabled) return Vector3.zero;
            var p = new Vector2(restWorldPosition.x, restWorldPosition.z);
            float storm = (StormWave(p, stormA, 0.7f) + StormWave(p, stormB, 2.3f)
                + StormWave(p, stormC, 4.1f) + StormWave(p, stormD, 5.8f)) * stormStrength;
            var delta = p - new Vector2(currentThreat.x, currentThreat.y);
            float d = delta.magnitude, angle = Mathf.Atan2(delta.y, delta.x);
            float r = d - currentThreat.z + 12 * Mathf.Sin(angle * 3 + SeaTime * 0.17f) + 7 * Mathf.Sin(angle * 7 - SeaTime * 0.11f);
            float envelope = currentThreat.z < 0.01f ? 0 : Mathf.Exp(-Mathf.Abs(r) * 0.018f) * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(20, 65, d));
            float wake = envelope * (Mathf.Sin(r * 0.16f - SeaTime * 0.75f + Mathf.Sin(angle * 4) * 0.8f) * 1.4f + Mathf.Sin(r * 0.29f + SeaTime * 0.61f + angle * 3) * 0.65f);
            return firstWave.Sample(restWorldPosition, SeaTime) + secondWave.Sample(restWorldPosition, SeaTime) + Vector3.up * (storm + wake + SampleImpactHeight(restWorldPosition));
        }

        private float StormWave(Vector2 p, Vector4 wave, float offset)
        {
            var dir = new Vector2(Mathf.Cos(wave.x), Mathf.Sin(wave.x));
            var side = new Vector2(-dir.y, dir.x);
            float k = 2 * Mathf.PI / Mathf.Max(0.01f, wave.y);
            float warp = Vector2.Dot(side, p) * k * 0.63f + SeaTime * 0.19f + offset;
            float phase = Vector2.Dot(dir, p) * k - Mathf.Sqrt(9.8f * k) * SeaTime * wave.w + offset + 0.85f * Mathf.Sin(warp);
            return wave.z * (Mathf.Cos(phase) + 0.22f * Mathf.Cos(2 * phase)) / 1.22f;
        }

        public void Bind(Camera camera)
        {
            Unbind();
            if (surface == null || surface.sharedMaterial == null) return;
            SeaTime = 0;
            original = surface.sharedMaterial;
            instance = new Material(original) { name = original.name + " (battle)", hideFlags = HideFlags.DontSave };
            surface.sharedMaterial = instance;
            properties = new MaterialPropertyBlock();
            // Shader displacement exceeds the authored mesh's static Y bounds.
            var bounds = surface.localBounds;
            bounds.Expand(new Vector3(0, SeaImpactWave.CombinedHeightLimit * 2 + 20, 0));
            surface.localBounds = bounds;
            if (impactSprayMaterial != null) spray = new SeaImpactSpray(transform, impactSprayMaterial);
            wavesEnabled = instance.IsKeywordEnabled("_ENABLEWAVE");
            firstWave = Wave.Read(instance, "_1st_Wave_");
            secondWave = Wave.Read(instance, "_2nd_Wave_");
            stormStrength = instance.GetFloat("_StormStrength");
            stormA = instance.GetVector("_StormWaveA"); stormB = instance.GetVector("_StormWaveB");
            stormC = instance.GetVector("_StormWaveC"); stormD = instance.GetVector("_StormWaveD");
            instance.SetFloat("_UseSeaTime", 1);
            // Opaque storm water must depth-test against itself. Transparent draw order
            // otherwise lets distant triangles overwrite a tall foreground crest.
            var opaque = instance.GetFloat("_ShoreFade") == 0 && instance.GetColor("_Color_Shallow").a >= 1
                && instance.GetColor("_Color_Deep").a >= 1 && !instance.IsKeywordEnabled("_ENABLEREFRACTION");
            instance.SetFloat("_SeaZWrite", opaque ? 1 : 0);

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
            currentThreat = threat;
            // Integrating speed preserves phase when the accessibility setting changes.
            SeaTime += deltaTime * (reducedMotion ? 0.25f : 1f);
            spray?.Tick(deltaTime * (reducedMotion ? 0.25f : 1f));
            Publish();
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
            System.Array.Clear(impacts, 0, impacts.Length);
            nextImpact = 0;
            ImpactCount = 0;
            spray?.Dispose();
            spray = null;
            if (surface != null) surface.ResetLocalBounds();
        }

        private static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
