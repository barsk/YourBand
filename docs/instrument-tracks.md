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
| Connection | Each instrument has a track dropdown in the control panel. The first choice, "Default (generated)", is today's playing. A new song is connected automatically (best effort); you fine-tune it. A drums, bass or keys track that plays in under a third of the song's bars is not connected automatically (a piano in a few bars): the band's own part plays |
| Duplicate tracks | Tracks with identical notes (Wicked Game's left and right rhythm guitars) are one choice |
| Vocals | A vocal track is a choice like any other, for instance instrument 1 on Numa Player's synth voice |
| Where choices live | In a file of its own next to the song (`<song>.band.json`), so a reimport keeps them |
| Sounds | Per song, each instrument keeps its plugin and that plugin's sound. The sound is saved when the plugin window closes |
| Defaults | A song nobody has changed follows the band style's default sound for each instrument, else the Default set. A "Band defaults..." dialog holds them (instrument x Default, Ballad, Rock, Pop) |
| Sound files | The saved plugin states (a Kontakt state is about 450 KB) go in a file per song under `%APPDATA%\GuitarTutor\songs\`, not in the song file or the engine settings |
| Mixer | Per instrument: mute and solo toggles, a volume knob and a pan knob, in place of today's faders. Solo silences the other band parts, never your guitar |
| Per song | Volume, pan and mute are saved with the song; solo is not (a practice tool) |
| Pan | Follows the musicians' places on stage until you turn the knob; "Pan from stage" puts them all back |
| Techniques | Bends, slides, hammer-ons, vibrato and palm mutes are imported and stored; they are played only on a plugin that supports them (the paid Shreddage does, the free one does not) |
| TAB panel | A shortcut key (Tab, Shift+Tab back) shows the TAB of any track that has tab data; its title names the track. Fixed for Ultimate Guitar songs |
| Avatars | The musicians keep today's animation; fingering the real frets comes later |
| Regression check | With every instrument on Default, the notes the game sends the engine are the same as before (the sounds may change, so audio is not compared) |

## Control panel

Below the song and the band style (with its "Band defaults..." button), one row per instrument:

| Instrument | Track | Plugin | | | | | | |
|---|---|---|---|---|---|---|---|---|
| Rhythm guitar | Default (generated), or a track | its plugin | plugin window | ... menu | M | S | volume | pan |

The ... menu: reset to the style's sound, save as the style's default, save as Default. Switching to a song
with other plugins or sounds loads them; the game shows its loading ring meanwhile. The band guitar tone and
"plays rhythm" controls move onto the rhythm guitar row.

## Steps

Each step leaves "Default" playing exactly as today, and is checked before the next starts.

1. **Play order.** (Done 2026-10-29.) The importer reads bars from first to last. A Guitar Pro file can repeat bars, take
   alternate endings and jump (Wicked Game has a D.S. al Coda): the song is unrolled into the order it is
   played, the way TuxGuitar plays it. Every track depends on this.
2. **All tracks imported.** (Done 2026-10-29: `tracks` in the song file.) The song model gets the tracks (notes with pitch, string, fret, velocity and
   technique; tuning, capo, program, kind); identical tracks are merged. Nothing plays them yet.
3. **TAB panel shortcut.** (Done 2026-10-29.) The first visible result: Tab cycles the TAB panel through the tracks with tab data.
4. **Connections.** (Done 2026-10-29. `BandConnections` in Core; the lead guitar and instrument 1 already play their
   tracks, as the song's lead parts did before; a lead with no track plays nothing.) The per-song connection file, the automatic connection (from the track's instrument
   number, name, strings, tuning and whether it is drums), the track dropdowns in the control panel, and the
   messages between the game and the engine. The game arranges the band from the connections and sends the
   notes, as today.
5. **Drums, keys and bass from tracks.** (Done 2026-10-29: the Arranger plays their connected tracks; the engine plays a
   drum a kit lacks on the nearest one it has, MONSTER Drums' layout measured.) General MIDI drum notes (with a note map for kits that are not
   General MIDI), the keyboard's notes as written, the bass line.
6. **Guitars from tracks.** (Done 2026-10-29: two strings or more at once are strokes on the chart's chord, held as the track strikes it; single
   strings are picked as notes; a fingerpicked track, seldom five strings at once, is played picked, every note as a
   note, and the rhythm row offers each guitar track both ways; a song
   strummed from an Ultimate Guitar page keeps its guitarist on that strumming unless you choose the track.) The rhythm and lead guitars play their tracks: strummed where the track strums,
   note by note where it picks, through the modes the engine already has.
7. **Sounds per song.** The plugin and sound per instrument and song, the Band defaults dialog, saving when
   the plugin window closes, loading on song change.
8. **Mixer.** Mute, solo, volume and pan knobs, pan from the stage, saved per song.
9. **Dynamics and techniques.** Velocities from the file's dynamics; bends, slides and the rest on the
   plugins that support them.
