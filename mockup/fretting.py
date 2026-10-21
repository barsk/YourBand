"""Fretting-hand chord grips, solved per chord, plus keyframing of chord changes and strumming.

What the reference study showed (photos on Wikimedia Commons, teacher and forum guidance):
- Open chords: the neck sits in the palm, the back of the hand faces the audience, the knuckles are just
  below/in front of the treble-side edge, and the fingers arch over that edge and press almost straight
  down. For most open chords (C, G, A, E, Am, Em) the thumb hooks over the bass-side edge of the neck.
- D and Dm (fingers on the thin strings): the thumb stays behind the neck, but high, near the top edge.
- Barre chords: the thumb goes behind the neck roughly opposite the index finger, the palm comes away
  from the neck and the index lies flat across all strings just behind the fret.
A fixed hand cannot reach every chord, so the placement is solved per chord: position along and around
the neck, roll and twist are searched until every fingertip and the thumb reach their spots without
passing through the neck or over-bending the wrist.
"""
import itertools
import math

from mathutils import Matrix, Vector

from avatars import two_bone
from cache import Cache

PLACEMENTS = Cache("fretting", ["fretting.py", "avatars.py"])  # solved hand placements

# finger (1 index, 2 middle, 3 ring, 4 pinky) -> (string, fret); string 0 = thinnest (high E) ... 5 = low E.
# Several common fingerings per chord: which one a player uses depends on the chords around it.
CHORD_VARIANTS = {
    "C": [dict(fingers={3: (4, 3), 2: (3, 2), 1: (1, 1)}, thumb="over")],
    "G": [dict(fingers={2: (5, 3), 1: (4, 2), 3: (0, 3)}, thumb="over"),             # 3-finger G
          dict(fingers={2: (5, 3), 1: (4, 2), 3: (1, 3), 4: (0, 3)}, thumb="over"),  # 4-finger "rock" G
          dict(fingers={3: (5, 3), 2: (4, 2), 4: (0, 3)}, thumb="over")],            # ring-middle-pinky G
    "D": [dict(fingers={1: (2, 2), 3: (1, 3), 2: (0, 2)}, thumb="high")],
    "A": [dict(fingers={1: (3, 2), 2: (2, 2), 3: (1, 2)}, thumb="over"),
          dict(fingers={2: (3, 2), 3: (2, 2), 4: (1, 2)}, thumb="over")],
    "E": [dict(fingers={2: (4, 2), 3: (3, 2), 1: (2, 1)}, thumb="over")],
    "Am": [dict(fingers={2: (3, 2), 3: (2, 2), 1: (1, 1)}, thumb="over")],
    "Em": [dict(fingers={2: (4, 2), 3: (3, 2)}, thumb="over"),
           dict(fingers={1: (4, 2), 2: (3, 2)}, thumb="over")],
    "Dm": [dict(fingers={2: (2, 2), 3: (1, 3), 1: (0, 1)}, thumb="high")],
    "F": [dict(fingers={3: (4, 3), 4: (3, 3), 2: (2, 2)}, barre=(1, 0, 5), thumb="behind")],
}
CHORDS = {name: variants[0] for name, variants in CHORD_VARIANTS.items()}


def placements(spec):
    """finger -> where it presses (a barre counts as the index on 'barre <fret>')."""
    pl = dict(spec["fingers"])
    if "barre" in spec:
        pl[1] = ("barre", spec["barre"][0])
    return pl


def change_cost(a, b):
    """How much the fingers have to move when changing from chord a to chord b."""
    pa, pb = placements(a), placements(b)
    moved = sum(1 for k, v in pb.items() if pa.get(k) != v)
    return moved + 0.3 * sum(1 for k in pa if k not in pb)


