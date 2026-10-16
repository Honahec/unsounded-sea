using System.Collections.Generic;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    public sealed class BattleStage : MonoBehaviour
    {
        public Camera battleCamera;
        public Transform vessel;
        public Transform[] actorAnchors;
        public Material selectionMaterial;
        public Renderer ocean;
        public BattleWater water;
        public AudioSource ambience;
        public AudioSource feedback;
        public AudioClip sealCue, impactCue, sanityCue;
        public Color fogColor = new Color(0.055f, 0.1f, 0.12f);
        public float fogDensity = 0.023f;
        public float vesselHeave = 0.06f, vesselPitch = 0.35f, vesselRoll = 0.7f, cameraShake = 0.035f;
        public IReadOnlyList<BattleActorView> Actors => actors;
        private readonly List<BattleActorView> actors = new List<BattleActorView>();
        private BattleDirector director;
        private Vector3 shipPosition, cameraPosition;
        private Quaternion shipRotation;
        private float age, shake;
        private bool previousFog;
        private Color previousColor;
        private FogMode previousMode;
        private float previousDensity;
        private MaterialPropertyBlock oceanProperties;

        private void OnEnable()
        {
            previousFog = RenderSettings.fog; previousColor = RenderSettings.fogColor;
            previousDensity = RenderSettings.fogDensity; previousMode = RenderSettings.fogMode;
            RenderSettings.fog = true; RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = fogDensity;
        }
        public void Bind(BattleDirector owner)
        {
            director = owner; shipPosition = vessel.localPosition; shipRotation = vessel.localRotation;
            cameraPosition = battleCamera.transform.localPosition;
            oceanProperties = new MaterialPropertyBlock();
            var view = owner.View;
            for (var i = 0; i < view.Enemies.Count; i++)
            {
                var actor = new GameObject(view.Enemies[i].Name).AddComponent<BattleActorView>();
                actor.transform.SetParent(actorAnchors[i], false);
                actor.Bind(owner, view.Enemies[i].Id, owner.scenario.ActorPrefab(i), selectionMaterial);
                actors.Add(actor);
            }
            if (water != null) water.Bind(battleCamera);
            owner.Resolved += Present; RefreshSelection();
        }
        public void RefreshSelection() { foreach (var actor in actors) actor.Select(actor.EnemyId == director.SelectedEnemy); }
        private void Present(BattleView before, BattleView after, BattleCommand command)
        {
            for (var i = 0; i < actors.Count; i++)
                actors[i].Feedback(after.Enemies[i].Health < before.Enemies[i].Health,
                    command.kind == CommandKind.EndTurn && before.Enemies[i].Health > 0, after.Enemies[i].Health > 0);
            if (after.Health < before.Health || after.Sanity < before.Sanity)
            {
                shake = 1;
                var cue = after.Sanity < before.Sanity && sanityCue != null ? sanityCue : impactCue;
                if (feedback != null && cue != null) feedback.PlayOneShot(cue);
            }
            if (after.ObjectiveProgress > before.ObjectiveProgress && feedback != null && sealCue != null) feedback.PlayOneShot(sealCue);
            RefreshSelection();
        }
        private void Update()
        {
            if (director == null) return;
            if (ambience != null)
            {
                if (director.Paused && ambience.isPlaying) ambience.Pause();
                else if (!director.Paused && !ambience.isPlaying) ambience.UnPause();
            }
            if (feedback != null)
            {
                if (director.Paused && feedback.isPlaying) feedback.Pause();
                else if (!director.Paused && !feedback.isPlaying) feedback.UnPause();
            }
            if (director.Paused)
            {
                if (water != null) water.Tick(0, true, GameSettings.ReducedMotion, Vector4.zero);
                return;
            }
            age += Time.unscaledDeltaTime;
            var reduced = GameSettings.ReducedMotion;
            vessel.localPosition = shipPosition + Vector3.up * (reduced ? 0 : Mathf.Sin(age * 0.55f) * vesselHeave);
            vessel.localRotation = shipRotation * Quaternion.Euler(reduced ? 0 : Mathf.Sin(age * 0.31f) * vesselPitch, 0, reduced ? 0 : Mathf.Sin(age * 0.47f) * vesselRoll);
            shake = Mathf.MoveTowards(shake, 0, Time.unscaledDeltaTime * 1.8f);
            battleCamera.transform.localPosition = cameraPosition + (reduced ? Vector3.zero : new Vector3(Mathf.Sin(age * 41), Mathf.Cos(age * 37), 0) * (shake * cameraShake));
            if (ocean != null)
            {
                var threat = Vector4.zero;
                foreach (var actor in actors)
                    if (actor.IsSeaActor && actor.Alive) { var p = actor.transform.position; threat = new Vector4(p.x, p.z, actor.WaterRadius, reduced ? 0 : shake); break; }
                if (water != null) water.Tick(Time.unscaledDeltaTime, false, reduced, threat);
                else
                {
                    oceanProperties.SetFloat("_SeaTime", reduced ? age * 0.25f : age);
                    oceanProperties.SetVector("_Threat", threat);
                    ocean.SetPropertyBlock(oceanProperties);
                }
            }
        }
        private void OnDisable()
        {
            if (director != null) director.Resolved -= Present;
            RenderSettings.fog = previousFog; RenderSettings.fogColor = previousColor;
            RenderSettings.fogDensity = previousDensity; RenderSettings.fogMode = previousMode;
        }
    }
}
