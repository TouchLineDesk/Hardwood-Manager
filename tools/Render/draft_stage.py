# The HSN draft-night stage (B3, run 142; broadcast-presentation-design.md, #984-#985).
# Run:  python3 tools/Render/draft_stage.py <samples> <out.png>
# Writes <out.png> (1920x1080) and <out>.json: the big screen's corners, the podium speaker's head, and for each of the
# four team representatives the crest card's corners and his head, all in frame pixels. The game fills the screen,
# paints the crests on the cards and places the speech bubble. Only the frame ships.
import bpy, bmesh, math, sys, os, json
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import mpfb_people

OUT = os.path.abspath(sys.argv[-1]); SAMPLES = int(sys.argv[-2])
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
hs = mpfb_people.enable()


def hexc(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def mat(name, color, rough=0.5, emit=None, strength=1.0, metal=0.0, coat=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1); b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal; b.inputs["Coat Weight"].default_value = coat
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1); b.inputs["Emission Strength"].default_value = strength
    return m


P1, P2 = hexc('#7C5CFF'), hexc('#F0A33A')   # the HSN college set's colours (studio-show-design.md)


def box(loc, size, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc); o = bpy.context.object
    o.scale = size; o.rotation_euler = rot; o.data.materials.append(m); return o


ANTON = bpy.data.fonts.load("/usr/share/fonts/truetype/google-fonts/ofl/anton/Anton-Regular.ttf")


def text(s, loc, size, m, rot=(math.radians(90), 0, 0)):
    bpy.ops.object.text_add(location=loc); t = bpy.context.object; t.data.font = ANTON
    t.data.body = s; t.data.size = size; t.data.align_x = 'CENTER'; t.data.align_y = 'CENTER'
    t.rotation_euler = rot; t.data.materials.append(m); return t


STAGE_Z = 0.45
# The hall floor (dark, glossy) and the stage: a raised deck with a lit lip.
box((0, 0, -0.05), (44, 34, 0.1), mat("floor", hexc('#08090D'), 0.15, coat=1.0))
box((0, 2.0, STAGE_Z / 2), (17, 7, STAGE_Z), mat("deck", hexc('#12151D'), 0.55, metal=0.2, coat=0.1))
box((0, -1.52, STAGE_Z - 0.04), (17, 0.06, 0.05), mat("lip", (0, 0, 0), 0.3, emit=P1, strength=9))
box((0, -1.53, 0.12), (17, 0.04, 0.04), mat("lip2", (0, 0, 0), 0.3, emit=P2, strength=6))

# Back wall: tall dark panels with light strips.
wall = mat("wall", hexc('#131722'), 0.45, metal=0.2)
strip1 = mat("strip1", (0, 0, 0), 0.3, emit=P1, strength=12)
strip2 = mat("strip2", (0, 0, 0), 0.3, emit=P2, strength=10)
for i in range(-10, 11):
    box((i * 1.6, 8.2, 5), (1.55, 0.3, 10), wall)
    if i % 2 == 0:
        box((i * 1.6 + 0.78, 8.03, 5), (0.05, 0.05, 9.4), strip1 if i % 4 == 0 else strip2)

# The big screen in the middle (the game fills it; its corners go to the JSON).
SCREEN_W, SCREEN_H, SCREEN_C = 8.4, 4.2, Vector((0, 7.75, 5.55))
box((0, 7.95, SCREEN_C.z), (SCREEN_W + 0.4, 0.2, SCREEN_H + 0.4), mat("frame", hexc('#20242E'), 0.3, metal=0.7))
box(tuple(SCREEN_C), (SCREEN_W, 0.05, SCREEN_H), mat("screen", hexc('#06080C'), 1.0, emit=hexc('#0E1420'), strength=1.0))
box((0, 7.72, SCREEN_C.z - SCREEN_H / 2 - 0.25), (SCREEN_W + 0.4, 0.06, 0.07), strip1)

# The two side boards, angled in: our lettering (HSN, DRAFT NIGHT), not anyone's mark.
board = mat("board", hexc('#0A0D14'), 0.35)
ink1 = mat("ink1", (0, 0, 0), 0.4, emit=(1, 1, 1), strength=6)
ink2 = mat("ink2", (0, 0, 0), 0.4, emit=P2, strength=8)
for sx in (-1, 1):
    a = math.radians(-22 * sx)
    cx, cy = sx * 8.3, 6.0
    box((cx, cy, 5.3), (3.6, 0.15, 5.4), board, rot=(0, 0, a))
    box((cx - 0.06 * math.sin(a), cy + 0.06 * math.cos(a), 5.3), (3.8, 0.1, 5.6), mat(f"bedge{sx}", (0, 0, 0), 0.3, emit=P1, strength=3), rot=(0, 0, a))  # a lit rim behind the board
    off = Vector((0.12 * math.sin(a), -0.12 * math.cos(a), 0))  # just in front of the board's face
    for s, z, size, m in (("HSN", 6.85, 1.05, ink2), ("DRAFT", 5.55, 1.05, ink1), ("NIGHT", 4.25, 1.05, ink1)):
        text(s, (cx + off.x, cy + off.y, z), size, m, rot=(math.radians(90), 0, a))
    for k in range(4):  # chevrons, the set's motif
        box((cx + off.x * 1.1, cy + off.y * 1.1, 3.0 - k * 0.0), (2.6 - k * 0.5, 0.04, 0.05), strip2, rot=(0, 0, a))