def choose_fingerings(progression, loop=True):
    """Pick a fingering per chord so as many fingers as possible stay down between changes."""
    options = [CHORD_VARIANTS[c] for c in progression]
    best = None
    for combo in itertools.product(*[range(len(o)) for o in options]):
        specs = [options[i][j] for i, j in enumerate(combo)]
        c = sum(change_cost(specs[i], specs[i + 1]) for i in range(len(specs) - 1))
        if loop:
            c += change_cost(specs[-1], specs[0])
        c += 0.05 * sum(combo)  # prefer the standard fingering on ties
        if best is None or c < best[0]:
            best = (c, specs)
    return best[1]


def finger_roles(a, b):
    """anchor: same string and fret in both chords, stays pressed; guide: same string, slides along it;
    move: lifts and arcs over to its new spot; free: pressed in neither chord."""
    pa, pb = placements(a), placements(b)
    roles = {}
    for k in (1, 2, 3, 4):
        va, vb = pa.get(k), pb.get(k)
        if va and va == vb:
            roles[k] = "anchor"
        elif va and vb and va[0] == vb[0] and va[0] != "barre":
            roles[k] = "guide"
        elif va or vb:
            roles[k] = "move"
        else:
            roles[k] = "free"
    return roles


# thumb styles as an angle around the neck (0 = straight behind, 90 = at the bass-side edge); hooking
# "over" peeks just past the edge but stays behind the fretboard face
THUMB_ANGLE = {"over": 96.0, "high": 60.0, "behind": 8.0}
THUMB_PAD = 0.0145  # thumb-pad radius: distance from the neck surface to the thumb's bone line
THUMB_MARGIN = 0.012  # keep-out margin for the thumb joints (the thumb is thicker than a finger)
THUMB_WEIGHT = 0.35  # thumb placement matters less than fingertips on their strings

# starting guess for (dx, dy, dz, roll, yaw) per thumb style; the solver refines it
START = {"over": (0.0, 0.0, 0.0, 0.0, 0.0), "high": (0.0, -0.004, 0.004, -0.15, 0.0),
         "behind": (0.0, -0.008, 0.0, -0.45, 0.0),
         "barre": (0.0, 0.006, 0.0, -1.0, 0.0)}  # fingers rolled to run across the strings
STEPS = (0.01, 0.008, 0.008, 0.15, 0.15)
UP = Vector((0, 0, 1))
LEFT_ARM = ["L UpperArm", "L Forearm", "L Hand"] + ["L Finger%d%s" % (k, j) for k in range(5) for j in ("", "1", "2")]
RIGHT_ARM = ["R UpperArm", "R Forearm", "R Hand"]


