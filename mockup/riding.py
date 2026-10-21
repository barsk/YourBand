"""The instrument rides on the player: Mixamo capture as the upper body of the guitarists and bassist.

The fretting and strumming solvers work against a guitar that stands still. Afterwards, frame by frame:
- the clip's upper body (spine, neck, head, collarbones) goes on top of the player's own planted hips and
  legs (taken relative to the clip's pelvis, so a clip that walks or rocks does not slide the feet);
- the guitar - with its strings - turns partly with the chest (Spine1): a strapped instrument hangs from
  its strap and rests on the belly and hip, so a player leaning over it (the bass capture hunches ~35 deg)
  bends the arms more instead of tipping the neck to the floor;
- it rests against the body: along its top normal it is moved out of, or in towards, the torso (the
  skinned mesh) until its back just touches; the strap runs from its button to the shoulder;
- the solved hands are carried along with the guitar, so fingertips stay on their frets and strings and
  the pick on the strings; the arms are re-solved from the captured shoulders, keeping their twist;
- optionally the plucking fingers are the clip's (the bassist);
- optionally the player stands straighter than captured (av.upright: the share of the capture's mean lean
  taken out, spread up the spine - the bass capture hunches far over the instrument).
Solved poses are read for all frames first: keying a frame changes how the next ones interpolate.
"""
import math

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

from fretting import key_pose, snapshot
from mocap import two_bone_keep_twist

UPPER = ["Spine", "Spine1", "Spine2", "Neck", "Head", "L Clavicle", "R Clavicle"]
SPREAD = {"Spine": 1 / 3, "Spine1": 2 / 3, "Spine2": 1.0, "Neck": 1.0, "Head": 1.0, "L Clavicle": 1.0,
          "R Clavicle": 1.0}  # how much of the straightening each bone takes (the spine bends gradually)
TORSO = ("Pelvis", "Spine", "Spine1", "Spine2", "L Thigh", "R Thigh")  # skin the instrument rests against
CLEARANCE = 0.004  # m between the clothes and the instrument's back
TURN = 0.4  # share of the chest's turn the instrument follows
REACH = 0.1  # m: torso points this far behind the instrument's back still count as touching it
SMOOTH = 4  # frames: the push follows the body without jitter


def fingers(S):
    return ["%s Finger%d%s" % (S, k, j) for k in range(5) for j in ("", "1", "2")]


def clip_grip(layer, k=None, side="R"):
    """The clip's finger rotations relative to its hand: a grip to put on a hand of ours. Without k, the
    clip's most relaxed moment (least bent middle, ring and little fingers)."""
    if k is None:
        def bend(i):
            W, _ = layer.pose(i)
            return sum(W[side + " Finger%d" % f].to_quaternion().rotation_difference(
                W[side + " Finger%d2" % f].to_quaternion()).angle for f in (2, 3, 4))
        k = min(range(0, len(layer.samples), 6), key=bend)
    W, _ = layer.pose(k)
    Rh = W[side + " Hand"].transposed()
    return {n: Rh @ W[n] for n in fingers(side)}


def torso_vertices(av):
    """Indices of the avatar's mesh vertices that move mainly with the torso and hips (not the arms)."""
    groups = {g.index: g.name for g in av.mesh.vertex_groups}
    keep = {i for i, n in groups.items() if n.replace("Bip01 ", "") in TORSO}
    return np.array([v.index for v in av.mesh.data.vertices
                     if v.groups and max(v.groups, key=lambda e: e.weight).group in keep])


def skin_points(av, idx):
    """World positions of the deformed (posed) mesh vertices idx."""
    ob = av.mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = ob.to_mesh()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ob.to_mesh_clear()
    M = np.array(ob.matrix_world)
    return co.reshape(-1, 3)[idx] @ M[:3, :3].T + M[:3, 3]


