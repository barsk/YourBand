"""Fender Stratocaster (Classic Player OBJ model) as the playable guitar.

The model is placed in the same guitar frame the hand solvers use (x along the neck towards the
headstock, y across it towards the bass side, z out of the top), and everything that matters for
playing is measured on the model itself: fret positions (scale length fitted to all 21 frets), nut
and saddle positions, string spread, fretboard height, the neck's width and depth along its length,
and the body outline.
"""
import math
import os

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
MODEL_DIR = os.path.normpath(os.path.join(HERE, "..", "model_images", "Stratocaster", "Stratocaster Classic Player"))
OBJ_PATH = os.path.join(MODEL_DIR, ",OBJ", "Stratocaster.obj")
# Model space after Blender's OBJ import (cm): neck along +z, front of the guitar towards -y and the
# bass side towards -x. Guitar frame: x_g = z, y_g = -x, z_g = -y.
AXES = Matrix(((0, 0, 1), (-1, 0, 0), (0, -1, 0)))
NUT_EDGE = 0.42  # cm from the nut's outer slot to the edge of the neck (Fender: about 1/8 inch)
CELL = 0.5       # cm, raster of the body outline


def _verts(o):
    mw = o.matrix_world
    return [mw @ v.co for v in o.data.vertices]


def _fit_frets(z_positions):
    """Least-squares fit of fret_n = nut - scale * (1 - 2^(-n/12)) to the measured fret wires."""
    a = [1 - 2 ** (-(n + 1) / 12) for n in range(len(z_positions))]
    n, sa, saa = len(a), sum(a), sum(x * x for x in a)
    sf, saf = sum(z_positions), sum(x * f for x, f in zip(a, z_positions))
    scale = (sf * sa - n * saf) / (n * saa - sa * sa)
    return (sf + scale * sa) / n, scale


def _interp(table, x):
    """Piecewise-linear lookup in a sorted [(x, value)] table, clamped at the ends."""
    if x <= table[0][0]:
        return table[0][1]
    for (x0, v0), (x1, v1) in zip(table, table[1:]):
        if x <= x1:
            return v0 + (v1 - v0) * (x - x0) / (x1 - x0)
    return table[-1][1]


def _measure(parts):
    by = lambda prefix: [o for name, o in parts.items() if name.startswith(prefix)]
    # frets: cluster the fret wires along the neck, fret 1 nearest the nut
    clusters = []
    for z in sorted(v.z for v in _verts(parts["Tasti"])):
        if clusters and z - clusters[-1][-1] < 0.3:
            clusters[-1].append(z)
        else:
            clusters.append([z])
    frets = sorted(((c[0] + c[-1]) / 2 for c in clusters), reverse=True)
    nut_z, scale = _fit_frets(frets)

    nut = _verts(parts["Capotasto"])
    neck_cx = (max(v.x for v in nut) + min(v.x for v in nut)) / 2
    nut_spread = max(v.x for v in nut) - min(v.x for v in nut) - 2 * NUT_EDGE
    saddles = sorted(sum(v.x for v in vs) / len(vs) for vs in map(_verts, by("cordiera")))  # low E (-x) first
    string_y = min(min(v.y for v in nut), min(min(v.y for v in _verts(o)) for o in by("cordiera"))) + 0.06
    board = _verts(parts["tastiera"])
    board_y = min(v.y for v in board if abs(v.x - neck_cx) < 0.6)
    body = parts["Body"]
    bv = _verts(body)
    top_y, back_y = min(v.y for v in bv), max(v.y for v in bv)
    centre = Vector((neck_cx, (top_y + back_y) / 2, (min(v.z for v in bv) + max(v.z for v in bv)) / 2))

    # neck width (from the fretboard edges) and back (from the neck) along its length
    neck = _verts(parts["Neck"])
    heel_z = min(v.z for v in board)
    width, back = [], []
    z = heel_z + 1.0
    while z < nut_z:
        sl = [v for v in board if abs(v.z - z) < 1.5]
        nk = [v for v in neck if abs(v.z - z) < 2.0 and abs(v.x - neck_cx) < 0.8]
        if sl and nk:
            width.append((z, (max(v.x for v in sl) - min(v.x for v in sl)) / 2))
            back.append((z, max(v.y for v in nk)))
        z += 2.0

    posts = sorted(by("Avvolgicorda"), key=lambda o: sum(v.z for v in _verts(o)))  # nearest the nut = low E
    post_pts = []
    for o in posts:
        vs = _verts(o)
        post_pts.append(Vector((sum(v.x for v in vs) / len(vs), min(v.y for v in vs) + 0.25, sum(v.z for v in vs) / len(vs))))

    # body outline: raster of the body's triangles projected onto the face of the guitar
    me = body.data
    me.calc_loop_triangles()
    mw = body.matrix_world
    pts = [mw @ v.co for v in me.vertices]
    x0, z0 = min(p.x for p in pts), min(p.z for p in pts)
    nx = int((max(p.x for p in pts) - x0) / CELL) + 1
    nz = int((max(p.z for p in pts) - z0) / CELL) + 1
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

    strap = _verts(parts["Button_01"])
    return dict(nut_z=nut_z, scale=scale, frets=frets, centre=centre, nut_spread=nut_spread, saddles=saddles,
                string_y=string_y, board_y=board_y, top_y=top_y, back_y=back_y, width=width, back=back,
                posts=post_pts, grid=(x0, z0, nx, nz, grid),
                strap=sum(strap, Vector()) / len(strap))


