# Rebuilds the whole tower in Tower.blend: exec(open(r"...\ArtSource\build_tower.py").read())
import os
import numpy as np

ART = os.path.dirname(bpy.data.filepath)
PROJECT = os.path.dirname(ART)
TEX_DIR = os.path.join(PROJECT, "Assets", "_Project", "Art", "Tower", "Textures")
exec(open(os.path.join(ART, "tower_lib.py")).read())
exec(open(os.path.join(ART, "hall_lib.py")).read())


def clear_tower():
    keep = {"Elevator_Platform"}
    for c in ("Tower", "Cutters", "HallParts", "Beams"):
        coll = bpy.data.collections.get(c)
        if coll is None:
            continue
        for ob in list(coll.objects):
            if ob.name in keep:
                continue
            me = ob.data
            bpy.data.objects.remove(ob, do_unlink=True)
            if me and me.users == 0:
                bpy.data.meshes.remove(me)


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


def make_glass_texture():
    """Leaded diamond panes in cold, slightly uneven colours; 64 px per metre."""
    n = 64
    y, x = np.mgrid[0:n, 0:n]
    a, b = (x + y) % 16, (x - y) % 16
    i, j = ((x + y) // 16) % 4, ((x - y) // 16) % 4
    rng = np.random.default_rng(7)
    tints = rng.uniform(0.75, 1.0, (4, 4, 3)) * np.array([0.55, 0.68, 0.85])
    tints[rng.random((4, 4)) < 0.2] *= np.array([1.0, 0.8, 0.55]) * 1.25
    col = tints[i, j] * rng.uniform(0.9, 1.05, (n, n, 1))
    lead = (a < 2) | (b < 2)
    col[lead] = 0.06
    rgba = np.concatenate([col, np.ones((n, n, 1))], axis=2)
    return save_png("glass_leaded", rgba, (n, n))


def ensure_glass_material():
    m = bpy.data.materials.get("M_Tower_Glass") or bpy.data.materials.new("M_Tower_Glass")
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = make_glass_texture()
    tex.interpolation = "Closest"
    nt.links.new(tex.outputs[0], em.inputs[0])
    nt.links.new(em.outputs[0], out.inputs[0])
    return m


def emission_material(name, image=None, color=(1, 1, 1, 1)):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = color
    if image is not None:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = image
        tex.interpolation = "Closest"
        nt.links.new(tex.outputs[0], em.inputs[0])
    nt.links.new(em.outputs[0], out.inputs[0])
    return m


def make_beam_texture():
    """Light shaft: bright at the window (v = 0) fading out along v, soft across u."""
    w, h = 16, 64
    v, u = np.mgrid[0:h, 0:w]
    fade = (1.0 - v / (h - 1)) ** 1.6
    edge = np.sin(np.pi * (u + 0.5) / w) ** 1.5
    a = (fade * edge)[..., None]
    rgba = np.concatenate([np.ones((h, w, 3)) * np.array([0.75, 0.85, 1.0]) * a, a], axis=2)
    return save_png("beam_soft", rgba, (w, h))


def split_planes(src, planes, name, coll):
    """Copy of src cut by planes [(co, no)], keeping the side the normal points to."""
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


def build_round_floors(tower):
    made = []
    for k in range(FLOORS - 1):
        src = build_floor(k)
        for s, c in enumerate(SECTOR_ANGLES):
            a0, a1 = math.radians(c - 22.5), math.radians(c + 22.5)
            planes = [((0, 0, 0), (-math.sin(a0), math.cos(a0), 0)), ((0, 0, 0), (math.sin(a1), -math.cos(a1), 0))]
            ob = split_planes(src, planes, "Floor%d_S%d" % (k + 1, s + 1), tower)
            if ob:
                made.append(ob)
        me = src.data
        bpy.data.objects.remove(src, do_unlink=True)
        bpy.data.meshes.remove(me)
    return made


HALL_XS = [0.0, 8.0, 16.0, 24.0, 32.0]
HALL_YS = [-PIER_Y, PIER_Y]

def build_hall(tower):
    parts_coll = get_coll("HallParts")
    parts = build_shell(parts_coll) + [build_trims(parts_coll), build_piers(parts_coll)] + build_vaults(parts_coll)
    # glass stays separate: it is unlit and must not cast shadows, so the moonlight comes through the windows
    glass = build_glass(tower)
    glass.name = "Floor5_Glass"
    bpy.ops.object.select_all(action="DESELECT")
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    hall = parts[0]
    hall.name = "Hall"
    cleanup(hall)
    # chunks for the 4-lights-per-object limit: bays along the nave x nave/aisles
    xs = [-1e4] + HALL_XS + [1e4]
    ys = [-1e4] + HALL_YS + [1e4]
    made = []
    n = 0
    for i in range(len(xs) - 1):
        for j in range(len(ys) - 1):
            planes = []
            if xs[i] > -1e3:
                planes.append(((xs[i], 0, 0), (1, 0, 0)))
            if xs[i + 1] < 1e3:
                planes.append(((xs[i + 1], 0, 0), (-1, 0, 0)))
            if ys[j] > -1e3:
                planes.append(((0, ys[j], 0), (0, 1, 0)))
            if ys[j + 1] < 1e3:
                planes.append(((0, ys[j + 1], 0), (0, -1, 0)))
            n += 1
            ob = split_planes(hall, planes, "Floor5_S%d" % n, tower)
            if ob:
                made.append(ob)
    me = hall.data
    bpy.data.objects.remove(hall, do_unlink=True)
    bpy.data.meshes.remove(me)
    return made + [glass]


def build_lift(tower):
    top = HZ + 5.0
    bm = bmesh.new()
    for a in GUIDE_ANGLES:
        u, _ = frame(a)
        c = u * GUIDE_R
        cylinder(bm, 0.08, -PIT, top, 8, 0, 0, 0, cx=c.x, cy=c.y)
    guides = obj_from_bm("Elevator_Guides", bm, ["M_Tower_Iron"], tower)
    world_uvs(guides, 0)

    # iron crown on the guides with the winch on top; chains run up to the keystone of the apse
    bm = bmesh.new()
    seg = 24
    for r0, r1, z0, z1 in ((GUIDE_R - 0.18, GUIDE_R + 0.18, top - 0.12, top + 0.12),):
        lo_i = [bm.verts.new((r0 * math.cos(2 * math.pi * i / seg), r0 * math.sin(2 * math.pi * i / seg), z0)) for i in range(seg)]
        lo_o = [bm.verts.new((r1 * math.cos(2 * math.pi * i / seg), r1 * math.sin(2 * math.pi * i / seg), z0)) for i in range(seg)]
        hi_o = [bm.verts.new((v.co.x, v.co.y, z1)) for v in lo_o]
        hi_i = [bm.verts.new((v.co.x, v.co.y, z1)) for v in lo_i]
        rings = [lo_i, lo_o, hi_o, hi_i]
        for i in range(seg):
            j = (i + 1) % seg
            for k in range(4):
                A, B = rings[k], rings[(k + 1) % 4]
                bm.faces.new((A[i], A[j], B[j], B[i])).material_index = 1
    for a in (45, 135):
        box(bm, 0, 0, top + 0.12, top + 0.4, 2 * GUIDE_R + 0.3, 0.3, a, 1)
    zc = top + 1.1
    def horiz_cyl(r, half_len, sg, mi):
        vs0 = [bm.verts.new((-half_len, r * math.cos(2 * math.pi * i / sg), zc + r * math.sin(2 * math.pi * i / sg))) for i in range(sg)]
        vs1 = [bm.verts.new((half_len, v.co.y, v.co.z)) for v in vs0]
        for i in range(sg):
            j = (i + 1) % sg
            bm.faces.new((vs0[i], vs0[j], vs1[j], vs1[i])).material_index = mi
        bm.faces.new(vs1).material_index = mi
        bm.faces.new(list(reversed(vs0))).material_index = mi
    horiz_cyl(0.6, 1.3, 12, 0)
    horiz_cyl(0.12, 1.9, 8, 1)
    for x in (-1.35, 1.35):
        box(bm, x, 0, zc - 0.75, zc + 0.75, 0.1, 1.4, 0, 1)
    for x in (-1.7, 1.7):
        box(bm, x, 0, top + 0.4, zc + 0.15, 0.25, 0.9, 0, 1)
    for x in (-0.8, 0.8):
        box(bm, x, 0, zc + 0.5, HZ + V_SPRING + V_RISE - 0.6, 0.09, 0.09, 0, 1)
    winch = obj_from_bm("Winch", bm, ["M_Tower_Wood", "M_Tower_Iron"], tower)
    world_uvs(winch, 0)
    return [guides, winch]


def smooth_all(tower):
    for ob in tower.objects:
        me = ob.data
        for p in me.polygons:
            p.use_smooth = True
        me.set_sharp_from_angle(angle=math.radians(30))


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


def export_tower():
    """Tower.fbx (all floors, guides, winch), HallBeams.fbx; the platform keeps its own Elevator.fbx."""
    art = os.path.join(PROJECT, "Assets", "_Project", "Art", "Tower")
    plat = bpy.data.objects["Elevator_Platform"]
    export_fbx(os.path.join(art, "Tower.fbx"), [o for o in bpy.data.collections["Tower"].objects if o is not plat])
    export_fbx(os.path.join(art, "HallBeams.fbx"), list(bpy.data.collections["Beams"].objects))


def build_all():
    ensure_glass_material()
    emission_material("M_Tower_Beam", make_beam_texture())
    clear_tower()
    tower = get_coll("Tower")
    made = build_round_floors(tower)
    made += build_hall(tower)
    made += build_lift(tower)
    smooth_all(tower)
    made.append(build_beams(get_coll("Beams")))
    return made
