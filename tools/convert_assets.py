"""Convert the native game's open asset set (app/assets_open, CC BY 4.0) into Unity-ready files.

Usage: python -I tools/convert_assets.py NATIVE_REPO [OUT_DIR]
       NATIVE_REPO: the native Project Arena checkout (read only; nothing in it is written)
       OUT_DIR:     default Assets/ProjectArena/Art/Open

- Models: *.mdl copied as they are to Models/ (Assets/ProjectArena/Editor/MdlImporter.cs imports them with every
  native material value, vertex colours and both UV sets; for Blender, the native repo's tools/export_glb.py makes
  glTF).
- Textures: tex/*.tex (STEX: magic, width, height, 0, then RGBA8 rows) -> Textures/*.png.
- Sounds: audio/*.wav copied as they are; audio/events.txt kept (event -> sound names).
- Data: *.col, *.swing, *.tuck, *.swish, *.hinge, *.lod -> Data/<name>.<ext>.bytes (Unity TextAssets for the C# loaders;
  MdlImporter reads a model's .lod).
- vr_splash.png and NOTICE.txt copied.
- tools/held_assets.txt: outputs that keep their earlier open version, because the native file's newer version is under
  other terms than CC BY 4.0 (a contributed sound, for one); their files and manifest entries stay as they were.
- manifest.json: for every output, its SHA-256 and the open source file(s) it came from with theirs. tools/check_assets.py
  uses it to prove that every file here comes from the open set. Unity's .meta files are kept for outputs that still
  exist; outputs (and their .meta) that are no longer produced are removed.
Re-running on the same sources gives the same files.
"""
import hashlib
import json
import os
import shutil
import struct
import subprocess
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_OUT = os.path.join(ROOT, 'Assets', 'ProjectArena', 'Art', 'Open')
DATA_EXT = ('.col', '.swing', '.tuck', '.swish', '.hinge', '.lod')   # (.swish: the skirts' sway shapes, native 0.3.21; .hinge:
                                                                   # the grapple gauntlets' fingers, .lod: FAR AWAY's lighter
                                                                   # triangles, native 0.3.22)


def sha256(path):
    with open(path, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest()


def write_png(path, w, h, rgba):
    """Minimal deterministic RGBA8 PNG writer (no Pillow needed, same bytes every run)."""
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xFFFFFFFF)
    stride = w * 4
    raw = b''.join(b'\0' + rgba[y * stride:(y + 1) * stride] for y in range(h))
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>2I5B', w, h, 8, 6, 0, 0, 0)) +
                chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


