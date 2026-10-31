# Gothic hall on the top floor. Run after tower_lib.py (uses floor_z, get_coll, bake_mesh).
# Blender units: the nave runs along +x, the lift is at the origin inside the round apse.
import bpy, bmesh, math
from mathutils import Vector

HZ = floor_z(FLOORS - 1)
BAY = 8.0
NBAYS = 5
X_END = BAY * NBAYS
PIER_Y = 7.0
WALL_T = 1.2
NAVE_IN = PIER_Y - WALL_T / 2
AISLE_IN = PIER_Y + WALL_T / 2
OUT_Y = 15.0
OUT_T = 1.5
END_T = 1.5
AMB_R = 12.0
AMB_T = 1.5
APSE_ANGLES = [90, 126, 162, 198, 234, 270]
APSE_BAYS = [108, 144, 180, 216, 252]
CAP = 6.6
ARC_RISE = 4.2
LEDGE = 11.0
STRING_Z = 15.0
V_SPRING = 15.5
V_RISE = 7.5
A_RISE = 4.5
TOP = 24.5
LOW_TOP = 12.0
AISLE_YC = (AISLE_IN + OUT_Y) / 2
AMB_RM = (AISLE_IN + AMB_R) / 2
WINDOW_BAYS = [0, 2, 4]
PASSAGE_BAYS = [1, 3]
STATUE_PIERS = [1, 2, 3, 4]
SIDES = (1, -1)

HALL_MATS = ["M_Tower_Wall", "M_Tower_Rough", "M_Tower_Floor", "M_Tower_Ceiling", "M_Tower_Void", "M_Tower_Glass", "M_Tower_Iron"]
WA, RO, FL, CE, VO, GL, IR = range(len(HALL_MATS))
HALL_TILE = [1.5, 1.5, 2.0, 2.0, 1.0, 1.0, 1.0]


def P(x, y, z):
    return Vector((x, y, HZ + z))

def new_obj(name, bm, coll, recalc=True):
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in HALL_MATS:
        me.materials.append(bpy.data.materials[m])
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    return ob

def face_inward(bm, faces, inside):
    bm.normal_update()
    for f in faces:
        if f.normal.dot(inside - f.calc_center_median()) < 0:
            f.normal_flip()


# ---------- pointed arches ----------

def arc_params(s, rise):
    R = (s * s + rise * rise) / (2 * s)
    return R, s - R

def arc_half(s, rise, n, extra=0.0):
    """Right half of a pointed arch from the spring (s, 0) to the apex (0, rise). extra offsets it outward."""
    R, c = arc_params(s, rise)
    R2 = R + extra
    tmax = math.acos(max(-1.0, min(1.0, -c / R2)))
    pts = [(c + R2 * math.cos(tmax * j / n), R2 * math.sin(tmax * j / n)) for j in range(n)]
    return pts + [(0.0, R2 * math.sin(tmax))]

def arc_pts(s, rise, n, extra=0.0):
    right = arc_half(s, rise, n, extra)
    return [(-u, h) for u, h in right] + [(u, h) for u, h in reversed(right[:-1])]

def arc_inv(s, rise, h):
    R, c = arc_params(s, rise)
    return c + math.sqrt(max(0.0, R * R - h * h))

def arc_h(s, rise, u):
    R, c = arc_params(s, rise)
    return math.sqrt(max(0.0, R * R - (abs(u) - c) ** 2))

def arch_outline(s, z0, spring, rise, n=6):
    """Closed opening outline (u, z): bottom edge first, then right jamb, arch and left jamb."""
    right = [(u, spring + h) for u, h in arc_half(s, rise, n)]
    left = [(-u, z) for u, z in reversed(right[:-1])]
    return [(-s, z0), (s, z0)] + right + left

def rect(u0, u1, z0, z1):
    return [(u0, z0), (u1, z0), (u1, z1), (u0, z1)]

def scaled(outline, k):
    return [(u * k, z) for u, z in outline]


# ---------- placement: (u along the wall, z up, d = depth coordinate) -> world ----------

def place_y(xc, side):
    return lambda u, z, d: P(xc + u, side * d, z)

def place_x(yc):
    return lambda u, z, d: P(d, yc + u, z)

def place_r(ang):
    a = math.radians(ang)
    c, s = math.cos(a), math.sin(a)
    return lambda u, z, d: P(c * d - s * u, s * d + c * u, z)


# ---------- solids ----------

def prism(bm, outline, place, d0, d1, side_mi, cap0_mi, cap1_mi, bottom_mi=None, outline1=None):
    o1 = outline1 or outline
    r0 = [bm.verts.new(place(u, z, d0)) for u, z in outline]
    r1 = [bm.verts.new(place(u, z, d1)) for u, z in o1]
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        f = bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
        f.material_index = bottom_mi if (i == 0 and bottom_mi is not None) else side_mi
    bm.faces.new(list(reversed(r0))).material_index = cap0_mi
    bm.faces.new(r1).material_index = cap1_mi

def box_place(bm, place, u0, u1, z0, z1, d0, d1, mi):
    prism(bm, rect(u0, u1, z0, z1), place, d0, d1, mi, mi, mi)

def box_xyz(bm, x0, x1, y0, y1, z0, z1, mi):
    prism(bm, rect(x0, x1, z0, z1), lambda u, z, d: P(u, d, z), y0, y1, mi, mi, mi)

