using UnityEngine;

namespace UnsoundedSea.Runtime
{
    /// <summary>World-space salt spray / resonance mist, simulated by the same paused clock as the rig.</summary>
    public sealed class GiantBossEffects : MonoBehaviour
    {
        private ParticleSystem mist;
        private Transform boat;
        private BattleWater water;
        private Color tint;
        private float age, chargeClock, ambientClock;
        private int serial;
        // Field initialization also restores valid slots after an Editor domain reload.
        private readonly Pulse[] pulses = { new Pulse(), new Pulse(), new Pulse(), new Pulse() };
        public int ParticleCount => mist == null ? 0 : mist.particleCount;
        private sealed class Pulse { public Vector3 origin, target; public float time; public bool active; }
        public void Initialize(Material material, Color color, BattleWater surface, Transform vessel)
        {
            tint=color;water=surface;boat=vessel;
            for(int i=0;i<pulses.Length;i++)pulses[i]=new Pulse();
            var go=new GameObject("Giant salt and resonance");go.transform.SetParent(transform,false);
            mist=go.AddComponent<ParticleSystem>();mist.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            mist.useAutoRandomSeed=false;mist.randomSeed=197;
            var main=mist.main;main.playOnAwake=false;main.loop=false;main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=1800;main.startSpeed=0;
            var emission=mist.emission;emission.enabled=false;var shape=mist.shape;shape.enabled=false;
            var size=mist.sizeOverLifetime;size.enabled=true;size.size=new ParticleSystem.MinMaxCurve(1,DeckActorMotion.Curve(0,.35f,.2f,.8f,1,1.9f));
            var colors=mist.colorOverLifetime;colors.enabled=true;
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(.7f,.1f),new GradientAlphaKey(.3f,.6f),new GradientAlphaKey(0,1)});colors.color=gradient;
            var renderer=go.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=material;renderer.renderMode=ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        private void Emit(Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
        {
            if(mist==null)return;
            mist.Emit(new ParticleSystem.EmitParams {position=position,velocity=velocity,startSize=size,startLifetime=lifetime,startColor=color,rotation=serial++*137.5f},1);
        }
        public void Hit(Vector3 origin,float strength)
        {
            // Salt chips and a brief local pressure cloud, never full-body emissive flashing.
            var rng=new System.Random(++serial);
            for(int i=0;i<70;i++)
            {
                var v=new Vector3((float)rng.NextDouble()-.5f,(float)rng.NextDouble()*.5f,(float)rng.NextDouble()-.5f);
                Emit(origin+v*10,v*12,2+4*(float)rng.NextDouble(),.8f+1.2f*(float)rng.NextDouble(),new Color(.75f,.76f,.65f,.8f*strength));
            }
        }
        public void Release(Vector3 origin,bool ritual,bool observing)
        {
            var target=boat!=null?boat.position:origin+Vector3.back*200;
            var toward=(target-origin).normalized;
            var rng=new System.Random(++serial*7919);
            int count=observing?90:ritual?200:140;
            for(int i=0;i<count;i++)
            {
                var spread=new Vector3((float)rng.NextDouble()-.5f,(float)rng.NextDouble()-.5f,(float)rng.NextDouble()-.5f);
                var v=toward*(ritual?22:12)+spread*(ritual?18:25);
                Emit(origin+spread*20,v,ritual?10:6,2.2f+(float)rng.NextDouble()*1.5f,new Color(tint.r,tint.g,tint.b,observing?.3f:.7f));
            }
            if(ritual && !observing)
            {
                var p=pulses[serial%pulses.Length];p.origin=origin;p.target=target;p.time=0;p.active=true;
            }
        }
        public void Tick(float dt,Vector3 origin,bool charging,bool retiring)
        {
            if(mist==null)return;
            age+=dt;chargeClock+=dt;ambientClock+=dt;
            if(charging && chargeClock>.045f)
            {
                chargeClock=0;
                for(int i=0;i<4;i++)
                {
                    float a=age*1.1f+i*Mathf.PI*.5f;
                    var radial=new Vector3(Mathf.Cos(a)*14,Mathf.Sin(a*1.31f)*7,Mathf.Sin(a)*14);
                    Emit(origin+radial,-radial*.5f,6,1.2f,new Color(tint.r,tint.g,tint.b,.45f));
                }
            }
            if(!retiring && ambientClock>.18f)
            {
                ambientClock=0;
                // Slow drainage anchors the creature to the sea even while it is idle.
                var p=origin+new Vector3(Mathf.Sin(age*1.7f)*18,-10,Mathf.Cos(age*1.3f)*8);
                Emit(p,Vector3.down*5,4,2,new Color(.42f,.53f,.52f,.16f));
            }
            foreach(var p in pulses)
            {
                if(!p.active)continue;
                p.time+=dt;float t=Mathf.Clamp01(p.time/2.1f);
                var center=Vector3.Lerp(p.origin,p.target,t);
                var forward=(p.target-p.origin).normalized;var side=Vector3.Cross(forward,Vector3.up).normalized;
                for(int i=0;i<4;i++)
                {
                    float a=(age*3+i*1.618f)*Mathf.PI;
                    var r=(10+Mathf.Sin(t*Mathf.PI)*25);
                    var q=center+side*Mathf.Sin(a)*r+Vector3.up*Mathf.Cos(a)*r*.5f;
                    if(water!=null)q.y=Mathf.Max(q.y,water.SurfaceHeight(q)+1);
                    Emit(q,forward*6,5+t*5,.65f,new Color(tint.r,tint.g,tint.b,.3f*(1-t*.5f)));
                }
                if(t>=1){p.active=false;Hit(p.target,.6f);}
            }
            mist.Simulate(dt,false,false,true);
        }
    }
}
