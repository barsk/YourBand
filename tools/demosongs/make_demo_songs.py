"""Writes YourBand's four demo songs as Guitar Pro 5 files.

The songs are original compositions written for YourBand: a blues-rock boogie, a pop song, a punk song and a
fingerpicked ballad. Each has a rhythm guitar (with chord names and diagrams), a lead guitar, a bass and drums,
and section markers, so every part of the Guitar Pro import has something to read.

    python -m venv venv && venv/Scripts/pip install PyGuitarPro
    venv/Scripts/python tools/demosongs/make_demo_songs.py [out-dir]

The files go to tools/demosongs/songs/ by default.

Times are counted in sixteenth notes: a 4/4 bar is 16. A part is a list of events per bar,
(length, notes, effects), where notes is a list of (string, fret) (string 1 is the high e) and an empty list is a rest.
"""

import os
import sys

import guitarpro as gp

ARTIST = 'YourBand Demo'
NOTICE = ['Original demo song written for YourBand.']

STANDARD = [64, 59, 55, 50, 45, 40]     # e B G D A E, string 1 first
BASS4 = [43, 38, 33, 28]                # G D A E

# Chord shapes, frets from the low E string to the high e (-1 = not played).
SHAPES = {
    'G':     [3, 2, 0, 0, 0, 3],
    'D':     [-1, -1, 0, 2, 3, 2],
    'Em':    [0, 2, 2, 0, 0, 0],
    'C':     [-1, 3, 2, 0, 1, 0],
    'Am':    [-1, 0, 2, 2, 1, 0],
    'Fmaj7': [-1, -1, 3, 2, 1, 0],
    'A7':    [-1, 0, 2, 0, 2, 0],
    'A5':    [-1, 0, 2, -1, -1, -1],
    'D5':    [-1, -1, 0, 2, -1, -1],
    'E5':    [0, 2, -1, -1, -1, -1],
}
POWER = {   # power chords: root string (6 or 5) and fret
    'E5': (6, 0), 'G5': (6, 3), 'A5': (5, 0), 'B5': (5, 2), 'C5': (5, 3), 'D5': (5, 5),
}
PITCH_CLASS = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}

# Drums (General MIDI)
KICK, SNARE, STICK, HAT, OPEN_HAT, CRASH, RIDE, TOM_HI, TOM_MID, TOM_FLOOR = 36, 38, 37, 42, 46, 49, 51, 48, 45, 43
DRUM_STRING = {HAT: 1, OPEN_HAT: 1, RIDE: 1, CRASH: 2, SNARE: 3, STICK: 3, KICK: 4, TOM_HI: 5, TOM_MID: 5, TOM_FLOOR: 6}

DURATIONS = {16: (1, False), 12: (2, True), 8: (2, False), 6: (4, True), 4: (4, False), 3: (8, True), 2: (8, False),
             1: (16, False)}

VERSE_VEL, CHORUS_VEL = gp.Velocities.mezzoForte, gp.Velocities.forte


def midi(name):
    """'F#4' -> 66."""
    letter, rest = name[0], name[1:]
    acc = 0
    while rest and rest[0] in '#b':
        acc += 1 if rest[0] == '#' else -1
        rest = rest[1:]
    return 12 * (int(rest) + 1) + PITCH_CLASS[letter] + acc


def place(pitch, tuning, center, lo=0, hi=20):
    """The string and fret for a pitch: the fret nearest the hand position `center`."""
    options = [(abs(pitch - open_ - center), s + 1, pitch - open_)
               for s, open_ in enumerate(tuning) if lo <= pitch - open_ <= hi]
    if not options:
        raise ValueError(f'pitch {pitch} does not fit the instrument')
    _, string, fret = min(options)
    return string, fret


def split(length):
    """A length in sixteenths as note values that GP can write."""
    parts = []
    for size in (16, 12, 8, 6, 4, 3, 2, 1):
        while length >= size:
            parts.append(size)
            length -= size
    return parts