def lathe(bm, prof, a0, a1, seg, mi):
    """Sweep a closed (r, z) profile around the lift axis from a0 to a1 degrees, capped at both ends."""
    rings = []
    for i in range(seg + 1):
        a = math.radians(a0 + (a1 - a0) * i / seg)
        rings.append([bm.verts.new(P(r * math.cos(a), r * math.sin(a), z)) for r, z in prof])
    n = len(prof)
    for i in range(seg):
        A, B = rings[i], rings[i + 1]
        for k in range(n):
            l = (k + 1) % n
            bm.faces.new((A[k], A[l], B[l], B[k])).material_index = mi
    bm.faces.new(rings[0]).material_index = mi
    bm.faces.new(list(reversed(rings[-1]))).material_index = mi

def carve(solid, cutters):
    """cutters: list of (suffix, bmesh). Subtract them all from solid and bake the result."""
    cut_coll = get_coll("Cutters")
    objs = [new_obj(solid.name + "_" + n, bm, cut_coll) for n, bm in cutters]
    for c in objs:
        m = solid.modifiers.new(c.name, "BOOLEAN")
        m.operation = "DIFFERENCE"
        m.solver = "EXACT"
        m.material_mode = "TRANSFER"
        m.object = c
    bake_mesh(solid)
    for c in objs:
        me = c.data
        bpy.data.objects.remove(c, do_unlink=True)
        bpy.data.meshes.remove(me)
    return solid

def hall_uvs(ob):
    """Planar world UVs; curved walls of the apse (x < 0) get cylindrical UVs around the lift axis."""
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    uvl = bm.loops.layers.uv.verify()
    bm.normal_update()
    for f in bm.faces:
        T = HALL_TILE[f.material_index]
        n = f.normal
        if abs(n.z) > 0.7:
            for l in f.loops:
                l[uvl].uv = (l.vert.co.x / T, l.vert.co.y / T)
            continue
        c = f.calc_center_median()
        nh = Vector((n.x, n.y)).normalized()
        rc = math.hypot(c.x, c.y)
        if rc > 1 and all(l.vert.co.x < 0.02 for l in f.loops) and abs(nh.dot(Vector((c.x, c.y)) / rc)) > 0.9:
            R = round(rc * 10) / 10
            for l in f.loops:
                a = math.atan2(l.vert.co.y, l.vert.co.x) % (2 * math.pi)
                l[uvl].uv = (a * R / T, (l.vert.co.z - HZ) / T)
        else:
            t = Vector((-nh.y, nh.x))
            for l in f.loops:
                p = l.vert.co
                l[uvl].uv = ((p.x * t.x + p.y * t.y) / T, (p.z - HZ) / T)
    bm.to_mesh(me)
    bm.free()


# ---------- parts with their own UVs ----------

def cyl(bm, uvl, cx, cy, r0, r1, z0, z1, seg, mi, phase=0.0):
    T = HALL_TILE[mi]
    a = [phase + 2 * math.pi * i / seg for i in range(seg)]
    bot = [bm.verts.new(P(cx + r0 * math.cos(t), cy + r0 * math.sin(t), z0)) for t in a]
    top = [bm.verts.new(P(cx + r1 * math.cos(t), cy + r1 * math.sin(t), z1)) for t in a]
    step = 2 * math.pi * (r0 + r1) / 2 / seg / T
    for i in range(seg):
        j = (i + 1) % seg
        f = bm.faces.new((bot[i], bot[j], top[j], top[i]))
        f.material_index = mi
        for l, uv in zip(f.loops, ((i * step, z0 / T), ((i + 1) * step, z0 / T), ((i + 1) * step, z1 / T), (i * step, z1 / T))):
            l[uvl].uv = uv
    for ring in (top, list(reversed(bot))):
        f = bm.faces.new(ring)
        f.material_index = mi
        for l in f.loops:
            l[uvl].uv = (l.vert.co.x / T, l.vert.co.y / T)

def pier(bm, uvl, cx, cy, ang, core=0.85):
    """Round pier with four attached shafts, moulded base and a bell capital that carries the arcade."""
    cyl(bm, uvl, cx, cy, 1.45, 1.45, 0.0, 0.45, 8, WA, phase=math.pi / 8)
    cyl(bm, uvl, cx, cy, 1.25, 1.12, 0.45, 0.75, 12, WA)
    cyl(bm, uvl, cx, cy, core, core, 0.75, CAP - 0.75, 12, WA)
    for k in range(4):
        a = math.radians(ang + 90 * k)
        cyl(bm, uvl, cx + core * math.cos(a), cy + core * math.sin(a), 0.27, 0.27, 0.75, CAP - 0.75, 8, WA)
    cyl(bm, uvl, cx, cy, 1.15, 1.42, CAP - 0.75, CAP - 0.2, 12, WA)
    cyl(bm, uvl, cx, cy, 1.55, 1.55, CAP - 0.2, CAP, 8, RO, phase=math.pi / 8)

