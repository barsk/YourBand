"""Fender Precision Bass (SolidWorks STL from model_images/p-bass-2) as the playable bass.

The STL comes as one welded shell (body, neck, fretboard, frets, pickguard, bridge plate and strings)
plus separate small parts (tuners, pickups, saddles, knobs, screws). The shell is coloured by region
- sunburst body (sunburn.png projected onto its face), white pickguard, chrome bridge plate, rosewood
fretboard, nickel frets, maple neck and headstock - and its strings are replaced by ours. As for the
Stratocaster, everything that matters for playing is measured on the model: fret positions (scale
fitted to the frets), nut and saddle positions, fretboard height, the neck's width and depth (by
ray casting) and the body outline.
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

from strat import _fit_frets, _interp, _mat, _bsdf

HERE = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.normpath(os.path.join(HERE, "..", "model_images", "p-bass-2"))
STL_PATH = os.path.join(MODEL_DIR, "bass_precision", "bass_precision.STL")
SUNBURST = os.path.join(MODEL_DIR, "sunburn.png")
# model space (mm): neck along +y, the top of the bass towards +z, bass side towards -x.
# Guitar frame: x_g = y, y_g = -x, z_g = z.
AXES = Matrix(((0, 1, 0), (-1, 0, 0), (0, 0, 1)))
CELL = 5.0  # mm, raster of the body outline
MATS = ("body", "pickguard", "chrome", "rosewood", "frets", "maple", "nut")


def _components(bm):
    comp, n = {}, 0
    for v in bm.verts:
        if v.index in comp:
            continue
        stack = [v]
        comp[v.index] = n
        while stack:
            a = stack.pop()
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in comp:
                    comp[b.index] = n
                    stack.append(b)
        n += 1
    return comp, n


def _measure(bm, comp, main):
    V = [v.co.copy() for v in bm.verts]
    mv = [V[i] for i in range(len(V)) if comp[i] == main]
    bvh = BVHTree.FromBMesh(bm)
    # fretboard: centre line from its edges; the board is radiused, so its height is measured on the
    # centre line by ray casting between the frets (the lowest quarter of the hits)
    board_pts = [p for p in mv if 750 < p.y < 1200 and 71.0 <= p.z <= 73.0]
    cx = (min(p.x for p in board_pts) + max(p.x for p in board_pts)) / 2
    def board_height(x):
        hits = []
        y = 800.0
        while y < 1190:
            h = bvh.ray_cast(Vector((x, y, 300.0)), Vector((0, 0, -1)))
            if h[0] is not None:
                hits.append(h[0].z)
            y += 2.0
        hits.sort()
        return hits[len(hits) // 4]

    board_z = board_height(cx)
    edge_z = min(board_height(cx - 20), board_height(cx + 20))  # lower than the centre if radiused
    # frets: crowns standing 0.5-1.3 mm above the board on the centre line (higher up are the strings);
    # the first cluster is the nut
    # (the fret wires are extruded straight across, so their vertices sit at the fretboard edges)
    half = (max(p.x for p in board_pts) - min(p.x for p in board_pts)) / 2
    crowns = sorted(p.y for p in mv if 600 < p.y < 1230 and abs(p.x - cx) < half + 1 and
                    board_z + 0.5 < p.z < board_z + 1.3)
    clusters = []
    for y in crowns:
        if clusters and y - clusters[-1][-1] < 1.5:
            clusters[-1].append(y)
        else:
            clusters.append([y])
    ys = sorted(((c[0] + c[-1]) / 2 for c in clusters), reverse=True)
    nut_crown = ys[0]
    frets = [y for y in ys[1:] if nut_crown - y > 6]  # the nut shows up with both its edges
    nut_y, scale = _fit_frets(frets)
    # fretboard half width along the neck
    width = []
    y = 760.0
    while y < nut_y:
        sl = [p for p in board_pts if abs(p.y - y) < 15]
        if sl:
            width.append((y, (max(p.x for p in sl) - min(p.x for p in sl)) / 2))
        y += 30.0
    # neck back: ray cast upwards on the centre line
    back = []
    y = 800.0
    while y < nut_y:
        hit = bvh.ray_cast(Vector((cx, y, -100.0)), Vector((0, 0, 1)))
        if hit[0] is not None:
            back.append((y, hit[0].z))
        y += 20.0
    # saddles (separate parts near the bridge) give the string positions there
    parts = {}
    for i, c in comp.items():
        parts.setdefault(c, []).append(i)
    saddles, tuners, pickups = [], [], []
    for c, idx in parts.items():
        if c == main:
            continue
        pts = [V[i] for i in idx]
        lo = Vector([min(p[k] for p in pts) for k in range(3)])
        hi = Vector([max(p[k] for p in pts) for k in range(3)])
        size = hi - lo
        if 15 < size.x < 30 and 40 < size.y < 60 and size.z < 12:
            saddles.append(((lo.x + hi.x) / 2, hi.y, hi.z))
        elif size.x > 60 and size.y > 40 and size.z > 30:
            post = [p for p in pts if p.z > hi.z - 6 and abs(p.x - cx) < 40]
            post = post or pts
            tuners.append(Vector((sum(p.x for p in post) / len(post), sum(p.y for p in post) / len(post), hi.z - 4)))
    saddles.sort()          # low E (-x) first
    tuners.sort(key=lambda p: p.y)  # nearest the nut first = low E
    # string heights: our strings rest on the saddles and on the nut
    string_z_bridge = sum(s[2] for s in saddles) / len(saddles) + 1.0
    # at the nut the strings sit in slots: centre = fret crown (1 mm) + 0.6 mm clearance + string radius
    string_z_nut = board_z + 1.0 + 0.6 + 1.0
    # body: outline raster from above, thickness, horn tip (strap button)
    body_pts = [p for p in mv if p.y < 800 and abs(p.x - cx) > 40]
    bx0, bx1 = min(p.x for p in mv), max(p.x for p in mv)
    by0, by1 = min(p.y for p in mv), max(p.y for p in body_pts)
    nx, ny = int((bx1 - bx0) / CELL) + 1, int((by1 - by0) / CELL) + 1
    grid = bytearray(nx * ny)
    levels = {}
    for j in range(ny):
        for i in range(nx):
            x, y = bx0 + (i + 0.5) * CELL, by0 + (j + 0.5) * CELL
            hit = bvh.ray_cast(Vector((x, y, 200.0)), Vector((0, 0, -1)))
            if hit[0] is not None and hit[0].z < 75:
                grid[j * nx + i] = 1
                if abs(x - cx) > 40:
                    levels[round(hit[0].z, 1)] = levels.get(round(hit[0].z, 1), 0) + 1
    # the body top is the lowest height covering a good share of the outline (the pickguard sits higher)
    total = sum(levels.values())
    body_top = min(z for z, cnt in levels.items() if cnt > 0.1 * total)
    body_back = min(p.z for p in body_pts)
    horn = max((p for p in body_pts if p.x < cx), key=lambda p: p.y)
    return dict(cx=cx, board_z=board_z, edge_z=edge_z, nut_y=nut_y, scale=scale, frets=frets, nut_crown=nut_crown,
                width=width, back=back, saddles=saddles, tuners=tuners, string_z_bridge=string_z_bridge,
                string_z_nut=string_z_nut, grid=(bx0, by0, nx, ny, grid), body_top=body_top, body_back=body_back,
                body_box=(bx0, bx1, by0, by1), horn=horn)


def _materials(ms):
    body = bpy.data.materials.new("pbass_sunburst")
    nt = body.node_tree
    b = _bsdf(body)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    bx0, bx1, by0, by1 = ms["body_box"]
    mp.inputs["Scale"].default_value = (1 / (bx1 - bx0), 1 / (by1 - by0), 0)
    mp.inputs["Location"].default_value = (-bx0 / (bx1 - bx0), -by0 / (by1 - by0), 0)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(SUNBURST, check_existing=True)
    tex.extension = "EXTEND"
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.16
    b.inputs["Coat Weight"].default_value = 1.0
    body.diffuse_color = (0.45, 0.2, 0.03, 1)
    return {"body": body,
            "pickguard": _mat("pbass_pickguard", (0.9, 0.9, 0.87), 0.3),
            "chrome": _mat("strat_chrome", (0.85, 0.85, 0.87), 0.12, 1.0),
            "rosewood": _mat("pbass_rosewood", (0.11, 0.055, 0.03), 0.6),
            "frets": _mat("strat_frets", (0.78, 0.76, 0.7), 0.22, 1.0),
            "maple": _mat("pbass_maple", (0.86, 0.68, 0.44), 0.35),
            "nut": _mat("strat_nut", (0.9, 0.87, 0.78), 0.4),
            "pickup": _mat("pbass_pickup", (0.02, 0.02, 0.022), 0.45),
            "dot": _mat("pbass_dot", (0.92, 0.9, 0.85), 0.35)}


def _classify(f, ms):
    """Material of one face of the welded shell, from where it sits on the bass."""
    c = f.calc_center_median()
    cx, bz, nut = ms["cx"], ms["board_z"], ms["nut_crown"]
    hw = _interp(ms["width"], c.y) if ms["width"] else 30
    if c.y > nut + 3:
        return "maple"                                   # headstock
    on_neck = abs(c.x - cx) < hw + 0.6 and c.y > ms["frets"][-1] - 25
    if on_neck and c.z > bz - 6.5:
        if abs(c.y - nut) <= 3 and c.z > bz:
            return "nut"
        board_here = bz - (bz - ms["edge_z"]) * min(1.0, ((c.x - cx) / hw) ** 2)  # radiused board
        return "frets" if c.z > board_here + 0.25 else "rosewood"
    if c.y > ms["body_box"][3] - 2 or (on_neck and c.z <= bz - 6.5 and c.y > 640):
        return "maple"                                   # neck back and heel
    if c.z > ms["body_top"] + 0.4:                       # raised over the body: pickguard or bridge plate
        return "chrome" if c.y < ms["saddles"][0][1] + 25 and abs(c.x - cx) < 45 else "pickguard"
    return "body"


def _prepare():
    """Import the STL, measure it, drop its strings and colour its faces by region (model space; cached)."""
    before = set(bpy.data.objects)
    bpy.ops.wm.stl_import(filepath=STL_PATH)
    src = next(o for o in bpy.data.objects if o not in before)
    bm = bmesh.new()
    bm.from_mesh(src.data)
    bm.verts.ensure_lookup_table()
    comp, n = _components(bm)
    sizes = [0] * n
    for c_ in comp.values():
        sizes[c_] += 1
    main = max(range(n), key=lambda i: sizes[i])  # the welded shell
    ms = _measure(bm, comp, main)
    mats = _materials(ms)
    names = list(MATS) + ["pickup", "dot"]
    me = src.data
    me.materials.clear()
    for k in names:
        me.materials.append(mats[k])
    idx = {k: i for i, k in enumerate(names)}
    # drop the model's strings (long slivers running along the neck, standing off the fretboard and
    # the headstock); ours go exactly on the measured nut and saddles
    strings = [f for f in bm.faces if comp[f.verts[0].index] == main and _is_string(f, ms)]
    bmesh.ops.delete(bm, geom=strings, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.verts.ensure_lookup_table()
    comp, _ = _components(bm)
    by_comp = _faces_by_comp(bm, comp)
    main = max(by_comp, key=lambda k: len(by_comp[k]))
    for cid, faces in by_comp.items():
        if cid == main:
            for f in faces:
                f.material_index = idx[_classify(f, ms)]
            continue
        # separate small parts: pickups black, everything else (tuners, saddles, knobs, screws) chrome
        pts = [v.co for f in faces for v in f.verts]
        size = Vector([max(p[k] for p in pts) - min(p[k] for p in pts) for k in range(3)])
        is_pickup = 60 < size.x < 100 and 25 < size.y < 45 and size.z < 10
        for f in faces:
            f.material_index = idx["pickup" if is_pickup else "chrome"]
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(35))
    src.name = "PrecisionBass"
    return {"PrecisionBass": src}, ms


def pbass_guitar(rig, c, xdir, zdir, coll):
    """Import the P-Bass into `coll`; returns the guitar frame + playing geometry (same keys as the others)."""
    import cache
    parts, ms = cache.model("pbass", [STL_PATH, SUNBURST], ["pbass.py", "strat.py"], _prepare)
    src = parts["PrecisionBass"]
    for col in list(src.users_collection):
        col.objects.unlink(src)
    coll.objects.link(src)

    # place: body centre on the neck line at mid thickness
    bx0, bx1, by0, by1 = ms["body_box"]
    C = Vector((ms["cx"], (by0 + by1) / 2, (ms["body_top"] + ms["body_back"]) / 2))
    X = Vector(xdir).normalized()
    Z = Vector(zdir)
    Z = (Z - X * Z.dot(X)).normalized()
    Y = Z.cross(X)
    B = rig.R @ Matrix((X, Y, Z)).transposed()
    c_world = rig.w(Vector(c))
    src.matrix_world = (Matrix.Translation(c_world) @ (B @ AXES).to_4x4() @ Matrix.Scale(0.001, 4)
                        @ Matrix.Translation(-C))

    def gl(p):
        return AXES @ (Vector(p) - C) * 0.001

    def W(p):
        return c_world + B @ Vector(p)

    nut_x = (ms["nut_y"] - C.y) / 1000
    scale = ms["scale"] / 1000
    bridge_x = nut_x - scale
    board_z = (ms["board_z"] - C.z) / 1000
    z_nut, z_bridge = (ms["string_z_nut"] - C.z) / 1000, (ms["string_z_bridge"] - C.z) / 1000
    # string 0 = G (thinnest, -y side) ... 3 = low E; spacing at the saddles and at the nut
    y_bridge = [-(s[0] - C.x) / 1000 for s in reversed(ms["saddles"])]
    nut_hw = _interp(ms["width"], ms["nut_y"]) / 1000
    y_nut = [(i - 1.5) / 3 * (2 * nut_hw - 0.008) for i in range(4)]
    width = [((y - C.y) / 1000, w / 1000) for y, w in ms["width"]]
    back = [((y - C.y) / 1000, (z - C.z) / 1000) for y, z in ms["back"]]

    def fret_x(n):
        return nut_x - scale * (1 - 2 ** (-n / 12))

    def string_y(i, x):
        t = (nut_x - x) / scale
        return y_nut[i] + (y_bridge[i] - y_nut[i]) * t

    def string_z(x):
        t = min(1.0, max(0.0, (nut_x - x) / scale))
        return z_nut + (z_bridge - z_nut) * t

    def fret_point(string, fret, lift=0.0):
        x = fret_x(fret) + 0.3 * (fret_x(fret - 1) - fret_x(fret))
        return W((x, string_y(string, x), string_z(x) + 0.005 + lift))

    gx0, gy0, nx, ny, grid = ms["grid"]

    def body_inside(x, y):
        mx, my = -y * 1000 + C.x, x * 1000 + C.y
        i, j = int((mx - gx0) / CELL), int((my - gy0) / CELL)
        return 0 <= i < nx and 0 <= j < ny and bool(grid[j * nx + i])

    # dot inlays (not in the CAD model), between the frets like on a real P-Bass
    dot = next(m for m in src.data.materials if m.name.startswith("pbass_dot"))
    for f, double in ((3, 0), (5, 0), (7, 0), (9, 0), (12, 1), (15, 0), (17, 0), (19, 0)):
        x = (fret_x(f) + fret_x(f - 1)) / 2
        for off in ((-0.012, 0.012) if double else (0.0,)):
            bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.0032, depth=0.0004,
                                                location=W((x, off, board_z + 0.0001)))
            d = bpy.context.active_object
            d.rotation_mode = "QUATERNION"
            d.rotation_quaternion = (B @ Matrix.Identity(3)).to_quaternion()
            d.data.materials.append(dot)
            for col in list(d.users_collection):
                col.objects.unlink(d)
            coll.objects.link(d)

    paths = []
    for i in range(4):
        post = gl(ms["tuners"][3 - i]) if len(ms["tuners"]) == 4 else (nut_x + 0.05 + 0.04 * (3 - i), y_nut[i], z_nut)
        paths.append([W((bridge_x, y_bridge[i], z_bridge)), W((nut_x, y_nut[i], z_nut)), W(post)])

    return {"W": W, "X": rig.R @ X, "Y": rig.R @ Y, "Z": rig.R @ Z, "fret_x": fret_x, "fret_point": fret_point,
            "top": (ms["body_top"] - C.z) / 1000, "bridge_x": bridge_x, "nut_x": nut_x, "scale": scale,
            "strings": y_bridge, "half_w": lambda x: _interp(width, x), "board_z": board_z,
            "back_z": lambda x: _interp(back, x), "string_z": string_z, "body_inside": body_inside,
            "thickness": (ms["body_top"] - ms["body_back"]) / 1000, "neck_w": 2 * _interp(width, fret_x(5)),
            "strap_top": W(gl(ms["horn"])), "string_paths": paths,
            "measured": dict(scale_mm=round(ms["scale"], 1), frets_on_neck=len(ms["frets"]),
                             nut_width_mm=round(2 * nut_hw * 1000, 1),
                             neck_depth_mm=round((board_z - _interp(back, fret_x(5))) * 1000, 1))}


def _is_string(f, ms):
    edges = [(e.verts[1].co - e.verts[0].co) for e in f.edges]
    longest = max(edges, key=lambda d: d.length)
    if longest.length < 25 or 2 * f.calc_area() / longest.length > 2.4 or abs(longest.y) < 0.7 * longest.length:
        return False
    c = f.calc_center_median()
    if abs(c.x - ms["cx"]) > 45:
        return False
    return c.z > ms["board_z"] + 1.0 or (c.y > ms["nut_crown"] - 5 and c.z > ms["board_z"] - 3)


def _faces_by_comp(bm, comp):
    out = {}
    for f in bm.faces:
        out.setdefault(comp[f.verts[0].index], []).append(f)
    return out
