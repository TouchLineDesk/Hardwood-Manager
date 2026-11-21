# The college arena for the live game's court view (B2a, run 140; broadcast-presentation-design.md, #984).
# Run:  python3 tools/Render/arena.py <samples> <out.png>      (assets: see README.md, "Arena assets")
# Writes <out.png> (1920x1080, the arena with a bare maple floor and white lines), <out>-mask.png (960x540, white where
# the game tints the crowd's team shirts and the LED ribbon in the home program's colours) and <out>.json (the court's
# corners and the baskets in pixels, which the game uses to project its floor paint and players onto the floor).
# The floor's paint (keys, apron, centre crest, baseline lettering) is drawn by the game, so one frame serves every
# program and every event. Only the frame, the mask and the JSON ship.
import bpy, bmesh, math, random, sys, os, json
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

random.seed(7)
OUT = os.path.abspath(sys.argv[-1]); SAMPLES = int(sys.argv[-2])
HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.environ.get("HW_RENDER_ASSETS", os.path.join(HERE, "assets"))
WOOD = os.path.join(ASSETS, "WoodFloor051", "WoodFloor051_1K-JPG_")
HDRI = os.path.join(ASSETS, "events_hall_interior_1k.hdr")

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


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


def box(loc, size, m, rot=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc); o = bpy.context.object
    o.scale = size; o.rotation_euler[2] = rot; o.data.materials.append(m); return o


ANTON = bpy.data.fonts.load("/usr/share/fonts/truetype/google-fonts/ofl/anton/Anton-Regular.ttf")


def text(s, loc, size, m, rot=(0, 0, 0)):
    bpy.ops.object.text_add(location=loc); t = bpy.context.object; t.data.font = ANTON
    t.data.body = s; t.data.size = size; t.data.align_x = 'CENTER'; t.data.align_y = 'CENTER'
    t.rotation_euler = rot; t.data.materials.append(m); return t


def ring(r_out, r_in, loc, m, z=0.004, seg=96, start=0.0, end=2 * math.pi):
    bm = bmesh.new(); vo = []; vi = []
    for i in range(seg + 1):
        a = start + (end - start) * i / seg
        vo.append(bm.verts.new((loc[0] + r_out * math.cos(a), loc[1] + r_out * math.sin(a), z)))
        vi.append(bm.verts.new((loc[0] + r_in * math.cos(a), loc[1] + r_in * math.sin(a), z)))
    for i in range(seg):
        bm.faces.new((vo[i], vo[i + 1], vi[i + 1], vi[i]))
    me = bpy.data.meshes.new("ring"); bm.to_mesh(me); o = bpy.data.objects.new("ring", me)
    bpy.context.collection.objects.link(o); o.data.materials.append(m); return o


# ---------- the floor: ambientCG WoodFloor051 (CC0), planks along the court, a glossy finish ----------
L, W = 28.65, 15.24                      # 94 x 50 feet
FX, FY = L + 7, W + 7                    # the floor runs past the lines (the apron)
bpy.ops.mesh.primitive_plane_add(size=1); floor = bpy.context.object
floor.scale = (FX, FY, 1)
# Dark concourse floor beyond the apron and towards the camera.
box((0, 0, -0.02), (FX + 30, FY + 30, 0.02), mat("concourse", hexc('#0C0E12'), 0.6))
wood = bpy.data.materials.new("maple"); wood.use_nodes = True
nt = wood.node_tree; n = nt.nodes; lk = nt.links
bsdf = n["Principled BSDF"]
bsdf.inputs["Coat Weight"].default_value = 0.7; bsdf.inputs["Coat Roughness"].default_value = 0.06
tc = n.new("ShaderNodeTexCoord"); mp = n.new("ShaderNodeMapping")
mp.inputs["Scale"].default_value = (FX / 1.6, FY / 1.6, 1)   # one 1K tile covers about 1.6 m
lk.new(tc.outputs["UV"], mp.inputs["Vector"])


