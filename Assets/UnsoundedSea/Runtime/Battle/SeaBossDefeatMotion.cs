using System;
using System.Collections.Generic;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    /// <summary>Pose-preserving loss of support, followed by water drag. No damage or rule state.</summary>
    public sealed class SeaBossDefeatMotion
    {
        readonly Transform root;
        readonly BossEncounterKind kind;
        readonly bool withdrawal;
        readonly Vector3 position, scale, pivot, contactPoint;
        readonly Quaternion rotation;
        readonly float height, depth;
        readonly Func<Vector3, float> surface;
        readonly Action<Vector3, float> impact;
        readonly List<Limb> limbs = new List<Limb>();
        bool contacted;
        public float Elapsed { get; private set; }
        // Keep the final water displacement on screen long enough to reach the foreground ship.
        public float Duration => withdrawal ? 5.5f : kind == BossEncounterKind.FlagBearer ? 6.5f : 10.5f;
        public bool Busy => Elapsed < Duration;
        public float Progress => Mathf.Clamp01(Elapsed / Duration);
        public float Load => withdrawal ? 0 : Smooth((Elapsed - .7f) / 2.6f);
        sealed class Limb
        {
            public Transform bone;
            public Vector3 position, scale, euler, anchor, rootPoint;
            public Quaternion rotation, basis;
            public float delay;
            public bool attached;
        }

        public SeaBossDefeatMotion(Transform root, BossEncounterKind kind, bool withdrawal,
            Func<Vector3, float> surface, Action<Vector3, float> impact)
        {
            this.root = root; this.kind = kind; this.withdrawal = withdrawal;
            this.surface = surface; this.impact = impact;
            position = root.localPosition; rotation = root.localRotation; scale = root.localScale;
            var bounds = BattleActorView.MeasureLocalBounds(root);
            height = Mathf.Max(1, root.TransformVector(Vector3.up * bounds.size.y).magnitude);
            pivot = bounds.center - Vector3.up * bounds.size.y * .28f;
            contactPoint = bounds.center;
            var top = root.TransformPoint(bounds.center + Vector3.up * bounds.extents.y);
            depth = Mathf.Max(height * 1.15f, top.y - (surface?.Invoke(top) ?? 0) + height * .35f);
        }

        public void AddJoint(GiantJointMotion joint)
        {
            var name = joint.bone.name;
            var euler = Vector3.zero;
            if (name == "GB_Head") euler = new Vector3(9, 0, 3);
            if (name.Contains("Fin_")) euler = new Vector3(0, 0, name.EndsWith("L") ? -12 : 12);
            if (name.Contains("Tail")) euler = new Vector3(7, 0, 4);
            if (name.Contains("Pendulum")) euler = new Vector3(5, 0, -8);
            if (name.Contains("Curtain")) euler = new Vector3(5, 0, name.EndsWith("L") ? -6 : 6);
            if (kind == BossEncounterKind.FlagBearer)
            {
                if (name == "GB_Body") euler = new Vector3(24, 0, -10);
                if (name == "GB_Head") euler = new Vector3(14, 0, 3);
                if (name == "GB_Pole") euler = new Vector3(12, 0, 2);
                if (name == "GB_FreeArm") euler = new Vector3(12, 0, -4);
                if (name.Contains("Flag")) euler = new Vector3(16, 0, 3);
            }
            limbs.Add(new Limb { bone = joint.bone, position = joint.bone.localPosition,
                rotation = joint.bone.localRotation, scale = joint.bone.localScale,
                basis = joint.basis, euler = euler, delay = .2f + joint.lag * 2 });
        }

        public void AddTentacle(Transform part, Vector3 attachment, Vector3 tip, int index)
        {
            var outward = part.localRotation * Vector3.Scale(tip - attachment, part.localScale);
            limbs.Add(new Limb { bone = part, position = part.localPosition, rotation = part.localRotation,
                scale = part.localScale, rootPoint = attachment,
                anchor = part.localPosition + part.localRotation * Vector3.Scale(attachment, part.localScale),
                // Sag the outside end; keep the captured inner attachment fixed.
                euler = Vector3.Cross(outward, Vector3.up).normalized * -7,
                basis = Quaternion.identity, attached = true, delay = .15f + index * .11f });
        }

        public void Tick(float dt, bool reduced)
        {
            if (dt <= 0 || !Busy) return;
            Elapsed = Mathf.Min(Duration, Elapsed + Mathf.Min(dt, .1f));
            float t = Elapsed;
            // Very little displacement at first, acceleration into collapse, then sustained drag.
            float sink = withdrawal ? Smooth(t / Duration) : t < 3.2f
                ? .36f * Mathf.Pow(Mathf.Clamp01((t - .65f) / 2.55f), 2.2f)
                : .36f + .64f * (1 - Mathf.Pow(1 - Mathf.Clamp01((t - 3.2f) / 4.8f), 2));
            float lean = withdrawal ? 0 : Smooth((t - .4f) / 2.7f);
            float amount = reduced ? .15f : 1;
            var euler = kind == BossEncounterKind.FlagBearer ? Vector3.zero :
                kind == BossEncounterKind.Choirmaster ? new Vector3(16, 0, 38) : new Vector3(8, 0, -22);
            var targetRotation = rotation * Quaternion.Euler(euler * (lean * amount));
            var pivotOffset = rotation * Vector3.Scale(pivot, scale) - targetRotation * Vector3.Scale(pivot, scale);
            var down = Vector3.down * (kind == BossEncounterKind.FlagBearer ? 0 : depth * sink);
            root.localPosition = position + pivotOffset + (root.parent == null ? down : root.parent.InverseTransformVector(down));
            root.localRotation = targetRotation;
            foreach (var limb in limbs)
            {
                float slack = withdrawal ? 0 : Smooth((t - .65f - limb.delay) / 3.8f) * amount;
                var delta = Quaternion.Euler(limb.euler * slack);
                limb.bone.localRotation = limb.attached ? delta * limb.rotation :
                    limb.rotation * Quaternion.Inverse(limb.basis) * delta * limb.basis;
                limb.bone.localPosition = limb.attached ? limb.anchor -
                    limb.bone.localRotation * Vector3.Scale(limb.rootPoint, limb.scale) : limb.position;
                limb.bone.localScale = limb.scale;
            }
            var point = root.TransformPoint(contactPoint);
            if (!contacted && kind != BossEncounterKind.FlagBearer && surface != null && t >= 2.2f && point.y <= surface(point))
            {
                contacted = true; point.y = surface(point);
                impact?.Invoke(point, Mathf.Clamp(height * (withdrawal ? .07f : .34f), 4, 80));
            }
        }
        static float Smooth(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
    }
}
