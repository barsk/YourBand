"""Sonor drum kit (CAD assembly exported to OBJ, model_images/drum-set) with its throne, for the drummer.

The OBJ has 152 unnamed parts ("Body1:NN") and no appearances. Parts get materials by role: black
lacquered shells, coated heads, a black front head on the kick, bronze cymbals, a leather throne seat,
wooden sticks and chrome for all other hardware. The snare wires (10M triangles, hidden under the snare)
are dropped. Playing points are measured on the parts themselves: the strike points on the heads and
cymbals, the footboards of the kick and hi-hat pedals (by ray casting), and the throne's seat.

For the animation the moving parts are separated and measured: the kick pedal (one solid in the model)
is split into its footboard, its beater and the static frame, with the beater's axle and the angle at
which it meets the kick's batter head; the hi-hat pedal's footboard is split off; the gap between the
hi-hat cymbals gives how far the top one drops when the pedal closes it (the model shows it open); and
each cymbal that gets hit has a pivot and an axis to swing about.

The throne in the model sits dead centre behind the kick pedal, which leaves the hi-hat out of reach;
like a real drummer, it is moved left between the two pedals.
"""
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

from strat import _mat, _verts
from clapton import _bbox, _bvh

HERE = os.path.dirname(os.path.abspath(__file__))
OBJ_PATH = os.path.normpath(os.path.join(HERE, "..", "model_images", "drum-set", "drum-set.obj"))
# model space (mm): z up, the drummer faces -y. Kit frame (the drummer's rig): x to the right, y forward.
AXES = Matrix(((-1, 0, 0), (0, -1, 0), (0, 0, 1)))
SEAT_SHARE = 0.3  # throne x between the kick pedal (0) and the hi-hat pedal (1)
MAX_FACES = 40000
# Fusion's part names, by role
SHELLS = ("Body1", "Body1:125", "Body1:67", "Body1:37", "Body1:7")
HEADS = ("Body1:1", "Body1:129", "Body1:130", "Body1:68", "Body1:95", "Body1:38", "Body1:65", "Body1:8",
         "Body1:35")
KICK_FRONT = "Body1:4"
CYMBALS = ("Body1:105", "Body1:111", "Body1:119", "Body1:143", "Body1:146")
STICKS = ("Body1:149", "Body1:151")
THRONE = ("Body1:123", "Body1:124")
SEAT, SNARE_WIRES = "Body1:124", "Body1:128"
KICK_PEDAL, HIHAT_PEDAL, HIHAT_STAND = "Body1:148", "Body1:141", "Body1:142"
# stands a drummer adjusts: the snare (drum and stand), and the hi-hat (stand and pedal; rod, cymbals and
# clutch go up and down)
SNARE_PARTS = ("Body1:125", "Body1:126", "Body1:127", "Body1:129", "Body1:130", "Body1:131", "Body1:132",
               "Body1:133", "Body1:134", "Body1:135", "Body1:136", "Body1:137", "Body1:138")
HIHAT_BASE = ("Body1:139", "Body1:140", "Body1:141", "Body1:144", "Body1:145", "hihat_board")
HIHAT_UPPER = ("Body1:142", "Body1:143", "Body1:146", "Body1:147")
CRASH_UPPER = ("Body1:119", "Body1:120", "Body1:121", "Body1:122")  # cymbal, boom arm and its tube, nut
CRASH_BASE = ("Body1:115", "Body1:116", "Body1:117", "Body1:118")  # its tripod
TOM_LEFT = ("Body1:7", "Body1:8", "Body1:9", "Body1:35", "Body1:36", "Body1:34") + tuple(
    "Body1:%d" % i for i in range(10, 34))  # shell, heads, rims, lugs and its arm
TOM_LEFT_POST = "Body1:6"  # the arm's post in the holder on the kick: it extends when the tom goes up
SNARE_KEEP_CLEAR = ("Body1:7", "Body1:8", "Body1:9", "Body1:35", "Body1:36", "Body1:34",  # left rack tom
                    "Body1", "Body1:2", "Body1:5", "Body1:139", "Body1:143")  # kick, hi-hat stand and cymbal
