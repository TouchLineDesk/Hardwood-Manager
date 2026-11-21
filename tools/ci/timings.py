#!/usr/bin/env python3
"""Seconds per test class from a .trx file, for shard.py: timings.py results.trx > tools/ci/timings.json"""
import collections, json, re, sys
s = open(sys.argv[1], encoding="utf-8").read()
secs = collections.Counter()
for m in re.finditer(r'<UnitTestResult [^>]*?testName="([^"]+)"[^>]*?duration="(\d+):(\d+):([\d.]+)"', s):
    cls = m.group(1).split("(", 1)[0].rsplit(".", 1)[0]
    secs[cls] += int(m.group(2)) * 3600 + int(m.group(3)) * 60 + float(m.group(4))
print(json.dumps({k: round(v, 1) for k, v in sorted(secs.items())}, indent=1))