def shape_notes(name, strings=None):
    """The (string, fret) notes of a chord shape; `strings` picks some of its strings (numbers, 1 = high e)."""
    frets = SHAPES[name]
    notes = [(6 - i, f) for i, f in enumerate(frets) if f >= 0]
    if strings is not None:
        notes = [n for n in notes if n[0] in strings]
    return notes


def power_notes(name, strings=3):
    string, fret = POWER[name]
    notes = [(string, fret), (string - 1, fret + 2)]
    if strings == 3:
        notes.append((string - 2, fret + 2))
    return notes


def make_chord(name, frets):
    """A chord diagram, frets from the low E string."""
    root = name[:2] if len(name) > 1 and name[1] in '#b' else name[:1]
    suffix = name[len(root):]
    kinds = {'': gp.ChordType.major, 'm': gp.ChordType.minor, '7': gp.ChordType.seventh,
             'maj7': gp.ChordType.majorSeventh, '5': gp.ChordType.power}
    chord = gp.Chord(length=6, name=name, sharp=True, root=gp.PitchClass(midi(root + '0') % 12),
                     type=kinds[suffix], newFormat=True, show=True, add=False)
    played = [f for f in frets if f > 0]
    chord.firstFret = max(1, min(played)) if played and max(played) > 4 else 1
    chord.strings = list(reversed(frets))
    return chord


def chord_diagram(name):
    if name in SHAPES:
        return make_chord(name, SHAPES[name])
    string, fret = POWER[name]
    frets = [-1] * 6
    low = 6 - string
    frets[low], frets[low + 1], frets[low + 2] = fret, fret + 2, fret + 2
    return make_chord(name, frets)


class Song:
    def __init__(self, title, tempo, key, sections):
        """sections: (name, [chord of each bar]); each bar has one chord."""
        self.title, self.tempo, self.key = title, tempo, key
        self.bars = []          # (section name, index in section, chord, section length)
        for name, chords in sections:
            for i, chord in enumerate(chords):
                self.bars.append((name, i, chord, len(chords)))
        self.tracks = []

    def add(self, name, program, tuning, events, percussion=False):
        assert len(events) == len(self.bars), (name, len(events), len(self.bars))
        for n, bar in enumerate(events):
            assert sum(e[0] for e in bar) == 16, (self.title, name, n + 1, sum(e[0] for e in bar))
        self.tracks.append((name, program, tuning, events, percussion))

    def write(self, path):
        song = gp.Song(title=self.title, artist=ARTIST, tempo=self.tempo, key=self.key,
                       notice=list(NOTICE), tab='YourBand', music=ARTIST, words='')
        song.measureHeaders = []
        song.tracks = []
        start = gp.Duration.quarterTime
        for n, (section, index, chord, _) in enumerate(self.bars):
            header = gp.MeasureHeader(number=n + 1, start=start, keySignature=self.key)
            if index == 0:
                header.marker = gp.Marker(title=section, color=gp.Color(200, 40, 40))
            song.addMeasureHeader(header)
            start += 4 * gp.Duration.quarterTime
        channel = 0
        for number, (name, program, tuning, events, percussion) in enumerate(self.tracks, 1):
            if percussion:
                midi_channel = gp.MidiChannel(channel=9, effectChannel=9, instrument=0)
            else:
                if channel == 9:
                    channel = 10
                midi_channel = gp.MidiChannel(channel=channel, effectChannel=channel + 1, instrument=program)
                channel += 2
            track = gp.Track(song, number=number, name=name, isPercussionTrack=percussion, channel=midi_channel,
                             strings=[gp.GuitarString(i + 1, v) for i, v in enumerate(tuning)],
                             color=gp.Color(200, 40, 40))
            for measure, bar_events, bar in zip(track.measures, events, self.bars):
                self._fill(measure, bar_events, bar[2] if number == 1 else None)
            song.tracks.append(track)
        gp.write(song, path)

    @staticmethod
    def _fill(measure, events, chord):
        voice = measure.voices[0]
        first = True
        for length, notes, fx in events:
            for piece, size in enumerate(split(length)):
                value, dotted = DURATIONS[size]
                beat = gp.Beat(voice, duration=gp.Duration(value=value, isDotted=dotted),
                               status=gp.BeatStatus.normal if notes else gp.BeatStatus.rest)
                if first and chord and fx.get('chord', True):
                    beat.effect.chord = chord_diagram(chord)
                first = False
                if piece == 0 and fx.get('pick'):
                    beat.effect.pickStroke = (gp.BeatStrokeDirection.down if fx['pick'] == 'D'
                                              else gp.BeatStrokeDirection.up)
                for string, fret in notes:
                    effect = gp.NoteEffect(palmMute=fx.get('pm', False), letRing=fx.get('ring', False),
                                           accentuatedNote=piece == 0 and fx.get('acc', False),
                                           vibrato=fx.get('vib', False) and piece == len(split(length)) - 1,
                                           hammer=piece == 0 and fx.get('h', False))
                    if piece == 0 and fx.get('bend'):
                        effect.bend = bend(fx['bend'])
                    if piece == 0 and fx.get('slidein'):
                        effect.slides = [gp.SlideType.intoFromBelow]
                    if fx.get('slide') and piece == len(split(length)) - 1:
                        effect.slides = [gp.SlideType.legatoSlideTo]
                    note = gp.Note(beat, value=fret, string=string, effect=effect,
                                   velocity=fx.get('vel', gp.Velocities.forte),
                                   type=gp.NoteType.normal if piece == 0 else gp.NoteType.tie)
                    beat.notes.append(note)
                voice.beats.append(beat)