def respond(bm, uvl, cx, cy):
    """Half column against an outer wall, carrying the aisle vault ribs."""
    cyl(bm, uvl, cx, cy, 0.7, 0.7, 0.0, 0.45, 8, WA, phase=math.pi / 8)
    cyl(bm, uvl, cx, cy, 0.45, 0.45, 0.45, CAP - 0.6, 10, WA)
    cyl(bm, uvl, cx, cy, 0.5, 0.75, CAP - 0.6, CAP - 0.15, 10, WA)
    cyl(bm, uvl, cx, cy, 0.85, 0.85, CAP - 0.15, CAP, 8, RO, phase=math.pi / 8)

def vault_shaft(bm, uvl, cx, cy, z0=CAP):
    """Thin shaft running up the nave wall from the pier capital to the vault springing."""
    cyl(bm, uvl, cx, cy, 0.26, 0.26, z0, V_SPRING - 0.4, 8, WA)
    cyl(bm, uvl, cx, cy, 0.3, 0.46, V_SPRING - 0.4, V_SPRING - 0.1, 8, WA)
    cyl(bm, uvl, cx, cy, 0.52, 0.52, V_SPRING - 0.1, V_SPRING, 8, RO, phase=math.pi / 8)

def arch_frame(bm, place, s, spring, rise, band, d0, d1, mi, n=6):
    """Moulded band around an arch, standing out of the wall face between depths d0 and d1."""
    inn = [(u, spring + h) for u, h in arc_pts(s, rise, n, -0.06)]
    out = [(u, spring + h) for u, h in arc_pts(s, rise, n, band)]
    m = len(inn)
    F = [[bm.verts.new(place(u, z, d)) for u, z in pts] for pts, d in ((inn, d0), (out, d0), (out, d1), (inn, d1))]
    for i in range(m - 1):
        for k in range(4):
            a, b = F[k], F[(k + 1) % 4]
            bm.faces.new((a[i], a[i + 1], b[i + 1], b[i])).material_index = mi
    for i in (0, m - 1):
        bm.faces.new((F[0][i], F[1][i], F[2][i], F[3][i])).material_index = mi


# ---------- vaults ----------

class Surface:
    """Collects vault faces with shared vertices and per-face UVs."""
    def __init__(self, bm, uvl):
        self.bm, self.uvl, self.cache, self.faces = bm, uvl, {}, []

    def vert(self, p):
        key = (round(p.x, 3), round(p.y, 3), round(p.z, 3))
        v = self.cache.get(key)
        if v is None:
            v = self.cache[key] = self.bm.verts.new(p)
        return v

    def face(self, pts, uvs, mi):
        vs, us = [], []
        for p, t in zip(pts, uvs):
            v = self.vert(p)
            if v in vs:
                continue
            vs.append(v)
            us.append(t)
        if len(vs) < 3:
            return
        f = self.bm.faces.new(vs)
        f.material_index = mi
        for l, t in zip(f.loops, us):
            l[self.uvl].uv = t
        self.faces.append(f)

def cumlen(pts):
    out = [0.0]
    for i in range(1, len(pts)):
        out.append(out[-1] + math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]))
    return out

def groin_vault(srf, mapf, su, sv, rise, n=8, ext=0.12, mi=CE):
    """Rib vault over a bay of half-spans su x sv; mapf(u, v, h) -> world point. Webs reach ext into the side walls.
    Returns the four groin curves, corner -> crown, keyed by (sign u, sign v)."""
    T = HALL_TILE[mi]
    A = arc_half(su, rise, n)
    G = [(u, arc_inv(sv, rise, h), h) for u, h in A]
    G[-1] = (0.0, 0.0, rise)
    lenA = cumlen(A)
    lenB = cumlen([(g[1], g[2]) for g in G])
    for a in SIDES:
        for b in SIDES:
            for k in range(n):
                g0, g1 = G[k], G[k + 1]
                # side cell: lines of constant u from the groin to the wall at v = sv
                pts = [mapf(a * g0[0], b * g0[1], g0[2]), mapf(a * g0[0], b * (sv + ext), g0[2]),
                       mapf(a * g1[0], b * (sv + ext), g1[2]), mapf(a * g1[0], b * g1[1], g1[2])]
                uvs = [(b * g0[1] / T, lenA[k] / T), (b * (sv + ext) / T, lenA[k] / T),
                       (b * (sv + ext) / T, lenA[k + 1] / T), (b * g1[1] / T, lenA[k + 1] / T)]
                srf.face(pts, uvs, mi)
                # end cell: lines of constant v from the groin to the transverse arch at u = su
                pts = [mapf(a * g0[0], b * g0[1], g0[2]), mapf(a * su, b * g0[1], g0[2]),
                       mapf(a * su, b * g1[1], g1[2]), mapf(a * g1[0], b * g1[1], g1[2])]
                uvs = [(a * g0[0] / T, lenB[k] / T), (a * su / T, lenB[k] / T),
                       (a * su / T, lenB[k + 1] / T), (a * g1[0] / T, lenB[k + 1] / T)]
                srf.face(pts, uvs, mi)
    return {(a, b): [mapf(a * g[0], b * g[1], g[2]) for g in G] for a in SIDES for b in SIDES}, G

def diagonals(groins):
    d1 = groins[(1, 1)] + list(reversed(groins[(-1, -1)]))[1:]
    d2 = groins[(1, -1)] + list(reversed(groins[(-1, 1)]))[1:]
    return [d1, d2]

