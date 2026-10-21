"""1989 Fender Eric Clapton Stratocaster (Fusion 360 OBJ export) as a playable guitar.

The export keeps every part as its own group: fret wires, nut, fretboard, neck, body, pickguard,
bridge, tuners, the six strings and the electronics under the pickguard. The hidden electronics are
dropped, CAD seams are welded so curved surfaces shade smoothly, and the heavy tessellation (about
100k triangles per fret wire) is decimated. The .mtl appearances are mapped to our materials. As for
the Classic Player, everything that matters for playing is measured on the model: fret positions
(scale fitted to all 22 frets), fretboard height, the neck's width and depth along its length (by ray
casting) and the body outline. The model's strings are measured and then replaced by ours: their
centre lines give the exact saddle, nut, string-tree and post contacts and each string's height and
spread along the neck.
"""
import math
import os
import re

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import cache
from strat import _fit_frets, _interp, _mat, _bsdf, _verts

HERE = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.normpath(os.path.join(HERE, "..", "model_images",
                                          "1989-fender-eric-clapton-stratocaster-1.snapshot.6"))
OBJ_PATH = os.path.join(MODEL_DIR, "1989 Fender Eric Clapton Stratocaster GREEN v1.obj")
# world space after Blender's OBJ import (mm): neck along +x, the top of the guitar towards -y, bass side
# towards +z. Guitar frame: x_g = x, y_g = z, z_g = -y.
AXES = Matrix(((1, 0, 0), (0, 0, 1), (0, -1, 0)))
CELL = 5.0             # mm, raster of the body outline
FULL = ("GEETAR", "Neck Section", "Fretboard")  # the big visible surfaces keep their full tessellation
MAX_FACES = 5000       # any other part after decimation
FRET_FACES = 8000      # fret crowns ripple in reflections below this
# Fusion's part names: body, pickguard, and the strap button on the upper horn
BODY, PICKGUARD, STRAP = "GEETAR", "Ply1", "Body1:29"


def _base(name):
    return re.sub(r"\.\d{3}$", "", name)


def _bbox(o):
    pts = [o.matrix_world @ Vector(c) for c in o.bound_box]
    return (Vector([min(p[i] for p in pts) for i in range(3)]), Vector([max(p[i] for p in pts) for i in range(3)]))


def _mtl(o):
    return o.data.materials[0].name if o.data.materials else ""


def _bvh(objs):
    verts, polys = [], []
    for o in objs:
        n = len(verts)
        verts += _verts(o)
        polys += [[n + i for i in p.vertices] for p in o.data.polygons]
    return BVHTree.FromPolygons(verts, polys)


def _simplify(pts, tol):
    """Douglas-Peucker on a polyline."""
    if len(pts) < 3:
        return pts
    a, b = pts[0], pts[-1]
    ab = b - a

    def dist(p):
        t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared)) if ab.length_squared else 0.0
        return (p - (a + ab * t)).length

    i, d = max(((i, dist(p)) for i, p in enumerate(pts[1:-1], 1)), key=lambda t: t[1])
    if d <= tol:
        return [a, b]
    return _simplify(pts[:i + 1], tol)[:-1] + _simplify(pts[i:], tol)


