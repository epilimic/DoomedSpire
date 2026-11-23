#!/usr/bin/env python3
"""Regenerates tests/Engine.Tests/upstream/ from sinshu/managed-doom's ManagedDoomTest/src.

usage: python3 make-upstream-tests.py <path to managed-doom>/ManagedDoomTest/src

Why the files are filtered: upstream's tests run on the commercial DOOM.WAD / DOOM2.WAD / TNT.WAD / PLUTONIA.WAD, which we do not have. Freedoom stands in for
DOOM2.WAD, which is fine for a test that loads its own tiny test level on top of it (the level, not the IWAD's maps, decides the result) but not for a test that
plays the original maps. So a [TestMethod] is kept only if it does not mention WadPath at all (pure maths) or passes a test .wad from data/ to CreateDummy.
Besides dropping methods, the only edit is data\\ -> data/ (upstream's paths are Windows paths).
"""
import glob, os, re, sys

SRC = sys.argv[1]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'upstream')
PWAD = re.compile(r'WadPath\.\w+,\s*@"data\\[^"]*\.wad"')
kept = dropped = 0
for f in sorted(glob.glob(SRC + '/CompatibilityTests/*.cs') + glob.glob(SRC + '/UnitTests/*.cs')):
    lines = open(f, encoding='utf-8-sig').read().replace('\r\n', '\n').split('\n')
    idx = [i for i, l in enumerate(lines) if l.strip() == '[TestMethod]']
    if not idx:
        continue
    end = len(lines)
    while end > 0 and lines[end - 1].strip() == '':
        end -= 1
    foot = end - 2  # the closing "    }" of the class and "}" of the namespace
    chunks = ['\n'.join(lines[a:b]) for a, b in zip(idx, idx[1:] + [foot])]
    keep = [c for c in chunks if 'WadPath.' not in c or PWAD.search(c)]
    kept += len(keep)
    dropped += len(chunks) - len(keep)
    if keep:
        out = '\n'.join(['\n'.join(lines[:idx[0]])] + keep + ['\n'.join(lines[foot:end])]) + '\n'
        sub = os.path.join(OUT, os.path.basename(os.path.dirname(f)))
        os.makedirs(sub, exist_ok=True)
        open(os.path.join(sub, os.path.basename(f)), 'w').write(out.replace('data\\', 'data/'))
print(f'kept {kept} tests, dropped {dropped}')