def tex(kind, non_color=False):
    t = n.new("ShaderNodeTexImage"); t.image = bpy.data.images.load(WOOD + kind + ".jpg")
    if non_color: t.image.colorspace_settings.name = "Non-Color"
    lk.new(mp.outputs["Vector"], t.inputs["Vector"]); return t


col = tex("Color"); rough = tex("Roughness", True); nrm = tex("NormalGL", True)
# Lighter and warmer: a hard maple court rather than an oak floor.
hsv = n.new("ShaderNodeHueSaturation")
hsv.inputs["Hue"].default_value = 0.49; hsv.inputs["Saturation"].default_value = 0.85; hsv.inputs["Value"].default_value = 0.95
lk.new(col.outputs["Color"], hsv.inputs["Color"])
lk.new(hsv.outputs["Color"], bsdf.inputs["Base Color"])
rmap = n.new("ShaderNodeMapRange"); rmap.inputs["To Min"].default_value = 0.12; rmap.inputs["To Max"].default_value = 0.35
lk.new(rough.outputs["Color"], rmap.inputs["Value"]); lk.new(rmap.outputs["Result"], bsdf.inputs["Roughness"])
nm = n.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value = 0.35
lk.new(nrm.outputs["Color"], nm.inputs["Color"]); lk.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
floor.data.materials.append(wood)

# ---------- the lines (white; the game paints the keys, the apron, the crest and the lettering) ----------
white = mat("line", (0.92, 0.92, 0.9), 0.3, coat=0.6)
lw = 0.051
for sy in (-1, 1): box((0, sy * W / 2, 0.003), (L, lw, 0.002), white)
for sx in (-1, 1): box((sx * L / 2, 0, 0.003), (lw, W, 0.002), white)
box((0, 0, 0.003), (lw, W, 0.002), white)
ring(1.83, 1.83 - lw, (0, 0), white, 0.004)
ring(0.61, 0.61 - lw, (0, 0), white, 0.004)
HOOP = L / 2 - 1.6
for sx in (-1, 1):
    # The key (3.66 m wide, college), the free-throw circle and the 22' 1.75" three-point arc.
    kx = sx * (L / 2 - 2.9)
    for sy in (-1, 1): box((kx, sy * 1.83, 0.003), (5.8, lw, 0.002), white)
    box((sx * (L / 2 - 5.8), 0, 0.003), (lw, 3.66, 0.002), white)
    ring(1.83, 1.83 - lw, (sx * (L / 2 - 5.8), 0), white, 0.004)
    hoop = (sx * HOOP, 0)
    R3 = 6.75; side = W / 2 - 0.91
    a = math.asin(side / R3)
    a0, a1 = (-a, a) if sx < 0 else (math.pi - a, math.pi + a)
    ring(R3, R3 - lw, hoop, white, 0.004, 90, a0, a1)
    ex = sx * HOOP + (-sx) * R3 * math.cos(a)
    for sy in (-1, 1):
        ln = abs(sx * L / 2 - ex)
        box(((sx * L / 2 + ex) / 2, sy * side, 0.003), (ln, lw, 0.002), white)
    # The restricted-area arc.
    ring(1.22, 1.22 - lw, hoop, white, 0.004, 40, (-math.pi / 2 if sx < 0 else math.pi / 2), (math.pi / 2 if sx < 0 else 3 * math.pi / 2))

