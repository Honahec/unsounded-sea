"""Rebuild the first sound palette. Python: numpy, scipy, soundfile. ffmpeg on PATH.
Sources and licenses live in ArtSource/Audio/Sources; no network required.
Original additive score + CC0 foley, deterministic seeds independent of game rules.
"""
from pathlib import Path
import hashlib, json, subprocess, re
import numpy as np
import soundfile as sf
from scipy import signal

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'ArtSource/Audio'
OUT = ROOT / 'Assets/UnsoundedSea/Audio'
RATE = 48000
rng = np.random.default_rng(18102026)
records, used = [], set()


def normalized(x, peak=.7):
    x = np.asarray(x, dtype=np.float32)
    return x * (peak / max(float(np.max(np.abs(x))), .00001))


def fade(x, attack=.015, release=.08):
    x = x.copy()
    a, b = min(len(x), int(attack*RATE)), min(len(x), int(release*RATE))
    shape = (slice(None),) + (None,) * (x.ndim-1)
    x[:a] *= np.linspace(0, 1, a)[shape]
    x[-b:] *= np.linspace(1, 0, b)[shape]
    return x


def filt(x, hz, kind='lowpass'):
    return signal.sosfilt(signal.butter(2, hz, btype=kind, fs=RATE, output='sos'), x, axis=0).astype(np.float32)


def noise(seconds, hz=1800):
    return filt(rng.normal(0, 1, round(seconds*RATE)), hz)


def read(pack, name, speed=1):
    p = SOURCE/'Sources'/pack/name
    used.add(p)
    x, sr = sf.read(p, dtype='float32', always_2d=True)
    x = x.mean(axis=1)
    n = int(len(x)*RATE/sr/speed)
    return signal.resample(x, n).astype(np.float32)


def mix(parts):
    out = np.zeros(max(len(x) for x in parts), np.float32)
    for x in parts: out[:len(x)] += x
    return out


def tone(freq, seconds, kind='bow', pan=0):
    t = np.arange(round(seconds*RATE))/RATE
    phase = 2*np.pi*freq*t + .012*np.sin(t*2*np.pi*4.6)
    x = np.zeros(len(t))
    for h in range(1, 10):
        amp = (1/h**2.25) * (1 if kind=='bow' else (1 if h%2 else .18))
        x += amp*np.sin(h*phase + h*.17*np.sin(t*.7))
    x *= (.86+.06*np.sin(t*1.4)+.03*np.sin(t*3.2))
    x = fade(normalized(x, .14), min(.8, seconds*.2), min(1.6, seconds*.4))
    return np.column_stack((x*np.sqrt((1-pan)/2),x*np.sqrt((1+pan)/2)))


def add(dst, x, at, gain=1):
    # Circular placement preserves the score's exact loop period, including reverb tails.
    pos = int(at*RATE) % len(dst)
    for start in range(0, len(x), len(dst)):
        part=x[start:start+len(dst)]*gain; n=min(len(dst)-pos,len(part))
        dst[pos:pos+n] += part[:n]
        if n<len(part): dst[:len(part)-n] += part[n:]


def room(x, wet=.23):
    y=x.copy()
    for secs,g in [(0.113,.7),(.229,.55),(.419,.37),(.683,.24),(1.173,.13)]:
        echo=np.roll(x, int(secs*RATE),axis=0)
        if x.ndim==2: echo=echo[:,::-1]
        y+=echo*wet*g
    return y


def write(name, x, loop=False, sources=(), gain=1):
    folder='Music' if name.startswith('music_') else 'Ambience' if loop else 'SFX'
    p=OUT/folder/(name+('.ogg' if loop else '.wav')); p.parent.mkdir(parents=True,exist_ok=True)
    x=np.asarray(x,dtype=np.float32)*gain
    if not loop: x=fade(x,.003,.04)
    if np.max(np.abs(x))>.95: raise ValueError(f'Clipping budget: {name}')
    temp=ROOT/'Logs'/'audio-export.wav'
    sf.write(temp,x,RATE,subtype='PCM_24')
    if loop:
        encoded=ROOT/'Logs'/'audio-export.ogg'
        subprocess.run(['ffmpeg','-v','error','-y','-i',str(temp),'-c:a','vorbis','-strict','-2','-q:a','5',str(encoded)],check=True)
        encoded.replace(p)
    else:
        temp.replace(p)
    records.append({'id':name,'path':str(p.relative_to(ROOT)),'loop':loop,'seconds':round(len(x)/RATE,6),
                    'channels':1 if x.ndim==1 else x.shape[1],'source_ids':list(sources) or ['original-synthesis'],
                    'peak_dbfs':round(float(20*np.log10(max(np.max(np.abs(x)),1e-9))),2),
                    'sha256':hashlib.sha256(p.read_bytes()).hexdigest()})
    return x


