# How the band's parts are made

The **arranger** (`game/Assets/YourBand/Core/Band/Arranger.cs`) turns a song into what each band member plays. It runs
in the game, in plain C# with no Unity references, and is the only place that decides *which note, when, how long, how
hard*. The audio engine plays the result without interpreting it ([architecture.md](architecture.md)), and the 3D band
animates from the same result.

Related: [shreddage-keyswitches.md](shreddage-keyswitches.md) (what the engine does with the guitar's part),
[instrument-tracks.md](instrument-tracks.md) (design notes and measurements).

## 1. Interface

```csharp
Arrangement Arranger.Arrange(SongChart chart, BandStyle style = null, BandConnections connections = null)
```

| Input | Meaning |
|---|---|
| `chart` | The song laid out bar by bar: bars with their signature, section and chords; strumming patterns per section; the song's tracks, riffs and chord voicings |
| `style` | `Ballad`, `Rock` or `Pop`; null: Ballad below 120 BPM at the start of the song, else Rock |
| `connections` | Which track of the song each instrument plays, or "None" ([section 3](#3-which-instrument-plays-what)). Null: the band makes up every part, and the lead channels play the song's own lead parts (used by tests and `songtool`) |

The result, an `Arrangement`, is a set of lists in quarter notes, in time order. The game sends them in the engine's
`load` message ([schema/engine-protocol.md](../schema/engine-protocol.md)).

| List | Item | Used by |
|---|---|---|
| `Notes` | `BandNote(part, pitch, atQuarter, quarters, velocity, single)`; for drums the pitch is a General MIDI drum note | Every part. The guitar's `Notes` are its per-string notes: they are what the built-in guitar and any non-strumming guitar plugin plays |
| `Voicings` | `VoicingNote(pitch, atQuarter, quarters)`: one note per string of the chord held | A strum-capable guitar (Shreddage) |
| `Strokes` | `BandStroke(atQuarter, up, velocity, muted)` | A strum-capable guitar, the made-up strumming |
| `Picks` | `BandPick(atQuarter, quarters, rank, velocity)`: a string of the held chord picked on its own | A strum-capable guitar, a Guitar Pro track |
| `SingleSpans` | (from, to): stretches where the guitarist plays single-string notes of the tab and does not strum | A strum-capable guitar |
| `Wheel` | `WheelPoint(part, atQuarter, semitones)`: the pitch wheel (bends, vibrato, slides) | Plugin parts |
| `Controls` | `ControlPoint(part, atQuarter, controller, value)`: MIDI controllers (the palm mute's CC20) | Plugin parts |
| `LeadNames` | What the two lead channels play (shown in the engine's log) | Display |

`BandPart` is `Drums`, `Bass`, `Keys`, `Guitar` (the rhythm guitar), `Lead1`, `Lead2`. They are numbered 0-5 in the
`load` message.

Two properties hold for every arrangement:

- **Deterministic.** The small velocity variations come from a fixed seed (`new Random(1)`, plus or minus 5), so the same
  song, style and connections always give the same notes.
- **Capo.** Chord names with a capo are the shapes played above it: the made-up bass, keys and guitar are played that many
  semitones higher. A Guitar Pro track's pitches already include its capo.

## 2. The made-up parts

When an instrument is not connected to a track, the arranger writes its part from the chart and the style. It makes up
nothing for an instrument that is "None" or, for the two leads, has no track.

### 2.1 Styles

All times are quarter-note offsets in a 4/4 bar. Velocities are MIDI 1-127.

| | Ballad | Rock | Pop |
|---|---|---|---|
| Hi-hat | Eighths, 72 on the beat, 50 off | Eighths, 96 / 72, the last eighth open | Eighths, 84 / 60, the last eighth open |
| Kick | 0 (100), 1.5 (70), 2 (88) | 0 (110), 2 (104), 2.5 (90) | 0 (104), 1.5 (84), 2 (96) |
| Snare | 1 (90), 3 (92); a side stick in quiet sections | 1 (110), 3 (112) | 1 (100), 3 (102) |
| Bass | Root 0 (1.5 long), root 1.5, fifth 2, approach 3 | Eighths: the root, the last one an approach | Root 0, octave 1.5, root 2, approach 3.5 |
| Keys rhythm | Chord at 0 and 2, two quarters each | At 0 (1.5) and 1.5 (2.5) | At 0, 1.5 (1.5 each), 3 (1) |
| Keys velocity (6 more on a bar's first hit) | 60 | 78 | 70 |
| Keys sound (Numa Player) | `2 - E_PIANO/1 - EP-Mark I` | `5 - ORGANS/4 - ROCK B3` | `1 - A_PIANO/1 - Model D 1983` |
| Keys level in the mix | 1.3 | 0.45 (an organ sustains) | 2.0 |

"Quiet sections" are those whose name starts with intro, verse, outro or bridge.

### 2.2 Drums

- Hits per the style, in 4/4. Other signatures use one generic bar: hi-hat on every half beat (72 on the beat, 50 off),
  kick on 1, snare on the last beat.
- A **crash** (2 quarters long, velocity 96 at the song's first bar, else 108) where a section starts and in the last bar;
  the last bar adds a kick and nothing else.
- A **fill** in the last beat of a bar that is followed by a new section and is at least two quarters long: sixteenths on
  snare, snare, high tom, low tom with velocities 76, 86, 96, 106; the kick stays.
- A hi-hat on a section's first beat is dropped for the crash.

### 2.3 Bass

On each chord's bass hits of the style, plus a hit where a chord changes between two hits; every hit is cut where the next
starts or the chord ends.

| Tone | Pitch |
|---|---|
| Root | The chord's bass note (its slash bass if it has one) in the octave from E1 (28) to E2 (40) |
| Fifth | The fifth above (or the flat or sharp fifth if the chord has one), else the root |
| Octave | The root plus 12 |
| Approach | A half step towards the next chord's root, from the nearer side, when the next chord starts at the end of this bar; else the root |

A hit at a chord change plays the new chord's root. Velocity 100 on the bar's first hit, else 86. The last bar is one
root held for the bar.

### 2.4 Keys

The chord voiced in three or four notes between G3 (55) and F5 (77): the voicing (a choice of bottom note and octave) that
moves least from the previous one; for the first chord, the one nearest E4. A chord of more than four distinct pitch
classes drops its fifth.

### 2.5 Rhythm guitar: strumming

`Arranger.Strum`. For each bar it takes the section's strumming pattern (`chart.PatternsFor`; a downstroke on every beat
if the song has none), and for each stroke that is not a rest:

1. The chord sounding then gives the **shape**: the song's own fingering (`Song.VoicingFor`) if it has one, else
   `GuitarShape` (the E or A barre shape, open where it can be; a power chord without a third).
2. The stroke crosses the strings as a hand does: **down** low to high over all played strings, **up** high to low over
   the top four strings (string index 2 and above, when at least two of them are played). A string takes 12 ms after the one
   before on a downstroke and 9 ms on an upstroke.
3. **Velocity** (the strum's speed and, less, its loudness on Shreddage: [shreddage-keyswitches.md](shreddage-keyswitches.md#55-what-a-strokes-velocity-does); before the 5-step humanising): muted 64; accented down 116, accented up 104; up 72; a down on the beat 96,
   off the beat 84.
4. It adds a `BandStroke` (for a strum-capable guitar), and one `BandNote` per string struck with its fret on the open string
   (for the built-in guitar and every other plugin).

Each string rings until it is struck again or until 70 ms before its chord ends (the hand's time to change chord), less a
hair (0.01 quarter); a muted stroke rings at most 0.08 quarters.

`Voicings` get one note per played string of every chord (the held shape). The last bar is one held downstroke.

**A song's tab, in a song without tracks** (one from Ultimate Guitar pages; `RiffTrack`): each riff's stretch is played as a
track is (`TrackRhythm`), its notes picked and ringing; the strumming stops there and goes on after it.

**Single-string notes from the song's tab, in a Guitar Pro song on its playstyle part** (`SingleNotes`): where the tab has onsets of one note, the guitarist plays them
as notes and does not strum over them. A *run* starts at the first single-note onset and ends at the next onset of two or
more strings (a chord, where strumming resumes), after a gap of more than a beat, or at the run's end; its notes are
flagged `single`, and its time range goes into `SingleSpans`.

## 3. Which instrument plays what

Six instruments: rhythm guitar, bass, keyboards, drums, lead guitar, instrument 1. Each plays one of:

| Choice | Result |
|---|---|
| **Band playstyle** | The part made up in sections 2.2-2.5. The lead guitar and instrument 1 have none: their default is silence |
| **A track** | The notes of one track of the song ([section 4](#4-playing-a-track)) |
| **None** | Nothing at all, not even a made-up part |

`BandConnections` (`Core/Band/BandConnections.cs`) holds the choices. What you choose is kept in
`<song>.band.json` next to the song, so a new import keeps it; an instrument you have not chosen for is **connected
automatically** each time the song opens (`AutoConnect`):

1. **Rhythm guitar**: the track the song's own tab comes from (the strummed guitar the chords were derived from), else the
   busiest guitar track. If the song's strumming comes from an Ultimate Guitar page (`ultimate-guitar-strumming`), the
   guitarist keeps playing that strumming and no track is connected.
2. **Drums, bass, keys**: the busiest track of that kind that plays in at least a third of the song's bars (a piano in a
   few bars does not replace the made-up keys).
3. **Lead guitar**: the busiest remaining guitar track.
4. **Instrument 1**: a track of kind "other" (harmonica, strings), else a vocal line.
5. A track goes to one instrument only. Tracks that double another (`SameAs`) are the same choice and play its notes. A
   song with no tracks (from Ultimate Guitar pages) connects nothing.

The band guitarist is always on: "None" on the rhythm guitar leaves the rhythm to you.

## 4. Playing a track

A track plays *as the file has it*: its notes, velocities and techniques, with no made-up fills.

| Instrument | How |
|---|---|
| Drums, keys | Every note as written (`Pitch` is the General MIDI drum note for drums), length at least 0.05 quarters. Dead notes are left out. Velocity as written. Drums take no techniques |
| Bass | `PlayTrack`: each note with its techniques ([section 5](#5-techniques)), then its pitch wheel. The bass bends only where none of its other notes sounds |
| Lead guitar, instrument 1 | `PlayTrack` with `lead = true`: a lead plays one note at a time, so its wheel is never held back |
| Rhythm guitar | `TrackRhythm`: every note is its string struck on the chord held ([section 4.1](#41-the-rhythm-guitar-from-a-track)) |

### 4.1 The rhythm guitar from a track

The chart's chord is held as the track strikes it, and every note is **a string of that chord picked on its own**, strummed
and fingerpicked parts alike. (Until 2026-11-01 there were three ways: strummed with strum keyswitches, picked plain
notes, and this; strum keyswitches hold only the strings a strum strikes, so the next pinch cut the bass notes ringing
under it in Redbud Tree.) The algorithm, in `TrackRhythm`:

1. **Onsets.** Notes less than 0.02 quarters apart are one onset (struck together).
2. **Held chords.** The stretches a chord is held for are the chart's chord spans. In each, the pitch a string is struck at
   most often is that string's held pitch. A chord that continues the one before without changing any string's pitch is that
   chord held on (the strings ring through the change). A note off its string's held pitch starts a new held chord from there.
3. The held chords, as the tab frets them, are the arrangement's **`Holds`**: the guitarist's hands hold these shapes (a six-string
   track that is tab), not the chart's chord boxes, which can be the file's diagram of the chord rather than the shape played.
   **`Voicings`** get the notes of every held chord **as Shreddage can voice it** (`GuitarVoicing`: it fingers the held notes on a
   guitar in standard tuning, and a chord it cannot finger, such as two notes only the low E string reaches, is silent: a song in
   another tuning has many, and so does a chord past the 12th fret, the highest Shreddage plays;
   [shreddage-keyswitches.md](shreddage-keyswitches.md#53-which-chords-shreddage-can-voice)). A chord that cannot be fingered has as
   few notes moved an octave as makes it so, an inner note before the top or the bass.
4. **Strikes.** Each onset becomes one strike per note, in the order a hand crosses the strings: down low to high, up high
   to low (the file's stroke `D` or `U` if it records one, a pick stroke or a brush, else down on the beat and up off it, as `StrumOf` works it out); the strokes' spread is 12 ms per string down and 9 ms up. An
   onset of two or more notes is a **strum**: its direction and velocity come from `StrumOf` (the file's stroke, else
   down on the beat and up off it, with the importer's own slot rule; as hard as the made-up strumming, shifted by the file's
   dynamics). A single note's velocity is `64 + (fileVelocity - 15) / 2`, the range a plugin guitar plays in (a plugin
   guitar is faint and choked at the file's soft velocities).
5. **Rank.** A strike's *rank* is the place of its pitch among the distinct pitches of the held chord as voiced, lowest first (a
   note left out has none and is not picked; strings moved onto one pitch share a key). That
   is the number of the string key that will pick it ([shreddage-keyswitches.md](shreddage-keyswitches.md#6-picking-a-tracks-notes)).
6. **Length.** A strike rings until the same string is struck again (less 0.01 s, `RestrikeGap`), or until 70 ms before its
   held chord ends, or 4 quarters, whichever is first. Muted strikes ring 0.08 quarters at most.
7. **Techniques** shape the length and velocity, add wheel points and controllers ([section 5](#5-techniques)). A bent or
   sliding note rings only for its own length, and the strings ringing when it is struck are let go then, so the wheel bends
   it alone.
8. **Output.** One `BandPick` per strike with a rank under 6 (a chord of more pitches than the six string keys leaves the
   higher ones to guitars that play notes), and one `BandNote` for every strike, for the built-in guitar and every plugin that
   plays notes.

A strum of dead or palm-muted strings only is a short chuck on the strings of the chord held then. A dead note on its
own is a click for the guitars that play notes, and left out on Shreddage.

## 5. Techniques

`Play` (notes), `Wheel` (pitch wheel) and `TrackRhythm` (the rhythm guitar) give each technique its meaning. This is the
game's side; what the engine and Shreddage do with the results is in
[shreddage-keyswitches.md](shreddage-keyswitches.md#8-techniques).

| Technique | Note | Wheel and controllers |
|---|---|---|
| Hammer-on, pull-off (`h`), legato slide into the note | The *next* note's velocity is 25 lower (it is not picked) | |
| Dead (`x`) | At most 0.06 s long, velocity at most 30 (a click) | |
| Palm mute (`m`) | At most half its length and 0.25 s; velocity 12 lower | CC20 = 127 as it starts, 0 as it ends |
| Staccato (`.`) | Half its length | |
| Tremolo picking (`z`) | Repeated notes at the written speed, each 0.9 of the interval, every other one 10 softer | |
| Bend (`b`) | | The wheel follows the bend's points; a bend past 2 semitones stops at 2 |
| Vibrato (`~`) | | The wheel wobbles 5.5 times a second, 0.25 semitone each way (slight) or 0.5 (wide), from 0.15 s into the note, fading in over 0.1 s |
| Slide to the next note (`s`, shift or legato) | | The wheel glides towards the next note over the note's last 0.1 s (half the note if it is shorter), at most 2 semitones; then the next note takes over |
| Slide out down or up | | A glide of -2 or +2 semitones over the note's last 0.1 s |
| Slide in from below or above | | A glide from -2 or +2 semitones into the note over its first 0.1 s |
| Ghost (`g`), accent (`a`) | Softer, louder: already in the velocity the importer gives | |
| Let ring (`r`) | The rhythm guitar's every note rings anyway; others are not extended | |
| Harmonic (`n`), tapped (`t`), grace (`q`) | Played plainly (a grace note is already a short note before the main one) | |

The pitch wheel is written as points of (moment, semitones); between two points it moves evenly. For a note with any wheel
effect the arranger writes a point every 15 ms and at each of the bend's own points. If the note starts away from the
middle (a pre-bend, a slide in), the wheel is brought there 0.02 quarters before the note (from the middle 0.001 quarter
earlier, so it jumps), and it returns to the middle at the note's end. A part that is not a lead gets wheel points only for notes under
which nothing else of its sounds, because the wheel bends everything on the part's MIDI channel.

## 6. Where to look

| For | See |
|---|---|
| The arranger | `Core/Band/Arranger.cs`: `Arrange` (the whole), `Strum`, `SingleNotes`, `TrackRhythm`, `StrumOf`, `PlayTrack`, `Play`, `Wheel`, `GuitarShape`, `Voice`, `BassPitch` |
| The choices | `Core/Band/BandConnections.cs` |
| The messages | `Core/Engine/EngineProtocol.cs`, `Load` |
| Checking by eye | `songtool show`, and `songtool band-midi` for the guitarist's MIDI |
| Tests | `Tests/EditMode/BandTests.cs`, `ConnectionTests.cs` |
