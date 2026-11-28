#!/usr/bin/env python3
"""Run 239 (V1 of claude/studio-voice-plan.md): download the voice pack's model files and check their pins.

    python3 tools/Voice/fetch.py <out folder>

Reads tools/Voice/pin.json, tries each file's URLs in order (GitHub first, then the Hugging Face mirror), and keeps a
download only if its size and sha256 match the pin. Copies pin.json into the folder as voice-pack.json. Exits non-zero
if any file can't be fetched with the pinned checksum. Nothing it downloads is ever committed.
"""
import hashlib
import json
import os
import shutil
import sys
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(HERE, "pin.json"), encoding="utf-8") as f:
        pin = json.load(f)
    for file in pin["files"]:
        target = os.path.join(out, file["name"])
        if os.path.exists(target) and os.path.getsize(target) == file["bytes"] and sha256(target) == file["sha256"]:
            print(f"ok   {file['name']} (already here)")
            continue
        for url in file["urls"]:
            part = target + ".part"
            try:
                print(f"get  {url}")
                with urllib.request.urlopen(url, timeout=120) as r, open(part, "wb") as w:
                    shutil.copyfileobj(r, w)
            except Exception as e:  # a refused or broken mirror: try the next one
                print(f"     failed: {e}")
                continue
            size, digest = os.path.getsize(part), sha256(part)
            if size == file["bytes"] and digest == file["sha256"]:
                os.replace(part, target)
                print(f"ok   {file['name']} {size:,} bytes, sha256 {digest}")
                break
            os.remove(part)
            print(f"     wrong file: {size:,} bytes, sha256 {digest}")
        else:
            print(f"FAIL {file['name']}: no URL gave the pinned file")
            return 1
    shutil.copyfile(os.path.join(HERE, "pin.json"), os.path.join(out, "voice-pack.json"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
