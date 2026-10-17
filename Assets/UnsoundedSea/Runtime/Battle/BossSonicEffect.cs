using System;
using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>Airborne pressure fronts from the sound organ to the vessel; no water impulses.</summary>
    public sealed class BossSonicEffect : MonoBehaviour
    {
        private readonly LineRenderer[] fronts = new LineRenderer[3];
        private Material material;
        private Vector3 origin, target;
        private float elapsed = -1;
        private bool contacted;
        public bool Busy => elapsed >= 0;
        public int LaunchCount { get; private set; }
        public event Action Contact;
        public void Launch(Vector3 from, Vector3 to)
        {
            if (material == null)
            {
                material = new Material(Resources.Load<Shader>("BossWind"));
                for (int i = 0; i < fronts.Length; i++)
                {
                    var go = new GameObject("Sonic pressure front " + i); go.transform.SetParent(transform, false);
                    var line = fronts[i] = go.AddComponent<LineRenderer>();
                    line.sharedMaterial = material; line.useWorldSpace = true; line.loop = true;
                    line.positionCount = 64; line.widthMultiplier = 1.4f;
                    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            origin = from; target = to; elapsed = 0; contacted = false; LaunchCount++;
        }
        public void Tick(float dt, bool reduced)
        {
            if (!Busy) return;
            elapsed += dt;
            var forward = (target - origin).normalized;
            var side = Vector3.Cross(forward, Vector3.up).normalized;
            var up = Vector3.Cross(side, forward);
            for (int i = 0; i < fronts.Length; i++)
            {
                float t = (elapsed - i * .08f) / .8f;
                var line = fronts[i]; line.enabled = t >= 0 && t < 1.15f;
                var center = Vector3.Lerp(origin, target, reduced ? 1 : Mathf.Clamp01(t));
                float radius = Mathf.Lerp(17, 5, Mathf.Clamp01(t));
                float alpha = Mathf.Clamp01(t * 8) * Mathf.Clamp01((1.15f - t) * 5) * (reduced ? .12f : .55f);
                line.startColor = line.endColor = new Color(.8f, .82f, .64f, alpha);
                for (int j = 0; j < 64; j++)
                {
                    float a = j * Mathf.PI * 2 / 64;
                    line.SetPosition(j, center + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius);
                }
            }
            if (!contacted && elapsed >= .8f) { contacted = true; Contact?.Invoke(); }
            if (elapsed >= 1.15f) { elapsed = -1; foreach (var line in fronts) line.enabled = false; }
        }
        private void OnDestroy()
        { if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); } }
    }
}