def straight_map(xc, yc, spring):
    return lambda u, v, h: P(xc + u, yc + v, spring + h)

def bent_map(ac, rm, spring):
    a0 = math.radians(ac)
    def f(u, v, h):
        a = a0 + u / rm
        r = rm + v
        return P(r * math.cos(a), r * math.sin(a), spring + h)
    return f

def half_dome(srf, G, a0, a1, seg, ext=0.12, mi=CE):
    """Half dome over the apse with the nave's cross-section as its meridian (G from the first nave bay)."""
    T = HALL_TILE[mi]
    prof = [(NAVE_IN + ext, 0.0)] + [(g[1], g[2]) for g in G]
    L = cumlen(prof)
    for i in range(seg):
        t0 = math.radians(a0 + (a1 - a0) * i / seg)
        t1 = math.radians(a0 + (a1 - a0) * (i + 1) / seg)
        for k in range(len(prof) - 1):
            (r0, h0), (r1, h1) = prof[k], prof[k + 1]
            pts = [P(r0 * math.cos(t0), r0 * math.sin(t0), V_SPRING + h0), P(r0 * math.cos(t1), r0 * math.sin(t1), V_SPRING + h0),
                   P(r1 * math.cos(t1), r1 * math.sin(t1), V_SPRING + h1), P(r1 * math.cos(t0), r1 * math.sin(t0), V_SPRING + h1)]
            u0, u1 = t0 * NAVE_IN / T, t1 * NAVE_IN / T
            srf.face(pts, [(u0, L[k] / T), (u1, L[k] / T), (u1, L[k + 1] / T), (u0, L[k + 1] / T)], mi)

def rib(bm, uvl, pts, inside, w, d, mi=RO, embed=0.08):
    """Sweep a w x d rectangle along pts, hanging from the vault toward the point `inside`."""
    T = HALL_TILE[mi]
    D = pts[-1] - pts[0]
    D.z = 0
    S = D.normalized().cross(Vector((0, 0, 1))).normalized()
    rings = []
    L = [0.0]
    for i, p in enumerate(pts):
        if i:
            L.append(L[-1] + (p - pts[i - 1]).length)
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        N = t.cross(S).normalized()
        if N.dot(inside - p) < 0:
            N = -N
        rings.append([bm.verts.new(p + S * sx + N * nz) for sx, nz in ((-w / 2, -embed), (w / 2, -embed), (w / 2, d), (-w / 2, d))])
    prof_v = [0.0, w, w + d + embed, 2 * w + d + embed, 2 * (w + d + embed)]
    for i in range(len(pts) - 1):
        for k in range(4):
            l = (k + 1) % 4
            f = bm.faces.new((rings[i][k], rings[i][l], rings[i + 1][l], rings[i + 1][k]))
            f.material_index = mi
            for lp, uv in zip(f.loops, ((L[i] / T, prof_v[k] / T), (L[i] / T, prof_v[k + 1] / T),
                                         (L[i + 1] / T, prof_v[k + 1] / T), (L[i + 1] / T, prof_v[k] / T))):
                lp[uvl].uv = uv
    for ring in (rings[0], rings[-1]):
        f = bm.faces.new(ring)
        f.material_index = mi
        for lp in f.loops:
            lp[uvl].uv = (lp.vert.co.x / T, lp.vert.co.z / T)


# ---------- hall builders ----------

END_NICHES = [(-3.6, 1.1, 4.6, 1.3), (0.0, 1.4, 5.2, 1.6), (3.6, 1.1, 4.6, 1.3)]

def sorted_y(side, d0, d1):
    return tuple(sorted((side * d0, side * d1)))

