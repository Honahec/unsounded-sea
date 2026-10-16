using System;
using UnityEngine;

namespace UnsoundedSea.Runtime
{
    public sealed class VoyageSailingPresentation : MonoBehaviour
    {
        private const float PassageDuration = 3.2f;
        private const float FadeFraction = 0.12f;

        [SerializeField] private Camera voyageCamera;
        [SerializeField] private Transform seaStage;

        private Vector3 cameraPosition;
        private Quaternion cameraRotation;
        private Vector3 stagePosition;
        private Quaternion stageRotation;
        private Vector3 forward;
        private Vector3 right;
        private float lateralHeading;
        private float progress;
        private float transitionOpacity;
        private bool reducedMotion;

        public bool IsPlaying { get; private set; }
        public float Duration => PassageDuration;
        public float TransitionOpacity => transitionOpacity;

        public void Begin(Vector2 heading, bool reducedMotion)
        {
            if (IsPlaying) return;
            if (voyageCamera == null)
                throw new InvalidOperationException("Assign the voyageCamera reference before beginning a sailing passage.");
            if (seaStage == null)
                throw new InvalidOperationException("Assign the seaStage reference before beginning a sailing passage.");
            if (seaStage == voyageCamera.transform || voyageCamera.transform.IsChildOf(seaStage))
                throw new InvalidOperationException("seaStage must be a separate world-stage root and must not contain voyageCamera.");

            var cameraTransform = voyageCamera.transform;
            cameraPosition = cameraTransform.position;
            cameraRotation = cameraTransform.rotation;
            stagePosition = seaStage.position;
            stageRotation = seaStage.rotation;

            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(seaStage.forward, Vector3.up);
            forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
            right = Vector3.Cross(Vector3.up, forward);
            lateralHeading = Vector2.ClampMagnitude(heading, 1f).x;
            this.reducedMotion = reducedMotion;
            progress = 0f;
            transitionOpacity = 1f;
            IsPlaying = true;
            ApplyProgress();
        }

        public void SetProgress(float value)
        {
            if (!IsPlaying) return;
            progress = float.IsNaN(value) ? 0f : Mathf.Clamp01(value);
            UpdateTransitionOpacity();
            ApplyProgress();
        }

        public void Finish()
        {
            if (!IsPlaying) return;
            RestoreBaseline();
            IsPlaying = false;
        }

        private void UpdateTransitionOpacity()
        {
            if (progress < FadeFraction)
                transitionOpacity = 1f - Mathf.SmoothStep(0f, 1f, progress / FadeFraction);
            else if (progress > 1f - FadeFraction)
                transitionOpacity = Mathf.SmoothStep(0f, 1f, (progress - (1f - FadeFraction)) / FadeFraction);
            else
                transitionOpacity = 0f;
        }

        private void ApplyProgress()
        {
            var eased = Mathf.SmoothStep(0f, 1f, progress);
            var lateral = lateralHeading * eased;
            var cameraTransform = voyageCamera.transform;
            var forwardDistance = reducedMotion ? 0f : 4f;
            cameraTransform.position = cameraPosition + forward * (forwardDistance * eased) + right * (reducedMotion ? 0f : 0.45f * lateral);
            seaStage.position = reducedMotion ? stagePosition : stagePosition - forward * (1.6f * eased) - right * (0.25f * lateral);

            if (reducedMotion)
            {
                cameraTransform.rotation = cameraRotation;
                seaStage.rotation = stageRotation;
                return;
            }

            var passageArc = Mathf.Sin(progress * Mathf.PI);
            cameraTransform.position += Vector3.up * (0.12f * Mathf.Sin(progress * Mathf.PI * 2f));
            cameraTransform.rotation = cameraRotation * Quaternion.Euler(
                0.35f * passageArc,
                0f,
                (0.45f + 0.35f * lateralHeading) * passageArc);
            seaStage.rotation = stageRotation;
        }

        private void RestoreBaseline()
        {
            if (voyageCamera != null)
            {
                voyageCamera.transform.position = cameraPosition;
                voyageCamera.transform.rotation = cameraRotation;
            }
            if (seaStage != null)
            {
                seaStage.position = stagePosition;
                seaStage.rotation = stageRotation;
            }
        }

        private void OnDisable()
        {
            if (!IsPlaying) return;
            RestoreBaseline();
            IsPlaying = false;
            transitionOpacity = 0f;
        }

        private void OnDestroy()
        {
            if (!IsPlaying) return;
            RestoreBaseline();
            IsPlaying = false;
            transitionOpacity = 0f;
        }
    }
}
