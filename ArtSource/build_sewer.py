# Tutorial sewer in Sewer.blend: exec(open(r"...\ArtSource\build_sewer.py").read()); build_all(); export_sewer()
# Layout is written in Unity terms: X right, P forward (Unity +z), z up. V() turns it into Blender coordinates,
# so after the FBX axis conversion Blender (x, y, z) -> Unity (-x, z, -y) gives back (X, z, P).
import os
import numpy as np

ART = os.path.dirname(bpy.data.filepath)
PROJECT = os.path.dirname(ART)
OUT_DIR = os.path.join(PROJECT, "Assets", "_Project", "Art", "Sewer")
TEX_DIR = os.path.join(OUT_DIR, "Textures")
exec(open(os.path.join(ART, "tower_lib.py")).read())

# Corridor
TUN_W = 6.0
TUN_SPRING = 2.6
GUT_W = 1.6          # gutter top width
GUT_BOT = 0.8        # gutter bottom width
GUT_D = 0.3
START_P = -1.0
HEAP_P0, HEAP_P1 = 22.8, 25.2   # flat floor under the crate heap: the gutter stops, nothing crawls under the crates
SIDE_P0, SIDE_P1 = 29.0, 31.4   # dark side passage on the left, where the first skeleton walks away
SIDE_END_X = -12.0
# Cistern with the chasm
CIS_P0, CIS_P1 = 34.0, 50.5
CIS_W = 16.0
CIS_BOT, CIS_TOP = -8.0, 9.0
NEAR_EDGE_P = 35.2
FAR_EDGE_P = 45.0
FAR_Z = 2.5
CURB_H = 0.45
# Room
ROOM_P0, ROOM_P1 = 51.5, 65.0
ROOM_W = 14.0
ROOM_H = 6.0
ENTRY_W = 6.0
PILLARS = [(-3.5, 55.0), (3.5, 55.0), (-3.5, 61.5), (3.5, 61.5)]
DRAIN_W, DRAIN_H = 0.9, 0.65
DRAIN_DEPTH = 3.5
# Solid block everything is cut from
BLOCK = (-14.0, 14.0, -4.0, 70.0, -10.0, 12.0)   # X0 X1 P0 P1 z0 z1
# Light chunks along P (WebGL: 4 lights per object)
CHUNKS = [("Sewer_Start", -1e4, 12.0), ("Sewer_Corridor", 12.0, 26.0), ("Sewer_Edge", 26.0, CIS_P0),
          ("Sewer_Cistern", CIS_P0, ROOM_P0 - 0.5), ("Sewer_Room", ROOM_P0 - 0.5, 1e4)]

MATS = ["M_Sewer_Brick", "M_Sewer_BrickBase", "M_Sewer_Slab", "M_Sewer_Slime", "M_Sewer_Water", "M_Sewer_Black",
        "M_Tower_Rough", "M_Tower_Iron", "M_Tower_Wood"]
BRICK, BASE, SLAB, SLIME, WATER, BLACK, ROUGH, IRON, WOOD = MATS
S_TILE = {BRICK: 2.0, BASE: 1.0, SLAB: 2.0, SLIME: 1.0, WATER: 2.0, BLACK: 1.0, ROUGH: 1.5, IRON: 1.0, WOOD: 1.0}


def V(X, P, z):
    return Vector((-X, -P, z))


# ---------- Textures and materials ----------