# The podium, stage left, with the draft official behind it.
POD = Vector((-4.6, -0.2, STAGE_Z))
pod_body = mat("pod", hexc('#1A1F2A'), 0.25, metal=0.5, coat=0.8)
box((POD.x, POD.y, POD.z + 0.55), (1.1, 0.6, 1.1), pod_body)
box((POD.x, POD.y - 0.02, POD.z + 1.16), (1.25, 0.75, 0.08), mat("podtop", hexc('#E8ECF2'), 0.08, coat=1.0), rot=(math.radians(-10), 0, 0))
box((POD.x, POD.y - 0.31, POD.z + 0.6), (0.95, 0.02, 0.8), mat("podface", hexc('#F4F6FA'), 0.2, emit=hexc('#FFFFFF'), strength=0.35))
ink = mat("ink", hexc('#101318'), 0.4)
text("HSN", (POD.x, POD.y - 0.33, POD.z + 0.78), 0.32, mat("podink2", hexc('#7C5CFF'), 0.4))
text("DRAFT NIGHT", (POD.x, POD.y - 0.33, POD.z + 0.42), 0.2, ink)
OFFICIAL = (POD.x, POD.y + 0.55, POD.z)

# Four team representatives, stage right, each behind a lectern with a blank crest card the game paints.
REPS = [(-0.2 + i * 1.75, 0.35) for i in range(4)]
card_m = mat("card", hexc('#F4F6FA'), 0.3, emit=hexc('#FFFFFF'), strength=0.25)
lect = mat("lect", hexc('#1A1F2A'), 0.25, metal=0.5, coat=0.8)
CARD_W, CARD_H = 0.9, 0.62
cards = []
for (x, y) in REPS:
    box((x, y, STAGE_Z + 0.5), (1.15, 0.45, 1.0), lect)
    box((x, y, STAGE_Z + 1.03), (1.25, 0.55, 0.05), mat(f"lt{x}", hexc('#E8ECF2'), 0.08, coat=1.0))
    c = Vector((x, y - 0.235, STAGE_Z + 0.55))
    box(tuple(c), (CARD_W, 0.02, CARD_H), card_m)
    box((x, y - 0.235, STAGE_Z + 0.16), (1.0, 0.02, 0.03), mat(f"ls{x}", (0, 0, 0), 0.3, emit=P1, strength=3))
    cards.append(c)

people = [
    ("official", dict(gender=1.0, age=0.6, race={"asian": 0.0, "caucasian": 0.9, "african": 0.1}, skin="middleage_caucasian_male",
                      suit="male_elegantsuit01", hair="short04", hair_rgb=(0.4, 0.38, 0.36), height=0.55), OFFICIAL, math.radians(14)),
    ("rep1", dict(gender=1.0, age=0.5, race={"asian": 0.0, "caucasian": 0.2, "african": 0.8}, skin="young_african_male",
                  suit="male_elegantsuit01", hair="short01", height=0.6), (REPS[0][0], REPS[0][1] + 0.45, STAGE_Z), math.radians(-4)),
    ("rep2", dict(gender=0.0, age=0.45, race={"asian": 0.85, "caucasian": 0.15, "african": 0.0}, skin="young_asian_female",
                  suit="female_elegantsuit01", hair="ponytail01", hair_rgb=(0.06, 0.05, 0.05), height=0.45), (REPS[1][0], REPS[1][1] + 0.45, STAGE_Z), math.radians(-8)),
    ("rep3", dict(gender=1.0, age=0.7, race={"asian": 0.0, "caucasian": 0.85, "african": 0.15}, skin="middleage_caucasian_male",
                  suit="male_casualsuit01", hair="short03", hair_rgb=(0.3, 0.22, 0.14), height=0.5), (REPS[2][0], REPS[2][1] + 0.45, STAGE_Z), math.radians(-12)),
    ("rep4", dict(gender=0.0, age=0.55, race={"asian": 0.0, "caucasian": 0.3, "african": 0.7}, skin="middleage_african_female",
                  suit="female_elegantsuit01", hair="afro01", hair_rgb=(0.05, 0.04, 0.04), height=0.5), (REPS[3][0], REPS[3][1] + 0.45, STAGE_Z), math.radians(-16)),
]
SCALE = 1.15
for name, kw, loc, turn in people:
    mpfb_people.presenter(hs, name, loc=loc, turn=turn, **kw)
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        o.scale = (SCALE, SCALE, SCALE)