def bend(semitones):
    """A bend up by 1 or 2 semitones, held (bend values are quarter tones; the raw value 100 is a whole tone)."""
    q = semitones * 2
    return gp.BendEffect(type=gp.BendType.bend, value=q * 25,
                         points=[gp.BendPoint(0, 0), gp.BendPoint(4, q), gp.BendPoint(12, q)])


def N(length, *notes, **fx):
    """An event: notes are (string, fret) pairs."""
    return (length, list(notes), fx)


def R(length):
    return (length, [], {})


def melody(spec, center, tuning=STANDARD, **common):
    """'B4:2 A4:2 -:4 G4:4~' -> events; '~' vibrato, '^' bend a whole tone, '/' slide in, 'h' hammer-on."""
    events = []
    for token in spec.split():
        name, length = token.split(':')
        fx = dict(common)
        while length and not length[-1].isdigit():
            fx.update({'~': {'vib': True}, '^': {'bend': 2}, '/': {'slidein': True}, 'h': {'h': True}}[length[-1]])
            length = length[:-1]
        if name == '-':
            events.append(R(int(length)))
        else:
            events.append(N(int(length), place(midi(name), tuning, center), **fx))
    return events


def bars_of(events):
    """Cuts a run of events into 16-sixteenth bars (an event never crosses a bar line)."""
    bars, bar, total = [], [], 0
    for e in events:
        bar.append(e)
        total += e[0]
        assert total <= 16, events
        if total == 16:
            bars.append(bar)
            bar, total = [], 0
    assert not bar
    return bars


def rest_bars(n):
    return [[R(16)] for _ in range(n)]


# ---- drums ----

def drum_bar(hits, vel=gp.Velocities.forte):
    """hits: {slot: [instruments]} on sixteenth slots 0-15 -> events."""
    slots = sorted(hits)
    events = []
    if not slots or slots[0] > 0:
        events.append(R(slots[0] if slots else 16))
    for i, slot in enumerate(slots):
        end = slots[i + 1] if i + 1 < len(slots) else 16
        notes = sorted({(DRUM_STRING[d], d) for d in hits[slot]})
        accent = any(d in (CRASH, SNARE) for d in hits[slot])
        events.append(N(end - slot, *notes, vel=min(127, vel + 16) if accent else vel, chord=False))
    return events