KICK_BATTER, HIHAT_BOTTOM, HIHAT_TOP, HIHAT_CLUTCH, CRASH = "Body1:1", "Body1:143", "Body1:146", "Body1:147", "Body1:119"
KICK_BEATER_Z = 152.0  # mm: above the kick pedal's cam everything is beater (shaft and head)
BOARD_TRAVEL = math.radians(12)  # footboard rotation between rest (up) and pressed
UP = Vector((0, 0, 1))
STRIKE = {  # surfaces the sticks play: (part, how far out from the centre towards the drummer, 0..1)
    "snare": ("Body1:130", 0.25), "hihat": ("Body1:146", 0.75), "floor_tom": ("Body1:95", 0.3),
    "tom1": ("Body1:65", 0.3), "tom2": ("Body1:35", 0.3), "ride": ("Body1:111", 0.65),
    "crash": ("Body1:119", 0.75)}


def _materials():
    return {
        "shell": _mat("drum_shell", (0.012, 0.012, 0.014), 0.15, 0.0, 1.0),
        "head": _mat("drum_head", (0.86, 0.85, 0.8), 0.55),
        "kick_front": _mat("drum_kick_front", (0.02, 0.02, 0.022), 0.35),
        "cymbal": _mat("drum_cymbal", (0.78, 0.57, 0.28), 0.28, 1.0),
        "stick": _mat("drum_stick", (0.82, 0.66, 0.43), 0.45),
        "leather": _mat("drum_seat", (0.02, 0.018, 0.018), 0.5),
        "chrome": _mat("drum_chrome", (0.85, 0.85, 0.87), 0.12, 1.0),
    }


def _dress(parts):
    mats = _materials()
    for name, o in parts.items():
        me = o.data
        me.materials.clear()
        if name == SEAT:  # one part: leather cushion on a chrome column
            me.materials.append(mats["leather"])
            me.materials.append(mats["chrome"])
            mw = o.matrix_world
            top = max((mw @ v.co).z for v in me.vertices)
            for p in me.polygons:
                p.material_index = 0 if (mw @ p.center).z > top - 90 else 1
            continue
        role = ("shell" if name in SHELLS else "head" if name in HEADS else "kick_front" if name == KICK_FRONT
                else "cymbal" if name in CYMBALS else "stick" if name in STICKS else "chrome")
        me.materials.append(mats[role])


def _lighten(parts):
    """Weld CAD seams, decimate the few very dense parts, and weight normals by face area (CAD slivers)."""
    for o in parts.values():
        me = o.data
        me.shade_smooth()
        me.set_sharp_from_angle(angle=math.radians(35))
        o.modifiers.new("weld", "WELD").merge_threshold = 0.01
        if len(me.polygons) > MAX_FACES:
            o.modifiers.new("decimate", "DECIMATE").ratio = MAX_FACES / len(me.polygons)
        wn = o.modifiers.new("normals", "WEIGHTED_NORMAL")
        wn.mode, wn.keep_sharp, wn.weight = "FACE_AREA", True, 100
    dg = bpy.context.evaluated_depsgraph_get()
    for o in parts.values():
        old = o.data
        o.data = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
        o.modifiers.clear()
        bpy.data.meshes.remove(old)


def _footboard(o, seat):
    """Heel and ball-of-foot points on a pedal's footboard: top-surface hits of a grid of downward rays,
    their main direction, and the end nearest the seat as the heel."""
    lo, hi = _bbox(o)
    bvh = _bvh([o])
    pts = []
    x = lo.x
    while x <= hi.x:
        y = lo.y
        while y <= hi.y:
            h = bvh.ray_cast(Vector((x, y, hi.z + 10)), Vector((0, 0, -1)))
            if h[0] and h[0].z < 90:  # the plate, not the beater post or the stand
                pts.append(h[0])
            y += 8
        x += 8
    c = sum(pts, Vector()) / len(pts)
    # main horizontal direction of the hits (2x2 covariance, largest eigenvector)
    sxx = sum((p.x - c.x) ** 2 for p in pts)
    syy = sum((p.y - c.y) ** 2 for p in pts)
    sxy = sum((p.x - c.x) * (p.y - c.y) for p in pts)
    ang = 0.5 * math.atan2(2 * sxy, sxx - syy)
    d = Vector((math.cos(ang), math.sin(ang), 0))
    if d.dot(Vector((seat.x - c.x, seat.y - c.y, 0))) < 0:
        d = -d  # towards the seat
    heel = max(pts, key=lambda p: (p - c).dot(d))
    ball_at = heel - d * 160
    ball = min(pts, key=lambda p: (p.xy - ball_at.xy).length)
    return heel, ball


