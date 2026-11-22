"""Compresses the native traces into tools/sim_compare/traces/*.trace.gz (fixed timestamps: the same traces give the
same files) and notes the native commit they came from. Usage: pack.py <build dir> <traces dir> <native repo>"""
import gzip
import os
import subprocess
import sys

src, dst, native = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(dst, exist_ok=True)
for f in sorted(os.listdir(src)):
    if f.endswith('.trace'):
        data = open(os.path.join(src, f), 'rb').read()
        with open(os.path.join(dst, f + '.gz'), 'wb') as out:
            with gzip.GzipFile(fileobj=out, mode='wb', mtime=0, filename='') as z:
                z.write(data)
        print('%-24s %8d bytes -> %7d' % (f, len(data), os.path.getsize(os.path.join(dst, f + '.gz'))))
commit = subprocess.check_output(['git', '-C', native, 'rev-parse', 'HEAD'], text=True).strip()
with open(os.path.join(dst, 'SOURCE.txt'), 'w', newline='\n') as f:
    f.write('Native simulation traces from github.com/jendoebelin/ProjectArena commit %s (app/src/game.cpp),\n'
            'made by tools/sim_compare/run.bat. Compared by Assets/ProjectArena/Tests/EditMode/SameResultsTests.cs.\n' % commit)
