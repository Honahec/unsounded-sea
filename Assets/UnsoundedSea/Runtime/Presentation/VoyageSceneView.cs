using UnityEngine;

namespace UnsoundedSea.Runtime
{
    public sealed class VoyageSceneView : MonoBehaviour
    {
        [Tooltip("Zero keeps lamps steady; positive values repeat a double signal.")]
        public float signalPeriod;
        private Renderer[] surfaces;
        private Light[] lamps;
        private float[] intensities;
        private VoyageSceneFloat[] floats;
        private MaterialPropertyBlock block;
        private float signal = 1;
        public float Visibility { get; private set; }
        private static readonly int VisibilityId = Shader.PropertyToID("_NodeVisibility");
        public void Initialize()
        {
            surfaces = GetComponentsInChildren<Renderer>(true);
            lamps = GetComponentsInChildren<Light>(true);
            floats = GetComponentsInChildren<VoyageSceneFloat>(true);
            intensities = new float[lamps.Length];
            for (int i = 0; i < lamps.Length; i++) intensities[i] = lamps[i].intensity;
            block = new MaterialPropertyBlock();
        }
        public void SetVisibility(float visibility)
        {
            Visibility = Mathf.Clamp01(visibility);
            foreach (var surface in surfaces)
            {
                surface.enabled = Visibility > 0;
                surface.GetPropertyBlock(block); block.SetFloat(VisibilityId, Visibility); surface.SetPropertyBlock(block);
            }
            for (int i = 0; i < lamps.Length; i++) lamps[i].intensity = intensities[i] * Visibility * signal;
        }
        public void Tick(BattleWater water, float dt, bool reduced)
        {
            if (!reduced)
            {
                foreach (var body in floats) body.Tick(water, dt);
                float phase = signalPeriod > 0 ? Mathf.Repeat(water.SeaTime, signalPeriod) / signalPeriod : 0;
                signal = signalPeriod <= 0 || phase < 0.12f || (phase > 0.24f && phase < 0.36f) ? 1 : 0.12f;
            }
            else signal = 1;
            for (int i = 0; i < lamps.Length; i++) lamps[i].intensity = intensities[i] * Visibility * signal;
        }
    }
}
