#!/usr/bin/env python3
"""Fight songs B (run 167): turn a single-voice LilyPond melody (\\relative or \\fixed, English or Dutch note names)
into the `melody:` line of a fight-song file (tools/Assets/fightsongs/README.md), split into bars and checked.

usage: lily2fs.py <file.ly> <beats-per-bar> [--pickup BEATS]
Prints the melody with a `|` at every bar line and fails if a bar doesn't add up.
Durations are in quarter-note beats; ties are merged; rests are `r`.
"""
import re, sys
from fractions import Fraction as F

STEPS = {'c': 0, 'd': 2, 'e': 4, 'f': 5, 'g': 7, 'a': 9, 'b': 11}
NAMES = ['C', 'Db', 'D', 'Eb', 'E', 'F', 'Gb', 'G', 'Ab', 'A', 'Bb', 'B']
NOTE = re.compile(r"^([a-g])(is|es|s|f|ff|ss|isis|eses|sharp|flat|x)?([',]*)(!?)(\d+)?(\.*)(~?)$|^(r)(\d+)?(\.*)$|^(\d+)(\.*)(~?)$")

def alter(acc):
    return {None: 0, '': 0, 'is': 1, 's': 1, 'sharp': 1, 'x': 2, 'ss': 2, 'isis': 2, 'es': -1, 'f': -1, 'flat': -1, 'ff': -2, 'eses': -2}.get(acc, 0)

def parse(text):
    text = re.sub(r"%.*", "", text)
    text = re.sub(r"\\addlyrics\s*\{.*", "", text, flags=re.S)
    m = re.search(r"\\(relative|fixed)\s+([a-g][',]*)", text)
    mode, ref = m.group(1), m.group(2)
    body = text[m.end():]
    def abs_pitch(tok):  # c' = 60
        return 48 + STEPS[tok[0]] + 12 * (tok.count("'") - tok.count(","))
    ref_midi = abs_pitch(ref)
    ref_step = 'cdefgab'.index(ref[0]) + 7 * (ref.count("'") - ref.count(",") + 3)
    # Strip the commands (and their arguments) that aren't notes.
    for pat in (r"\\partial\s*\d+\.*", r"\\tempo\s*(\"[^\"]*\")?\s*(\d+\s*=\s*\d+)?", r"\\time\s*\d+/\d+", r"\\key\s+[a-g][a-z]*\s*\\(major|minor)",
                r"\\clef\s+\w+", r"\\set\s+[\w.]+\s*=\s*(#?\"[^\"]*\"|##?[tf]|#'\(\)|\S+)", r"\\unset\s+[\w.]+", r"\\bar\s*\"[^\"]*\"",
                r"\\new\s+\w+", r"\\repeat\s+volta\s+\d+", r"\\[A-Za-z]+"):
        body = re.sub(pat, " ", body)
    body = re.sub(r"[{}()|\[\]]", " ", body).replace("~", "~ ")
    toks = body.split()
    out = []  # (midi or None, Fraction quarters, tied)
    last_dur = F(1)
    prev_step, prev_midi = ref_step, ref_midi
    for t in toks:
        mm = NOTE.match(t)
        if not mm:
            if re.match(r"^[\d.]+$", t) or t in ('~',):
                pass
            continue
        if mm.group(1):
            letter, acc, octs, _, dur, dots, tie = mm.group(1, 2, 3, 4, 5, 6, 7)
            idx = 'cdefgab'.index(letter)
            if mode == 'relative':
                # The nearest step to the previous note, then the octave marks.
                base = prev_step - (prev_step % 7) + idx
                cands = [base - 7, base, base + 7]
                step = min(cands, key=lambda s: abs(s - prev_step))
                step += 7 * (octs.count("'") - octs.count(","))
            else:
                step = ref_step - (ref_step % 7) + idx + 7 * (octs.count("'") - octs.count(","))
            octave = step // 7
            midi = 12 * (octave + 1) + STEPS[letter] + alter(acc)   # step 28 = c in octave 4 -> MIDI 60
            if mode == 'relative': prev_step = step
            pitch = midi
        elif mm.group(8):
            pitch, dur, dots, tie = None, mm.group(9), mm.group(10), ''
        else:
            pitch, dur, dots, tie = (out[-1][0] if out else None), mm.group(11), mm.group(12), mm.group(13)
        d = F(4, int(dur)) if dur else last_dur
        if dur:
            last_dur = F(4, int(dur))
            base = d
            for k in range(len(dots or '')):
                base /= 2; d += base
            last_dur = d
        out.append([pitch, d, bool(tie)])
    # Merge ties.
    merged = []
    for p, d, tie in out:
        if merged and merged[-1][2] and merged[-1][0] == p:
            merged[-1][1] += d; merged[-1][2] = tie
        else:
            merged.append([p, d, tie])
    return merged

def name(midi):
    return 'r' if midi is None else f"{NAMES[midi % 12]}{midi // 12 - 1}"

def fmt(d):
    return str(d.numerator) if d.denominator == 1 else f"{d.numerator}/{d.denominator}"

def bars(notes, per_bar, pickup=F(0)):
    """Split into bars; a note held over a bar line continues as `_` (a tie) in the next bar."""
    lines, cur, pos, limit = [], [], F(0), (pickup or per_bar)
    for p, d, _ in notes:
        first = True
        while d > 0:
            take = min(d, limit - pos)
            cur.append(f"{name(p) if first else '_'}:{fmt(take)}"); pos += take; d -= take; first = False
            if pos == limit:
                lines.append(' '.join(cur)); cur, pos, limit = [], F(0), per_bar
    if cur: lines.append(' '.join(cur) + f"   %% short last bar: {fmt(pos)}")
    return lines

if __name__ == '__main__':
    src = open(sys.argv[1]).read()
    per = F(sys.argv[2])
    pick = F(sys.argv[sys.argv.index('--pickup') + 1]) if '--pickup' in sys.argv else F(0)
    for i, b in enumerate(bars(parse(src), per, pick), 1):
        print(f"{b} |   % {i}")
