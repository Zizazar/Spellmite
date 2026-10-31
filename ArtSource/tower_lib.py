import bpy, bmesh, math
from mathutils import Vector

R_IN = 14.0
R_OUT = 19.5
FLOOR_H = 6.0
ROOM_H = 5.5
TOP_ROOM_H = 7.5
SLAB = 0.5
SHAFT_R = 3.0
PIT = 0.3
FLOORS = 5
WALL_SEG = 48
PASSAGE_ANGLES = [45, 135, 225, 315]
PASSAGE_W = 2.6
PASSAGE_SPRING = 2.5
PASSAGE_DEPTH_END = 18.0
SLIT_ANGLES = [22, 112, 202, 292]
PILLAR_ANGLES = [0, 90, 180, 270]
PILLAR_R = 4.8
PILLAR_W = 0.9
GUIDE_ANGLES = [45, 135, 225, 315]
GUIDE_R = 3.25

def mat(name):
    return bpy.data.materials[name]

def floor_z(k):
    return k * FLOOR_H

def ceil_z(k):
    return floor_z(k) + (TOP_ROOM_H if k == FLOORS - 1 else ROOM_H)

def chunk_bottom(k):
    return -0.6 if k == 0 else floor_z(k) - SLAB / 2

def chunk_top(k):
    return ceil_z(k) + (0.8 if k == FLOORS - 1 else SLAB / 2)

def link(ob, coll=None):
    (coll or bpy.context.scene.collection).objects.link(ob)
    return ob

def obj_from_bm(name, bm, mats, coll=None):
    me = bpy.data.meshes.new(name)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.normal_update()
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(mat(m))
    return link(bpy.data.objects.new(name, me), coll)

def cylinder(bm, r, z0, z1, seg, side_mi, top_mi, bot_mi, cx=0.0, cy=0.0, phase=0.0):
    bot = [bm.verts.new((cx + r * math.cos(2 * math.pi * i / seg + phase), cy + r * math.sin(2 * math.pi * i / seg + phase), z0)) for i in range(seg)]
    top = [bm.verts.new((v.co.x, v.co.y, z1)) for v in bot]
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((bot[i], bot[j], top[j], top[i])).material_index = side_mi
    bm.faces.new(top).material_index = top_mi
    bm.faces.new(list(reversed(bot))).material_index = bot_mi

def frame(ang_deg):
    a = math.radians(ang_deg)
    u = Vector((math.cos(a), math.sin(a), 0))
    t = Vector((-math.sin(a), math.cos(a), 0))
    return u, t

def radial_prism(bm, profile, edge_mis, r0, r1, ang_deg, zb, cap0_mi, cap1_mi, profile1=None):
    """profile: CCW list of (x, z) seen from inside the room looking out; edge_mis[i] = material of edge i->i+1.
    profile1: optional profile at r1 (for tapered shapes)."""
    u, t = frame(ang_deg)
    p1 = profile1 or profile
    ring0 = [bm.verts.new(u * r0 + t * x + Vector((0, 0, zb + z))) for x, z in profile]
    ring1 = [bm.verts.new(u * r1 + t * x + Vector((0, 0, zb + z))) for x, z in p1]
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((ring0[i], ring1[i], ring1[j], ring0[j])).material_index = edge_mis[i]
    bm.faces.new(list(reversed(ring0))).material_index = cap0_mi
    bm.faces.new(ring1).material_index = cap1_mi

def arch_profile(w, spring):
    h = w / 2
    # pointed arch, 3 segments a side
    right = [(h, spring), (h * 0.82, spring + 0.6), (h * 0.48, spring + 1.0), (0.0, spring + 1.2)]
    left = [(-x, z) for x, z in reversed(right[:-1])]
    pts = [(-h, 0.0), (h, 0.0)] + right + left
    # edges: bottom, right side, arch..., left side
    mis = []
    for i in range(len(pts)):
        if i == 0:
            mis.append(2)          # floor
        elif i in (1, len(pts) - 1):
            mis.append(0)          # straight sides
        else:
            mis.append(1)          # arch
    return pts, mis