# Truss and fixtures.
truss = mat("truss", hexc('#2A2F3A'), 0.4, metal=0.8)
for yy in (-2.0, 3.5):
    box((0, yy, 9.6), (24, 0.25, 0.25), truss)
fix = mat("fix", (0, 0, 0), 0.3, emit=(1, 0.95, 0.85), strength=25)
for xx in range(-10, 11, 3):
    for yy in (-2.0, 3.5):
        bpy.ops.mesh.primitive_cylinder_add(radius=0.18, depth=0.35, location=(xx, yy, 9.3)); bpy.context.object.data.materials.append(fix)

world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.005, 0.006, 0.01, 1)


def light(kind, loc, energy, size, target, color=(1, 1, 1)):
    bpy.ops.object.light_add(type=kind, location=loc); l = bpy.context.object
    l.data.energy = energy; l.data.color = color
    if kind == 'AREA':
        l.data.size = size
    else:
        l.data.spot_size = size
    l.rotation_euler = (Vector(target) - l.location).to_track_quat('-Z', 'Y').to_euler()


light('AREA', (0, -7, 6.5), 2200, 8, (0, 1, 1.4))
light('AREA', (1, -5, 2.6), 520, 6, (1, 1, 1.8), (1.0, 0.96, 0.92))       # faces
light('AREA', (-7, -2, 5), 700, 3, (-4.5, 0, 1.5), (0.85, 0.8, 1.0))
light('AREA', (8, -2, 5), 700, 3, (3, 0.5, 1.5), (1.0, 0.9, 0.8))
light('AREA', (0, 4, 9), 900, 10, (0, 3, 0))
light('SPOT', (-4.6, -2.5, 8.5), 2600, 0.5, OFFICIAL, (1.0, 0.97, 0.92))   # the podium's key
light('SPOT', (-6, 0, 9), 3000, 0.7, (-3, 5, 0), P1)
light('SPOT', (6, 0, 9), 3000, 0.7, (3, 5, 0), P2)
light('AREA', (1.5, 4.0, 3.4), 320, 6, (1.5, 0.5, 1.8), (0.75, 0.7, 1.0))  # back rim on the reps

bpy.ops.object.camera_add(location=(0, -10.2, 2.6)); cam = bpy.context.object; scene.camera = cam
cam.data.lens = 28
cam.rotation_euler = (Vector((0, 4, 3.3)) - cam.location).to_track_quat('-Z', 'Y').to_euler()

scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = SAMPLES
scene.cycles.use_denoising = True
W, H = 1920, 1080
scene.render.resolution_x = W; scene.render.resolution_y = H; scene.render.resolution_percentage = int(os.environ.get("HW_PCT", "100"))
scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'Medium High Contrast'
scene.render.filepath = OUT


def px(p):
    v = world_to_camera_view(scene, cam, Vector(p))
    return [round(v.x * W, 1), round((1 - v.y) * H, 1)]


bpy.context.view_layer.update()
hw, hh = SCREEN_W / 2, SCREEN_H / 2
corners = [px((SCREEN_C.x + dx, SCREEN_C.y - 0.03, SCREEN_C.z + dz)) for dx, dz in ((-hw, hh), (hw, hh), (hw, -hh), (-hw, -hh))]
cw, ch = CARD_W / 2, CARD_H / 2
meta = {
    "size": [W, H],
    "screen": corners,  # top-left, top-right, bottom-right, bottom-left
    "host_head": px((OFFICIAL[0], OFFICIAL[1], OFFICIAL[2] + 1.74 * SCALE)),
    "analyst_head": px((REPS[3][0], REPS[3][1] + 0.45, STAGE_Z + 1.62 * SCALE)),
    "cards": [[px((c.x + dx, c.y - 0.02, c.z + dz)) for dx, dz in ((-cw, ch), (cw, ch), (cw, -ch), (-cw, -ch))] for c in cards],
    "rep_heads": [px((x, y + 0.45, STAGE_Z + 1.66 * SCALE)) for (x, y) in REPS],
}
with open(os.path.splitext(OUT)[0] + ".json", "w") as f:
    json.dump(meta, f, indent=1)
bpy.ops.render.render(write_still=True)
print("done", OUT, meta)
