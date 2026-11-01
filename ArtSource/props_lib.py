# Props for the cathedral hall, each exported to its own FBX. Run after build_tower.py (save_png, emission_material).
# Every prop is built at the origin, standing on z = 0 and facing -y (Unity +z).
import bpy, bmesh, math
import numpy as np
from mathutils import Vector, Matrix

PROP_MATS = ["M_Tower_Statue", "M_Tower_Iron", "M_Tower_Wax", "M_Tower_Flame", "M_Tower_Clay", "M_Tower_Wall", "M_Tower_Rough", "M_Tower_Void", "M_Tower_Soil"]
ST, IRN, WAX, FLM, CLY, WAL, RGH, VOD, SOI = range(len(PROP_MATS))
PROP_TILE = [1.0, 1.0, 0.5, 1.0, 1.0, 1.5, 1.5, 1.0, 1.0]


def noise_texture(name, base, var, blotch, seed):
    n = 64
    rng = np.random.default_rng(seed)
    coarse = rng.uniform(-1, 1, (8, 8))
    coarse = np.kron(coarse, np.ones((8, 8)))
    fine = rng.uniform(-1, 1, (n, n))
    k = (1 + blotch * coarse + var * fine)[..., None]
    col = np.clip(np.array(base) * k, 0, 1)
    return save_png(name, np.concatenate([col, np.ones((n, n, 1))], axis=2), (n, n))

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
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = color
    return m

def ensure_prop_materials():
    textured_material("M_Tower_Statue", noise_texture("statue_stone", (0.58, 0.56, 0.52), 0.08, 0.12, 3))
    textured_material("M_Tower_Clay", noise_texture("clay", (0.42, 0.26, 0.18), 0.1, 0.15, 5))
    textured_material("M_Tower_Soil", noise_texture("soil", (0.22, 0.15, 0.1), 0.2, 0.22, 41))
    color_material("M_Tower_Wax", (0.85, 0.78, 0.62, 1))
    emission_material("M_Tower_Flame", None, (1.0, 0.62, 0.22, 1))


class Builder:
    def __init__(self):
        self.bm = bmesh.new()

    def verts(self, pts):
        return [self.bm.verts.new(p) for p in pts]

    def ring_solid(self, rings, mi, cap0=True, cap1=True):
        """rings: list of vertex rings (same count), stitched in order; optional caps on the first/last ring."""
        n = len(rings[0])
        for a, b in zip(rings, rings[1:]):
            for i in range(n):
                j = (i + 1) % n
                self.bm.faces.new((a[i], a[j], b[j], b[i])).material_index = mi
        if cap0:
            self.bm.faces.new(list(reversed(rings[0]))).material_index = mi
        if cap1:
            self.bm.faces.new(rings[-1]).material_index = mi

    def lathe(self, prof, seg, mi, c=(0, 0, 0), sy=1.0, phase=0.0, cap=True):
        """Solid of revolution of (r, z) points, bottom to top."""
        rings = []
        for r, z in prof:
            rings.append(self.verts([Vector((c[0] + r * math.cos(phase + 2 * math.pi * i / seg), c[1] + sy * r * math.sin(phase + 2 * math.pi * i / seg), c[2] + z)) for i in range(seg)]))
        self.ring_solid(rings, mi, cap, cap)

    def limb(self, p0, p1, r0, r1, seg, mi):
        """Tapered cylinder between two points."""
        p0, p1 = Vector(p0), Vector(p1)
        d = (p1 - p0).normalized()
        a = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized()
        b = d.cross(a)
        rings = []
        for p, r in ((p0, r0), (p1, r1)):
            rings.append(self.verts([p + (a * math.cos(2 * math.pi * i / seg) + b * math.sin(2 * math.pi * i / seg)) * r for i in range(seg)]))
        self.ring_solid(rings, mi)

    def box(self, c, size, mi, rot=None):
        c = Vector(c)
        hx, hy, hz = (s / 2 for s in size)
        M = rot or Matrix.Identity(3)
        pts = [c + M @ Vector((x * hx, y * hy, z * hz)) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
        v = self.verts(pts)
        for f in ((0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)):
            self.bm.faces.new([v[i] for i in f]).material_index = mi

    def tapered_box(self, z0, z1, w0, w1, d0, d1, mi, y0=0.0, y1=0.0, x=0.0):
        """Box whose width/depth change from bottom (z0) to top (z1); y0/y1 shift it back or forth."""
        lo = self.verts([Vector((x + sx * w0 / 2, y0 + sy * d0 / 2, z0)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
        hi = self.verts([Vector((x + sx * w1 / 2, y1 + sy * d1 / 2, z1)) for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1))])
        self.ring_solid([lo, hi], mi)

    def flame(self, c, h=0.09, w=0.035):
        c = Vector(c)
        v = self.verts([c + Vector(o) for o in ((0, 0, 0), (w, 0, h * 0.35), (0, w, h * 0.35), (-w, 0, h * 0.35), (0, -w, h * 0.35), (0, 0, h))])
        for a, b in ((1, 2), (2, 3), (3, 4), (4, 1)):
            self.bm.faces.new((v[0], v[b], v[a])).material_index = FLM
            self.bm.faces.new((v[5], v[a], v[b])).material_index = FLM

    def candle(self, x, y, z, r, h, seg=6):
        self.lathe([(r, 0), (r, h)], seg, WAX, c=(x, y, z))
        self.flame((x, y, z + h + 0.01))

    def finish(self, name, coll):
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0001)
        bmesh.ops.dissolve_degenerate(bm, edges=bm.edges[:], dist=0.0001)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        uvl = bm.loops.layers.uv.verify()
        bm.normal_update()
        for f in bm.faces:
            T = PROP_TILE[f.material_index]
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for l in f.loops:
                p = l.vert.co
                l[uvl].uv = ((p.y, p.z), (p.x, p.z), (p.x, p.y))[ax]
                l[uvl].uv = (l[uvl].uv[0] / T, l[uvl].uv[1] / T)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for m in PROP_MATS:
            me.materials.append(bpy.data.materials[m])
        # drop unused slots so Unity doesn't get empty materials
        used = sorted({p.material_index for p in me.polygons})
        remap = {old: new for new, old in enumerate(used)}
        mats = [me.materials[i] for i in used]
        idx = [remap[p.material_index] for p in me.polygons]
        me.materials.clear()
        for m in mats:
            me.materials.append(m)
        me.polygons.foreach_set("material_index", idx)
        for p in me.polygons:
            p.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(40))
        ob = bpy.data.objects.new(name, me)
        coll.objects.link(ob)
        return ob


