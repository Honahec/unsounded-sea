"""Resume explicit Hyper3D binding jobs; never retry an ambiguous submission.

RODIN_API_KEY must be provided in the environment. Private job subscription keys
stay in ignored Logs; public provenance is written separately after validation.
Usage: python Tools/Hyper3DRigBatch.py --names FogRigger LostSailor
Omit --names to process all 25 ordinary humanoid source meshes from main.
"""
import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import struct
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
STATE = ROOT / 'Logs/AssetDesign/hyper3d-humanoids'
RAW = ROOT / 'ArtSource/Characters/Hyper3DRigs'
BASE = 'https://api.hyper3d.com/api/v2/'


def save(path, data):
    temp = path.with_suffix('.tmp')
    temp.write_text(json.dumps(data, indent=2) + '\n')
    temp.replace(path)


def request(endpoint, body, content_type='application/json', timeout=180):
    req = urllib.request.Request(BASE + endpoint, data=body, method='POST', headers={
        'Authorization': 'Bearer ' + os.environ['RODIN_API_KEY'],
        'Content-Type': content_type,
    })
    try:
        with urllib.request.urlopen(req, timeout=timeout) as response:
            data = json.loads(response.read())
    except urllib.error.HTTPError as error:
        # Never log credentials, request headers, or signed download URLs.
        raise RuntimeError(f'{endpoint}: HTTP {error.code}') from None
    if data.get('error'):
        raise RuntimeError(f"{endpoint}: {data['error']}: {data.get('message', '')}")
    return data


def run(name, source):
    state_path = STATE / (name + '.json')
    raw = source.read_bytes()
    if raw[:4] != b'glTF' or struct.unpack_from('<I', raw, 8)[0] != len(raw):
        raise ValueError(f'{name}: hydrate the LFS source before submitting')
    sha = hashlib.sha256(raw).hexdigest()
    state = json.loads(state_path.read_text()) if state_path.exists() else {}
    if state and state['source_sha256'] != sha:
        raise ValueError(f'{name}: source changed; explicit revision required')
    target = RAW / (name + '_Raw.glb')
    if not state and target.exists():
        raise RuntimeError(f'{name}: archived rig exists; recover private task state or use the public manifest, do not resubmit')
    if state.get('download_sha256') and target.exists():
        if hashlib.sha256(target.read_bytes()).hexdigest() != state['download_sha256']:
            raise ValueError(f'{name}: downloaded result hash mismatch')
        print(name, 'already downloaded', flush=True)
        return
    if not state:
        state = {'name': name, 'source': str(source.relative_to(ROOT)),
                 'source_sha256': sha, 'request': {'bone_template': 'unity',
                 'material': 'None', 'geometry_file_format': 'glb',
                 'rigging_mode': 'generate', 'skinning_method': 'shell'},
                 'submission_started': time.time()}
        save(state_path, state)
        boundary = uuid.uuid4().hex
        parts = []
        for key, value in state['request'].items():
            parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{key}"\r\n\r\n{value}\r\n'.encode())
        parts += [f'--{boundary}\r\nContent-Disposition: form-data; name="model"; filename="{source.name}"\r\nContent-Type: model/gltf-binary\r\n\r\n'.encode(), raw,
                  f'\r\n--{boundary}--\r\n'.encode()]
        submitted = request('rigging', b''.join(parts), 'multipart/form-data; boundary=' + boundary)
        if not submitted.get('uuid') or not submitted.get('jobs', {}).get('subscription_key'):
            raise RuntimeError(f'{name}: incomplete submit response; do not resubmit blindly')
        state['submission'] = submitted
        save(state_path, state)
        print(name, 'submitted', submitted['uuid'], flush=True)
    if 'submission' not in state:
        raise RuntimeError(f'{name}: submission outcome unknown; inspect account before retrying')
    deadline = time.monotonic() + 1800
    previous = None
    while True:
        status = request('status', json.dumps({'subscription_key': state['submission']['jobs']['subscription_key']}).encode(), timeout=60)
        state['status'] = status
        save(state_path, state)
        jobs = status.get('jobs', [])
        current = ','.join(j['status'] for j in jobs)
        if current != previous:
            print(name, current, flush=True)
            previous = current
        if any(j['status'] == 'Failed' for j in jobs):
            raise RuntimeError(f'{name}: API job failed; no automatic paid resubmission')
        if jobs and all(j['status'] == 'Done' for j in jobs):
            break
        if time.monotonic() >= deadline:
            raise TimeoutError(f'{name}: polling deadline; resume this existing job later')
        time.sleep(15)
    files = request('download', json.dumps({'task_uuid': state['submission']['uuid']}).encode())
    glbs = [f for f in files.get('list', []) if f['name'].lower().endswith('.glb')]
    if len(glbs) != 1:
        raise RuntimeError(f'{name}: expected one GLB output, got {len(glbs)}')
    with urllib.request.urlopen(glbs[0]['url'], timeout=180) as response:
        result = response.read()
    if result[:4] != b'glTF' or struct.unpack_from('<I', result, 8)[0] != len(result):
        raise ValueError(f'{name}: incomplete GLB download')
    target.write_bytes(result)
    state.update(download_sha256=hashlib.sha256(result).hexdigest(), download_bytes=len(result),
                 output=str(target.relative_to(ROOT)))
    save(state_path, state)
    print(name, 'downloaded', len(result), flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--names', nargs='+')
    parser.add_argument('--workers', type=int, default=3, choices=(1, 2, 3))
    args = parser.parse_args()
    sources = {p.stem.removesuffix('_Source'): p for p in (ROOT / 'ArtSource/Characters').glob('*_Source.glb')}
    sources['FogRigger'] = ROOT / 'ArtSource/Characters/FogRigger_LOD0.glb'
    names = args.names or sorted(sources)
    if any(name not in sources for name in names):
        parser.error('Unknown source name')
    STATE.mkdir(parents=True, exist_ok=True)
    RAW.mkdir(parents=True, exist_ok=True)
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = {pool.submit(run, name, sources[name]): name for name in names}
        failed = []
        for future in concurrent.futures.as_completed(futures):
            try:
                future.result()
            except Exception as error:
                failed.append(futures[future])
                print('ERROR', futures[future], str(error), flush=True)
    if failed:
        raise SystemExit('Incomplete: ' + ', '.join(failed))


if __name__ == '__main__':
    main()
