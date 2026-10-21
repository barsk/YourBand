r"""Mockup of the VR band: a virtual band playing in your living room (Quest 3 MR).

Builds the scene (room, four Rocketbox musicians posed down to the fingers on
real fret/key/stick targets, you with your guitar and headset, a floating song
HUD), animates the whole band through the G-Em-C-D progression (or a MIDI song),
registers every camera, saves it all as a .blend and renders from it (render.py).

Build once, render many: the build is the slow part, so render later from the .blend:
  & "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background ^
      --python mockup\build_vr_band_scene.py -- mockup\renders [what] [quality] [options]
  & "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background mockup\vr_band_mockup.blend ^
      --python mockup\render.py -- mockup\renders [what] [quality] [options]
what: the three stills by default, or `debug` (hand close-ups), `portraits` (faces and clothes), `anim`
(videos; `videos=a,b` picks some), `only=a,b`; `norender` just builds and saves. `chords` renders the
chord library (re-poses the guitarist; not saved). quality: `draft` (default: EEVEE without ray tracing),
`final` (Cycles stills, ray-traced EEVEE videos), `preview` (half size). Options: `frame=N` (stills),
`step=N` (videos), `song=x.mid bars=a-b` (a MIDI song; saved as vr_band_<song>_<a>-<b>.blend),
`blend=path`, `nocache` (re-solve everything instead of using mockup/cache).
"""
import json
import math
import os
import random
import sys
import time

import bpy
from bpy_extras.anim_utils import action_get_channelbag_for_slot
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.path.abspath("renders")
PREVIEW = "preview" in argv
os.makedirs(OUT, exist_ok=True)
random.seed(7)

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene


# ---------------------------------------------------------------- collections
def new_coll(name):
    c = bpy.data.collections.new(name)
    scene.collection.children.link(c)
    return c


C_ROOM, C_BAND, C_USER, C_HUD, C_LABELS, C_LIGHTS = (
    new_coll(n) for n in ("Room", "Band", "User", "HUD", "Labels", "Lights"))


def link(o, coll):
    for c in list(o.users_collection):
        c.objects.unlink(o)
    coll.objects.link(o)
    return o


# ------------------------------------------------------------------ materials
_mats = {}


def M(name, color, rough=0.5, metal=0.0, emit=None, strength=0.0):
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.new(name)
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = strength
    m.diffuse_color = (*color, 1)
    _mats[name] = m
    return m


def E(name, color, strength=1.0, alpha=1.0):
    """Emission-only material (HUD, glow), optionally see-through."""
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.new(name)
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (*color, 1)
    em.inputs["Strength"].default_value = strength
    if alpha < 1.0:
        tr = nt.nodes.new("ShaderNodeBsdfTransparent")
        mix = nt.nodes.new("ShaderNodeMixShader")
        mix.inputs["Fac"].default_value = alpha
        nt.links.new(tr.outputs[0], mix.inputs[1])
        nt.links.new(em.outputs[0], mix.inputs[2])
        nt.links.new(mix.outputs[0], out.inputs["Surface"])
        m.surface_render_method = "BLENDED"
        m.use_transparent_shadow = True
    else:
        nt.links.new(em.outputs[0], out.inputs["Surface"])
    m.diffuse_color = (*color, 1)
    _mats[name] = m
    return m


def wood_floor():
    m = bpy.data.materials.new("wood_floor")
    nt = m.node_tree
    b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    br = nt.nodes.new("ShaderNodeTexBrick")
    br.inputs["Color1"].default_value = (0.42, 0.26, 0.14, 1)
    br.inputs["Color2"].default_value = (0.52, 0.34, 0.19, 1)
    br.inputs["Mortar"].default_value = (0.2, 0.12, 0.06, 1)
    br.inputs["Scale"].default_value = 1.0
    br.inputs["Mortar Size"].default_value = 0.004
    br.inputs["Brick Width"].default_value = 1.4
    br.inputs["Row Height"].default_value = 0.19
    nt.links.new(tc.outputs["Object"], br.inputs["Vector"])
    nt.links.new(br.outputs["Color"], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = 0.42
    m.diffuse_color = (0.47, 0.3, 0.16, 1)
    return m


CHROME = M("chrome", (0.82, 0.82, 0.84), 0.15, 1.0)
BLACK = M("black_plastic", (0.02, 0.02, 0.022), 0.4)
LEATHER = M("strap", (0.018, 0.012, 0.008), 0.85)
MAPLE = M("maple", (0.72, 0.52, 0.28), 0.4)
ROSEWOOD = M("rosewood", (0.13, 0.06, 0.035), 0.5)
GLOW = E("mr_glow", (0.25, 0.85, 1.0), 6.0)
GLOW_FILL = E("mr_glow_fill", (0.25, 0.85, 1.0), 0.6, alpha=0.12)


# ----------------------------------------------------------------- primitives
def _finish(o, mat, coll, smooth=True):
    if mat:
        o.data.materials.append(mat)
    if smooth and o.type == "MESH":
        o.data.shade_smooth()
        o.data.set_sharp_from_angle(angle=math.radians(40))
    return link(o, coll)


def _orient(o, quat):
    if quat is not None:
        o.rotation_mode = "QUATERNION"
        o.rotation_quaternion = quat


def ball(c, r, mat, coll, scale=None, quat=None, segs=24, rings=12):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=rings, radius=r, location=c)
    o = bpy.context.active_object
    if scale:
        o.scale = scale
    _orient(o, quat)
    return _finish(o, mat, coll)


def ell(c, semi_axes, quat, mat, coll):
    return ball(c, 1.0, mat, coll, scale=semi_axes, quat=quat)


def seg(p1, p2, r, mat, coll, caps=True, verts=16):
    p1, p2 = Vector(p1), Vector(p2)
    d = p2 - p1
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=max(d.length, 1e-4),
                                        location=(p1 + p2) / 2)
    o = bpy.context.active_object
    _orient(o, Vector((0, 0, 1)).rotation_difference(d.normalized()))
    _finish(o, mat, coll)
    if caps:
        ball(p1, r, mat, coll, segs=16, rings=8)
        ball(p2, r, mat, coll, segs=16, rings=8)
    return o


def box(c, size, mat, coll, quat=None, bevel=0.0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=c)
    o = bpy.context.active_object
    o.data.transform(Matrix.Diagonal(Vector((*size, 1))))
    _orient(o, quat)
    if bevel:
        md = o.modifiers.new("bevel", "BEVEL")
        md.width = bevel
        md.segments = 3
    return _finish(o, mat, coll, smooth=bool(bevel))


def cyl(c, r, depth, mat, coll, quat=None, verts=40):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=depth, location=c)
    o = bpy.context.active_object
    _orient(o, quat)
    return _finish(o, mat, coll)


def cone(c, r1, r2, depth, mat, coll, quat=None, verts=48):
    bpy.ops.mesh.primitive_cone_add(vertices=verts, radius1=r1, radius2=r2, depth=depth, location=c)
    o = bpy.context.active_object
    _orient(o, quat)
    return _finish(o, mat, coll)


def torus(c, R, r, mat, coll, quat=None):
    bpy.ops.mesh.primitive_torus_add(major_radius=R, minor_radius=r, major_segments=64,
                                     minor_segments=10, location=c)
    o = bpy.context.active_object
    _orient(o, quat)
    return _finish(o, mat, coll)


def disc(c, r, mat, coll, quat=None):
    bpy.ops.mesh.primitive_circle_add(vertices=48, radius=r, fill_type="NGON", location=c)
    o = bpy.context.active_object
    _orient(o, quat)
    return _finish(o, mat, coll, smooth=False)