def knight_body(b, mi, plinth=True):
    if plinth:
        b.box((0, 0, 0.1), (0.8, 0.8, 0.2), mi)
    z = 0.2 if plinth else 0.0
    for sx in (-1, 1):
        b.box((sx * 0.14, -0.05, z + 0.06), (0.16, 0.3, 0.12), mi)
        b.limb((sx * 0.14, 0, z + 0.1), (sx * 0.15, 0, z + 0.8), 0.11, 0.13, 8, mi)
        b.limb((sx * 0.3, 0.02, z + 1.33), (sx * 0.26, -0.12, z + 1.04), 0.09, 0.075, 8, mi)
        b.limb((sx * 0.26, -0.12, z + 1.04), (sx * 0.07, -0.28, z + 0.93), 0.075, 0.065, 8, mi)
        b.lathe([(0.0, 0.0), (0.15, 0.02), (0.16, 0.1), (0.1, 0.16)], 8, mi, c=(sx * 0.3, 0.02, z + 1.28))
        b.box((sx * 0.05, -0.3, z + 0.92), (0.1, 0.11, 0.12), mi)
    b.lathe([(0.3, 0.0), (0.24, 0.3)], 10, mi, c=(0, 0, z + 0.65), sy=0.75)
    b.lathe([(0.24, 0.0), (0.29, 0.38), (0.2, 0.48)], 10, mi, c=(0, 0, z + 0.95), sy=0.7)
    b.lathe([(0.08, 0.0), (0.08, 0.08)], 8, mi, c=(0, 0, z + 1.42))
    b.lathe([(0.14, 0.0), (0.14, 0.2), (0.1, 0.27)], 8, mi, c=(0, 0, z + 1.48), sy=1.05)
    b.box((0, -0.135, z + 1.62), (0.18, 0.02, 0.03), VOD)
    # sword planted in front, hands on the pommel
    b.box((0, -0.3, z + 1.0), (0.07, 0.07, 0.07), mi)
    b.limb((0, -0.3, z + 0.97), (0, -0.3, z + 0.8), 0.025, 0.025, 6, mi)
    b.box((0, -0.3, z + 0.79), (0.36, 0.05, 0.05), mi)
    b.tapered_box(z + 0.02, z + 0.77, 0.02, 0.08, 0.02, 0.025, mi, y0=-0.3, y1=-0.3)
    # cloak hanging from the shoulders
    b.tapered_box(z + 0.2, z + 1.38, 0.75, 0.6, 0.06, 0.06, mi, y0=0.2, y1=0.12)