# ------------------------------------------------------------------ materials
def _bsdf(m):
    return next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")


def _mat(name, color, rough=0.4, metal=0.0, clearcoat=0.0):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    b = _bsdf(m)
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    b.inputs["Coat Weight"].default_value = clearcoat
    m.diffuse_color = (*color, 1)
    return m


def _textured(name, image, bump=None, rough=0.4):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    nt = m.node_tree
    b = _bsdf(m)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(os.path.join(MODEL_DIR, image), check_existing=True)
    nt.links.new(tex.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = rough
    if bump:
        bt = nt.nodes.new("ShaderNodeTexImage")
        bt.image = bpy.data.images.load(os.path.join(MODEL_DIR, bump), check_existing=True)
        bt.image.colorspace_settings.name = "Non-Color"
        bn = nt.nodes.new("ShaderNodeBump")
        bn.inputs["Strength"].default_value = 0.15
        nt.links.new(bt.outputs["Color"], bn.inputs["Height"])
        nt.links.new(bn.outputs["Normal"], b.inputs["Normal"])
    return m


def _sunburst(body):
    """Three-tone sunburst: a radial gradient across the face of the body (local coordinates)."""
    m = bpy.data.materials.new("strat_sunburst")
    nt = m.node_tree
    b = _bsdf(m)
    lo = Vector([min(c[i] for c in body.bound_box) for i in range(3)])
    hi = Vector([max(c[i] for c in body.bound_box) for i in range(3)])
    size = hi - lo
    thin = min(range(3), key=lambda i: size[i])  # the thickness axis does not affect the burst
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    sc = Vector([0.0 if i == thin else 2.0 / size[i] for i in range(3)])
    mp.inputs["Scale"].default_value = sc
    mp.inputs["Location"].default_value = Vector([-(lo[i] + hi[i]) / 2 * sc[i] for i in range(3)])
    gr = nt.nodes.new("ShaderNodeTexGradient")
    gr.gradient_type = "SPHERICAL"
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    els = ramp.color_ramp.elements
    els[0].position, els[0].color = 0.0, (0.015, 0.008, 0.004, 1)
    els[1].position, els[1].color = 0.6, (0.82, 0.5, 0.1, 1)
    mid = els.new(0.28)
    mid.color = (0.32, 0.05, 0.015, 1)
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], gr.inputs["Vector"])
    nt.links.new(gr.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.18
    b.inputs["Coat Weight"].default_value = 1.0
    m.diffuse_color = (0.6, 0.35, 0.08, 1)
    return m


FINISHES = {"red": (0.55, 0.03, 0.025), "black": (0.012, 0.012, 0.014), "white": (0.85, 0.84, 0.8)}
PLASTIC = ("Battipenna", "Magnete", "Knob", "pomolo")


def _dress(parts, finish):
    chrome = _mat("strat_chrome", (0.85, 0.85, 0.87), 0.12, 1.0)
    for name, o in parts.items():
        if name == "Body":
            m = _sunburst(o) if finish == "sunburst" else _mat("strat_body_" + finish, FINISHES[finish], 0.18, 0.0, 1.0)
        elif name == "Neck":
            m = _textured("strat_neck", "Manico UVW_Dif.png", "Manico UVW_Bump.png", 0.35)
        elif name == "tastiera":
            m = _textured("strat_fretboard", "tastiera.jpg", rough=0.6)
        elif name == "Tasti":
            m = _mat("strat_frets", (0.78, 0.76, 0.7), 0.22, 1.0)
        elif name == "Capotasto":
            m = _mat("strat_nut", (0.9, 0.87, 0.78), 0.4)
        elif name == "Battipenna":
            m = _mat("strat_pickguard", (0.86, 0.87, 0.8), 0.3)
        elif name.startswith(PLASTIC):
            m = _mat("strat_plastic", (0.88, 0.88, 0.82), 0.35)
        else:
            m = chrome
        o.data.materials.clear()
        o.data.materials.append(m)


# ---------------------------------------------------------------------- place
def _prepare(finish):
    """Import, measure and dress the model (in model space; cached as a .blend library)."""
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=OBJ_PATH)
    parts = {o.name.split(".")[0]: o for o in bpy.data.objects if o not in before and o.type == "MESH"}
    for o in parts.values():
        # the 3ds Max export carries broken stored normals (flat plates smoothed into their edges):
        # drop them and let Blender shade by angle, keeping edges over 35 deg sharp
        if o.data.has_custom_normals:
            with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o],
                                           selected_editable_objects=[o]):
                bpy.ops.mesh.customdata_custom_splitnormals_clear()
        o.data.shade_smooth()
        o.data.set_sharp_from_angle(angle=math.radians(35))
    bpy.context.view_layer.update()
    ms = _measure(parts)
    _dress(parts, finish)
    return parts, ms


