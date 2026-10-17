using UnityEngine;
using System;
using System.Linq;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    /// <summary>Identifies a world-space encounter target consumed by the battle HUD.</summary>
    public sealed class BossTargetAnchor : MonoBehaviour
    {
        public string targetId;
        public MechanismState PresentedState { get; private set; }
        private BattleDirector director;
        private Surface[] surfaces = Array.Empty<Surface>();
        private Transform[] pipes = Array.Empty<Transform>();
        private Vector3[] pipePositions = Array.Empty<Vector3>();
        private Color targetTint = Color.white;
        private float closure;
        private sealed class Surface
        {
            public Renderer renderer;
            public int index;
            public Color color;
            public MaterialPropertyBlock original, current;
        }

        public void Bind(BattleView view, BattleDirector owner = null)
        {
            if (view.BossKind != BossEncounterKind.Choirmaster && view.BossKind != BossEncounterKind.Surveyor && view.BossKind != BossEncounterKind.FlagBearer) return;
            director = owner;
            surfaces = GetComponentsInChildren<Renderer>().SelectMany(renderer => renderer.sharedMaterials.Select((material, index) =>
            {
                var original = new MaterialPropertyBlock(); renderer.GetPropertyBlock(original, index);
                var current = new MaterialPropertyBlock(); renderer.GetPropertyBlock(current, index);
                return new Surface { renderer = renderer, index = index, original = original, current = current,
                    color = material != null && material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white };
            })).ToArray();
            pipes = GetComponentsInChildren<Transform>().Where(t => t.name == "Resonance pipe").ToArray();
            pipePositions = pipes.Select(t => t.localPosition).ToArray();
            Present(view); Tick(0, true);
        }
        public void Present(BattleView view)
        {
            var state = view.EncounterTargets.FirstOrDefault(t => t.Id == targetId);
            if (state == null) return;
            PresentedState = state.State;
            targetTint = state.State == MechanismState.Open ? new Color(.55f, 1.4f, 1.15f) :
                state.State == MechanismState.Disabled ? new Color(1.2f, .45f, .3f) :
                state.State == MechanismState.Measured ? new Color(.65f, 1.1f, 1.2f) :
                state.IsCurrent ? new Color(1.55f, 1.3f, .7f) : Color.white;
        }
        public void Tick(float delta, bool immediate = false)
        {
            closure = immediate || GameSettings.ReducedMotion ? (PresentedState == MechanismState.Open ? 1 : 0) :
                Mathf.MoveTowards(closure, PresentedState == MechanismState.Open ? 1 : 0, delta * 3);
            foreach (var surface in surfaces)
            {
                if (surface.renderer == null) continue;
                var color = surface.color * targetTint;
                surface.current.SetColor("_BaseColor", color);
                surface.current.SetColor("_Color", color);
                surface.renderer.SetPropertyBlock(surface.current, surface.index);
            }
            for (var i = 0; i < pipes.Length; i++)
                pipes[i].localPosition = pipePositions[i] + Vector3.down * (.35f * closure);
        }
        private void Update()
        {
            if (director != null && director.Paused) return;
            Tick(Time.unscaledDeltaTime);
        }
        private void OnDisable() => RestorePresentation();
        public void RestorePresentation()
        {
            foreach (var surface in surfaces)
                if (surface.renderer != null) surface.renderer.SetPropertyBlock(surface.original, surface.index);
            for (var i = 0; i < pipes.Length; i++) if (pipes[i] != null) pipes[i].localPosition = pipePositions[i];
        }
    }
}