def groove(kick, snare, cymbal, cymbal_slots, crash=False, fill=None):
    hits = {}
    for s in cymbal_slots:
        hits.setdefault(s, []).append(cymbal)
    for s in kick:
        hits.setdefault(s, []).append(KICK)
    for s in snare:
        hits.setdefault(s, []).append(SNARE)
    if crash:
        hits[0] = [d for d in hits.get(0, []) if d not in (HAT, OPEN_HAT, RIDE)] + [CRASH]
    if fill:
        hits = {s: d for s, d in hits.items() if s < 8}
        hits.update(fill)
    return hits


FILL_8THS = {8: [SNARE], 10: [SNARE], 12: [TOM_HI], 13: [TOM_HI], 14: [TOM_FLOOR], 15: [TOM_FLOOR]}
FILL_16THS = {8: [SNARE], 9: [SNARE], 10: [SNARE], 11: [SNARE], 12: [TOM_HI], 13: [TOM_MID], 14: [TOM_FLOOR],
              15: [TOM_FLOOR]}


def drum_part(song, style):
    """style(section, index, length, chord) -> hits"""
    return [drum_bar(*style(sec, i, n, ch)) for sec, i, ch, n in song.bars]


# ---------------------------------------------------------------- 1. Highway Boogie (blues rock, A)

def highway_boogie():
    twelve = ['A5', 'A5', 'A5', 'A5', 'D5', 'D5', 'A5', 'A5', 'E5', 'D5', 'A5', 'E5']
    song = Song('Highway Boogie', 140, gp.KeySignature.AMajor, [
        ('Intro', ['E5', 'D5', 'A5', 'E5']),
        ('Verse 1', twelve),
        ('Verse 2', twelve),
        ('Solo', twelve),
        ('Outro', twelve),
        ('Ending', ['A7']),
    ])
    # Rhythm: the boogie shuffle riff, straight eighths, palm muted, all downstrokes.
    root = {'A5': (5, 4), 'D5': (4, 3), 'E5': (6, 5)}   # open root string, string the riff climbs on
    rhythm = []
    for sec, i, chord, n in song.bars:
        if chord == 'A7':
            rhythm.append([N(16, *shape_notes('A7'), pick='D', ring=True, acc=True)])
            continue
        low, up = root[chord]
        vel = CHORUS_VEL if sec in ('Solo', 'Outro') else VERSE_VEL
        rhythm.append([N(2, (low, 0), (up, f), pick='D', pm=True, vel=vel)
                       for f in (2, 2, 4, 4, 5, 5, 4, 4)])
    song.add('Rhythm Guitar', 29, STANDARD, rhythm)

    # Lead: A minor pentatonic in 5th position (string, fret).
    lick1 = [[R(2), N(2, (1, 5)), N(2, (2, 8)), N(2, (2, 5)), N(4, (3, 7), bend=2), N(4, (2, 5), vib=True)],
             [N(2, (3, 7)), N(2, (3, 5)), N(4, (4, 7), vib=True), R(8)]]
    lick2 = [[N(4, (2, 8), bend=2), N(2, (1, 5)), N(2, (2, 8)), N(2, (2, 5)), N(2, (3, 7)), N(4, (3, 5))],
             [N(2, (4, 7)), N(2, (4, 5)), N(2, (5, 7)), N(2, (5, 5)), N(8, (6, 5), vib=True)]]
    lick3 = [[N(1, (1, 8)), N(1, (1, 5)), N(2, (2, 8))] * 3 + [N(4, (2, 8), bend=2, vib=True)],
             [N(1, (1, 8)), N(1, (1, 5)), N(2, (2, 8))] * 2 + [N(8, (1, 5), vib=True)]]
    turnaround = [[N(2, (1, 5)), N(2, (2, 8)), N(2, (2, 5)), N(2, (3, 7)), N(2, (3, 5)), N(2, (4, 7)),
                   N(4, (4, 5))],
                  [N(2, (5, 7)), N(2, (5, 6)), N(2, (5, 5)), N(2, (6, 8)), N(8, (6, 7), vib=True)]]
    verse = rest_bars(2) + lick1 + rest_bars(2) + lick2 + rest_bars(2) + turnaround
    solo = lick3 + lick1 + lick2 + lick3 + lick2 + turnaround
    outro = lick3 + lick1 + rest_bars(2) + lick2 + rest_bars(2) + turnaround
    ending = [[N(16, (1, 5), vib=True)]]
    lead = rest_bars(2) + turnaround + verse + verse + solo + outro + ending
    song.add('Lead Guitar', 30, STANDARD, lead)

    # Bass: the walking boogie, eighths; a bar that follows the same chord walks back down.
    walk = {'A5': ((3, 0), (3, 4), (2, 2), (2, 4), (2, 5)),
            'D5': ((2, 0), (2, 4), (1, 2), (1, 4), (1, 5)),
            'E5': ((4, 0), (4, 4), (3, 2), (3, 4), (3, 5))}
    bass, prev = [], None
    for sec, i, chord, n in song.bars:
        if chord == 'A7':
            bass.append([N(16, (3, 0))])
            continue
        r, third, fifth, sixth, seventh = walk[chord]
        notes = [seventh, sixth, fifth, third] if chord == prev and i % 2 == 1 else [r, third, fifth, sixth]
        bass.append([N(2, x) for x in notes for _ in (0, 1)])
        prev = chord
    song.add('Bass', 33, BASS4, bass)

    def style(sec, i, n, chord):
        if chord == 'A7':
            return {0: [CRASH, KICK]}, CHORUS_VEL
        ride = sec in ('Solo', 'Outro')
        hits = groove([0, 6, 8], [4, 12], RIDE if ride else HAT, range(0, 16, 2), crash=i == 0,
                      fill=FILL_8THS if i == n - 1 else None)
        return hits, CHORUS_VEL if ride else VERSE_VEL
    song.add('Drums', 0, [0] * 6, drum_part(song, style), percussion=True)
    return song


