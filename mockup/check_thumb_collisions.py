"""Check the deformed hand meshes against the guitar necks over the whole animation.

Run after building the scene (same Blender session):
  blender --background --python mockup/build_vr_band_scene.py --python mockup/check_thumb_collisions.py -- <out> preview only=none
Reports, per player, how deep any thumb vertex sinks into the neck and whether any thumb vertex shows
in front of the fretboard face (which reads as the thumb poking through the neck).
"""
import bpy
from mathutils import Vector

THUMB = ("Bip01 L Finger0", "Bip01 L Finger01", "Bip01 L Finger02")
scene = bpy.context.scene
deps = bpy.context.evaluated_depsgraph_get()

for arm in [o for o in scene.objects if o.type == "ARMATURE" and "guitar_frame" in o]:
    fr = list(arm["guitar_frame"])
    c0, X, Y, Z = Vector(fr[0:3]), Vector(fr[3:6]), Vector(fr[6:9]), Vector(fr[9:12])
    half_w, board, back = fr[12], fr[13], fr[14]   # measured at the 3rd fret
    mid, depth_back = (board + back) / 2, (board - back) / 2
    mesh = next(o for o in arm.children if o.type == "MESH")
    gidx = {mesh.vertex_groups[n].index: n for n in THUMB if n in mesh.vertex_groups}
    owner = {}
    for v in mesh.data.vertices:
        best = max(((g.weight, gidx[g.group]) for g in v.groups if g.group in gidx), default=None)
        if best and best[0] > 0.3:
            owner[v.index] = best[1][-8:]
    worst = {}
    front_frames = []
    for f in range(scene.frame_start, scene.frame_end + 1, 2):
        scene.frame_set(f)
        ev = mesh.evaluated_get(deps)
        me = ev.to_mesh()
        mw = ev.matrix_world
        front = 0.0
        for i, bone in owner.items():
            d = mw @ me.vertices[i].co - c0
            y, z = d.dot(Y), d.dot(Z)
            if abs(y) < half_w and z > board:             # in front of the fretboard face, over the strings
                front = max(front, z - board)
            depth = 0.0
            if z < mid:                                   # rounded back of the neck
                e = (y / half_w) ** 2 + ((z - mid) / depth_back) ** 2
                if e < 1:
                    depth = (1 - e ** 0.5) * depth_back
            elif abs(y) < half_w and z <= board:          # upper half up to the fretboard face
                depth = min(half_w - abs(y), z - mid, board - z)
            if depth > worst.get(bone, (0, 0))[0]:
                worst[bone] = (depth, f)
        ev.to_mesh_clear()
        if front > 0.001:
            front_frames.append(f)
    print("THUMBCHECK %-20s deepest inside neck: %s | frames showing in front of the fretboard: %s" % (
        arm.name, {b: "%.1f mm @%d" % (d * 1000, f) for b, (d, f) in worst.items()}, front_frames[:12]))


# ----------------------------------------------------------- strumming arm vs guitar body
ARM = ("Bip01 R UpperArm", "Bip01 R Forearm", "Bip01 R Hand")


for arm in [o for o in scene.objects if o.type == "ARMATURE" and "guitar_body_grid" in o and "guitar_frame" in o]:
    fr = list(arm["guitar_frame"])
    c0, X, Y, Z = Vector(fr[0:3]), Vector(fr[3:6]), Vector(fr[6:9]), Vector(fr[9:12])
    bg = arm["guitar_body_grid"]
    th = 2 * bg["half_th"]
    cells = list(bg["data"])

    def body_inside(x, y):
        i, j = int((x - bg["x0"]) / bg["cell"]), int((y - bg["y0"]) / bg["cell"])
        return 0 <= i < bg["nx"] and 0 <= j < bg["ny"] and cells[j * bg["nx"] + i] == 1
    mesh = next(o for o in arm.children if o.type == "MESH")
    gidx = {mesh.vertex_groups[n].index: n for n in ARM + ("Bip01 R Finger1", "Bip01 R Finger2", "Bip01 R Finger3",
                                                              "Bip01 R Finger4") if n in mesh.vertex_groups}
    owner = {}
    for v in mesh.data.vertices:
        best = max(((g.weight, gidx[g.group]) for g in v.groups if g.group in gidx), default=None)
        if best and best[0] > 0.3:
            owner[v.index] = best[1][6:]
    idx = list(owner)
    worst, worst_f, hits, per_bone = 0.0, None, 0, {}
    for f in range(scene.frame_start, scene.frame_end + 1, 3):
        scene.frame_set(f)
        ev = mesh.evaluated_get(deps)
        me = ev.to_mesh()
        mw = ev.matrix_world
        for i in idx:
            d = mw @ me.vertices[i].co - c0
            x, y, z = d.dot(X), d.dot(Y), d.dot(Z)
            if abs(z) < th / 2 and body_inside(x, y):
                depth = th / 2 - abs(z)
                hits += 1
                per_bone[owner[i]] = max(per_bone.get(owner[i], 0.0), depth)
                if depth > worst:
                    worst, worst_f = depth, f
        ev.to_mesh_clear()
    print("ARMCHECK %-20s strumming arm inside the guitar body: %d vertex-frames, deepest %.1f mm @%s %s" % (
        arm.name, hits, worst * 1000, worst_f, {b: round(d * 1000, 1) for b, d in per_bone.items()}))