# ---------- the baskets: stanchion, arm, glass board, rim, net ----------
blk = mat("stanchion", (0.035, 0.035, 0.04), 0.35, coat=0.5)
pad = mat("pad", hexc('#1A1D24'), 0.6)
glass = mat("glass", (0.95, 0.97, 1.0), 0.03); glass.node_tree.nodes["Principled BSDF"].inputs["Transmission Weight"].default_value = 0.92
rim = mat("rim", hexc('#FF5A1F'), 0.3, metal=0.7)
net = mat("net", (0.95, 0.95, 0.95), 0.6)
for sx in (-1, 1):
    base = sx * (L / 2 + 2.4)
    box((base, 0, 0.55), (1.5, 1.3, 1.1), pad)
    box((base, 0, 1.8), (0.32, 0.32, 2.5), blk)
    box((sx * (L / 2 + 0.9), 0, 3.0), (3.0, 0.26, 0.26), blk)
    box((sx * (L / 2 - 1.22), 0, 3.4), (0.04, 1.83, 1.07), glass)
    box((sx * (L / 2 - 1.20), 0, 3.08), (0.03, 0.59, 0.45), white)
    box((sx * (L / 2 - 1.20), 0, 3.87), (0.05, 1.86, 0.05), white)
    bpy.ops.mesh.primitive_torus_add(major_radius=0.23, minor_radius=0.012, location=(sx * HOOP, 0, 3.05))
    bpy.context.object.data.materials.append(rim)
    bpy.ops.mesh.primitive_cone_add(radius1=0.23, radius2=0.13, depth=0.42, location=(sx * HOOP, 0, 2.83), end_fill_type='NOTHING', vertices=24)
    bpy.ops.object.modifier_add(type='WIREFRAME'); bpy.context.object.modifiers[0].thickness = 0.012
    bpy.context.object.data.materials.append(net)

# ---------- the ribbon boards, the scorer's table and the stands ----------
# Team-tinted parts are a light neutral in the frame and white in the mask, so the game colours them for any program.
TINT = hexc('#C8CCD2')
led = mat("led", (0.02, 0.02, 0.03), 0.4, emit=TINT, strength=1.6)
ledtxt = mat("ledtxt", (1, 1, 1), 0.4, emit=(1, 1, 1), strength=5.0)
far = W / 2 + 4.4
ribbon = [box((0, far, 0.45), (L + 6, 0.15, 0.9), led)]
text("HSN  ·  COLLEGE BASKETBALL", (0, far - 0.09, 0.45), 0.5, ledtxt, rot=(math.radians(90), 0, 0))
for sx in (-1, 1):
    ribbon.append(box((sx * (L / 2 + 5.2), 0, 0.45), (0.15, W + 7, 0.9), led))
table = mat("table", (0.03, 0.03, 0.04), 0.3, emit=hexc('#20242C'), strength=1.0)
box((0, -(W / 2 + 4.6), 0.42), (9, 0.8, 0.84), table)

seat = mat("seats", hexc('#1B1F26'), 0.7)
crowd_m = bpy.data.materials.new("crowd"); crowd_m.use_nodes = True
cb = crowd_m.node_tree.nodes["Principled BSDF"]; cb.inputs["Roughness"].default_value = 0.85
vc = crowd_m.node_tree.nodes.new("ShaderNodeVertexColor"); vc.layer_name = "Col"
crowd_m.node_tree.links.new(vc.outputs["Color"], cb.inputs["Base Color"])
NEUTRAL = [hexc(h) for h in ('#222222', '#3A3F48', '#5B616C', '#8A8F99', '#E8E8E8', '#1F3550', '#6B4E37', '#C9B28A', '#7A2E2E')]
SKIN = [hexc(h) for h in ('#8d5524', '#c68642', '#e0ac69', '#f1c27d', '#ffdbac', '#5c3a1e')]

bm = bmesh.new(); vcol = bm.loops.layers.color.new("Col"); vmask = bm.loops.layers.color.new("Mask")


def cube(cx, cy, cz, sx, sy, sz, color, m):
    r = bmesh.ops.create_cube(bm, size=1)
    for v in r["verts"]:
        v.co.x = cx + v.co.x * sx; v.co.y = cy + v.co.y * sy; v.co.z = cz + v.co.z * sz
    for f in {f for v in r["verts"] for f in v.link_faces}:
        for lp in f.loops: lp[vcol] = (*color, 1); lp[vmask] = (m, m, m, 1)