def build_knight(coll):
    b = Builder()
    knight_body(b, ST)
    return b.finish("Statue_Knight", coll)

def build_fallen_knight(coll):
    """Armoured body for the floor: the knight without a plinth, in iron. Lay it down in Unity."""
    b = Builder()
    knight_body(b, IRN, plinth=False)
    return b.finish("Knight_Fallen", coll)

def build_saint(coll):
    b = Builder()
    b.box((0, 0, 0.1), (0.7, 0.7, 0.2), ST)
    b.lathe([(0.34, 0.2), (0.3, 0.7), (0.22, 1.45), (0.17, 1.58)], 10, ST, sy=0.8)
    b.lathe([(0.17, 0.0), (0.15, 0.22), (0.11, 0.36), (0.0, 0.42)], 10, ST, c=(0, 0.02, 1.52), sy=1.0)
    b.box((0, -0.15, 1.66), (0.14, 0.03, 0.16), VOD)
    for sx in (-1, 1):
        b.limb((sx * 0.2, 0, 1.45), (sx * 0.07, -0.22, 1.15), 0.075, 0.1, 8, ST)
    b.box((0, -0.27, 1.18), (0.24, 0.06, 0.3), ST, rot=Matrix.Rotation(math.radians(-25), 3, "X"))
    return b.finish("Statue_Saint", coll)

def build_candelabrum(coll):
    b = Builder()
    for k in range(3):
        a = math.radians(90 + 120 * k)
        b.limb((0, 0, 0.3), (0.38 * math.cos(a), 0.38 * math.sin(a), 0.02), 0.03, 0.025, 5, IRN)
    b.lathe([(0.09, 0.15), (0.09, 0.35)], 8, IRN)
    b.lathe([(0.03, 0.35), (0.03, 1.6)], 6, IRN)
    for z in (0.8, 1.2):
        b.lathe([(0.03, z - 0.05), (0.07, z), (0.03, z + 0.05)], 8, IRN)
    b.lathe([(0.04, 1.58), (0.2, 1.62), (0.2, 1.65)], 10, IRN)
    b.candle(0, 0, 1.65, 0.04, 0.24)
    for sx in (-1, 1):
        b.limb((0, 0, 1.55), (sx * 0.3, 0, 1.74), 0.022, 0.022, 5, IRN)
        b.lathe([(0.02, 1.72), (0.07, 1.76), (0.07, 1.78)], 8, IRN, c=(sx * 0.3, 0, 0))
        b.candle(sx * 0.3, 0, 1.78, 0.035, 0.18)
    return b.finish("Candelabrum", coll)

def build_chandelier(coll, chain=11.5):
    """Origin at the centre of the candle ring; the chain runs `chain` metres up."""
    b = Builder()
    seg = 24
    for r0, r1 in ((1.3, 1.42), (0.58, 0.66)):
        rings = []
        for r, z in ((r0, -0.05), (r1, -0.05), (r1, 0.05), (r0, 0.05)):
            rings.append(b.verts([Vector((r * math.cos(2 * math.pi * i / seg), r * math.sin(2 * math.pi * i / seg), z)) for i in range(seg)]))
        b.ring_solid(rings + [rings[0]], IRN, False, False)
    for k in range(6):
        a = math.radians(60 * k)
        c = Vector((math.cos(a), math.sin(a), 0)) * 0.99
        b.box(c, (0.72, 0.06, 0.06), IRN, rot=Matrix.Rotation(a, 3, "Z"))
    for k in range(12):
        a = 2 * math.pi * k / 12
        b.candle(1.36 * math.cos(a), 1.36 * math.sin(a), 0.05, 0.04, 0.2)
    for k in range(6):
        a = 2 * math.pi * (k + 0.5) / 6
        b.candle(0.62 * math.cos(a), 0.62 * math.sin(a), 0.05, 0.035, 0.15)
    b.lathe([(0.1, -0.35), (0.16, -0.1), (0.12, 0.05), (0.04, 0.12)], 8, IRN)
    hook = Vector((0, 0, 2.2))
    for k in range(4):
        a = math.radians(45 + 90 * k)
        b.limb((1.36 * math.cos(a), 1.36 * math.sin(a), 0.05), hook, 0.025, 0.025, 4, IRN)
    b.lathe([(0.12, 2.1), (0.12, 2.3)], 8, IRN)
    b.box((0, 0, 2.3 + chain / 2), (0.06, 0.06, chain), IRN)
    return b.finish("Chandelier", coll)