def strat_guitar(rig, c, xdir, zdir, finish, coll):
    """Import the Stratocaster into `coll` and return the guitar frame + playing geometry (same keys as the
    procedural guitar). c, xdir, zdir: body centre and orientation in the player's rig frame."""
    import cache
    parts, ms = cache.model("strat_" + finish, [OBJ_PATH, OBJ_PATH[:-4] + ".mtl"], ["strat.py"], _prepare, finish)
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
    root = bpy.data.objects.new("Stratocaster_" + finish, None)
    coll.objects.link(root)
    for o in parts.values():
        o.parent = root
    root.matrix_world = (Matrix.Translation(c_world) @ (B @ AXES).to_4x4() @ Matrix.Scale(0.01, 4)
                         @ Matrix.Translation(-ms["centre"]))
    bpy.context.view_layer.update()

    C = ms["centre"]

    def gl(p):  # model cm -> guitar frame m
        return AXES @ (Vector(p) - C) * 0.01

    def W(p):
        return c_world + B @ Vector(p)

    nut_x = (ms["nut_z"] - C.z) / 100
    scale = ms["scale"] / 100
    bridge_x = nut_x - scale
    string_z = -(ms["string_y"] - C.y) / 100
    board_z = -(ms["board_y"] - C.y) / 100
    top = -(ms["top_y"] - C.y) / 100
    # string 0 = high E (-y side) ... 5 = low E; spacing at the saddles and at the nut
    y_bridge = [-(x - C.x) / 100 for x in reversed(ms["saddles"])]
    y_nut = [(i - 2.5) / 5 * ms["nut_spread"] / 100 for i in range(6)]
    width = [((z - C.z) / 100, w / 100) for z, w in ms["width"]]
    back = [((z - C.z) / 100, -(y - C.y) / 100) for z, y in ms["back"]]

    def fret_x(n):
        return nut_x - scale * (1 - 2 ** (-n / 12))

    def string_y(i, x):
        t = (nut_x - x) / scale
        return y_nut[i] + (y_bridge[i] - y_nut[i]) * t

    def fret_point(string, fret, lift=0.0):
        """Fingertip target just behind a fret; string 0 is the highest (thinnest) one."""
        x = fret_x(fret) + 0.3 * (fret_x(fret - 1) - fret_x(fret))
        return W((x, string_y(string, x), string_z + 0.005 + lift))

    x0, z0, nx, nz, grid = ms["grid"]

    def body_inside(x, y):
        mx, mz = -y * 100 + C.x, x * 100 + C.z
        ix, iz = int((mx - x0) / CELL), int((mz - z0) / CELL)
        return 0 <= ix < nx and 0 <= iz < nz and bool(grid[iz * nx + ix])

    # strings: saddle -> nut -> tuner post
    paths = []
    for i in range(6):
        post = gl(ms["posts"][5 - i])
        paths.append([W((bridge_x, y_bridge[i], string_z)), W((nut_x, y_nut[i], string_z)), W(post)])

    return {"W": W, "X": rig.R @ X, "Y": rig.R @ Y, "Z": rig.R @ Z, "fret_x": fret_x, "fret_point": fret_point,
            "top": top, "bridge_x": bridge_x, "nut_x": nut_x, "scale": scale, "strings": y_bridge,
            "half_w": lambda x: _interp(width, x), "board_z": board_z, "back_z": lambda x: _interp(back, x),
            "string_z": lambda x: string_z, "body_inside": body_inside,
            "thickness": (ms["back_y"] - ms["top_y"]) / 100, "neck_w": 2 * _interp(width, fret_x(3)),
            "strap_top": W(gl(ms["strap"])), "string_paths": paths,
            "measured": dict(scale_cm=round(ms["scale"], 2), frets=len(ms["frets"]),
                             nut_width_cm=round(2 * width[-1][1] * 100, 2) if width else None,
                             neck_depth_cm=round((board_z - _interp(back, fret_x(3))) * 100, 2))}
