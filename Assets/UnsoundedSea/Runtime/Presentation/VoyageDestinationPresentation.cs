using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>Only the current location and confirmed destination exist in the world.</summary>
    public sealed class VoyageDestinationPresentation : MonoBehaviour
    {
        public VoyageSceneCatalog catalog;
        private VoyageSceneView current, destination;
        private float baselineFog;
        private bool ownsFog;
        public VoyageSceneView Current => current;
        public VoyageSceneView Destination => destination;
        public static float ArrivalVisibility(float progress) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.6f, 1, progress));

        public void SetLocation(string location, Vector3 position, Vector3 heading)
        {
            Clear();
            baselineFog = RenderSettings.fogDensity; ownsFog = true;
            current = Spawn(location, position, heading, 1);
        }
        public void Begin(string location, Vector3 position, Vector3 heading)
        {
            Release(ref destination);
            destination = Spawn(location, position, heading, 0);
        }
        private VoyageSceneView Spawn(string location, Vector3 position, Vector3 heading, float visibility)
        {
            var entry = catalog != null ? catalog.Find(location) : null;
            if (entry == null || entry.prefab == null)
            {
                Debug.LogWarning("No voyage scene configured for: " + location, this);
                return null;
            }
            var orbit = Quaternion.LookRotation(heading.sqrMagnitude > 0.001f ? heading : Vector3.forward);
            var view = Instantiate(entry.prefab, position + orbit * entry.offset, orbit * Quaternion.Euler(0, entry.heading, 0), transform);
            view.Initialize(); view.SetVisibility(visibility); return view;
        }
        public void Advance(float progress, bool reduced, bool relocated)
        {
            current?.SetVisibility(reduced ? (relocated ? 0 : 1) : 1 - Mathf.SmoothStep(0, 1, progress / 0.24f));
            destination?.SetVisibility(reduced ? (relocated ? 1 : 0) : ArrivalVisibility(progress));
            // A bank of sea mist hides the middle of the passage; the last two seconds clear toward landfall.
            if (ownsFog) RenderSettings.fogDensity = reduced ? baselineFog : Mathf.Lerp(baselineFog, 0.045f,
                Mathf.SmoothStep(0, 1, progress / 0.3f) * (1 - ArrivalVisibility(progress)));
        }
        public void Finish()
        {
            Release(ref current); current = destination; destination = null;
            current?.SetVisibility(1);
            if (ownsFog) RenderSettings.fogDensity = baselineFog;
        }
        public void Tick(BattleWater water, float dt, bool reduced)
        {
            current?.Tick(water, dt, reduced); destination?.Tick(water, dt, reduced);
        }
        private static void Release(ref VoyageSceneView view)
        {
            if (view != null) { view.gameObject.SetActive(false); Destroy(view.gameObject); view = null; }
        }
        public void Clear()
        {
            Release(ref current); Release(ref destination);
            if (ownsFog) RenderSettings.fogDensity = baselineFog;
            ownsFog = false;
        }
        private void OnDisable() => Clear();
    }
}