def sink(g, T, pts):
    """How deep the torso points sink into the instrument moved by T (from its build position): their depth
    past its back, measured along its top normal, wherever they are inside the body's outline. Negative:
    the gap between the instrument and the body (up to REACH); 0 when the body is not behind it."""
    Ti = np.array(T.inverted())
    p = pts @ Ti[:3, :3].T + Ti[:3, 3] - np.array(g["W"]((0, 0, 0)))
    x, y, z = p @ np.array(g["X"]), p @ np.array(g["Y"]), p @ np.array(g["Z"])
    back = -g["thickness"] / 2 - CLEARANCE
    worst = None
    for i in np.nonzero((z > back - REACH) & (z < back + 0.3))[0]:  # near or past the back; the outline decides
        if (worst is None or z[i] - back > worst) and g["body_inside"](x[i], y[i]):
            worst = z[i] - back
    return 0.0 if worst is None else worst


def _segment(a, b, facing):
    """A frame on the segment a-b: x across, y towards `facing`, z along it (its length), origin mid-way."""
    z = b - a
    y = (facing - z * facing.dot(z) / z.length_squared).normalized()
    M = Matrix((y.cross(z.normalized()), y, z)).transposed().to_4x4()
    M.translation = (a + b) / 2
    return M


def _smooth(push):
    """Hold each push for a few frames on either side, then average: the instrument eases out and back."""
    n = len(push)
    held = [max(push[max(0, i - SMOOTH):i + SMOOTH + 1]) for i in range(n)]
    return [sum(held[max(0, i - SMOOTH):i + SMOOTH + 1]) / len(held[max(0, i - SMOOTH):i + SMOOTH + 1])
            for i in range(n)]


