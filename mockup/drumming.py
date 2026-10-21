"""Drummer: Mixamo's "Basic Rock Beat" motion capture on the Sonor kit, keyed on every frame.

The captured groove is ours - one bar of 4/4 in eighth notes:
- right hand crossed over the left onto the closed hi-hat on every eighth;
- left hand on the snare on the backbeat (beats 2 and 4);
- right foot on the kick on 1, 3 and the "and" of 3; left foot on the hi-hat pedal.
In the clip (30 fps) beat 1 falls on frame 5.5 and an eighth lasts 8.75 frames; its two bars loop.
Our time is mapped onto it, so the captured body - posture, shoulders, head, the wrist snap and the
grip of the sticks - plays in our tempo.

Fitting the player to the kit: the clip's stick tips at its hi-hat and snare hits and its feet on the
pedals are compared with where our kit has them, and the seat position and a turn of the body that fit
best are solved for; the throne is then moved under the player. The sticks sit in the captured grip,
between the thumb and the middle joint of the index finger and across the palm. What still differs at a
hit is corrected with IK that keeps the captured wrist rotation and elbow direction, blended smoothly
from hit to hit; the balls of the feet ride the pedals' footboards.

The kit plays along:
- kick pedal: the footboard goes down under the foot and the beater swings forward to meet the head on
  the beat, then both spring back;
- hi-hat: the pedal holds the top cymbal down on the bottom one; on the "and" of 4 the foot lets it open
  for an open-hat accent and closes it again on the downbeat; the stick follows the
  cymbal's height;
- cymbals swing about their pivot after a hit: a damped tilt away from the stick plus a slower wobble,
  re-excited by every hit; the closed hi-hat only shivers.
"""
import math

import bpy
from mathutils import Matrix, Vector

from fretting import key_pose, snapshot
from drumkit import adjust, raise_tom, snare_room, tom_room
from mocap import Clip, Retarget, two_bone_keep_twist

UP = Vector((0, 0, 1))
CLIP = "playing_drums_113820901.fbx"
CLIP_BEAT1, CLIP_EIGHTH, CLIP_LOOP = 5.5, 8.75, 140.0  # clip frames
STICK_TIP = 0.27         # m from the fulcrum (thumb and index) to the tip of the stick
SOLE = 0.005             # m from the ball-of-foot joint down to the sole (Rocketbox: at sole level)
CRASH_ABOVE_HIHAT = 0.12  # m: the crash rides above the hi-hat
BALL_BACK = 0.06          # m the ball of the foot sits back from the footboard's measured ball point
HIP_DEPTH = 0.15          # m the body model reaches below the spine's base (over the hips)
ARM_RADIUS = 0.06         # m, forearm and hand (sleeve included), for keeping them out of the torso
TOM_RAISE_MAX = 0.25      # m the left rack tom may go up to make room for the snare
SEAT_UNDER_PELVIS = (0.0, -0.03, 0.09)  # pelvis joint relative to the seat's top centre (rig frame)
KICK_DOWN, KICK_UP = 0.25, 0.7       # eighths: footboard travel before the hit, and back after it
OPEN_FROM, OPEN_TO, CLOSE_AT = 6.55, 7.6, 8.0  # hi-hat foot (eighths in the bar): open for the "and" of 4
OPEN_SHARE = 0.55                    # share of the model's gap the hi-hat opens to
SWING = {  # amplitude, frequency (Hz), decay time (s)
    "crash": (math.radians(7.0), 1.4, 1.2),
    "hihat_closed": (math.radians(0.5), 4.0, 0.15),
    "hihat_open": (math.radians(3.0), 2.5, 0.5)}


def groove(bars):
    """Hits in eighth notes from the start: {"r"/"l": [(eighth, target)], "kick": [eighth]}."""
    hits = {"r": [], "l": [], "kick": []}
    for b in range(bars):
        hits["r"] += [(b * 8 + e, "hihat") for e in range(8)]
        hits["l"] += [(b * 8 + 2, "snare"), (b * 8 + 6, "snare")]
        hits["kick"] += [b * 8, b * 8 + 4, b * 8 + 5]
    return hits