def foley():
    impact='kenney-impact';rpg='kenney-rpg'
    mappings={
      'hit_body':(impact,'impactSoft_heavy_{:03d}.ogg',.65),
      'hit_metal':(impact,'impactMetal_medium_{:03d}.ogg',.55),
      'hit_bone':(impact,'impactWood_medium_{:03d}.ogg',.5),
      'block':(impact,'impactPlank_medium_{:03d}.ogg',.6),
      'hull':(impact,'impactWood_heavy_{:03d}.ogg',.72),
      'card':(rpg,'bookFlip{}.ogg',.32),
      'cloth':(rpg,'cloth{}.ogg',.36),
      'creak':(rpg,'creak{}.ogg',.32)}
    for cue,(pack,pattern,peak) in mappings.items():
        for i in range(3):
            fn=pattern.format(i+1 if pack==rpg else i)
            speed=.8 if cue in ('hull','creak') else 1
            write(f'{cue}_{i}',normalized(read(pack,fn,speed),peak),sources=[f'{pack}/{fn}'])
    for cue,fn,peak in [('confirm','metalClick.ogg',.3),('mechanism','metalLatch.ogg',.5),('heal','clothBelt.ogg',.35),('insight','bookOpen.ogg',.38),('seal','bookClose.ogg',.4),('slash','knifeSlice.ogg',.55),('reload','drawKnife2.ogg',.4),('guard','handleSmallLeather.ogg',.4)]:
        write(cue, normalized(read(rpg,fn),peak),sources=[f'{rpg}/{fn}'])
    for i in range(3):
        t=np.arange(int(.75*RATE))/RATE
        shot=noise(.75,8000)*np.exp(-t*42)+.35*np.sin(2*np.pi*(125*t+20*(1-np.exp(-t*18))))*np.exp(-t*22)
        write(f'shot_{i}',normalized(room(shot,.17),.8))
        t=np.arange(int(1.3*RATE))/RATE
        hit=noise(1.3,500)*np.exp(-t*5)+.2*np.sin(t*2*np.pi*57)*np.exp(-t*3)
        write(f'hit_giant_{i}',normalized(hit,.58))
    for cue,freq,length in [('sanity',87,1.5),('pay',147,1),('ward',196,1.2),('bell',49,4.5),('ritual',73,2.3),('victory',146.83,4),('retreat',110,4),('defeat',65.4,4),('turn',220,.35),('reject',98,.18),('interrupt',180,.5),('emerge',62,2.4),('pressure',82,1.4),('windstrike',130,1.4)]:
        t=np.arange(int(length*RATE))/RATE
        if cue in ('victory','retreat','defeat'):
            chord=[1,1.5,2] if cue=='victory' else [1,1.3348,2] if cue=='retreat' else [1,1.05946,1.5]
            x=sum(tone(freq*f,length,'reed',0).mean(axis=1) for f in chord)
        else:
            ratios=[1,2.76,4.07,5.41] if cue=='bell' else [1,1.013,1.498]
            x=sum(np.sin(2*np.pi*freq*f*t)*np.exp(-t*(1+j*.6))*a for j,(f,a) in enumerate(zip(ratios,[1,.35,.2,.1])))
            x=fade(x,.08 if cue=='bell' else .025,.3 if length>.5 else .02)
            if cue in ('sanity','pressure','windstrike','emerge'):
                x=mix([x*.35,noise(length,1100)*np.sin(np.pi*t/length)**2])
        write(cue,normalized(x,.55 if cue in ('sanity','bell','pressure') else .38))