def build_shell(coll):
    """Walls and floor with every opening cut by booleans. Returns the solids."""
    out = []

    for side in SIDES:
        bm = bmesh.new()
        box_xyz(bm, 0.0, X_END + 0.7, *sorted_y(side, NAVE_IN, AISLE_IN), 0.0, TOP, WA)
        ob = new_obj("NaveWall%+d" % side, bm, coll)
        arc, blind, win = bmesh.new(), bmesh.new(), bmesh.new()
        for i in range(NBAYS):
            xc = BAY * i + BAY / 2
            pl = place_y(xc, side)
            prism(arc, arch_outline(BAY / 2 - 0.5, -0.5, CAP, ARC_RISE), pl, NAVE_IN - 0.1, AISLE_IN + 0.1, WA, WA, WA)
            for du in (-2.3, 0.0, 2.3):
                prism(blind, arch_outline(0.9, LEDGE + 0.35, 13.6, 1.2, 4), place_y(xc + du, side), NAVE_IN - 0.1, NAVE_IN + 0.7, RO, RO, RO)
            prism(win, arch_outline(1.5, 16.0, 19.6, 1.8), pl, NAVE_IN - 0.1, AISLE_IN + 0.1, WA, WA, WA)
        out.append(carve(ob, [("arcade", arc), ("blind", blind), ("win", win)]))

    # round wall of the apse around the lift
    bm = bmesh.new()
    lathe(bm, rect(NAVE_IN, AISLE_IN, 0.0, V_SPRING + 1.0), 90, 270, 30, WA)
    ob = new_obj("ApseWall", bm, coll)
    arc, blind = bmesh.new(), bmesh.new()
    k_arc = (AISLE_IN + 0.1) / (NAVE_IN - 0.1)
    k_blind = (NAVE_IN + 0.65) / (NAVE_IN - 0.1)
    for a in APSE_BAYS:
        o = arch_outline(1.65, -0.5, CAP, ARC_RISE)
        prism(arc, o, place_r(a), NAVE_IN - 0.1, AISLE_IN + 0.1, WA, WA, WA, outline1=scaled(o, k_arc))
        for da in (-6.45, 6.45):
            o = arch_outline(0.55, LEDGE + 0.35, 13.6, 1.0, 4)
            prism(blind, o, place_r(a + da), NAVE_IN - 0.1, NAVE_IN + 0.65, RO, RO, RO, outline1=scaled(o, k_blind))
    out.append(carve(ob, [("arcade", arc), ("blind", blind)]))

    # outer wall of the ambulatory: a window behind the lift, statue niches in the other bays
    bm = bmesh.new()
    lathe(bm, rect(AMB_R, AMB_R + AMB_T, 0.0, LOW_TOP), 90, 270, 30, WA)
    ob = new_obj("AmbulatoryWall", bm, coll)
    niche, win = bmesh.new(), bmesh.new()
    for a in APSE_BAYS:
        if a == 180:
            o = arch_outline(1.4, 2.0, 8.0, 1.6, 5)
            prism(win, o, place_r(a), AMB_R - 0.1, AMB_R + AMB_T + 0.1, WA, WA, WA, outline1=scaled(o, (AMB_R + AMB_T + 0.1) / (AMB_R - 0.1)))
        else:
            o = arch_outline(1.2, 0.9, 4.6, 1.4, 5)
            prism(niche, o, place_r(a), AMB_R - 0.1, AMB_R + 0.9, RO, RO, RO, outline1=scaled(o, (AMB_R + 0.9) / (AMB_R - 0.1)))
    out.append(carve(ob, [("niche", niche), ("win", win)]))

    # walls between the ambulatory and the wider aisles
    for side in SIDES:
        bm = bmesh.new()
        box_xyz(bm, -0.6, 0.0, *sorted_y(side, AISLE_IN, OUT_Y + OUT_T), 0.0, LOW_TOP, WA)
        ob = new_obj("Diaphragm%+d" % side, bm, coll)
        op = bmesh.new()
        prism(op, arch_outline(2.25, -0.5, CAP, A_RISE + 0.1), place_x(side * AMB_RM), -0.7, 0.1, WA, WA, WA)
        out.append(carve(ob, [("open", op)]))

    # aisle walls: windows, and dark passages for the enemies in the other bays
    door_o = arch_outline(1.95, -0.5, 3.75, 2.0, 5)
    for side in SIDES:
        bm = bmesh.new()
        box_xyz(bm, -0.6, X_END + END_T, *sorted_y(side, OUT_Y, OUT_Y + OUT_T), 0.0, LOW_TOP, WA)
        ob = new_obj("AisleWall%+d" % side, bm, coll)
        win, door = bmesh.new(), bmesh.new()
        for i in range(NBAYS):
            pl = place_y(BAY * i + BAY / 2, side)
            if i in WINDOW_BAYS:
                prism(win, arch_outline(1.6, 1.8, 7.6, 2.0, 5), pl, OUT_Y - 0.1, OUT_Y + OUT_T + 0.1, WA, WA, WA)
            else:
                prism(door, door_o, pl, OUT_Y - 0.1, OUT_Y + OUT_T + 0.1, RO, RO, RO)
                prism(win, arch_outline(1.0, 6.6, 8.4, 1.2, 4), pl, OUT_Y - 0.1, OUT_Y + OUT_T + 0.1, WA, WA, WA)
        out.append(carve(ob, [("win", win), ("door", door)]))
        for i in PASSAGE_BAYS:
            xc = BAY * i + BAY / 2
            bm = bmesh.new()
            box_xyz(bm, xc - 3.0, xc + 3.0, *sorted_y(side, OUT_Y + OUT_T, OUT_Y + OUT_T + 5.0), 0.0, 7.0, RO)
            ob = new_obj("Passage%d%+d" % (i, side), bm, coll)
            cut = bmesh.new()
            prism(cut, door_o, place_y(xc, side), OUT_Y + OUT_T - 0.1, OUT_Y + OUT_T + 4.5, RO, RO, VO)
            out.append(carve(ob, [("cut", cut)]))

    # far wall: big window over three statue niches
    bm = bmesh.new()
    box_xyz(bm, X_END, X_END + END_T, -(OUT_Y + OUT_T), OUT_Y + OUT_T, 0.0, TOP, WA)
    ob = new_obj("EndWall", bm, coll)
    win, niche = bmesh.new(), bmesh.new()
    prism(win, arch_outline(3.5, 12.0, 18.0, 4.0, 8), place_x(0.0), X_END - 0.1, X_END + END_T + 0.1, WA, WA, WA)
    for yc in (-AISLE_YC, AISLE_YC):
        prism(win, arch_outline(1.5, 1.8, 7.6, 1.9, 5), place_x(yc), X_END - 0.1, X_END + END_T + 0.1, WA, WA, WA)
    for yc, s, spring, rise in END_NICHES:
        prism(niche, arch_outline(s, 1.0, spring, rise, 5), place_x(yc), X_END - 0.1, X_END + 1.0, RO, RO, RO)
    out.append(carve(ob, [("win", win), ("niche", niche)]))

    # floor with the shaft hole
    bm = bmesh.new()
    box_xyz(bm, -AMB_R - AMB_T - 0.5, X_END + END_T + 0.5, -22.0, 22.0, -SLAB / 2, 0.0, FL)
    ob = new_obj("HallFloor", bm, coll)
    sh = bmesh.new()
    cylinder(sh, SHAFT_R, HZ - 1.0, HZ + 0.1, 32, RO, RO, RO)
    out.append(carve(ob, [("shaft", sh)]))

    for ob in out:
        cleanup(ob)
        hall_uvs(ob)
    return out


