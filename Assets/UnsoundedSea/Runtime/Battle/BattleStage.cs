using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        public Material skybox;
        public Color fogColor = new Color(0.055f, 0.1f, 0.12f);
        public float fogDensity = 0.023f;
        public float vesselHeave = 0.06f, vesselPitch = 0.35f, vesselRoll = 0.7f, cameraShake = 0.035f;
        public bool vesselFollowsWater;
        public Vector2 vesselSampleExtents = new Vector2(2.5f, 7);
        public float maxWavePitch = 6, maxWaveRoll = 9, vesselYaw = 1.5f;
        public bool placeholderCombatMotion = true;
        public IReadOnlyList<BattleActorView> Actors => actors;
        private readonly List<BattleActorView> actors = new List<BattleActorView>();
        private readonly List<BellMotherRigidMotion> seaMotions = new List<BellMotherRigidMotion>();
        private readonly VesselWaveResponse vesselResponse = new VesselWaveResponse();
        public bool HasPendingSeaAttack => seaMotions.Exists(m => m != null && m.ActiveAction == "Attack") ||
            actors.Exists(a => a != null && (a.IsEmerging || a.GiantMotion != null && a.GiantMotion.Busy));
        private BattleDirector director;
        private Vector3 shipPosition, cameraPosition;
        private Quaternion shipRotation, cameraRotation;
        private float impactCameraLift;
        private float age, shake, combatSway, combatVelocity, combatTarget;
        public bool HasPendingDeckMotion => actors.Exists(a => a != null && a.isActiveAndEnabled && a.HasPendingDeckMotion);
        public bool DelaysHudUntilImpact { get; private set; }
        private Coroutine watcherSequence;
        private int vesselImpactSerial;
        public float CombatSway => combatSway;
        private bool previousFog;
        private Material previousSkybox;
        private Color previousColor;
        private FogMode previousMode;
        private float previousDensity;
        private MaterialPropertyBlock oceanProperties;

        private void OnEnable()
        {
            previousSkybox = RenderSettings.skybox;
            if (skybox != null) RenderSettings.skybox = skybox;
            previousFog = RenderSettings.fog; previousColor = RenderSettings.fogColor;
            previousDensity = RenderSettings.fogDensity; previousMode = RenderSettings.fogMode;
            RenderSettings.fog = true; RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = fogDensity;
        }
        public void Bind(BattleDirector owner)
        {
            director = owner; shipPosition = vessel.localPosition; shipRotation = vessel.localRotation;
            cameraPosition = battleCamera.transform.localPosition;
            cameraRotation = battleCamera.transform.localRotation;
            oceanProperties = new MaterialPropertyBlock();
            GameSettings.ApplyAudioSource(ambience, true);
            GameSettings.ApplyAudioSource(feedback, false);
            var view = owner.View;
            for (var i = 0; i < view.Enemies.Count; i++)
            {
                var actor = new GameObject(view.Enemies[i].Name).AddComponent<BattleActorView>();
                actor.transform.SetParent(actorAnchors[i], false);
                actor.Bind(owner, view.Enemies[i].Id, owner.scenario.ActorPrefab(i), selectionMaterial);
                if(actor.IsSeaActor&&water!=null)actor.ConfigureSeaEmergence(water.SurfaceHeight);
                actor.ConfigureMotion(placeholderCombatMotion);
                if (view.BossKind != BossEncounterKind.None)
                {
                    actor.Feedback(false, false, view.Enemies[i].Health > 0);
                    actor.gameObject.SetActive(view.Enemies[i].Health > 0);
                }
                actors.Add(actor);
                actor.BrokeSurface += OnBrokeSurface;
                if (actor.GiantMotion != null)
                {
                    actor.GiantMotion.BindEffects(water,vessel);
                    actor.GiantMotion.SurfaceReleased += OnGiantRelease;
                    actor.GiantMotion.VesselImpact += OnVesselImpact;
                }
                var motion = actor.GetComponentInChildren<BellMotherRigidMotion>();
                if (actor.IsSeaActor && motion != null && water != null)
                {
                    motion.SurfaceHeight = water.SurfaceHeight;
                    motion.SurfaceStruck += OnSurfaceStruck;
                    seaMotions.Add(motion);
                }
            }
            if (water != null) water.Bind(battleCamera);
            owner.Resolved += Present; RefreshSelection();
        }
        public void RefreshSelection() { foreach (var actor in actors) actor.Select(actor.EnemyId == director.SelectedEnemy); }
        private void Present(BattleView before, BattleView after, BattleCommand command)
        {
            var watcherTurn = command.kind == CommandKind.EndTurn && after.BossKind == BossEncounterKind.Watcher;
            for (var i = 0; i < actors.Count; i++)
            {
                if (after.BossKind != BossEncounterKind.None && actors[i].GiantMotion == null)
                    actors[i].gameObject.SetActive(after.Enemies[i].Health > 0);
                var action = command.kind == CommandKind.EndTurn
                    ? after.ResolvedEnemyActions.FirstOrDefault(x => x.EnemyId == actors[i].EnemyId) : null;
                var damaged = after.Enemies[i].Health < before.Enemies[i].Health;
                actors[i].Feedback(damaged, action != null && !watcherTurn, after.Enemies[i].Health > 0,
                    action != null && action.Channel == DamageChannel.Sanity, action != null && action.Amount == 0, i * .12f);
                if (actors[i].GiantMotion != null)
                {
                    if (after.Phase == BattlePhase.Victory && before.Phase != BattlePhase.Victory) actors[i].GiantMotion.Retire();
                    else if (!after.Enemies[i].Targetable && after.ObjectiveProgress > before.ObjectiveProgress)
                        actors[i].GiantMotion.PlayHit(.65f);
                }
                if (!actors[i].IsSeaActor && damaged) combatTarget = Mathf.Max(combatTarget, .65f);
            }
            DelaysHudUntilImpact = watcherTurn && after.ResolvedEnemyActions.Count > 0;
            if (DelaysHudUntilImpact)
            {
                if (watcherSequence != null) StopCoroutine(watcherSequence);
                watcherSequence = StartCoroutine(PresentWatcherActions(after.ResolvedEnemyActions));
            }
            else if (after.Health < before.Health || after.Sanity < before.Sanity) PlayPlayerImpact(after.Sanity < before.Sanity);
            if (after.ObjectiveProgress > before.ObjectiveProgress && feedback != null && sealCue != null) feedback.PlayOneShot(sealCue);
            RefreshSelection();
        }
        private void PlayPlayerImpact(bool sanity)
        {
            shake = placeholderCombatMotion ? 1 : 0;
            var cue = sanity && sanityCue != null ? sanityCue : impactCue;
            if (feedback != null && cue != null) feedback.PlayOneShot(cue);
        }
        private void OnVesselImpact()
        {
            vesselImpactSerial++;
        }
        private IEnumerator PresentWatcherActions(IReadOnlyList<ResolvedEnemyAction> actions)
        {
            foreach (var action in actions)
            {
                var actor = actors.FirstOrDefault(x => x.EnemyId == action.EnemyId);
                if (actor == null) continue;
                var giant = actor.GiantMotion;
                var release = giant == null ? 0 : giant.ReleaseCount;
                var vesselHit = vesselImpactSerial;
                actor.Feedback(false, true, true, action.Channel == DamageChannel.Sanity);
                if (giant != null)
                {
                    if (action.Channel == DamageChannel.Sanity)
                        yield return new WaitUntil(() => vesselImpactSerial > vesselHit);
                    else
                        yield return new WaitUntil(() => giant.ReleaseCount > release);
                }
                else if (actor.HasPendingDeckMotion)
                    yield return new WaitUntil(() => actor.DeckActionImpact > 0);
                else if (actor.HasPendingGenericAttack)
                    yield return new WaitUntil(() => actor.GenericAttackAtImpact);
                PlayPlayerImpact(action.Channel == DamageChannel.Sanity);
                director.NotifyPresentationImpact(action);
                yield return new WaitUntil(() => giant != null ? !giant.Busy :
                    !actor.HasPendingDeckMotion && !actor.HasPendingGenericAttack);
            }
            watcherSequence = null;
            DelaysHudUntilImpact = false;
            director.NotifyPresentationImpact();
        }
        private void OnGiantRelease(Vector3 point,float amplitude) { if(water!=null)water.EmitImpact(point,amplitude); }
        private void OnSurfaceStruck(Vector3 point) => water.EmitImpact(point);
        private void OnBrokeSurface(BattleActorView actor)
        {
            if(water==null)return;
            var point=actor.transform.position;point.y=water.SurfaceHeight(point);
            water.EmitImpact(point,Mathf.Max(12,actor.WaterRadius*.45f));
        }

        // Runs after actor motion: contacts, water geometry and vessel samples share one frame.
        private void LateUpdate()
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
            if (vesselFollowsWater && water != null)
            {
                FollowWaves(Time.unscaledDeltaTime);
                FrameImpactWaves(Time.unscaledDeltaTime, reduced);
            }
            else
            {
                var dt = Time.unscaledDeltaTime;
                combatTarget = Mathf.MoveTowards(combatTarget, 0, dt * .65f);
                var target = Mathf.Max(combatTarget, actors.Count == 0 ? 0 : actors.Max(a => a.DeckActionImpact));
                combatSway = Mathf.SmoothDamp(combatSway, reduced ? 0 : target, ref combatVelocity, .3f, Mathf.Infinity, dt);
                // Two non-matching swell periods, with a bounded, smoothly decaying combat envelope.
                var heave = Mathf.Sin(age * .55f) + .22f * Mathf.Sin(age * 1.13f + .8f);
                var pitch = Mathf.Sin(age * .41f + .3f) + .18f * Mathf.Sin(age * .89f);
                var roll = Mathf.Sin(age * .63f) + .24f * Mathf.Sin(age * 1.07f + 1.1f);
                vessel.localPosition = shipPosition + Vector3.up * (reduced ? 0 : heave * vesselHeave * (1 + combatSway * .35f));
                vessel.localRotation = shipRotation * Quaternion.Euler(reduced ? 0 : pitch * vesselPitch * (1 + combatSway * .6f),
                    0, reduced ? 0 : roll * vesselRoll * (1 + combatSway * .65f));
            }
        }
        private void FollowWaves(float delta)
        {
            var parent = vessel.parent;
            var rest = parent != null ? parent.TransformPoint(shipPosition) : shipPosition;
            var rotation = parent != null ? parent.rotation * shipRotation : shipRotation;
            var forward = rotation * Vector3.forward * Mathf.Max(0.1f, vesselSampleExtents.y);
            var right = rotation * Vector3.right * Mathf.Max(0.1f, vesselSampleExtents.x);
            var sampleCenter = rest + Vector3.ProjectOnPlane(vesselResponse.Offset, Vector3.up);
            float pitch = -Mathf.Atan2(water.SampleDisplacement(sampleCenter + forward).y - water.SampleDisplacement(sampleCenter - forward).y, forward.magnitude * 2) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(water.SampleDisplacement(sampleCenter + right).y - water.SampleDisplacement(sampleCenter - right).y, right.magnitude * 2) * Mathf.Rad2Deg;
            var impact = Mathf.Abs(water.SampleImpactHeight(sampleCenter));
            float response = Mathf.Clamp01(impact / 25f);
            var targetAngles = new Vector3(Mathf.Clamp(pitch, -maxWavePitch - response * 18, maxWavePitch + response * 18),
                Mathf.Sin(water.SeaTime * 0.24f) * vesselYaw,
                Mathf.Clamp(roll, -maxWaveRoll - response * 23, maxWaveRoll + response * 23));
            // Finite hull footprint filters small ripples; the same travelling crest lifts the hull.
            var displacement = water.SampleDisplacement(sampleCenter);
            displacement.y = (displacement.y * 2 + water.SampleDisplacement(sampleCenter + forward).y
                + water.SampleDisplacement(sampleCenter - forward).y + water.SampleDisplacement(sampleCenter + right).y
                + water.SampleDisplacement(sampleCenter - right).y) / 6;
            displacement += water.SampleImpactSurge(sampleCenter);
            var verticalSpeed = (water.SampleImpactHeight(sampleCenter, 0.05f) - water.SampleImpactHeight(sampleCenter, -0.05f)) / 0.1f;
            if (GameSettings.ReducedMotion)
            {
                displacement.x = displacement.z = 0;
                targetAngles = Vector3.zero;
                verticalSpeed *= 0.25f;
            }
            vesselResponse.Tick(displacement, targetAngles, delta, Vector3.up * verticalSpeed);
            vessel.position = rest + vesselResponse.Offset;
            vessel.rotation = rotation * Quaternion.Euler(vesselResponse.Angles);
        }
        private void FrameImpactWaves(float delta, bool reduced)
        {
            var parent = battleCamera.transform.parent;
            var rest = parent != null ? parent.TransformPoint(cameraPosition) : cameraPosition;
            var baseRotation = parent != null ? parent.rotation * cameraRotation : cameraRotation;
            // Anticipate the approaching crest so a 70–100 m wall does not engulf the camera.
            var speed = reduced ? 0.25f : 1f;
            float crest = Mathf.Max(0, water.SampleImpactHeight(rest), water.SampleImpactHeight(rest, 0.6f * speed), water.SampleImpactHeight(rest, 1.2f * speed));
            float shipLift = Mathf.Max(0, vesselResponse.Offset.y);
            var goal = Mathf.Max(crest, shipLift * 0.85f);
            impactCameraLift = Mathf.Lerp(impactCameraLift, goal, 1 - Mathf.Exp(-delta * 3f));
            // Safety clearance also handles reduced motion and overlapping crests.
            var clearance = Mathf.Max(0, water.SurfaceHeight(rest) + 12 - rest.y);
            impactCameraLift = Mathf.Max(impactCameraLift, clearance);
            battleCamera.transform.position = rest + Vector3.up * impactCameraLift;
            var target = rest + baseRotation * Vector3.forward * 140 + Vector3.up * (shipLift * 0.65f);
            battleCamera.transform.rotation = Quaternion.LookRotation(target - battleCamera.transform.position, Vector3.up);
        }

        private void OnDisable()
        {
            if (director != null) director.Resolved -= Present;
            foreach (var motion in seaMotions)
                if (motion != null) { motion.SurfaceStruck -= OnSurfaceStruck; motion.SurfaceHeight = null; }
            seaMotions.Clear();
            foreach(var actor in actors) if(actor != null && actor.GiantMotion != null) { actor.GiantMotion.SurfaceReleased -= OnGiantRelease; actor.GiantMotion.VesselImpact -= OnVesselImpact; }
            foreach(var actor in actors)if(actor!=null)actor.BrokeSurface-=OnBrokeSurface;
            if (skybox != null) RenderSettings.skybox = previousSkybox;
            RenderSettings.fog = previousFog; RenderSettings.fogColor = previousColor;
            RenderSettings.fogDensity = previousDensity; RenderSettings.fogMode = previousMode;
        }
    }
}
