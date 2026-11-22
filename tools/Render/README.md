# Broadcast renders (B1, run 139; Danny #984–#985)

Blender scenes for the broadcast presentation (`claude/broadcast-presentation-design.md` and `claude/broadcast-art-assets.md`
in the project). The game ships only the rendered frames, under `src/Hardwood.Desktop/Assets/Broadcast/`, each with a JSON
of where its big screen and presenters sit. Licences and sources: `THIRD-PARTY-NOTICES.md`.

## Set up (once, in a Linux shell)
```bash
pip install bpy==5.1.2 --break-system-packages
curl -L -o mpfb.zip "https://extensions.blender.org/download/sha256:c075b3e9b94b8e5556a5f03f4653602d02ea306de08126e8f55ed6dee28a2dad/add-on-mpfb-v2.0.18.zip"
curl -L -o mh_cc0.zip https://files.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip
python3 -c "import bpy; bpy.ops.extensions.package_install_files(filepath='$PWD/mpfb.zip', repo='user_default', enable_on_install=True)"
D=$(python3 -c "import bpy; print(bpy.utils.extension_path_user('bl_ext.user_default.mpfb', create=True))")/data
mkdir -p "$D" && unzip -q mh_cc0.zip -d "$D"
```
The Anton font comes from the system's Google Fonts folder (`/usr/share/fonts/truetype/google-fonts/ofl/anton`).

### Arena assets (B2a, run 140)
Downloaded into `tools/Render/assets/` (git-ignored), or anywhere named by `HW_RENDER_ASSETS`:
```bash
mkdir -p tools/Render/assets && cd tools/Render/assets
curl -L -o WoodFloor051.zip "https://ambientcg.com/get?file=WoodFloor051_1K-JPG.zip" && unzip -q -d WoodFloor051 WoodFloor051.zip
curl -L -o events_hall_interior_1k.hdr "$(curl -s https://api.polyhaven.com/files/events_hall_interior | python3 -c 'import json,sys; print(json.load(sys.stdin)["hdri"]["1k"]["hdr"]["url"])')"
```

## Render
```bash
python3 tools/Render/selection_studio.py 48 /tmp/selection-studio.png   # about 4 minutes on 2 CPUs
```
Then save it as `src/Hardwood.Desktop/Assets/Broadcast/selection-studio.jpg` (quality 86) and copy the `.json` beside it.

| Script | Frame |
|---|---|
| `arena.py` | **Retired in run 152 (M5):** the frame no longer ships; the live game is the full-screen top-down court. Kept for reference. The college arena (B2a): a bare maple floor with white lines, baskets, ribbon boards and about 6,000 fans; writes the frame, a tint mask (team shirts and ribbon boards) and the court's corners in pixels. The game paints the keys, the apron, the lettering and the crest, projects the players onto the floor and tints the crowd. `python3 tools/Render/arena.py 40 /tmp/college-arena.png` takes about 5 minutes; save the frame as `college-arena.jpg` (quality 86), the mask as an alpha PNG (`college-arena-mask.png`, alpha = the mask) and copy the `.json`. |
| `draft_stage.py` | Draft night (B3, run 142): a stage with the big screen, two HSN DRAFT NIGHT boards, the official at a podium and four team representatives behind blank crest cards; writes the frame and a JSON with the screen, the official's head and each card's corners. `python3 tools/Render/draft_stage.py 40 /tmp/draft-stage.png` takes about 3 minutes; save it as `draft-stage.jpg` (quality 86) and copy the `.json`. |
| `selection_studio.py` | The Selection Show studio: curved LED wall, the big screen, the anchor desk, host and analyst (MakeHuman, `mpfb_people.py`). |
