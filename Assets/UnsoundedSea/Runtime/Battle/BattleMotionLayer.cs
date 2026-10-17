using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnsoundedSea.Runtime
{
    internal sealed class BattleMotionLayer : VisualElement
    {
        internal enum Cue { Strike, Shot, Guard, Ward, Insight, Heal, Impact, Progress }
        sealed class Burst { public Vector2 point; public Cue cue; public float age, duration; }
        sealed class Tween { public VisualElement view; public float age, duration; public Action<float> step; public Action done; }
        readonly List<Burst> pulses = new List<Burst>();
        readonly List<Tween> tweens = new List<Tween>();
        public Vector2 AimFrom, AimTo;
        public bool Aiming, ValidTarget;
        bool wasAiming;
        public int ActiveCount => pulses.Count + tweens.Count;
        public static readonly Color Brass = new Color32(230, 198, 132, 255);
        public static readonly Color Teal = new Color32(136, 206, 191, 255);

        public BattleMotionLayer()
        {
            name = "battle-motion"; pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.left = style.right = style.top = style.bottom = 0;
            generateVisualContent += Draw;
        }
        public void Animate(VisualElement view, float duration, Action<float> step, Action done = null, float delay = 0)
        {
            tweens.Add(new Tween { view = view, duration = duration, age = -delay, step = step, done = done });
            step(0);
        }
        public void ClearMotion()
        {
            tweens.Clear(); pulses.Clear(); Clear(); Aiming = false;
        }
        public void Tick(float dt)
        {
            bool hadPulses = pulses.Count > 0;
            for (int i = tweens.Count - 1; i >= 0; i--)
            {
                var tween = tweens[i]; tween.age += dt;
                if (tween.age < 0) continue;
                float t = Mathf.Clamp01(tween.age / tween.duration);
                tween.step(t);
                if (t >= 1) { tweens.RemoveAt(i); tween.done?.Invoke(); }
            }
            for (int i = pulses.Count - 1; i >= 0; i--)
            {
                pulses[i].age += dt;
                if (pulses[i].age >= pulses[i].duration) pulses.RemoveAt(i);
            }
            if (Aiming || wasAiming || hadPulses) MarkDirtyRepaint();
            wasAiming = Aiming;
        }
        public void Pulse(Vector2 at, Cue cue, string text = null, float delay = 0)
        {
            pulses.Add(new Burst { point = at, cue = cue, age = -delay, duration = GameSettings.ReducedMotion ? .2f : .55f });
            if (string.IsNullOrEmpty(text)) return;
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("combat-float");
            label.style.color = cue == Cue.Strike || cue == Cue.Shot || cue == Cue.Impact ? (Color)new Color32(255, 197, 166, 255) : Teal;
            Add(label);
            Animate(label, .85f, t =>
            {
                Place(label, at + new Vector2(-70, -48 - (GameSettings.ReducedMotion ? 0 : UiMotion.Ease(t) * 38)));
                label.style.opacity = t < .6f ? 1 : (1 - t) / .4f;
            }, label.RemoveFromHierarchy, delay);
        }
        public void Banner(string title)
        {
            var label = new Label(title) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("combat-banner"); Add(label);
            Animate(label, 1.05f, t =>
            {
                label.style.opacity = Mathf.Min(t / .16f, (1 - t) / .3f, 1);
                label.style.translate = new Translate(0, GameSettings.ReducedMotion ? 0 : (1 - UiMotion.Ease(t)) * 16);
            }, label.RemoveFromHierarchy);
        }
        public static void Place(VisualElement view, Vector2 at) { view.style.left = at.x; view.style.top = at.y; }
        public static void Scale(VisualElement view, float value) => view.style.scale = new Scale(new Vector3(value, value, 1));

        void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            void Line(Vector2 a, Vector2 b) { p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke(); }
            void Ring(Vector2 at, float radius, float from = 0, float to = 360)
            { p.BeginPath(); p.Arc(at, radius, from, to); p.Stroke(); }
            if (Aiming)
            {
                p.strokeColor = ValidTarget ? Teal : Brass; p.lineWidth = 2.4f;
                var bend = Vector2.Lerp(AimFrom, AimTo, .45f) + Vector2.up * -65;
                p.BeginPath(); p.MoveTo(AimFrom); p.QuadraticCurveTo(bend, AimTo); p.Stroke();
                Ring(AimTo, ValidTarget ? 20 : 12);
                Line(AimTo + new Vector2(-6, -6), AimTo); Line(AimTo + new Vector2(6, -6), AimTo);
            }
            foreach (var pulse in pulses)
            {
                if (pulse.age < 0) continue;
                var t = Mathf.Clamp01(pulse.age / pulse.duration);
                var a = pulse.point;
                var color = pulse.cue == Cue.Strike || pulse.cue == Cue.Shot || pulse.cue == Cue.Impact ? new Color(1, .77f, .57f) : Teal;
                color.a = (1 - t) * .9f; p.strokeColor = color;
                p.lineWidth = GameSettings.ReducedMotion ? 2 : Mathf.Lerp(5, 1, t);
                var size = GameSettings.ReducedMotion ? 32 : Mathf.Lerp(18, 66, UiMotion.Ease(t));
                if (pulse.cue == Cue.Shot)
                {
                    if (!GameSettings.ReducedMotion && t < .42f)
                    {
                        var muzzle = new Vector2(contentRect.width * .48f, contentRect.height - 170);
                        var head = Vector2.Lerp(muzzle, a, Mathf.Clamp01(t / .24f));
                        var tail = Vector2.Lerp(muzzle, a, Mathf.Clamp01((t - .12f) / .24f));
                        Line(tail, head);
                    }
                    Ring(a, size * .5f); Ring(a, size * .28f);
                }
                else if (pulse.cue == Cue.Strike)
                {
                    Line(a + new Vector2(-size, size * .6f), a + new Vector2(size, -size * .6f));
                    Line(a + new Vector2(-size * .5f, -size * .5f), a + new Vector2(size * .5f, size * .5f));
                    Ring(a, size * .5f, 10, 150);
                }
                else if (pulse.cue == Cue.Guard || pulse.cue == Cue.Ward)
                {
                    // Thin engraving around the resource display, not a magic bubble over the character.
                    p.BeginPath(); p.MoveTo(a + new Vector2(-size * .65f, -size * .6f));
                    p.LineTo(a + new Vector2(size * .65f, -size * .6f)); p.LineTo(a + new Vector2(size * .5f, size * .2f));
                    p.LineTo(a + new Vector2(0, size * .65f)); p.LineTo(a + new Vector2(-size * .5f, size * .2f)); p.ClosePath(); p.Stroke();
                    Ring(a, size, 200, 340);
                }
                else { Ring(a, size); Ring(a, size * .65f, 15, 160); }
                if (!GameSettings.ReducedMotion)
                    for (int i = 0; i < 8; i++)
                    {
                        var direction = new Vector2(Mathf.Cos(i * Mathf.PI / 4 + .2f), Mathf.Sin(i * Mathf.PI / 4 + .2f));
                        Line(a + direction * size, a + direction * (size + 9 * (1 - t)));
                    }
            }
        }
    }
}
