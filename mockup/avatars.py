"""Rocketbox avatars posed for the VR band mockup, down to individual finger joints.

Every pose is solved in world space and written straight into the pose bones:
arms and legs with analytic two-bone IK, hands from a target palm frame, and
fingers by searching curl/spread angles until the fingertip lands on its
target (a string at a fret, a key, a drumstick).
"""
import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

AV_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "avatars")
SIDE = {"l": "L", "r": "R"}
DIP_PIP = 0.7  # the fingertip joint bends ~0.7x the middle joint (they are tendon-coupled)


def _link(o, coll):
    for c in list(o.users_collection):
        c.objects.unlink(o)
    coll.objects.link(o)


def frame(F, N):
    """Orthonormal frame with columns (F, N, F x N): finger direction, palm normal, curl axis."""
    F = Vector(F).normalized()
    N = Vector(N)
    N = (N - F * N.dot(F)).normalized()
    return Matrix((F, N, F.cross(N))).transposed()


def two_bone(a, t, l1, l2, pole):
    """Analytic two-bone IK: returns (middle joint, reached end)."""
    d = t - a
    dn = d.normalized()
    L = min(max(d.length, abs(l1 - l2) + 1e-4), l1 + l2 - 1e-4)
    t = a + dn * L
    x = (l1 * l1 - l2 * l2 + L * L) / (2 * L)
    h = math.sqrt(max(l1 * l1 - x * x, 0.0))
    pv = pole - a
    pv = pv - dn * pv.dot(dn)
    pv = pv.normalized() if pv.length > 1e-6 else Vector((0, 0, -1))
    return a + dn * x + pv * h, t


# ---------------------------------------------------------------- materials
# Rocketbox packs skin and clothes into one texture atlas per body and per head. A per-texel "look" map
# (cached next to the textures) separates them: R = skin weight (drives subsurface scattering),
# G = roughness, B = cloth sheen. Skin is found by colour: chromaticity close to the avatar's own face.
LOOK_VERSION = 4
SKIN_ROUGH, CLOTH_ROUGH, HAIR_ROUGH, LEATHER_ROUGH, WET_ROUGH = 0.42, 0.85, 0.55, 0.4, 0.15
CLOTH_SHEEN, HAIR_SHEEN = 0.5, 0.05
SKIN_SPEC, CLOTH_SPEC, HAIR_SPEC = 0.35, 0.25, 0.12  # dark hair reflecting a white room read as grey
SSS_SCALE, SSS_RADIUS = 0.002, (1.0, 0.4, 0.25)  # mean free path 2 / 0.8 / 0.5 mm (red, green, blue);
                                                 # longer paths or forward scattering make thin fingers glow
LEATHER = {"Male_Adult_04": 0.22}  # avatars in leather: dark, grey garment texels below this are leather
# Rocketbox's own greyscale specular maps (raw values 0 .. ~0.35): when present they set the reflectivity
# per texel (oily T-zone, wet eyes and lips, metal zips, near-matte cloth) and make shiny spots glossier
USE_SPEC_MAPS = True
SPEC_FROM, SPEC_TO = 0.35, (0.1, 0.7)  # raw map value -> Principled Specular IOR Level
GLOSS_FROM, GLOSS_TO = 0.5, (1.0, 0.4)  # raw map value -> roughness factor
HAIR_SPEC_DAMP = 0.45                   # spec-map reflectivity kept on hair (non-skin head texels)


def _pixels(path):
    im = bpy.data.images.load(path, check_existing=True)
    w, h = im.size
    px = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(px)
    return px.reshape(h, w, 4)[:, :, :3]  # rows bottom-up, sRGB-encoded values


def _blur(a, r):
    """Box blur of radius r (separable, by cumulative sums)."""
    for axis in (0, 1):
        c = np.cumsum(np.pad(a, [(r + 1, r) if i == axis else (0, 0) for i in range(2)], mode="edge"), axis=axis)
        hi, lo = [slice(None)] * 2, [slice(None)] * 2
        hi[axis], lo[axis] = slice(2 * r + 1, None), slice(0, -2 * r - 1)
        a = (c[tuple(hi)] - c[tuple(lo)]) / (2 * r + 1)
    return a