def build_trims(coll):
    bm = bmesh.new()
    def post_ok(x):
        return all(abs(x - BAY * i) > 0.45 for i in range(NBAYS + 1))
    for side in SIDES:
        for i in range(NBAYS):
            pl = place_y(BAY * i + BAY / 2, side)
            arch_frame(bm, pl, BAY / 2 - 0.5, CAP, ARC_RISE, 0.35, NAVE_IN - 0.22, NAVE_IN + 0.05, RO)
            arch_frame(bm, pl, 1.5, 19.6, 1.8, 0.25, NAVE_IN - 0.18, NAVE_IN + 0.05, RO, n=5)
            box_place(bm, pl, -0.1, 0.1, 16.0, 21.45, NAVE_IN + 0.45, NAVE_IN + 0.85, RO)
            if i in WINDOW_BAYS:
                arch_frame(bm, pl, 1.6, 7.6, 2.0, 0.3, OUT_Y - 0.2, OUT_Y + 0.05, RO, n=5)
                box_place(bm, pl, -0.1, 0.1, 1.8, 9.65, OUT_Y + 0.5, OUT_Y + 0.9, RO)
            else:
                arch_frame(bm, pl, 1.95, 3.75, 2.0, 0.45, OUT_Y - 0.25, OUT_Y + 0.05, RO, n=5)
        # gallery ledge with corbels and balustrade, string course under the windows
        box_xyz(bm, 0.0, X_END, *sorted_y(side, 5.9, NAVE_IN + 0.05), LEDGE, LEDGE + 0.35, RO)
        box_xyz(bm, 0.0, X_END, *sorted_y(side, 5.95, 6.27), LEDGE + 1.2, LEDGE + 1.34, RO)
        box_xyz(bm, 0.0, X_END, *sorted_y(side, 6.18, NAVE_IN + 0.05), STRING_Z, STRING_Z + 0.22, RO)
        x = 0.3
        while x < X_END - 0.2:
            if post_ok(x):
                box_xyz(bm, x - 0.07, x + 0.07, *sorted_y(side, 6.03, 6.17), LEDGE + 0.35, LEDGE + 1.2, RO)
            x += 0.45
        for k in range(NBAYS * 8):
            x = 0.5 + k
            if post_ok(x):
                box_xyz(bm, x - 0.12, x + 0.12, *sorted_y(side, 6.05, NAVE_IN + 0.02), LEDGE - 0.35, LEDGE, RO)
        # corbels and canopies for the statues on the piers
        for i in STATUE_PIERS:
            pl = place_y(BAY * i, side)
            box_place(bm, pl, -0.45, 0.45, 2.85, 3.1, 5.05, 6.2, RO)
            box_place(bm, pl, -0.32, 0.32, 2.55, 2.85, 5.3, 6.2, RO)
            box_place(bm, pl, -0.18, 0.18, 2.25, 2.55, 5.55, 6.2, RO)
            box_place(bm, pl, -0.55, 0.55, 5.35, 5.55, 4.95, 6.2, RO)
            prism(bm, [(-0.55, 5.55), (0.55, 5.55), (0.0, 6.15)], pl, 5.0, 6.2, RO, RO, RO)

    # apse: arch mouldings, ledge, balustrade and string course follow the curve
    for a in APSE_BAYS:
        arch_frame(bm, place_r(a), 1.65 * NAVE_IN / (NAVE_IN - 0.1), CAP, ARC_RISE, 0.3, NAVE_IN - 0.2, NAVE_IN + 0.05, RO)
    lathe(bm, rect(5.9, NAVE_IN + 0.05, LEDGE, LEDGE + 0.35), 90, 270, 30, RO)
    lathe(bm, rect(5.95, 6.27, LEDGE + 1.2, LEDGE + 1.34), 90, 270, 30, RO)
    lathe(bm, rect(6.18, NAVE_IN + 0.05, STRING_Z, STRING_Z + 0.22), 90, 270, 30, RO)
    steps = 42
    for k in range(1, steps):
        a = 90 + 180 * k / steps
        if any(abs(a - b) < 4.0 for b in APSE_ANGLES):
            continue
        c = Vector((math.cos(math.radians(a)), math.sin(math.radians(a))))
        box(bm, c.x * 6.1, c.y * 6.1, HZ + LEDGE + 0.35, HZ + LEDGE + 1.2, 0.14, 0.14, a, RO)
        if k % 2 == 0:
            box(bm, c.x * 6.24, c.y * 6.24, HZ + LEDGE - 0.35, HZ + LEDGE, 0.37, 0.24, a, RO)

    # ambulatory window and niche mouldings
    for a in APSE_BAYS:
        pl = place_r(a)
        if a == 180:
            arch_frame(bm, pl, 1.4, 8.0, 1.6, 0.3, AMB_R - 0.2, AMB_R + 0.05, RO, n=5)
            box_place(bm, pl, -0.1, 0.1, 2.0, 9.65, AMB_R + 0.5, AMB_R + 0.9, RO)
        else:
            arch_frame(bm, pl, 1.2 * AMB_R / (AMB_R - 0.1), 4.6, 1.4, 0.25, AMB_R - 0.2, AMB_R + 0.05, RO, n=5)

    # far wall: window tracery, niche mouldings, aisle windows
    pl = place_x(0.0)
    for u in (-1.75, 0.0, 1.75):
        box_place(bm, pl, u - 0.1, u + 0.1, 12.0, 18.0 + arc_h(3.5, 4.0, u) + 0.05, X_END + 0.55, X_END + 0.95, RO)
    box_place(bm, pl, -3.5, 3.5, 15.0, 15.2, X_END + 0.55, X_END + 0.95, RO)
    arch_frame(bm, pl, 3.5, 18.0, 4.0, 0.4, X_END - 0.25, X_END + 0.05, RO, n=8)
    for yc, s, spring, rise in END_NICHES:
        arch_frame(bm, place_x(yc), s, spring, rise, 0.25, X_END - 0.2, X_END + 0.05, RO, n=5)
    for yc in (-AISLE_YC, AISLE_YC):
        arch_frame(bm, place_x(yc), 1.5, 7.6, 1.9, 0.3, X_END - 0.2, X_END + 0.05, RO, n=5)
        box_place(bm, place_x(yc), -0.1, 0.1, 1.8, 9.55, X_END + 0.55, X_END + 0.95, RO)

    # dais and altar in front of the far wall
    box_xyz(bm, X_END - 7.0, X_END + 0.2, -6.0, 6.0, 0.0, 0.25, RO)
    box_xyz(bm, X_END - 6.2, X_END + 0.2, -5.4, 5.4, 0.25, 0.5, RO)
    box_xyz(bm, X_END - 5.4, X_END + 0.2, -4.8, 4.8, 0.5, 0.75, RO)
    box_xyz(bm, X_END - 3.0, X_END - 1.4, -1.6, 1.6, 0.75, 1.85, WA)
    box_xyz(bm, X_END - 3.1, X_END - 1.3, -1.75, 1.75, 1.85, 2.0, RO)

    ob = new_obj("Trims", bm, coll)
    hall_uvs(ob)
    return ob


