using UnityEngine;

namespace UnsoundedSea.Runtime
{
    public sealed class MainMenuSeaPresentation : MonoBehaviour
    {
        public BattleWater water;
        public Camera menuCamera;
        public Transform vessel;
        private Vector3 vesselRestPosition;

        private void OnEnable()
        {
            if (water == null || menuCamera == null) return;
            if (vessel != null) vesselRestPosition = vessel.position;
            water.Bind(menuCamera);
        }

        private void Update()
        {
            if (water == null) return;
            water.Tick(Time.unscaledDeltaTime, false, GameSettings.ReducedMotion, Vector4.zero);
            if (vessel != null) vessel.position = vesselRestPosition + water.SampleDisplacement(vesselRestPosition);
        }

        private void OnDisable()
        {
            if (vessel != null) vessel.position = vesselRestPosition;
        }
    }
}
