"""Songs from MIDI files: what each member of the band plays.

A Standard MIDI File (format 0 or 1) is read without outside libraries: tracks, the tempo map, the time
signature, program changes and notes. Parts are assigned the General MIDI way - channel 10 is the drum
kit, programs 32-39 are basses, 24-31 guitars, 0-23 keyboards (pianos, chromatic percussion, organs) -
with track names as a fallback ("bass", "guitar", "piano", ...). From the parts:

- chords, bar by bar (or half bar): the pitch classes sounding, weighted by how long they sound, matched
  against chord templates, the lowest note favouring the root;
- the drummer's hits per limb, from the General MIDI drum map: kick to the right foot, snare (and toms)
  to the left hand, hi-hat, ride and crash to the right hand; open hi-hat notes say when the left foot
  lets the hi-hat open;
- the bass line as notes, with a string and fret for each;
- the guitar's strumming: down on the beats, up between them, a stroke wherever a chord is struck;
- notes for the keyboard player's two hands.

Times are in beats from the start of the song (quarter notes), as the animation code counts in beats.
`write_midi` and `demo_song` write a small song for testing.
"""
import os
import struct
from collections import Counter, defaultdict

NAMES = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"]
CHORD_TYPES = {"": (0, 4, 7), "m": (0, 3, 7), "7": (0, 4, 7, 10), "maj7": (0, 4, 7, 11), "m7": (0, 3, 7, 10),
               "sus2": (0, 2, 7), "sus4": (0, 5, 7), "dim": (0, 3, 6), "5": (0, 7)}
BASS_STRINGS = (43, 38, 33, 28)  # G2 D2 A1 E1: string 0 is the highest, as for the guitar
DRUMS = {  # General MIDI percussion -> (limb, target)
    35: ("kick", None), 36: ("kick", None),
    37: ("l", "snare"), 38: ("l", "snare"), 39: ("l", "snare"), 40: ("l", "snare"),
    42: ("r", "hihat"), 44: ("pedal", None), 46: ("r", "hihat"),
    49: ("r", "crash"), 52: ("r", "crash"), 55: ("r", "crash"), 57: ("r", "crash"),
    51: ("r", "ride"), 53: ("r", "ride"), 59: ("r", "ride"),
    41: ("l", "floor_tom"), 43: ("l", "floor_tom"), 45: ("l", "tom2"), 47: ("l", "tom2"),
    48: ("r", "tom1"), 50: ("r", "tom1")}
OPEN_HIHAT = 46