def up_to(axis):
    return Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized())


def basis_q(x, y, z):
    return Matrix((x, y, z)).transposed().to_quaternion()


# ------------------------------------------------------------------------ rig
class Rig:
    """Local frame of a musician/object: x = their right, y = forward, z = up."""

    def __init__(self, loc, yaw):
        self.loc = Vector((loc[0], loc[1], 0.0))
        self.R = Matrix.Rotation(yaw, 3, "Z")

    def w(self, p):
        return self.loc + self.R @ Vector(p)

    def q(self, basis=None):
        return (self.R if basis is None else self.R @ basis).to_quaternion()


def face_toward(loc, target):
    return math.atan2(-(target[0] - loc[0]), target[1] - loc[1])


# -------------------------------------------------------------------- avatars
SCRIPT_DIR = os.path.dirname(os.path.abspath(
    globals().get("__file__") or sys.argv[sys.argv.index("--python") + 1]))
sys.path.insert(0, SCRIPT_DIR)
from avatars import Avatar  # noqa: E402
from fretting import CHORDS, FretHand, animate_progression  # noqa: E402
from strumming import StrumHand, animate_strumming  # noqa: E402
from strat import strat_guitar  # noqa: E402
from clapton import clapton_guitar  # noqa: E402
from pbass import pbass_guitar  # noqa: E402
from drumkit import drum_kit  # noqa: E402
from yamaha import yamaha_keyboard  # noqa: E402
from drumming import DrumPlayer, animate_drums  # noqa: E402
from comping import KeysPlayer, animate_comping  # noqa: E402
from mocap import Layer  # noqa: E402
from riding import clip_grip, ride  # noqa: E402
from song import Song  # noqa: E402
from synth import render as render_song  # noqa: E402
from strumming import STRUM_PATTERN  # noqa: E402
from fretting import PLACEMENTS  # noqa: E402
from comping import ANCHORS  # noqa: E402
import render  # noqa: E402

_t0 = [time.perf_counter()]


def lap(stage):
    """Print how long the build stage took."""
    t = time.perf_counter()
    print("TIMING %-22s %6.1f s" % (stage, t - _t0[0]))
    _t0[0] = t

# Microsoft Rocketbox avatars (MIT), downloaded into mockup/avatars/
AVATARS = {"drums": "Male_Adult_09", "bass": "Female_Adult_08", "keys": "Female_Adult_12",
           "guitar": "Male_Adult_04", "you": "Male_Adult_06"}
UP = Vector((0, 0, 1))


def report(name, misses):
    print("%-12s fingertip miss (mm): %s" % (name, {k: round(v * 1000, 1) for k, v in misses.items()}))


# -------------------------------------------------------------------- guitars
# Offset-style solid body outline in guitar coordinates (x towards the neck, y up across it)
BODY_OUTLINE = [(-0.25, 0.0), (-0.235, -0.10), (-0.17, -0.16), (-0.08, -0.165), (0.0, -0.13), (0.05, -0.112),
                (0.10, -0.12), (0.135, -0.10), (0.12, -0.06), (0.09, -0.035), (0.09, 0.035), (0.13, 0.07),
                (0.20, 0.095), (0.18, 0.125), (0.10, 0.135), (0.04, 0.12), (-0.02, 0.135), (-0.11, 0.165),
                (-0.20, 0.14), (-0.245, 0.08)]


def inside_outline(x, y, pts):
    """Point-in-polygon test against a body outline (guitar coordinates)."""
    inside = False
    for (x1, y1), (x2, y2) in zip(pts, pts[1:] + pts[:1]):
        if (y1 > y) != (y2 > y) and x < x1 + (y - y1) * (x2 - x1) / (y2 - y1):
            inside = not inside
    return inside


def outline_solid(name, pts, center, quat, thickness, mat, coll, bevel=0.006):
    """Closed smooth 2D outline extruded to a solid with rounded edges."""
    cu = bpy.data.curves.new(name, "CURVE")
    cu.dimensions = "2D"
    cu.fill_mode = "BOTH"
    cu.extrude = max(thickness / 2 - bevel, 0.0005)
    cu.bevel_depth = bevel
    cu.bevel_resolution = 3
    cu.resolution_u = 10
    sp = cu.splines.new("BEZIER")
    sp.bezier_points.add(len(pts) - 1)
    for bp, (x, y) in zip(sp.bezier_points, pts):
        bp.co = (x, y, 0)
        bp.handle_left_type = bp.handle_right_type = "AUTO"
    sp.use_cyclic_u = True
    cu.materials.append(mat)
    o = bpy.data.objects.new(name, cu)
    coll.objects.link(o)
    o.location = center
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = quat
    return o


def guitar(rig, c, xdir, zdir, kind, body_mat, guard_mat, coll):
    """Electric guitar/bass. Guitar frame: X along neck to headstock, Z out of the top."""
    c = Vector(c)
    X = Vector(xdir).normalized()
    Z = Vector(zdir)
    Z = (Z - X * Z.dot(X)).normalized()
    Y = Z.cross(X)
    q = rig.q(Matrix((X, Y, Z)).transposed())

    def L(p):
        return c + X * p[0] + Y * p[1] + Z * p[2]

    def W(p):
        return rig.w(L(p))

    bass = kind == "bass"
    scale = 0.864 if bass else 0.648
    neck_len = 0.62 if bass else 0.45
    nut_x = 0.12 + neck_len
    bridge_x = nut_x - scale
    th = 0.045
    top = th / 2

    k = (1.12, 1.08) if bass else (1.0, 1.0)
    body = [(x * k[0], y * k[1]) for x, y in BODY_OUTLINE]
    outline_solid("guitar_body", body, W((0, 0, 0)), q, th, body_mat, coll)
    guard = [(-0.03 + (x + 0.03) * 0.7, -0.012 + y * 0.72) for x, y in body]
    outline_solid("pickguard", guard, W((0, 0, top + 0.001)), q, 0.002, guard_mat, coll, bevel=0.0004)

    neck_w = 0.056 if bass else 0.05
    box(W(((0.08 + nut_x) / 2, 0, top + 0.012)), (nut_x - 0.08, neck_w, 0.008), MAPLE, coll, q)
    # rounded (elliptic) neck back: cylinder along the neck, flattened front-to-back
    neck_back = cyl(W(((0.08 + nut_x) / 2, 0, top + 0.004)), 1.0, nut_x - 0.08, MAPLE, coll,
                    (rig.R @ Matrix((-Z, Y, X)).transposed()).to_quaternion())
    neck_back.scale = (0.016, neck_w / 2, 1.0)
    box(W(((0.12 + nut_x) / 2, 0, top + 0.019)), (nut_x - 0.12, neck_w, 0.006), ROSEWOOD, coll, q)
    box(W((nut_x + 0.1, 0.01, top + 0.002)), (0.2, 0.09, 0.018), MAPLE, coll, q)
    n_str = 4 if bass else 6
    for i in range(n_str):
        y = (i - (n_str - 1) / 2) * (neck_w * 0.72 / (n_str - 1))
        box(W((nut_x + 0.04 + 0.025 * i, 0.05, top + 0.004)), (0.012, 0.03, 0.012), CHROME, coll, q)
        seg(W((bridge_x, y, top + 0.027)), W((nut_x, y * 0.85, top + 0.025)),
            0.0018 if bass else 0.0011, CHROME, coll, caps=False, verts=6)
    box(W((bridge_x, 0, top + 0.007)), (0.03, 0.075, 0.014), CHROME, coll, q)
    for px in ((0.12,) if bass else (0.05, 0.17)):
        box(W((bridge_x + px, 0, top + 0.006)), (0.022, 0.072, 0.012), BLACK, coll, q)
    for k in range(2):
        cyl(W((bridge_x - 0.02 + 0.05 * k, -0.12, top + 0.01)), 0.012, 0.02, BLACK, coll, q, verts=16)

    strings = [(i - (n_str - 1) / 2) * (neck_w * 0.72 / (n_str - 1)) for i in range(n_str)]

    def fret_x(n):
        return nut_x - scale * (1 - 2 ** (-n / 12))

    for n in range(1, 21):
        box(W((fret_x(n), 0, top + 0.023)), (0.0018, neck_w, 0.0025), CHROME, coll, q)

    def fret_point(string, fret, lift=0.0):
        """Fingertip target just behind a fret; string 0 is the highest (thinnest) one."""
        x = fret_x(fret) + 0.3 * (fret_x(fret - 1) - fret_x(fret))
        y = strings[string] * (0.85 + 0.15 * (nut_x - x) / scale)
        return W((x, y, top + 0.031 + lift))

    return {"W": W, "X": rig.R @ X, "Y": rig.R @ Y, "Z": rig.R @ Z, "fret_x": fret_x,
            "fret_point": fret_point, "top": top, "bridge_x": bridge_x, "nut_x": nut_x, "scale": scale,
            "neck_w": neck_w, "strings": strings, "thickness": th, "strap_top": W((0.2, 0.11, -0.01)),
            "half_w": lambda x: neck_w / 2, "board_z": top + 0.022, "back_z": lambda x: top - 0.012,
            "string_z": lambda x: top + 0.026, "body_inside": lambda x, y: inside_outline(x, y, body)}


