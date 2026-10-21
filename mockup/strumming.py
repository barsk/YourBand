"""Strumming hand: down- and upstrokes that only hit the strings a chord actually uses.

How players strum (and what the reference photo shows):
- The forearm rests over the upper rear bout of the body and the hand works over the bridge pickup;
  the elbow sits behind and above the body edge, towards the tail of the guitar.
- The arm swings like a pendulum on eighth notes - down on the beat, up on the "and" - and keeps
  swinging even when a stroke is skipped; then the pick simply passes just outside the strings.
- A downstroke starts at the chord's lowest string (C skips the low E, D skips low E and A) and goes
  through to the high E. An upstroke mostly catches the top three strings, then the pick leaves the
  strings on the way back up.
- The wrist rolls a little with each sweep.
"""
import math

from mathutils import Matrix, Vector

from avatars import two_bone
from fretting import RIGHT_ARM, key_pose, snapshot

UP = Vector((0, 0, 1))
# lowest string a chord is strummed from (0 = high E ... 5 = low E)
LOWEST_STRING = {"C": 4, "G": 5, "D": 3, "A": 4, "E": 5, "Am": 4, "Em": 5, "Dm": 3, "F": 5}
UPSTROKE_STRINGS = 3  # upstrokes catch the top three strings (high E, B, G)
STRUM_PATTERN = "D-DU-UDU"  # one bar of eighth notes: D down, U up, - the hand swings past without hitting


class StrumHand:
    def __init__(self, av, g, pick_x=0.07, grip=None):
        """grip: finger rotations relative to the hand (e.g. riding.clip_grip of a captured player);
        without it, a pick grip of our own."""
        self.av, self.g = av, g
        X, Y, Z, W = g["X"], g["Y"], g["Z"], g["W"]
        self.X, self.Y, self.Z, self.W = X, Y, Z, W
        self.strings = g["strings"]
        self.px = g["bridge_x"] + pick_x  # over the bridge pickup
        self.z_strings = g["string_z"](self.px) + 0.001
        # elbow up at the body edge towards the tail, a little in front: the forearm lies over the rear upper bout
        self.pole = W((-0.4, 0.45, 0.1))
        self.F0, self.N0 = -Y + X * 0.2, -Z  # fingers point down across the strings, palm to the guitar top

        # pick grip: index curled, thumb on its last joint, the rest loosely closed
        Rp = av.hand_delta("r", self.F0, self.N0)
        wrist = av.wrist_for("r", Rp, "Finger1", W((self.px, 0.045, self.z_strings + 0.023)))
        av.hand_to("r", self.F0, self.N0, wrist, self.pole)
        if grip:  # the captured grip: thumb and index on the pick, the other fingers relaxed and extended
            for n, L in grip.items():
                av.set_rot(n, av.crot("R Hand") @ L)
            self.pick0 = (av.fingertip("r", 0) + av.head("R Finger12")) / 2
        else:
            for k, c in zip((1, 2, 3, 4), (1.2, 1.0, 0.85, 0.75)):
                av.finger("r", k, c)
            av.thumb_to("r", av.head("R Finger12"))
            self.pick0 = av.fingertip("r", 0)
        # pick position relative to the hand, in the hand's rest frame (fingers stay in the grip)
        self.pick_rest = Rp.transposed() @ (self.pick0 - av.head("R Hand"))
        self.shoulder = av.head("R UpperArm")
        self.l1 = (av.rhead("R Forearm") - av.rhead("R UpperArm")).length
        self.l2 = (av.rhead("R Hand") - av.rhead("R Forearm")).length
        self.body_inside, self.half_th = g["body_inside"], g["thickness"] / 2

    def _forearm_clearance(self, wrist, pole, radius=0.055):
        """How deep the forearm (a capsule of `radius`) would sink into the guitar body for this elbow pole."""
        el, _ = two_bone(self.shoulder, wrist, self.l1, self.l2, pole)
        c0 = self.W((0, 0, 0))
        depth = 0.0
        for i in range(0, 10):  # from the elbow (bulky jacket sleeves included) to the wrist
            d = el.lerp(wrist, i / 9) - c0
            x, y, z = d.dot(self.X), d.dot(self.Y), d.dot(self.Z)
            if abs(z) < self.half_th + radius and self.body_inside(x, y):
                depth = max(depth, self.half_th + radius - abs(z))
        return depth

    def place(self, y, lift, roll):
        """Put the pick at height y across the strings, `lift` metres out from them, wrist rolled by `roll`."""
        av = self.av
        R = Matrix.Rotation(roll, 3, self.X)
        F, N = R @ self.F0, R @ self.N0
        Rp = av.hand_delta("r", F, N)
        pick = self.W((self.px, y, self.z_strings + lift))
        wrist = pick - Rp @ self.pick_rest
        # rest the forearm on the body edge: raise the elbow further in front only if it would sink in
        pole = self.pole
        for k in range(8):
            pole = self.pole + self.Z * (0.06 * k) + self.Y * (0.03 * k)
            if self._forearm_clearance(wrist, pole) < 0.004:
                break
        av.hand_to("r", F, N, wrist, pole)

    def contact_window(self, stroke, chord):
        """Range across the strings (y) where the pick is in the strings for this stroke."""
        below_high = self.strings[0] - 0.004
        if stroke == "D":
            low = LOWEST_STRING.get(chord, 5)
            return below_high, self.strings[low] + 0.0035
        if stroke == "U":
            return below_high, self.strings[UPSTROKE_STRINGS - 1] + 0.0035
        return None


def animate_strumming(sh, chord_at, frames, eighth, pattern=STRUM_PATTERN, start=1,
                      amplitude=0.036, lift=0.012, roll=0.22):
    """Key the strumming arm on every frame. chord_at(frame) -> chord name (decides the strings hit)."""
    prev = None
    for f in range(start, start + frames + 1):
        t = (f - start) / eighth                  # in eighth notes; strings are crossed on whole numbers
        swing = -math.sin(math.pi * t)            # down on the beats, up on the "and"s
        y = amplitude * swing
        stroke = pattern[int(round(t)) % len(pattern)]
        window = sh.contact_window(stroke, chord_at(f))
        if window is None:
            out = lift
        else:
            lo, hi = window
            outside = max(lo - y, y - hi, 0.0)    # how far the pick is beyond the strings it should hit
            u = min(1.0, outside / 0.006)
            out = lift * u * u * (3 - 2 * u)
        sh.place(y, out, roll * swing)
        prev = key_pose(sh.av, snapshot(sh.av, RIGHT_ARM), f, prev)