def _string(o):
    """Centre line of one string from 2 mm slices along the neck. Slices where the string is a plain tube
    are kept (not where it dives into the bridge or wraps round the post); the path ends on the post's
    axis. Returns the simplified path, the radius, and straight-line fits of the speaking length."""
    vs = _verts(o)
    bins = {}
    for p in vs:
        bins.setdefault(int(p.x // 2), []).append(p)
    line, radii = [], []
    for k in sorted(bins):
        b = bins[k]
        c = sum(b, Vector()) / len(b)
        r = sum((p - c).yz.length for p in b) / len(b)
        if r < 1.0:
            line.append(c)
            radii.append(r)
    wrap = [p for p in vs if p.x > line[-1].x + 1.0]
    if wrap:
        post = sum(wrap, Vector()) / len(wrap)
        line.append(Vector((post.x, line[-1].y, post.z)))
    # least squares y(x), z(x) on the speaking length, well clear of saddle and nut
    mid = [c for c in line if 200 < c.x < 760]
    n, sx = len(mid), sum(c.x for c in mid)
    sxx = sum(c.x * c.x for c in mid)

    def fit(vals):
        sv, sxv = sum(vals), sum(c.x * v for c, v in zip(mid, vals))
        k = (n * sxv - sx * sv) / (n * sxx - sx * sx)
        return (sv - k * sx) / n, k

    radii.sort()
    return dict(path=_simplify(line, 0.03), radius=radii[len(radii) // 2],
                y=fit([c.y for c in mid]), z=fit([c.z for c in mid]))


def _measure(parts):
    frets = []
    for n in range(1, 23):
        lo, hi = _bbox(parts["Fret %d" % n])
        frets.append((lo.x + hi.x) / 2)
    nut_x, scale = _fit_frets(frets)

    nut_lo, nut_hi = _bbox(parts["Nut"])
    body = parts[BODY]
    blo, bhi = _bbox(body)
    centre = Vector(((blo.x + bhi.x) / 2, (blo.y + bhi.y) / 2, (nut_lo.z + nut_hi.z) / 2))

    # fretboard height on the centre line, between the frets (the board is radiused)
    board = _bvh([parts["Fretboard"]])
    hits = []
    for a, b in zip(frets, frets[1:]):
        h = board.ray_cast(Vector(((a + b) / 2, -200, centre.z)), Vector((0, 1, 0)))
        if h[0]:
            hits.append(h[0].y)
    board_y = sorted(hits)[len(hits) // 2]

    # neck width (fretboard edges, 2 mm under the top) and back (neck + skunk stripe) along its length
    neck = _bvh([o for name, o in parts.items() if name in ("Neck Section", "Fretboard") or name.startswith("Back Infill")])
    heel_x = _bbox(parts["Fretboard"])[0].x
    width, back = [], []
    x = heel_x + 2.0
    while x < nut_x:
        y = board_y + 2.0
        hi = neck.ray_cast(Vector((x, y, centre.z + 80)), Vector((0, 0, -1)))
        lo = neck.ray_cast(Vector((x, y, centre.z - 80)), Vector((0, 0, 1)))
        bk = neck.ray_cast(Vector((x, 100, centre.z)), Vector((0, -1, 0)))
        if hi[0] and lo[0] and bk[0]:
            width.append((x, (hi[0].z - lo[0].z) / 2))
            back.append((x, bk[0].y))
        x += 4.0

    # strings, high E (-z) first
    strings = sorted((o for o in parts.values() if _mtl(o).startswith("Opaque(144")),
                     key=lambda o: sum(_bbox(o), Vector()).z)
    strings = [_string(o) for o in strings]

    # body outline: raster of the body's triangles projected onto the face of the guitar
    me = body.data
    me.calc_loop_triangles()
    pts = _verts(body)
    x0, z0 = blo.x, blo.z
    nx, nz = int((bhi.x - x0) / CELL) + 1, int((bhi.z - z0) / CELL) + 1
    grid = bytearray(nx * nz)
    for tri in me.loop_triangles:
        a, b, c = (pts[i] for i in tri.vertices)
        area = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z)
        if abs(area) < 1e-9:
            continue
        for ix in range(int((min(a.x, b.x, c.x) - x0) / CELL), int((max(a.x, b.x, c.x) - x0) / CELL) + 1):
            for iz in range(int((min(a.z, b.z, c.z) - z0) / CELL), int((max(a.z, b.z, c.z) - z0) / CELL) + 1):
                px, pz = x0 + (ix + 0.5) * CELL, z0 + (iz + 0.5) * CELL
                w1 = ((b.x - px) * (c.z - pz) - (c.x - px) * (b.z - pz)) / area
                w2 = ((c.x - px) * (a.z - pz) - (a.x - px) * (c.z - pz)) / area
                if w1 >= 0 and w2 >= 0 and w1 + w2 <= 1 and 0 <= ix < nx and 0 <= iz < nz:
                    grid[iz * nx + ix] = 1

    slo, shi = _bbox(parts[STRAP])
    return dict(nut_x=nut_x, scale=scale, frets=frets, centre=centre, board_y=board_y, top_y=blo.y,
                back_y=bhi.y, width=width, back=back, strings=strings, grid=(x0, z0, nx, nz, grid),
                strap=(slo + shi) / 2)


# -------------------------------------------------------------------- clean up
def _hidden(parts):
    """Electronics under the pickguard: inside its outline, below its top face, reaching into the body."""
    glo, ghi = _bbox(parts[PICKGUARD])
    top_y, back_y = _bbox(parts[BODY])[0].y, _bbox(parts[BODY])[1].y
    out = []
    for name, o in parts.items():
        lo, hi = _bbox(o)
        if (glo.x <= lo.x and hi.x <= ghi.x and glo.z <= lo.z and hi.z <= ghi.z
                and lo.y > glo.y - 0.05 and hi.y > top_y + 0.4 and hi.y < back_y - 1.0):
            out.append(name)
    return out


def _lighten(parts):
    """Weld the CAD seams, decimate the heavy parts and set face-area weighted normals (CAD tessellation
    has long slivers across flat faces; plain smoothing bends their shading towards the rounded edges).
    All objects evaluate in one (parallel) depsgraph pass."""
    for name, o in parts.items():
        me = o.data
        me.shade_smooth()
        me.set_sharp_from_angle(angle=math.radians(35))
        o.modifiers.new("weld", "WELD").merge_threshold = 0.002
        target = FRET_FACES if name.startswith("Fret ") else MAX_FACES
        if name not in FULL and len(me.polygons) > target:
            o.modifiers.new("decimate", "DECIMATE").ratio = target / len(me.polygons)
        wn = o.modifiers.new("normals", "WEIGHTED_NORMAL")
        wn.mode, wn.keep_sharp, wn.weight = "FACE_AREA", True, 100
    dg = bpy.context.evaluated_depsgraph_get()
    for o in parts.values():
        old = o.data
        me = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
        o.modifiers.clear()
        o.data = me
        bpy.data.meshes.remove(old)


# ------------------------------------------------------------------ materials
def _maple(name):
    """Gloss maple with a vintage tint: fine grain running along the neck (model x)."""
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    nt = m.node_tree
    b = _bsdf(m)
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    mp.inputs["Scale"].default_value = (0.003, 0.22, 0.22)  # local mm: long along the neck, ~1.5 mm across
    wave = nt.nodes.new("ShaderNodeTexWave")
    wave.wave_type, wave.bands_direction, wave.wave_profile = "BANDS", "Y", "SAW"
    wave.inputs["Scale"].default_value = 1.0
    wave.inputs["Distortion"].default_value = 7.0
    wave.inputs["Detail"].default_value = 4.0
    wave.inputs["Detail Scale"].default_value = 0.6
    ramp = nt.nodes.new("ShaderNodeValToRGB")  # thin darker late-wood lines on light maple
    els = ramp.color_ramp.elements
    els[0].position, els[0].color = 0.0, (0.5, 0.29, 0.11, 1)
    els[1].position, els[1].color = 0.22, (0.7, 0.46, 0.21, 1)
    noise = nt.nodes.new("ShaderNodeTexNoise")  # broad, gentle colour variation
    noise.inputs["Scale"].default_value = 0.02
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type, mix.blend_type = "RGBA", "MULTIPLY"
    mix.inputs["Factor"].default_value = 0.25
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], wave.inputs["Vector"])
    nt.links.new(wave.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])
    sock = {s.identifier: s for s in list(mix.inputs) + list(mix.outputs)}  # Mix shows one socket set per type
    nt.links.new(ramp.outputs["Color"], sock["A_Color"])
    nt.links.new(noise.outputs["Color"], sock["B_Color"])
    nt.links.new(sock["Result_Color"], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.28
    b.inputs["Coat Weight"].default_value = 0.6
    m.diffuse_color = (0.7, 0.47, 0.22, 1)
    return m


def _paint(finish):
    """Metallic finish under clear coat (the 1989 Clapton came in candy green, torino red and pewter)."""
    name = "clapton_" + finish
    m = bpy.data.materials.get(name)
    if m:
        return m
    color, metal = FINISHES[finish]
    m = _mat(name, color, 0.3, metal, 1.0)
    b = _bsdf(m)
    b.inputs["Coat Roughness"].default_value = 0.03
    return m


FINISHES = {"green": ((0.03, 0.3, 0.025), 0.55), "torino_red": ((0.42, 0.015, 0.02), 0.55),
            "pewter": ((0.33, 0.34, 0.35), 0.75), "black": ((0.012, 0.012, 0.014), 0.0)}
APPEARANCES = [  # .mtl appearance prefix -> (material, colour, roughness, metallic)
    ("Nickel", "clapton_frets", (0.78, 0.76, 0.7), 0.22, 1.0),
    ("Steel_-_Satin", "clapton_satin", (0.7, 0.7, 0.72), 0.35, 1.0),
    ("Stainless", "clapton_chrome", (0.85, 0.85, 0.87), 0.12, 1.0),
    ("Chrome", "clapton_chrome", (0.85, 0.85, 0.87), 0.12, 1.0),
    ("Coating_-_Black_Oxide", "clapton_black_metal", (0.04, 0.04, 0.045), 0.4, 1.0),
    ("Copper", "clapton_copper", (0.8, 0.45, 0.3), 0.3, 1.0),
    ("Brass", "clapton_brass", (0.8, 0.62, 0.3), 0.3, 1.0),
    ("Plastic_-_Glossy_(White)", "clapton_pickguard", (0.88, 0.88, 0.85), 0.3, 0.0),
    ("Plastic_-_Glossy_(Yellow)", "clapton_knobs", (0.86, 0.83, 0.7), 0.3, 0.0),
    ("Plastic_-_Glossy_(Black)", "clapton_dots", (0.015, 0.015, 0.015), 0.3, 0.0),
    ("ABS", "clapton_covers", (0.88, 0.87, 0.8), 0.35, 0.0),
    ("3D_Ash", "clapton_covers", (0.88, 0.87, 0.8), 0.35, 0.0),  # neck pickup cover, knob bases
    ("Acetal", "clapton_nut", (0.92, 0.9, 0.82), 0.35, 0.0),
    ("Walnut", "clapton_walnut", (0.22, 0.1, 0.045), 0.3, 0.0),
]


def _dress(parts, finish):
    dark = _mat("clapton_dark", (0.03, 0.03, 0.03), 0.5)
    for name, o in parts.items():
        mtl = _mtl(o)
        if mtl.startswith("Paint"):
            m = _paint(finish)
        elif mtl.startswith("3D_Maple"):
            m = _maple("clapton_maple")
        else:
            hit = next((a for a in APPEARANCES if mtl.startswith(a[0])), None)
            m = _mat(*hit[1:]) if hit else dark
        o.data.materials.clear()
        o.data.materials.append(m)


# ---------------------------------------------------------------------- place
def _prepare(finish):
    """Import, measure, clean up and dress the model (in model space; cached as a .blend library)."""
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=OBJ_PATH, use_split_groups=True)
    parts = {_base(o.name): o for o in bpy.data.objects if o not in before and o.type == "MESH"}
    ms = _measure(parts)
    drop = [n for n, o in parts.items() if _mtl(o).startswith("Opaque(144")] + _hidden(parts)
    for n in drop:
        bpy.data.objects.remove(parts.pop(n))
    _lighten(parts)
    _dress(parts, finish)
    return parts, ms