def box(bm, cx, cy, z0, z1, sx, sy, ang_deg, mi):
    u, t = frame(ang_deg)
    c = Vector((cx, cy, 0))
    corners = [c + u * (sx / 2) * a + t * (sy / 2) * b for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    bot = [bm.verts.new((p.x, p.y, z0)) for p in corners]
    top = [bm.verts.new((p.x, p.y, z1)) for p in corners]
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((bot[i], bot[j], top[j], top[i])).material_index = mi
    bm.faces.new(top).material_index = mi
    bm.faces.new(list(reversed(bot))).material_index = mi

def bake_mesh(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)
    return ob


def get_coll(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c

def build_floor_shell(k):
    """Solid ring chunk for floor k with the room, shaft, passages and slits cut out. Returns the baked object."""
    name = "Floor%d" % (k + 1)
    out = get_coll("Tower")
    cut = get_coll("Cutters")
    zb, zt = chunk_bottom(k), chunk_top(k)
    fz, cz = floor_z(k), ceil_z(k)

    bm = bmesh.new()
    cylinder(bm, R_OUT, zb, zt, WALL_SEG, 0, 0, 0)
    solid = obj_from_bm(name, bm, ["M_Tower_Wall"], out)

    cutters = []
    bm = bmesh.new()
    cylinder(bm, R_IN, fz, cz, WALL_SEG, 0, 1, 2, phase=math.pi / WALL_SEG)
    cutters.append(obj_from_bm(name + "_cut_room", bm, ["M_Tower_Wall", "M_Tower_Ceiling", "M_Tower_Floor"], cut))

    s0 = -PIT if k == 0 else zb - 0.1
    s1 = zt + 0.1 if k < FLOORS - 1 else fz + 0.1
    bm = bmesh.new()
    cylinder(bm, SHAFT_R, s0, s1, 32, 0, 0, 1)
    cutters.append(obj_from_bm(name + "_cut_shaft", bm, ["M_Tower_Wall", "M_Tower_Rough"], cut))

    prof, mis = arch_profile(PASSAGE_W, PASSAGE_SPRING)
    bm = bmesh.new()
    for a in PASSAGE_ANGLES:
        radial_prism(bm, prof, mis, R_IN - 1.0, PASSAGE_DEPTH_END, a, fz, 0, 3)
    cutters.append(obj_from_bm(name + "_cut_passages", bm, ["M_Tower_Rough", "M_Tower_Rough", "M_Tower_Floor", "M_Tower_Void"], cut))

    def rect(w, z0, z1):
        return [(-w / 2, z0), (w / 2, z0), (w / 2, z1), (-w / 2, z1)]
    bm = bmesh.new()
    for a in SLIT_ANGLES:
        radial_prism(bm, rect(1.3, 1.9, 4.1), [0, 0, 0, 0], R_IN - 0.4, R_OUT + 0.5, a, fz, 0, 0,
                     profile1=rect(0.22, 2.35, 3.65))
    cutters.append(obj_from_bm(name + "_cut_slits", bm, ["M_Tower_Wall"], cut))

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
    return solid


TILE = {"M_Tower_Wall": 1.5, "M_Tower_WallBase": 1.5, "M_Tower_Floor": 2.0, "M_Tower_Ceiling": 2.0,
        "M_Tower_Rough": 1.5, "M_Tower_Void": 1.0, "M_Tower_Wood": 1.0, "M_Tower_Iron": 1.0}
NOMINAL_R = [R_IN, R_OUT, SHAFT_R]
BASE_H = 0.75

def ensure_mat(me, name):
    names = [m.name for m in me.materials]
    if name not in names:
        me.materials.append(mat(name))
        names.append(name)
    return names.index(name)

def add_details(ob, k):
    """Pillars around the shaft, ceiling beams and the iron ring at the shaft edge, joined into the floor mesh."""
    me = ob.data
    wall = ensure_mat(me, "M_Tower_Wall")
    wood = ensure_mat(me, "M_Tower_Wood")
    iron = ensure_mat(me, "M_Tower_Iron")
    fz, cz = floor_z(k), ceil_z(k)
    bm = bmesh.new()
    bm.from_mesh(me)
    for a in PILLAR_ANGLES:
        u, _ = frame(a)
        c = u * PILLAR_R
        box(bm, c.x, c.y, fz, fz + 0.5, PILLAR_W + 0.3, PILLAR_W + 0.3, a, wall)
        box(bm, c.x, c.y, fz + 0.5, cz - 0.45, PILLAR_W, PILLAR_W, a, wall)
        box(bm, c.x, c.y, cz - 0.45, cz + 0.05, PILLAR_W + 0.3, PILLAR_W + 0.3, a, wall)
    for i in range(8):
        a = i * 45
        r0 = PILLAR_R if a % 90 == 0 else SHAFT_R + 0.6
        r1 = R_IN + 0.4
        u, _ = frame(a)
        c = u * ((r0 + r1) / 2)
        box(bm, c.x, c.y, cz - 0.5, cz + 0.05, r1 - r0, 0.4, a, wood)
    # iron ring around the shaft edge, 2 cm above the floor
    seg = 32
    inner = [bm.verts.new((SHAFT_R * math.cos(2 * math.pi * i / seg), SHAFT_R * math.sin(2 * math.pi * i / seg), fz + 0.02)) for i in range(seg)]
    outer = [bm.verts.new(((SHAFT_R + 0.5) * math.cos(2 * math.pi * i / seg), (SHAFT_R + 0.5) * math.sin(2 * math.pi * i / seg), fz + 0.02)) for i in range(seg)]
    outer_lo = [bm.verts.new((v.co.x, v.co.y, fz)) for v in outer]
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((inner[i], outer[i], outer[j], inner[j])).material_index = iron
        bm.faces.new((outer_lo[i], outer_lo[j], outer[j], outer[i])).material_index = iron
    bm.normal_update()
    for f in bm.faces:
        if f.material_index == iron and abs(f.normal.z) > 0.5 and f.normal.z < 0:
            f.normal_flip()
    bm.to_mesh(me)
    bm.free()

def split_base_band(ob, k):
    """Damp base texture on the lowest 0.75 m of the room wall."""
    me = ob.data
    wall = ensure_mat(me, "M_Tower_Wall")
    base = ensure_mat(me, "M_Tower_WallBase")
    fz = floor_z(k)
    bm = bmesh.new()
    bm.from_mesh(me)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, dist=0.001, plane_co=(0, 0, fz + BASE_H), plane_no=(0, 0, 1))
    for f in bm.faces:
        if f.material_index != wall or abs(f.normal.z) > 0.3:
            continue
        c = f.calc_center_median()
        rc = math.hypot(c.x, c.y)
        if fz - 0.01 < c.z < fz + BASE_H and rc > R_IN - 0.2 and rc < R_IN + 0.3:
            f.material_index = base
    bm.to_mesh(me)
    bm.free()

def world_uvs(ob, z_ref):
    me = ob.data
    names = [m.name for m in me.materials]
    bm = bmesh.new()
    bm.from_mesh(me)
    uvl = bm.loops.layers.uv.verify()
    bm.normal_update()
    for f in bm.faces:
        T = TILE.get(names[f.material_index], 1.0)
        n = f.normal
        c = f.calc_center_median()
        if abs(n.z) > 0.7:
            for l in f.loops:
                l[uvl].uv = (l.vert.co.x / T, l.vert.co.y / T)
            continue
        rc = math.hypot(c.x, c.y)
        nh = Vector((n.x, n.y)).normalized()
        radial = Vector((c.x, c.y)).normalized() if rc > 1e-4 else Vector((1, 0))
        if rc > 0.5 and abs(nh.dot(radial)) > 0.92:
            R = min(NOMINAL_R, key=lambda r: abs(r - rc))
            if abs(R - rc) > 0.3:
                R = rc
            Te = 2 * math.pi * R / max(1, round(2 * math.pi * R / T))
            ac = math.atan2(c.y, c.x)
            for l in f.loops:
                a = math.atan2(l.vert.co.y, l.vert.co.x)
                da = (a - ac + math.pi) % (2 * math.pi) - math.pi
                l[uvl].uv = ((ac + da) * R / Te, (l.vert.co.z - z_ref) / T)
        else:
            t = Vector((-nh.y, nh.x))
            for l in f.loops:
                p = l.vert.co
                l[uvl].uv = ((p.x * t.x + p.y * t.y) / T, (p.z - z_ref) / T)
    bm.to_mesh(me)
    bm.free()

def cleanup(ob):
    me = ob.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=0.0005)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges[:], dist=0.0005)
    bm.to_mesh(me)
    bm.free()

