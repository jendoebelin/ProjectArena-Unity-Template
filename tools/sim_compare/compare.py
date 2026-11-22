"""Same-results check, bit for bit: runs the IL2CPP compare player (Build/SimCompare/SimCompare.exe, made by
Builds.SimCompare) and compares its traces with the native ones (tools/sim_compare/traces/*.trace.gz). Prints, per
scenario, the first difference if any. Exit code 1 if any scenario differs.
Usage: python -I tools/sim_compare/compare.py [player exe]"""
import gzip
import os
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
exe = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'Build', 'SimCompare', 'SimCompare.exe')
out = os.path.join(ROOT, 'Build', 'SimCompare', 'traces')
os.makedirs(out, exist_ok=True)
subprocess.check_call([exe, '-batchmode', '-nographics', '-logFile', os.path.join(out, 'player.log'), '-out', out])
bad = 0
names = sorted(f[:-9] for f in os.listdir(os.path.join(ROOT, 'tools', 'sim_compare', 'traces')) if f.endswith('.trace.gz'))
for name in names:
    native = gzip.open(os.path.join(ROOT, 'tools', 'sim_compare', 'traces', name + '.trace.gz'), 'rt').read().splitlines()
    path = os.path.join(out, name + '.trace')
    mine = open(path).read().splitlines() if os.path.exists(path) else []
    first = next((i for i, (a, b) in enumerate(zip(native, mine)) if a != b), None)
    if first is None and len(native) == len(mine):
        print('%-16s %6d lines, bit-identical' % (name, len(native)))
        continue
    bad += 1
    if first is None:
        print('%-16s different length: native %d, C# %d lines' % (name, len(native), len(mine)))
    else:
        print('%-16s first difference at line %d of %d:\n  native %s\n  C#     %s' % (name, first + 1, len(native), native[first], mine[first]))
sys.exit(1 if bad else 0)
