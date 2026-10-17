using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnsoundedSea.Runtime
{
    /// <summary>Presentation-only audio. Owns all loops, music transport and bounded one-shots.</summary>
    public sealed class SeaAudio : MonoBehaviour
    {
        private sealed class Voice
        {
            public AudioSource source;
            public SeaAudioBus bus;
            public float gain;
            public int priority;
            public bool active;
        }
        private sealed class MusicDeck
        {
            public Voice[] stems;
            public float level, target;
            public string id;
        }
        private static SeaAudio instance;
        private SeaAudioBank bank;
        private readonly Dictionary<string, SeaAudioCue> cues = new Dictionary<string, SeaAudioCue>();
        private readonly Dictionary<string, int> lastVariant = new Dictionary<string, int>();
        private readonly Dictionary<string, int> playCounts = new Dictionary<string, int>();
        private readonly System.Random random = new System.Random(18102026);
        private readonly List<Voice> voices = new List<Voice>();
        private MusicDeck[] decks;
        private Voice sea, wind, wake;
        private bool paused, hasWorld;
        private float movement, targetMovement, duck, lapTimer=4, creakTimer=9, intensity=.25f;
        private string context;
        public static bool Ready => instance != null && instance.bank != null;
        public static SeaAudio Instance => instance;
        public bool Paused => paused;
        public float WakeGain => wake == null ? 0 : wake.source.volume;
        public string Context => context;
        public int ActiveVoices => voices.Count(v => v.active);
        public int CueCount(string id) => playCounts.TryGetValue(id, out var value) ? value : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() { instance = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (instance != null) return;
            var data = Resources.Load<SeaAudioBank>("SeaAudioBank");
            if (data == null) return;
            instance = new GameObject("Sea sound director").AddComponent<SeaAudio>();
            DontDestroyOnLoad(instance.gameObject);
            instance.Initialize(data);
        }
        private Voice NewVoice(string name, SeaAudioBus bus, bool loop)
        {
            var child = new GameObject(name); child.transform.SetParent(transform, false);
            child.AddComponent<SeaAudioVoice>();
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = loop; source.spatialBlend = 0; source.volume = 0;
            source.dopplerLevel = 0; source.priority = loop ? 160 : 80;
            return new Voice { source=source, bus=bus };
        }
        private Voice Loop(string name, AudioClip clip, float gain)
        {
            var voice=NewVoice(name,SeaAudioBus.Ambience,true);
            voice.source.clip=clip; voice.gain=gain; voice.active=clip!=null;
            if(clip!=null) voice.source.Play();
            return voice;
        }
        private void Initialize(SeaAudioBank data)
        {
            bank=data;
            foreach(var cue in bank.cues) cues[cue.id]=cue;
            for(int i=0;i<20;i++) voices.Add(NewVoice("Effect "+i,SeaAudioBus.Effects,false));
            decks=new MusicDeck[2];
            for(int d=0;d<2;d++) decks[d]=new MusicDeck { stems=Enumerable.Range(0,3).Select(i=>NewVoice("Music "+d+"/"+i,SeaAudioBus.Music,true)).ToArray() };
            sea=Loop("Open sea",bank.sea,.48f); wind=Loop("Wind",bank.wind,.25f); wake=Loop("Bow and wake",bank.wake,0);
            SceneManager.sceneLoaded+=SceneLoaded;
        }
        private void SceneLoaded(Scene scene,LoadSceneMode mode)
        {
            // A new owner sets context on its first tick. One-shot tails must not leak into a new encounter.
            hasWorld=false; SetPaused(false); targetMovement=0;
            foreach(var v in voices) if(v.bus!=SeaAudioBus.Interface) {v.source.Stop();v.active=false;}
        }
        public static void World(string music, bool isPaused, float speed=0, float pressure=.25f)
        {
            if(!Ready)return;
            instance.hasWorld=true; instance.SetPaused(isPaused);
            instance.targetMovement=Mathf.Clamp01(speed/8f);
            instance.intensity=Mathf.Clamp01(pressure);
            if(instance.context!=music) instance.ChangeMusic(music);
        }
        private void ChangeMusic(string id)
        {
            context=id;
            var track=bank.music.FirstOrDefault(x=>x.id==id);
            if(track==null)return;
            var existing=decks.FirstOrDefault(d=>d.id==id);
            var next=existing ?? decks.OrderBy(d=>d.level).First();
            foreach(var deck in decks)deck.target=deck==next?1:0;
            if(existing!=null)return;
            next.id=id; next.level=0;
            double start=AudioSettings.dspTime+.15;
            for(int i=0;i<next.stems.Length;i++)
            {
                var voice=next.stems[i];voice.source.Stop();voice.source.volume=0;
                voice.source.clip=i<track.stems.Length?track.stems[i]:null;
                voice.active=voice.source.clip!=null;
                if(voice.active) { voice.source.PlayScheduled(start); if(paused)voice.source.Pause(); }
            }
        }
        private IEnumerable<Voice> AllVoices()
        {
            foreach(var v in voices)yield return v;
            foreach(var d in decks)foreach(var v in d.stems)yield return v;
            yield return sea;yield return wind;yield return wake;
        }
        private void SetPaused(bool value)
        {
            if(paused==value)return;
            paused=value;
            foreach(var v in AllVoices())
            {
                if(v.bus==SeaAudioBus.Interface || !v.active)continue;
                if(value)v.source.Pause();else v.source.UnPause();
            }
        }
        public static bool Play(string id, float gain=1, float pan=0, int priority=1, bool ui=false)
        {
            return Ready && instance.PlayCue(id,gain,pan,priority,ui);
        }
        private bool PlayCue(string id,float gain,float pan,int priority,bool ui)
        {
            if(paused&&!ui || !cues.TryGetValue(id,out var cue) || cue.clips==null || cue.clips.Length==0)return false;
            var voice=voices.FirstOrDefault(v=>!v.active || (!paused && !v.source.isPlaying));
            if(voice==null)voice=voices.Where(v=>v.priority<priority).OrderBy(v=>v.priority).FirstOrDefault();
            if(voice==null)return false;
            int index=random.Next(cue.clips.Length);
            if(cue.clips.Length>1 && lastVariant.TryGetValue(id,out var previous) && index==previous)index=(index+1)%cue.clips.Length;
            lastVariant[id]=index;
            var clip=cue.clips[index];if(clip==null)return false;
            voice.source.Stop();voice.source.clip=clip;voice.source.pitch=1;voice.source.panStereo=Mathf.Clamp(pan,-.65f,.65f);
            voice.bus=ui?SeaAudioBus.Interface:(id=="lap"||id=="creak"?SeaAudioBus.Ambience:SeaAudioBus.Effects);voice.gain=cue.gain*gain;
            voice.priority=priority;voice.active=true;Apply(voice);voice.source.Play();
            playCounts[id]=CueCount(id)+1;
            if(priority>=3)duck=.55f;
            return true;
        }
        private float BusGain(SeaAudioBus bus)
        {
            switch(bus)
            {
                case SeaAudioBus.Music:return GameSettings.MusicVolume/(float)GameSettings.VolumeSteps;
                case SeaAudioBus.Ambience:return GameSettings.AmbienceVolume/(float)GameSettings.VolumeSteps;
                default:return GameSettings.EffectsVolume/(float)GameSettings.VolumeSteps;
            }
        }
        private void Apply(Voice voice)
        {
            float gain=voice.gain;
            if(GameSettings.NarrowDynamics) gain=Mathf.Min(gain,.62f);
            voice.source.volume=Mathf.Clamp01(gain*BusGain(voice.bus));
        }
        private void Update()
        {
            if(bank==null)return;
            float dt=Time.unscaledDeltaTime;
            foreach(var v in voices)
            {
                if(!paused || v.bus==SeaAudioBus.Interface) v.active=v.source.isPlaying;
                Apply(v);
            }
            if(paused)return;
            duck=Mathf.MoveTowards(duck,0,dt*1.5f);
            movement=Mathf.MoveTowards(movement,targetMovement,dt*1.7f);
            sea.gain=hasWorld?.6f*(1-duck*.25f):0;
            wind.gain=hasWorld?.22f:0;wake.gain=movement*.55f;
            Apply(sea);Apply(wind);Apply(wake);
            foreach(var deck in decks)
            {
                deck.level=Mathf.MoveTowards(deck.level,deck.target,dt/2.8f);
                for(int i=0;i<deck.stems.Length;i++)
                {
                    var v=deck.stems[i];
                    v.gain=deck.level*(i==0?.36f:i==1?.33f:Mathf.Lerp(.1f,.45f,intensity))*(1-duck*.55f);
                    Apply(v);
                    if(deck.target==0 && deck.level==0 && v.active){v.source.Stop();v.active=false;}
                }
                if(deck.target==0 && deck.level==0)deck.id=null;
            }
            if(!hasWorld)return;
            lapTimer-=dt;creakTimer-=dt;
            if(lapTimer<=0){PlayCue("lap",.35f,(float)random.NextDouble()*1.1f-.55f,0,false);lapTimer=4+(float)random.NextDouble()*5;}
            if(creakTimer<=0){PlayCue("creak",.3f,(float)random.NextDouble()*.6f-.3f,0,false);creakTimer=7+(float)random.NextDouble()*9;}
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded-=SceneLoaded;
            if(instance==this)instance=null;
        }
    }
}
