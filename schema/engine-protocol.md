# Game ↔ audio engine protocol (version 1)

The game (`game/`) and the audio engine (`engine/`) run as two programs on the same PC
(see [../docs/plan.md](../docs/plan.md), "Architecture"). They talk in two ways:

- **Messages**: TCP on `127.0.0.1:47800`. The engine listens and the game connects. Each message is one JSON
  object on one line (UTF-8, `\n` at the end). One game at a time.
- **Clock**: shared memory named `Local\YourBandClock`. The engine writes it after every audio block;
  the game reads it every frame. Messages are too late and too jittery for drawing in time with the music.

Positions are in quarter notes from the start of the song, as in the song file. The engine owns the transport
(it is the music); the game only asks.

Implemented by hand on both sides for now: `game/Assets/YourBand/Core/Engine/EngineProtocol.cs` and
`engine/src/GameLink.cpp`, `engine/src/Band.cpp`. The plan is to generate both from one definition here once
the message set settles.

## Game → engine

| `type` | Fields | Meaning |
|---|---|---|
| `hello` | `client`, `protocol` | First message after connecting |
| `load` | `title`, `style`, `tempos` [[atQuarter, bpm]...], `meters` [[atBar, numerator, denominator]...], `notes` [[part, pitch, at, length, velocity]...], `voicings` [[pitch, at, length]...], `strokes` [[at, direction, velocity, muted]...], `picks` [[at, length, rank, velocity]...], `wheel` [[part, at, semitones]...], `controls` [[part, at, controller, value]...], `keys`, `song`, `styles` [{`name`, `keys`}...] | The song's tempo map and the band's parts (replaces what was loaded; stops playing). `part`: 0 drums (General MIDI drum notes; a drum the kit in use lacks is played on the nearest one it has, or left out), 1 bass, 2 keys, 3 guitar (the band guitarist's rhythm: always played; the game sends none of it where the rhythm guitar is out of the band). The guitar's `notes` are its per-string strikes (12 ms apart down, 9 ms up, each string ringing until it is struck again or the chord changes) for the built-in guitar and the non-strum plugins. `voicings`: the guitarist's chords as held voicings, one note per string, from the chord's start to its end. `strokes`: its strumming, in quarter notes (the part it makes up; a track it plays sends `picks`: each note a string key, F#6 plus its `rank` in the held chord, held `length` quarters as its string rings); `direction` 0 down, 1 up; `muted` 0/1. On a strum-capable guitar (Shreddage's strumming mode) the engine plays the held voicings and the strokes as strum keyswitches (down G#5, up A5; each key is held until the next stroke or 1.2 s at most, as the plugin rings the chord while the key is down and damps it on release, and a muted stroke adds the Mute keyswitch C6 around the strum for a fast chug; the strumming-mode keyswitch F#5 held for the duration) and ignores the per-string notes. `keys`: the keyboard sound, a Numa Player sound name such as `2 - E_PIANO/1 - EP-Mark I` (empty: the engine's default). `wheel`: the parts' pitch wheels (bends), points in semitones, the wheel moving evenly between a part's points; the engine plays them on a part's plugin (2 semitones each way at most) and puts the wheels back in the middle at stop, seek and a new song. `controls`: MIDI controllers for a part's plugin at moments (a palm mute's, controller 20: 127 as the note starts, 0 as it ends), set back to 0 at stop and seek. `song`: the song's file name, `styles`: the band playstyles with each one's keyboard sound; the engine keeps a song's sounds per playstyle (the saved defaults the first time it is opened), and one per instrument for playing a track, and puts the band on them before it answers `loaded` (the game sends `tracks` first: which instruments play a track) |
| `stage` | `pans` {instrument id: -1..1} | Where the band's musicians stand, as each instrument's pan as the audience hears it (-1 all left, 1 all right; the leads have no musician: 0); sent with the song and when a musician moves (VR). An instrument pans from its place on stage until its pan knob is turned |
| `play` | `from`, `countIn` | Play from `from`, after a count-in of `countIn` quarter notes (0: none). The drummer counts in on sticks; band notes inside the count-in are not played |
| `pause` | | Stop; sounding notes are released |
| `seek` | `to` | Move to `to`; keeps playing if playing |
| `speed` | `value` | Practice speed: 1 is the song's tempo |
| `window` | `show` | Brings up a window of the engine: `controlPanel` (devices, faders, log), `guitar` (your guitar's TONE3000), `drums`, `bandBass` or `bandGuitar` (the band's instrument plugins) |
| `quit` | | Close the engine (it saves its settings); the game sends it when it closes an engine it started |
| `mix` | `part`, `gain` | The level of a band part's sound: `drums`, `bass`, `keys` or `guitar`, 1 as played (defaults: drums 0.8, bass 1.75 (a DI sound), keys 1.3, guitar 1.75 (the built-in guitar, a DI sound); the game sets the keys per style). The control panel's faders multiply it and are remembered by the engine |
| `bandBass` | `plugin` | The band's bass on an instrument plugin, named by its file name (`MONSTER Bass v2`, `ABPL`), or `synthesized`; as chosen in the control panel, and kept for next time |

| `tracks` | `tracks` [{`index`, `name`, `kind`}...], `instruments` [{`id`, `name`, `track`, `chosen`}...] | The song's tracks the band's instruments can play (a Guitar Pro song's, one per doubled pair; `name` describes it: "Guitar 2 = Guitar 3 (guitar, 300 notes)"; none for a song from Ultimate Guitar pages) and, for each instrument (`rhythm`, `bass`, `keys`, `drums`, `lead`, `instrument1`), the `index` of the track it plays (-1: its default part; -2: it is out of the band), and whether that was chosen or connected automatically: after every `load`. The control panel's instrument rows show them |
| `songs` | `names`, `current` | The songs the game can open (their file names without the suffix) and the index of the open one: after the engine connects and whenever the game opens a song. The control panel shows them in its song list |

## Engine → game

| `type` | Fields | Meaning |
|---|---|---|
| `hello` | `engine`, `protocol`, `sampleRate`, `clock` | Answer to the game's hello; `clock` names the shared memory |
| `loaded` | `notes`, `keys` | A `load` has been taken over by the audio thread; `keys`: the sound in use, or empty if there is no keyboard plugin |
| `command` | `command` | A control the engine read (the footswitch): `playPause`, `restart`, `sectionPrevious`, `sectionNext`. Tone presets are switched in the engine and not sent |
| `connect` | `instrument`, `track` | A track chosen in the control panel for an instrument (an `index` from the last `tracks`; -1: its default part; -2: none, the instrument is out of the band in this song): the game keeps it in the song's band file (`<song>.band.json`) and arranges the band again (`load`, `tracks`) |
| `openSong` | `index` | The song chosen in the control panel's song list: an index into the game's last `songs` |
| `style` | `name` | The band playstyle chosen in the control panel ("Ballad", one of `load`'s `styles`): the game arranges the band in it (`tracks`, `load`) |
| `importedSong` | `path` | The control panel's import window has written this song file (in the game's songs-local folder): the game reads its song list again, sends `songs`, and opens it |

## Clock (shared memory, 64 bytes, little-endian)

| Offset | Type | Field |
|---|---|---|
| 0 | uint32 | magic `0x4B435447` ("GTCK") |
| 4 | uint32 | version (1) |
| 8 | int64 | sequence: odd while the engine is writing |
| 16 | double | quarter: the song position heard at `ticks` |
| 24 | int64 | ticks: Windows performance counter (`QueryPerformanceCounter`) |
| 32 | double | quarter notes per second (tempo × speed; 0 when stopped) |
| 40 | int32 | playing (0 or 1) |
| 44 | int32 | reserved |
| 48 | double | where the count-in ends (NaN: no count-in) |
| 56 | int64 | performance counter ticks per second |

The engine sets `ticks` to the moment the block's first sample is heard: the time it processes the block
plus the output latency. The game reads with the sequence (read it, the fields, then the sequence again;
retry if it changed or was odd) and works out the position now:
`quarter + (now - ticks) / ticksPerSecond × quartersPerSecond`.