def ride(riders, frames):
    """riders: (avatar, mocap.Layer, instrument objects, pluck) with layer samples matching `frames`; the
    avatars carry their instrument's frame as av.guitar."""
    sc = bpy.context.scene
    keep = sc.frame_current
    solved = []
    for f in frames:  # pass 1: the solved poses
        sc.frame_set(f)
        solved.append([({n: av.crot(n) for n in layer.rt.order}, av.head("Pelvis"),
                        {n: av.head(n) for n in ("L Hand", "R Hand", "L Forearm", "R Forearm")})
                       for av, layer, _, _ in riders])
    rests, straps = [], []
    for av, _, inst, _ in riders:
        for o in inst:
            o.rotation_mode = "QUATERNION"
        rests.append([o.matrix_world.copy() for o in inst])
        st = getattr(av, "strap", None)  # (strap, button, shoulder, facing, shoulder in the collarbone)
        if st:
            st[0].rotation_mode = "QUATERNION"
            straps.append((st, _segment(*st[1:4]).inverted() @ st[0].matrix_world))
        else:
            straps.append(None)

    def captured(k, r):
        """The clip's upper body on the planted hips (world rotations)."""
        layer, Wp = riders[r][1], solved[k][r][0]
        W = dict(Wp)
        W.update(layer.relative(k, "Pelvis", Wp["Pelvis"], UPPER))
        return W

    def lean(W, k, r):
        """The direction from the lower back to the neck."""
        pos = riders[r][1].rt.fk(W, solved[k][r][1])
        return (pos["Neck"][0] - pos["Spine"][0]).normalized()

    # straighten: take out a share (av.upright) of the capture's mean lean away from the solved posture,
    # spread up the spine, so the player stands straighter but still rocks as captured
    straighten = []
    for r, (av, layer, _, _) in enumerate(riders):
        share = getattr(av, "upright", 0.0)
        C = Quaternion()
        if share:
            mean = sum((lean(captured(k, r), k, r) for k in range(len(frames))), Vector()).normalized()
            back = mean.rotation_difference(lean(solved[0][r][0], 0, r))
            C = Quaternion().slerp(back, share)
            print("    %s: capture leans %.0f deg from upright on average; straightened by %.0f deg" % (
                av.arm.name, math.degrees(Vector((0, 0, 1)).angle(mean)), math.degrees(C.angle)))
        straighten.append(C)

    def body(k, r):
        """The captured upper body on the planted hips, and the instrument's turn with the chest."""
        av, layer = riders[r][:2]
        Wp, pelvis, _ = solved[k][r]
        rt = layer.rt
        W = captured(k, r)
        C = straighten[r]
        for n, part in SPREAD.items():
            W[n] = Quaternion().slerp(C, part).to_matrix() @ W[n]
        chest = W["Spine1"] @ Wp["Spine1"].transposed()
        R = Quaternion().slerp(chest.to_quaternion(), TURN).to_matrix()  # the instrument's turn
        pivot = rt.fk(Wp, pelvis)["Spine1"][0]
        T = Matrix.Translation(pivot) @ R.to_4x4() @ Matrix.Translation(-pivot)
        for S in "LR":
            for n in [S + " UpperArm", S + " Forearm"]:  # the arms start out turned with the chest,
                W[n] = chest @ Wp[n]
            for n in [S + " Hand"] + fingers(S):  # the hands hold the instrument
                W[n] = R @ Wp[n]
        return W, R, T

    # pass 2: how far each instrument has to come out of its player's body, frame by frame
    pushes = []
    for r, (av, layer, inst, _) in enumerate(riders):
        idx = torso_vertices(av)
        push = []
        for k, f in enumerate(frames):
            W, R, T = body(k, r)
            layer.rt.apply(W, solved[k][r][1])
            push.append(sink(av.guitar, T, skin_points(av, idx)))
        pushes.append(_smooth(push))
        print("    %s: instrument %.0f mm into the body at worst (frame %d); moved out by %.0f to %.0f mm" % (
            av.arm.name, max(push) * 1000, frames[push.index(max(push))], min(pushes[-1]) * 1000,
            max(pushes[-1]) * 1000))

    prev, left = [None] * len(riders), [0.0] * len(riders)
    idx = [torso_vertices(av) for av, _, _, _ in riders]
    short = [{"L": 0.0, "R": 0.0} for _ in riders]  # how far the hands fall short of the instrument
    for k, f in enumerate(frames):  # pass 3: captured upper body, instrument and hands riding on it
        for r, (av, layer, inst, pluck) in enumerate(riders):
            _, pelvis, heads = solved[k][r]
            rt = layer.rt
            W, R, T = body(k, r)
            T = Matrix.Translation(R @ av.guitar["Z"] * pushes[r][k]) @ T
            if pluck:
                Wc, _ = layer.pose(k)
                Rc = W["R Hand"] @ Wc["R Hand"].transposed()
                for n in fingers("R"):
                    W[n] = Rc @ Wc[n]
            pos = rt.fk(W, pelvis)
            for S in "LR":
                short[r][S] = max(short[r][S], two_bone_keep_twist(
                    W, pos, S + " UpperArm", S + " Forearm", S + " Hand", T @ heads[S + " Hand"],
                    T @ heads[S + " Forearm"]))
            rt.apply(W, pelvis)
            left[r] = max(left[r], sink(av.guitar, T, skin_points(av, idx[r])))
            prev[r] = key_pose(av, snapshot(av, rt.order), f, prev[r])
            for o, M0 in zip(inst, rests[r]):
                o.matrix_world = T @ M0
                o.keyframe_insert("location", frame=f)
                o.keyframe_insert("rotation_quaternion", frame=f)
            if straps[r]:  # from the strap button (on the instrument) to the shoulder (on the collarbone)
                (o, button, _, facing, local), M0 = straps[r]
                shoulder = av.arm.matrix_world @ av.pb("L Clavicle").matrix @ local
                o.matrix_world = _segment(T @ button, shoulder, R @ facing) @ M0
                for path in ("location", "rotation_quaternion", "scale"):
                    o.keyframe_insert(path, frame=f)
    for (av, _, _, _), d, sh in zip(riders, left, short):
        print("    %s: deepest left after the push %.1f mm; hands short of the instrument by up to L %.0f mm,"
              " R %.0f mm" % (av.arm.name, d * 1000, sh["L"] * 1000, sh["R"] * 1000))
    sc.frame_set(keep)