class FretHand:
    def __init__(self, av, g):
        self.av, self.g = av, g
        self.X, self.Y, self.Z, self.W = g["X"], g["Y"], g["Z"], g["W"]
        # neck geometry, measured on the instrument: width and back vary along the neck
        self.half_w, self.back = g["half_w"], g["back_z"]
        self.board, self.string_z = g["board_z"], g["string_z"]
        self.c0 = self.W((0, 0, 0))
        self.shoulder = av.head("L UpperArm")
        self.l1 = (av.rhead("L Forearm") - av.rhead("L UpperArm")).length
        self.l2 = (av.rhead("L Hand") - av.rhead("L Forearm")).length
        self.pole = self.shoulder - av.right * 0.25 - av.fwd * 0.2 - UP * 0.35
        self.mcp_off = [av.rhead("L Finger%d" % k) - av.rhead("L Hand") for k in (1, 2, 3, 4)]
        self.mcp_off.append(av.rhead("L Finger0") - av.rhead("L Hand"))  # thumb base: palm stays off the neck
        self.solutions = {}
        self.prev_knuckles = None  # set while solving inside a progression: prefer little hand travel
        self._geometry = None

    def geometry(self):
        """Everything the placement search depends on besides the chord: the instrument's measured neck
        and strings where the player holds it, and the player's shoulder and (rest) hand."""
        if self._geometry is None:
            g, av = self.g, self.av
            xs = [i * 0.02 for i in range(-5, 40)]
            spots = [g["fret_point"](s, f) for s in range(len(g["strings"])) for f in range(16)]
            hand = [av.hand_rest_frame("l")] + [av._chain("l", k)[1:] for k in range(5)]
            self._geometry = (av.arm.name, self.c0, self.X, self.Y, self.Z, self.shoulder, self.l1, self.l2,
                              self.pole, self.mcp_off, self.board, spots, hand,
                              [(self.half_w(x), self.back(x), self.string_z(x)) for x in xs])
        return self._geometry

    def _search(self, spec):
        """The best placement from a few starting rolls (cached on disk by its inputs)."""
        key = PLACEMENTS.key(self.geometry(), spec, self.prev_knuckles)
        hit = PLACEMENTS.get(key)
        if hit is not None:
            return hit[1], list(hit[0])
        base = START["barre" if "barre" in spec else spec.get("thumb", "over")]
        starts = [[base[0] + ddx] + list(base[1:3]) + [base[3] + d, base[4]] for d in (0.0, -0.45, 0.45) for ddx in (0.0, 0.012)]
        best, p = min((self._descend(spec, s) for s in starts), key=lambda r: r[0])
        PLACEMENTS.put(key, (tuple(p), best))
        return best, p

    def local(self, p):
        d = p - self.c0
        return d.dot(self.X), d.dot(self.Y), d.dot(self.Z)

    def in_neck(self, points, margin=0.008):
        """How deep points sink into the neck (fingerboard + rounded back), `margin` = finger radius."""
        depth = 0.0
        for p in points:
            x, y, z = self.local(p)
            lim_y, front, rear = self.half_w(x) + margin, self.board + margin, self.back(x) - margin
            if abs(y) < lim_y and rear < z < front:
                depth += min(front - z, z - rear, lim_y - abs(y))
        return depth

    def frame_for(self, roll, yaw):
        """Palm under the neck, back of the hand to the audience; rolled about the neck and twisted."""
        Rr = Matrix.Rotation(roll, 3, self.X)
        F, N = Rr @ (self.Z + self.Y * 0.3), Rr @ (self.Y - self.Z * 0.3)
        return Matrix.Rotation(yaw, 3, N.normalized()) @ F, N

    def targets(self, spec):
        fp = self.g["fret_point"]
        press = {k: fp(s, f) for k, (s, f) in spec["fingers"].items()}
        frets = [f for _, f in spec["fingers"].values()]
        barre = None
        if "barre" in spec:
            fret, _, s_lo = spec["barre"]
            frets.append(fret)
            barre = (fp(s_lo, fret) + self.Y * 0.004, self.local(fp(0, fret)))
        lo, hi = min(frets), max(frets)
        strings = [s for s, _ in spec["fingers"].values()]
        hover_string = round(sum(strings) / len(strings))
        hover = {k: fp(hover_string, max(1, min(lo + k - 1, hi + 1)), lift=0.018)
                 for k in (1, 2, 3, 4) if k not in press and not (k == 1 and barre)}
        xs = [self.local(t)[0] for t in press.values()] + ([barre[1][0]] if barre else [])
        x_mid = sum(xs) / len(xs)
        x_lo = self.local(fp(0, lo))[0]  # fret nearest the nut
        style = spec.get("thumb", "over")
        thumb_x = {"over": x_lo + 0.005, "high": x_mid + 0.01, "behind": x_mid + 0.005}[style]
        thumb = self.thumb_point(thumb_x, THUMB_ANGLE[style])
        return press, hover, barre, thumb, x_mid

    def thumb_point(self, x, angle):
        """Thumb-pad centre on the outside of the neck, by angle around the neck's cross-section:
        0 = straight behind the neck, 90 deg = at the bass-side edge. It never passes the fretboard
        face, so the thumb stays behind the neck even when it hooks over the edge."""
        a = math.radians(angle)
        mid = (self.board + self.back(x)) / 2  # the neck's cross-section: width and depth at x
        ry, rz = self.half_w(x) + THUMB_PAD, mid - self.back(x) + THUMB_PAD
        return self.W((x, ry * math.sin(a), mid - rz * math.cos(a)))

    def _barre_extra(self, barre):
        x_f, _, _ = barre[1]
        z_f = self.string_z(x_f) + 0.005

        def extra(joints, tip):
            c = 0.0
            for j in joints:  # the whole index lies along the fret, flat on the strings
                x, _, z = self.local(j)
                c += 3.0 * (abs(x - x_f) + abs(z - z_f))
            return c
        return extra

    def placement(self, spec, p):
        """Hand frame, wrist and knuckle position for placement parameters p = (dx, dy, dz, roll, yaw)."""
        dx, dy, dz, roll, yaw = p
        x_mid = self.targets(spec)[4]
        F, N = self.frame_for(roll, yaw)
        knuckles = self.W((x_mid + dx, -self.half_w(x_mid) - 0.015 + dy, self.board + 0.008 + dz))
        wrist = self.av.wrist_for("l", self.av.hand_delta("l", F, N), "Finger2", knuckles)
        return F, N, wrist, knuckles

    def evaluate(self, spec, p, warm, fine=False):
        av = self.av
        dx, dy, dz, roll, yaw = p
        press, hover, barre, thumb, x_mid = self.targets(spec)
        F, N, wrist, knuckles = self.placement(spec, p)
        Rh = av.hand_delta("l", F, N)
        plan = av.plan_hand("l", F, N, wrist)

        cost = 0.0
        if self.prev_knuckles is not None:
            cost += 0.15 * (knuckles - self.prev_knuckles).length
        d = (wrist - self.shoulder).length
        cost += 2.0 * max(0.0, d - (self.l1 + self.l2 - 0.005))           # arm must reach
        el, _ = two_bone(self.shoulder, wrist, self.l1, self.l2, self.pole)
        bend = (wrist - el).angle(F, 0.0)
        cost += 0.03 * max(0.0, bend - 1.0)                                 # no extreme wrist bend
        cost += 4.0 * self.in_neck([wrist + Rh @ o for o in self.mcp_off] + [wrist], margin=0.012)
        # anatomy: the hand comes from below the treble-side edge (never over the top of the neck),
        # with the palm facing up towards the neck or back towards the player
        _, ky, _ = self.local(knuckles)
        _, wy, _ = self.local(wrist)
        hw = self.half_w(self.local(knuckles)[0])
        cost += 2.0 * max(0.0, ky - (-hw + 0.009)) + 2.0 * max(0.0, wy - (-hw - 0.02))
        n = N.normalized()
        cost += 0.2 * max(0.0, 0.3 - (n.dot(self.Y) - 0.5 * n.dot(self.Z)))

        sol = {}
        for k, t in press.items():
            c, a, b, s, _ = av.solve_finger(plan, k, t, self.in_neck, warm=warm.get(k), fine=fine)
            cost += c
            sol[k] = (a, b, s)
        if barre:
            c, a, b, s, _ = av.solve_finger(plan, 1, barre[0], self.in_neck, self._barre_extra(barre),
                                            limits=((-0.3, 0.7), (0.0, 0.25), (-0.6, 0.6)), warm=warm.get(1), fine=fine)
            cost += c
            sol[1] = (a, b, s)
        for k, t in hover.items():
            c, a, b, s, _ = av.solve_finger(plan, k, t, self.in_neck, warm=warm.get(k), fine=fine)
            cost += 0.3 * c
            sol[k] = (a, b, s)
        tc, _ = av.thumb_fit(plan, thumb, lambda pts: self.in_neck(pts, THUMB_MARGIN))
        # the fingers decide where the hand goes; the thumb follows as well as it can
        cost += THUMB_WEIGHT * tc + 0.01 * (abs(roll) + abs(yaw))
        return cost, sol, (F, N, wrist)

    def _descend(self, spec, p):
        steps = list(STEPS)
        best, sol, _ = self.evaluate(spec, p, {})
        for _ in range(40):
            improved = False
            for i in range(5):
                for sgn in (1, -1):
                    q = p[:]
                    q[i] += sgn * steps[i]
                    c, s2, _ = self.evaluate(spec, q, sol)
                    if c < best - 1e-5:
                        best, p, sol, improved = c, q, s2, True
                        break
            if not improved:
                steps = [s / 2 for s in steps]
                if steps[0] < 0.0012:
                    break
        return best, p

    def solve(self, name, spec):
        """Coordinate descent over the hand placement from a few hand rolls; cached per chord."""
        if name not in self.solutions:
            best, p = self._search(spec)
            self.solutions[name] = (p, best)
        return self.solutions[name]

    def solve_near(self, spec, prev_knuckles):
        """Solve a chord inside a progression: same search, plus a preference for little hand travel."""
        if prev_knuckles is None:
            return self.solve(id(spec), spec)
        self.prev_knuckles = prev_knuckles
        best, p = self._search(spec)
        self.prev_knuckles = None
        return p, best

    def chord_plan(self, name, spec, p):
        press, hover, barre, thumb, _ = self.targets(spec)
        ends = dict(hover)
        ends.update(press)
        if barre:
            ends[1] = barre[0]
        F, N, wrist, knuckles = self.placement(spec, p)
        style = spec.get("thumb", "over")
        return dict(name=name, spec=spec, p=p, knuckles=knuckles, ends=ends, thumb=thumb,
                    thumb_x=self.local(thumb)[0], thumb_angle=THUMB_ANGLE[style], press=press, barre=barre)

    def pose_plan(self, pl):
        """The full chord: pressed fingers on their spots (barre kept flat), the rest hovering."""
        av = self.av
        F, N, wrist, _ = self.placement(pl["spec"], pl["p"])
        av.hand_to("l", F, N, wrist, self.pole)
        miss = {}
        for k, t in pl["ends"].items():
            warm = av.angles.get(("l", k))
            if pl["barre"] and k == 1:
                miss[k] = av.finger_to("l", 1, t, self.in_neck, self._barre_extra(pl["barre"]),
                                       limits=((-0.3, 0.7), (0.0, 0.25), (-0.6, 0.6)), warm=warm)
            else:
                m = av.finger_to("l", k, t, self.in_neck, warm=warm)
                if k in pl["press"]:
                    miss[k] = m
        av.thumb_to("l", pl["thumb"], lambda pts: self.in_neck(pts, THUMB_MARGIN))
        return miss

    def pose_between(self, a, b, t, roles):
        """One frame of a chord change from plan a to plan b at time t (0..1)."""
        av = self.av
        s = t * t * (3 - 2 * t)
        knuckles = a["knuckles"].lerp(b["knuckles"], s)
        roll = a["p"][3] + (b["p"][3] - a["p"][3]) * s
        yaw = a["p"][4] + (b["p"][4] - a["p"][4]) * s
        F, N = self.frame_for(roll, yaw)
        wrist = av.wrist_for("l", av.hand_delta("l", F, N), "Finger2", knuckles)
        av.hand_to("l", F, N, wrist, self.pole)
        # the finger playing the lowest (bass) note of the new chord lands first
        lead = max(b["press"], key=lambda k: b["spec"]["fingers"][k][0]) if b["press"] else None
        for k in (1, 2, 3, 4):
            ta, tb, role = a["ends"][k], b["ends"][k], roles[k]
            if role == "anchor":
                target = tb
            elif role == "guide":
                target = ta.lerp(tb, s) + self.Z * 0.003 * math.sin(math.pi * s)
            else:
                t0, t1 = (0.0, 0.8) if k == lead else (0.12, 1.0)
                u = min(1.0, max(0.0, (t - t0) / (t1 - t0)))
                su = u * u * (3 - 2 * u)
                lift = 0.02 if role == "move" else 0.006
                target = ta.lerp(tb, su) + self.Z * lift * math.sin(math.pi * su)
            av.finger_to("l", k, target, self.in_neck, warm=av.angles.get(("l", k)))
        # the thumb travels around the outside of the neck, never through it
        thumb = self.thumb_point(a["thumb_x"] + (b["thumb_x"] - a["thumb_x"]) * s,
                                 a["thumb_angle"] + (b["thumb_angle"] - a["thumb_angle"]) * s)
        av.thumb_to("l", thumb, lambda pts: self.in_neck(pts, THUMB_MARGIN))

    def apply(self, name, spec):
        """Pose the hand for a chord on its own (chord library, first frame)."""
        av = self.av
        p, _ = self.solve(name, spec)
        press, hover, barre, thumb, _ = self.targets(spec)
        _, _, (F, N, wrist) = self.evaluate(spec, p, {}, fine=True)
        av.log.clear()
        av.hand_to("l", F, N, wrist, self.pole)
        miss = {}
        for k, t in press.items():
            miss[k] = av.finger_to("l", k, t, self.in_neck)
        if barre:
            miss[1] = av.finger_to("l", 1, barre[0], self.in_neck, self._barre_extra(barre),
                                   limits=((-0.3, 0.7), (0.0, 0.25), (-0.6, 0.6)))
        for k, t in hover.items():
            av.finger_to("l", k, t, self.in_neck)
        av.thumb_to("l", thumb, lambda pts: self.in_neck(pts, THUMB_MARGIN))
        return miss