# ---------------------------------------------------------------- 2. Paper Sky (pop, G)

def paper_sky():
    verse = ['G', 'D', 'Em', 'C'] * 2
    chorus = ['C', 'G', 'D', 'Em', 'C', 'G', 'D', 'D']
    song = Song('Paper Sky', 100, gp.KeySignature.GMajor, [
        ('Intro', ['G', 'D', 'Em', 'C']),
        ('Verse 1', verse),
        ('Chorus 1', chorus),
        ('Verse 2', verse),
        ('Chorus 2', chorus),
        ('Outro', ['G', 'D', 'Em', 'C']),
        ('Ending', ['G']),
    ])
    # Rhythm: acoustic strumming. Verse D - D U - U D U, chorus D - D U D U D U.
    verse_strum = [(4, 'D'), (2, 'D'), (4, 'U'), (2, 'U'), (2, 'D'), (2, 'U')]
    chorus_strum = [(4, 'D'), (2, 'D'), (2, 'U'), (2, 'D'), (2, 'U'), (2, 'D'), (2, 'U')]
    rhythm = []
    for sec, i, chord, n in song.bars:
        if sec == 'Ending':
            rhythm.append([N(16, *shape_notes(chord), pick='D', ring=True, acc=True)])
            continue
        loud = sec.startswith('Chorus')
        bar = []
        for k, (length, d) in enumerate(chorus_strum if loud else verse_strum):
            # Up strokes are written as the whole shape too: an up stroke on the top strings only has a different
            # lowest note (D under G), which reads as a chord change.
            bar.append(N(length, *shape_notes(chord), pick=d, acc=k == 0, vel=CHORUS_VEL if loud else VERSE_VEL))
        rhythm.append(bar)
    song.add('Acoustic Guitar', 25, STANDARD, rhythm)

    intro = 'D5:4 B4:4 G4:4 B4:4 A4:8 F#4:8 G4:4 B4:4 E5:4 D5:4 C5:8~ -:8'
    verse_m = ('-:4 B4:2 A4:2 G4:4 A4:2 B4:2  A4:6 F#4:2 D4:8~  -:4 G4:2 A4:2 B4:4 D5:2 B4:2  C5:4 B4:4 G4:8~ '
               '-:4 B4:2 A4:2 G4:4 D5:4  D5:4 E5:2 D5:2 A4:8~  -:2 G4:2 B4:2 E5:2 D5:4 B4:4  C5:6 B4:2 A4:4 -:4')
    chorus_m = ('E5:4 E5:2 D5:2 E5:4 G5:4  D5:8~ B4:4 G4:4  A4:4 D5:4 E5:4 F#5:4  G5:8~ E5:4 B4:4 '
                'E5:4 E5:2 D5:2 E5:4 G5:4  D5:4 B4:2 A4:2 G4:8~  A4:4 B4:4 D5:4 A4:4  A4:12~ -:4')
    lead_spec = ' '.join([intro, verse_m, chorus_m, verse_m, chorus_m, intro, 'G4:16~'])
    song.add('Lead Guitar', 27, STANDARD, bars_of(melody(lead_spec, center=8)))

    roots = {'G': 31, 'D': 38, 'Em': 28, 'C': 36}
    bass = []
    for sec, i, chord, n in song.bars:
        r = place(roots[chord], BASS4, 3)
        if sec == 'Ending':
            bass.append([N(16, r)])
        elif sec.startswith('Chorus'):
            bass.append([N(2, r)] * 8)
        else:
            fifth = place(roots[chord] + 7, BASS4, 3)
            bass.append([N(6, r), N(2, r), N(4, fifth), N(4, r)])
    song.add('Bass', 33, BASS4, bass)

    def style(sec, i, n, chord):
        if sec == 'Ending':
            return {0: [CRASH, KICK]}, CHORUS_VEL
        if sec == 'Intro':
            return groove([0, 10], [], HAT, range(0, 16, 2), fill=FILL_8THS if i == n - 1 else None), VERSE_VEL
        if sec.startswith('Chorus'):
            return groove([0, 6, 8], [4, 12], OPEN_HAT if i % 2 else HAT, range(0, 16, 2), crash=i == 0,
                          fill=FILL_16THS if i == n - 1 else None), CHORUS_VEL
        return groove([0, 10], [4, 12], HAT, range(0, 16, 2), crash=i == 0,
                      fill=FILL_8THS if i == n - 1 else None), VERSE_VEL
    song.add('Drums', 0, [0] * 6, drum_part(song, style), percussion=True)
    return song