def _smooth(u):
    u = min(max(u, 0.0), 1.0)
    return u * u * (3 - 2 * u)


def kick_press(t, kicks):
    """How far the kick pedal is down (0 rest .. 1 beater on the head) at time t (eighths)."""
    press = 0.0
    for k in kicks:
        if k - KICK_DOWN <= t <= k:
            press = max(press, _smooth((t - (k - KICK_DOWN)) / KICK_DOWN))
        elif k < t <= k + KICK_UP:
            press = max(press, 1 - _smooth((t - k) / KICK_UP))
    return press


def hihat_open(t):
    """How far the foot lets the hi-hat open (0 closed .. 1) at time t (eighths)."""
    e = t % 8
    if t < 0 or e < OPEN_FROM:
        return 0.0
    if e < 7.0:
        return _smooth((e - OPEN_FROM) / (7.0 - OPEN_FROM))
    if e < OPEN_TO:
        return 1.0
    return 1 - _smooth((e - OPEN_TO) / (CLOSE_AT - OPEN_TO))


def _ring(dt, amp, freq, decay, ratio=1.0):
    return amp * math.exp(-dt / decay) * math.sin(2 * math.pi * freq * ratio * dt) if dt >= 0 else 0.0


def _pivot(M0, pivot, R, shift=Vector()):
    """M0 rotated by R about a world pivot, then moved by shift."""
    return Matrix.Translation(pivot + shift) @ R.to_4x4() @ Matrix.Translation(-pivot) @ M0


def clip_frame(t):
    """Clip frame for our time t (eighths from beat 1)."""
    m = CLIP_BEAT1 + (t * CLIP_EIGHTH) % CLIP_LOOP
    return m - CLIP_LOOP if m > CLIP_BEAT1 + CLIP_LOOP - 4 else m  # the loop's last frames are its first


def _smooth_offsets(events, t):
    """Correction at time t, blended between the corrections at the hits before and after it."""
    if t <= events[0][0]:
        return events[0][1]
    for (ta, oa), (tb, ob) in zip(events, events[1:]):
        if t <= tb:
            u = (t - ta) / (tb - ta)
            return oa.lerp(ob, u * u * (3 - 2 * u))
    return events[-1][1]


