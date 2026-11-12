# Instrument tracks: the band plays the song's real parts

Living document, started 2026-10-29, on the branch `instrument-tracks`. The version before this work is tagged
`stable-2026-10-28`, and a built copy of it runs on its own from `Documents\GuitarTutor-stable` (it shares the
port, the engine settings and the game's preferences with this one, so only one of them runs at a time).

## Goal

Today the band makes up its parts from the chords and the strumming. A Guitar Pro file holds the real parts:
the drums, the piano, the bass line, a fingerpicked guitar, the solo. With this work each band instrument can
play one of the file's tracks instead, with a sound chosen for that song, so a song sounds more like the record.

Songs imported from Ultimate Guitar chords and tabs pages do not change: they have no tracks, and the band
plays them as it does today.

## Decisions

| Topic | Decision |
|---|---|
| Instruments | Six, as today: rhythm guitar, bass, keyboards, drums, lead guitar, instrument 1 (was Lead 2). More later |
| Tracks | A Guitar Pro import keeps every track: notes with pitch, string and fret, ties, dynamics and techniques |
| Connection | Each instrument has a track dropdown in the control panel. The first choice, "Band playstyle", is today's playing (the lead guitar and instrument 1: "None", they have no part of their own). The second, "None" (2026-11-12), takes the instrument out of the band in this song: it plays no part at all, made up or from a track (kept in the song's band file; the lead guitar's and instrument 1's one "None" is that too). The band guitarist is always on: "None" on the rhythm guitar leaves the rhythm to you, in place of the game's old G key and the engine's setting. A new song is connected automatically (best effort); you fine-tune it. A drums, bass or keys track that plays in under a third of the song's bars is not connected automatically (a piano in a few bars): the band's own part plays |
| Duplicate tracks | Tracks with identical notes (Wicked Game's left and right rhythm guitars) are one choice |
| Vocals | A vocal track is a choice like any other, for instance instrument 1 on Numa Player's synth voice |
| Where choices live | In a file of its own next to the song (`<song>.band.json`), so a reimport keeps them |
| Sounds | Per song and band playstyle (Ballad, Rock, Pop), the rhythm guitar, bass, keyboards and drums each keep a plugin and its sound; the lead guitar and instrument 1 one per song. A sound is saved when its plugin window closes |
| Defaults | A song opened the first time gets a copy of the saved playstyle defaults (none saved: the built-in sounds; the keyboards, which have none, Numa Player with the playstyle's sound). The "Band playstyle defaults..." window shows the song's table and saves it as the defaults (revised 2026-10-31: the first try, a Default set and per-style sets the song followed, did not work in practice) |
| Sound files | The saved plugin states (a Kontakt state is about 450 KB) go in a file per song under `%APPDATA%\GuitarTutor\songs\`, not in the song file or the engine settings |
| Mixer | Per instrument: mute and solo toggles, a volume knob and a pan knob, in place of today's faders. Solo silences the other band parts, never your guitar |
| Per song | Volume, pan and mute are saved with the song; solo is not (a practice tool) |
| Pan | Follows the musicians' places on stage until you turn the knob; "Pan from stage" puts them all back |
| Techniques | Bends, slides, hammer-ons, vibrato and palm mutes are imported and stored; they are played only on a plugin that supports them (the paid Shreddage does, the free one does not) |
| TAB panel | A shortcut key (Tab, Shift+Tab back) shows the TAB of any track that has tab data; its title names the track. Fixed for Ultimate Guitar songs |
| Avatars | The musicians keep today's animation; fingering the real frets comes later |
| Regression check | With every instrument on Default, the notes the game sends the engine are the same as before (the sounds may change, so audio is not compared) |

## Control panel

The guitar input row has the tone preset dropdown after "Guitar tone...". A "Band style" line has the playstyle
list (choosing one asks the game to play the song in it) and "Band playstyle defaults...". Then one row per
instrument:

| Instrument | Track | Plugin | | | | | |
|---|---|---|---|---|---|---|---|
| Rhythm guitar | Band playstyle, None, or a track | its plugin | ... (its window) | M | S | volume | pan |

Switching to a song or playstyle with other plugins or sounds loads them; the game shows its loading ring meanwhile.
The "Band playstyle defaults..." window: a row per band member (rhythm guitar, bass, keyboards, drums), a column per
playstyle, each cell a plugin dropdown and its "..."; "Save defaults" and "Ok".

## Steps

Each step leaves "Default" playing exactly as today, and is checked before the next starts.

1. **Play order.** (Done 2026-10-29.) The importer reads bars from first to last. A Guitar Pro file can repeat bars, take
   alternate endings and jump (Wicked Game has a D.S. al Coda): the song is unrolled into the order it is
   played, the way TuxGuitar plays it. Every track depends on this.
2. **All tracks imported.** (Done 2026-10-29: `tracks` in the song file.) The song model gets the tracks (notes with pitch, string, fret, velocity and
   technique; tuning, capo, program, kind); identical tracks are merged. Nothing plays them yet.
3. **TAB panel shortcut.** (Done 2026-10-29; narrowed 2026-11-12.) The first visible result: Tab cycles the TAB panel through the tracks with tab data, since 2026-11-12 only through the song's own tab and the tracks the band's instruments play (a track chosen or connected for one; the tracks overlay, M, still shows any track's tab when you click it, and Tab then goes on to the next one the band plays).
4. **Connections.** (Done 2026-10-29. `BandConnections` in Core; the lead guitar and instrument 1 already play their
   tracks, as the song's lead parts did before; a lead with no track plays nothing.) The per-song connection file, the automatic connection (from the track's instrument
   number, name, strings, tuning and whether it is drums), the track dropdowns in the control panel, and the
   messages between the game and the engine. The game arranges the band from the connections and sends the
   notes, as today.
5. **Drums, keys and bass from tracks.** (Done 2026-10-29: the Arranger plays their connected tracks; the engine plays a
   drum a kit lacks on the nearest one it has, MONSTER Drums' layout measured.) General MIDI drum notes (with a note map for kits that are not
   General MIDI), the keyboard's notes as written, the bass line.
6. **Guitars from tracks.** (Done 2026-10-29: two strings or more at once are strokes on the chart's chord, held as the track strikes it; single
   strings are picked as notes; the rhythm row also offers each guitar track picked, every note as a note, as a
   choice; a song
   strummed from an Ultimate Guitar page keeps its guitarist on that strumming unless you choose the track.) The rhythm and lead guitars play their tracks: strummed where the track strums,
   note by note where it picks, through the modes the engine already has.
   Experiments (2026-10-30, branch `experiment/stroke-chords`): each stroke holds only the strings it strikes; and the
   rhythm row's "string by string" choice, where every note is its string picked on the held chord with Shreddage's
   string keys, so the bass rings under Redbud Tree's pinches (the strummed version cuts it 5-20 dB within 1.5 beats).
   It needs Shreddage set to Strumming Mode off, Keyswitchable on, and voices enough (Rhythm, Polyphonic and the voice
   limits raised to 32/16 did it).
   On 2026-11-01 the three ways (strummed, picked note by note, string by string) became one: every track is played
   string by string, strums as their strings struck one after another, with the techniques of step 9. Strummed with the
   strum keys, a strum holds only the strings it strikes and cuts the others; picked, Shreddage chose the strings and
   choked fast repeats; and a track does not say which of them it needs (Redbud Tree's bass notes are written short).
7. **Sounds per song.** The plugin and sound per instrument, playstyle and song, the Band playstyle defaults window,
   saving when the plugin window closes, loading on song and playstyle change. In detail (done 2026-10-31, after a
   redesign the same day):
   - A *sound* is an instrument's plugin (or its built-in sound) and the plugin's state. The engine keeps a song's
     sounds in `%APPDATA%\GuitarTutor\songs\<song>.sounds`: per playstyle the band members', and the leads' (JSON).
   - A song opened the first time (an import too) gets a copy of `%APPDATA%\GuitarTutor\band-defaults.sounds`
     ("Save defaults"); with none saved, the built-in sounds (the keyboards: Numa Player with each playstyle's sound).
     The leads start on the plugins chosen for them last. (Your defaults were made once from the sounds you had,
     2026-10-31.)
   - The game names the song and lists the playstyles (with their keyboard sounds) in `load`; the engine puts the band
     on the playing playstyle's sounds, loading only what differs, and answers `loaded` once they are in. The game
     shows its loading ring meanwhile.
   - An instrument playing one of the song's tracks (the leads always do) has its own sound for that in the song:
     the control panel row's plugin dropdown and its "..." window, shown only then. It starts as its playstyle part's
     sound (a lead: the plugin chosen for it last) and does not change with the playstyle. On "Band playstyle" the
     row's plugin is hidden: the playstyle's sound plays (revised 2026-10-31). The keyboards have a plugin dropdown and
     window too.
   - "Band playstyle defaults...": the song's table (band members x playstyles), a plugin dropdown and "..." per cell.
     A cell's "..." puts the band instrument on that sound while its window is open; closed, the sound is kept and the
     instrument goes back to what it plays. "Save defaults" saves the table as the defaults; "Load defaults"
     (2026-11-01) gives the song the saved ones, after asking; "Ok" closes.
   - A silent engine (tests) writes none of these files.
8. **Mixer.** Mute, solo, volume and pan knobs, pan from the stage, saved per song. (Done 2026-10-31.) Each
   instrument row ends with M, S, a volume knob and a pan knob, in place of the six faders; volume, pan and mute are
   kept in the song's sounds file, solo is not, and a song not mixed yet starts at the old faders' levels. The game
   sends the musicians' places (`stage`; moved in VR, sent again): an instrument pans from its place as the audience
   hears it until its knob is turned; "Pan from stage" puts them all back. Mute and solo never silence your guitar.
9. **Dynamics and techniques.** (Done 2026-10-31.) Velocities from the file's dynamics; bends, slides and the rest on the
   plugins that support them. Plan (2026-10-31, for approval):
   - **What the free Shreddage can do.** The manual's red text marks what only the full version has; plugin_probe
     measured the rest. The pitch wheel bends a note ±2 semitones (+198 cents full up, -200 full down, linear and at
     once), a plain note and a string key's pick alike. The Mute (palm-muted single notes) and Choke articulations are
     there, but their keyswitches are full-version only. Hammer-ons, pull-offs, slides, harmonics, staccato, tapping
     and tremolo articulations are full-version only.
   - **9.1 Import the shapes.** (Done 2026-10-31: the Guitar Pro songs' tracks imported again and copied into the
     song files, the rest of each song as it was; Redbud Tree's solo has 4 bends, 6 slides and 6 vibratos, Heart of
     Gold 58 slides and 38 vibratos, Wicked Game 41 slides, Everlong 33.) Per note the importer keeps the bend's points (when in the note, how many semitones:
     a bend, a release, a pre-bend, a bend and release play as written), the slide's kind (to the next note, out
     down or up, in from below or above), the vibrato's width (slight or wide), the tremolo picking's speed and a
     harmonic's sounding pitch. The Guitar Pro songs are imported again.
   - **9.2 The pitch wheel.** (Done 2026-10-31, with the leads' bends from the file's points as its first use;
     measured: Shreddage, MONSTER Bass and Numa Player bend 2 semitones each way, Ample Bass P Lite not at all, so the
     engine uses 2 for every plugin. The importer now also keeps a bend through a tied note: the points move as the
     note grows, and a release drawn on the tied note is kept.) The arrangement gives each part that bends a list of wheel points (when, semitones),
     and the load message carries them. The engine sends them on the part's MIDI channel, scaled to its plugin's
     range (±2 for Shreddage; Numa Player, Ample Bass and MONSTER Bass are measured in this step), and puts the
     wheel back in the middle at stop, seek and a new song.
   - **9.3 What each technique becomes.** (Done 2026-10-31. The palm mute's controller is 20: in Shreddage's Map tab,
     Mute with the rule "CC20 64-127" plays real palm mutes; without that rule only the shorter, softer note.) For the lead guitar, instrument 1, the bass, and the rhythm guitar (playing a
     track: since 2026-11-01 its one way, string by string, takes them too; a bent or sliding note lets go of the strings
     ringing as it is struck, and rings for its own length):

     | Technique | Played as |
     |---|---|
     | Bend (b) | The wheel follows the bend's points; past 2 semitones it stops at 2 |
     | Vibrato (~) | The wheel wobbles about 5.5 times a second, ±25 cents (slight) or ±50 (wide), from about 0.15 s into the note |
     | Slide to the next note (s) | The wheel glides towards the next note over the end of this one (2 semitones at most, then the next note takes over); the next note starts softer, as a slide picks it no more |
     | Slide in or out (s) | A 2-semitone glide into the note from below or above, or out of it, over about 0.1 s |
     | Hammer-on, pull-off (h) | The next note starts softer (it is not picked) |
     | Palm mute (m), single notes | Short and softer; or the Mute articulation, if it can be mapped in the free Shreddage (see the open points) |
     | Dead note (x) | A short, very soft note (today it is left out) |
     | Ghost (g), accent (a) | Softer, louder (already, from the importer) |
     | Staccato (.) | Half its length |
     | Let ring (r) | Rings until the next note on its string (the rhythm guitar's every note does) |
     | Harmonic (n) | Its sounding pitch, plainly |
     | Tremolo picking (z) | Repeated notes at the written speed, every other one a little softer |
     | Tapping (t), grace note (q) | Plainly (a grace note is already a short note before the main one) |

     The wheel bends everything on a part's channel. The leads play one note at a time, so a bend is theirs alone;
     the rhythm guitar and the bass bend only where none of their other strings rings.
   - **9.4 Dynamics.** (Done 2026-10-31. Hairpins: only Redbud Tree has them, a diminuendo at 108.5 and a crescendo at
     177.5 on the steel guitar. The plugins, measured with plugin_probe at velocities 47 to 127: MONSTER Drums' kick
     spreads 15 dB from p to f, its snare 23 and its hi-hat 14, Ample Bass 6 and MONSTER Bass 10; none goes faint and
     choked as Shreddage did, only quieter, and Wicked Game's soft drums are written so (its ride mezzo-piano, its soft
     snare hits ghost notes): no mapping.) Velocities from the file's dynamics are in since step 2. This step also reads hairpins
     (crescendo, diminuendo) and spreads the velocity across them, and measures whether the drum and bass plugins
     sound faint at the file's soft velocities, as Shreddage did; if so they get a mapping like the guitar's.
   - **Checked by** unit tests of the wheel points per technique, and a silent recording of a lead with bends,
     vibrato and slides whose pitch is traced over time (as the probe measurement did).
   - **Decided (2026-10-31):** a palm mute tries the Mute articulation mapped to a MIDI controller first: checked
     whether the free edition lets it be mapped in its articulation tab; if not, short and softer. The rhythm guitar
     and the bass leave a bend out where another of their strings still rings. A bend past 2 semitones stops at a whole
     step. Hairpins are part of this step.