def _skin_tone(head_px):
    """Median colour of nose and cheeks; Rocketbox heads all share the same face layout."""
    h, w, _ = head_px.shape
    return np.median(head_px[int(h * 0.61):int(h * 0.71), int(w * 0.4):int(w * 0.6)].reshape(-1, 3), axis=0)


def _look(color_path, tone, kind, leather=None):
    out = color_path.replace("_color.tga", "_look%d.png" % LOOK_VERSION)
    if os.path.exists(out):
        return out
    px = _pixels(color_path)
    h, w, _ = px.shape
    ref = tone()
    lum, L0 = px.mean(axis=2), ref.mean()
    chroma = px / (px.sum(axis=2, keepdims=True) + 1e-4)
    skin = np.exp(-(np.linalg.norm(chroma - ref / ref.sum(), axis=2) / 0.05) ** 2)
    skin *= np.clip((lum / L0 - 0.4) / 0.25, 0, 1) * np.clip((2.2 - lum / L0) / 0.5, 0, 1)
    skin = np.clip((_blur(skin, 10) - 0.15) / 0.3, 0, 1)  # closes the gaps where palms are paler than the face

    skin_rough = np.full((h, w), SKIN_ROUGH, np.float32)
    rough = np.full((h, w), CLOTH_ROUGH if kind == "body" else HAIR_ROUGH, np.float32)
    sheen = np.full((h, w), CLOTH_SHEEN if kind == "body" else HAIR_SHEEN, np.float32)
    if kind == "body" and leather:
        sat = (px.max(axis=2) - px.min(axis=2)) / (px.max(axis=2) + 1e-4)  # brown cloth in shadow stays out
        lea = _blur(((lum < leather) & (sat < 0.2)).astype(np.float32), 2)
        rough += (LEATHER_ROUGH - CLOTH_ROUGH) * lea
        sheen *= 1 - lea
    if kind == "head":  # the eyeball island, lower left of the head atlas, is wet
        yy, xx = np.mgrid[0:h, 0:w]
        wet = ((xx / w - 0.27) ** 2 + (yy / h - 0.075) ** 2) < 0.07 ** 2
        rough[wet], skin_rough[wet], sheen[wet] = WET_ROUGH, WET_ROUGH, 0.0
    rough = skin * skin_rough + (1 - skin) * rough

    img = bpy.data.images.new(os.path.basename(out), w, h)
    img.colorspace_settings.name = "Non-Color"
    img.pixels.foreach_set(np.dstack([skin, rough, sheen * (1 - skin), np.ones((h, w))]).astype(np.float32).ravel())
    img.filepath_raw, img.file_format = out, "PNG"
    img.save()
    bpy.data.images.remove(img)
    return out


def _hair_cards(color_path):
    """Hair-card texture with its pure-black root bands recoloured to the hair's own median colour: the
    bands are opaque in the texture and show as black stripes wherever a card stands clear of the scalp."""
    out = color_path.replace("_color.tga", "_cards%d.png" % LOOK_VERSION)
    if os.path.exists(out):
        return out
    im = bpy.data.images.load(color_path, check_existing=True)
    w, h = im.size
    px = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(px)
    px = px.reshape(h, w, 4)
    rgb, alpha = px[:, :, :3], px[:, :, 3]
    lum = rgb.mean(axis=2)
    hair = np.median(rgb[(alpha > 0.9) & (lum > 0.05)], axis=0)
    k = np.clip((0.05 - lum) / 0.03, 0, 1)[:, :, None]  # 1 for black, 0 from lum 0.05 up
    px[:, :, :3] = rgb * (1 - k) + hair * k
    img = bpy.data.images.new(os.path.basename(out), w, h, alpha=True)
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw, img.file_format = out, "PNG"
    img.save()
    bpy.data.images.remove(img)
    return out


