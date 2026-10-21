"""Yamaha EZ-220 keyboard (SolidWorks assembly exported to OBJ, model_images/yamaha-keyboard-ex-220).

All 61 keys are separate parts, so every key's playing surface comes straight from the model: the 36
white and 25 black keys are found by their size, sorted from the lowest note, and measured. The export
has no appearances (everything is "Steel - Satin"), so parts get materials by shape: black housing and
speaker grilles, white and black keys, light panel buttons, a dark logo plate. The housing and the two
perforated grilles (about 355k triangles each) are decimated.
"""
import math
import os

import bpy
from mathutils import Matrix, Vector

from strat import _mat
from clapton import _bbox

HERE = os.path.dirname(os.path.abspath(__file__))
OBJ_PATH = os.path.normpath(os.path.join(HERE, "..", "model_images", "yamaha-keyboard-ex-220",
                                         "YAMAHA EZ-220 Assembly v1.obj"))
INCH = 0.0254
# model space (inches): keys along +x from the lowest note, the player at -y, z up. Same axes as the rig.
HEIGHT = 0.95      # m, top of the white keys above the floor (standing player)
FRONT = 0.20       # m, front edge of the white keys ahead of the player
MAX_FACES = 60000
GRILLE_FACES = 15000


def _classify(parts):
    """white keys, black keys, the housing and grilles, and the rest (panel buttons, logo plate, felt)."""
    size = {n: (lambda b: b[1] - b[0])(_bbox(o)) for n, o in parts.items()}
    white = sorted((n for n, s in size.items() if 0.8 < s.x < 0.9 and 5 < s.y < 5.8),
                   key=lambda n: _bbox(parts[n])[0].x)
    black = sorted((n for n, s in size.items() if 0.4 < s.x < 0.6 and 3 < s.y < 3.8),
                   key=lambda n: _bbox(parts[n])[0].x)
    housing = max(size, key=lambda n: size[n].x * size[n].y)
    grilles = [n for n, s in size.items() if 6 < s.x < 7 and 4.5 < s.y < 6]
    return white, black, housing, grilles


def _lighten(parts, heavy):
    for name, o in parts.items():
        me = o.data
        me.shade_smooth()
        me.set_sharp_from_angle(angle=math.radians(35))
        o.modifiers.new("weld", "WELD").merge_threshold = 0.0005
        target = heavy.get(name, MAX_FACES)
        if len(me.polygons) > target:
            o.modifiers.new("decimate", "DECIMATE").ratio = target / len(me.polygons)
        wn = o.modifiers.new("normals", "WEIGHTED_NORMAL")
        wn.mode, wn.keep_sharp, wn.weight = "FACE_AREA", True, 100
    dg = bpy.context.evaluated_depsgraph_get()
    for o in parts.values():
        old = o.data
        o.data = bpy.data.meshes.new_from_object(o.evaluated_get(dg))
        o.modifiers.clear()
        bpy.data.meshes.remove(old)


def _prepare():
    """Import, classify and lighten the model; the parts' boxes measured before welding (cached)."""
    before = set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=OBJ_PATH, use_split_objects=True, use_split_groups=True)
    parts = {o.name: o for o in bpy.data.objects if o not in before and o.type == "MESH"}
    white, black, housing, grilles = _classify(parts)
    assert len(white) == 36 and len(black) == 25, (len(white), len(black))
    boxes = {n: _bbox(o) for n, o in parts.items()}
    _lighten(parts, {housing: MAX_FACES, **{g: GRILLE_FACES for g in grilles}})
    return parts, dict(white=white, black=black, housing=housing, grilles=grilles, boxes=boxes)


def yamaha_keyboard(rig, coll, height=HEIGHT, front=FRONT):
    """Import the keyboard into `coll`, centred in front of the player at the rig origin. Returns the key
    playing points in world space and the keyboard's footprint (for the stand)."""
    import cache
    parts, ms = cache.model("yamaha", [OBJ_PATH, OBJ_PATH[:-4] + ".mtl"], ["yamaha.py"], _prepare)
    white, black, housing, grilles, boxes = ms["white"], ms["black"], ms["housing"], ms["grilles"], ms["boxes"]
    name_of = {o.as_pointer(): n for n, o in parts.items()}

    def _bbox(o):  # the parts' boxes as measured on import (before welding)
        return boxes[name_of[o.as_pointer()]]

    lo, hi = _bbox(parts[housing])
    wlo, whi = _bbox(parts[white[0]])
    blo, bhi = _bbox(parts[black[0]])
    centre_x, front_y, top_z = (lo.x + hi.x) / 2, wlo.y, whi.z
    origin = Vector((centre_x, front_y - front / INCH, top_z - height / INCH))

    def W(p):  # model inches -> world
        return rig.w((Vector(p) - origin) * INCH)

    # playing points: on top of each key, 4 cm behind a white key's front edge, 1.5 cm behind a black one's
    keys = {}
    for names, behind in ((white, 0.04), (black, 0.015)):
        for n in names:
            a, b = _bbox(parts[n])
            keys[n] = W(((a.x + b.x) / 2, a.y + behind / INCH, b.z))
    # semitone order from the lowest C: merge the two rows by x
    order = sorted(white + black, key=lambda n: _bbox(parts[n])[0].x + _bbox(parts[n])[1].x)
    notes = [keys[n] for n in order]

    pitch = (_bbox(parts[white[1]])[0].x - wlo.x) * 25.4
    mats = {"housing": _mat("ez_housing", (0.022, 0.022, 0.025), 0.55),
            "grille": _mat("ez_grille", (0.012, 0.012, 0.014), 0.75),
            "white": _mat("ez_key_white", (0.9, 0.9, 0.88), 0.28),
            "black": _mat("ez_key_black", (0.012, 0.012, 0.013), 0.3),
            "button": _mat("ez_button", (0.8, 0.8, 0.8), 0.4),
            "dark": _mat("ez_trim", (0.05, 0.05, 0.055), 0.4, 0.6)}
    root = bpy.data.objects.new("Yamaha_EZ220", None)
    coll.objects.link(root)
    for n, o in parts.items():
        role = ("white" if n in white else "black" if n in black else "housing" if n == housing
                else "grille" if n in grilles else "dark" if _bbox(o)[1].x - _bbox(o)[0].x > 2 else "button")
        o.data.materials.clear()
        o.data.materials.append(mats[role])
        for col in list(o.users_collection):
            col.objects.unlink(o)
        coll.objects.link(o)
        o.parent = root
    root.matrix_world = (Matrix.Translation(rig.w(Vector())) @ rig.R.to_4x4() @ Matrix.Scale(INCH, 4)
                         @ Matrix.Translation(-origin))
    bpy.context.view_layer.update()
    return {"notes": notes, "white": [keys[n] for n in white], "black": [keys[n] for n in black],
            "key_objects": [parts[n] for n in order],  # same order as notes
            # footprint in the rig frame, for the stand: front and back y, underside z, half width
            "box": ((lo.y - origin.y) * INCH, (hi.y - origin.y) * INCH, (lo.z - origin.z) * INCH,
                    (hi.x - lo.x) * INCH / 2),
            "measured": dict(keys=len(notes), white_pitch_mm=round(pitch, 1),
                             black_raise_mm=round((bhi.z - top_z) * 25.4, 1),
                             faces=sum(len(o.data.polygons) for o in parts.values()))}
