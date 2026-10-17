using System;
using UnityEngine;
using UnsoundedSea.Core;

namespace UnsoundedSea.Runtime
{
    [Serializable]
    public sealed class GiantJointMotion
    {
        public Transform bone;
        // Authored in model axes, independent of the bone's imported rest orientation.
        public Vector3 idleEuler, attackEuler, followEuler, hitEuler;
        public Vector3 idleShift, attackShift, hitShift, breathScale;
        public float phase, lag;
        [NonSerialized] public Quaternion rotation, basis;
        [NonSerialized] public Vector3 position, scale;
    }

    /// <summary>Deterministic, additive anatomical poses. Presentation never deals damage.</summary>
    public sealed class GiantBossMotion : MonoBehaviour
    {
        public BossEncounterKind kind;
        public GiantJointMotion[] joints = Array.Empty<GiantJointMotion>();
        public Transform effectOrigin;
        public Transform sonicOrigin;
        public Material mistMaterial;
        public Color effectColor = new Color(.45f,.68f,.66f,1);
        public float duration = 2.8f, contactTime = 1.45f, waveAmplitude = 65;
        public float breathPeriod = 7;
        public AnimationCurve strike = DeckActorMotion.Curve(0,0,.34f,-.5f,.46f,-.54f,.57f,1,.65f,.82f,1,0);
        public AnimationCurve follow = DeckActorMotion.Curve(0,0,.39f,-.22f,.63f,1,.76f,.6f,1,0);
        public AnimationCurve resonance = DeckActorMotion.Curve(0,0,.28f,.45f,.55f,1,.72f,.8f,1,0);
        public AnimationCurve calling = DeckActorMotion.Curve(0,0,.36f,.85f,.62f,1,.82f,.45f,1,0);
        public AnimationCurve poleDrive = DeckActorMotion.Curve(0,0,.28f,-.12f,.42f,0,.57f,1,.67f,1,1,0);
        public AnimationCurve poleDraw = DeckActorMotion.Curve(0,0,.3f,1,.42f,1,.57f,0,1,0);
        public AnimationCurve recoil = DeckActorMotion.Curve(0,0,.28f,1,.55f,.88f,1.9f,0);
        public float Age { get; private set; }
        public float ActionTime { get; private set; } = -1;
        public float HitWeight { get; private set; }
        public bool Busy => ActionTime >= 0 || effects != null && effects.Busy || retiring && retirement < 1;
        public int ReleaseCount { get; private set; }
        public event Action<Vector3,float> SurfaceReleased;
        public event Action VesselImpact;
        private bool initialized, paused, released, ritual, observing, retiring;
        private float hitTime = -1, hitStart, retirement, hitScale = 1;
        private GiantBossEffects effects;
        private BattleWater water;
        private Transform vessel;
        public Vector3 StrikeTarget => vessel != null ? vessel.TransformPoint(new Vector3(0, 1, 5)) : Origin;
        public Vector3 PoleTip => Origin;

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            foreach (var j in joints)
            {
                if (j.bone == null) throw new InvalidOperationException("Missing giant joint");
                j.rotation = j.bone.localRotation; j.position = j.bone.localPosition; j.scale = j.bone.localScale;
                j.basis = Quaternion.Inverse(transform.rotation) * j.bone.rotation;
            }
            foreach (var skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            { var bounds=skin.localBounds; bounds.Expand(bounds.size*.6f); skin.localBounds=bounds; }
        }
        public void BindEffects(BattleWater surface, Transform boat)
        {
            Initialize(); water=surface; vessel=boat;
            effects=gameObject.AddComponent<GiantBossEffects>();
            effects.Initialize(mistMaterial, effectColor, water, boat, kind==BossEncounterKind.Watcher);
            effects.VesselImpact += OnVesselImpact;
        }
        private void OnVesselImpact() => VesselImpact?.Invoke();
        public void SetPaused(bool value) => paused=value;
        public void PlayAttack(bool isRitual = false, bool isObservation = false)
        {
            Initialize(); if (retiring || ActionTime>=0) return;
            ritual=isRitual; observing=isObservation; released=false; ActionTime=0;
        }
        public void PlayHit(float strength = 1)
        {
            Initialize(); if (retiring) return;
            hitStart=HitWeight; hitScale=Mathf.Clamp(strength,.35f,1.4f); hitTime=0;
            effects?.Hit(Origin, hitScale);
        }
        public void Retire()
        {
            Initialize(); if (retiring) return;
            retiring=true; ActionTime=-1; hitTime=-1; HitWeight=0;
        }
        public Vector3 Origin => effectOrigin != null ? effectOrigin.position : transform.position;
        public Vector3 SonicOrigin => sonicOrigin != null ? sonicOrigin.position : Origin;
        public void Tick(float dt)
        {
            Initialize(); if (paused || dt<=0) return;
            dt=Mathf.Min(dt,.1f); Age+=dt;
            if (ActionTime>=0) ActionTime+=dt;
            if (hitTime>=0)
            {
                hitTime+=dt;
                HitWeight=hitTime<.28f ? Mathf.Lerp(hitStart,hitScale,Mathf.SmoothStep(0,1,hitTime/.28f)) : recoil.Evaluate(hitTime)*hitScale;
                if (hitTime>=1.9f) { hitTime=-1; HitWeight=0; }
            }
            if (retiring) retirement=Mathf.Min(1,retirement+dt/4);
            var reduced=GameSettings.ReducedMotion ? .22f : 1f;
            foreach (var j in joints)
            {
                float idle=Mathf.Sin(Age*2*Mathf.PI/breathPeriod+j.phase);
                float phase=ActionTime<0 ? 0 : Mathf.Clamp01((ActionTime-j.lag)/Mathf.Max(.1f,duration-j.lag));
                var distinct = kind != BossEncounterKind.Watcher && (ritual || observing);
                float action=ActionTime<0 ? 0 : distinct ? (observing ? calling.Evaluate(phase) : resonance.Evaluate(phase)) : strike.Evaluate(phase);
                float drag=ActionTime<0 ? 0 : follow.Evaluate(phase);
                // Rituals unfold/lift instead of repeating the downward physical strike.
                // Calls sustain the sound organ; fins/cloth stay restrained while a summon appears.
                float power=distinct?(observing?.22f:-.48f):observing?.45f:ritual?.72f:1;
                var actionEuler=j.attackEuler*(action*power)+j.followEuler*(drag*(distinct?.18f:power));
                if (distinct)
                {
                    var bone=j.bone.name;
                    if (kind==BossEncounterKind.Choirmaster && (bone=="GB_Head" || bone=="GB_Gullet"))
                        actionEuler+=new Vector3(-3*action,0,0);
                    if (kind==BossEncounterKind.Surveyor && bone.Contains("Pendulum"))
                        actionEuler+=new Vector3(0,0,5*action);
                    if (kind==BossEncounterKind.FlagBearer && bone.Contains("Flag"))
                        actionEuler+=new Vector3(0,3*action,0);
                }
                var euler=(j.idleEuler*idle+actionEuler+j.hitEuler*HitWeight)*reduced;
                var modelRotation=Quaternion.Euler(euler);
                j.bone.localRotation=j.rotation*Quaternion.Inverse(j.basis)*modelRotation*j.basis;
                var displacement=(j.idleShift*idle+j.attackShift*(action*power)+j.hitShift*HitWeight)*reduced;
                // Child local translations follow the parent's current pose.
                var parentBasis=j.bone.parent==null ? Quaternion.identity : Quaternion.Inverse(transform.rotation)*j.bone.parent.rotation;
                j.bone.localPosition=j.position+Quaternion.Inverse(parentBasis)*displacement;
                j.bone.localScale=Vector3.Scale(j.scale,Vector3.one+j.breathScale*((idle*.5f+.5f+(distinct?action*2:Mathf.Max(0,-action)*power))*reduced));
                if (retiring && j.bone.name=="GB_Body")
                    j.bone.localPosition+=Quaternion.Inverse(parentBasis)*Vector3.down*(Mathf.SmoothStep(0,1,retirement)*.7f);
            }
            if (kind == BossEncounterKind.FlagBearer && !ritual && !observing && ActionTime >= 0 && vessel != null && !GameSettings.ReducedMotion)
                ApplyPoleThrust();
            if (ActionTime>=contactTime && !released)
            {
                released=true;ReleaseCount++;
                var point=Origin;
                if (water!=null) point.y=water.SurfaceHeight(point);
                if (!ritual && !observing && kind == BossEncounterKind.Watcher)
                    SurfaceReleased?.Invoke(point,waveAmplitude);
                if (kind == BossEncounterKind.FlagBearer && !ritual && !observing)
                    effects?.PoleContact(GameSettings.ReducedMotion ? StrikeTarget : PoleTip);
                else effects?.Release(kind == BossEncounterKind.Choirmaster && !ritual && !observing ? SonicOrigin : Origin,ritual,observing,kind);
            }
            effects?.Tick(dt,Origin,ActionTime>=0 && !released,retiring);
            if (ActionTime>=duration) ActionTime=-1;
        }
        private void ApplyPoleThrust()
        {
            var pole = Array.Find(joints, j => j.bone.name == "GB_Pole");
            var body = Array.Find(joints, j => j.bone.name == "GB_Body");
            if (pole == null || body == null || effectOrigin == null) return;
            var target = StrikeTarget;
            var toTarget = target - pole.bone.position;
            var tip = effectOrigin.position - pole.bone.position;
            var phase = Mathf.Clamp01(ActionTime / duration);
            var advance = Mathf.Clamp(toTarget.magnitude - tip.magnitude, -32, 32);
            body.bone.position += toTarget.normalized * (advance * poleDrive.Evaluate(phase) - 14 * poleDraw.Evaluate(phase));
            var aimed = Quaternion.FromToRotation(effectOrigin.position - pole.bone.position, target - pole.bone.position) * pole.bone.rotation;
            var aim = phase < .3f ? Mathf.SmoothStep(0, 1, phase / .3f) : phase < .72f ? 1 : 1 - Mathf.SmoothStep(0, 1, (phase - .72f) / .28f);
            pole.bone.rotation = Quaternion.Slerp(pole.bone.rotation, aimed, aim);
        }
    }
}
