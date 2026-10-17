"""Verify rendered samples, loop seams, aligned stems, provenance and hashes (no Unity needed)."""
from pathlib import Path
import json,hashlib
import numpy as np
import soundfile as sf
from scipy.signal import resample_poly

root=Path(__file__).resolve().parents[1]
manifest=json.loads((root/'ArtSource/Audio/manifest.json').read_text())
results=[];stems={}
for asset in manifest['assets']:
 p=root/asset['path'];x,sr=sf.read(p,dtype='float32',always_2d=True)
 assert sr==48000 and np.isfinite(x).all(),p
 assert hashlib.sha256(p.read_bytes()).hexdigest()==asset['sha256'],p
 assert abs(len(x)/sr-asset['seconds'])<.001,p
 peak=float(np.max(np.abs(x)));rms=float(np.sqrt(np.mean(x*x)))
 assert .0001<rms and peak<.98,(p,peak,rms)
 seam=float(np.max(np.abs(x[0]-x[-1])))
 if asset['loop']:assert seam<.03,(p,'loop seam',seam)
 # 4x reconstruction peak estimate, processed in chunks to bound memory.
 true_peak=max(float(np.max(np.abs(resample_poly(x[start:start+sr*10],4,1,axis=0)))) for start in range(0,len(x),sr*10))
 assert true_peak<1,(p,'intersample clipping',true_peak)
 if asset['id'].startswith('music_'):
  group=asset['id'].rsplit('_',1)[0];stems.setdefault(group,[]).append((len(x),sr))
 results.append({'id':asset['id'],'samples':len(x),'peak_dbfs':round(20*np.log10(peak),2),'rms_dbfs':round(20*np.log10(rms),2),'estimated_true_peak_dbtp':round(20*np.log10(true_peak),2),'loop_boundary_delta':round(seam,6) if asset['loop'] else None})
assert len(stems)==4 and all(len(v)==3 and len(set(v))==1 for v in stems.values()),stems
for source in manifest['sources']:
 p=root/source['path'];assert hashlib.sha256(p.read_bytes()).hexdigest()==source['sha256'],p
report={'date':'2026-10-18','status':'passed','scope':'Offline sample decoding, source/output hashes, 48 kHz, finite/nonempty samples, no reconstructed clipping, loop seams <0.03 amplitude, four aligned three-stem scores. Not a subjective listening or device-performance approval.', 'runtime_clip_count':len(results),'source_file_count':len(manifest['sources']),'clips':results}
(root/'ArtSource/Audio/audio-check.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({k:v for k,v in report.items() if k!='clips'},indent=2))
