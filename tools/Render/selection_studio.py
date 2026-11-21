# The HSN studio set for the Selection Show (B1, run 139; broadcast-presentation-design.md, #984-#985).
# Run:  python3 tools/Render/selection_studio.py <samples> <out.png>
# Writes <out.png> (1920x1080) and <out>.json with the big screen's rectangle and the presenters' head points in pixels,
# which the game reads to place the screen graphics and the speech bubbles. Only the frame ships.
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


P1, P2 = hexc('#7C5CFF'), hexc('#F0A33A')   # the college set's two colours (studio-show-design.md)


def box(loc, size, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc); o = bpy.context.object
    o.scale = size; o.rotation_euler = rot; o.data.materials.append(m); return o


ANTON = bpy.data.fonts.load("/usr/share/fonts/truetype/google-fonts/ofl/anton/Anton-Regular.ttf")


def text(s, loc, size, m, rot=(math.radians(90), 0, 0)):
    bpy.ops.object.text_add(location=loc); t = bpy.context.object; t.data.font = ANTON
    t.data.body = s; t.data.size = size; t.data.align_x = 'CENTER'; t.data.align_y = 'CENTER'
    t.rotation_euler = rot; t.data.materials.append(m); return t


# Floor: dark, glossy.
box((0, 0, -0.05), (40, 30, 0.1), mat("floor", hexc('#0B0D12'), 0.12, coat=1.0))
# Back wall: curved panels with light strips.
wall = mat("wall", hexc('#151A24'), 0.45, metal=0.2)
strip1 = mat("strip1", (0, 0, 0), 0.3, emit=P1, strength=12)
strip2 = mat("strip2", (0, 0, 0), 0.3, emit=P2, strength=10)
R = 11
for i in range(-16, 17):
    a = math.radians(i * 7.0)
    x, y = R * math.sin(a), 4 - R * math.cos(a) + R
    box((x, y + 4, 4), (1.9, 0.3, 8), wall, rot=(0, 0, a))
    if i % 3 == 0:
        box((x, y + 3.82, 4), (0.06, 0.05, 7.4), strip1 if i % 2 == 0 else strip2, rot=(0, 0, a))
for k in range(5):  # chevrons, the set's motif
    box((-6.5 + k * 0.5, 7.7, 0.8 + k * 0.35), (2.5, 0.05, 0.05), strip2, rot=(0, math.radians(-25), 0))
    box((6.5 - k * 0.5, 7.7, 0.8 + k * 0.35), (2.5, 0.05, 0.05), strip2, rot=(0, math.radians(25), 0))

# The big screen: a dark glass panel the game fills (its corners are written to the JSON).
frame = mat("frame", hexc('#20242E'), 0.3, metal=0.7)
box((0, 7.0, 4.6), (10.6, 0.2, 5.0), frame)
SCREEN_W, SCREEN_H, SCREEN_C = 10.2, 4.6, Vector((0, 6.85, 4.6))
box(tuple(SCREEN_C), (SCREEN_W, 0.05, SCREEN_H), mat("screen", hexc('#06080C'), 1.0, emit=hexc('#0E1420'), strength=1.0))
box((0, 6.84, 2.15), (10.6, 0.08, 0.08), strip1)
box((0, 6.84, 7.05), (10.6, 0.08, 0.08), strip1)

# The desk: a curved anchor desk with a glossy top and a lit front.
desk_body = mat("desk", hexc('#1A1F2A'), 0.25, metal=0.5, coat=0.8)
desk_top = mat("desktop", hexc('#E8ECF2'), 0.08, coat=1.0)
segs = 40; r_out, r_in = 7.0, 5.6; a_span = math.radians(70)


def arc_box(r0, r1, z0, z1, m, name):
    bm = bmesh.new(); vs = []
    for i in range(segs + 1):
        a = -a_span / 2 + a_span * i / segs
        vs.append([bm.verts.new((r * math.sin(a), -r * math.cos(a) + 6.2, z)) for (r, z) in ((r0, z0), (r1, z0), (r1, z1), (r0, z1))])
    for i in range(segs):
        for k in range(4):
            bm.faces.new((vs[i][k], vs[i][(k + 1) % 4], vs[i + 1][(k + 1) % 4], vs[i + 1][k]))
    bm.faces.new(vs[0]); bm.faces.new(list(reversed(vs[-1])))
    me = bpy.data.meshes.new(name); bm.to_mesh(me); o = bpy.data.objects.new(name, me)
    bpy.context.collection.objects.link(o); o.data.materials.append(m)
    for p in me.polygons:
        p.use_smooth = True
    return o


