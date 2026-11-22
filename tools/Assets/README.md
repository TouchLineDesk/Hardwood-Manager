# Game assets from bought or downloaded packs

The raw packs live **only on Danny's PC**, in `C:\Users\Danie\OneDrive\Desktop\HW\asset`
(`audio\`, `floor\`, `cards\`). They are never committed. The scripts here turn the few files
the game uses into small, game-ready copies under `src/Hardwood.Desktop/Assets/`, which are
committed and embedded in the desktop app (see `Hardwood.Desktop.csproj`).

## Rebuild

```
ASSET_ROOT=<folder holding audio/ floor/ cards/> python3 tools/Assets/audio.py  <out>/audio
ASSET_ROOT=<same folder>                         python3 tools/Assets/images.py <out>
```

Needs `ffmpeg` (with libvorbis), `numpy` and `Pillow`. Copy the results over the files below.

## What is in the repo

| File (under `src/Hardwood.Desktop/Assets/`) | Made from | What it is for |
|---|---|---|
| `Audio/Crowd/crowd-bed-low.ogg` (45 s loop) | Sonik Sound Library, *Spanish Wallas*: "Crowd, Walla, Dense Immersive Group of Adults…" (Sonniss GDC 2026) | The crowd murmur for a thin house. Loops seamlessly. |
| `Audio/Crowd/crowd-bed-high.ogg` (55 s loop) | Jake Fielding, *British Walla and Crowd* #14, layered with the Sonik walla (Sonniss GDC 2026) | The full-house murmur. Crossfade it with `bed-low` by attendance and momentum. |
| `Audio/Crowd/crowd-cheer-1..3.ogg` (3–5 s) | Sonik Sound Library, *Spanish Crowds*: "Small Club, 50 People… Cheering and Applause" (5.1 folded to stereo) | Reactions: a make, a three, a big block or a run. |
| `Audio/Crowd/crowd-applause.ogg` (7 s) | The same Sonik recording, its clapping tail | Timeouts, substitutions, the end of a period. |
| `Audio/Crowd/crowd-fan-whistle.ogg` (2 s) | SoundBits, *Vox Hominis*, "Whistle Male 06 09" | A fan whistling in the stands. **It is not a referee's whistle.** |
| `Court/court-maple.jpg` (1024², tiles) | Poly Haven **wood_floor** (2K diffuse), recoloured to light maple | Under the painted court in the 2D live view (M4). |
| `Cards/foil-*.jpg` (768×1152) | **HOLOFOIL** by FRZ Designs: textures 13 (diamond), 5 (pearl), 1 (amethyst), 2 (ruby), 11 (opal), 9 (prism) | The foil layer on the higher player-card tiers. |

All audio has a 90 Hz high-pass and a light hall echo so the outdoor and pub recordings sound
indoors. The beds are normalised to −26 and −20 dBFS RMS, the cheers to −18, so nothing clips.

## Still missing (not in any pack Danny has)

- A **referee's whistle** and the **shot-clock/period buzzer**: draw them in code (a pea-whistle
  trill and a square-wave buzzer) or buy SoundDogs *Basketball Sound FX*.
- **Sneaker squeaks, dribbles, rim and backboard hits**: SoundDogs *Basketball Sound FX*.

## Looked at and not used

- ambientCG WoodFloor068/069/070: parquet (basket weave), not court strips.
- ambientCG Metal017 (rusty), Metal048C and Metal049C (painted): they do not read as bronze,
  silver or gold. The card metals are drawn in code instead.
- Poly Haven old_wooden_floor_03: too worn for a court.

## Adding more

Danny drops a new pack into `HW\asset\<area>\`. A session with his computer linked copies only
what it needs, extends the scripts above, adds a row here and a section to
`THIRD-PARTY-NOTICES.md`, and commits the processed files only.