def person(x, y, z, along):
    team = random.random() < 0.62            # most of the crowd wears the home colours
    c = TINT if team else random.choice(NEUTRAL)
    s = random.choice(SKIN); j = random.uniform(-0.06, 0.06)
    w, d = (0.42, 0.26) if along == 'x' else (0.26, 0.42)
    cube(x, y, z + 0.32 + j, w, d, 0.55, c, 1.0 if team else 0.0)
    cube(x, y, z + 0.75 + j, 0.2, 0.2, 0.24, s, 0.0)
    if random.random() < 0.14:
        cube(x + (0.2 if along == 'x' else 0), y + (0 if along == 'x' else 0.2), z + 0.97 + j, 0.08, 0.08, 0.45, c, 1.0 if team else 0.0)


def stand(side_len, rows, origin, out_dir, along, rise=0.44, depth=0.86):
    ox, oy = origin
    for r in range(rows):
        z = 1.0 + r * rise
        cx = ox + out_dir[0] * r * depth; cy = oy + out_dir[1] * r * depth
        box((cx + out_dir[0] * depth / 2, cy + out_dir[1] * depth / 2, z / 2),
            (side_len if along == 'x' else depth, side_len if along == 'y' else depth, z), seat)
        n_seats = int(side_len / 0.55)
        for i in range(n_seats):
            if random.random() < 0.05: continue
            t = -side_len / 2 + (i + 0.5) * side_len / n_seats + random.uniform(-0.05, 0.05)
            px_ = cx + (t if along == 'x' else 0) + out_dir[0] * depth * 0.55
            py_ = cy + (t if along == 'y' else 0) + out_dir[1] * depth * 0.55
            person(px_, py_, z, along)


stand(L + 12, 26, (0, far + 0.6), (0, 1), 'x')
for sx in (-1, 1):
    stand(W + 9, 24, (sx * (L / 2 + 5.8), 0), (sx, 0), 'y')
me = bpy.data.meshes.new("crowd"); bm.to_mesh(me)
crowd = bpy.data.objects.new("crowd", me); bpy.context.collection.objects.link(crowd); crowd.data.materials.append(crowd_m)
upper_led = box((0, far + 23.0, 12.6), (L + 22, 0.15, 0.7), led); ribbon.append(upper_led)

# ---------- light: Poly Haven's Events Hall Interior (CC0) for fill and reflections, area lights over the floor ----------
world = bpy.data.worlds.new("w"); scene.world = world; world.use_nodes = True
wn = world.node_tree.nodes; env = wn.new("ShaderNodeTexEnvironment")
env.image = bpy.data.images.load(HDRI)
# The camera sees a dark hall; the HDRI only lights the scene and fills the reflections.
lp = wn.new("ShaderNodeLightPath"); dark = wn.new("ShaderNodeBackground"); dark.inputs["Color"].default_value = (0.004, 0.005, 0.008, 1)
mixw = wn.new("ShaderNodeMixShader")
world.node_tree.links.new(env.outputs["Color"], wn["Background"].inputs["Color"])
world.node_tree.links.new(lp.outputs["Is Camera Ray"], mixw.inputs["Fac"])
world.node_tree.links.new(wn["Background"].outputs["Background"], mixw.inputs[1])
world.node_tree.links.new(dark.outputs["Background"], mixw.inputs[2])
world.node_tree.links.new(mixw.outputs["Shader"], wn["World Output"].inputs["Surface"])
wn["Background"].inputs["Strength"].default_value = 0.35
for (x, y) in ((-10, -4), (0, -4), (10, -4), (-10, 4), (0, 4), (10, 4)):
    bpy.ops.object.light_add(type='AREA', location=(x, y, 20)); l = bpy.context.object
    l.data.energy = 5200; l.data.size = 6; l.data.color = (1.0, 0.97, 0.92)
