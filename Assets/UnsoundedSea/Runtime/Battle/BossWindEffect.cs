using System;
using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>M03's travelling air ribbons. Uses the boss clock, never the water wave system.</summary>
    public sealed class BossWindEffect : MonoBehaviour
    {
        private readonly LineRenderer[] ribbons = new LineRenderer[7];
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
                // Resources keeps the runtime-created ribbon shader included in Player builds.
                var shader = Resources.Load<Shader>("BossWind");
                if (shader == null) throw new InvalidOperationException("Missing BossWind shader resource.");
                material = new Material(shader);
                for (var i = 0; i < ribbons.Length; i++)
                {
                    var go = new GameObject("Wind ribbon " + i); go.transform.SetParent(transform, false);
                    var line = go.AddComponent<LineRenderer>(); ribbons[i] = line;
                    line.sharedMaterial = material; line.useWorldSpace = true; line.positionCount = 24;
                    line.widthMultiplier = 1.2f; line.numCornerVertices = 3; line.numCapVertices = 3;
                    line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    line.receiveShadows = false;
                }
            }
            origin = from; target = to; target.y += 12;
            elapsed = 0; contacted = false; LaunchCount++;
        }
        public void Tick(float delta, bool reduced)
        {
            if (!Busy) return;
            elapsed += delta;
            var progress = Mathf.Clamp01(elapsed / 1.05f);
            var center = Vector3.Lerp(origin, target, reduced ? 1 : progress);
            var forward = (target - origin).normalized;
            var side = Vector3.Cross(forward, Vector3.up).normalized;
            var fade = Mathf.Clamp01(elapsed / .12f) * Mathf.Clamp01((1.55f - elapsed) / .5f);
            for (var i = 0; i < ribbons.Length; i++)
            {
                var line = ribbons[i]; line.enabled = elapsed < 1.55f;
                var color = new Color(.63f, .79f, .75f, fade * (reduced ? .15f : .4f));
                line.startColor = color; line.endColor = color;
                for (var j = 0; j < 24; j++)
                {
                    var t = j / 23f;
                    var angle = i * 1.618f + t * 1.4f + (reduced ? 0 : elapsed * 2);
                    var radius = Mathf.Lerp(22, 12, progress) * Mathf.Sin(t * Mathf.PI);
                    line.SetPosition(j, center - forward * ((1 - t) * 100) + side * (Mathf.Cos(angle) * radius) + Vector3.up * (Mathf.Sin(angle) * radius * .55f));
                }
            }
            if (!contacted && progress >= 1) { contacted = true; Contact?.Invoke(); }
            if (elapsed >= 1.55f) elapsed = -1;
        }
        private void OnDestroy()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }
}
