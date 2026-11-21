# MakeHuman people for the broadcast renders (B1, run 139; Danny #985).
# MPFB 2.0.18 (extensions.blender.org, CC0 output) builds the body; the MakeHuman system assets pack (CC0) dresses it.
# See tools/Render/README.md for how to install both into the bpy module. Nothing from here ships but the rendered frames.
import bpy, math, os, importlib

MPFB = "bl_ext.user_default.mpfb"
DATA = os.path.join(bpy.utils.extension_path_user(MPFB, create=True), "data")


def enable():
    import addon_utils
    addon_utils.enable(MPFB, default_set=True)
    return importlib.import_module(MPFB + ".services.humanservice").HumanService


def _tint(obj, rgb):
    """Darken an asset's base colour (hair) by multiplying its first image texture."""
    for slot in obj.material_slots:
        m = slot.material
        if not m or not m.use_nodes:
            continue
        nt = m.node_tree
        for n in nt.nodes:
            if n.type == 'BSDF_PRINCIPLED':
                link = next((l for l in nt.links if l.to_socket == n.inputs["Base Color"]), None)
                mix = nt.nodes.new("ShaderNodeMix"); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
                mix.inputs[0].default_value = 1.0
                mix.inputs[7].default_value = (*rgb, 1)
                if link:
                    nt.links.new(link.from_socket, mix.inputs[6])
                else:
                    mix.inputs[6].default_value = n.inputs["Base Color"].default_value
                nt.links.new(mix.outputs[2], n.inputs["Base Color"])


def presenter(hs, name, *, gender, age, race, skin, suit, hair, hair_rgb=(0.08, 0.06, 0.05), height=0.5, weight=0.5,
              loc=(0, 0, 0), turn=0.0, arms=80.0, forearms=70.0):
    """A dressed, standing presenter with the arms lowered and the forearms forward (hands at the desk)."""
    macro = {"gender": gender, "age": age, "muscle": 0.5, "weight": weight, "proportions": 0.6, "height": height,
             "cupsize": 0.5, "firmness": 0.5, "race": race}
    body = hs.create_human(macro_detail_dict=macro, scale=0.1)
    body.name = name
    hs.add_builtin_rig(body, "default", import_weights=True)
    rig = body.parent
    hs.set_character_skin(os.path.join(DATA, "skins", skin, skin + ".mhmat"), body, skin_type="ENHANCED_SSS")
    parts = [("Eyes", "eyes/high-poly/high-poly.mhclo"), ("Eyebrows", "eyebrows/eyebrow001/eyebrow001.mhclo"),
             ("Eyelashes", "eyelashes/eyelashes01/eyelashes01.mhclo"), ("Clothes", f"clothes/{suit}/{suit}.mhclo")]
    if hair:
        parts.append(("Hair", f"hair/{hair}/{hair}.mhclo"))
    for kind, f in parts:
        hs.add_mhclo_asset(os.path.join(DATA, f), body, asset_type=kind, subdiv_levels=0)
    if hair:
        for o in bpy.data.objects:
            if o.name.startswith(name + "." + hair):
                _tint(o, hair_rgb)
    for side, sg in (("L", 1), ("R", -1)):
        ua = rig.pose.bones["upperarm01." + side]; ua.rotation_mode = 'XYZ'; ua.rotation_euler = (0, math.radians(arms * sg), 0)
        la = rig.pose.bones["lowerarm01." + side]; la.rotation_mode = 'XYZ'; la.rotation_euler = (0, 0, math.radians(forearms * sg))
    rig.location = loc
    rig.rotation_euler = (0, 0, turn)
    return body, rig
