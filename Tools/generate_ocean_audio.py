"""Deterministic, original synthesized sea bed and brass cue. No external samples."""
from array import array
from pathlib import Path
import math, random, wave

OUT = Path(__file__).resolve().parents[1] / 'Assets/UnsoundedSea/Art/OceanBattle'
RATE = 22050

def write(name, samples):
    with wave.open(str(OUT / name), 'wb') as f:
        f.setnchannels(1); f.setsampwidth(2); f.setframerate(RATE)
        data = array('h', (int(max(-1, min(1, x)) * 32767) for x in samples))
        f.writeframes(data.tobytes())

rng = random.Random(47)
low = 0.0
sea = []
for i in range(RATE * 16):
    t = i / RATE
    low = low * 0.975 + rng.uniform(-1, 1) * 0.025
    swell = 0.55 + 0.3 * math.sin(2 * math.pi * t / 8)
    drone = math.sin(2 * math.pi * 48 * t) + 0.55 * math.sin(2 * math.pi * 48.1875 * t)
    sea.append(low * swell * 1.2 + drone * 0.028)
# Crossfade to a cyclic join, avoiding a discontinuity at the loop boundary.
n = RATE
for i in range(n):
    u = i / n
    sea[-n+i] = sea[-n+i] * (1-u) + sea[i] * u
write('FogSea.wav', sea[n:])
write('BrassSeal.wav', ((math.sin(2*math.pi*220*i/RATE) + 0.35*math.sin(2*math.pi*587*i/RATE)) * 0.13 * math.exp(-i/RATE*3) * min(1,i/200) for i in range(RATE*2)))
write('HullImpact.wav', ((math.sin(2*math.pi*74*i/RATE)*0.18+rng.uniform(-1,1)*0.035) * math.exp(-i/RATE*8)*min(1,i/100) for i in range(RATE)))