def build_floor(k):
    ob = build_floor_shell(k)
    cleanup(ob)
    add_details(ob, k)
    split_base_band(ob, k)
    world_uvs(ob, floor_z(k))
    return ob


TORCH_ANGLES = [0, 90, 180, 270]

def add_wall_trim(ob, k):
    """Pilasters for the torches and closed stone frames around the passage arches."""
    me = ob.data
    wall = ensure_mat(me, "M_Tower_Wall")
    rough = ensure_mat(me, "M_Tower_Rough")
    fz, cz = floor_z(k), ceil_z(k)
    tb = bmesh.new()
    for a in TORCH_ANGLES:
        u, _ = frame(a)
        c = u * (R_IN - 0.05)
        box(tb, c.x, c.y, fz, cz + 0.05, 0.7, 1.1, a, wall)
    inner, _ = arch_profile(PASSAGE_W + 0.02, PASSAGE_SPRING)
    outer, _ = arch_profile(PASSAGE_W + 0.8, PASSAGE_SPRING)
    outer = [(x, z if z == 0 else z + 0.15 + (0.25 if x == 0 else 0)) for x, z in outer]
    order = list(range(1, len(inner))) + [0]
    r_front, r_back = R_IN - 0.3, R_IN
    for a in PASSAGE_ANGLES:
        u, t = frame(a)
        def P(pt, r):
            return tb.verts.new(u * r + t * pt[0] + Vector((0, 0, fz + pt[1])))
        fi = [P(inner[i], r_front) for i in order]
        fo = [P(outer[i], r_front) for i in order]
        bo = [P(outer[i], r_back) for i in order]
        bi = [P(inner[i], r_back) for i in order]
        n = len(order)
        for i in range(n - 1):
            tb.faces.new((fi[i], fi[i + 1], fo[i + 1], fo[i])).material_index = rough
            tb.faces.new((fo[i], fo[i + 1], bo[i + 1], bo[i])).material_index = rough
            tb.faces.new((bo[i], bo[i + 1], bi[i + 1], bi[i])).material_index = rough
            tb.faces.new((bi[i], bi[i + 1], fi[i + 1], fi[i])).material_index = rough
        for i in (0, n - 1):
            tb.faces.new((fi[i], fo[i], bo[i], bi[i])).material_index = rough
    bmesh.ops.recalc_face_normals(tb, faces=tb.faces[:])
    tmp = bpy.data.meshes.new("tmp_trim")
    tb.to_mesh(tmp)
    tb.free()
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.from_mesh(tmp)
    bm.to_mesh(me)
    bm.free()
    bpy.data.meshes.remove(tmp)

def build_floor(k):
    ob = build_floor_shell(k)
    cleanup(ob)
    add_details(ob, k)
    add_wall_trim(ob, k)
    split_base_band(ob, k)
    world_uvs(ob, floor_z(k))
    return ob