def strap(a, b, facing, coll, width=0.045):
    """Flat leather strap from a to b, its face turned towards `facing`."""
    d = b - a
    n = (facing - d * facing.dot(d) / d.length_squared).normalized()
    q = basis_q(n.cross(d.normalized()), n, d.normalized())
    return box((a + b) / 2, (width, 0.004, d.length), LEATHER, coll, q, bevel=0.0015)


def guitarist(role, loc, target, kind, body_mat, guard_mat, coll, chord, head_pitch=-0.3, finish="red",
              model="classic"):
    """chord: a CHORDS name (guitar) or a (label, spec) pair such as a bass note.
    Guitars are Stratocaster models: model "classic" (Classic Player; finish red, black, white or sunburst)
    or "clapton" (1989 Eric Clapton; finish green, torino_red, pewter or black). The bass is the P-Bass."""
    yaw = face_toward(loc, target)
    rig = Rig(loc, yaw)
    av = Avatar(AVATARS[role], coll, loc, yaw)
    # Mixamo capture for the upper body (riding.py): the bass clip for the bassist, the pick player for the
    # guitars (you a little later in the clip than the band's guitarist, so they do not move as one)
    seconds = [f / FPS for f in range(int(round(BAR * len(PROGRESSION))) + 1)]
    clip = "guitar_playing_111430901.fbx" if kind == "bass" else "guitar_playing_111490901.fbx"
    layer = Layer(av, clip, seconds, offset=1.7 if role == "you" else 0.0)
    pel = av.local(av.rhead("Pelvis"))
    front = av.front_at(pel.z - 0.02, pel.z + 0.25)
    before = set(bpy.data.objects)
    if kind == "bass":
        lap(role + ": avatar, clip")
        g = pbass_guitar(rig, (0.05, front + 0.05, pel.z + 0.06), (-0.9, 0.15, 0.4), (0.05, 1, 0.25), coll)
        lap(role + ": P-Bass model")
        print("    Precision Bass measured:", g["measured"])
        steel = M("string_steel", (0.8, 0.8, 0.8), 0.25, 1.0)
        for i, path in enumerate(g["string_paths"]):  # G .. low E, on the model's nut and saddles
            for a_, b_ in zip(path, path[1:]):
                seg(a_, b_, (0.00055, 0.00065, 0.00085, 0.00105)[i], steel, coll, caps=False, verts=8)
    else:
        make = clapton_guitar if model == "clapton" else strat_guitar
        lap(role + ": avatar, clip")
        g = make(rig, (0.04, front + 0.045, pel.z + 0.1), (-0.92, 0.12, 0.37), (0.05, 1, 0.25), finish, coll)
        lap(role + ": %s model" % model)
        print("    Stratocaster (%s) measured:" % model, g["measured"])
        steel = M("string_steel", (0.8, 0.8, 0.8), 0.25, 1.0)
        for i, path in enumerate(g["string_paths"]):  # our own strings, exactly on the model's saddles and nut
            r = (0.00022, 0.00028, 0.00034, 0.00045, 0.00055, 0.00065)[i]
            for a_, b_ in zip(path, path[1:]):
                seg(a_, b_, r, steel, coll, caps=False, verts=6)
    X, Y, Z, W = g["X"], g["Y"], g["Z"], g["W"]
    av.stance(0.06)
    av.bend("Head", pitch=head_pitch)

    # fretting hand: grip solved per chord (fretting.py has the reference study behind it)
    fh = FretHand(av, g)
    label, spec = (chord, CHORDS[chord]) if isinstance(chord, str) else chord
    report(role + " " + label, fh.apply(label, spec))
    av.guitar, av.fret = g, fh
    x3 = g["fret_x"](3)
    av.arm["guitar_frame"] = [*fh.c0, *X, *Y, *Z, g["half_w"](x3), g["board_z"], g["back_z"](x3)]  # collision check
    av.fret_spot = W((fh.targets(spec)[4], 0, g["board_z"] + 0.005))
    cells = [int(g["body_inside"](-0.3 + (i + 0.5) * 0.005, -0.25 + (j + 0.5) * 0.005))
             for j in range(100) for i in range(120)]
    av.arm["guitar_body_grid"] = {"x0": -0.3, "y0": -0.25, "cell": 0.005, "nx": 120, "ny": 100,
                                  "half_th": g["thickness"] / 2, "data": cells}
    if kind == "bass":
        # plucking hand: palm towards the strings between the pickups, thumb resting on the pickup
        F, N = -Y + X * 0.35, -Z
        Rp = av.hand_delta("r", F, N)
        px = g["bridge_x"] + 0.2
        wrist = av.wrist_for("r", Rp, "Finger1", W((px, 0.05, g["string_z"](px) + 0.024)))
        av.hand_to("r", F, N, wrist, av.head("R UpperArm") + av.right * 0.35 - av.fwd * 0.2 - UP * 0.2)
        for k, c in zip((1, 2, 3, 4), (0.45, 0.6, 0.95, 1.05)):
            av.finger("r", k, c)
        av.thumb_to("r", W((px - 0.06, 0.035, g["string_z"](px) - 0.011)))
    else:
        av.strum = StrumHand(av, g, grip=clip_grip(layer))  # captured pick grip, forearm over the rear bout
        pk = cyl(av.strum.pick0, 0.011, 0.0015, M("pick", (0.05, 0.05, 0.06), 0.4), coll, up_to(Z), verts=3)
        bpy.context.view_layer.update()
        mw = pk.matrix_world.copy()
        pk.parent, pk.parent_type, pk.parent_bone = av.arm, "BONE", "Bip01 R Finger02"  # follows the strum
        bpy.context.view_layer.update()
        pk.matrix_world = mw
    shoulder = (av.head("L Clavicle") + av.head("L UpperArm")) / 2 + UP * 0.07
    st = strap(g["strap_top"], shoulder, av.fwd, coll)
    # the instrument and its strings ride on the captured upper body; the strap runs from the instrument's
    # strap button to the shoulder (riding.py re-aims it every frame)
    clav = av.arm.matrix_world @ av.pb("L Clavicle").matrix
    av.strap = (st, g["strap_top"].copy(), shoulder.copy(), av.fwd.copy(), clav.inverted() @ shoulder)
    av.upright = 0.6 if kind == "bass" else 0.0  # the bass capture hunches over the instrument
    av.ride = (av, layer, [o for o in bpy.data.objects if o not in before and o.parent is None and o != st],
               kind == "bass")
    return rig, av


