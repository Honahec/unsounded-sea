"""Original low metallic bell; deterministic synthesis, no external samples."""
from array import array
from pathlib import Path
import math, wave
path=Path(__file__).resolve().parents[1]/'Assets/UnsoundedSea/Art/SeaBoss'
path.mkdir(parents=True,exist_ok=True)
rate=22050
samples=[]
for i in range(rate*4):
    t=i/rate
    # Inharmonic partials and a gradual onset; bounded well below full-scale.
    v=sum(a*math.sin(2*math.pi*f*t)*math.exp(-t*d) for f,a,d in [(48,.2,1.4),(101.4,.09,1.8),(139.7,.07,2.1),(219.3,.03,2.9)])
    samples.append(int(v*min(1,t/.06)*32767))
with wave.open(str(path/'DeepBell.wav'),'wb') as f:
    f.setnchannels(1);f.setsampwidth(2);f.setframerate(rate);f.writeframes(array('h',samples).tobytes())