def build_candles(coll):
    b = Builder()
    b.lathe([(0.32, 0.0), (0.34, 0.015)], 10, WAX)
    rng = np.random.default_rng(11)
    for k in range(7):
        a = 2 * math.pi * k / 7 + rng.uniform(-0.3, 0.3)
        r = 0.08 if k == 0 else rng.uniform(0.12, 0.26)
        x, y = (0.0, 0.0) if k == 0 else (r * math.cos(a), r * math.sin(a))
        b.candle(x, y, 0.015, rng.uniform(0.03, 0.05), rng.uniform(0.08, 0.32))
    return b.finish("Candles", coll)

def build_urn(coll):
    b = Builder()
    b.lathe([(0.17, 0.0), (0.22, 0.05), (0.3, 0.25), (0.33, 0.45), (0.28, 0.65), (0.16, 0.78), (0.12, 0.86), (0.18, 0.9), (0.18, 0.94)], 10, CLY)
    return b.finish("Urn", coll)

def build_rubble(coll):
    b = Builder()
    rng = np.random.default_rng(21)
    for k in range(13):
        r = rng.uniform(0, 1.1)
        a = rng.uniform(0, 2 * math.pi)
        s = rng.uniform(0.25, 0.7, 3) * np.array([1.0, 0.8, 0.6]) * (1.2 - r * 0.4)
        z = s[2] / 2 * 0.8 + (0.25 if r < 0.4 and k > 5 else 0.0)
        rot = (Matrix.Rotation(rng.uniform(0, math.pi), 3, "Z") @ Matrix.Rotation(rng.uniform(-0.4, 0.4), 3, "X"))
        b.box((r * math.cos(a), r * math.sin(a), z), tuple(s), RGH if k % 3 else WAL, rot=rot)
    return b.finish("Rubble", coll)

def build_column_drum(coll):
    """Fallen piece of a pier lying along x, with a broken end."""
    b = Builder()
    seg = 12
    rng = np.random.default_rng(31)
    rings = []
    for x in (-0.8, 0.8):
        rings.append(b.verts([Vector((x, 0.85 * math.cos(2 * math.pi * i / seg), 0.85 + 0.85 * math.sin(2 * math.pi * i / seg))) for i in range(seg)]))
    broken = b.verts([Vector((0.8 + rng.uniform(0.05, 0.4), 0.6 * math.cos(2 * math.pi * i / seg), 0.85 + 0.6 * math.sin(2 * math.pi * i / seg))) for i in range(seg)])
    b.ring_solid(rings + [broken], WAL, True, True)
    return b.finish("Column_Drum", coll)

def build_planter(coll):
    """Octagonal stone bed full of earth in front of the far wall, where the boss tree grows. A flat side faces -y."""
    b = Builder()
    ph = math.pi / 8
    b.lathe([(3.95, 0.0), (3.95, 0.12), (3.85, 0.12), (3.85, 0.4), (3.92, 0.4), (3.92, 0.48), (3.45, 0.48), (3.45, 0.0)],
            8, RGH, phase=ph, cap=False)
    # earth: 16 points round the inner octagon, scaled in towards a mound in the middle
    rng = np.random.default_rng(51)
    edge = []
    for i in range(8):
        a0, a1 = ph + 2 * math.pi * i / 8, ph + 2 * math.pi * (i + 1) / 8
        p0 = Vector((math.cos(a0), math.sin(a0), 0)) * 3.45
        p1 = Vector((math.cos(a1), math.sin(a1), 0)) * 3.45
        edge += [p0, (p0 + p1) / 2]
    rings = []
    for k, z in ((1.0, 0.36), (0.75, 0.44), (0.5, 0.52), (0.25, 0.58)):
        jitter = 0.0 if k == 1.0 else 0.04
        rings.append(b.verts([Vector((p.x * k, p.y * k, z + rng.uniform(-jitter, jitter))) for p in edge]))
    top = b.verts([Vector((0, 0, 0.62))])[0]
    n = len(edge)
    for r0, r1 in zip(rings, rings[1:]):
        for i in range(n):
            j = (i + 1) % n
            b.bm.faces.new((r0[i], r0[j], r1[j], r1[i])).material_index = SOI
    for i in range(n):
        b.bm.faces.new((rings[-1][i], rings[-1][(i + 1) % n], top)).material_index = SOI
    return b.finish("Planter", coll)

PROP_BUILDERS = [build_knight, build_fallen_knight, build_saint, build_candelabrum, build_chandelier, build_candles,
                 build_urn, build_rubble, build_column_drum, build_planter]

def build_props():
    ensure_prop_materials()
    coll = get_coll("Props")
    for ob in list(coll.objects):
        me = ob.data
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.meshes.remove(me)
    return [f(coll) for f in PROP_BUILDERS]