def _split(o, select, name):
    """Move the faces whose world-space centre passes select() into a new object."""
    mw = o.matrix_world
    sel = [select(mw @ p.center) for p in o.data.polygons]
    new = o.copy()
    new.data = o.data.copy()
    new.name = name
    for c in o.users_collection:
        c.objects.link(new)
    for obj, keep in ((o, [not x for x in sel]), (new, sel)):
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[f for f, k in zip(bm.faces, keep) if not k], context="FACES")
        bm.to_mesh(obj.data)
        bm.free()
    return new


def _plate(o, seat, zmax):
    """A pedal's footboard: hits of downward rays on its top face (a plane fitted through them), its heel
    end (nearest the seat) and toe end, the direction from heel to toe, and a selector for its faces."""
    lo, hi = _bbox(o)
    bvh = _bvh([o])
    hits = []
    x = lo.x
    while x <= hi.x:
        y = lo.y
        while y <= hi.y:
            h = bvh.ray_cast(Vector((x, y, hi.z + 10)), Vector((0, 0, -1)))
            if h[0] and 12 < h[0].z < zmax:  # above the base and the floor rods, below beater or stand
                hits.append(h[0])
            y += 6
        x += 6
    for _ in range(3):  # plane z = a + b x + c y through the hits; keep those on it
        coef = np.linalg.lstsq(np.array([(1, q.x, q.y) for q in hits]), np.array([q.z for q in hits]),
                               rcond=None)[0]
        hits = [q for q in hits if abs(q.z - (coef[0] + coef[1] * q.x + coef[2] * q.y)) < 6]
    c = sum(hits, Vector()) / len(hits)
    sxx = sum((q.x - c.x) ** 2 for q in hits)
    syy = sum((q.y - c.y) ** 2 for q in hits)
    sxy = sum((q.x - c.x) * (q.y - c.y) for q in hits)
    ang = 0.5 * math.atan2(2 * sxy, sxx - syy)
    d = Vector((math.cos(ang), math.sin(ang), 0))
    if d.dot(Vector((seat.x - c.x, seat.y - c.y, 0))) > 0:
        d = -d  # from the heel (seat side) towards the toe
    heel = min(hits, key=lambda q: (q - c).dot(d))
    toe = max(hits, key=lambda q: (q - c).dot(d))
    kd = KDTree(len(hits))
    for i, q in enumerate(hits):
        kd.insert(Vector((q.x, q.y, 0)), i)
    kd.balance()

    def select(f):  # within the plate's thickness under its top face, inside its footprint
        dz = f.z - (coef[0] + coef[1] * f.x + coef[2] * f.y)
        return -14 < dz < 3 and kd.find(Vector((f.x, f.y, 0)))[2] < 8

    return heel, toe, d, select


def _rotation_sign(points, pivot, axis, better):
    """+1 or -1: the direction of rotation about axis that makes better(rotated points) grow."""
    R = Matrix.Rotation(0.02, 3, axis)
    return 1 if better([pivot + R @ (q - pivot) for q in points]) > better(points) else -1


def _disc(o):
    """Pivot (top of the bell, on the stand's rod) and normal of a cymbal."""
    vs = _verts(o)
    a = np.array([tuple(v) for v in vs])
    c = a.mean(axis=0)
    w, v = np.linalg.eigh(np.cov((a - c).T))
    n = Vector(v[:, 0])
    n = n if n.z > 0 else -n
    c = Vector(c)
    near = [q for q in vs if ((q - c) - n * (q - c).dot(n)).length < 25]
    return c + n * max((q - c).dot(n) for q in near), n


def _stick(o):
    """Butt and tip of a drumstick: the ends of its long axis; the tip is the thinner end."""
    vs = _verts(o)
    c = sum(vs, Vector()) / len(vs)
    a = max(vs, key=lambda v: (v - c).length)  # one end, then the farthest point from it
    b = max(vs, key=lambda v: (v - a).length)
    d = (a - b).normalized()

    def girth(end):
        near = [v for v in vs if abs((v - end).dot(d)) < 25]
        mid = sum(near, Vector()) / len(near)
        return sum(((v - mid) - d * (v - mid).dot(d)).length for v in near) / len(near)

    return (b, a) if girth(a) < girth(b) else (a, b)


