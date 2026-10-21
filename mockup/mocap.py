"""Mixamo motion capture on Rocketbox avatars.

Mixamo clips (FBX downloaded without skin, mockup/mocap/) are retargeted bone by bone. The two
skeletons are posed differently at rest - Mixamo stands in a T-pose, Rocketbox in an A-pose with bent
forearms and curled fingers - so rotations cannot be copied directly. Each bone instead gets an
anatomical frame in both rest poses, from its own joint positions: the bone's direction plus a reference
axis (the body's right for the torso and feet, its forward for arms and legs, across the palm for hands
and fingers). The rotation that takes the Rocketbox frame onto the Mixamo one aligns the rest poses;
after that every Mixamo rotation away from rest (in world space) is applied to the Rocketbox bone:

    W_rb(t) = Q · W_mx(t) · W_mx_rest⁻¹ · F_mx · F_rbᵀ · W_rb_rest

where Q turns the clip to face the avatar's way. Joint rotations only are transferred; the hips
follow the clip's motion scaled to the avatar's leg length. Everything is computed in Python (world
rotations, then forward kinematics for joint positions) and written to the pose bones in one go, so a
caller can correct contacts with IK before anything is applied.
"""
import math
import os

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
MOCAP_DIR = os.path.join(HERE, "mocap")
UP = Vector((0, 0, 1))
MX = "mixamorig:"

# Rocketbox bone (without "Bip01 ") <- (Mixamo bone, Mixamo aim joint, Rocketbox aim joint, reference axis)
# an aim of None means the bone's own tail (end bones)
BONES = {"Pelvis": ("Hips", "Spine", "Spine", "right"), "Spine": ("Spine", "Spine1", "Spine1", "right"),
         "Spine1": ("Spine1", "Spine2", "Spine2", "right"), "Spine2": ("Spine2", "Neck", "Neck", "right"),
         "Neck": ("Neck", "Head", "Head", "right"), "Head": ("Head", "eyes", "eyes", "right")}
for S, Side in (("L", "Left"), ("R", "Right")):
    BONES.update({
        S + " Clavicle": (Side + "Shoulder", Side + "Arm", S + " UpperArm", "fwd"),
        S + " UpperArm": (Side + "Arm", Side + "ForeArm", S + " Forearm", "fwd"),
        S + " Forearm": (Side + "ForeArm", Side + "Hand", S + " Hand", "fwd"),
        S + " Hand": (Side + "Hand", Side + "HandMiddle1", S + " Finger2", "palm" + S),
        S + " Thigh": (Side + "UpLeg", Side + "Leg", S + " Calf", "fwd"),
        S + " Calf": (Side + "Leg", Side + "Foot", S + " Foot", "fwd"),
        S + " Foot": (Side + "Foot", Side + "ToeBase", S + " Toe0", "right"),
        S + " Toe0": (Side + "ToeBase", Side + "Toe_End", None, "right")})
    for k, finger in enumerate(("Thumb", "Index", "Middle", "Ring", "Pinky")):
        rb = ["%s Finger%d%s" % (S, k, j) for j in ("", "1", "2")]
        for i in range(3):
            BONES[rb[i]] = ("%sHand%s%d" % (Side, finger, i + 1), "%sHand%s%d" % (Side, finger, i + 2),
                            rb[i + 1] if i < 2 else None, "palm" + S)


def _frame(p, s):
    """Orthonormal frame (columns) with x along p and y towards s."""
    x = p.normalized()
    y = (s - x * s.dot(x)).normalized()
    return Matrix((x, y, x.cross(y))).transposed()


def _rot(M):
    return M.to_3x3().normalized()