def ocean():
    waves=[]
    for i in range(4):
        fn=f'wave_0{i+1}_cc0-11505__transitking__wavesound.flac'
        x=read('water-waves',fn)
        x=fade(normalized(filt(x,4500),.5),.06,.2)
        waves.append(x)
        write(f'lap_{i}',x,sources=['water-waves/'+fn])
    n=RATE*60; sea=np.zeros((n,2),np.float32)
    for i in range(24):
        x=waves[i%4];pan=rng.uniform(-.6,.6)
        stereo=np.column_stack((x*np.sqrt((1-pan)/2),x*np.sqrt((1+pan)/2)))
        add(sea,stereo,i*2.51,.25)
    sea=normalized(filt(sea,2200),.34)
    write('sea',sea,True,['water-waves'])
    wind=np.column_stack((noise(47,700),noise(47,850)))
    t=np.arange(len(wind))/RATE
    wind*= (.6+.15*np.sin(t*2*np.pi/47*3)+.12*np.sin(t*2*np.pi/47*7))[:,None]
    # Crossfade the ends into a cyclic boundary.
    def cyclic(x):
        n=RATE
        w=np.linspace(0,1,n)[:,None]
        x[-n:]=x[-n:]*(1-w)+x[:n]*w
        return x[n:]
    write('wind',normalized(cyclic(wind),.17),True)
    wake=np.column_stack((noise(41,2800),noise(41,2600)))
    wake=filt(wake,170,'highpass')
    write('wake',normalized(cyclic(wake),.3),True)
    for i in range(3):
        x=waves[i];slow=signal.resample(x,int(len(x)*1.5))
        t=np.arange(len(slow))/RATE
        x=mix([slow*.7,noise(len(slow)/RATE,260)*np.exp(-t*.7)*.4])
        write(f'splash_{i}',normalized(x,.68),sources=['water-waves','original-synthesis'])


