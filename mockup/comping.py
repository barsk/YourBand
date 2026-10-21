"""Keyboardist: comping the progression on the Yamaha, keyed on every frame.

- Right hand: close triads pulsing on every beat (thumb, middle and little finger; index and ring hover
  in between). The voicings move by the smallest steps: G-B-D, G-B-E, G-C-E, A-D-F#.
- Left hand: root, fifth and octave on beats 1 and 3 (little finger, index, thumb).
- A stroke: the finger meets the key on the beat, the key goes down 8 mm, holds, releases and the finger
  rises a few millimetres before the next one.
- Chord changes: in the gap after the last stroke of a bar the hand lifts slightly and glides to the next
  voicing, landing on the downbeat.
- The keys themselves go down while they are held.

The player sits at the keyboard with Mixamo's piano capture as the body layer (mocap.Layer): hips,
back, shoulders, head and legs are the captured performance; the arms reach from the captured shoulders
to the chord hands above.
"""
import math

from mathutils import Vector

from cache import Cache
from fretting import key_pose, snapshot
from song import chord_tones

UP = Vector((0, 0, 1))
ANCHORS = Cache("comping", ["comping.py", "avatars.py"])  # fitted hand positions per voicing
# semitones from the lowest C (C2 = 0, middle C = 24): left (root, fifth, octave), right (bottom, middle, top)
def voicing(chord):
    """(left, right) semitones from the lowest C (C2) for a chord name: the left hand root, fifth and
    octave with the root between E2 and D#3; the right hand a close triad, its lowest note the first chord
    tone from G4 up (G-B-D, G-B-E, G-C-E, A-D-F#: the voicings move by the smallest steps)."""
    tones = chord_tones(chord)[:3]
    r = tones[0] if tones[0] >= 4 else tones[0] + 12
    best = None
    for k in range(3):
        bottom = 24 + (tones[k] - 24) % 12
        while bottom < 31:
            bottom += 12
        triad = sorted(bottom + (tones[(k + j) % 3] - tones[k]) % 12 for j in range(3))
        if best is None or triad[0] < best[0]:
            best = triad
    return (r, r + 7, r + 12), tuple(best)
KEY_TRAVEL, FINGER_LIFT, HAND_LIFT, HOVER = 0.008, 0.006, 0.015, 0.012
CHANGE = 0.35      # beats before the downbeat in which the hands move to the next chord
PERIOD = {"r": 1.0, "l": 2.0}  # beats between strokes



def _smooth(u):
    u = min(max(u, 0.0), 1.0)
    return u * u * (3 - 2 * u)


def _stroke(phase):
    """(press, lift) through one stroke period: down at 0, held, released at 0.65, back on the key at 1."""
    if phase < 0.06:
        return _smooth(phase / 0.06), 0.0
    if phase < 0.65:
        return 1.0, 0.0
    if phase < 0.75:
        u = _smooth((phase - 0.65) / 0.1)
        return 1.0 - u, u
    if phase < 0.9:
        return 0.0, 1.0
    return 0.0, 1.0 - _smooth((phase - 0.9) / 0.1)