class Clip:
    """A Mixamo FBX, imported, sampled at the requested frames and removed again."""

    def __init__(self, name):
        before = set(bpy.data.objects)
        actions = set(bpy.data.actions)
        bpy.ops.import_scene.fbx(filepath=os.path.join(MOCAP_DIR, name))
        new = [o for o in bpy.data.objects if o not in before]
        self.arm = next(o for o in new if o.type == "ARMATURE")
        self._new, self._actions = new, [a for a in bpy.data.actions if a not in actions]
        self.frames = tuple(self.arm.animation_data.action.frame_range)
        mw = self.arm.matrix_world
        self.rest = {b.name[len(MX):]: (_rot(mw @ b.matrix_local), mw @ b.head_local, mw @ b.tail_local)
                     for b in self.arm.data.bones}

    def sample(self, frames):
        """{Mixamo bone: (world rotation, world head)} at each (fractional) frame."""
        sc, mw, out = bpy.context.scene, self.arm.matrix_world, []
        keep = sc.frame_current
        for f in frames:
            sc.frame_set(int(math.floor(f)), subframe=f - math.floor(f))
            out.append({pb.name[len(MX):]: (_rot(mw @ pb.matrix), mw @ pb.head) for pb in self.arm.pose.bones})
        sc.frame_set(keep)
        return out

    def remove(self):
        for o in self._new:
            bpy.data.objects.remove(o)
        for a in self._actions:
            bpy.data.actions.remove(a)


class Retarget:
    """Clip rotations onto an avatar (avatars.Avatar)."""

    def __init__(self, av, clip):
        self.av = av
        mr = clip.rest
        mx = {n: r[1] for n, r in mr.items()}
        mx["eyes"] = (mx["LeftEye"] + mx["RightEye"]) / 2
        rb = {n: av.rhead(n) for n in BONES}
        rb["eyes"] = (av.rhead("LEye") + av.rhead("REye")) / 2
        ref = {}
        for tag, m, r in (("right", mx["RightUpLeg"] - mx["LeftUpLeg"], rb["R Thigh"] - rb["L Thigh"]),
                          ("palmL", mx["LeftHandPinky1"] - mx["LeftHandIndex1"], rb["L Finger4"] - rb["L Finger1"]),
                          ("palmR", mx["RightHandPinky1"] - mx["RightHandIndex1"], rb["R Finger4"] - rb["R Finger1"])):
            ref[tag] = (m, r)
        ref["fwd"] = (UP.cross(ref["right"][0]), UP.cross(ref["right"][1]))
        # the clip faces its own way: Q turns its (right, forward, up) onto the avatar's
        rm = Vector((ref["right"][0].x, ref["right"][0].y, 0)).normalized()  # level: Q must be a pure turn
        self.Q0 = Matrix((av.right, av.fwd, UP)).transposed() @ Matrix((rm, UP.cross(rm), UP))
        self.yaw = 0.0
        # the Rocketbox FBX has no real bone tails: end bones continue their parent's direction (fingers),
        # or point level along the foot (toes)
        tail = {}
        for n, (m, m_aim, r_aim, axis) in BONES.items():
            if r_aim:
                tail[n] = rb[r_aim]
            elif n.endswith("Toe0"):
                d = rb[n] - rb[n[:2] + "Foot"]
                tail[n] = rb[n] + Vector((d.x, d.y, 0)).normalized() * 0.05
            else:
                tail[n] = rb[n] + (rb[n] - av.rhead(av.pb(n).parent.name[6:])) * 0.95
        self.align = {}
        for n, (m, m_aim, r_aim, axis) in BONES.items():
            pm = (mx[m_aim] if m_aim in mx else mr[m][2]) - mr[m][1]
            if n.endswith("Toe0"):
                pm = Vector((pm.x, pm.y, 0))
            pr = tail[n] - rb[n]
            Fm = _frame(pm, ref[axis][0])
            Fr = _frame(pr, ref[axis][1])
            # world rotation of the avatar bone = Q · Wmx(t) · Wmx_rest⁻¹ · Fm · Frᵀ · Wrb_rest
            self.align[n] = (m, mr[m][0].transposed() @ Fm @ Fr.transposed() @ av.rrot(n))
        legs = lambda h: (h["LeftLeg"] - h["LeftUpLeg"]).length + (h["LeftFoot"] - h["LeftLeg"]).length
        self.k = ((rb["L Calf"] - rb["L Thigh"]).length + (rb["L Foot"] - rb["L Calf"]).length) / legs(mx)
        # hierarchy (parents first) and rest data for forward kinematics
        arm = av.arm
        self.order, self.parent = [], {}

        def walk(b):
            n = b.name[6:]
            if n in BONES:
                self.order.append(n)
                self.parent[n] = b.parent.name[6:] if b.parent else None
            for c in b.children:
                walk(c)

        for b in arm.data.bones:
            if b.parent is None:
                walk(b)
        self.rest = {n: (av.rrot(n), av.rhead(n), tail[n]) for n in self.order}

    @property
    def Q(self):
        return Matrix.Rotation(self.yaw, 3, "Z") @ self.Q0

    def rotations(self, sample):
        """World rotations of the avatar's bones for one clip sample."""
        Q = self.Q
        return {n: Q @ sample[m][0] @ A for n, (m, A) in self.align.items()}

    def hips(self, sample, mean):
        """Offset of the hips from their mean position in the clip, in the avatar's world and size."""
        return self.Q @ ((sample["Hips"][1] - mean) * self.k)

    def fk(self, W, pelvis):
        """Joint positions {bone: (head, tail)} for world rotations W with the pelvis head at `pelvis`."""
        pos = {}
        for n in self.order:
            R0, h0, t0 = self.rest[n]
            p = self.parent[n]
            if p is None:
                h = Vector(pelvis)
            else:
                Rp0, hp0, _ = self.rest[p]
                h = pos[p][0] + W[p] @ Rp0.transposed() @ (h0 - hp0)
            pos[n] = (h, h + W[n] @ R0.transposed() @ (t0 - h0))
        return pos

    def apply(self, W, pelvis):
        """Write world rotations W and the pelvis position into the pose bones (one scene update)."""
        arm, Ra = self.av.arm, self.av.Ra
        local = {}
        for n in self.order:
            b = arm.data.bones["Bip01 " + n]
            pb = arm.pose.bones["Bip01 " + n]
            R = Ra.transposed() @ W[n]
            L = b.matrix_local.to_3x3()
            p = self.parent[n]
            B = L.transposed() @ R if p is None else (
                L.transposed() @ arm.data.bones["Bip01 " + p].matrix_local.to_3x3() @ local[p].transposed() @ R)
            local[n] = R
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = B.to_quaternion()
            if p is None:
                pb.location = L.transposed() @ (arm.matrix_world.inverted() @ Vector(pelvis) - b.matrix_local.translation)
        bpy.context.view_layer.update()