bpy.ops.object.light_add(type='AREA', location=(0, far + 10, 14)); l = bpy.context.object
l.data.energy = 6000; l.data.size = 30; l.rotation_euler = (math.radians(60), 0, 0)
for sx in (-1, 1):
    bpy.ops.object.light_add(type='AREA', location=(sx * (L / 2 + 14), 0, 12)); l = bpy.context.object
    l.data.energy = 5000; l.data.size = 20; l.rotation_euler = (0, math.radians(-60 * sx), 0)

# ---------- the broadcast camera: high on the near side, the whole floor in view ----------
bpy.ops.object.camera_add(location=(0, -21.5, 9.6)); cam = bpy.context.object; scene.camera = cam
cam.data.lens = 19
cam.rotation_euler = (Vector((0, 3.0, 0.6)) - cam.location).to_track_quat('-Z', 'Y').to_euler()

scene.render.engine = 'CYCLES'; scene.cycles.device = 'CPU'; scene.cycles.samples = SAMPLES
scene.cycles.use_denoising = True
RW, RH = int(os.environ.get("HW_RENDER_W", 1920)), int(os.environ.get("HW_RENDER_H", 1080))
scene.render.resolution_x = RW; scene.render.resolution_y = RH
scene.view_settings.view_transform = 'Standard'; scene.view_settings.look = 'Medium High Contrast'
scene.view_settings.exposure = -0.85
bpy.context.view_layer.update()


def px(p):
    v = world_to_camera_view(scene, cam, Vector(p))
    return [round(v.x * 1920, 1), round((1 - v.y) * 1080, 1)]


def court(xf, yf):
    """Game court feet (x 0..94 baseline to baseline, y 0..50 far sideline to near) to the world."""
    return (xf / 94 * L - L / 2, W / 2 - yf / 50 * W, 0)


meta = {
    "size": [1920, 1080],
    # Court corners in feet order (0,0), (94,0), (94,50), (0,50): far-left, far-right, near-right, near-left.
    "court": [px(court(0, 0)), px(court(94, 0)), px(court(94, 50)), px(court(0, 50))],
    "rims": [px((-HOOP, 0, 3.05)), px((HOOP, 0, 3.05))],
    "rim_floor": [px((-HOOP, 0, 0)), px((HOOP, 0, 0))],
}
with open(os.path.splitext(OUT)[0] + ".json", "w") as f:
    json.dump(meta, f, indent=1)
scene.render.filepath = OUT
bpy.ops.render.render(write_still=True)

# ---------- the tint mask: team shirts and the ribbon boards white, everything else black ----------
black = bpy.data.materials.new("black"); black.use_nodes = True
bn = black.node_tree.nodes; bn.remove(bn["Principled BSDF"])
em = bn.new("ShaderNodeEmission"); em.inputs["Color"].default_value = (0, 0, 0, 1)
black.node_tree.links.new(em.outputs["Emission"], bn["Material Output"].inputs["Surface"])
whiteem = black.copy(); whiteem.node_tree.nodes["Emission"].inputs["Color"].default_value = (1, 1, 1, 1)
mask_m = black.copy(); mn = mask_m.node_tree.nodes
mvc = mn.new("ShaderNodeVertexColor"); mvc.layer_name = "Mask"
mask_m.node_tree.links.new(mvc.outputs["Color"], mn["Emission"].inputs["Color"])
for o in scene.objects:
    if o.type not in ('MESH', 'FONT', 'CURVE'): continue
    m = mask_m if o == crowd else whiteem if o in ribbon else black
    o.data.materials.clear(); o.data.materials.append(m)
for o in [o for o in scene.objects if o.type == 'LIGHT']: o.hide_render = True
wn["Background"].inputs["Strength"].default_value = 0.0
scene.cycles.samples = 4; scene.cycles.use_denoising = False
scene.render.resolution_x = RW // 2; scene.render.resolution_y = RH // 2
scene.view_settings.look = 'None'; scene.view_settings.exposure = 0
scene.render.filepath = os.path.splitext(OUT)[0] + "-mask.png"
bpy.ops.render.render(write_still=True)
print("done", OUT, meta)