# ----------------------------------------------------------------- drum kit
def drummer(loc, target, coll):
    """The Sonor kit (drumkit.py) with the drummer on its throne, playing the captured groove."""
    yaw = face_toward(loc, target)
    rig = Rig(loc, yaw)
    kit = drum_kit(rig, coll)
    lap("drums: kit model")
    print("    Drum kit measured:", kit["measured"])
    av = Avatar(AVATARS["drums"], coll, loc, yaw)
    # Mixamo's "Basic Rock Beat" retargeted onto the avatar and fitted to the kit (drumming.py, mocap.py)
    av.drums = DrumPlayer(av, kit, rig, len(PROGRESSION), BEAT / 2, song_hits=DRUM_HITS)
    print("    Drummer fitted to the kit:", av.drums.fit_report)
    bpy.app.driver_namespace["drum_player"] = av.drums  # for check scripts run after the build
    return rig, av


# ------------------------------------------------------------------ keyboard
def keyboardist(loc, target, coll):
    """The Yamaha EZ-220 (yamaha.py) on an X-stand, the player seated on a bench with Mixamo's piano capture
    as the body layer; comping.py plays the chords. Bench and keyboard are set up for the captured posture:
    feet on the floor, keys where the captured fingertips are."""
    yaw = face_toward(loc, target)
    rig = Rig(loc, yaw)
    w = rig.w
    av = Avatar(AVATARS["keys"], coll, loc, yaw)
    seconds = [f / FPS for f in range(int(round(BAR * len(PROGRESSION))) + 1)]
    layer = Layer(av, "piano_playing_112170902.fbx", seconds)
    lowest, tips = 1.0, []
    for k in range(0, len(seconds), 4):
        W, hips = layer.pose(k)
        pos = layer.rt.fk(W, Vector((0, 0, layer.height)) + hips)
        lowest = min(lowest, pos["L Toe0"][0].z, pos["R Toe0"][0].z)
        tips += [rig.R.transposed() @ Vector(pos[S + " Finger22"][1]) for S in "LR"]
    anchor = rig.w((0, 0, layer.height - lowest + 0.005))  # the clip's chair is higher than its floor
    tip = sum(tips, Vector()) / len(tips) + Vector((0, 0, anchor.z - layer.height))
    lap("keys: avatar, clip")
    kb = yamaha_keyboard(rig, coll, height=tip.z - 0.005, front=tip.y - 0.04)
    lap("keys: Yamaha model")
    seat = anchor - rig.R @ Vector((0, -0.04, 0.10))  # bench top under the hips
    leather = M("bench_top", (0.02, 0.018, 0.018), 0.5)
    box(seat - UP * 0.025, (0.56, 0.32, 0.05), leather, coll, rig.q(), bevel=0.012)
    for sx in (-0.24, 0.24):
        for sy in (-0.12, 0.12):
            seg(seat + rig.R @ Vector((sx, sy, -0.05)), Vector((*(seat + rig.R @ Vector((sx, sy, 0))).xy, 0.0)),
                0.014, CHROME, coll)
    print("    Yamaha EZ-220 measured:", kb["measured"])
    y0, y1, z0, half = kb["box"]
    for sx in (-0.55 * half, 0.55 * half):  # X-stand
        seg(w((sx, y0 + 0.03, 0.02)), w((sx, y1 - 0.03, z0 - 0.015)), 0.015, BLACK, coll)
        seg(w((sx, y1 - 0.03, 0.02)), w((sx, y0 + 0.03, z0 - 0.015)), 0.015, BLACK, coll)
    seg(w((-0.55 * half, (y0 + y1) / 2, z0 - 0.02)), w((0.55 * half, (y0 + y1) / 2, z0 - 0.02)), 0.012, BLACK, coll)
    av.keys = KeysPlayer(av, kb, layer, anchor)  # comping.py: voicings, strokes and chord changes
    av.keys.pose(0.0, [PROGRESSION[0]])
    report("keys", av.keys.miss)
    return rig, av


# ----------------------------------------------------------------------- amps
def amp(loc, target, size, coll):
    rig = Rig(loc, face_toward(loc, target))
    w_, d, h = size
    q = rig.q()
    box(rig.w((0, 0, h / 2)), (w_, d, h), BLACK, coll, q, bevel=0.015)
    box(rig.w((0, d / 2 + 0.002, h * 0.43)), (w_ * 0.88, 0.006, h * 0.72), M("grille", (0.06, 0.06, 0.065), 0.95), coll, q)
    box(rig.w((0, d / 2 + 0.002, h * 0.89)), (w_ * 0.88, 0.006, h * 0.1), M("amp_panel", (0.75, 0.72, 0.62), 0.35, 0.6), coll, q)
    for k in range(6):
        cyl(rig.w((-w_ * 0.35 + k * w_ * 0.12, d / 2 + 0.012, h * 0.89)), 0.012, 0.02, BLACK, coll,
            q @ up_to((0, 1, 0)), verts=16)


# ----------------------------------------------------------------------- room
X0, X1, Y0, Y1, H = -3.6, 3.6, -3.4, 4.2, 2.7
WALL = M("wall", (0.7, 0.66, 0.6), 0.85)
TRIM = M("trim", (0.93, 0.93, 0.91), 0.5)