MEASURED = ("origin", "seat", "throne", "kick_heel", "kick_ball", "hh_heel", "hh_ball", "strike", "sticks", "axle",
            "k_hinge", "axis", "beater_hit", "k_travel", "h_hinge", "h_axis", "h_travel", "drop", "swing")


def _prepare():
    """Import the kit, split its moving parts and measure them, then weld, decimate and dress it - all in
    model space (mm), before it is placed (cached as a .blend library)."""
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=OBJ_PATH, use_split_objects=True, use_split_groups=True)
    parts = {o.name: o for o in bpy.data.objects if o not in before and o.type == "MESH"}
    bpy.data.objects.remove(parts.pop(SNARE_WIRES))

    # the throne goes between the pedals; the kit frame's origin is its centre on the floor
    slo, shi = _bbox(parts[SEAT])
    throne = (slo + shi) / 2
    kick_heel, kick_ball = _footboard(parts[KICK_PEDAL], throne)
    hh_heel, hh_ball = _footboard(parts[HIHAT_PEDAL], throne)
    seat_x = kick_heel.x + SEAT_SHARE * (hh_heel.x - kick_heel.x)
    shift = Matrix.Translation((seat_x - throne.x, 0, 0))
    for n in THRONE:
        parts[n].matrix_world = shift @ parts[n].matrix_world
    seat = Vector((seat_x, throne.y, shi.z))
    origin = Vector((seat.x, seat.y, 0))

    strike = {}
    for key, (name, out) in STRIKE.items():
        lo, hi = _bbox(parts[name])
        c = (lo + hi) / 2
        toward = Vector((seat.x - c.x, seat.y - c.y, 0)).normalized()
        p = c + toward * out * (hi.x - lo.x) / 2
        h = _bvh([parts[name]]).ray_cast(Vector((p.x, p.y, hi.z + 50)), Vector((0, 0, -1)))
        strike[key] = h[0] if h[0] else Vector((p.x, p.y, hi.z))
    sticks = [(parts[n], *_stick(parts[n])) for n in STICKS]

    # --- moving parts. Kick pedal: beater (above the cam), footboard, static frame
    kp = parts[KICK_PEDAL]
    k_heel, k_toe, k_d, k_select = _plate(kp, throne, 120)
    beater = parts["kick_beater"] = _split(kp, lambda f: f.z > KICK_BEATER_Z, "kick_beater")
    kboard = parts["kick_board"] = _split(kp, k_select, "kick_board")
    axis = k_d.cross(UP).normalized()  # pedal hinges run across the pedal
    cam = [v for v in _verts(kp) if 85 < v.z < 160 and (v - k_toe).dot(k_d) > -30]
    clo = Vector([min(v[i] for v in cam) for i in range(3)])
    chi = Vector([max(v[i] for v in cam) for i in range(3)])
    centre = (k_heel + k_toe) / 2
    axle = Vector((centre.x, (clo.y + chi.y) / 2, (clo.z + chi.z) / 2))
    bv = _verts(beater)
    batter = min(v.dot(k_d) for v in _verts(parts[KICK_BATTER]))  # the head's face towards the pedal
    reach = lambda pts: max(q.dot(k_d) for q in pts)
    sgn = _rotation_sign(bv, axle, axis, reach)
    lo_a, hi_a = 0.0, 1.5
    for _ in range(30):  # beater angle where it meets the head (1 mm short)
        mid = (lo_a + hi_a) / 2
        R = Matrix.Rotation(sgn * mid, 3, axis)
        if reach([axle + R @ (v - axle) for v in bv]) < batter - 1:
            lo_a = mid
        else:
            hi_a = mid
    beater_hit = sgn * lo_a
    k_hinge = k_heel - UP * 5
    k_travel = BOARD_TRAVEL * _rotation_sign([k_toe], k_hinge, axis, lambda pts: -pts[0].z)
    # hi-hat pedal footboard, and how far the top cymbal drops onto the bottom one when it closes
    h_heel, h_toe, h_d, h_select = _plate(parts[HIHAT_PEDAL], throne, 170)
    hboard = parts["hihat_board"] = _split(parts[HIHAT_PEDAL], h_select, "hihat_board")
    h_axis = h_d.cross(UP).normalized()
    h_hinge = h_heel - UP * 5
    h_travel = BOARD_TRAVEL * _rotation_sign([h_toe], h_hinge, h_axis, lambda pts: -pts[0].z)
    bottom = _bvh([parts[HIHAT_BOTTOM]])
    gaps = []
    for q in _verts(parts[HIHAT_TOP]):
        h = bottom.ray_cast(q, -UP)
        if h[0]:
            gaps.append(q.z - h[0].z)
    drop = min(gaps) - 1.0
    swing = {"crash": (parts[CRASH], *_disc(parts[CRASH])), "hihat": (parts[HIHAT_TOP], *_disc(parts[HIHAT_TOP]))}

    _lighten(parts)
    _dress(parts)
    return parts, {k: v for k, v in zip(MEASURED, (
        origin, seat, throne, kick_heel, kick_ball, hh_heel, hh_ball, strike, sticks, axle, k_hinge, axis,
        beater_hit, k_travel, h_hinge, h_axis, h_travel, drop, swing))}