class Note:
    __slots__ = ("start", "end", "pitch", "velocity", "channel", "program", "track")

    def __init__(self, start, end, pitch, velocity, channel, program, track):
        self.start, self.end, self.pitch, self.velocity = start, end, pitch, velocity
        self.channel, self.program, self.track = channel, program, track

    def __repr__(self):
        return "Note(%.2f-%.2f %s%d v%d ch%d)" % (self.start, self.end, NAMES[self.pitch % 12],
                                                  self.pitch // 12 - 1, self.velocity, self.channel + 1)


# ----------------------------------------------------------------- reading
def _varlen(data, i):
    v = 0
    while True:
        b = data[i]
        i += 1
        v = (v << 7) | (b & 0x7F)
        if not b & 0x80:
            return v, i


def read_midi(path):
    """(ticks per quarter note, [(track name, [(tick, kind, value...)])]) for a Standard MIDI File."""
    data = open(path, "rb").read()
    if data[:4] != b"MThd":
        raise ValueError("not a Standard MIDI File: %s" % path)
    hlen = struct.unpack(">I", data[4:8])[0]
    fmt, ntracks, division = struct.unpack(">HHH", data[8:14])
    if division & 0x8000:
        raise ValueError("SMPTE time division is not supported")
    i, tracks = 8 + hlen, []
    while i < len(data) and len(tracks) < ntracks:
        tag, size = data[i:i + 4], struct.unpack(">I", data[i + 4:i + 8])[0]
        chunk, i = data[i + 8:i + 8 + size], i + 8 + size
        if tag != b"MTrk":
            continue
        events, name, tick, j, status = [], "", 0, 0, 0
        while j < len(chunk):
            delta, j = _varlen(chunk, j)
            tick += delta
            b = chunk[j]
            if b == 0xFF:  # meta event
                kind, j = chunk[j + 1], j + 2
                n, j = _varlen(chunk, j)
                body, j = chunk[j:j + n], j + n
                if kind == 0x51:
                    events.append((tick, "tempo", int.from_bytes(body, "big")))
                elif kind == 0x58:
                    events.append((tick, "meter", body[0], 2 ** body[1]))
                elif kind == 0x03 and not name:
                    name = body.decode("latin-1", "replace").strip()
                continue
            if b in (0xF0, 0xF7):  # sysex
                n, j = _varlen(chunk, j + 1)
                j += n
                continue
            if b & 0x80:
                status, j = b, j + 1
            kind, ch = status & 0xF0, status & 0x0F
            if kind in (0xC0, 0xD0):
                value, j = chunk[j], j + 1
                if kind == 0xC0:
                    events.append((tick, "program", ch, value))
                continue
            a, c, j = chunk[j], chunk[j + 1], j + 2
            if kind == 0x90 and c > 0:
                events.append((tick, "on", ch, a, c))
            elif kind in (0x80, 0x90):
                events.append((tick, "off", ch, a))
        tracks.append((name, events))
    return division, tracks


def _part(note, name):
    n = name.lower()
    if note.channel == 9:
        return "drums"
    for part, words in (("bass", ("bass",)), ("guitar", ("guit", "gtr")),
                        ("keys", ("piano", "key", "organ", "synth", "rhodes", "pad"))):
        if any(w in n for w in words):
            return part
    p = note.program
    if 32 <= p <= 39:
        return "bass"
    if 24 <= p <= 31:
        return "guitar"
    if p <= 23 or 80 <= p <= 95:
        return "keys"
    return "other"


class Song:
    def __init__(self, path):
        self.path = path
        ppq, tracks = read_midi(path)
        self.tempos = [(0, 500000)]
        self.meter = (4, 4)
        notes, programs = [], defaultdict(int)
        for name, events in tracks:
            for ev in events:
                if ev[1] == "tempo":
                    self.tempos.append((ev[0], ev[2]))
                elif ev[1] == "meter" and ev[0] == 0:
                    self.meter = (ev[2], ev[3])
        self.tempos = sorted({t: v for t, v in sorted(self.tempos)}.items())
        for name, events in tracks:
            pending = {}
            for ev in sorted(events, key=lambda e: e[0]):
                if ev[1] == "program":
                    programs[ev[2]] = ev[3]
                elif ev[1] == "on":
                    pending.setdefault((ev[2], ev[3]), []).append((ev[0], ev[4], programs[ev[2]]))
                elif ev[1] == "off" and pending.get((ev[2], ev[3])):
                    t0, vel, prog = pending[(ev[2], ev[3])].pop(0)
                    notes.append(Note(t0 / ppq, ev[0] / ppq, ev[3], vel, ev[2], prog, name))
        self.ppq = ppq
        self.notes = sorted(notes, key=lambda n: (n.start, n.pitch))
        self.parts = defaultdict(list)
        for n in self.notes:
            self.parts[_part(n, n.track)].append(n)
        self.beats_per_bar = self.meter[0] * 4 / self.meter[1]
        end = max((n.end for n in self.notes), default=0.0)
        self.bars = int(-(-end // self.beats_per_bar))
        self.bpm = 60e6 / self.tempos[0][1]

    def seconds(self, beat):
        """Seconds from the start for a time in beats (follows the tempo map)."""
        s, last_b, last_us = 0.0, 0.0, self.tempos[0][1]
        for tick, us in self.tempos[1:]:
            b = tick / self.ppq
            if b >= beat:
                break
            s += (b - last_b) * last_us / 1e6
            last_b, last_us = b, us
        return s + (beat - last_b) * last_us / 1e6

    # -------------------------------------------------------------- chords
    def chords(self, per_bar=1):
        """A chord name for each bar (or each 1/per_bar of a bar) from the pitched parts."""
        pitched = [n for p, ns in self.parts.items() if p != "drums" for n in ns]
        step = self.beats_per_bar / per_bar
        out, last = [], None
        for k in range(self.bars * per_bar):
            a, b = k * step, (k + 1) * step
            weight, lowest = Counter(), None
            for n in pitched:
                d = min(b, n.end) - max(a, n.start)
                if d > 0:
                    weight[n.pitch % 12] += d * (1.5 if n in self.parts["bass"] else 1.0)
                    if lowest is None or n.pitch < lowest:
                        lowest = n.pitch
            name = chord_name(weight, None if lowest is None else lowest % 12) or last
            out.append(name)
            last = name
        return out

    # --------------------------------------------------------------- drums
    def drum_hits(self):
        """Per limb, in eighth notes from the start: {"r": [(eighth, target)], "l": [...], "kick": [...],
        "open": [(from, to)] spans where the hi-hat is open}."""
        hits = {"r": [], "l": [], "kick": [], "open": []}
        for n in self.parts["drums"]:
            limb, target = DRUMS.get(n.pitch, (None, None))
            e = n.start * 2
            if limb == "kick":
                hits["kick"].append(e)
            elif limb in ("r", "l"):
                hits[limb].append((e, target))
            if n.pitch == OPEN_HIHAT:
                hits["open"].append((e, max(n.end * 2, e + 0.9)))
        for k in ("r", "l"):  # one stroke per hand at a time: two notes together go to both hands
            merged = []
            for e, t in sorted(hits[k]):
                if merged and abs(merged[-1][0] - e) < 1e-6:
                    other = "l" if k == "r" else "r"
                    hits[other].append((e, t))
                    continue
                merged.append((e, t))
            hits[k] = merged
        for k in ("r", "l"):
            hits[k] = sorted(set(hits[k]))
        hits["kick"] = sorted(set(hits["kick"]))
        return hits

    # ---------------------------------------------------------------- bass
    def bass_line(self):
        """[(start beat, end beat, pitch, (string, fret))] choosing positions a bassist would: low frets,
        close to the previous note."""
        out, prev = [], None
        for n in self.parts["bass"]:
            best = None
            for s, open_pitch in enumerate(BASS_STRINGS):
                fret = n.pitch - open_pitch
                if 0 <= fret <= 17:
                    cost = fret * 0.1 + (abs(fret - prev[1]) + 2 * abs(s - prev[0]) if prev else 0)
                    if best is None or cost < best[0]:
                        best = (cost, (s, fret))
            if best:
                out.append((n.start, n.end, n.pitch, best[1]))
                prev = best[1]
        return out

    def bass_roots(self):
        """The first bass note in each bar (string, fret), or None."""
        line = self.bass_line()
        out = []
        for bar in range(self.bars):
            a = bar * self.beats_per_bar
            first = next(((s, f) for t0, _, _, (s, f) in line if a <= t0 < a + self.beats_per_bar), None)
            out.append(first)
        return out

    # -------------------------------------------------------------- guitar
    def strum_pattern(self):
        """The guitar's commonest bar of eighth-note strokes as a string: D down on a beat, U up between
        beats, - no stroke. Chord onsets within 30 ms of each other count as one stroke."""
        onsets = sorted({round(n.start * 2, 2) for n in self.parts["guitar"]})
        bars = defaultdict(set)
        per = int(round(self.beats_per_bar * 2))
        for e in onsets:
            slot = int(round(e))
            if abs(e - slot) < 0.2:
                bars[slot // per].add(slot % per)
        if not bars:
            return None
        common = Counter(tuple(sorted(s)) for s in bars.values()).most_common(1)[0][0]
        return "".join(("D" if i % 2 == 0 else "U") if i in common else "-" for i in range(per))

    # ---------------------------------------------------------------- keys
    def keys_notes(self, split=60):
        """The keyboard part split between the hands at middle C: {"l": [notes], "r": [notes]}."""
        out = {"l": [], "r": []}
        for n in self.parts["keys"]:
            out["l" if n.pitch < split else "r"].append(n)
        return out

    def summary(self):
        return dict(bpm=round(self.bpm, 1), meter="%d/%d" % self.meter, bars=self.bars,
                    parts={p: len(ns) for p, ns in self.parts.items()}, chords=self.chords(),
                    strum=self.strum_pattern())


def chord_name(weight, bass=None):
    """Best matching chord for pitch-class weights (the bass note favours its own root)."""
    total = sum(weight.values())
    if not total:
        return None
    best = None
    for root in range(12):
        for suffix, tones in CHORD_TYPES.items():
            pcs = {(root + t) % 12 for t in tones}
            hit = sum(w for pc, w in weight.items() if pc in pcs)
            miss = sum(w for pc, w in weight.items() if pc not in pcs)
            absent = sum(1 for pc in pcs if weight.get(pc, 0) < 0.05 * total)
            score = hit - 1.0 * miss - 0.35 * total * absent - 0.04 * total * len(tones)
            if bass is not None and bass == root:
                score += 0.25 * total
            if best is None or score > best[0]:
                best = (score, NAMES[root] + suffix)
    return best[1]


def chord_tones(name):
    """Pitch classes of a chord name such as "Em", "G7" or "F#m7"."""
    root = 2 if len(name) > 1 and name[1] in "#b" else 1
    pc = NAMES.index(name[:root]) if name[:root] in NAMES else {"Db": 1, "D#": 3, "Gb": 6, "G#": 8, "A#": 10}[name[:root]]
    return [(pc + t) % 12 for t in CHORD_TYPES[name[root:]]]


# ----------------------------------------------------------------- writing
def _vl(v):
    out = [v & 0x7F]
    v >>= 7
    while v:
        out.insert(0, (v & 0x7F) | 0x80)
        v >>= 7
    return bytes(out)


def write_midi(path, tracks, bpm, ppq=480, meter=(4, 4)):
    """tracks: [(name, channel, program, [(start beat, length beats, pitch, velocity)])] -> format 1 file."""
    chunks = []
    head = (b"\x00\xFF\x51\x03" + int(60e6 / bpm).to_bytes(3, "big")
            + b"\x00\xFF\x58\x04" + bytes((meter[0], {2: 1, 4: 2, 8: 3}[meter[1]], 24, 8)) + b"\x00\xFF\x2F\x00")
    chunks.append(head)
    for name, ch, program, notes in tracks:
        evs = []
        for start, length, pitch, vel in notes:
            evs.append((int(round(start * ppq)), 1, bytes((0x90 | ch, pitch, vel))))
            evs.append((int(round((start + length) * ppq)), 0, bytes((0x80 | ch, pitch, 0))))
        body = b"\x00\xFF\x03" + _vl(len(name)) + name.encode() + b"\x00" + bytes((0xC0 | ch, program))
        last = 0
        for tick, _, msg in sorted(evs):
            body += _vl(tick - last) + msg
            last = tick
        chunks.append(body + b"\x00\xFF\x2F\x00")
    with open(path, "wb") as fh:
        fh.write(b"MThd" + struct.pack(">IHHH", 6, 1, len(chunks), ppq))
        for c in chunks:
            fh.write(b"MTrk" + struct.pack(">I", len(c)) + c)


def demo_song(path, progression=("C", "G", "Am", "F"), repeats=2, bpm=96):
    """A small pop-rock song for testing: rock beat (crash on each phrase), root-note bass in eighths,
    strummed guitar (D-DU-UDU), keyboard triads on the beat with the root below."""
    drums, bass, gtr, keys = [], [], [], []
    bars = [c for _ in range(repeats) for c in progression]
    for b, ch in enumerate(bars):
        t0 = b * 4
        tones = chord_tones(ch)
        root = tones[0]
        for e in range(8):
            drums.append((t0 + e / 2, 0.25, 49 if (e == 0 and b % 4 == 0) else 42, 70 if e % 2 else 95))
        for beat in (1, 3):
            drums.append((t0 + beat, 0.25, 38, 110))
        for e in (0, 4, 5):
            drums.append((t0 + e / 2, 0.25, 36, 115))
        bass_pitch = 28 + (root - 4) % 12  # lowest root from low E
        for e in range(8):
            bass.append((t0 + e / 2, 0.45, bass_pitch, 100))
        shape = sorted(52 + (pc - 4) % 12 for pc in tones[:3]) + [64 + (tones[0] - 4) % 12]
        for e, c in enumerate("D-DU-UDU"):
            if c != "-":
                for k, p in enumerate(shape):
                    gtr.append((t0 + e / 2 + 0.01 * k, 0.45, p, 90 if c == "D" else 70))
        for beat in range(4):
            for pc in tones[:3]:
                keys.append((t0 + beat, 0.9, 60 + (pc - 60) % 12, 80))
        keys.append((t0, 3.9, 36 + root, 85))
    write_midi(path, [("Drums", 9, 0, drums), ("Bass", 1, 33, bass), ("Guitar", 2, 25, gtr), ("Piano", 3, 0, keys)], bpm)
    return path


if __name__ == "__main__":
    import sys
    p = sys.argv[1] if len(sys.argv) > 1 else demo_song(os.path.join(os.path.dirname(os.path.abspath(__file__)), "songs", "demo.mid"))
    s = Song(p)
    print(s.summary())
    h = s.drum_hits()
    print("drums: right", h["r"][:9], "\n       left", h["l"][:4], "\n       kick", h["kick"][:6], "open", h["open"][:2])
    print("bass:", s.bass_line()[:5], "\nroots per bar:", s.bass_roots())
    print("keys:", {k: v[:4] for k, v in s.keys_notes().items()})
