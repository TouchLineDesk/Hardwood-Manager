# Third-party notices

## Lucide icons (src/Hardwood.Desktop/Controls/IconData.cs)

Line icons from Lucide v0.460.0 (https://lucide.dev), also hosted on Streamline as "Lucide Line".

```
ISC License

Copyright (c) for portions of Lucide are held by Cole Bemis 2013-2022 as part of Feather (MIT). All other copyright (c) for Lucide are held by Lucide Contributors 2022.

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```

## Broadcast renders (src/Hardwood.Desktop/Assets/Broadcast)

Pre-rendered frames built by `tools/Render/` with Blender 5.1 (the `bpy` module, run headless). Only the rendered frames (JPEG, and a PNG tint mask) ship;
no model, texture or add-on file is in the repository or the release.

| Frame | Made with | Licence |
|---|---|---|
| `selection-studio.jpg` (the HSN Selection Show studio) | The set is modelled in code (`tools/Render/selection_studio.py`). The two presenters are MakeHuman characters built with **MPFB 2.0.18** (https://extensions.blender.org/add-ons/mpfb/), dressed with the **MakeHuman system assets, CC0 pack** (https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip): skins `middleage_african_male` and `young_caucasian_female`, clothes `male_elegantsuit01` and `female_elegantsuit01`, hair `short02` and `bob02`, `eyebrow001`, `eyelashes01`, high-poly eyes. Headlines use **Anton** (SIL Open Font License 1.1). | MakeHuman/MPFB output and the system assets are CC0 1.0. MPFB itself (GPL-3.0) is a build tool only and is not distributed. |
| `draft-stage.jpg` (the HSN draft-night stage) | The stage, screen, side boards, podium and lecterns are modelled in code (`tools/Render/draft_stage.py`). The draft official and four team representatives are MakeHuman characters built with **MPFB 2.0.18**, dressed with the **MakeHuman system assets, CC0 pack**: skins `middleage_caucasian_male`, `young_african_male`, `young_asian_female`, `middleage_african_female`, clothes `male_elegantsuit01`, `female_elegantsuit01`, `male_casualsuit01`, hair `short01`, `short03`, `short04`, `ponytail01`, `afro01`, `eyebrow001`, `eyelashes01`, high-poly eyes. Lettering uses **Anton** (SIL Open Font License 1.1). The screen, the side board and the crest cards are drawn by the game. | MakeHuman/MPFB output and the system assets are CC0 1.0. MPFB (GPL-3.0) is a build tool only and is not distributed. |

## Crowd audio (src/Hardwood.Desktop/Assets/Audio/Crowd)

Trimmed, layered and re-encoded excerpts from the **Sonniss #GameAudioGDC 2026 bundle** (https://sonniss.com), used under the Sonniss #GameAudioGDC Bundle Licensing Agreement (`docs/licences/sonniss-gameaudiogdc-license.pdf`): royalty-free use and modification in the licensee's projects, no attribution required, the sounds may not be sold as they come, and they may not be used to train AI. Credited anyway:

| File | Source recordings |
|---|---|
| `crowd-bed-low.ogg` | Sonik Sound Library, *Spanish Wallas* (KS014) |
| `crowd-bed-high.ogg` | Jake Fielding, *British Walla and Crowd* (#14); Sonik Sound Library, *Spanish Wallas* (KS014) |
| `crowd-cheer-1.ogg`, `crowd-cheer-2.ogg`, `crowd-cheer-3.ogg`, `crowd-applause.ogg` | Sonik Sound Library, *Spanish Crowds* (KS015) |
| `crowd-fan-whistle.ogg` | SoundBits, *Vox Hominis – Human Effort Voices* |

How they were made: `tools/Assets/audio.py`.

## Court floor (src/Hardwood.Desktop/Assets/Court)

`court-maple.jpg` is the **Poly Haven wood_floor** diffuse texture (https://polyhaven.com/a/wood_floor, 2K), scaled to 1024 px and recoloured to light maple by `tools/Assets/images.py`. Poly Haven assets are CC0 1.0.

## Card foils (src/Hardwood.Desktop/Assets/Cards)

`foil-*.jpg` are cropped and scaled copies of six textures from **HOLOFOIL** by FRZ Designs (https://takoizu.gumroad.com/l/holofoil), which the pack states are free for personal and commercial use. Made by `tools/Assets/images.py`.

## Card gem bodies (src/Hardwood.Desktop/Assets/Cards/gem-*.jpg)

Cropped and scaled copies of three low-poly backgrounds from **Vecteezy** (https://www.vecteezy.com): "Purple polygonal mosaic background" (#561306), "A red background with diamonds shape" (#34817381) and "Triangle poly prism background" (#34784959). Made by `tools/Assets/images.py`. Downloaded under Vecteezy's licence; under the Free licence the attribution "Vecteezy.com" is required, and the game shows it on its credits line as well as here.

## Audio libraries (src/Hardwood.Desktop/Audio)

The live game's sound (run 153) plays through two MIT-licensed libraries, used as NuGet packages:

- **NAudio** 2.2.1 (`NAudio.Core`, `NAudio.WinMM`), Copyright 2020 Mark Heath, MIT licence: https://github.com/naudio/NAudio
- **NVorbis** 0.10.5, Copyright (c) 2020 Andrew Ward, MIT licence: https://github.com/NVorbis/NVorbis

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

The period buzzer and the referee's whistle are synthesised in code (`Synth.cs`); no recording is used for them.
