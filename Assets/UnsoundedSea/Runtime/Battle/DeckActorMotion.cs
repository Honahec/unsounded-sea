using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnsoundedSea.Runtime
{
    public enum DeckMotionStyle { Sailor, Skirmisher, Heavy, Ritual, Veiled, Uncanny, Gunner }

    /// <summary>Small, additive poses over the curated skin's bind pose. Never advances rules.</summary>
    public sealed class DeckActorMotion : MonoBehaviour
    {
        public DeckMotionStyle style;
        [Range(0.5f, 1.5f)] public float amplitude = 1;
        [Min(0.4f)] public float attackDuration = 1.45f;
        [Min(0.3f)] public float hitDuration = 0.75f;
        // Zero endpoint tangents give a held anticipation, fast release and a soft overshoot.
        public AnimationCurve strike = Curve(0, 0, .23f, -.48f, .37f, 1, .45f, .82f, .70f, -.10f, 1, 0);
        public AnimationCurve followThrough = Curve(0, 0, .28f, -.3f, .43f, 1, .55f, .65f, .79f, -.08f, 1, 0);
        public AnimationCurve recoil = Curve(0, 0, .11f, 1, .28f, .68f, .57f, -.14f, 1, 0);
        private readonly List<Joint> joints = new List<Joint>();
        private float age, phase, actionTime = -1, hitTime = -1, delay, strength, hitWeight, death;
        private bool ritual, observing, alive = true, initialized;
        private PrayerSkirtMotion skirt;
        public bool Busy => alive && (actionTime >= 0 || hitTime >= 0);
        public bool Attacking => alive && actionTime >= 0;
        public float ActionImpact { get; private set; }
        public float Age => age;
        public int DrivenJointCount => joints.Count;
        private sealed class Joint
        {
            public Transform bone;
            public Quaternion rest, basis;
            public int role;
        }

        public static AnimationCurve Curve(params float[] pairs)
        {
            var keys = new Keyframe[pairs.Length / 2];
            for (var i = 0; i < keys.Length; i++) keys[i] = new Keyframe(pairs[i * 2], pairs[i * 2 + 1], 0, 0);
            return new AnimationCurve(keys);
        }

        public void Initialize(string identity)
        {
            if (initialized) return;
            initialized = true;
            skirt = GetComponent<PrayerSkirtMotion>();
            // Stable presentation phase; never touch UnityEngine.Random or the session RNG.
            uint hash = 2166136261;
            foreach (var c in identity ?? name) hash = (hash ^ c) * 16777619;
            phase = (hash % 1024) / 1024f * Mathf.PI * 2;
            foreach (var b in GetComponentsInChildren<Transform>(true))
            {
                var role = Role(b.name);
                if (role < 0) continue;
                joints.Add(new Joint { bone = b, rest = b.localRotation,
                    basis = Quaternion.Inverse(transform.rotation) * b.rotation, role = role });
            }
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var bounds = skin.localBounds;
                bounds.Expand(bounds.size * (skirt != null ? 1f : .45f));
                skin.localBounds = bounds;
            }
        }

        private static int Role(string n)
        {
            switch (n)
            {
                case "Spine": case "spine": return 0;
                case "Chest": case "chest": return 1;
                case "UpperChest": return 2;
                case "Neck": case "neck": return 3;
                case "Head": case "head": return 4;
                case "LeftShoulder": case "shoulder.L": return 5;
                case "RightShoulder": case "shoulder.R": return 6;
                case "LeftUpperArm": case "upper_arm.L": return 7;
                case "RightUpperArm": case "upper_arm.R": return 8;
                case "LeftLowerArm": case "forearm.L": return 9;
                case "RightLowerArm": case "forearm.R": return 10;
                default: return -1;
            }
        }

        public void PlayAttack(bool isRitual, bool isObservation, float startDelay = 0)
        {
            if (!alive) return;
            actionTime = 0; delay = Mathf.Max(0, startDelay); ritual = isRitual; observing = isObservation;
        }
        public void PlayHit()
        {
            if (!alive) return;
            // Add a new smooth impulse from the current pressure, rather than snapping back to zero.
            hitWeight = Mathf.Min(1.7f, 1 + Mathf.Abs(strength) * .45f);
            hitTime = 0;
        }
        public void SetAlive(bool value)
        {
            if (value && !alive) { death = 0; actionTime = hitTime = -1; strength = 0; }
            alive = value;
            if (!alive) actionTime = hitTime = -1;
        }

        public void Tick(float delta, bool paused, bool reduced)
        {
            if (!initialized) Initialize(name);
            if (paused) return;
            delta = Mathf.Max(0, delta);
            age += delta;
            if (actionTime >= 0)
            {
                actionTime += delta;
                if (actionTime >= delay + attackDuration) actionTime = -1;
            }
            if (hitTime >= 0)
            {
                hitTime += delta;
                if (hitTime >= hitDuration) hitTime = -1;
            }
            var targetHit = hitTime < 0 ? 0 : recoil.Evaluate(hitTime / hitDuration) * hitWeight;
            strength = Mathf.Lerp(strength, targetHit, 1 - Mathf.Exp(-delta * 45));
            if (Mathf.Abs(strength) < .0001f) strength = 0;
            death = Mathf.MoveTowards(death, alive ? 0 : 1, delta * 1.4f);
            var t = actionTime < delay ? -1 : (actionTime - delay) / attackDuration;
            var a = t < 0 ? 0 : strike.Evaluate(t);
            var f = t < 0 ? 0 : followThrough.Evaluate(t);
            ActionImpact = t < .32f || t > .62f || observing ? 0 : Mathf.Max(0, f);
            var speed = style == DeckMotionStyle.Heavy ? 1.05f : style == DeckMotionStyle.Uncanny ? .87f : 1.65f;
            var breath = Mathf.Sin(age * speed + phase);
            var sway = Mathf.Sin(age * .67f + phase * 1.3f);
            var lag = Mathf.Sin(age * speed + phase - .55f);
            var weight = reduced ? 0 : amplitude * (1 - death);
            var arm = style == DeckMotionStyle.Veiled ? .035f : style == DeckMotionStyle.Ritual ? .28f : .65f;
            float twist = style == DeckMotionStyle.Skirmisher ? 11 : style == DeckMotionStyle.Heavy ? 3 : 6;
            float lean = style == DeckMotionStyle.Heavy ? 8 : style == DeckMotionStyle.Uncanny ? 10 : 7;
            if (ritual) { lean *= -.55f; twist *= .25f; arm *= .55f; }
            if (observing) { a *= .2f; f *= .2f; }
            foreach (var joint in joints)
            {
                Vector3 angles;
                switch (joint.role)
                {
                    case 0: angles = new Vector3(breath * .65f + a * lean * .45f - strength * 3.5f, a * twist * .3f, sway * .65f + strength * 1.2f); break;
                    case 1: angles = new Vector3(lag * .95f + f * lean * .55f - strength * 5, a * twist * .7f, -sway * .35f - strength * 2); break;
                    case 2: angles = new Vector3(lag * .25f + f * 1.2f, 0, 0); break;
                    case 3: angles = new Vector3(-lag * .55f - f * 2 + strength * 2, -a * 2, sway * .3f); break;
                    case 4: angles = new Vector3(-breath * .35f - f + strength * 2.5f, Mathf.Sin(age * .43f + phase) * 1.4f, -sway * .4f); break;
                    case 5: angles = new Vector3(-f * 3 * arm, f * 2 * arm, lag * .4f * arm); break;
                    case 6: angles = new Vector3(-a * 5 * arm, -f * 2 * arm, -breath * .5f * arm); break;
                    case 7: angles = new Vector3((lag * 1.2f - f * 12 + strength * 3) * arm, 0, -f * 3 * arm); break;
                    case 8: angles = new Vector3((breath - a * 22 + strength * 4) * arm, -a * 5 * arm, f * 4 * arm); break;
                    case 9: angles = new Vector3((-lag - f * 8) * arm, 0, 0); break;
                    default: angles = new Vector3((-breath - f * 16) * arm, 0, 0); break;
                }
                if (style == DeckMotionStyle.Gunner && joint.role >= 5) angles *= .4f;
                angles *= weight;
                if (joint.role < 2) angles.x += death * (reduced ? 4 : 15);
                // Axes are character-space, independent of the imported bone roll and anchor yaw.
                joint.bone.localRotation = joint.rest * Quaternion.Inverse(joint.basis) * Quaternion.Euler(angles) * joint.basis;
            }
            if (skirt != null) skirt.Tick(age, observing ? -1 : t, strength, reduced, alive);
        }
        private void OnDisable()
        {
            foreach (var joint in joints) if (joint.bone != null) joint.bone.localRotation = joint.rest;
        }
    }
}