def drum_kit(rig, coll):
    """Import the kit into `coll`, with the throne at the drummer's rig origin. Returns the kit frame and
    the playing points in world space."""
    import cache
    parts, m = cache.model("drumkit", [OBJ_PATH, OBJ_PATH[:-4] + ".mtl"], ["drumkit.py", "clapton.py", "strat.py"],
                           _prepare)
    (origin, seat, throne, kick_heel, kick_ball, hh_heel, hh_ball, strike, sticks, axle, k_hinge, axis,
     beater_hit, k_travel, h_hinge, h_axis, h_travel, drop, swing) = (m[k] for k in MEASURED)
    beater, kboard, hboard = parts["kick_beater"], parts["kick_board"], parts["hihat_board"]
    root = bpy.data.objects.new("DrumKit_Sonor", None)
    coll.objects.link(root)
    for o in parts.values():
        for col in list(o.users_collection):
            col.objects.unlink(o)
        coll.objects.link(o)
        o.parent = root
    B = rig.R @ AXES
    root.matrix_world = (Matrix.Translation(rig.w(Vector())) @ B.to_4x4() @ Matrix.Scale(0.001, 4)
                         @ Matrix.Translation(-origin))
    bpy.context.view_layer.update()

    def W(p):  # model mm -> world
        return rig.w(AXES @ (Vector(p) - origin) * 0.001)

    def D(v):  # model direction -> world
        return (B @ Vector(v)).normalized()

    moving = [beater, kboard, hboard, parts[HIHAT_TOP], parts[HIHAT_CLUTCH], parts[CRASH]]
    for o in moving:
        o.rotation_mode = "QUATERNION"
    return {"kick": {"beater": beater, "board": kboard, "axle": W(axle), "hinge": W(k_hinge), "axis": D(axis),
                     "hit": beater_hit, "travel": k_travel},
            "hihat": {"top": [parts[HIHAT_TOP], parts[HIHAT_CLUTCH]], "board": hboard, "hinge": W(h_hinge),
                      "axis": D(h_axis), "travel": h_travel, "drop": drop * 0.001},
            "swing": {k: (o, W(q), D(n)) for k, (o, q, n) in swing.items()},
            "rest": {o.name: o.matrix_world.copy() for o in moving},
            "throne": [parts[n] for n in THRONE], "parts": parts,
            "seat": W(seat), "kick_heel": W(kick_heel), "kick_ball": W(kick_ball), "hihat_heel": W(hh_heel),
            "hihat_ball": W(hh_ball), "strike": {k: W(p) for k, p in strike.items()},
            "sticks": [(o, W(butt), W(tip)) for o, butt, tip in sticks],
            "measured": dict(seat_mm=round(seat.z), snare_mm=round(strike["snare"].z),
                             hihat_mm=round(strike["hihat"].z), beater_deg=round(math.degrees(beater_hit), 1),
                             hihat_gap_mm=round(drop + 1.0, 1), parts=len(parts),
                             faces=sum(len(o.data.polygons) for o in parts.values()))}