class DrumPlayer:
    def __init__(self, av, kit, rig, bars, eighth, song_hits=None):
        """song_hits: the drum part of a song (song.Song.drum_hits, in eighths from the section's start);
        without it the captured groove itself. The kit is set up for the player from the captured groove."""
        self.av, self.kit, self.rig = av, kit, rig
        self.sec = eighth / bpy.context.scene.render.fps  # seconds per eighth note
        self.hits = hits = groove(bars)
        self.open_spans = None  # the groove's own accent until the song's hits are in
        self.frames = list(range(1, int(round(bars * 8 * eighth)) + 2))
        self.ts = [(f - 1) / eighth for f in self.frames]
        clip = Clip(CLIP)
        self.rt = rt = Retarget(av, clip)
        n_hand = [len(hits[s]) for s in ("r", "l")]
        loop = clip.sample([CLIP_BEAT1 + CLIP_LOOP * i / 70 for i in range(70)])
        self.mean = sum((smp["Hips"][1] for smp in loop), Vector()) / len(loop)
        ev = clip.sample([clip_frame(e) for e, _ in hits["r"]] + [clip_frame(e) for e, _ in hits["l"]]
                         + [clip_frame(e) for e in hits["kick"]])
        self.samples = clip.sample([clip_frame(t) for t in self.ts])
        clip.remove()
        ev_r, ev_l, ev_k = ev[:n_hand[0]], ev[n_hand[0]:sum(n_hand)], ev[sum(n_hand):]

        # the sticks' grip, from the captured hands at their first hit
        self.grip = {}
        for side, smp in (("r", ev_r[0]), ("l", ev_l[0])):
            S = side.upper()
            W = rt.rotations(smp)
            pos = rt.fk(W, Vector())
            hand = pos[S + " Hand"][0]
            fulcrum = (pos[S + " Finger02"][1] + pos[S + " Finger11"][0]) / 2
            heel = hand + (pos[S + " Finger4"][0] - hand) * 0.35
            d = (fulcrum - heel).normalized()
            Wt = W[S + " Hand"].transposed()
            self.grip[side] = (Wt @ (fulcrum - hand), Wt @ d)

        # fit the player to the kit: seat position and a turn of the body
        self.fit(ev_r, ev_l, ev_k)
        self.move_throne()
        # the song's hits (the fit used the captured groove)
        if song_hits is not None:
            self.hits = hits = song_hits
            clip_hits = Clip(CLIP)
            ev_r = clip_hits.sample([clip_frame(e) for e, _ in hits["r"]])
            ev_l = clip_hits.sample([clip_frame(e) for e, _ in hits["l"]])
            clip_hits.remove()
        self.open_spans = hits.get("open") if song_hits is not None else None
        # the hits' corrections, after the fit
        self.events = {}
        for side, smp_list in (("r", ev_r), ("l", ev_l)):
            evs = []
            for (e, target), smp in zip(hits[side], smp_list):
                tip, _ = self.tip(side, smp, self.anchor)
                evs.append((float(e), self.target(target, e) - tip))
            self.events[side] = evs
        # start pose, and the kit's sticks into the hands
        self.measure_torso()
        self.pose(0)
        for side, (stick, butt0, tip0) in zip(("l", "r"), kit["sticks"]):
            S = side.upper()
            hand = av.head(S + " Hand")
            Rh = av.crot(S + " Hand")
            f_loc, d_loc = self.grip[side]
            d = Rh @ d_loc
            tip = hand + Rh @ f_loc + d * STICK_TIP
            a = (tip0 - butt0).normalized()
            stick.matrix_world = (Matrix.Translation(tip) @ a.rotation_difference(d).to_matrix().to_4x4()
                                  @ Matrix.Translation(-tip0) @ stick.matrix_world)
            bpy.context.view_layer.update()
            mw = stick.matrix_world.copy()
            stick.parent, stick.parent_type = av.arm, "BONE"
            stick.parent_bone = "Bip01 %s Hand" % S
            bpy.context.view_layer.update()
            stick.matrix_world = mw

    def tip(self, side, smp, anchor=Vector()):
        """Stick tip and the joint positions for a clip sample, uncorrected."""
        S = side.upper()
        W = self.rt.rotations(smp)
        pos = self.rt.fk(W, anchor + self.rt.hips(smp, self.mean))
        f_loc, d_loc = self.grip[side]
        Rh = W[S + " Hand"]
        return pos[S + " Hand"][0] + Rh @ (f_loc + d_loc * STICK_TIP), pos

    def pedal_ball(self, pedal, press):
        """Where the ball of the foot goes on a pedal's footboard, pressed 0..1."""
        k = self.kit[pedal]
        ball = self.kit["kick_ball" if pedal == "kick" else "hihat_ball"]
        heel = self.kit["kick_heel" if pedal == "kick" else "hihat_heel"]
        ball = ball + (heel - ball).normalized() * BALL_BACK
        R = Matrix.Rotation(k["travel"] * press, 3, k["axis"])
        return k["hinge"] + R @ (ball - k["hinge"]) + UP * SOLE

    def fit(self, ev_r, ev_l, ev_k):
        """Sit the player at the kit and set the kit up for them, as a drummer does: the kick drum and its
        pedal stay; the seat goes where the clip's right foot lands on the kick pedal (which also sets its
        height); the hi-hat stand moves to the clip's left foot and its cymbals to the height the clip
        strikes them at; the snare stand moves to where the clip's left stick strikes. The body's turn is
        the one that needs the least moving of stands."""
        hits, rt = self.hits, self.rt

        def mean(points):
            return sum(points, Vector()) / len(points)

        hh_e = [(e, smp) for (e, tg), smp in zip(hits["r"], ev_r) if tg == "hihat"]
        sn_e = [(e, smp) for (e, tg), smp in zip(hits["l"], ev_l) if tg == "snare"]
        best = None
        for deg in range(-25, 26):
            rt.yaw = math.radians(deg)
            kick = mean([self.tip("r", smp)[1]["R Toe0"][0] for smp in ev_k])
            foot = mean([self.tip("l", smp)[1]["L Toe0"][0] for smp in self.samples[::4]])
            hh = mean([self.tip("r", smp)[0] for _, smp in hh_e])
            sn = mean([self.tip("l", smp)[0] for _, smp in sn_e])
            anchor = self.pedal_ball("kick", 1.0) - kick
            move_hh = anchor + foot - self.pedal_ball("hihat", 1.0)
            move_hh.z = 0.0
            raise_hh = (anchor + hh).z - self.target("hihat", hh_e[0][0]).z
            move_sn = anchor + sn - self.target("snare", sn_e[0][0])
            cost = move_hh.length + abs(raise_hh) + move_sn.length
            if best is None or cost < best[0]:
                best = (cost, deg, anchor, move_hh, raise_hh, move_sn, anchor + hh)
        _, deg, self.anchor, move_hh, raise_hh, wanted_sn, hh_tip = best
        rt.yaw = math.radians(deg)
        # the snare goes where the clip strikes it; the left rack tom goes up to make room for it (only if
        # that is too far does the snare stop short)
        tom_up = tom_room(self.kit, wanted_sn)
        if tom_up is not None and tom_up <= TOM_RAISE_MAX:
            move_sn = wanted_sn
            if tom_up > 0:
                raise_tom(self.kit, tom_up + 0.01)
        else:
            tom_up, move_sn = 0.0, snare_room(self.kit, wanted_sn)
        # the crash rides above the hi-hat, as on most kits (the groove itself has none: shifting the
        # captured hand onto it pushed the arm into the chest; a song's crash notes will need the wrist)
        move_cr = UP * max(0.0, self.target("hihat", 0).z + raise_hh + CRASH_ABOVE_HIHAT - self.kit["strike"]["crash"].z)
        adjust(self.kit, snare=move_sn, hihat=move_hh, hihat_raise=raise_hh, crash_raise=move_cr.z)
        R = self.rig.R.transposed()
        mm = lambda v: tuple(round(c * 1000) for c in R @ v)
        self.fit_report = dict(turn_deg=deg, snare_moved_mm=mm(move_sn), snare_short_mm=mm(wanted_sn - move_sn),
                               tom_raised_mm=round(tom_up * 1000),
                               hihat_moved_mm=mm(move_hh), hihat_raised_mm=round(raise_hh * 1000),
                               crash_moved_mm=mm(move_cr),
                               hihat_tip_off_cymbal_mm=mm(hh_tip - self.target("hihat", hh_e[0][0])))

    def move_throne(self):
        """The throne under the fitted pelvis (its top centre where the pelvis would sit on it)."""
        seat = self.anchor - self.rig.R @ Vector(SEAT_UNDER_PELVIS)
        delta = seat - self.kit["seat"]
        for o in self.kit["throne"]:
            o.matrix_world = Matrix.Translation(delta) @ o.matrix_world
        self.kit["seat"] = seat
        self.fit_report["throne_moved_mm"] = tuple(round(c * 1000) for c in self.rig.R.transposed() @ delta)

    def open_at(self, t):
        """How far the hi-hat is open (0..1): the song's open hi-hat notes, or the groove's accent."""
        if self.open_spans is None:
            return hihat_open(t)
        o = 0.0
        for a, b in self.open_spans:
            if a - 0.45 <= t <= b + 0.4:
                o = max(o, min(_smooth((t - (a - 0.45)) / 0.45), 1 - _smooth((t - b) / 0.4)))
        return o

    def drop(self, t):
        """How far the top hi-hat cymbal is down from the model's (open) position at time t."""
        return self.kit["hihat"]["drop"] * (1 - OPEN_SHARE * self.open_at(t))

    def target(self, name, t):
        p = self.kit["strike"][name]
        return p - UP * self.drop(t) if name == "hihat" else p

    def kit_pose(self, t, hits):
        """Pedals, hi-hat and cymbal swings at time t (eighths)."""
        kit, rest = self.kit, self.kit["rest"]
        k, h = kit["kick"], kit["hihat"]
        press = kick_press(t, hits["kick"])
        for o, pivot, angle in ((k["beater"], k["axle"], k["hit"]), (k["board"], k["hinge"], k["travel"])):
            o.matrix_world = _pivot(rest[o.name], pivot, Matrix.Rotation(angle * press, 3, k["axis"]))
        op = self.open_at(t)
        close = 1 - OPEN_SHARE * op
        h["board"].matrix_world = _pivot(rest[h["board"].name], h["hinge"],
                                         Matrix.Rotation(h["travel"] * close, 3, h["axis"]))
        down = -UP * self.drop(t)
        for name, struck in (("crash", "crash"), ("hihat", "hihat")):
            o, pivot, n = kit["swing"][name]
            r = kit["strike"][struck] - pivot
            r = (r - n * r.dot(n)).normalized()
            a1 = n.cross(r)  # tilting about a1 takes the struck edge down
            tilt = wobble = 0.0
            for e, tgt in hits["r"]:
                if tgt != struck or e > t:
                    continue
                key = name if name == "crash" else ("hihat_open" if self.open_at(e) > 0.5 else "hihat_closed")
                amp, freq, decay = SWING[key]
                dt = (t - e) * self.sec
                tilt += _ring(dt, amp, freq, decay)
                wobble += _ring(dt, 0.35 * amp, freq, decay, 1.13)
            if name == "hihat":
                tilt, wobble = tilt * (0.2 + 0.8 * op), wobble * (0.2 + 0.8 * op)  # closed hats barely move
            R = Matrix.Rotation(tilt, 3, a1) @ Matrix.Rotation(wobble, 3, r)
            o.matrix_world = _pivot(rest[o.name], pivot, R, down if name == "hihat" else Vector())
        clutch = h["top"][1]
        clutch.matrix_world = Matrix.Translation(down) @ rest[clutch.name]
        return press, close, op

    def key_kit(self, frame):
        for o in (self.kit["kick"]["beater"], self.kit["kick"]["board"], self.kit["hihat"]["board"],
                  *self.kit["hihat"]["top"], self.kit["swing"]["crash"][0]):
            o.keyframe_insert("location", frame=frame)
            o.keyframe_insert("rotation_quaternion", frame=frame)

    def measure_torso(self):
        """Half width and half depth of the avatar's torso (rest mesh) around its spine, chest and belly."""
        av = self.av
        mesh = av.mesh
        groups = {g.index: g.name for g in mesh.vertex_groups}
        torso = {i for i, n in groups.items() if any(k in n for k in ("Spine", "Pelvis"))}
        base, top = av.rhead("Spine"), av.rhead("Neck")
        axis = (top - base).normalized()
        xs, ys = [], []
        mw = mesh.matrix_world
        for v in mesh.data.vertices:
            if v.groups and max(v.groups, key=lambda g: g.weight).group in torso:
                q = mw @ v.co - base
                h = q.dot(axis)
                if -HIP_DEPTH < h < (top - base).length - 0.05:
                    xs.append(abs(q.dot(av.right)))
                    ys.append(q.dot(av.fwd))
        xs.sort()
        ys.sort()
        self.torso_size = (xs[int(len(xs) * 0.99)], ys[int(len(ys) * 0.99)], (top - base).length)

    def torso_frame(self, W, pos):
        """Spine base, up axis, right and forward axes of the torso for this pose."""
        av, rt = self.av, self.rt
        R = W["Spine1"] @ rt.rest["Spine1"][0].transposed()
        base, top = pos["Spine"][0], pos["Neck"][0]
        return base, (top - base).normalized(), R @ av.right, R @ av.fwd

    def penetration(self, torso, a, b):
        """How deep the segment a-b (a limb of ARM_RADIUS) gets into the torso's elliptic cylinder."""
        base, up, right, fwd = torso
        hw, hd, height = self.torso_size
        worst = 0.0
        for i in range(7):
            q = a.lerp(b, i / 6) - base
            h = q.dot(up)
            if not -HIP_DEPTH < h < height:
                continue
            x, y = q.dot(right) / (hw + ARM_RADIUS), q.dot(fwd) / (hd + ARM_RADIUS)
            e = math.hypot(x, y)
            if e < 1.0:
                worst = max(worst, (1.0 - e) * hd)
        return worst

    def clear_elbow(self, torso, shoulder, wrist, pole, elbow0, wrist0):
        """An elbow pole that keeps the arm out of the torso: the captured one, or the nearest turn of the
        elbow about the shoulder-wrist line that clears it."""
        from avatars import two_bone
        l1, l2 = (elbow0 - shoulder).length, (wrist0 - elbow0).length
        axis = (wrist - shoulder).normalized()
        best = None
        for deg in (0, 10, -10, 20, -20, 30, -30, 45, -45, 60, -60, 80, -80, 100, -100):
            p = shoulder + Matrix.Rotation(math.radians(deg), 3, axis) @ (pole - shoulder)
            el, wr = two_bone(shoulder, wrist, l1, l2, p)
            d = max(self.penetration(torso, el, wr), self.penetration(torso, shoulder.lerp(el, 0.6), el))
            if d <= 0.0:
                return p
            if best is None or d < best[0]:
                best = (d, p)
        return best[1]

    def pose(self, k):
        """Pose for output frame index k: the captured body, contacts corrected."""
        t, smp, rt = self.ts[k], self.samples[k], self.rt
        press, close, op = self.kit_pose(t, self.hits)
        W = rt.rotations(smp)
        pelvis = self.anchor + rt.hips(smp, self.mean)
        pos = rt.fk(W, pelvis)
        torso = self.torso_frame(W, pos)
        for side in ("r", "l"):  # hands: shift by the blended correction, captured wrist rotation kept
            S = side.upper()
            c = _smooth_offsets(self.events[side], t)
            hand = (pos[S + " Hand"][0], pos[S + " Finger2"][0])
            for _ in range(6):  # the captured hand is clear of the body: shrink a correction that is not
                if self.penetration(torso, hand[0] + c, hand[1] + c) <= 0.0:
                    break
                c = c * 0.6
            wrist = pos[S + " Hand"][0] + c
            pole = self.clear_elbow(torso, pos[S + " UpperArm"][0], wrist, pos[S + " Forearm"][0] + c,
                                    pos[S + " Forearm"][0], pos[S + " Hand"][0])
            two_bone_keep_twist(W, pos, S + " UpperArm", S + " Forearm", S + " Hand", wrist, pole)
        for side, pedal, down in (("r", "kick", press), ("l", "hihat", close)):  # balls of the feet on the pedals
            S = side.upper()
            c = self.pedal_ball(pedal, down) - pos[S + " Toe0"][0]
            two_bone_keep_twist(W, pos, S + " Thigh", S + " Calf", S + " Foot",
                                pos[S + " Foot"][0] + c, pos[S + " Calf"][0] + c)
            # toes flat on the footboard (the clip's pedals are not inclined like ours)
            k = self.kit[pedal]
            board = Matrix.Rotation(k["travel"] * down, 3, k["axis"]) @ (
                self.kit[pedal + "_ball"] - self.kit[pedal + "_heel"]).normalized()
            R0, h0, t0 = rt.rest[S + " Toe0"]
            toe = W[S + " Toe0"] @ R0.transposed() @ (t0 - h0)
            W[S + " Toe0"] = toe.rotation_difference(board).to_matrix() @ W[S + " Toe0"]
        rt.apply(W, pelvis)


def animate_drums(player):
    """Key the drummer (and the kit) on every frame of the groove."""
    av, prev = player.av, None
    names = player.rt.order
    for k, f in enumerate(player.frames):
        player.pose(k)
        prev = key_pose(av, snapshot(av, names), f, prev)
        player.key_kit(f)
