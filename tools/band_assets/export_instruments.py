"""Exports the band's instruments for the game, from the Blender mockup's processed models (mockup/).

  blender --background --factory-startup --python export_instruments.py -- <out-folder>

Each instrument is built alone, in its own frame (as the mockup builds it for a player at the origin), and
written as <name>.fbx with its playing geometry in <name>.json:

- guitar (the band guitarist's green Clapton Stratocaster) and bass (the P-Bass): the guitar frame, x along
  the neck towards the headstock, z out of the top, origin at the body's centre; fret and string positions;
- drums (the Sonor kit): the drummer's frame, x to the drummer's right, y forward, z up, origin under the
  throne; where each drum and cymbal is struck, the pedals, the sticks and the parts that move;
- keys (the Yamaha EZ-220): the keyboardist's frame (as the drummer's), the keys' playing points from the lowest.

Positions in the JSON are in that frame, in metres. Four empties in each FBX (frame_origin, frame_x, frame_y,
frame_z: the origin and 0.1 m along each axis) let the game map them onto the imported model whatever the
exporter did with the axes. Object names are made safe for Unity (no ':' or '.'); the JSON uses those names.
"""
import json
import math
import os
import re
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, os.path.join(REPO, "mockup"))

import clapton  # noqa: E402  (the mockup's modules, which need bpy)
import drumkit  # noqa: E402
import pbass  # noqa: E402
import yamaha  # noqa: E402


class Rig:
    """The mockup's player frame (x their right, y forward, z up), here at the origin."""

    def __init__(self):
        self.loc = Vector((0.0, 0.0, 0.0))
        self.R = Matrix.Identity(3)

    def w(self, p):
        return self.loc + self.R @ Vector(p)


def safe(name):
    return re.sub(r"[^A-Za-z0-9_]", "_", name)


def plain(x):
    """Vectors, Matrices, objects and containers as JSON values (objects by their safe name)."""
    if isinstance(x, Vector):
        return [round(v, 6) for v in x]
    if isinstance(x, Matrix):
        return [[round(v, 6) for v in row] for row in x]
    if isinstance(x, bpy.types.Object):
        return safe(x.name)
    if isinstance(x, dict):
        return {str(k): plain(v) for k, v in x.items()}
    if isinstance(x, (list, tuple)):
        return [plain(v) for v in x]
    if isinstance(x, float):
        return round(x, 6)
    return x


def fresh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    coll = bpy.data.collections.new("export")
    bpy.context.scene.collection.children.link(coll)
    return coll


def roots(coll):
    return [o for o in coll.objects if o.parent is None]


def bake_mapping(coll):
    """Textures the mockup projects in object space (the P-Bass's sunburst: no UVs on the CAD mesh) as UVs,
    which FBX can carry: for each material whose image takes object coordinates through a Mapping node, the
    faces with it get the same coordinates as UVs, and the image reads them."""
    for o in coll.objects:
        if o.type != "MESH":
            continue
        for index, m in enumerate(o.data.materials):
            if m is None or not m.use_nodes:
                continue
            mapping = next((n for n in m.node_tree.nodes if n.type == "MAPPING"), None)
            image = next((n for n in m.node_tree.nodes if n.type == "TEX_IMAGE"), None)
            if mapping is None or image is None:
                continue
            scale, location = mapping.inputs["Scale"].default_value, mapping.inputs["Location"].default_value
            uv = o.data.uv_layers.get("UVMap") or o.data.uv_layers.new(name="UVMap")
            for poly in o.data.polygons:
                if poly.material_index != index:
                    continue
                for li in poly.loop_indices:
                    co = o.data.vertices[o.data.loops[li].vertex_index].co
                    uv.data[li].uv = (co.x * scale[0] + location[0], co.y * scale[1] + location[1])
            for link in list(image.inputs["Vector"].links):
                m.node_tree.links.remove(link)  # unlinked, an image reads the mesh's UVs
            print("UV %s: %s from object space" % (o.name, m.name))