def build_room():
    c = C_ROOM
    box(((X0 + X1) / 2, (Y0 + Y1) / 2, -0.01), (X1 - X0, Y1 - Y0, 0.02), wood_floor(), c)
    box((0, Y1 + 0.05, H / 2), (X1 - X0 + 0.2, 0.1, H), WALL, c)
    box((X0 - 0.05, (Y0 + Y1) / 2, H / 2), (0.1, Y1 - Y0, H), WALL, c)
    box((X1 + 0.05, (Y0 + Y1) / 2, H / 2), (0.1, Y1 - Y0, H), WALL, c)
    box((0, (Y0 + Y1) / 2, H + 0.05), (X1 - X0 + 0.2, Y1 - Y0, 0.1), M("ceiling", (0.9, 0.9, 0.88), 0.9), c)
    box((0, Y1 - 0.008, 0.045), (X1 - X0, 0.016, 0.09), TRIM, c)
    box((X0 + 0.008, (Y0 + Y1) / 2, 0.045), (0.016, Y1 - Y0, 0.09), TRIM, c)
    box((X1 - 0.008, (Y0 + Y1) / 2, 0.045), (0.016, Y1 - Y0, 0.09), TRIM, c)

    # window with daylight on the left wall
    wy, wz, ww, wh = 0.4, 1.45, 1.6, 1.35
    box((X0 + 0.006, wy, wz), (0.01, ww, wh), E("sky", (0.78, 0.87, 1.0), 2.2), c)
    for dy in (-ww / 2, 0, ww / 2):
        box((X0 + 0.03, wy + dy, wz), (0.05, 0.06, wh - 0.06), TRIM, c)
    for dz in (-wh / 2, wh / 2):
        box((X0 + 0.03, wy, wz + dz), (0.05, ww + 0.06, 0.06), TRIM, c)
    box((X0 + 0.08, wy, wz - wh / 2 - 0.04), (0.16, ww + 0.2, 0.04), TRIM, c)
    curtain = M("curtain", (0.55, 0.6, 0.52), 0.95)
    for s in (-1, 1):
        for k in range(4):
            cyl((X0 + 0.12, wy + s * (ww / 2 + 0.18) + (k - 1.5) * 0.08, 1.2), 0.05, 2.3, curtain, c, verts=12)

    # sofa against the right wall
    rig = Rig((X1 - 0.5, -0.7), face_toward((X1 - 0.5, -0.7), (0, -0.7)))
    fabric = M("sofa", (0.17, 0.27, 0.32), 0.9)
    cushion = M("sofa_cushion", (0.21, 0.32, 0.37), 0.95)
    q = rig.q()
    box(rig.w((0, 0, 0.24)), (2.1, 0.9, 0.3), fabric, c, q, bevel=0.03)
    box(rig.w((0, -0.36, 0.62)), (2.1, 0.2, 0.5), fabric, c, q, bevel=0.05)
    for sx in (-1, 1):
        box(rig.w((sx * 1.0, 0, 0.42)), (0.18, 0.9, 0.36), fabric, c, q, bevel=0.06)
    for k in (-1, 0, 1):
        box(rig.w((k * 0.62, 0.05, 0.44)), (0.6, 0.72, 0.14), cushion, c, q, bevel=0.05)
        box(rig.w((k * 0.62, -0.2, 0.7)), (0.58, 0.16, 0.42), cushion, c, q, bevel=0.06)
    box(rig.w((-0.75, -0.06, 0.66)), (0.4, 0.13, 0.36), M("pillow", (0.85, 0.6, 0.15), 0.9), c,
        q @ up_to((0, -0.3, 1)), bevel=0.05)

    # bookshelf on the left wall
    rig = Rig((X0 + 0.19, 3.1), face_toward((X0 + 0.19, 3.1), (0, 3.1)))
    q = rig.q()
    shelf = M("shelf", (0.9, 0.88, 0.84), 0.6)
    for sx in (-0.5, 0.5):
        box(rig.w((sx, 0, 1.0)), (0.03, 0.34, 2.0), shelf, c, q)
    for z in (0.02, 0.45, 0.85, 1.25, 1.65, 2.0):
        box(rig.w((0, 0, z)), (1.0, 0.34, 0.03), shelf, c, q)
    box(rig.w((0, -0.16, 1.0)), (1.0, 0.02, 2.0), shelf, c, q)
    book_cols = [(0.6, 0.12, 0.1), (0.12, 0.25, 0.5), (0.85, 0.7, 0.3), (0.2, 0.4, 0.25),
                 (0.9, 0.9, 0.85), (0.1, 0.1, 0.12), (0.5, 0.3, 0.55)]
    for z in (0.035, 0.465, 0.865, 1.265, 1.665):
        x = -0.46
        while x < 0.4:
            bw = random.uniform(0.025, 0.05)
            bh = random.uniform(0.22, 0.34)
            col = random.choice(book_cols)
            box(rig.w((x + bw / 2, 0.0, z + 0.015 + bh / 2)), (bw, random.uniform(0.18, 0.24), bh),
                M("book_%d" % book_cols.index(col), col, 0.7), c, q)
            x += bw + 0.004
            if random.random() < 0.08:
                x += 0.12

    # plant
    cyl((X0 + 0.45, Y1 - 0.45, 0.2), 0.2, 0.4, M("terracotta", (0.6, 0.3, 0.18), 0.8), c)
    leaf = M("leaf", (0.12, 0.38, 0.14), 0.6)
    for k in range(16):
        a = random.uniform(0, 2 * math.pi)
        rr = random.uniform(0.05, 0.3)
        p = Vector((X0 + 0.45 + rr * math.cos(a), Y1 - 0.45 + rr * math.sin(a), random.uniform(0.6, 1.35)))
        ell(p, (0.06, 0.2, 0.015), up_to((math.cos(a), math.sin(a), 1.2)) @ up_to((0, 0.3, 1)), leaf, c)
    for k in range(5):
        seg((X0 + 0.45, Y1 - 0.45, 0.38), (X0 + 0.45 + random.uniform(-0.2, 0.2), Y1 - 0.45 + random.uniform(-0.2, 0.2),
                                           random.uniform(0.8, 1.2)), 0.012, leaf, c)

    # floor lamp
    lx, ly = X1 - 0.45, Y1 - 0.45
    cyl((lx, ly, 0.015), 0.18, 0.03, BLACK, c)
    seg((lx, ly, 0.03), (lx, ly, 1.55), 0.012, BLACK, c)
    cone((lx, ly, 1.65), 0.25, 0.17, 0.3, E("lampshade", (1.0, 0.82, 0.6), 2.5), c)
    light("lamp", "POINT", (lx, ly, 1.6), 45, (1.0, 0.72, 0.45), size=0.1)

    # wall art
    frame = M("frame", (0.06, 0.05, 0.04), 0.5)
    for (x, z, fw, fh, c1, c2) in ((-2.1, 1.6, 0.8, 0.6, (0.85, 0.45, 0.2), (0.15, 0.3, 0.5)),
                                   (2.0, 1.65, 0.6, 0.8, (0.9, 0.8, 0.55), (0.6, 0.15, 0.15))):
        box((x, Y1 - 0.015, z), (fw + 0.06, 0.03, fh + 0.06), frame, c)
        box((x, Y1 - 0.032, z - fh / 4), (fw, 0.004, fh / 2), M("art_%.1f" % x, c1, 0.8), c)
        box((x, Y1 - 0.032, z + fh / 4), (fw, 0.004, fh / 2), M("art2_%.1f" % x, c2, 0.8), c)

    # drum rug
    box((0, 2.15, 0.006), (2.5, 2.1, 0.012), M("rug", (0.36, 0.09, 0.08), 1.0), c, bevel=0.005)


# --------------------------------------------------------------------- lights
def light(name, kind, loc, power, color, size=1.0, target=None, size_y=None):
    ld = bpy.data.lights.new(name, kind)
    ld.energy = power
    ld.color = color
    if kind == "AREA":
        ld.shape = "RECTANGLE"
        ld.size = size
        ld.size_y = size_y if size_y else size
    elif kind == "POINT":
        ld.shadow_soft_size = size
    o = bpy.data.objects.new(name, ld)
    C_LIGHTS.objects.link(o)
    o.location = loc
    if target:
        d = Vector(target) - Vector(loc)
        o.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    return o


