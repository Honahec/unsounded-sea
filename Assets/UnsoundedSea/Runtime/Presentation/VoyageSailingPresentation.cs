using System;
using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>Owns voyage-only vessel, camera, wake and water. Never advances voyage rules.</summary>
    public sealed class VoyageSailingPresentation : MonoBehaviour
    {
        [SerializeField] private Camera voyageCamera;
        [SerializeField] private Transform vessel;
        [SerializeField] private BattleWater water;
        [SerializeField] private VoyageWake wake;
        [SerializeField] private AudioSource ambience;
        [SerializeField] private Vector3 cameraOffset = new Vector3(25, 17, -29);
        [SerializeField] private Vector3 cameraTarget = new Vector3(0, 0, 4);
        private VoyageSailingPath path;
        private Vector3 routePosition, routeHeading = Vector3.forward;
        private Vector3 previousPosition;
        private float cameraYaw, progress, speed;
        private bool initialized, reducedPassage, reducedRelocated;
        private float reducedElapsed, reducedStartProgress;

        public bool IsPlaying { get; private set; }
        public float Duration => path.Duration;
        public float Progress => progress;
        public float Speed => speed;
        public Vector3 RoutePosition => routePosition;
        public Transform Vessel => vessel;
        public Camera VoyageCamera => voyageCamera;
        public BattleWater Water => water;
        // Normal travel is continuous, including arrival. Only reduced motion uses a short veil.
        public float TransitionOpacity
        {
            get
            {
                if (!reducedPassage || !IsPlaying) return 0;
                float t = Mathf.Clamp01(reducedElapsed / 0.25f);
                return t < 0.3f ? Mathf.SmoothStep(0, 1, t / 0.3f)
                    : t > 0.7f ? Mathf.SmoothStep(1, 0, (t - 0.7f) / 0.3f) : 1;
            }
        }

        public void SetLocation(Vector2 chartPosition, Vector2 heading)
        {
            if (voyageCamera == null || vessel == null || water == null)
                throw new InvalidOperationException("Assign the voyage camera, vessel and water references.");
            if (!initialized) { water.Bind(voyageCamera); initialized = true; }
            routePosition = VoyageSailingPath.WorldPoint(chartPosition);
            routeHeading = new Vector3(heading.x, 0, heading.y).normalized;
            if (routeHeading.sqrMagnitude < 0.001f) routeHeading = Vector3.forward;
            previousPosition = routePosition;
            vessel.SetPositionAndRotation(routePosition + water.SampleDisplacement(routePosition), Quaternion.LookRotation(routeHeading));
            cameraYaw = Mathf.Atan2(routeHeading.x, routeHeading.z) * Mathf.Rad2Deg;
            PlaceCamera(1);
            wake?.Clear();
        }

        public void Begin(Vector2 destination, bool reduceMotion)
        {
            if (IsPlaying) return;
            if (!initialized) throw new InvalidOperationException("Set the voyage location before sailing.");
            path = new VoyageSailingPath(routePosition, VoyageSailingPath.WorldPoint(destination), routeHeading);
            reducedPassage = reduceMotion; reducedRelocated = false; reducedElapsed = 0; reducedStartProgress = 0;
            progress = 0; speed = 0; IsPlaying = true;
        }

        public void Advance(float deltaTime, bool reduceMotion)
        {
            if (!IsPlaying) return;
            if (reduceMotion && !reducedPassage)
            {
                reducedPassage = true; reducedElapsed = 0; reducedStartProgress = progress;
            }
            if (!reducedPassage)
            {
                progress = Mathf.Clamp01(progress + Mathf.Max(0, deltaTime) / Duration);
                path.Evaluate(progress, out routePosition, out routeHeading);
                return;
            }
            reducedElapsed += Mathf.Max(0, deltaTime);
            float t = Mathf.Clamp01(reducedElapsed / 0.25f);
            progress = Mathf.Lerp(reducedStartProgress, 1, t);
            // Relocate once, while the accessibility veil is fully opaque.
            if (t >= 0.5f && !reducedRelocated)
            {
                reducedRelocated = true;
                path.Evaluate(1, out routePosition, out routeHeading);
                vessel.SetPositionAndRotation(routePosition + water.SampleDisplacement(routePosition), Quaternion.LookRotation(routeHeading));
                cameraYaw = Mathf.Atan2(routeHeading.x, routeHeading.z) * Mathf.Rad2Deg;
                PlaceCamera(1); wake?.Clear();
            }
        }

        public void Finish()
        {
            if (!IsPlaying) return;
            IsPlaying = false; speed = 0;
            // Keep the arrival pose. Returning to a previous baseline would visibly teleport the ship.
        }

        public void Tick(float deltaTime, bool paused, bool reduceMotion)
        {
            if (!initialized) return;
            water.Tick(deltaTime, paused, reduceMotion, Vector4.zero);
            if (ambience != null)
            {
                if (paused && ambience.isPlaying) ambience.Pause();
                else if (!paused && !ambience.isPlaying) ambience.UnPause();
            }
            if (paused) return;
            float dt = Mathf.Max(0.0001f, deltaTime);
            float targetSpeed = IsPlaying && !reduceMotion ? Vector3.Distance(routePosition, previousPosition) / dt : 0;
            speed = Mathf.Lerp(speed, targetSpeed, 1 - Mathf.Exp(-dt * 4));
            previousPosition = routePosition;
            if (!reduceMotion && !(IsPlaying && reducedPassage))
            {
                FollowWaves(dt);
                float yaw = Mathf.Atan2(routeHeading.x, routeHeading.z) * Mathf.Rad2Deg;
                cameraYaw = Mathf.LerpAngle(cameraYaw, yaw, 1 - Mathf.Exp(-dt * 1.4f));
                PlaceCamera(1 - Mathf.Exp(-dt * 3));
            }
            wake?.Tick(dt, routePosition, routeHeading, speed, water, reduceMotion);
        }

        private void FollowWaves(float dt)
        {
            var right = Vector3.Cross(Vector3.up, routeHeading);
            float pitch = -Mathf.Atan2(water.SampleDisplacement(routePosition + routeHeading * 7).y - water.SampleDisplacement(routePosition - routeHeading * 7).y, 14) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(water.SampleDisplacement(routePosition + right * 2.5f).y - water.SampleDisplacement(routePosition - right * 2.5f).y, 5) * Mathf.Rad2Deg;
            var rotation = Quaternion.LookRotation(routeHeading) * Quaternion.Euler(Mathf.Clamp(pitch, -6, 6), 0, Mathf.Clamp(roll, -8, 8));
            // Horizontal translation belongs to the route, while swell adds a small physical drift.
            var displacement = water.SampleDisplacement(routePosition);
            displacement.x *= 0.4f; displacement.z *= 0.4f;
            float blend = 1 - Mathf.Exp(-dt * 3);
            vessel.position = Vector3.Lerp(vessel.position, routePosition + displacement, blend);
            vessel.rotation = Quaternion.Slerp(vessel.rotation, rotation, blend);
        }

        private void PlaceCamera(float blend)
        {
            var orbit = Quaternion.Euler(0, cameraYaw, 0);
            // Track navigation, not hull roll; only a fifth of the heave reaches the camera.
            var center = routePosition + Vector3.up * (vessel.position.y * 0.2f);
            var position = center + orbit * cameraOffset;
            var target = center + orbit * cameraTarget;
            var cameraTransform = voyageCamera.transform;
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, position, blend);
            cameraTransform.rotation = Quaternion.Slerp(cameraTransform.rotation, Quaternion.LookRotation(target - cameraTransform.position, Vector3.up), blend);
        }

        private void OnDisable()
        {
            IsPlaying = false; initialized = false;
            if (water != null) water.Unbind();
            wake?.Clear();
        }
    }
}
