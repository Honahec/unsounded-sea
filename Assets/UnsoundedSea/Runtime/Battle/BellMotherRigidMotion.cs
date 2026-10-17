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

        public string ActiveAction { get; private set; } = "Idle";
        public float ActionElapsed { get; private set; }
        public float ActionImpact { get; private set; }

        private Vector3 bodyPosition, crownPosition;
        private Quaternion bodyRotation, crownRotation;
        private Vector3[] tentaclePositions;
        private Quaternion[] tentacleRotations;
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
            const float telegraph = 0.22f;
            const float strike = 0.28f;
            const float recover = 0.40f;
            var t = ActionElapsed;
            if (t < telegraph)
            {
                var u = Smooth(t / telegraph);
                ApplyPose(Mathf.Lerp(0, -5.5f, u), Mathf.Lerp(0, -2.5f, u), Mathf.Lerp(0, -2f, u),
                    Mathf.Lerp(0, -10f, u), Mathf.Lerp(0, -5f, u), Mathf.Lerp(0, -3f, u), Mathf.Lerp(0, -0.12f, u));
                return;
            }
            if (t < telegraph + strike)
            {
                var u = Smooth((t - telegraph) / strike);
                var snap = Mathf.Sin(u * Mathf.PI);
                ActionImpact = Mathf.Max(ActionImpact, snap);
                ApplyPose(Mathf.Lerp(-5.5f, 9.5f, u), Mathf.Lerp(-2.5f, 4f, u), Mathf.Lerp(-2f, 3f, u),
                    Mathf.Lerp(-10f, 15f, u), Mathf.Lerp(-5f, 10f, u), Mathf.Lerp(-3f, 8f, u), Mathf.Lerp(-0.12f, 0.28f, u));
                return;
            }
            if (t < telegraph + strike + recover)
            {
                var u = Smooth((t - telegraph - strike) / recover);
                ApplyPose(Mathf.Lerp(9.5f, 0, u), Mathf.Lerp(4f, 0, u), Mathf.Lerp(3f, 0, u),
                    Mathf.Lerp(15f, 0, u), Mathf.Lerp(10f, 0, u), Mathf.Lerp(8f, 0, u), Mathf.Lerp(0.28f, 0, u));
                return;
            }
            ResetIdle();
        }

        private void UpdateHit()
        {
            const float duration = 0.34f;
            if (ActionElapsed >= duration) { ResetIdle(); return; }
            var u = ActionElapsed / duration;
            var shake = Mathf.Sin(u * Mathf.PI * 3f) * (1f - u) * 5f;
            ApplyPose(shake, 0, -shake * 0.6f, shake * 1.4f, -shake, shake * 0.5f, 0);
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

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