def export(coll, out, name, data):
    """Renames, adds the frame markers under the instrument's root, and writes the FBX and the JSON."""
    top = roots(coll)
    root = top[0] if len(top) == 1 else None
    if root is None:  # several loose parts (the P-Bass and its inlay dots): one root for all
        root = bpy.data.objects.new(name, None)
        coll.objects.link(root)
        for o in top:
            world = o.matrix_world.copy()
            o.parent = root
            o.matrix_world = world
    root.name = name
    for o in coll.objects:
        if o is not root:
            o.name = safe(o.name)
    for marker, at in (("frame_origin", (0, 0, 0)), ("frame_x", (0.1, 0, 0)), ("frame_y", (0, 0.1, 0)), ("frame_z", (0, 0, 0.1))):
        e = bpy.data.objects.new(marker, None)
        coll.objects.link(e)
        e.parent = root
        e.matrix_world = Matrix.Translation(Vector(at))
    bpy.context.view_layer.update()
    data = plain(data)  # names as they are now
    bpy.ops.object.select_all(action="DESELECT")
    for o in coll.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=True,
                             object_types={"EMPTY", "MESH"}, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False, mesh_smooth_type="FACE",
                             use_mesh_modifiers=True, add_leaf_bones=False, path_mode="COPY", embed_textures=True)
    with open(os.path.join(out, name + ".json"), "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)
    print("EXPORT %s: %d objects" % (name, len(coll.objects)))


def guitar_data(g, kind, strings):
    """The playing geometry of a guitar or bass (the same keys in the mockup's two builders)."""
    frets = g["measured"].get("frets") or g["measured"].get("frets_on_neck")
    # fingertip targets just behind each fret (fret 0: the open string, not fretted), string 0 the thinnest
    points = [[g["fret_point"](s, f) for f in range(frets + 1)] for s in range(strings)]
    span = [g["bridge_x"] + (g["nut_x"] - g["bridge_x"]) * i / 20 for i in range(21)]
    return {
        "kind": kind, "strings": strings, "frets": frets,
        "nut_x": g["nut_x"], "bridge_x": g["bridge_x"], "scale": g["scale"],
        "board_z": g["board_z"], "top": g["top"], "thickness": g["thickness"], "neck_w": g["neck_w"],
        "fret_x": [g["fret_x"](n) for n in range(frets + 1)],
        "fret_points": points,
        # each string from the bridge saddle over the nut to its tuner post
        "string_paths": g["string_paths"],
        # the neck's half width and the height of its back, along it (for the fretting hand's palm and thumb)
        "neck": [{"x": x, "half_w": g["half_w"](x), "back_z": g["back_z"](x), "string_z": g["string_z"](x)} for x in span],
        "strap_top": g["strap_top"],
        "measured": g["measured"],
    }


def main(out):
    os.makedirs(out, exist_ok=True)
    rig = Rig()

    coll = fresh()
    g = clapton.clapton_guitar(rig, (0, 0, 0), (1, 0, 0), (0, 0, 1), "green", coll)
    export(coll, out, "BandGuitar", guitar_data(g, "guitar", 6))

    coll = fresh()
    g = pbass.pbass_guitar(rig, (0, 0, 0), (1, 0, 0), (0, 0, 1), coll)
    bake_mapping(coll)
    export(coll, out, "BandBass", guitar_data(g, "bass", 4))

    coll = fresh()
    k = drumkit.drum_kit(rig, coll)
    export(coll, out, "DrumKit", {
        "seat": k["seat"], "kick_heel": k["kick_heel"], "kick_ball": k["kick_ball"],
        "hihat_heel": k["hihat_heel"], "hihat_ball": k["hihat_ball"], "strike": k["strike"],
        "sticks": [{"object": o, "butt": b, "tip": t} for o, b, t in k["sticks"]],
        "kick": {"beater": k["kick"]["beater"], "board": k["kick"]["board"], "axle": k["kick"]["axle"],
                 "hinge": k["kick"]["hinge"], "axis": k["kick"]["axis"], "hit": k["kick"]["hit"],
                 "travel": k["kick"]["travel"]},
        "hihat": {"top": k["hihat"]["top"], "board": k["hihat"]["board"], "hinge": k["hihat"]["hinge"],
                  "axis": k["hihat"]["axis"], "travel": k["hihat"]["travel"], "drop": k["hihat"]["drop"]},
        "swing": {name: {"object": o, "pivot": p, "axis": a} for name, (o, p, a) in k["swing"].items()},
        "throne": k["throne"],
        "measured": k["measured"],
    })

    coll = fresh()
    kb = yamaha.yamaha_keyboard(rig, coll)
    export(coll, out, "Keyboard", {
        "lowest": 36,  # the EZ-220's 61 keys: C2 (MIDI 36) to C7
        "notes": kb["notes"], "key_objects": kb["key_objects"], "box": kb["box"],
        "height": yamaha.HEIGHT, "front": yamaha.FRONT,
        "measured": kb["measured"],
    })


if __name__ == "__main__":
    main(os.path.abspath(sys.argv[sys.argv.index("--") + 1]))
