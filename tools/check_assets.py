"""Asset safety check: every art / sound / model file in the Unity project must come from the open asset set.

Usage: python -I tools/check_assets.py [NATIVE_REPO]

1. Every file in Assets/ProjectArena/Art/Open (except Unity .meta files and manifest.json) is listed in manifest.json
   with the same SHA-256, and every listed file exists.
2. With NATIVE_REPO: every source the manifest names is under app/assets_open there and still has the recorded SHA-256
   (so the converted files match today's open set; re-run tools/convert_assets.py when they don't). Outputs listed
   in tools/held_assets.txt keep their earlier open version on purpose, so their sources aren't compared.
3. No model, texture or sound file anywhere else under Assets/, unless tools/new_art.txt lists it (one path per line,
   relative to the repo root: new original art, reviewed by the owner).
Exits 1 and lists the problems when any check fails.
"""
import hashlib
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OPEN = os.path.join(ROOT, 'Assets', 'ProjectArena', 'Art', 'Open')
MEDIA = ('.mdl', '.fbx', '.obj', '.glb', '.gltf', '.blend', '.dae', '.3ds', '.max', '.ma', '.mb', '.uasset', '.umap',
         '.png', '.jpg', '.jpeg', '.tga', '.tif', '.tiff', '.psd', '.exr', '.hdr', '.bmp', '.gif', '.dds', '.ktx',
         '.wav', '.ogg', '.mp3', '.aif', '.aiff', '.flac', '.bank', '.mp4', '.webm', '.mov', '.ttf', '.otf')


def sha256(path):
    with open(path, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest()


def main():
    bad = []
    mpath = os.path.join(OPEN, 'manifest.json')
    if not os.path.isfile(mpath):
        sys.exit('asset check FAILED: no %s (run tools/convert_assets.py)' % os.path.relpath(mpath, ROOT))
    with open(mpath) as f:
        files = json.load(f)['files']

    held = set()   # tools/held_assets.txt: outputs that keep their earlier open version
    hpath = os.path.join(ROOT, 'tools', 'held_assets.txt')
    if os.path.isfile(hpath):
        with open(hpath) as f:
            held = {l.split('#')[0].strip().replace('\\', '/') for l in f if l.split('#')[0].strip()}

    seen = set()
    for dp, _, names in os.walk(OPEN):
        for n in names:
            rel = os.path.relpath(os.path.join(dp, n), OPEN).replace(os.sep, '/')
            if rel.endswith('.meta') or rel == 'manifest.json':
                continue
            seen.add(rel)
            if rel not in files:
                bad.append('Art/Open/%s: not in the manifest' % rel)
            elif sha256(os.path.join(dp, n)) != files[rel]['sha256']:
                bad.append('Art/Open/%s: differs from the manifest' % rel)
    bad += ['Art/Open/%s: in the manifest but missing' % rel for rel in sorted(set(files) - seen)]

    if len(sys.argv) > 1:
        native = os.path.abspath(sys.argv[1])
        for rel, entry in sorted(files.items()):
            if rel in held:
                continue
            for src in entry['from']:
                p = os.path.normpath(os.path.join(native, src['path']))
                if not src['path'].startswith('app/assets_open/') or '..' in src['path'].split('/'):
                    bad.append('%s: source %s is not in app/assets_open' % (rel, src['path']))
                elif not os.path.isfile(p):
                    bad.append('%s: source %s no longer exists' % (rel, src['path']))
                elif sha256(p) != src['sha256']:
                    bad.append('%s: source %s has changed (re-run convert_assets.py)' % (rel, src['path']))

    allowed = set()
    listed = os.path.join(ROOT, 'tools', 'new_art.txt')
    if os.path.isfile(listed):
        with open(listed) as f:
            allowed = {l.strip().replace('\\', '/') for l in f if l.strip() and not l.startswith('#')}
    assets = os.path.join(ROOT, 'Assets')
    for dp, _, names in os.walk(assets):
        if os.path.abspath(dp).startswith(OPEN):
            continue
        for n in names:
            if n.lower().endswith(MEDIA):
                rel = os.path.relpath(os.path.join(dp, n), ROOT).replace(os.sep, '/')
                if rel not in allowed:
                    bad.append('%s: media file outside Art/Open and not in tools/new_art.txt' % rel)

    if bad:
        print('asset check FAILED (%d):\n  %s' % (len(bad), '\n  '.join(bad[:50])))
        sys.exit(1)
    print('asset check: %d files in Art/Open, all from the open set%s; no other media files' %
          (len(files), ' (sources verified; %d held at an earlier version)' % len(held & set(files))
           if len(sys.argv) > 1 else ''))


if __name__ == '__main__':
    main()
