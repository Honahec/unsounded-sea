using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>A local cinematic route, in metres. Chart north maps to world +Z.</summary>
    public readonly struct VoyageSailingPath
    {
        private readonly Vector3 start, first, second, end;
        public float Duration { get; }
        public static Vector3 WorldPoint(Vector2 chart) => new Vector3((chart.x - 50) * 3, 0, (chart.y - 12) * 3);

        public VoyageSailingPath(Vector3 start, Vector3 end, Vector3 departureHeading)
        {
            this.start = start; this.end = end;
            var delta = end - start;
            var distance = delta.magnitude;
            var direction = distance > 0.001f ? delta / distance : Vector3.forward;
            departureHeading = Vector3.ProjectOnPlane(departureHeading, Vector3.up).normalized;
            if (departureHeading.sqrMagnitude < 0.001f) departureHeading = direction;
            first = start + departureHeading * (distance * 0.3f);
            second = end - direction * (distance * 0.3f);
            Duration = 5f;
        }

        public void Evaluate(float progress, out Vector3 position, out Vector3 heading)
        {
            float p = Mathf.Clamp01(progress);
            // Zero departure/arrival speed; steering remains tangent to the curved route.
            float t = p * p * (3 - 2 * p), u = 1 - t;
            position = u * u * u * start + 3 * u * u * t * first + 3 * u * t * t * second + t * t * t * end;
            heading = (u * u * (first - start) + 2 * u * t * (second - first) + t * t * (end - second)).normalized;
            if (heading.sqrMagnitude < 0.001f) heading = Vector3.forward;
        }
    }
}