def save_png(name, rgba, size):
    os.makedirs(TEX_DIR, exist_ok=True)
    img = bpy.data.images.get(name) or bpy.data.images.new(name, size[0], size[1], alpha=True)
    if tuple(img.size) != tuple(size):
        img.scale(*size)
    img.pixels.foreach_set(rgba.astype(np.float32).ravel())
    img.filepath_raw = os.path.join(TEX_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    return img


def blotches(rng, n, cell):
    c = rng.uniform(-1, 1, (n // cell, n // cell))
    return np.kron(c, np.ones((cell, cell)))


def bricks(n, seed, bw=16, bh=8):
    """Running-bond bricks, 64 px per metre (brick 0.25 x 0.125 m). Row 0 is the bottom (v = 0)."""
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:n, 0:n]
    row = y // bh
    xs = x + (row % 2) * (bw // 2)
    bid = (row * 64 + (xs // bw) % (n // bw)) % 4096
    tint = rng.uniform(0.7, 1.1, 4096)[bid][..., None] * np.array([0.36, 0.25, 0.19])
    green = rng.random(4096)[bid] < 0.25
    tint[green] = (rng.uniform(0.7, 1.0, 4096)[bid][..., None] * np.array([0.24, 0.27, 0.2]))[green]
    col = tint * (1 + 0.07 * rng.uniform(-1, 1, (n, n, 1)) + 0.08 * blotches(rng, n, 8)[..., None])
    yy = y % bh
    col[yy == 1] *= 0.8
    col[yy == bh - 1] *= 1.12
    mortar = (yy == 0) | (xs % bw == 0)
    col[mortar] = np.array([0.11, 0.11, 0.09])
    return col, rng


def make_textures():
    col, _ = bricks(128, 3)
    save_png("sewer_brick", np.concatenate([col, np.ones((128, 128, 1))], axis=2), (128, 128))

    # damp base band: same bricks, slime rising from the floor (v = 0 at the floor)
    n = 64
    col, rng = bricks(n, 3)
    v = (np.mgrid[0:n, 0:n][0] / (n - 1))[..., None]
    wet = np.clip((1 - v) ** 1.3 + 0.25 * blotches(rng, n, 8)[..., None] + 0.15 * rng.uniform(-1, 1, (n, n, 1)), 0, 1)
    slime = np.array([0.12, 0.17, 0.07]) * (1 + 0.2 * rng.uniform(-1, 1, (n, n, 1)))
    col = col * (1 - 0.8 * wet) + slime * 0.8 * wet
    moss = (rng.random((n, n)) < 0.06 * (1 - v[..., 0]) ** 2)
    col[moss] = np.array([0.2, 0.3, 0.08])
    save_png("sewer_brick_base", np.concatenate([col, np.ones((n, n, 1))], axis=2), (n, n))

    # walkway slabs 1 x 0.5 m
    n = 128
    rng = np.random.default_rng(11)
    y, x = np.mgrid[0:n, 0:n]
    row = y // 32
    xs = x + (row % 2) * 32
    sid = row * 4 + (xs // 64) % 2
    col = rng.uniform(0.75, 1.05, 64)[sid][..., None] * np.array([0.3, 0.31, 0.27])
    col *= 1 + 0.06 * rng.uniform(-1, 1, (n, n, 1)) + 0.12 * blotches(rng, n, 16)[..., None]
    col[(y % 32 == 0) | (xs % 64 == 0)] = np.array([0.1, 0.1, 0.08])
    save_png("sewer_slab", np.concatenate([col, np.ones((n, n, 1))], axis=2), (n, n))

    n = 64
    rng = np.random.default_rng(17)
    col = np.array([0.12, 0.15, 0.07]) * (1 + 0.25 * rng.uniform(-1, 1, (n, n, 1)) + 0.3 * blotches(rng, n, 8)[..., None])
    save_png("sewer_slime", np.concatenate([col, np.ones((n, n, 1))], axis=2), (n, n))

    rng = np.random.default_rng(23)
    col = np.array([0.05, 0.11, 0.07]) * (1 + 0.15 * rng.uniform(-1, 1, (n, n, 1)) + 0.25 * blotches(rng, n, 16)[..., None])
    streak = rng.random((n, n)) < 0.03
    streak = streak | np.roll(streak, 1, axis=1) | np.roll(streak, 2, axis=1)
    col[streak] *= 1.8
    save_png("sewer_water", np.concatenate([col, np.ones((n, n, 1))], axis=2), (n, n))


def textured_material(name, image):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = image
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs[0], bsdf.inputs["Base Color"])
    nt.links.new(bsdf.outputs[0], out.inputs[0])
    return m


def color_material(name, color):
    """Stand-ins for materials that already exist in Unity (remapped by name on import)."""
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = color
    return m


def ensure_materials():
    make_textures()
    for name, tex in ((BRICK, "sewer_brick"), (BASE, "sewer_brick_base"), (SLAB, "sewer_slab"),
                      (SLIME, "sewer_slime"), (WATER, "sewer_water")):
        textured_material(name, bpy.data.images[tex])
    color_material(BLACK, (0, 0, 0, 1))
    color_material(ROUGH, (0.3, 0.3, 0.28, 1))
    color_material(IRON, (0.15, 0.13, 0.12, 1))
    color_material(WOOD, (0.3, 0.2, 0.12, 1))


# ---------- Shapes ----------

def prism(bm, prof, mis, origin, along, s0, s1, cap0, cap1):
    """prof: (x, z) loop across the run; along: unit (dX, dP); x runs to the right of `along`."""
    ax, ap = along
    cx, cp = ap, -ax
    def P3(x, s, z):
        return V(origin[0] + cx * x + ax * s, origin[1] + cp * x + ap * s, z)
    r0 = [bm.verts.new(P3(x, s0, z)) for x, z in prof]
    r1 = [bm.verts.new(P3(x, s1, z)) for x, z in prof]
    n = len(prof)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((r0[i], r1[i], r1[j], r0[j])).material_index = mis[i]
    bm.faces.new(list(reversed(r0))).material_index = cap0
    bm.faces.new(r1).material_index = cap1


def vault_profile(w, spring, z0=0.0, seg=10, floor=0, wall=1, arc=1):
    h = w / 2
    pts = [(-h, z0), (h, z0), (h, z0 + spring)]
    for i in range(1, seg):
        a = math.pi * i / seg
        pts.append((h * math.cos(a), z0 + spring + h * math.sin(a)))
    pts.append((-h, z0 + spring))
    mis = [floor, wall] + [arc] * seg + [wall]
    return pts, mis


def rect_profile(x0, x1, z0, z1, floor, wall, top):
    return [(x0, z0), (x1, z0), (x1, z1), (x0, z1)], [floor, wall, top, wall]


def sbox(bm, X0, X1, P0, P1, z0, z1, mi, top=None):
    t = mi if top is None else top
    prism(bm, *rect_profile(X0, X1, z0, z1, t, mi, t), (0.0, 0.0), (0.0, 1.0), P0, P1, mi, mi)


def cyl_along_p(bm, X, z, r, P0, P1, seg, mi):
    prof = [(X + r * math.cos(2 * math.pi * i / seg), z + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]
    prism(bm, prof, [mi] * seg, (0.0, 0.0), (0.0, 1.0), P0, P1, mi, mi)


def mi(name):
    return MATS.index(name)


# ---------- Shell ----------

def build_cutters(cut):
    cutters = []
    def add(name, fill):
        bm = bmesh.new()
        fill(bm)
        cutters.append(obj_from_bm(name, bm, MATS, cut))

    def tunnels(bm):
        prof, mis = vault_profile(TUN_W, TUN_SPRING, floor=mi(SLAB), wall=mi(BRICK), arc=mi(BRICK))
        prism(bm, prof, mis, (0, 0), (0, 1), START_P, CIS_P0 + 0.3, mi(BRICK), mi(BRICK))
    add("cut_tunnels", tunnels)

    # The gutter stops under the crate heap, so nothing can crawl under the crates
    def gutters(bm):
        h, b = GUT_W / 2, GUT_BOT / 2
        prof = [(-b, -GUT_D), (b, -GUT_D), (h, 0.0), (h, 0.2), (-h, 0.2), (-h, 0.0)]
        prism(bm, prof, [mi(SLIME)] * 6, (0, 0), (0, 1), START_P - 0.1, HEAP_P0, mi(SLIME), mi(SLIME))
        prism(bm, prof, [mi(SLIME)] * 6, (0, 0), (0, 1), HEAP_P1, CIS_P0 + 0.3, mi(SLIME), mi(SLIME))
    add("cut_gutters", gutters)

    # Outflow pipe in the start wall: the parasite came out of it
    def outflow(bm):
        seg = 16
        r, zc = 1.0, 1.0 - GUT_D
        prof = [(r * math.cos(2 * math.pi * i / seg), zc + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]
        prism(bm, prof, [mi(SLIME)] * seg, (0, 0), (0, 1), -3.5, START_P + 0.1, mi(BLACK), mi(SLIME))
    add("cut_outflow", outflow)

    def side(bm):
        w = SIDE_P1 - SIDE_P0
        prof, mis = vault_profile(w, 2.0, seg=6, floor=mi(SLAB), wall=mi(BRICK), arc=mi(BRICK))
        prism(bm, prof, mis, (-TUN_W / 2 + 0.2, (SIDE_P0 + SIDE_P1) / 2), (-1, 0), 0, -SIDE_END_X - TUN_W / 2 + 0.2,
              mi(BRICK), mi(BLACK))
    add("cut_side", side)

    def cistern(bm):
        prof, mis = rect_profile(-CIS_W / 2, CIS_W / 2, CIS_BOT, CIS_TOP, mi(SLIME), mi(BRICK), mi(BRICK))
        prism(bm, prof, mis, (0, 0), (0, 1), CIS_P0, CIS_P1, mi(BRICK), mi(BRICK))
    add("cut_cistern", cistern)

    # Separate cutters: overlapping shells inside one cutter confuse the EXACT boolean (it left the arch walled up)
    def entry(bm):
        prof, mis = vault_profile(ENTRY_W, 3.0, z0=FAR_Z, seg=8, floor=mi(SLAB), wall=mi(ROUGH), arc=mi(ROUGH))
        prism(bm, prof, mis, (0, 0), (0, 1), CIS_P1 - 0.1, ROOM_P0 + 0.1, mi(ROUGH), mi(ROUGH))
    add("cut_entry", entry)

    def room(bm):
        prof, mis = rect_profile(-ROOM_W / 2, ROOM_W / 2, FAR_Z, FAR_Z + ROOM_H, mi(SLAB), mi(BRICK), mi(BRICK))
        prism(bm, prof, mis, (0, 0), (0, 1), ROOM_P0, ROOM_P1, mi(BRICK), mi(BRICK))
    add("cut_room", room)

    def drain(bm):
        prof, mis = rect_profile(-DRAIN_W / 2, DRAIN_W / 2, FAR_Z, FAR_Z + DRAIN_H, mi(SLIME), mi(SLIME), mi(SLIME))
        prism(bm, prof, mis, (0, 0), (0, 1), ROOM_P1 - 0.1, ROOM_P1 + DRAIN_DEPTH, mi(SLIME), mi(BLACK))
    add("cut_drain", drain)
    return cutters


def build_shell(coll):
    X0, X1, P0, P1, z0, z1 = BLOCK
    bm = bmesh.new()
    sbox(bm, X0, X1, P0, P1, z0, z1, mi(BRICK))
    solid = obj_from_bm("SewerShell", bm, MATS, coll)
    cutters = build_cutters(get_coll("SewerCutters"))
    for c in cutters:
        mod = solid.modifiers.new(c.name, "BOOLEAN")
        mod.operation = "DIFFERENCE"
        mod.solver = "EXACT"
        mod.material_mode = "TRANSFER"
        mod.object = c
    bake_mesh(solid)
    for c in cutters:
        me = c.data
        bpy.data.objects.remove(c, do_unlink=True)
        bpy.data.meshes.remove(me)
    drop_outer_faces(solid)
    return solid


def drop_outer_faces(ob):
    """The block's outside is never seen: drop faces lying on its bounds."""
    X0, X1, P0, P1, z0, z1 = BLOCK
    bx = sorted((-X0, -X1))
    by = sorted((-P0, -P1))
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    eps = 1e-3
    def on_bound(f):
        for axis, lo, hi in ((0, *bx), (1, *by), (2, z0, z1)):
            vals = [v.co[axis] for v in f.verts]
            if all(abs(c - lo) < eps for c in vals) or all(abs(c - hi) < eps for c in vals):
                return True
        return False
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if on_bound(f)], context="FACES")
    bm.to_mesh(ob.data)
    bm.free()


# ---------- Details (joined into the shell, overlaps are fine) ----------

def build_details(coll):
    bm = bmesh.new()
    # near ledge: the corridor floor runs on into the cistern and breaks off
    sbox(bm, -TUN_W / 2, TUN_W / 2, CIS_P0 - 0.2, NEAR_EDGE_P - 0.5, CIS_BOT, 0, mi(BRICK), mi(SLAB))
    for x0, x1, p1 in ((-3.0, -1.6, NEAR_EDGE_P - 0.1), (-1.6, -0.2, NEAR_EDGE_P + 0.35), (-0.2, 1.4, NEAR_EDGE_P),
                       (1.4, 3.0, NEAR_EDGE_P - 0.3)):
        sbox(bm, x0, x1, NEAR_EDGE_P - 0.6, p1, CIS_BOT, 0, mi(BRICK), mi(SLAB))
    # far ledge in front of the room, with a low curb so enemies don't walk off
    sbox(bm, -CIS_W / 2 - 0.1, CIS_W / 2 + 0.1, FAR_EDGE_P, CIS_P1 + 0.1, CIS_BOT, FAR_Z, mi(BRICK), mi(SLAB))
    sbox(bm, -CIS_W / 2, CIS_W / 2, FAR_EDGE_P, FAR_EDGE_P + 0.4, FAR_Z, FAR_Z + CURB_H, mi(ROUGH))
    # rotten beams across the cistern, high up
    for p in (38.0, 42.0):
        sbox(bm, -CIS_W / 2 - 0.2, CIS_W / 2 + 0.2, p - 0.2, p + 0.2, 6.8, 7.2, mi(WOOD))
    # pipe along the right wall of the corridor, above the crate heap
    cyl_along_p(bm, TUN_W / 2 - 0.3, 3.6, 0.18, START_P, CIS_P0, 8, mi(IRON))
    # room pillars with base and capital
    top = FAR_Z + ROOM_H
    for x, p in PILLARS:
        sbox(bm, x - 0.7, x + 0.7, p - 0.7, p + 0.7, FAR_Z, FAR_Z + 0.5, mi(ROUGH))
        sbox(bm, x - 0.5, x + 0.5, p - 0.5, p + 0.5, FAR_Z + 0.5, top - 0.5, mi(BRICK))
        sbox(bm, x - 0.7, x + 0.7, p - 0.7, p + 0.7, top - 0.5, top + 0.05, mi(ROUGH))
    # stone surround that draws the eye to the drain
    d, h = DRAIN_W / 2, DRAIN_H
    pa, pb = ROOM_P1 - 0.2, ROOM_P1 + 0.01
    sbox(bm, -d - 0.35, -d, pa, pb, FAR_Z, FAR_Z + h + 0.35, mi(ROUGH))
    sbox(bm, d, d + 0.35, pa, pb, FAR_Z, FAR_Z + h + 0.35, mi(ROUGH))
    sbox(bm, -d - 0.35, d + 0.35, pa, pb, FAR_Z + h, FAR_Z + h + 0.35, mi(ROUGH))
    return obj_from_bm("SewerDetails", bm, MATS, coll)


def build_water(coll):
    bm = bmesh.new()
    def plane(X0, X1, P0, P1, z):
        vs = [bm.verts.new(V(x, p, z)) for x, p in ((X0, P0), (X1, P0), (X1, P1), (X0, P1))]
        f = bm.faces.new(vs)
        f.material_index = 0
    w = (GUT_BOT + GUT_W) / 4 + 0.05
    plane(-w, w, START_P - 2.0, HEAP_P0, -0.12)
    plane(-w, w, HEAP_P1, NEAR_EDGE_P - 0.6, -0.12)
    plane(-CIS_W / 2, CIS_W / 2, CIS_P0, CIS_P1, CIS_BOT + 0.4)
    bm.normal_update()
    for f in bm.faces:
        if f.normal.z < 0:
            f.normal_flip()
    me = bpy.data.meshes.new("Sewer_Water")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat(WATER))
    return link(bpy.data.objects.new("Sewer_Water", me), coll)


def build_grate(coll):
    """Separate object: the drain grate opens once the room is cleared. Origin at its bottom centre."""
    bm = bmesh.new()
    d, h = DRAIN_W / 2, DRAIN_H
    sbox(bm, -d, d, -0.03, 0.03, 0, 0.06, 0)
    sbox(bm, -d, d, -0.03, 0.03, h - 0.06, h, 0)
    for i in range(6):
        x = -d + 0.06 + i * (DRAIN_W - 0.12) / 5
        sbox(bm, x - 0.025, x + 0.025, -0.025, 0.025, 0, h, 0)
    ob = obj_from_bm("Drain_Grate", bm, [IRON], coll)
    ob.location = V(0, ROOM_P1 + 0.08, FAR_Z)
    return ob


# ---------- UVs, damp band, chunks ----------

def band_floor(c):
    """Floor height the damp band starts from at face centre c (Blender coords), or None."""
    P = -c.y
    if c.z < CIS_BOT + 2:
        return CIS_BOT
    if P > FAR_EDGE_P - 0.1 and FAR_Z - 0.01 < c.z:
        return FAR_Z
    if P < CIS_P0 + 0.5 and c.z > -0.01:
        return 0.0
    return None


def grid_cut(ob, step=1.0, offset=0.25):
    """PsxLit lights per vertex: a ~1 m grid keeps point lights round instead of smeared over big faces."""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    for axis in range(3):
        lo = min(v.co[axis] for v in bm.verts)
        hi = max(v.co[axis] for v in bm.verts)
        k = math.floor(lo - offset)
        while k + offset < hi:
            co = [0.0, 0.0, 0.0]
            no = [0.0, 0.0, 0.0]
            co[axis] = k + offset
            no[axis] = 1.0
            g = bm.verts[:] + bm.edges[:] + bm.faces[:]
            bmesh.ops.bisect_plane(bm, geom=g, dist=0.001, plane_co=co, plane_no=no)
            k += step
    bm.to_mesh(me)
    bm.free()


def damp_band(ob):
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    for z in (1.0, FAR_Z + 1.0, CIS_BOT + 1.0):
        g = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=g, dist=0.001, plane_co=(0, 0, z), plane_no=(0, 0, 1))
    bm.normal_update()
    b, base = mi(BRICK), mi(BASE)
    for f in bm.faces:
        if f.material_index != b or abs(f.normal.z) > 0.3:
            continue
        c = f.calc_center_median()
        fl = band_floor(c)
        if fl is not None and fl - 0.01 < c.z < fl + 1.0:
            f.material_index = base
    bm.to_mesh(me)
    bm.free()


def box_uvs(ob):
    me = ob.data
    names = [m.name for m in me.materials]
    bm = bmesh.new()
    bm.from_mesh(me)
    uvl = bm.loops.layers.uv.verify()
    bm.normal_update()
    for f in bm.faces:
        name = names[f.material_index]
        T = S_TILE.get(name, 1.0)
        n = f.normal
        zref = 0.0
        if name == BASE:
            zref = band_floor(f.calc_center_median()) or 0.0
        if abs(n.z) > 0.7:
            for l in f.loops:
                l[uvl].uv = (l.vert.co.x / T, l.vert.co.y / T)
        elif abs(n.x) > abs(n.y):
            for l in f.loops:
                l[uvl].uv = (l.vert.co.y / T, (l.vert.co.z - zref) / T)
        else:
            for l in f.loops:
                l[uvl].uv = (l.vert.co.x / T, (l.vert.co.z - zref) / T)
    bm.to_mesh(me)
    bm.free()


def split_chunks(src, coll):
    made = []
    for name, p0, p1 in CHUNKS:
        planes = []
        if p0 > -1e3:
            planes.append(((0, -p0, 0), (0, -1, 0)))
        if p1 < 1e3:
            planes.append(((0, -p1, 0), (0, 1, 0)))
        ob = split_planes(src, planes, name, coll)
        if ob:
            made.append(ob)
    me = src.data
    bpy.data.objects.remove(src, do_unlink=True)
    bpy.data.meshes.remove(me)
    return made


def split_planes(src, planes, name, coll):
    bm = bmesh.new()
    bm.from_mesh(src.data)
    for co, no in planes:
        g = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=g, dist=0.0005, plane_co=co, plane_no=no, clear_inner=True)
    if len(bm.faces) == 0:
        bm.free()
        return None
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in src.data.materials:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    return ob


def smooth(ob):
    me = ob.data
    for p in me.polygons:
        p.use_smooth = True
    me.set_sharp_from_angle(angle=math.radians(30))


def clear_sewer():
    for c in ("Sewer", "SewerCutters"):
        coll = bpy.data.collections.get(c)
        if coll is None:
            continue
        for ob in list(coll.objects):
            me = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            if me and me.users == 0:
                bpy.data.meshes.remove(me)


def build_all():
    ensure_materials()
    clear_sewer()
    coll = get_coll("Sewer")
    shell = build_shell(coll)
    details = build_details(coll)
    bpy.ops.object.select_all(action="DESELECT")
    shell.select_set(True)
    details.select_set(True)
    bpy.context.view_layer.objects.active = shell
    bpy.ops.object.join()
    cleanup(shell)
    grid_cut(shell)
    damp_band(shell)
    box_uvs(shell)
    made = split_chunks(shell, coll)
    for ob in made:
        smooth(ob)
    water = build_water(coll)
    grid_cut(water, 2.0)
    box_uvs(water)
    grate = build_grate(coll)
    box_uvs(grate)
    return made + [water, grate]


def export_fbx(path, sel):
    for o in sel:
        m = o.modifiers.new("Tri", "TRIANGULATE")
        m.quad_method = "BEAUTY"
        m.ngon_method = "BEAUTY"
        m.keep_custom_normals = True
    bpy.ops.object.select_all(action="DESELECT")
    for o in sel:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = sel[0]
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH", "EMPTY"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type="OFF", use_tspace=False,
        add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    for o in sel:
        o.modifiers.remove(o.modifiers["Tri"])


def export_sewer():
    os.makedirs(OUT_DIR, exist_ok=True)
    export_fbx(os.path.join(OUT_DIR, "Sewer.fbx"), list(bpy.data.collections["Sewer"].objects))
