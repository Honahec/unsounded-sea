using UnityEngine;

namespace UnsoundedSea.Runtime
{
    // Driven by the voyage clock, never by a separate Update: pause and reduced motion stay coherent.
    public sealed class VoyageSceneFloat : MonoBehaviour
    {
        public Vector2 sampleExtents = new Vector2(2, 5);
        [Range(0, 1)] public float heave = 0.7f;
        [Range(0, 10)] public float maxTilt = 4;
        [Range(0, 1)] public float horizontalDrift;
        private Vector3 origin;
        private Quaternion rotation;
        private bool initialized;
        public void Tick(BattleWater water, float dt)
        {
            if (!initialized) { origin = transform.position; rotation = transform.rotation; initialized = true; }
            var forward = rotation * Vector3.forward; var right = rotation * Vector3.right;
            float pitch = -Mathf.Atan2(water.SampleDisplacement(origin + forward * sampleExtents.y).y - water.SampleDisplacement(origin - forward * sampleExtents.y).y, sampleExtents.y * 2) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(water.SampleDisplacement(origin + right * sampleExtents.x).y - water.SampleDisplacement(origin - right * sampleExtents.x).y, sampleExtents.x * 2) * Mathf.Rad2Deg;
            float blend = 1 - Mathf.Exp(-dt * 2);
            var displacement = water.SampleDisplacement(origin);
            displacement = new Vector3(displacement.x * horizontalDrift, displacement.y * heave, displacement.z * horizontalDrift);
            transform.position = Vector3.Lerp(transform.position, origin + displacement, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation * Quaternion.Euler(Mathf.Clamp(pitch, -maxTilt, maxTilt), 0, Mathf.Clamp(roll, -maxTilt, maxTilt)), blend);
        }
    }
}
