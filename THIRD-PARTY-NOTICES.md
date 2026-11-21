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
| `college-arena.jpg`, `college-arena-mask.png` (the college arena for the live game) | The arena, stands, crowd and baskets are modelled in code (`tools/Render/arena.py`). The floor uses **ambientCG WoodFloor051** (https://ambientcg.com/view?id=WoodFloor051, 1K-JPG: colour, roughness, normal). The lighting and reflections use **Poly Haven Events Hall Interior** (https://polyhaven.com/a/events_hall_interior, 1K HDR). The LED board lettering uses **Anton** (SIL Open Font License 1.1). The floor's paint, lettering and crest are drawn by the game. | ambientCG and Poly Haven assets are CC0 1.0. |