def tex_to_png(src, dst):
    with open(src, 'rb') as f:
        b = f.read()
    if b[:4] != b'STEX':
        raise ValueError('%s: not a .tex file' % src)
    w, h, fmt = struct.unpack_from('<3I', b, 4)
    if fmt != 0 or len(b) != 16 + w * h * 4:
        raise ValueError('%s: unexpected .tex layout (format %d, %d bytes for %dx%d)' % (src, fmt, len(b), w, h))
    write_png(dst, w, h, b[16:])


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    native = os.path.abspath(sys.argv[1])
    out = os.path.abspath(sys.argv[2]) if len(sys.argv) > 2 else DEFAULT_OUT
    src_dir = os.path.join(native, 'app', 'assets_open')
    if not os.path.isdir(src_dir):
        sys.exit('not a Project Arena checkout (no app/assets_open): ' + native)
    held = set()   # tools/held_assets.txt: outputs that keep their earlier open version
    hpath = os.path.join(ROOT, 'tools', 'held_assets.txt')
    if os.path.isfile(hpath):
        with open(hpath) as f:
            held = {l.split('#')[0].strip().replace('\\', '/') for l in f if l.split('#')[0].strip()}
    old = {}
    if os.path.isfile(os.path.join(out, 'manifest.json')):
        with open(os.path.join(out, 'manifest.json')) as f:
            old = json.load(f)['files']

    produced = {}   # output path relative to out -> list of source paths relative to native

    def add(rel_out, rel_srcs):
        produced[rel_out.replace(os.sep, '/')] = [s.replace(os.sep, '/') for s in rel_srcs]

    def target(rel):
        p = os.path.join(out, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        return p

    def is_held(rel):
        return rel in held and rel in old and os.path.isfile(os.path.join(out, rel))

    def copy_if_changed(src, dst):
        if is_held(os.path.relpath(dst, out).replace(os.sep, '/')):
            return
        if not (os.path.isfile(dst) and sha256(dst) == sha256(src)):
            shutil.copyfile(src, dst)

    a = 'app/assets_open/'
    for f in sorted(os.listdir(os.path.join(src_dir, 'tex'))):
        if f.endswith('.tex'):
            rel = os.path.join('Textures', f[:-4] + '.png')
            tmp_png = target(rel) + '.tmp'
            tex_to_png(os.path.join(src_dir, 'tex', f), tmp_png)
            copy_if_changed(tmp_png, target(rel))
            os.remove(tmp_png)
            add(rel, [a + 'tex/' + f])

    for f in sorted(os.listdir(os.path.join(src_dir, 'audio'))):
        if f.endswith(('.wav', '.txt')):
            rel = os.path.join('Audio', f)
            copy_if_changed(os.path.join(src_dir, 'audio', f), target(rel))
            add(rel, [a + 'audio/' + f])

    for f in sorted(os.listdir(src_dir)):
        p = os.path.join(src_dir, f)
        if f.endswith('.mdl'):
            rel = os.path.join('Models', f)
        elif f.endswith(DATA_EXT):
            rel = os.path.join('Data', f + '.bytes')
        elif f in ('vr_splash.png', 'NOTICE.txt'):
            rel = f
        else:
            continue
        copy_if_changed(p, target(rel))
        add(rel, [a + f])

    # every source file must have been used
    unused = []
    for dp, _, files in os.walk(src_dir):
        for f in files:
            rel = os.path.relpath(os.path.join(dp, f), native).replace(os.sep, '/')
            if not any(rel in s for s in produced.values()):
                unused.append(rel)
    if unused:
        print('warning: open files not converted:', ', '.join(sorted(unused)))

    # remove outputs no longer produced (and orphaned .meta files)
    for dp, _, files in os.walk(out):
        for f in files:
            rel = os.path.relpath(os.path.join(dp, f), out).replace(os.sep, '/')
            base = rel[:-5] if rel.endswith('.meta') else rel
            if base == 'manifest.json' or base in produced or os.path.isdir(os.path.join(out, base)):
                continue
            os.remove(os.path.join(dp, f))

    try:
        commit = subprocess.check_output(['git', '-C', native, 'rev-parse', 'HEAD'], text=True).strip()
    except (OSError, subprocess.CalledProcessError):
        commit = ''
    manifest = {
        'source': 'Project Arena app/assets_open (CC BY 4.0); converted by tools/convert_assets.py',
        'source_commit': commit,
        'files': {rel: old[rel] if is_held(rel) else
                  {'sha256': sha256(os.path.join(out, rel)),
                   'from': [{'path': s, 'sha256': sha256(os.path.join(native, s))} for s in srcs]}
                  for rel, srcs in sorted(produced.items())},
    }
    with open(os.path.join(out, 'manifest.json'), 'w', newline='\n') as f:
        json.dump(manifest, f, indent=1, sort_keys=True)
        f.write('\n')
    kinds = {}
    for rel in produced:
        k = rel.split('/')[0] if '/' in rel else 'other'
        kinds[k] = kinds.get(k, 0) + 1
    print('converted %d files -> %s (%s)' % (len(produced), out,
                                            ', '.join('%s %d' % kv for kv in sorted(kinds.items()))))
    for rel in sorted(r for r in produced if is_held(r)):
        print('held at its earlier open version (tools/held_assets.txt): ' + rel)


if __name__ == '__main__':
    main()