def snare_room(kit, wanted, steps=12):
    """The snare move closest to `wanted` that keeps the snare clear of the rack tom, the kick and the hi-hat
    (it may also shift sideways, away from the tom)."""
    parts = kit["parts"]
    clear = [_bvh([parts[n]]) for n in SNARE_KEEP_CLEAR]
    verts, polys = [], []
    for n in SNARE_PARTS:
        o = parts[n]
        k = len(verts)
        verts += _verts(o)
        polys += [[k + i for i in q.vertices] for q in o.data.polygons]
    side = UP.cross(wanted).normalized() if wanted.length > 1e-6 else Vector((1, 0, 0))
    best = None
    for i in range(steps + 1):
        for lat in (0.0, -0.03, 0.03, -0.06, 0.06, -0.09, 0.09):
            d = wanted * (i / steps) + side * lat
            miss = (wanted - d).length
            if best is not None and miss >= best[0]:
                continue
            moved = BVHTree.FromPolygons([v + d for v in verts], polys)
            if not any(moved.overlap(c) for c in clear):
                best = (miss, d)
    return best[1] if best else Vector()


def tom_room(kit, snare, step=0.01, most=0.3):
    """How far the left rack tom has to go up so that the snare, moved by `snare`, fits under it."""
    parts = kit["parts"]
    verts, polys = [], []
    for n in SNARE_PARTS:
        k = len(verts)
        verts += [v + snare for v in _verts(parts[n])]
        polys += [[k + i for i in q.vertices] for q in parts[n].data.polygons]
    moved = BVHTree.FromPolygons(verts, polys)
    tom_v, tom_p = [], []
    for n in TOM_LEFT:
        k = len(tom_v)
        tom_v += _verts(parts[n])
        tom_p += [[k + i for i in q.vertices] for q in parts[n].data.polygons]
    dz = 0.0
    while dz <= most:
        if not BVHTree.FromPolygons([v + UP * dz for v in tom_v], tom_p).overlap(moved):
            return dz
        dz += step
    return None


def raise_tom(kit, dz):
    """Raise the left rack tom; its post stretches from the holder up to the arm."""
    parts = kit["parts"]
    for n in TOM_LEFT:
        parts[n].matrix_world = Matrix.Translation(UP * dz) @ parts[n].matrix_world
    post = parts[TOM_LEFT_POST]
    lo, hi = _bbox(post)
    k = (hi.z - lo.z + dz) / (hi.z - lo.z)
    post.matrix_world = (Matrix.Translation((0, 0, lo.z)) @ Matrix.Diagonal((1, 1, k, 1))
                         @ Matrix.Translation((0, 0, -lo.z)) @ post.matrix_world)
    kit["strike"]["tom2"] = kit["strike"]["tom2"] + UP * dz
    bpy.context.view_layer.update()


def adjust(kit, snare=Vector(), hihat=Vector(), hihat_raise=0.0, crash=Vector(), crash_raise=0.0):
    """Set the kit up for a player: move the snare (drum and stand) by `snare`, the hi-hat stand along the
    floor by `hihat` and its cymbals up by `hihat_raise`, and the crash up on its boom stand by
    `crash_raise` after moving its whole stand along the floor by `crash` (world, metres). Keeps the
    kit's measured points."""
    parts = kit["parts"]
    up = hihat + Vector((0, 0, hihat_raise))
    crash_base = Vector((crash.x, crash.y, 0))
    crash = crash_base + Vector((0, 0, crash_raise))
    for names, d in ((SNARE_PARTS, snare), (HIHAT_BASE, hihat), (HIHAT_UPPER, up), (CRASH_UPPER, crash),
                     (CRASH_BASE, crash_base)):
        for n in names:
            o = parts[n]
            o.matrix_world = Matrix.Translation(d) @ o.matrix_world
            if o.name in kit["rest"]:
                kit["rest"][o.name] = Matrix.Translation(d) @ kit["rest"][o.name]
    bpy.context.view_layer.update()
    kit["strike"]["snare"] = kit["strike"]["snare"] + snare
    kit["strike"]["hihat"] = kit["strike"]["hihat"] + up
    kit["hihat_heel"], kit["hihat_ball"] = kit["hihat_heel"] + hihat, kit["hihat_ball"] + hihat
    kit["hihat"]["hinge"] = kit["hihat"]["hinge"] + hihat
    o, pivot, n = kit["swing"]["hihat"]
    kit["swing"]["hihat"] = (o, pivot + up, n)
    kit["strike"]["crash"] = kit["strike"]["crash"] + crash
    o, pivot, n = kit["swing"]["crash"]
    kit["swing"]["crash"] = (o, pivot + crash, n)