# ---------------------------------------------------------------- 3. Basement Show (punk, E)

def basement_show():
    verse = ['E5', 'C5', 'G5', 'D5'] * 2
    chorus = ['C5', 'G5', 'D5', 'E5'] * 2
    bridge = ['A5', 'A5', 'C5', 'D5'] * 2
    song = Song('Basement Show', 168, gp.KeySignature.EMinor, [
        ('Intro', chorus[:4]),
        ('Verse 1', verse),
        ('Chorus 1', chorus),
        ('Verse 2', verse),
        ('Chorus 2', chorus),
        ('Bridge', bridge),
        ('Chorus 3', chorus),
        ('Ending', ['E5']),
    ])
    rhythm = []
    for sec, i, chord, n in song.bars:
        notes = power_notes(chord)
        if sec == 'Ending':
            rhythm.append([N(16, *notes, pick='D', ring=True, acc=True)])
        elif sec.startswith('Verse'):
            rhythm.append([N(2, *notes[:2], pick='D', pm=True, acc=k == 0, vel=VERSE_VEL) for k in range(8)])
        elif sec == 'Bridge':
            rhythm.append([N(8, *notes, pick='D', ring=True, acc=True), N(6, *notes, pick='D'),
                           N(2, *notes, pick='D')])
        else:
            rhythm.append([N(2, *notes, pick='D', acc=k in (0, 3, 6), vel=CHORUS_VEL) for k in range(8)])
    song.add('Rhythm Guitar', 30, STANDARD, rhythm)

    # Lead: an octave melody (the note and the one an octave up, two strings higher).
    def octave(name, length, **fx):
        p = midi(name)
        options = []
        for low, gap in ((6, 2), (5, 2), (4, 3)):
            fret = p - STANDARD[low - 1]
            if 0 <= fret <= 15:
                options.append((abs(fret - 6), low, fret, gap))
        _, low, fret, gap = min(options)
        return N(length, (low, fret), (low - 2, fret + gap), **fx)
    hook = [[octave('E3', 6), octave('D3', 2), octave('E3', 4), octave('G3', 4)],
            [octave('D3', 8, vib=True), octave('B2', 8)],
            [octave('A2', 4), octave('D3', 4), octave('E3', 4), octave('F#3', 4)],
            [octave('G3', 12, vib=True), R(4)]]
    hook2 = hook[:2] + [[octave('A2', 4), octave('B2', 4), octave('D3', 4), octave('F#3', 4)],
                        [octave('E3', 12, vib=True), R(4)]]
    chorus_lead = hook + hook2
    bridge_lead = [[octave('A2', 16)], [octave('B2', 16)], [octave('C3', 16)], [octave('D3', 16, vib=True)]] * 2
    lead = hook + rest_bars(8) + chorus_lead + rest_bars(8) + chorus_lead + bridge_lead + chorus_lead \
        + [[octave('E3', 16, vib=True)]]
    song.add('Lead Guitar', 29, STANDARD, lead)

    roots = {'E5': 28, 'C5': 36, 'G5': 31, 'D5': 38, 'A5': 33, 'B5': 35}
    bass = []
    for sec, i, chord, n in song.bars:
        r = place(roots[chord], BASS4, 3)
        bass.append([N(16, r)] if sec == 'Ending' else [N(2, r)] * 8)
    song.add('Bass', 34, BASS4, bass)

    def style(sec, i, n, chord):
        if sec == 'Ending':
            return {0: [CRASH, KICK]}, CHORUS_VEL
        last = i == n - 1
        if sec.startswith('Verse'):
            return groove([0, 6, 8], [4, 12], HAT, range(0, 16, 2), crash=i == 0,
                          fill=FILL_8THS if last else None), VERSE_VEL
        if sec == 'Bridge':
            return groove([0, 8], [4, 12], TOM_FLOOR, range(0, 16, 2), crash=i == 0,
                          fill=FILL_16THS if last else None), CHORUS_VEL
        # chorus and intro: the punk beat, kick and snare trading eighths under the ride
        return groove([0, 4, 8, 12], [2, 6, 10, 14], RIDE, range(0, 16, 2), crash=i % 4 == 0,
                      fill=FILL_16THS if last else None), CHORUS_VEL
    song.add('Drums', 0, [0] * 6, drum_part(song, style), percussion=True)
    return song