def build_lights():
    light("ceiling_front", "AREA", (0, -1.2, H - 0.05), 260, (1.0, 0.95, 0.88), 2.2)
    light("ceiling_back", "AREA", (0, 1.8, H - 0.05), 260, (1.0, 0.95, 0.88), 2.2)
    light("window", "AREA", (X0 + 0.15, 0.4, 1.45), 380, (0.85, 0.92, 1.0), 1.6, (0, 0.4, 1.2), 1.3)
    light("front_fill", "AREA", (0, Y0 + 0.2, 1.9), 220, (1.0, 0.96, 0.92), 3.0, (0, 1.5, 1.1), 1.2)
    w = scene.world or bpy.data.worlds.new("World")
    scene.world = w
    bg = next(n for n in w.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (0.03, 0.03, 0.035, 1)


# ---------------------------------------------------------------- HUD, labels
def text(body, loc, size, mat, coll, parent, align="CENTER"):
    cu = bpy.data.curves.new("txt", type="FONT")
    cu.body = body
    cu.size = size
    cu.align_x = align
    cu.align_y = "CENTER"
    o = bpy.data.objects.new("txt_" + body[:12], cu)
    coll.objects.link(o)
    o.data.materials.append(mat)
    o.parent = parent
    o.location = loc
    o.visible_shadow = False
    return o


def child(o, parent, loc):
    o.parent = parent
    o.location = loc
    o.visible_shadow = False
    return o


def plane(size, mat, coll):
    bpy.ops.mesh.primitive_plane_add(size=1)
    o = bpy.context.active_object
    o.data.transform(Matrix.Diagonal(Vector((size[0], size[1], 1, 1))))
    return _finish(o, mat, coll, smooth=False)


def empty(name, loc, rot, coll):
    e = bpy.data.objects.new(name, None)
    coll.objects.link(e)
    e.location = loc
    e.rotation_euler = rot
    return e


def build_hud():
    c = C_HUD
    root = empty("HUD", (0, 1.62, 2.12), (math.radians(98), 0, 0), c)
    white = E("hud_text", (0.92, 0.97, 1.0), 3.0)
    dim = E("hud_dim", (0.45, 0.55, 0.62), 2.0)
    accent = E("hud_accent", (0.2, 0.85, 1.0), 4.0)
    green = E("hud_green", (0.3, 1.0, 0.45), 5.0)
    child(plane((1.75, 0.62), E("hud_panel", (0.02, 0.05, 0.08), 1.0, alpha=0.72), c), root, (0, 0, 0))
    child(plane((1.75, 0.008), accent, c), root, (0, 0.31, 0.001))
    text("%s  \u00b7  BAR %d / %d  \u00b7  %d BPM" % ("SONG" if SONG else "CHORUS", FIRST_BAR if SONG else 33,
                                                       SONG.bars if SONG else 96, round(BPM)),
         (0, 0.2, 0.004), 0.085, white, c, root)
    for i, ch in enumerate(PROGRESSION):
        x = -0.54 + i * 0.36
        child(plane((0.32, 0.17), E("hud_box", (0.1, 0.16, 0.22), 1.0), c), root, (x, 0.0, 0.002))
        text(ch, (x, 0.0, 0.004), 0.11, white, c, root)
    chord_hl = child(plane((0.35, 0.2), accent, c), root, (-0.54, 0.0, 0.001))  # glowing frame, current chord
    for i in range(4):
        child(disc(Vector(), 0.024, dim, c), root, (-0.72 + i * 0.075, -0.2, 0.003))
    beat_hl = child(disc(Vector(), 0.026, accent, c), root, (-0.72, -0.2, 0.0035))
    child(disc(Vector(), 0.018, green, c), root, (-0.32, -0.2, 0.003))
    text("BAND FOLLOWING YOU   +12 ms", (-0.28, -0.2, 0.004), 0.05, white, c, root, align="LEFT")
    text("NEXT: VERSE 3", (0.82, -0.2, 0.004), 0.045, dim, c, root, align="RIGHT")
    for o in c.objects:
        if o.type == "MESH":
            o.visible_shadow = False
    return chord_hl, beat_hl


def build_labels(cam, items):
    c = C_LABELS
    white = E("label_text", (1, 1, 1), 3.0)
    sub = E("label_sub", (0.55, 0.88, 1.0), 3.0)
    panel = E("label_panel", (0.02, 0.04, 0.06), 1.0, alpha=0.8)
    for name, desc, loc in items:
        e = empty("label_" + name, loc, (0, 0, 0), c)
        con = e.constraints.new("TRACK_TO")
        con.target = cam
        con.track_axis = "TRACK_Z"
        con.up_axis = "UP_Y"
        child(plane((0.95, 0.25), panel, c), e, (0, 0, 0))
        text(name, (0, 0.045, 0.003), 0.1, white, c, e)
        text(desc, (0, -0.065, 0.003), 0.052, sub, c, e)


def ground_ring(loc, r):
    torus((loc[0], loc[1], 0.006), r, 0.006, GLOW, C_BAND).visible_shadow = False
    disc(Vector((loc[0], loc[1], 0.004)), r, GLOW_FILL, C_BAND).visible_shadow = False


# -------------------------------------------------------------------- cameras
def camera(name, loc, target, lens):
    cd = bpy.data.cameras.new(name)
    cd.lens = lens
    cd.clip_start = 0.05
    o = bpy.data.objects.new(name, cd)
    scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    return o


# ---------------------------------------------------------------------- build
PROGRESSION = ["G", "Em", "C", "D"]  # I-vi-IV-V in G: shared fingers in 3 of 4 changes
BPM, FPS = 92, 24
STRUM = STRUM_PATTERN
SONG, DRUM_HITS, BASS_SPECS, FIRST_BAR = None, None, None, 1


def playable(chord):
    """A chord the guitarists' library has: 7ths, sus and power chords played as their triad."""
    if chord in CHORDS:
        return chord
    for suffix, as_ in (("maj7", ""), ("m7", "m"), ("7", ""), ("sus2", ""), ("sus4", ""), ("5", ""), ("dim", "m")):
        if chord.endswith(suffix) and chord[:-len(suffix)] + as_ in CHORDS:
            return chord[:-len(suffix)] + as_
    print("    no guitar shape for %s: playing G" % chord)
    return "G"


def bass_spec(pos):
    """A fretted bass note (string, fret) as a fretting-hand spec; open strings are played on the next
    string down at the 5th fret, as the fretting hand stays in position."""
    s, fret = pos
    if fret == 0 and s < 3:
        s, fret = s + 1, 5
    return {"fingers": {1: (s, max(fret, 1))}, "thumb": "behind"}


for a_ in argv:
    if a_.startswith("song="):
        SONG = Song(os.path.abspath(a_[5:]))
if SONG:
    rng = next((a_[5:] for a_ in argv if a_.startswith("bars=")), "1-%d" % min(4, SONG.bars))
    FIRST_BAR, last_bar = (int(x) for x in rng.split("-"))
    nbars = last_bar - FIRST_BAR + 1
    BPM = SONG.bpm
    PROGRESSION = [playable(c) for c in SONG.chords()[FIRST_BAR - 1:last_bar]]
    STRUM = SONG.strum_pattern() or STRUM_PATTERN
    e0 = (FIRST_BAR - 1) * SONG.beats_per_bar * 2
    hits = SONG.drum_hits()
    inside = lambda e: 0 <= e - e0 < nbars * SONG.beats_per_bar * 2
    DRUM_HITS = {"r": [(e - e0, t) for e, t in hits["r"] if inside(e)],
                 "l": [(e - e0, t) for e, t in hits["l"] if inside(e)],
                 "kick": [e - e0 for e in hits["kick"] if inside(e)],
                 "open": [(a - e0, b - e0) for a, b in hits["open"] if inside(a)]}
    roots, last = SONG.bass_roots()[FIRST_BAR - 1:last_bar], (2, 3)
    BASS_SPECS = []
    for r_ in roots:
        last = r_ or last
        BASS_SPECS.append(bass_spec(last))
    print("Song %s: %s" % (os.path.basename(SONG.path), dict(SONG.summary(), section=rng, played=PROGRESSION)))
BEAT = 60 / BPM * FPS  # frames per beat
BAR = 4 * BEAT
BLEND = next((os.path.abspath(a_[6:]) for a_ in argv if a_.startswith("blend=")), os.path.join(
    SCRIPT_DIR, "vr_band_%s_%d-%d.blend" % (os.path.splitext(os.path.basename(SONG.path))[0], FIRST_BAR,
                                            FIRST_BAR + len(PROGRESSION) - 1) if SONG else "vr_band_mockup.blend"))


lap("setup")
build_room()
build_lights()
lap("room, lights")

USER_POS = (0.0, -1.55)
DRUM_POS, BASS_POS, KEYS_POS, GTR_POS = (0.0, 2.5), (-1.6, 0.85), (-1.8, 3.0), (1.6, 0.85)
BASS_ROOTS = {  # root notes kept between frets 5 and 10 where the bassist reaches comfortably
    "G": {"fingers": {1: (2, 10)}, "thumb": "behind"}, "C": {"fingers": {1: (3, 8)}, "thumb": "behind"},
    "D": {"fingers": {1: (2, 5)}, "thumb": "behind"}, "Em": {"fingers": {1: (2, 7)}, "thumb": "behind"}}
FIRST = PROGRESSION[0]
if BASS_SPECS is None:
    BASS_SPECS = [BASS_ROOTS[n] for n in PROGRESSION]

_, drum_av = drummer(DRUM_POS, (0, -1.0), C_BAND)
lap("drummer")
_, bass_av = guitarist("bass", BASS_POS, (0.4, -1.2), "bass", M("bass_body", (0.05, 0.05, 0.06), 0.2),
                       M("guard_white", (0.9, 0.9, 0.87), 0.4), C_BAND, (FIRST, BASS_SPECS[0]), head_pitch=-0.3)
lap("bassist")
_, keys_av = keyboardist(KEYS_POS, (0.2, -0.6), C_BAND)
lap("keyboardist")
_, gtr_av = guitarist("guitar", GTR_POS, (-0.4, -1.2), "guitar", M("gtr_red", (0.55, 0.03, 0.025), 0.18),
                      M("guard_white", (0.9, 0.9, 0.87), 0.4), C_BAND, FIRST, head_pitch=-0.25,
                      finish="green", model="clapton")
lap("guitarist")
amp((-2.6, 1.4), (0, -1.0), (0.62, 0.45, 1.05), C_BAND)
amp((2.4, 1.6), (0, -1.0), (0.62, 0.28, 0.5), C_BAND)
for p, r in (((DRUM_POS[0], DRUM_POS[1] - 0.45), 1.15), (BASS_POS, 0.55), (KEYS_POS, 0.7), (GTR_POS, 0.55)):
    ground_ring(p, r)


def headset(av):
    """Quest 3 on the avatar's posed head (it follows the head)."""
    before = set(bpy.data.objects)
    Q = av.crot("Head") @ av.rrot("Head").transposed()
    fwd, up = Q @ av.fwd, Q @ UP
    right = fwd.cross(up)
    q = Matrix((right, fwd, up)).transposed().to_quaternion()
    eyes = (av.head("LEye") + av.head("REye")) / 2

    def hw(p):
        return eyes + right * p[0] + fwd * p[1] + up * p[2]

    box(hw((0, 0.06, 0.0)), (0.19, 0.075, 0.095), M("quest_white", (0.88, 0.88, 0.87), 0.4), C_USER, q, bevel=0.025)
    box(hw((0, 0.098, 0.0)), (0.17, 0.006, 0.075), M("quest_front", (0.03, 0.03, 0.035), 0.15), C_USER, q, bevel=0.002)
    for sx in (-0.045, 0.045):
        for sz in (-0.02, 0.02):
            cyl(hw((sx, 0.103, sz)), 0.008, 0.004, BLACK, C_USER, q @ up_to((0, 1, 0)), verts=12)
    torus(hw((0, -0.07, 0.03)), 0.105, 0.011, M("quest_strap", (0.2, 0.2, 0.21), 0.8), C_USER, q @ up_to((0, 0.25, 1)))
    bpy.context.view_layer.update()
    for o in [o for o in bpy.data.objects if o not in before]:
        mw = o.matrix_world.copy()
        o.parent, o.parent_type, o.parent_bone = av.arm, "BONE", "Bip01 Head"
        bpy.context.view_layer.update()
        o.matrix_world = mw


_, you_av = guitarist("you", USER_POS, (0, 3.0), "guitar", M("gtr_amber", (0.78, 0.42, 0.1), 0.22),
                      M("guard_tort", (0.18, 0.07, 0.03), 0.35), C_USER, FIRST, head_pitch=-0.15,
                      finish="sunburst")
headset(you_av)
chord_hl, beat_hl = build_hud()
lap("you, headset, HUD")

# ------------------------------------------------------------------- animation
# one bar per chord: chord changes for both guitars, bass roots, strumming, HUD highlights
scene.render.fps = FPS
scene.frame_start, scene.frame_end = 1, int(1 + BAR * len(PROGRESSION))
if "chords" not in argv:
    for role, av in (("guitar", gtr_av), ("you", you_av)):
        for label, miss, roles in animate_progression(av.fret, PROGRESSION, BAR):
            report("%s %s" % (role, label), miss)
            if roles:
                print("    change into %s: %s" % (label, roles))
        animate_strumming(av.strum, lambda f: PROGRESSION[min(len(PROGRESSION) - 1, int((f - 1) / BAR))],
                          int(BAR * len(PROGRESSION)), BEAT / 2, pattern=STRUM)
        lap("animate " + role)
    for label, miss, roles in animate_progression(bass_av.fret, PROGRESSION, BAR,
                                                  specs=BASS_SPECS):
        report("bass " + label, miss)
    lap("animate bass")
    # the captured upper bodies of the guitarists and the bassist, instruments riding on them
    ride([av.ride for av in (gtr_av, you_av, bass_av)], list(range(1, int(round(BAR * len(PROGRESSION))) + 2)))
    lap("ride (upper bodies)")
    animate_drums(drum_av.drums)
    lap("animate drums")
    print("keys (worst pressed fingertip over the progression, mm):", animate_comping(keys_av.keys, PROGRESSION, BEAT))
    lap("animate keys")
    for i in range(len(PROGRESSION)):
        chord_hl.location.x = -0.54 + i * 0.36
        chord_hl.keyframe_insert("location", index=0, frame=1 + i * BAR)
    for b in range(4 * len(PROGRESSION)):
        beat_hl.location.x = -0.72 + (b % 4) * 0.075
        beat_hl.keyframe_insert("location", index=0, frame=1 + b * BEAT)
    for o in (chord_hl, beat_hl):  # the highlights jump from chord to chord and beat to beat
        ad = o.animation_data
        for fc in action_get_channelbag_for_slot(ad.action, ad.action_slot).fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = "CONSTANT"
STILL_FRAME = int(1 + BAR * 1.5)  # middle of the C bar
for a_ in argv:
    if a_.startswith("frame="):  # stills at another moment of the animation
        STILL_FRAME = int(a_[6:])
scene.frame_set(STILL_FRAME)

cam_pov = camera("CAM_POV_headset", (USER_POS[0], USER_POS[1] + 0.12, 1.66), (0, 1.6, 1.2), 16)
cam_ots = camera("CAM_over_shoulder", (0.9, -2.95, 1.95), (-0.25, 1.0, 1.05), 20)
cam_ovr = camera("CAM_overview", (-3.25, -3.1, 2.45), (0.3, 0.9, 0.55), 18)

build_labels(cam_ovr, [
    ("DRUMS", "drum track > hits > stick IK", (0.25, 2.75, 2.3)),
    ("BASS", "bass notes > fret-hand IK", (-1.6, 0.85, 2.0)),
    ("KEYS", "piano notes > finger IK", (-2.45, 2.6, 1.95)),
    ("GUITAR", "chords > strum + fret IK", (1.6, 0.85, 2.05)),
    ("YOU", "real guitar > tempo tracker", (0.0, -1.55, 2.1)),
])

# ----------------------------------------------------------------------- shots
# every camera any render uses, registered in the scene, so the saved .blend renders them later (render.py)
shots = [
    ("1_headset_view", cam_pov, {"User", "Labels"}, "still"),
    ("2_over_shoulder", cam_ots, {"Labels"}, "still"),
    ("3_overview", cam_ovr, {"HUD"}, "still"),
]


def close_up(name, target, eye, lens=45, kind="debug"):
    shots.append((name, camera("CAM_" + name, eye, target, lens), {"Labels", "HUD"}, kind))


# debug: close-ups of the hands, for checking poses
for nm, av in (("dbg_guitar", gtr_av), ("dbg_bass", bass_av), ("dbg_you", you_av)):
    h = (av.head("L Hand") + av.head("R Hand")) / 2
    close_up(nm, h, h + av.fwd * 0.9 + UP * 0.15)
    g, spot = av.guitar, av.fret_spot
    close_up(nm + "_fret", spot - g["Y"] * 0.02, spot + g["Z"] * 0.4 - g["Y"] * 0.06, 50)  # audience side
    close_up(nm + "_neck", spot, spot - g["Z"] * 0.3 - g["Y"] * 0.12 + g["X"] * 0.12, 50)  # player side, thumb
h = (keys_av.head("L Hand") + keys_av.head("R Hand")) / 2
close_up("dbg_keys", h, h + keys_av.fwd * 0.7 + UP * 0.5)
close_up("dbg_keys_full", h - UP * 0.25, h + keys_av.fwd * 2.2 + UP * 0.6 + keys_av.right * 0.6, 28)
h = (drum_av.head("L Hand") + drum_av.head("R Hand")) / 2
close_up("dbg_drums", h, h + drum_av.fwd * 1.1 + UP * 0.6 + drum_av.right * 0.4)
c = drum_av.head("Pelvis") + UP * 0.05  # whole drummer: seat, legs on the pedals, sticks
close_up("dbg_drums_full", c, c + drum_av.fwd * 2.3 + UP * 0.9 - drum_av.right * 1.0, 28)
close_up("dbg_drums_side", c - UP * 0.2, c - UP * 0.1 + drum_av.right * 1.9 + drum_av.fwd * 0.6, 28)
close_up("dbg_drums_front", c + UP * 0.25, c + UP * 0.55 + drum_av.fwd * 1.5, 35)
# portraits: head-and-shoulders views of the musicians, for checking skin and clothes
for nm, av in (("portrait_guitar", gtr_av), ("portrait_bass", bass_av), ("portrait_keys", keys_av),
               ("portrait_drums", drum_av)):
    t = av.head("Head") - UP * 0.22
    close_up(nm, t, t + av.fwd * 1.15 + av.right * 0.3 + UP * 0.08, 50, "portraits")
    f = av.head("Head") + UP * 0.08  # face
    shots.append((nm.replace("portrait", "face"), camera("CAM_face_" + nm, f + av.fwd * 0.8 + av.right * 0.18, f, 60),
                  {"Labels", "HUD"}, "portraits"))


# anim: videos of the progression - guitarist close-ups, the drummer, the bassist and the keyboardist
def video(name, eye, target, lens):
    shots.append((name, camera("CAM_" + name, eye, target, lens), {"Labels", "User"}, "anim"))


g = gtr_av.guitar
spot = g["W"]((g["fret_x"](2), 0, g["top"] + 0.02))
h = gtr_av.head("L Hand") * 0.6 + gtr_av.head("R Hand") * 0.4 + UP * 0.06
video("fretting_hand", spot + g["Z"] * 0.42 - g["Y"] * 0.1, spot - g["Y"] * 0.02, 45)
video("guitarist", h + gtr_av.fwd * 0.95 + UP * 0.1 - gtr_av.right * 0.35, h, 35)
strum_spot = g["W"]((-0.03, 0.0, 0.03))
video("strumming_hand", strum_spot + g["Z"] * 0.5 + g["X"] * 0.12 - g["Y"] * 0.08, strum_spot, 40)
c = drum_av.head("Pelvis")  # drummer from the front, high over the toms: hands, snare, hi-hat and crash
video("drummer", c + drum_av.fwd * 1.6 + UP * 1.1 - drum_av.right * 0.15,
      c + drum_av.fwd * 0.25 + UP * 0.3 - drum_av.right * 0.12, 32)
h = (bass_av.head("L Hand") * 0.4 + bass_av.head("R Hand") * 0.6) + UP * 0.05  # the bassist, plucking side
video("bassist", h + bass_av.fwd * 1.0 + UP * 0.1 + bass_av.right * 0.35, h, 35)
ax = drum_av.drums.kit["kick"]["axle"]  # the kick pedal from the drummer's right: foot, footboard, beater
video("kick_pedal", ax + drum_av.right * 0.65 - drum_av.fwd * 0.15 + UP * 0.08, ax + UP * 0.02, 28)
h = (keys_av.head("L Hand") + keys_av.head("R Hand")) / 2  # hands on the keys, over the left shoulder
video("keyboardist", h - keys_av.fwd * 0.3 + UP * 0.6 - keys_av.right * 0.75, h + keys_av.fwd * 0.05 - UP * 0.05, 30)

scene["shots"] = json.dumps([[n, cam.name, sorted(hid), kind] for n, cam, hid, kind in shots])
scene["video_tag"] = "_".join(PROGRESSION)
scene["still_frame"] = STILL_FRAME
if SONG:  # the song's notes, synthesized, as the videos' soundtrack
    wav = render_song(SONG, os.path.join(OUT, "song_%d-%d.wav" % (FIRST_BAR, FIRST_BAR + len(PROGRESSION) - 1)),
                      (FIRST_BAR - 1) * SONG.beats_per_bar, (FIRST_BAR - 1 + len(PROGRESSION)) * SONG.beats_per_bar)
    seq = scene.sequence_editor_create()
    strips = seq.strips if hasattr(seq, "strips") else seq.sequences
    strips.new_sound("song", wav, 1, 1)
    print("SOUNDTRACK", wav)
scene.camera = cam_pov
render.colour(scene)
lap("cameras")

# ------------------------------------------------------------- save, render
if "chords" in argv:  # chord library: each grip from the audience side and from above the neck (re-poses)
    g, fh = gtr_av.guitar, gtr_av.fret
    spot = g["W"]((g["fret_x"](2), 0, g["top"] + 0.02))
    cams = [("front", camera("CAM_chord_front", spot + g["Z"] * 0.42 - g["Y"] * 0.1, spot - g["Y"] * 0.02, 45)),
            ("top", camera("CAM_chord_top", spot + g["Y"] * 0.36 + g["Z"] * 0.1 - g["X"] * 0.06, spot, 45))]
    render.eevee(scene, "final" if "final" in argv else "draft", PREVIEW)
    for coll in (C_LABELS, C_HUD):
        coll.hide_render = True
    for name, spec in CHORDS.items():
        report("library " + name, fh.apply(name, spec))
        for view, cam in cams:
            scene.camera = cam
            scene.render.filepath = os.path.join(OUT, "chord_%s_%s.png" % (name, view))
            bpy.ops.render.render(write_still=True)
            print("RENDERED", scene.render.filepath)
else:
    bpy.context.preferences.filepaths.save_version = 0  # no .blend1 backups
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("SAVED", BLEND)
    lap("save")
    if "norender" not in argv:
        render.run(argv, OUT)
        lap("render")
print("CACHE", PLACEMENTS.stats(), "|", ANCHORS.stats())
