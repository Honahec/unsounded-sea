using System;
using System.Linq;
using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>The wreck and its captive share a pivot; this component never resolves combat.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class FlagshipPresentation : MonoBehaviour
    {
        public Transform hull;
        public Transform[] tetherRoots = Array.Empty<Transform>();
        public Renderer[] wardRings = Array.Empty<Renderer>();
        public Material chainMaterial, dustMaterial;
        public Vector3[] bodyAttachments = { new Vector3(-.18f,.2f,0), new Vector3(.18f,.2f,0), new Vector3(-.2f,0,.1f), new Vector3(.2f,0,.1f) };
        public float Roll { get; private set; }
        public float WardStrength { get; private set; } = 1;
        public int StressBursts { get; private set; }
        BattleDirector director;
        GiantBossMotion motion;
        Transform hips;
        Vector3 restPosition;
        Quaternion restRotation;
        float age, previousHit, previousDefeatLoad;
        int release;
        Mesh chains;
        Vector3[] vertices, normals;
        ParticleSystem dust;
        MaterialPropertyBlock properties;
        const int Links = 28, Around = 10, Tube = 4;

        public void Bind(BattleDirector owner, GiantBossMotion actor)
        {
            director = owner; motion = actor;
            hips = motion.joints.Single(j => j.bone.name == "GB_Body").bone;
            restPosition = hull.localPosition; restRotation = hull.localRotation;
            properties = new MaterialPropertyBlock();
            var go = new GameObject("Tensioned iron chains"); go.transform.SetParent(transform, false);
            chains = new Mesh { name = "Four articulated iron tethers" }; chains.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = chains;
            go.AddComponent<MeshRenderer>().sharedMaterial = chainMaterial;
            int count = tetherRoots.Length * Links * Around * Tube;
            vertices = new Vector3[count]; normals = new Vector3[count];
            var indices = new int[count * 6]; var uv = new Vector2[count];
            for (int ring = 0; ring < tetherRoots.Length * Links; ring++)
                for (int a = 0; a < Around; a++) for (int b = 0; b < Tube; b++)
                {
                    int v = ring * Around * Tube + a * Tube + b, t = v * 6;
                    int nextA = ring * Around * Tube + (a + 1) % Around * Tube + b;
                    int nextB = ring * Around * Tube + a * Tube + (b + 1) % Tube;
                    int diagonal = ring * Around * Tube + (a + 1) % Around * Tube + (b + 1) % Tube;
                    indices[t] = v; indices[t+1] = nextA; indices[t+2] = nextB;
                    indices[t+3] = nextB; indices[t+4] = nextA; indices[t+5] = diagonal;
                    uv[v] = new Vector2((float)a/Around, (float)b/Tube);
                }
            chains.vertices = vertices; chains.triangles = indices; chains.uv = uv;
            var plume = new GameObject("Timber dust at the captive cradle"); plume.transform.SetParent(transform, false);
            dust = plume.AddComponent<ParticleSystem>(); dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = dust.main; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 240; main.gravityModifier = .12f; main.loop = false;
            var emission = dust.emission; emission.enabled = false; var shape = dust.shape; shape.enabled = false;
            var color = dust.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1) },
                new[] { new GradientAlphaKey(0,0),new GradientAlphaKey(.4f,.1f),new GradientAlphaKey(0,1) }); color.color = gradient;
            plume.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMaterial;
            UpdateChains();
        }
        void Update()
        {
            if (motion == null || director.Paused) return;
            Tick(Mathf.Min(Time.unscaledDeltaTime,.1f), GameSettings.ReducedMotion);
        }
        public void Tick(float delta, bool reduced)
        {
            if (motion == null || delta <= 0) return;
            age += delta;
            float phase = motion.ActionTime < 0 ? 0 : motion.ActionTime/motion.duration;
            float drive = motion.ActionTime < 0 ? 0 : motion.follow.Evaluate(phase);
            drive = Mathf.Max(drive, motion.DefeatLoad * 1.4f);
            float amount = reduced ? 0 : 1;
            Roll = (.18f*Mathf.Sin(age*.31f)+drive*.65f+motion.HitWeight*.22f)*amount;
            hull.localPosition = restPosition + Vector3.up*((.45f*Mathf.Sin(age*.24f)+.18f*Mathf.Sin(age*.53f)-motion.DefeatLoad*1.8f)*amount);
            hull.localRotation = restRotation*Quaternion.Euler(-drive*.35f*amount,0,Roll);
            WardStrength = Mathf.MoveTowards(WardStrength,motion.IsRetiring || director.View.BossStage >= 3 ? 0 : 1,delta*1.5f);
            foreach (var renderer in wardRings)
            {
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_EmissionColor",new Color(.12f,.3f,.27f)*WardStrength);
                renderer.SetPropertyBlock(properties);
            }
            if (release != motion.ReleaseCount || motion.HitWeight > .7f && previousHit <= .7f ||
                motion.DefeatLoad > .65f && previousDefeatLoad <= .65f)
            {
                release = motion.ReleaseCount; StressBursts++;
                for (int i=0;i<(reduced?8:40);i++)
                {
                    float a=i*2.399963f;
                    var direction=new Vector3(Mathf.Cos(a),.25f,Mathf.Sin(a));
                    dust.Emit(new ParticleSystem.EmitParams { position=hips.position+direction*17+Vector3.down*5,
                        velocity=direction*3, startSize=2+i%4, startLifetime=1.4f+i%5*.25f,
                        startColor=new Color(.32f,.29f,.23f,.5f) },1);
                }
            }
            previousHit = motion.HitWeight; dust.Simulate(delta,false,false,true);
            previousDefeatLoad = motion.DefeatLoad;
        }
        void LateUpdate() { if (motion != null && !director.Paused) UpdateChains(); }
        void UpdateChains()
        {
            for (int c=0;c<tetherRoots.Length;c++)
            {
                var from = tetherRoots[c].position;
                // Attachment coordinates are in the normalized actor space, not the imported bone basis.
                var to = hips.position + motion.transform.TransformVector(bodyAttachments[c]);
                float tension = motion.ActionTime < 0 ? 0 : Mathf.Clamp01(motion.poleDrive.Evaluate(motion.ActionTime/motion.duration));
                tension = Mathf.Max(tension, motion.DefeatLoad);
                float sag = Mathf.Lerp(4,.5f,tension), length=Vector3.Distance(from,to);
                for(int l=0;l<Links;l++)
                {
                    float t=(l+.5f)/Links;
                    var center=Vector3.Lerp(from,to,t)+Vector3.down*(4*t*(1-t)*sag);
                    var along=(to-from+Vector3.down*(4*(1-2*t)*sag)).normalized;
                    var across=Vector3.Cross(along,Vector3.up).normalized;
                    if(across.sqrMagnitude<.1f)across=Vector3.right;
                    var up=Vector3.Cross(across,along);
                    var width=l%2==0?across:up;
                    var normal=Vector3.Cross(along,width);
                    for(int a=0;a<Around;a++) for(int b=0;b<Tube;b++)
                    {
                        float theta=a*2*Mathf.PI/Around, phi=b*2*Mathf.PI/Tube;
                        var radial=along*Mathf.Cos(theta)+width*Mathf.Sin(theta);
                        var n=radial*Mathf.Cos(phi)+normal*Mathf.Sin(phi);
                        var p=center+along*(Mathf.Cos(theta)*length/Links*.72f)+width*(Mathf.Sin(theta)*.65f)+n*.18f;
                        int v=((c*Links+l)*Around+a)*Tube+b;
                        vertices[v]=transform.InverseTransformPoint(p); normals[v]=transform.InverseTransformDirection(n);
                    }
                }
            }
            chains.vertices=vertices; chains.normals=normals; chains.RecalculateBounds();
        }
        void OnDestroy() { if(chains!=null)Destroy(chains); }
    }
}