def score():
    mixes={}
    for scene,bpm,bars in [('harbor',64,24),('voyage',60,32),('deck',72,28),('boss',60,32)]:
        print('Composing',scene,flush=True)
        beat=60/bpm;length=bars*4*beat;n=round(length*RATE)
        layers=[np.zeros((n,2),np.float32) for _ in range(3)]
        roots=[38,38,34,36,38,41,39,33]
        for b in range(bars):
            root=roots[(b//4)%len(roots)];freq=440*2**((root-69)/12)
            if scene=='deck' or b%2==0:
                add(layers[0],tone(freq,beat*(3 if scene=='deck' else 5),'bow',-.22),b*4*beat,.75)
            if b%2==0:
                add(layers[0],tone(freq*1.5,beat*8,'reed',.3),b*4*beat,.35)
            if b%4==1 or b%4==3:
                for j,interval in enumerate([12,13,19]):
                    f=freq*2**(interval/12)
                    add(layers[1],tone(f,beat*(2.6 if scene=='harbor' else 3.5),'bow',[-.3,.18,.36][j]),(b*4+j*1.3)*beat,.6 if scene=='harbor' else .4)
            if scene in ('deck','boss'):
                for j in ([0,2,3.5] if scene=='deck' else [0]):
                    dur=.45 if scene=='deck' else 1.2
                    t=np.arange(int(dur*RATE))/RATE
                    hit=(np.sin(t*2*np.pi*(72 if scene=='deck' else 48))*np.exp(-t*9)+noise(dur,800)*np.exp(-t*23))*.06
                    add(layers[2],np.column_stack((hit,hit)),(b*4+j)*beat)
                add(layers[2],tone(freq*1.05946,beat*5,'reed',.45),b*4*beat,.12)
            else:
                add(layers[2],tone(freq*2.013,beat*7,'reed',-.4),b*4*beat,.15)
        layers=[room(x,.38) for x in layers]
        peak=max(np.max(np.abs(sum(layers))),.001)
        layers=[x*(.42/peak) for x in layers]
        # Listening revision: soften the persistent bass by 6 dB and leave breathing space.
        layers[0]*=.5
        layers[1]*=.85
        for i,x in enumerate(layers): write(f'music_{scene}_{i}',x,True)
        mixed=sum(layers);mixes[scene]=mixed
        p=SOURCE/'Masters'/f'{scene}.flac';p.parent.mkdir(parents=True,exist_ok=True)
        sf.write(p,mixed,RATE,subtype='PCM_24')
    return mixes


def audition(mixes):
    for variant,gain in [('restrained',.75),('uneasy',1.1)]:
        y=np.zeros((75*RATE,2),np.float32)
        def clip(name,at,volume=1,seconds=None):
            folder='Music' if name.startswith('music_') else 'Ambience' if name in ('sea','wind','wake') else 'SFX'
            ext='.ogg' if folder in ('Music','Ambience') else '.wav'
            x,_=sf.read(OUT/folder/(name+ext),dtype='float32',always_2d=True)
            if seconds: x=x[:round(seconds*RATE)]
            x=fade(x,.1,.5)
            pos=int(at*RATE);count=min(len(x),len(y)-pos)
            if x.shape[1]==1:x=np.repeat(x,2,axis=1)
            y[pos:pos+count]+=x[:count]*volume
        clip('sea',0,.6);clip('sea',55,.6,20)
        clip('music_voyage_0',0,.65,24);clip('music_voyage_1',3,.65,20)
        clip('wake',9,.7,5);clip('creak_0',11,.7)
        clip('music_deck_0',24,.65,20);clip('music_deck_2',24,gain,20)
        for at in [26,31,37]:
            clip('card_0',at,.6);clip('shot_0',at+.4,.65);clip('hit_metal_0',at+.48,.7)
        clip('slash',34,.8);clip('block_0',34.35,.7);clip('pay',39,.7);clip('sanity',42,.6)
        clip('music_boss_0',45,.65,23);clip('music_boss_2',45,gain,23)
        clip('emerge',47,.65);clip('splash_0',49,.8);clip('hull_0',51,.9);clip('creak_1',51.2,.8)
        clip('bell',56,.55);clip('sanity',57,.5);clip('retreat',66,.6)
        y=normalized(y,.85)
        p=SOURCE/'Previews'/f'soundscape-{variant}.wav';p.parent.mkdir(parents=True,exist_ok=True)
        sf.write(p,y,RATE,subtype='PCM_24')
        subprocess.run(['ffmpeg','-v','error','-y','-i',str(p),'-b:a','192k',str(p.with_suffix('.mp3'))],check=True)
        p.unlink()


if __name__=='__main__':
    print("Foley",flush=True);foley();print("Ocean",flush=True);ocean();scores=score();print("Auditions",flush=True);audition(scores)
    manifest={'version':1,'date':'2026-10-18','recipe':'Tools/build_audio.py','sample_rate':RATE,
              'original_material':'Additive/modal synthesis and original composition; no external instrument samples or generated-service output.',
              'processing':'CC0 sources resampled, trimmed, gain matched and layered. Original music exported as phase-aligned stems.',
              'sources':[{'path':str(p.relative_to(ROOT)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in sorted(used)],'assets':records}
    manifest['recipe_sha256']=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    manifest['deliverables']=[{'path':str(p.relative_to(ROOT)),'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'bytes':p.stat().st_size} for folder in ['Masters','Previews'] for p in sorted((SOURCE/folder).glob('*'))]
    manifest['preview_measurements']={'method':'FFmpeg ebur128=peak=true on exported 75-second MP3s'}
    for variant in ['restrained','uneasy']:
        proc=subprocess.run(['ffmpeg','-hide_banner','-i',str(SOURCE/'Previews'/f'soundscape-{variant}.mp3'),'-af','ebur128=peak=true','-f','null','-'],capture_output=True,text=True,check=True)
        summary=proc.stderr.rsplit('Summary:',1)[-1]
        manifest['preview_measurements'][variant]={'integrated_lufs':float(re.search(r'I:\s+([\d.-]+) LUFS',summary)[1]),'loudness_range_lu':float(re.search(r'LRA:\s+([\d.-]+) LU',summary)[1]),'true_peak_dbtp':float(re.search(r'Peak:\s+([\d.-]+) dBFS',summary)[1])}
    (SOURCE/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
    # Keep only sources actually used, plus their supplied licenses and provenance.
    for p in (SOURCE/'Sources').rglob('*.ogg'):
        if p not in used:p.unlink()
    print(f'Built {len(records)} runtime clips, four master scores and two 75-second auditions.')