def build_piers(coll):
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.verify()
    for side in SIDES:
        for i in range(NBAYS + 1):
            pier(bm, uvl, BAY * i, side * PIER_Y, 0)
            vault_shaft(bm, uvl, BAY * i, side * NAVE_IN)
        for i in range(1, NBAYS):
            respond(bm, uvl, BAY * i, side * OUT_Y)
    for a in APSE_ANGLES[1:-1]:
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        pier(bm, uvl, c * PIER_Y, s * PIER_Y, a)
        vault_shaft(bm, uvl, c * NAVE_IN, s * NAVE_IN)
        respond(bm, uvl, c * AMB_R, s * AMB_R)
    return new_obj("Piers", bm, coll, recalc=False)


def build_vaults(coll):
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.verify()
    srf = Surface(bm, uvl)
    rb = bmesh.new()
    ruv = rb.loops.layers.uv.verify()

    def bay(mapf, su, sv, rise, inside, w, d):
        start = len(srf.faces)
        groins, G = groin_vault(srf, mapf, su, sv, rise)
        face_inward(bm, srf.faces[start:], inside)
        for dg in diagonals(groins):
            rib(rb, ruv, dg, inside, w, d)
        return G

    def across(G, at):
        """Cross-section of a vault from -v to +v; at(v, h) -> world."""
        return [at(-g[1], g[2]) for g in G] + [at(g[1], g[2]) for g in reversed(G[:-1])]

    # nave
    for i in range(NBAYS):
        xc = BAY * i + BAY / 2
        mapf = straight_map(xc, 0.0, V_SPRING)
        G = bay(mapf, BAY / 2, NAVE_IN, V_RISE, P(xc, 0, V_SPRING), 0.42, 0.45)
        for side in SIDES:
            pts = [mapf(-g[0], side * NAVE_IN, g[2]) for g in G] + [mapf(g[0], side * NAVE_IN, g[2]) for g in reversed(G[:-1])]
            rib(rb, ruv, pts, P(xc, side * NAVE_IN, V_SPRING), 0.26, 0.22)
        cyl(rb, ruv, xc, 0.0, 0.42, 0.55, V_SPRING + V_RISE - 0.65, V_SPRING + V_RISE + 0.05, 8, RO)
    nave_G = G
    for i in range(NBAYS + 1):
        x = BAY * i
        rib(rb, ruv, across(nave_G, lambda v, h: P(x, v, V_SPRING + h)), P(x, 0, V_SPRING), 0.42, 0.45)

    # apse half dome with ribs running to the keystone over the lift
    start = len(srf.faces)
    half_dome(srf, nave_G, 90, 270, 15)
    face_inward(bm, srf.faces[start:], P(0, 0, V_SPRING - 1.0))
    for a in APSE_ANGLES[1:-1]:
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        pts = [P(g[1] * c, g[1] * s, V_SPRING + g[2]) for g in nave_G]
        rib(rb, ruv, pts, P(0, 0, V_SPRING), 0.4, 0.42)
    cyl(rb, ruv, 0.0, 0.0, 0.5, 0.65, V_SPRING + V_RISE - 0.75, V_SPRING + V_RISE + 0.05, 8, RO)

    # aisles
    sv = (OUT_Y - AISLE_IN) / 2
    for side in SIDES:
        yc = side * AISLE_YC
        for i in range(NBAYS):
            xc = BAY * i + BAY / 2
            G = bay(straight_map(xc, yc, CAP), BAY / 2, sv, A_RISE, P(xc, yc, CAP), 0.32, 0.35)
        for i in range(NBAYS + 1):
            x = BAY * i
            rib(rb, ruv, across(G, lambda v, h: P(x, yc + v, CAP + h)), P(x, yc, CAP), 0.32, 0.35)

    # ambulatory, bent around the apse
    sv = (AMB_R - AISLE_IN) / 2
    su = AMB_RM * math.radians(18)
    for ac in APSE_BAYS:
        m = bent_map(ac, AMB_RM, CAP)
        G = bay(m, su, sv, A_RISE, m(0, 0, 0), 0.32, 0.35)
    for a in APSE_ANGLES:
        m = bent_map(a, AMB_RM, CAP)
        rib(rb, ruv, across(G, lambda v, h: m(0, v, h)), m(0, 0, 0), 0.32, 0.35)

    return [new_obj("Vaults", bm, coll, recalc=False), new_obj("Ribs", rb, coll)]