# ------------------------------------------------------------------ keyframes
def snapshot(av, names):
    return {n: (av.pb(n).rotation_quaternion.copy(), av.pb(n).location.copy()) for n in names}


def key_pose(av, snap, frame, prev=None):
    for n, (q, loc) in snap.items():
        pb = av.pb(n)
        pb.rotation_mode = "QUATERNION"
        if prev is not None and prev[n][0].dot(q) < 0:
            q = -q  # keep quaternions on one hemisphere so interpolation takes the short way
        pb.rotation_quaternion = q
        pb.location = loc
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.keyframe_insert("location", frame=frame)
    return {n: (av.pb(n).rotation_quaternion.copy(), loc) for n, (_, loc) in snap.items()}


def animate_progression(fh, names, bar_frames, specs=None, start=1, change=8):
    """Chord changes the way a player makes them: fingers shared by both chords stay pressed (anchors),
    fingers that stay on their string slide along it (guides), the others lift and arc over to their new
    spots with the bass-note finger landing first, and the hand glides between the two solved placements.
    Every frame of a change is solved, so anchored fingertips really stay put on the string."""
    av = fh.av
    specs = specs or choose_fingerings(names, loop=False)
    plans, prev_k = [], None
    for name, spec in zip(names, specs):
        p, _ = fh.solve_near(spec, prev_k)
        plans.append(fh.chord_plan(name, spec, p))
        prev_k = plans[-1]["knuckles"]
    prev = None
    for i, pl in enumerate(plans):
        f0 = start + i * bar_frames
        roles = {}
        if i == 0:
            miss = fh.pose_plan(pl)
            prev = key_pose(av, snapshot(av, LEFT_ARM), f0, prev)
        else:
            roles = finger_roles(plans[i - 1]["spec"], pl["spec"])
            for j in range(1, change):
                fh.pose_between(plans[i - 1], pl, j / change, roles)
                prev = key_pose(av, snapshot(av, LEFT_ARM), f0 - change + j, prev)
            miss = fh.pose_plan(pl)
            prev = key_pose(av, snapshot(av, LEFT_ARM), f0, prev)
        prev = key_pose(av, snapshot(av, LEFT_ARM), f0 + bar_frames - change, prev)
        yield pl["name"], miss, roles