arc_box(r_in, r_out, 0, 1.05, desk_body, "deskbody")
arc_box(r_in - 0.15, r_out + 0.12, 1.05, 1.13, desk_top, "desktop")
arc_box(r_out + 0.01, r_out + 0.03, 0.15, 0.95, mat("deskglow", (0, 0, 0), 0.4, emit=P1, strength=2.5), "deskglow")
panel = mat("panel", hexc('#F4F6FA'), 0.2, emit=hexc('#FFFFFF'), strength=0.4)
box((0, -0.88, 0.58), (3.6, 0.05, 0.75), panel)
text("SELECTION SHOW", (0, -0.92, 0.62), 0.5, mat("ink", hexc('#101318'), 0.4))

# The presenters (MakeHuman, CC0): the host on the left, the analyst on the right, standing at the desk.
HOST = (-1.45, 1.0, 0.24); ANALYST = (1.45, 1.0, 0.24)  # on a riser behind the desk
SCALE = 1.2
mpfb_people.presenter(hs, "host", gender=1.0, age=0.55, race={"asian": 0.0, "caucasian": 0.15, "african": 0.85},
                      skin="middleage_african_male", suit="male_elegantsuit01", hair="short02", height=0.55,
                      loc=HOST, turn=math.radians(12))
mpfb_people.presenter(hs, "analyst", gender=0.0, age=0.45, race={"asian": 0.2, "caucasian": 0.8, "african": 0.0},
                      skin="young_caucasian_female", suit="female_elegantsuit01", hair="bob02", hair_rgb=(0.35, 0.22, 0.12),
                      height=0.5, loc=ANALYST, turn=math.radians(-12))
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        o.scale = (SCALE, SCALE, SCALE)
box((0, 1.0, 0.12), (6.0, 1.2, 0.24), mat("riser", hexc('#101420'), 0.5))

# Truss, fixtures, LED pillars.
truss = mat("truss", hexc('#2A2F3A'), 0.4, metal=0.8)
for yy in (-2.5, 2.5):
    box((0, yy, 9.2), (22, 0.25, 0.25), truss)
fix = mat("fix", (0, 0, 0), 0.3, emit=(1, 0.95, 0.85), strength=25)
for xx in range(-9, 10, 3):
    for yy in (-2.5, 2.5):
        bpy.ops.mesh.primitive_cylinder_add(radius=0.18, depth=0.35, location=(xx, yy, 8.9)); bpy.context.object.data.materials.append(fix)
for sx in (-1, 1):
    box((sx * 9.5, 3.5, 3.5), (0.6, 0.6, 7), mat(f"pil{sx}", (0, 0, 0), 0.3, emit=P1 if sx < 0 else P2, strength=4))

# Light: a dark world, a key on the desk, two coloured rims, and a soft face light.
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


light('AREA', (0, -6, 6), 1800, 6, (0, 1, 1.2))
light('AREA', (0, -4.5, 2.2), 420, 4, (0, 1, 1.6), (1.0, 0.96, 0.92))   # faces
light('AREA', (-6, -2, 5), 700, 3, (-1.5, 1, 1.5), (0.85, 0.8, 1.0))
light('AREA', (6, -2, 5), 700, 3, (1.5, 1, 1.5), (1.0, 0.9, 0.8))
light('AREA', (0, 3, 9), 900, 8, (0, 3, 0))
light('SPOT', (-5, -1, 8), 3000, 0.6, (-2, 3, 0), P1)
light('SPOT', (5, -1, 8), 3000, 0.6, (2, 3, 0), P2)
light('AREA', (0, 3.5, 3.2), 260, 3, (0, 0.9, 1.6), (0.75, 0.7, 1.0))  # back rim on the presenters

bpy.ops.object.camera_add(location=(0, -10.5, 2.5)); cam = bpy.context.object; scene.camera = cam
cam.data.lens = 30
cam.rotation_euler = (Vector((0, 4, 3.0)) - cam.location).to_track_quat('-Z', 'Y').to_euler()

scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = SAMPLES
scene.cycles.use_denoising = True
W, H = 1920, 1080
scene.render.resolution_x = W; scene.render.resolution_y = H
scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'Medium High Contrast'
scene.render.filepath = OUT


def px(p):
    v = world_to_camera_view(scene, cam, Vector(p))
    return [round(v.x * W, 1), round((1 - v.y) * H, 1)]


bpy.context.view_layer.update()
hw, hh = SCREEN_W / 2, SCREEN_H / 2
corners = [px((SCREEN_C.x + dx, SCREEN_C.y - 0.03, SCREEN_C.z + dz)) for dx, dz in ((-hw, hh), (hw, hh), (hw, -hh), (-hw, -hh))]
meta = {
    "size": [W, H],
    "screen": corners,  # top-left, top-right, bottom-right, bottom-left
    "host_head": px((HOST[0], HOST[1], HOST[2] + 1.78 * SCALE)),
    "analyst_head": px((ANALYST[0], ANALYST[1], ANALYST[2] + 1.66 * SCALE)),
    "desk_top": px((0, -0.9, 1.13)),
}
with open(os.path.splitext(OUT)[0] + ".json", "w") as f:
    json.dump(meta, f, indent=1)
bpy.ops.render.render(write_still=True)
print("done", OUT, meta)
