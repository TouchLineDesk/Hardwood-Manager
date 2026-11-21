#!/usr/bin/env python3
"""Split the test classes into CI shards (Danny, Nov 21, 2026: shard CI's tests three ways).

Usage: shard.py <index> <count> <list-tests output file> [timings.json]
Prints a dotnet test --filter expression that selects every test class in shard <index> (0-based).
Classes are balanced by their recorded seconds (tools/ci/timings.json, from a full run's .trx;
unknown classes count as the median), largest first onto the lightest shard. Every class lands in
exactly one shard, so the shards together run the whole suite.
"""
import json, os, statistics, sys

def classes(listing):
    found = []
    listed = False
    for line in open(listing, encoding="utf-8"):
        if line.startswith("The following Tests are available"):
            listed = True
            continue
        name = line.strip()
        if not listed or not name:
            continue
        # A test the filter can't name (a DisplayName, another namespace) would silently never run: stop instead.
        if not line.startswith("    ") or not name.startswith("Hardwood.") or "." not in name.split("(", 1)[0]:
            sys.exit(f"shard.py: can't map listed test to a class: {name!r}")
        cls = name.split("(", 1)[0].rsplit(".", 1)[0]
        if cls not in found:
            found.append(cls)
    return found

def shards(names, count, timings):
    known = [timings[n] for n in names if n in timings]
    default = statistics.median(known) if known else 1.0
    load = [0.0] * count
    out = [[] for _ in range(count)]
    for n in sorted(names, key=lambda n: (-timings.get(n, default), n)):
        i = min(range(count), key=lambda k: (load[k], k))
        out[i].append(n)
        load[i] += timings.get(n, default)
    return out, load

def main():
    index, count, listing = int(sys.argv[1]), int(sys.argv[2]), sys.argv[3]
    path = sys.argv[4] if len(sys.argv) > 4 else os.path.join(os.path.dirname(__file__), "timings.json")
    timings = json.load(open(path)) if os.path.exists(path) else {}
    names = classes(listing)
    if not names:
        sys.exit("no tests listed")
    out, load = shards(names, count, timings)
    note = f"about {load[index] / 60:.1f} min recorded" if timings else "no timings.json: split by class count"
    sys.stderr.write(f"shard {index + 1}/{count}: {len(out[index])} classes, {note}\n")
    # The trailing dot keeps "Run6Tests." from matching "Run68Tests.".
    print("|".join(f"FullyQualifiedName~{n}." for n in sorted(out[index])))

if __name__ == "__main__":
    main()
