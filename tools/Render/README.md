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

## Render
```bash
python3 tools/Render/selection_studio.py 48 /tmp/selection-studio.png   # about 4 minutes on 2 CPUs
```
Then save it as `src/Hardwood.Desktop/Assets/Broadcast/selection-studio.jpg` (quality 86) and copy the `.json` beside it.

| Script | Frame |
|---|---|
| `selection_studio.py` | The Selection Show studio: curved LED wall, the big screen, the anchor desk, host and analyst (MakeHuman, `mpfb_people.py`). |
