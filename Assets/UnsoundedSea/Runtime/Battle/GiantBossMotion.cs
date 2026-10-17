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
        public Material mistMaterial;
        public Color effectColor = new Color(.45f,.68f,.66f,1);
        public float duration = 2.8f, contactTime = 1.45f, waveAmplitude = 65;
        public float breathPeriod = 7;
        public AnimationCurve strike = DeckActorMotion.Curve(0,0,.34f,-.5f,.46f,-.54f,.57f,1,.65f,.82f,1,0);
        public AnimationCurve follow = DeckActorMotion.Curve(0,0,.39f,-.22f,.63f,1,.76f,.6f,1,0);
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
            Initialize(); water=surface;
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
                float action=ActionTime<0 ? 0 : strike.Evaluate(phase);
                float drag=ActionTime<0 ? 0 : follow.Evaluate(phase);
                float power=observing?.45f:ritual?.72f:1;
                var euler=(j.idleEuler*idle+j.attackEuler*(action*power)+j.followEuler*(drag*power)+j.hitEuler*HitWeight)*reduced;
                var modelRotation=Quaternion.Euler(euler);
                j.bone.localRotation=j.rotation*Quaternion.Inverse(j.basis)*modelRotation*j.basis;
                var displacement=(j.idleShift*idle+j.attackShift*(action*power)+j.hitShift*HitWeight)*reduced;
                // Child local translations follow the parent's current pose.
                var parentBasis=j.bone.parent==null ? Quaternion.identity : Quaternion.Inverse(transform.rotation)*j.bone.parent.rotation;
                j.bone.localPosition=j.position+Quaternion.Inverse(parentBasis)*displacement;
                j.bone.localScale=Vector3.Scale(j.scale,Vector3.one+j.breathScale*((idle*.5f+.5f+Mathf.Max(0,-action)*power)*reduced));
                if (retiring && j.bone.name=="GB_Body")
                    j.bone.localPosition+=Quaternion.Inverse(parentBasis)*Vector3.down*(Mathf.SmoothStep(0,1,retirement)*.7f);
            }
            if (ActionTime>=contactTime && !released)
            {
                released=true;ReleaseCount++;
                var point=Origin;
                if (water!=null) point.y=water.SurfaceHeight(point);
                if (!ritual && !observing) SurfaceReleased?.Invoke(point,waveAmplitude);
                effects?.Release(Origin,ritual,observing);
            }
            effects?.Tick(dt,Origin,ActionTime>=0 && !released,retiring);
            if (ActionTime>=duration) ActionTime=-1;
        }
    }
}