def _single_sided(obj, k):
    """Rocketbox hair cards come as coincident front/back copies, and the back copy carries normals that
    point into the head: wherever the renderer hits it first the hair shows black bands. Both engines
    shade either side of one face, so keep only the copy whose normals agree with its face."""
    me = obj.data
    pairs = {}
    for p in me.polygons:
        if p.material_index == k:
            key = tuple(sorted(tuple(round(c, 5) for c in me.vertices[v].co) for v in p.vertices))
            agree = sum((me.corner_normals[i].vector for i in p.loop_indices), Vector()).dot(p.normal)
            pairs.setdefault(key, []).append((agree, p.index))
    drop = [i for group in pairs.values() for _, i in sorted(group, reverse=True)[1:]]
    if drop:
        bm = bmesh.new()
        bm.from_mesh(me)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in drop], context="FACES_ONLY")
        bm.to_mesh(me)
        bm.free()


def _fix_materials(mesh, name, tex_dir):
    head = next((f for f in os.listdir(tex_dir) if f.endswith("_head_color.tga")), None)
    cache = {}

    def tone():
        if "tone" not in cache:
            cache["tone"] = _skin_tone(_pixels(os.path.join(tex_dir, head)))
        return cache["tone"]

    for slot in mesh.material_slots:
        m = slot.material
        if not m or not m.node_tree:
            continue
        nt = m.node_tree
        color = spec_map = None
        for n in list(nt.nodes):
            if n.type != "TEX_IMAGE" or not n.image:
                continue
            fn = os.path.basename(n.image.filepath.replace("\\", "/"))
            path = os.path.join(tex_dir, fn)
            if not os.path.exists(path) or ("_specular" in fn and not USE_SPEC_MAPS):
                nt.nodes.remove(n)
                continue
            n.image.filepath = path
            n.image.reload()
            if "normal" in fn or "_specular" in fn:
                n.image.colorspace_settings.name = "Non-Color"
                if "_specular" in fn:
                    spec_map = n
            elif fn.endswith("_color.tga") and color is None:
                color = (n, path)
        bsdf = next((n for n in nt.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if not bsdf:
            continue
        # the FBX importer turns Rocketbox's reflection factor into Metallic = 1, which made skin and
        # clothes look like tinted metal
        bsdf.inputs["Metallic"].default_value = 0.0
        for l in list(bsdf.inputs["Specular IOR Level"].links):  # the importer's specular-map link
            nt.links.remove(l)
        bsdf.inputs["Specular IOR Level"].default_value = 0.3
        if "opacity" in m.name:  # hair cards, eyelashes
            bsdf.inputs["Roughness"].default_value = HAIR_ROUGH
            bsdf.inputs["Specular IOR Level"].default_value = HAIR_SPEC
            # the importer feeds the texture's colour into Alpha (dark hair came out as faint see-through
            # sheets); use its real alpha channel, with the faintest fringe cut away
            for l in list(bsdf.inputs["Alpha"].links):
                nt.links.remove(l)
            if color:
                color[0].image = bpy.data.images.load(_hair_cards(color[1]), check_existing=True)
                for n in nt.nodes:  # the importer's second copy of the texture, now unused
                    if n.type == "TEX_IMAGE" and n is not color[0] and not any(o.links for o in n.outputs):
                        nt.nodes.remove(n)
                        break
                cut = nt.nodes.new("ShaderNodeMapRange")
                cut.inputs["From Min"].default_value = 0.05
                nt.links.new(color[0].outputs["Alpha"], cut.inputs["Value"])
                nt.links.new(cut.outputs["Result"], bsdf.inputs["Alpha"])
            m.surface_render_method = "DITHERED"
            m.use_transparent_shadow = True
            _single_sided(mesh, mesh.material_slots.find(m.name))
            continue
        if color and head:
            kind = "head" if "_head" in m.name else "body"
            look = nt.nodes.new("ShaderNodeTexImage")
            look.image = bpy.data.images.load(_look(color[1], tone, kind, LEATHER.get(name)), check_existing=True)
            look.image.colorspace_settings.name = "Non-Color"
            sep = nt.nodes.new("ShaderNodeSeparateColor")
            nt.links.new(look.outputs["Color"], sep.inputs["Color"])
            nt.links.new(sep.outputs["Blue"], bsdf.inputs["Sheen Weight"])
            nt.links.new(sep.outputs["Red"], bsdf.inputs["Subsurface Weight"])
            bsdf.inputs["Subsurface Scale"].default_value = SSS_SCALE
            spec = nt.nodes.new("ShaderNodeMapRange")
            if spec_map:
                spec.inputs["From Max"].default_value = SPEC_FROM
                spec.inputs["To Min"].default_value, spec.inputs["To Max"].default_value = SPEC_TO
                nt.links.new(spec_map.outputs["Color"], spec.inputs["Value"])
                gloss = nt.nodes.new("ShaderNodeMapRange")
                gloss.inputs["From Max"].default_value = GLOSS_FROM
                gloss.inputs["To Min"].default_value, gloss.inputs["To Max"].default_value = GLOSS_TO
                nt.links.new(spec_map.outputs["Color"], gloss.inputs["Value"])
                rough = nt.nodes.new("ShaderNodeMath")
                rough.operation = "MULTIPLY"
                nt.links.new(sep.outputs["Green"], rough.inputs[0])
                nt.links.new(gloss.outputs["Result"], rough.inputs[1])
                nt.links.new(rough.outputs["Value"], bsdf.inputs["Roughness"])
                if kind == "head":  # strand glints in the map still read as a grey veil on dark hair
                    damp = nt.nodes.new("ShaderNodeMapRange")
                    damp.inputs["To Min"].default_value = HAIR_SPEC_DAMP
                    nt.links.new(sep.outputs["Red"], damp.inputs["Value"])
                    scaled = nt.nodes.new("ShaderNodeMath")
                    scaled.operation = "MULTIPLY"
                    nt.links.new(spec.outputs["Result"], scaled.inputs[0])
                    nt.links.new(damp.outputs["Result"], scaled.inputs[1])
                    spec = scaled
            else:  # reflectivity by skin weight only
                spec.inputs["To Min"].default_value = HAIR_SPEC if kind == "head" else CLOTH_SPEC
                spec.inputs["To Max"].default_value = SKIN_SPEC
                nt.links.new(sep.outputs["Red"], spec.inputs["Value"])
                nt.links.new(sep.outputs["Green"], bsdf.inputs["Roughness"])
            nt.links.new(spec.outputs[0], bsdf.inputs["Specular IOR Level"])
            bsdf.inputs["Subsurface Radius"].default_value = SSS_RADIUS
        else:
            bsdf.inputs["Roughness"].default_value = 0.7


class Avatar:
    def __init__(self, name, coll, loc, yaw):
        before = set(bpy.data.objects)
        # the FBX carries an animation take that would reset placement and pose on render
        bpy.ops.import_scene.fbx(filepath=os.path.join(AV_DIR, name, "Export", name + ".fbx"), use_anim=False)
        new = [o for o in bpy.data.objects if o not in before]
        for o in new:
            o.animation_data_clear()
            _link(o, coll)
            if o.type == "EMPTY":
                o.hide_render = o.hide_viewport = True
        self.arm = next(o for o in new if o.type == "ARMATURE")
        self.mesh = next(o for o in new if o.type == "MESH")
        self.arm.name = "RIG_" + name
        self.mesh.name = "AV_" + name
        _fix_materials(self.mesh, name, os.path.join(AV_DIR, name, "Textures"))

        # Rocketbox avatars face -Y; our rig frame has x = their right, y = forward.
        self.loc = Vector((loc[0], loc[1], 0.0))
        self.R = Matrix.Rotation(yaw, 3, "Z")
        self.arm.matrix_world = (Matrix.Translation(self.loc) @ Matrix.Rotation(yaw + math.pi, 4, "Z")
                                 @ self.arm.matrix_world)
        bpy.context.view_layer.update()
        self.Ra = self.arm.matrix_world.to_3x3().normalized()
        self.right = self.R @ Vector((1, 0, 0))
        self.fwd = self.R @ Vector((0, 1, 0))
        self.up = Vector((0, 0, 1))
        self._Rh = {}
        self._axes = {}
        self.log = []
        self.angles = {}
        self._pending = {}  # bone -> pose matrix set but not yet evaluated (see _set)

    # ------------------------------------------------------------ frames
    def _pb(self, n):
        return self.arm.pose.bones["Bip01 " + n]

    def pb(self, n):
        self.flush()
        return self._pb(n)

    def flush(self):
        """Evaluate bone rotations that were set without a scene update."""
        if self._pending:
            self._pending.clear()
            bpy.context.view_layer.update()

    def head(self, n):
        self.flush()
        return self.arm.matrix_world @ self._pb(n).head

    def rhead(self, n):
        return self.arm.matrix_world @ self._pb(n).bone.head_local

    def rrot(self, n):
        return self.Ra @ self._pb(n).bone.matrix_local.to_3x3()

    def crot(self, n):
        self.flush()
        return self.Ra @ self._pb(n).matrix.to_3x3()

    def world(self, p):
        return self.loc + self.R @ Vector(p)

    def local(self, p):
        return self.R.transposed() @ (Vector(p) - self.loc)

    def front_at(self, z0, z1, half_width=0.13):
        """How far the body sticks out forward between two heights (rig-local y)."""
        mw = self.mesh.matrix_world
        best = 0.0
        for v in self.mesh.data.vertices:
            p = self.local(mw @ v.co)
            if z0 <= p.z <= z1 and abs(p.x) <= half_width:
                best = max(best, p.y)
        return best

    # ---------------------------------------------------------- posing
    def _set(self, n, Rw):
        """Turn bone n to world rotation Rw (its head stays) without a scene update: a scene update walks
        every object of the scene, so a chain of bones (finger, arm, leg) is set first and evaluated once.
        The parent's pose comes from the bones set before it, else from the evaluated pose."""
        pb = self._pb(n)
        par = pb.parent
        if par is None:
            P = Pr = None
            head = self._pending[pb.name].translation if pb.name in self._pending else pb.head
        else:
            P = self._pending.get(par.name)
            if P is None:
                if self._pending and any(a.name in self._pending for a in par.parent_recursive):
                    self.flush()
                P = par.matrix
            Pr = par.bone.matrix_local
            head = (P @ Pr.inverted() @ pb.bone.matrix_local @ Matrix.Translation(pb.location)).translation
        M = Matrix.Translation(head) @ (self.Ra.transposed() @ Rw).to_4x4()
        basis = pb.bone.convert_local_to_pose(M, pb.bone.matrix_local, parent_matrix=P,
                                              parent_matrix_local=Pr, invert=True)
        basis.translation = pb.location  # a rotation only: the location stays exactly (no rounding drift)
        pb.matrix_basis = basis
        self._pending[pb.name] = M

    def set_rot(self, n, Rw):
        self._set(n, Rw)
        self.flush()

    def rotate(self, n, axis, angle, flush=True):
        self._set(n, Matrix.Rotation(angle, 3, Vector(axis)) @ self.crot(n))
        if flush:
            self.flush()

    def aim(self, n, child, direction, flush=True):
        """Swing bone n (from its rest pose) so that the joint at `child` lies along `direction`."""
        seg = self.rhead(child) - self.rhead(n)
        q = seg.rotation_difference(Vector(direction))
        self._set(n, q.to_matrix() @ self.rrot(n))
        if flush:
            self.flush()

    def move_root(self, world_pos):
        pb = self.pb("Pelvis")
        M = pb.matrix.copy()
        M.translation = self.arm.matrix_world.inverted() @ Vector(world_pos)
        pb.matrix = M
        bpy.context.view_layer.update()

    def bend(self, bone, pitch=0.0, yaw=0.0, roll=0.0):
        """Extra rotation about the avatar's own axes; negative pitch bends forward/down."""
        if pitch:
            self.rotate(bone, self.right, pitch)
        if yaw:
            self.rotate(bone, self.up, yaw)
        if roll:
            self.rotate(bone, self.fwd, roll)

    def stance(self, width=0.07):
        for side, sgn in (("l", 1), ("r", -1)):
            S = SIDE[side]
            self.rotate(f"{S} Thigh", self.fwd, sgn * width)
            self.set_rot(f"{S} Foot", Matrix.Rotation(sgn * 0.12, 3, self.up) @ self.rrot(f"{S} Foot"))

    def leg_to(self, side, ankle, pole, toe_dir):
        S = SIDE[side]
        hip = self.head(f"{S} Thigh")
        l1 = (self.rhead(f"{S} Calf") - self.rhead(f"{S} Thigh")).length
        l2 = (self.rhead(f"{S} Foot") - self.rhead(f"{S} Calf")).length
        kn, an = two_bone(hip, Vector(ankle), l1, l2, Vector(pole))
        self.aim(f"{S} Thigh", f"{S} Calf", kn - hip, flush=False)
        self.aim(f"{S} Calf", f"{S} Foot", an - kn, flush=False)
        self.aim(f"{S} Foot", f"{S} Toe0", toe_dir)

    def arm_to(self, side, wrist, pole):
        S = SIDE[side]
        sh = self.head(f"{S} UpperArm")
        l1 = (self.rhead(f"{S} Forearm") - self.rhead(f"{S} UpperArm")).length
        l2 = (self.rhead(f"{S} Hand") - self.rhead(f"{S} Forearm")).length
        el, wr = two_bone(sh, Vector(wrist), l1, l2, Vector(pole))
        self.aim(f"{S} UpperArm", f"{S} Forearm", el - sh, flush=False)
        self.aim(f"{S} Forearm", f"{S} Hand", wr - el, flush=False)
        return wr

    # ----------------------------------------------------------- hands
    def hand_rest_frame(self, side):
        S = SIDE[side]
        h = self.rhead(f"{S} Hand")
        F = self.rhead(f"{S} Finger2") - h
        lat = self.rhead(f"{S} Finger1") - self.rhead(f"{S} Finger4")  # pinky -> index
        A = -lat if side == "l" else lat
        return frame(F, A.cross(F))

    def hand_delta(self, side, F, N):
        return frame(F, N) @ self.hand_rest_frame(side).transposed()

    def wrist_for(self, side, Rh, point_rest_offset_bone, target, extra=Vector()):
        """Wrist position that puts a hand landmark (a finger root) at `target`."""
        S = SIDE[side]
        off = self.rhead(f"{S} {point_rest_offset_bone}") - self.rhead(f"{S} Hand")
        return Vector(target) - Rh @ off - extra

    def hand_to(self, side, F, N, wrist, pole, twist_share=0.5):
        """Place the wrist with arm IK and orient the palm: F = towards the knuckles, N = palm normal."""
        S = SIDE[side]
        Rh = self.hand_delta(side, F, N)
        reached = self.arm_to(side, wrist, pole)
        if (reached - Vector(wrist)).length > 0.005:
            self.log.append("%s wrist short by %.0f mm" % (side, (reached - Vector(wrist)).length * 1000))
        fa = f"{S} Forearm"
        Q_fa = self.crot(fa) @ self.rrot(fa).transposed()
        q = (Rh @ Q_fa.transposed()).to_quaternion()
        axis = (self.head(f"{S} Hand") - self.head(fa)).normalized()
        twist = 2 * math.atan2(Vector((q.x, q.y, q.z)).dot(axis), q.w)
        if twist > math.pi:
            twist -= 2 * math.pi
        if abs(twist) > 1e-3 and twist_share:
            self.rotate(fa, axis, twist * twist_share, flush=False)
        self.set_rot(f"{S} Hand", Rh @ self.rrot(f"{S} Hand"))
        fr = frame(F, N)
        self._Rh[side] = Rh
        self._axes[side] = (fr.col[0], fr.col[1], fr.col[2])
        return Rh

    def _chain(self, side, k):
        """Bone names, rest joint positions (+ estimated tip) and the finger root offset from the wrist."""
        S = SIDE[side]
        names = [f"{S} Finger{k}", f"{S} Finger{k}1", f"{S} Finger{k}2"]
        heads = [self.rhead(n) for n in names]
        tip = heads[2] + (heads[2] - heads[1]) * 0.95
        heads.append(tip)
        segs = [heads[j + 1] - heads[j] for j in range(3)]
        return names, segs, heads[0] - self.rhead(f"{S} Hand")

    # A "plan" is a hand frame + wrist position. Planning evaluates finger and thumb reach with plain
    # vector maths, without touching the rig, so many candidate hand placements can be tried quickly.
    def plan_hand(self, side, F, N, wrist):
        fr = frame(F, N)
        return {"side": side, "Rh": self.hand_delta(side, F, N), "axes": (fr.col[0], fr.col[1], fr.col[2]),
                "wrist": Vector(wrist)}

    def current_plan(self, side):
        return {"side": side, "Rh": self._Rh[side], "axes": self._axes[side],
                "wrist": self.head(SIDE[side] + " Hand")}

    @staticmethod
    def _finger_fk(plan, segs, base_off, mcp, pip, spread):
        """Joints and tip for base-joint flexion `mcp`, middle-joint flexion `pip` (tip joint 0.7x pip)."""
        Rh = plan["Rh"]
        F, N, A = plan["axes"]
        p = plan["wrist"] + Rh @ base_off
        Rs = Matrix.Rotation(spread, 3, N)
        rots, joints = [], []
        for seg, cum in zip(segs, (mcp, mcp + pip, mcp + pip * (1 + DIP_PIP))):
            R = Rs @ Matrix.Rotation(cum, 3, A) @ Rh
            rots.append(R)
            p = p + R @ seg
            joints.append(p)
        return p, rots, joints[:2]

    def solve_finger(self, plan, k, target, avoid=None, extra=None, limits=None, warm=None, fine=True):
        """Best (cost, mcp, pip, spread, tip) so the fingertip lands on target.

        avoid(points): penetration depth (m) of the finger joints into a keep-out volume (the neck).
        extra(joints, tip): additional cost, e.g. keeping a barre finger flat along a fret.
        """
        names, segs, base_off = self._chain(plan["side"], k)
        target = Vector(target)
        a_lim, b_lim, s_lim = limits or ((-0.4, 1.4), (0.0, 1.8), (-0.6, 0.6))

        def cost(a, b, s):
            tip, _, joints = self._finger_fk(plan, segs, base_off, a, b, s)
            # small preferences for natural poses: little sideways spread, no hyperextension
            c = (tip - target).length + 0.004 * abs(s) + 0.01 * max(0.0, -a)
            if avoid:
                c += 4.0 * avoid(joints)
            if extra:
                c += extra(joints, tip)
            return c

        def grid(center, half, step):
            best = None
            rng = [(max(lo, c - h), min(hi, c + h)) for (lo, hi), c, h in zip((a_lim, b_lim, s_lim), center, half)]
            a = rng[0][0]
            while a <= rng[0][1] + 1e-9:
                b = rng[1][0]
                while b <= rng[1][1] + 1e-9:
                    s = rng[2][0]
                    while s <= rng[2][1] + 1e-9:
                        c = cost(a, b, s)
                        if best is None or c < best[0]:
                            best = (c, a, b, s)
                        s += step
                    b += step
                a += step
            return best

        if warm:
            best = grid(warm, (0.3, 0.3, 0.3), 0.1)
        else:
            mid = [(lo + hi) / 2 for lo, hi in (a_lim, b_lim, s_lim)]
            best = grid(mid, [(hi - lo) / 2 for lo, hi in (a_lim, b_lim, s_lim)], 0.1 if fine else 0.15)
        if fine:
            best = grid(best[1:], (0.1, 0.1, 0.1), 0.02)
            best = grid(best[1:], (0.02, 0.02, 0.02), 0.005)
        c, a, b, s = best
        tip = self._finger_fk(plan, segs, base_off, a, b, s)[0]
        return c, a, b, s, tip

    def finger_angles(self, side, k, mcp, pip, spread=0.0):
        names, segs, base_off = self._chain(side, k)
        tip, rots, _ = self._finger_fk(self.current_plan(side), segs, base_off, mcp, pip, spread)
        for n, R in zip(names, rots):
            self._set(n, R @ self.rrot(n))
        self.flush()
        return tip

    def finger(self, side, k, curl, spread=0.0):
        """Relaxed curl preset: one number bends all joints."""
        return self.finger_angles(side, k, curl, curl * 1.15, spread)

    def finger_to(self, side, k, target, avoid=None, extra=None, limits=None, warm=None):
        """Solve and apply one finger; returns the miss distance in metres.
        warm: previous (mcp, pip, spread) to search around, which keeps animation frames continuous."""
        _, a, b, s, tip = self.solve_finger(self.current_plan(side), k, target, avoid, extra, limits, warm=warm)
        tip = self.finger_angles(side, k, a, b, s)
        self.angles[(side, k)] = (a, b, s)
        miss = (tip - Vector(target)).length
        self.log.append("%s%d mcp %.2f pip %.2f spread %.2f miss %.0f mm" % (side, k, a, b, s, miss * 1000))
        return miss

    def thumb_fit(self, plan, target, avoid=None):
        """Bend the thumb so its length matches the distance to target; swing it there.
        Returns (cost, per-bone rotations)."""
        names, segs, base_off = self._chain(plan["side"], 0)
        Rh = plan["Rh"]
        F, N, A = plan["axes"]
        base = plan["wrist"] + Rh @ base_off
        target = Vector(target)
        reach = (target - base).length
        segs = [Rh @ sg for sg in segs]
        axis = segs[0].cross(N).normalized()
        best = None
        for i in range(-10, 31):
            f = i * 0.04
            Rs = [Matrix.Rotation(f * w, 3, axis) for w in (0.0, 0.6, 1.4)]  # cumulative joint bends
            v = [R @ sg for R, sg in zip(Rs, segs)]
            tip = base + v[0] + v[1] + v[2]
            c = abs((tip - base).length - reach) + 0.002 * abs(f)
            q = (tip - base).rotation_difference(target - base).to_matrix()
            if avoid:  # sample along the whole thumb, not just its joints
                j1 = base + q @ v[0]
                j2 = j1 + q @ v[1]
                c += 4.0 * avoid([base.lerp(j1, 0.5), j1, j1.lerp(j2, 0.5), j2, j2.lerp(target, 0.5)])
            if best is None or c < best[0]:
                best = (c, [q @ R @ Rh for R in Rs])
        return best

    def thumb_to(self, side, target, avoid=None):
        names = [f"{SIDE[side]} Finger0", f"{SIDE[side]} Finger01", f"{SIDE[side]} Finger02"]
        c, rots = self.thumb_fit(self.current_plan(side), target, avoid)
        for n, R in zip(names, rots):
            self._set(n, R @ self.rrot(n))
        self.flush()
        return c

    def fingertip(self, side, k):
        """Posed fingertip: the rest tip estimate carried along by the tip bone."""
        S = SIDE[side]
        pb = self.pb(f"{S} Finger{k}2")  # (evaluates pending rotations)
        h1, h2 = pb.parent.bone.head_local, pb.bone.head_local
        tip_rest = h2 + (h2 - h1) * 0.95
        return self.arm.matrix_world @ (pb.matrix @ (pb.bone.matrix_local.inverted() @ tip_rest))