def clapton_guitar(rig, c, xdir, zdir, finish, coll):
    """Import the Clapton Stratocaster into `coll` and return the guitar frame + playing geometry (same
    keys as the other guitars). c, xdir, zdir: body centre and orientation in the player's rig frame."""
    parts, ms = cache.model("clapton_" + finish, [OBJ_PATH, OBJ_PATH[:-4] + ".mtl"], ["clapton.py", "strat.py"],
                            _prepare, finish)
    for o in parts.values():
        for col in list(o.users_collection):
            col.objects.unlink(o)
        coll.objects.link(o)

    # guitar frame in the world
    X = Vector(xdir).normalized()
    Z = Vector(zdir)
    Z = (Z - X * Z.dot(X)).normalized()
    Y = Z.cross(X)
    B = rig.R @ Matrix((X, Y, Z)).transposed()
    c_world = rig.w(Vector(c))
    root = bpy.data.objects.new("Stratocaster_Clapton_" + finish, None)
    coll.objects.link(root)
    C = ms["centre"]
    for o in parts.values():  # each part keeps the importer's axis conversion as its local transform
        o.parent = root
    root.matrix_world = (Matrix.Translation(c_world) @ (B @ AXES).to_4x4() @ Matrix.Scale(0.001, 4)
                         @ Matrix.Translation(-C))
    bpy.context.view_layer.update()

    def gl(p):  # model mm -> guitar frame m
        return AXES @ (Vector(p) - C) * 0.001

    def W(p):
        return c_world + B @ Vector(p)

    nut_x = (ms["nut_x"] - C.x) / 1000
    scale = ms["scale"] / 1000
    bridge_x = nut_x - scale
    board_z = -(ms["board_y"] - C.y) / 1000
    top = -(ms["top_y"] - C.y) / 1000
    S = ms["strings"]  # 0 = high E (-y side) ... 5 = low E

    def string_y(i, x):  # across the neck, from the model string's fitted line
        a, k = S[i]["z"]
        return (a + k * (x * 1000 + C.x) - C.z) / 1000

    def string_h(i, x):  # string centre above the guitar frame origin (radiused board: middle strings higher)
        a, k = S[i]["y"]
        return -(a + k * (x * 1000 + C.x) - C.y) / 1000

    y_bridge = [string_y(i, bridge_x) for i in range(6)]
    width = [((x - C.x) / 1000, w / 1000) for x, w in ms["width"]]
    back = [((x - C.x) / 1000, -(y - C.y) / 1000) for x, y in ms["back"]]

    def fret_x(n):
        return nut_x - scale * (1 - 2 ** (-n / 12))

    def fret_point(string, fret, lift=0.0):
        """Fingertip target just behind a fret; string 0 is the highest (thinnest) one."""
        x = fret_x(fret) + 0.3 * (fret_x(fret - 1) - fret_x(fret))
        return W((x, string_y(string, x), string_h(string, x) + 0.005 + lift))

    x0, z0, nx, nz, grid = ms["grid"]

    def body_inside(x, y):
        mx, mz = x * 1000 + C.x, y * 1000 + C.z
        ix, iz = int((mx - x0) / CELL), int((mz - z0) / CELL)
        return 0 <= ix < nx and 0 <= iz < nz and bool(grid[iz * nx + ix])

    paths = [[W(gl(p)) for p in s["path"]] for s in S]
    return {"W": W, "X": rig.R @ X, "Y": rig.R @ Y, "Z": rig.R @ Z, "fret_x": fret_x, "fret_point": fret_point,
            "top": top, "bridge_x": bridge_x, "nut_x": nut_x, "scale": scale, "strings": y_bridge,
            "half_w": lambda x: _interp(width, x), "board_z": board_z, "back_z": lambda x: _interp(back, x),
            "string_z": lambda x: sum(string_h(i, x) for i in range(6)) / 6, "body_inside": body_inside,
            "thickness": (ms["back_y"] - ms["top_y"]) / 1000, "neck_w": 2 * _interp(width, fret_x(3)),
            "strap_top": W(gl(ms["strap"])), "string_paths": paths,
            "measured": dict(scale_mm=round(ms["scale"], 1), frets=len(ms["frets"]),
                             nut_width_mm=round(2 * width[-1][1] * 1000, 1) if width else None,
                             neck_depth_mm=round((board_z - _interp(back, fret_x(3))) * 1000, 1),
                             string_gauges_in=[round(2 * s["radius"] / 25.4, 3) for s in S],
                             parts=len(parts), faces=sum(len(o.data.polygons) for o in parts.values()))}
