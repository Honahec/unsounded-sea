using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>
    /// Drives the BANG-separated Bell Mother as rigid presentation parts.
    /// The model has no full-body skinning, so the silhouette stays stable while
    /// the shell, crown and tentacles get readable battle motion.
    /// </summary>
    public sealed class BellMotherRigidMotion : MonoBehaviour
    {
        public Transform body;
        public Transform crown;
        public Transform[] tentacles;
        [Tooltip("Attachment points in each tentacle transform's local space, baked by BellMotherBangSetup.")]
        public Vector3[] tentacleRoots;
        [Tooltip("Outer end samples in each tentacle transform's local space.")]
        public Vector3[] tentacleTips;

        public string ActiveAction { get; private set; } = "Idle";
        public float ActionElapsed { get; private set; }
        public float ActionImpact { get; private set; }

        private Vector3 bodyPosition, crownPosition;
        private Quaternion bodyRotation, crownRotation;
        private Vector3[] tentaclePositions;
        private Quaternion[] tentacleRotations;
        private Vector3[] attackAnchors, attackAxes;
        private Quaternion[] attackRotations;
        private bool paused;
        private float idleTime;

        private enum Sequence
        {
            Idle,
            Attack,
            Hit,
            Death,
        }

        private Sequence sequence;

        private void Awake()
        {
            CaptureRestPose();
            ResetIdle();
        }

        public void CaptureRestPose()
        {
            if (body != null) { bodyPosition = body.localPosition; bodyRotation = body.localRotation; }
            if (crown != null) { crownPosition = crown.localPosition; crownRotation = crown.localRotation; }
            tentaclePositions = new Vector3[tentacles == null ? 0 : tentacles.Length];
            tentacleRotations = new Quaternion[tentacles == null ? 0 : tentacles.Length];
            for (var i = 0; i < tentaclePositions.Length; i++)
            {
                tentaclePositions[i] = tentacles[i].localPosition;
                tentacleRotations[i] = tentacles[i].localRotation;
            }
        }

        public void SetPaused(bool value) => paused = value;

        public void ResetIdle()
        {
            sequence = Sequence.Idle;
            ActiveAction = "Idle";
            ActionElapsed = 0;
            ActionImpact = 0;
            idleTime = 0;
            ApplyPose(0, 0, 0, 0, 0, 0, 0);
        }

        public void PlayAttack()
        {
            // Freeze the current attachment positions. The imported transform
            // origins are not anatomical joints, so rotate about the baked root
            // and compensate translation instead of lifting the whole part.
            if (tentacleRoots == null || tentacleTips == null || tentacles == null ||
                tentacleRoots.Length != tentacles.Length || tentacleTips.Length != tentacles.Length)
                return;
            attackAnchors = new Vector3[tentacles.Length];
            attackAxes = new Vector3[tentacles.Length];
            attackRotations = new Quaternion[tentacles.Length];
            for (var i = 0; i < tentacles.Length; i++)
            {
                var part = tentacles[i];
                attackRotations[i] = part.localRotation;
                attackAnchors[i] = part.localPosition + part.localRotation * Vector3.Scale(tentacleRoots[i], part.localScale);
                var outward = part.localRotation * Vector3.Scale(tentacleTips[i] - tentacleRoots[i], part.localScale);
                // Cross(outward, up) raises the outer end for every radial
                // direction, including mirrored left/right and rear tentacles.
                attackAxes[i] = Vector3.Cross(outward, Vector3.up).normalized;
            }
            sequence = Sequence.Attack;
            ActiveAction = "Attack";
            ActionElapsed = 0;
            ActionImpact = 0;
        }

        public void PlayHit()
        {
            sequence = Sequence.Hit;
            ActiveAction = "Hit";
            ActionElapsed = 0;
            ActionImpact = 0;
        }

        public void PlayDeath()
        {
            sequence = Sequence.Death;
            ActiveAction = "Death";
            ActionElapsed = 0;
            ActionImpact = 0;
        }

        private void Update()
        {
            if (paused) return;
            Tick(Time.unscaledDeltaTime);
        }

        public void Tick(float delta)
        {
            if (paused) return;
            delta = Mathf.Max(0, delta);
            ActionElapsed += delta;
            ActionImpact = Mathf.MoveTowards(ActionImpact, 0, delta * 3.5f);
            switch (sequence)
            {
                case Sequence.Idle: UpdateIdle(delta); break;
                case Sequence.Attack: UpdateAttack(); break;
                case Sequence.Hit: UpdateHit(); break;
                case Sequence.Death: UpdateDeath(); break;
            }
        }

        private void UpdateIdle(float delta)
        {
            idleTime += delta;
            var wave = Mathf.Sin(idleTime * 1.25f);
            ApplyPose(wave * 1.25f, Mathf.Sin(idleTime * 0.7f) * 0.8f,
                wave * 0.65f, wave * 2.4f, 0, 0, 0);
        }

        private void UpdateAttack()
        {
            const float raise = 0.32f;
            const float hold = 0.06f;
            const float strike = 0.18f;
            const float recover = 0.38f;
            const float raisedAngle = 48f;
            const float contactAngle = -8f;
            var t = ActionElapsed;
            if (t < raise)
                ApplyAttackPose(raisedAngle * Smooth(t / raise));
            else if (t < raise + hold)
                ApplyAttackPose(raisedAngle);
            else if (t < raise + hold + strike)
            {
                var u = (t - raise - hold) / strike;
                ApplyAttackPose(Mathf.Lerp(raisedAngle, contactAngle, u * u));
            }
            else if (t < raise + hold + strike + recover)
            {
                var elapsed = t - raise - hold - strike;
                ActionImpact = Mathf.Clamp01(1f - elapsed / 0.18f);
                ApplyAttackPose(Mathf.Lerp(contactAngle, 0, Smooth(elapsed / recover)));
            }
            else
            {
                ApplyAttackPose(0);
                sequence = Sequence.Idle;
                ActiveAction = "Idle";
                ActionElapsed = 0;
                ActionImpact = 0;
            }
        }

        private void UpdateHit()
        {
            const float duration = 0.30f;
            if (ActionElapsed >= duration) { ResetIdle(); return; }
            // A hit is a single heavy recoil with a controlled settle.  Avoid
            // sinusoidal shake: the rigid silhouette should absorb the blow,
            // rather than bounce like soft rubber.
            var u = Smooth(ActionElapsed / duration);
            var settle = 1f - u;
            ApplyPose(-2.8f * settle, 1.1f * settle, -1.4f * settle,
                -4.5f * settle, 1.5f * settle, -2.2f * settle, -0.055f * settle);
        }

        private void UpdateDeath()
        {
            var u = Mathf.Clamp01(ActionElapsed / 0.9f);
            var eased = Smooth(u);
            ApplyPose(Mathf.Lerp(0, 22f, eased), Mathf.Lerp(0, -8f, eased), Mathf.Lerp(0, -18f, eased),
                Mathf.Lerp(0, 30f, eased), Mathf.Lerp(0, -18f, eased), Mathf.Lerp(0, -12f, eased), Mathf.Lerp(0, -1.2f, eased));
        }

        private void ApplyPose(float bodyPitch, float bodyYaw, float bodyRoll,
            float crownPitch, float crownYaw, float crownRoll, float bodyLift)
        {
            if (tentacles == null) return;
            if (tentaclePositions == null || tentaclePositions.Length != tentacles.Length) CaptureRestPose();
            if (body != null)
            {
                body.localPosition = bodyPosition + Vector3.up * bodyLift;
                body.localRotation = bodyRotation * Quaternion.Euler(bodyPitch, bodyYaw, bodyRoll);
            }
            if (crown != null)
            {
                crown.localPosition = crownPosition + Vector3.up * (bodyLift * 0.7f);
                crown.localRotation = crownRotation * Quaternion.Euler(crownPitch, crownYaw, crownRoll);
            }
            for (var i = 0; i < tentacles.Length; i++)
            {
                if (tentacles[i] == null) continue;
                var phase = i * 0.82f;
                var wave = Mathf.Sin(idleTime * 1.8f + phase);
                var angle = Mathf.Lerp(bodyRoll, bodyRoll + wave * 2.2f, sequence == Sequence.Idle ? 0.4f : 0.16f);
                tentacles[i].localPosition = tentaclePositions[i] + Vector3.up * (bodyLift * 0.35f);
                tentacles[i].localRotation = tentacleRotations[i] * Quaternion.Euler(bodyPitch * 0.45f + wave * 2f, bodyYaw * 0.35f, angle);
            }
        }

        private void ApplyAttackPose(float angle)
        {
            for (var i = 0; i < tentacles.Length; i++)
            {
                var part = tentacles[i];
                var rotation = Quaternion.AngleAxis(angle, attackAxes[i]) * attackRotations[i];
                part.localRotation = rotation;
                part.localPosition = attackAnchors[i] - rotation * Vector3.Scale(tentacleRoots[i], part.localScale);
            }
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