def two_bone_keep_twist(W, pos, upper, lower, end, target, pole):
    """Move the end of a limb to `target` with analytic IK, swinging the two bones from their current
    (captured) rotations so their twist is kept; the end bone's world rotation is unchanged."""
    from avatars import two_bone
    a, b, c = pos[upper][0], pos[lower][0], pos[end][0]
    l1, l2 = (b - a).length, (c - b).length
    b2, c2 = two_bone(a, Vector(target), l1, l2, Vector(pole))
    W[upper] = (b - a).rotation_difference(b2 - a).to_matrix() @ W[upper]
    W[lower] = (c - b).rotation_difference(c2 - b2).to_matrix() @ W[lower]
    return (c2 - Vector(target)).length


class Layer:
    """A clip as a body layer for an avatar: retargeted, sampled at our times (seconds; looped over `loop`
    clip frames from `start` if the song is longer than the clip), with the hips' offset from their mean
    in the avatar's size. `height` is the mean hip height above the clip's floor, scaled to the avatar."""

    def __init__(self, av, name, seconds, start=1.0, fps=30.0, loop=None, offset=0.0):
        clip = Clip(name)
        self.rt = Retarget(av, clip)
        loop = loop or (clip.frames[1] - start)
        self.samples = clip.sample([start + ((s + offset) * fps) % loop for s in seconds])
        every = clip.sample([start + loop * i / 60 for i in range(60)])
        self.mean = sum((smp["Hips"][1] for smp in every), Vector()) / len(every)
        self.height = self.mean.z * self.rt.k
        clip.remove()

    def pose(self, k):
        """World rotations and the hips' offset (avatar world) at sample k."""
        smp = self.samples[k]
        return self.rt.rotations(smp), self.rt.hips(smp, self.mean)

    def relative(self, k, base, W_base, bones):
        """Rotations of `bones` taken relative to the clip's bone `base` and put on our `W_base`: the clip's
        upper body on a pelvis of our own (legs and hips stay planted)."""
        W, _ = self.pose(k)
        Rb = W_base @ W[base].transposed()
        return {n: Rb @ W[n] for n in bones}