def build_glass(coll):
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.verify()
    def pane(outline, place, d, inside):
        f = bm.faces.new([bm.verts.new(place(u, z, d)) for u, z in outline])
        f.material_index = GL
        for l, (u, z) in zip(f.loops, outline):
            l[uvl].uv = (u, z)
        face_inward(bm, [f], inside)
    for side in SIDES:
        for i in range(NBAYS):
            xc = BAY * i + BAY / 2
            pane(arch_outline(1.5, 16.0, 19.6, 1.8), place_y(xc, side), AISLE_IN - 0.08, P(xc, 0, 18))
            if i in WINDOW_BAYS:
                pane(arch_outline(1.6, 1.8, 7.6, 2.0, 5), place_y(xc, side), OUT_Y + OUT_T - 0.08, P(xc, side * AISLE_YC, 5))
            else:
                pane(arch_outline(1.0, 6.6, 8.4, 1.2, 4), place_y(xc, side), OUT_Y + OUT_T - 0.08, P(xc, side * AISLE_YC, 5))
    pane(arch_outline(3.5, 12.0, 18.0, 4.0, 8), place_x(0.0), X_END + END_T - 0.08, P(30, 0, 15))
    for yc in (-AISLE_YC, AISLE_YC):
        pane(arch_outline(1.5, 1.8, 7.6, 1.9, 5), place_x(yc), X_END + END_T - 0.08, P(30, yc, 5))
    o = arch_outline(1.4, 2.0, 8.0, 1.6, 5)
    pane(scaled(o, (AMB_R + AMB_T - 0.08) / (AMB_R - 0.1)), place_r(180), AMB_R + AMB_T - 0.08, P(0, 0, 5))
    return new_obj("Glass", bm, coll, recalc=False)


# Direction the moonlight travels. The directional light in Unity uses the same vector: (-x, z, -y).
MOON = Vector((-0.45, -0.6, -0.66)).normalized()

def build_beams(coll):
    """Fake light shafts: open boxes pushed from the windows along MOON, faded by the texture along v."""
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.verify()
    def beam(corners, length):
        near = [bm.verts.new(c) for c in corners]
        far = [bm.verts.new(c + MOON * length) for c in corners]
        for i in range(4):
            j = (i + 1) % 4
            f = bm.faces.new((near[i], near[j], far[j], far[i]))
            for l, uv in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
                l[uvl].uv = uv
    for i in range(NBAYS):
        xc = BAY * i + BAY / 2
        beam([P(xc - 1.3, NAVE_IN, 16.2), P(xc + 1.3, NAVE_IN, 16.2), P(xc + 1.3, NAVE_IN, 20.6), P(xc - 1.3, NAVE_IN, 20.6)], 24.0)
        if i in WINDOW_BAYS:
            beam([P(xc - 1.4, OUT_Y, 2.2), P(xc + 1.4, OUT_Y, 2.2), P(xc + 1.4, OUT_Y, 8.8), P(xc - 1.4, OUT_Y, 8.8)], 12.0)
    beam([P(X_END, -3.2, 12.5), P(X_END, 3.2, 12.5), P(X_END, 3.2, 20.5), P(X_END, -3.2, 20.5)], 26.0)
    me = bpy.data.meshes.new("Hall_Beams")
    bm.to_mesh(me)
    bm.free()
    me.materials.append(bpy.data.materials["M_Tower_Beam"])
    ob = bpy.data.objects.new("Hall_Beams", me)
    coll.objects.link(ob)
    return ob