class KeysPlayer:
    def __init__(self, av, kb, layer, anchor):
        self.av, self.kb = av, kb
        self.layer, self.hips = layer, anchor  # captured body, and where its hips sit
        self.notes, self.objects = kb["notes"], kb["key_objects"]
        self.rest = [o.location.z for o in self.objects]
        self.travel = KEY_TRAVEL / 0.0254  # key objects move in the keyboard's model units (inches)
        self.F, self.N = av.fwd - UP * 0.1, -UP  # nearly level hands, palms down
        self.miss, self.fits = {}, {}

    def layout(self, side, chord):
        """{finger: key point} for a voicing: pressed fingers on their keys, hovering ones in between."""
        n = self.notes
        left, right = voicing(chord)
        if side == "r":
            a, b, c = (n[i] for i in right)
            return {0: a, 2: b, 4: c}, {1: a.lerp(b, 0.5), 3: b.lerp(c, 0.5)}, right
        a, b, c = (n[i] for i in left)
        return {4: a, 1: b, 0: c}, {3: a.lerp(b, 0.35), 2: a.lerp(b, 0.7)}, left

    def anchor(self, side, chord):
        """Where the middle knuckle goes for a voicing: above the middle of its keys, offset (up, towards
        the player, towards the thumb) by a small search so that the pressed fingertips land."""
        if (side, chord) in self.fits:
            return self.fits[(side, chord)]
        av = self.av
        press, hover, _ = self.layout(side, chord)
        # the search depends on the keys, the captured shoulder and the fingers it starts from
        S = "L" if side == "l" else "R"
        warm = {k: v for k, v in av.angles.items() if k[0] == side}
        key = ANCHORS.key(av.arm.name, side, press, hover, self.F, self.N, av.fwd, av.right,
                          av.head(S + " UpperArm"), av.hand_rest_frame(side),
                          [av._chain(side, k)[1:] for k in range(5)], warm)
        hit = ANCHORS.get(key)
        if hit is not None:
            self.fits[(side, chord)] = Vector(hit)
            return self.fits[(side, chord)]
        pts = list(press.values()) + list(hover.values())
        mid = sum(pts, Vector()) / len(pts)
        sgn = -1 if side == "l" else 1
        best = None
        for h in (0.03, 0.04, 0.05):
            for back in (0.0, 0.02, 0.04):
                for dx in (-0.012, 0.0, 0.012, 0.024):
                    a = mid + UP * h - av.fwd * back - av.right * (dx * sgn)
                    cost = self.place(side, a, press, hover, UP * -KEY_TRAVEL, fit=True)  # keys held down
                    if best is None or cost < best[0]:
                        best = (cost, a)
        for k in [k for k in av.angles if k[0] == side]:  # the search leaves no trace: the frames go on
            del av.angles[k]                                # from their own fingers
        av.angles.update(warm)
        self.fits[(side, chord)] = best[1]
        ANCHORS.put(key, tuple(best[1]))
        return best[1]

    def place(self, side, a, press, hover, dz, lift=0.0, fit=False):
        """Hand with its middle knuckle at a, fingers on (or hovering over) their keys; returns the summed
        fingertip miss of the pressed fingers (thumb included)."""
        av = self.av
        Rh = av.hand_delta(side, self.F, self.N)
        wrist = av.wrist_for(side, Rh, "Finger2", a)
        sgn = -1 if side == "l" else 1
        pole = av.head(("L" if side == "l" else "R") + " UpperArm") + av.right * (0.3 * sgn) - av.fwd * 0.15 - UP * 0.3
        av.hand_to(side, self.F, self.N, wrist, pole)
        cost = 0.0
        for k, p in press.items():
            if k == 0:
                t = p + dz + UP * 0.004 - av.fwd * 0.01
                av.thumb_to(side, t)
                cost += (av.fingertip(side, 0) - t).length
            else:
                m = av.finger_to(side, k, p + dz, warm=av.angles.get((side, k)))
                cost += 2 * m
                if not fit and lift == 0.0 and dz.z < -0.0079:
                    self.miss[(side, k)] = max(self.miss.get((side, k), 0.0), m)
        for k, p in hover.items():
            av.finger_to(side, k, p + dz + UP * HOVER, warm=av.angles.get((side, k)))
        return cost

    def pose(self, t, names, k=0):
        """Pose for time t in beats (output frame k); returns {key index: press 0..1}."""
        av = self.av
        W, hips = self.layer.pose(k)
        self.layer.rt.apply(W, self.hips + hips)
        bar = min(int(t // 4), len(names) - 1)
        tb = t - 4 * bar
        keys = {}
        for side in ("l", "r"):
            press, hover, idx = self.layout(side, names[bar])
            a = self.anchor(side, names[bar])
            if tb > 4 - CHANGE and bar + 1 < len(names):  # glide to the next chord
                s = _smooth((tb - (4 - CHANGE)) / CHANGE)
                press2, hover2, _ = self.layout(side, names[bar + 1])
                a = a.lerp(self.anchor(side, names[bar + 1]), s) + UP * (HAND_LIFT * math.sin(math.pi * s))
                press = {k: p.lerp(press2[k], s) for k, p in press.items()}
                hover = {k: p.lerp(hover2[k], s) for k, p in hover.items()}
                down, lift = 0.0, 1.0
            else:
                down, lift = _stroke((tb % PERIOD[side]) / PERIOD[side])
                for i in idx:
                    keys[i] = max(keys.get(i, 0.0), down)
            self.place(side, a, press, hover, UP * (FINGER_LIFT * lift - KEY_TRAVEL * down), lift)
        return keys

    def key_keys(self, pressed, last, frame):
        """Move and key the keyboard's keys that are, or just were, held down."""
        for i in set(pressed) | {i for i, v in last.items() if v > 0}:
            o = self.objects[i]
            if pressed.get(i, 0.0) > 0 and last.get(i, 0.0) == 0:  # hold the key up until it is struck
                o.location.z = self.rest[i]
                o.keyframe_insert("location", index=2, frame=frame - 1)
            o.location.z = self.rest[i] - self.travel * pressed.get(i, 0.0)
            o.keyframe_insert("location", index=2, frame=frame)


def animate_comping(player, names, beat, start=1):
    """Key the keyboardist (and the keys) on every frame; beat = frames per beat."""
    av, prev, last = player.av, None, {}
    for o in player.objects:
        o.keyframe_insert("location", index=2, frame=start - 1)
    for k, f in enumerate(range(start, start + int(round(len(names) * 4 * beat)) + 1)):
        pressed = player.pose((f - start) / beat, names, k)
        prev = key_pose(av, snapshot(av, player.layer.rt.order), f, prev)
        player.key_keys(pressed, last, f)
        last = pressed
    return {"%s%d" % k: round(v * 1000, 1) for k, v in sorted(player.miss.items())}
