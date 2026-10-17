using UnityEngine;

namespace UnsoundedSea.Runtime
{
    public sealed class MainMenuSeaPresentation : MonoBehaviour
    {
        public BattleWater water;
        public Camera menuCamera;
        public Transform vessel;
        public Transform lighthouse;
        public Material departureWakeMaterial;
        private Vector3 vesselRestPosition;
        private Quaternion vesselRestRotation, cameraRestRotation;
        private Vector3 cameraRestPosition;
        private float restFog, departureProgress;
        private bool departing;
        private VoyageSailingPath departurePath;
        private VoyageWake departureWake;
        private Material beaconMaterial;
        private Light beacon;
        private LineRenderer beam, glowHorizontal, glowVertical;
        public float BeaconStrength { get; private set; }
        public int DepartureWakeSamples => departureWake != null ? departureWake.SampleCount : 0;

        private void OnEnable()
        {
            if (Application.isPlaying && menuCamera != null && menuCamera.GetComponent<AudioListener>() == null)
                menuCamera.gameObject.AddComponent<AudioListener>();
            if (water == null || menuCamera == null) return;
            if (vessel != null) { vesselRestPosition = vessel.position; vesselRestRotation = vessel.rotation; }
            cameraRestPosition = menuCamera.transform.position; cameraRestRotation = menuCamera.transform.rotation;
            restFog = RenderSettings.fogDensity;
            water.Bind(menuCamera);
        }

        private void Update()
        {
            float departureSpeed = departing && !GameSettings.ReducedMotion
                ? 7 * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, 1, departureProgress)) : 0;
            SeaAudio.World("harbor", GameSceneLoader.IsLoading && !departing, departureSpeed);
            if (water == null) return;
            water.Tick(Time.unscaledDeltaTime, false, GameSettings.ReducedMotion, Vector4.zero);
            if (vessel == null) return;
            var point = vesselRestPosition; var heading = vesselRestRotation * Vector3.forward;
            if (departing && !GameSettings.ReducedMotion)
            {
                departurePath.Evaluate(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, 1, departureProgress)), out point, out heading);
                vessel.rotation = Quaternion.FromToRotation(vesselRestRotation * Vector3.forward, heading) * vesselRestRotation;
            }
            vessel.position = point + water.SampleDisplacement(point);
            if (departing)
            {
                RenderSettings.fogDensity = Mathf.Lerp(restFog, .065f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.16f, .85f, departureProgress)));
                if (!GameSettings.ReducedMotion)
                {
                    var follow = Quaternion.LookRotation(heading) * new Vector3(25, 17, -29);
                    float blend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, 1, departureProgress));
                    menuCamera.transform.position = Vector3.Lerp(cameraRestPosition, point + follow, blend);
                    menuCamera.transform.rotation = Quaternion.Slerp(cameraRestRotation, Quaternion.LookRotation(point + Vector3.up * 3 - menuCamera.transform.position), blend);
                    departureWake?.Tick(Time.unscaledDeltaTime, point, heading, 7 * blend, water, false);
                }
                UpdateBeacon(Mathf.SmoothStep(0, 1, departureProgress / .18f));
            }
        }

        public void BeginDeparture()
        {
            departing = true; departureProgress = 0;
            var heading = vesselRestRotation * Vector3.forward;
            var starboard = Vector3.Cross(Vector3.up, heading).normalized;
            departurePath = new VoyageSailingPath(vesselRestPosition, vesselRestPosition + heading * 42 + starboard * 18, heading);
            if (departureWakeMaterial != null)
            {
                var host = new GameObject("Departure wake"); host.transform.SetParent(transform, false);
                departureWake = host.AddComponent<VoyageWake>(); host.GetComponent<MeshRenderer>().sharedMaterial = departureWakeMaterial;
            }
            if (lighthouse == null) lighthouse = transform.Find("Lighthouse");
            if (lighthouse == null) return;
            var bounds = BattleActorView.MeasureLocalBounds(lighthouse);
            var lamp = lighthouse.TransformPoint(new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * .86f, bounds.center.z));
            var lightObject = new GameObject("Departure lighthouse beacon"); lightObject.transform.SetParent(transform, false);
            lightObject.transform.position = lamp; beacon = lightObject.AddComponent<Light>();
            beacon.type = LightType.Point; beacon.range = 140; beacon.color = new Color(1, .78f, .42f); beacon.intensity = 0;
            beaconMaterial = new Material(Resources.Load<Shader>("BossWind"));
            LineRenderer Line(string name, float start, float end)
            {
                var line = new GameObject(name).AddComponent<LineRenderer>(); line.transform.SetParent(lightObject.transform, false);
                line.sharedMaterial = beaconMaterial; line.positionCount = 2; line.useWorldSpace = true;
                line.startWidth = start; line.endWidth = end; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return line;
            }
            beam = Line("Beacon through mist", .8f, 9);
            glowHorizontal = Line("Lantern horizontal glow", 1.2f, 1.2f);
            glowVertical = Line("Lantern vertical glow", 1.2f, 1.2f);
            UpdateBeacon(0);
        }

        public void SetDepartureProgress(float progress) => departureProgress = Mathf.Clamp01(progress);
        private void UpdateBeacon(float strength)
        {
            BeaconStrength = strength;
            if (beacon == null) return;
            beacon.intensity = 1600 * strength;
            var point = beacon.transform.position;
            var color = new Color(1.2f, .9f, .48f, strength * .2f);
            beam.startColor = beam.endColor = color; beam.SetPosition(0, point); beam.SetPosition(1, vessel.position + Vector3.up);
            color.a = strength * .95f;
            glowHorizontal.startColor = glowHorizontal.endColor = color;
            glowVertical.startColor = glowVertical.endColor = color;
            var right = menuCamera.transform.right * 1.3f; var up = menuCamera.transform.up * 1.3f;
            glowHorizontal.SetPosition(0, point - right); glowHorizontal.SetPosition(1, point + right);
            glowVertical.SetPosition(0, point - up); glowVertical.SetPosition(1, point + up);
        }

        public void CancelDeparture()
        {
            if (!departing) return;
            departing = false; BeaconStrength = 0;
            if (vessel != null) vessel.SetPositionAndRotation(vesselRestPosition, vesselRestRotation);
            if (menuCamera != null) menuCamera.transform.SetPositionAndRotation(cameraRestPosition, cameraRestRotation);
            RenderSettings.fogDensity = restFog;
            if (beacon != null) Destroy(beacon.gameObject);
            if (departureWake != null) Destroy(departureWake.gameObject);
            if (beaconMaterial != null) Destroy(beaconMaterial);
        }

        private void OnDisable()
        {
            CancelDeparture();
            if (vessel != null) vessel.position = vesselRestPosition;
        }
    }
}