# ---------------------------------------------------------------- 4. Lantern Light (fingerpicked ballad, A minor)

def lantern_light():
    verse = ['Am', 'Fmaj7', 'C', 'G'] * 2
    chorus = ['Fmaj7', 'C', 'G', 'Am', 'Fmaj7', 'C', 'G', 'G']
    song = Song('Lantern Light', 72, gp.KeySignature.AMinor, [
        ('Intro', ['Am', 'Fmaj7', 'C', 'G']),
        ('Verse 1', verse),
        ('Chorus 1', chorus),
        ('Verse 2', verse),
        ('Chorus 2', chorus),
        ('Outro', ['Am', 'Fmaj7', 'C', 'G']),
        ('Ending', ['Am']),
    ])
    bass_string = {'Am': 5, 'Fmaj7': 4, 'C': 5, 'G': 6}
    rhythm = []
    for sec, i, chord, n in song.bars:
        fret = {s: f for s, f in shape_notes(chord)}
        b = bass_string[chord]
        if sec == 'Ending':
            rhythm.append([N(16, *shape_notes(chord), ring=True, pick='D')])
            continue
        loud = sec.startswith('Chorus')
        vel = CHORUS_VEL if loud else VERSE_VEL
        # The thumb's bass note pinched with two fingers on beat 1 (the importer finds the chords in strums, and
        # reads a part of single notes or two-note pinches as a lead line, with no chords of its own).
        alt = 4 if b != 4 else b
        if loud:   # alternating bass on beat 3, the top two strings together
            order = [[b, 2, 1], [3], [2, 1], [3], [alt, 2, 1], [3], [2, 1], [3]]
        else:
            order = [[b, 2, 1], [3], [2], [3], [1], [3], [2], [3]]
        rhythm.append([N(2, *[(s, fret[s]) for s in strings], ring=True, vel=vel) for strings in order])
    song.add('Acoustic Guitar', 25, STANDARD, rhythm)

    verse_m = ('-:4 E4:2 A4:2 C5:4 B4:2 A4:2  A4:8~ G4:4 E4:4  E4:4 G4:4 C5:4 D5:4  D5:8~ B4:8 '
               '-:4 E4:2 A4:2 C5:4 D5:2 E5:2  E5:8~ C5:4 A4:4  G4:4 E4:4 G4:4 C5:4  B4:12~ -:4')
    chorus_m = ('A4:4 C5:4 E5:8~  E5:6 D5:2 C5:8~  B4:4 D5:8 B4:4  C5:4 B4:4 A4:8~ '
                'A4:4 C5:4 F5:8~  E5:6 D5:2 C5:4 E5:4  D5:8~ B4:4 G4:4  G4:8 A4:4 B4:4')
    outro = '-:4 E4:2 A4:2 C5:4 B4:2 A4:2  A4:8~ G4:4 E4:4  E4:4 G4:4 C5:4 D5:4  D5:8~ B4:8'
    lead = rest_bars(4) + bars_of(melody(' '.join([verse_m, chorus_m, verse_m, chorus_m, outro, 'A4:16~']),
                                         center=8))
    song.add('Lead Guitar', 27, STANDARD, lead)

    roots = {'Am': 33, 'Fmaj7': 29, 'C': 36, 'G': 31}
    bass = []
    for sec, i, chord, n in song.bars:
        r = place(roots[chord], BASS4, 3)
        if sec == 'Intro':
            bass.append([R(16)])
        elif sec.startswith('Chorus'):
            bass.append([N(8, r), N(6, place(roots[chord] + 7, BASS4, 3)), N(2, r)])
        else:
            bass.append([N(16, r)])
    song.add('Bass', 33, BASS4, bass)

    def style(sec, i, n, chord):
        if sec == 'Intro':
            return {}, VERSE_VEL
        if sec == 'Ending':
            return {0: [CRASH, KICK]}, VERSE_VEL
        if sec.startswith('Chorus'):
            return groove([0, 6, 8], [4, 12], HAT, range(0, 16, 2), crash=i == 0,
                          fill=FILL_8THS if i == n - 1 else None), CHORUS_VEL
        hits = {s: [HAT] for s in range(0, 16, 4)}
        hits[0].append(KICK)
        hits[8].append(STICK)
        hits.setdefault(10, []).append(KICK)
        return hits, VERSE_VEL
    song.add('Drums', 0, [0] * 6, drum_part(song, style), percussion=True)
    return song


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), 'songs')
    os.makedirs(out, exist_ok=True)
    for make in (highway_boogie, paper_sky, basement_show, lantern_light):
        song = make()
        path = os.path.join(out, f'YourBand Demo - {song.title}.gp5')
        song.write(path)
        bars = len(song.bars)
        print(f'{path}: {bars} bars at {song.tempo} BPM, {bars * 4 * 60 // song.tempo // 60}:'
              f'{bars * 4 * 60 // song.tempo % 60:02d}, tracks: {", ".join(t[0] for t in song.tracks)}')


if __name__ == '__main__':
    main()
