using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>A supernatural gust carried around eight overlapping skirt panels.</summary>
    public sealed class PrayerSkirtMotion : MonoBehaviour
    {
        public Transform[] upperPanels, hemPanels;
        public AnimationCurve gust = DeckActorMotion.Curve(0, 0, .22f, .08f, .35f, 1, .54f, .95f, .76f, .48f, 1, 0);
        [Range(0, 45)] public float upperSwing = 16, hemSwing = 34;
        public float GustStrength { get; private set; }
        private Quaternion[] upperRest, hemRest, upperBasis, hemBasis;
        private bool initialized;
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            Capture(upperPanels, out upperRest, out upperBasis);
            Capture(hemPanels, out hemRest, out hemBasis);
        }
        private void Capture(Transform[] panels, out Quaternion[] rest, out Quaternion[] basis)
        {
            rest = new Quaternion[panels.Length]; basis = new Quaternion[panels.Length];
            for (int i = 0; i < panels.Length; i++)
            {
                rest[i] = panels[i].localRotation;
                basis[i] = Quaternion.Inverse(transform.rotation) * panels[i].rotation;
            }
        }
        public void Tick(float age, float attackTime, float hit, bool reduced, bool alive)
        {
            Initialize();
            GustStrength = reduced || !alive || attackTime < 0 ? 0 : Mathf.Max(0, gust.Evaluate(attackTime));
            for (int i = 0; i < upperPanels.Length; i++)
            {
                var sector = i * Mathf.PI * 2 / upperPanels.Length;
                var idle = reduced || !alive ? 0 : Mathf.Sin(age * 1.8f + sector * 1.2f) * .7f;
                // A broad rotating gust plus fast, phase-delayed hem flutter. The waist stays attached.
                var wave = Mathf.Sin(attackTime * Mathf.PI * 5 - sector * .7f);
                var delayed = Mathf.Sin(attackTime * Mathf.PI * 5 - sector * .7f - .8f);
                var lift = GustStrength * (.35f + .65f * wave);
                var tip = GustStrength * (.35f + .65f * delayed);
                var upper = idle + upperSwing * lift;
                var hem = idle * 1.4f + hemSwing * tip;
                if (!reduced && alive) hem += hit * Mathf.Sin(sector + age * 3) * 3;
                Apply(upperPanels[i], upperRest[i], upperBasis[i], sector, upper, GustStrength * wave * 3);
                Apply(hemPanels[i], hemRest[i], hemBasis[i], sector, hem, GustStrength * delayed * 5);
            }
        }
        private static void Apply(Transform panel, Quaternion rest, Quaternion basis, float sector, float angle, float twist)
        {
            // Blender radial (x,y) maps to Unity (x,-z).
            var axis = new Vector3(-Mathf.Sin(sector), 0, -Mathf.Cos(sector));
            var delta = Quaternion.AngleAxis(angle, axis) * Quaternion.AngleAxis(twist, Vector3.up);
            panel.localRotation = rest * Quaternion.Inverse(basis) * delta * basis;
        }
        private void OnDisable()
        {
            if (!initialized) return;
            for (int i = 0; i < upperPanels.Length; i++) { upperPanels[i].localRotation = upperRest[i]; hemPanels[i].localRotation = hemRest[i]; }
        }
    }
}
