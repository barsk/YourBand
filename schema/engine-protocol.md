# Game ↔ audio engine protocol (version 1)

The game (`game/`) and the audio engine (`engine/`) run as two programs on the same PC
(see [../docs/plan.md](../docs/plan.md), "Architecture"). They talk in two ways:

- **Messages**: TCP on `127.0.0.1:47800`. The engine listens and the game connects. Each message is one JSON
  object on one line (UTF-8, `\n` at the end). One game at a time.
- **Clock**: shared memory named `Local\GuitarTutorClock`. The engine writes it after every audio block;
  the game reads it every frame. Messages are too late and too jittery for drawing in time with the music.

Positions are in quarter notes from the start of the song, as in the song file. The engine owns the transport
(it is the music); the game only asks.

Implemented by hand on both sides for now: `game/Assets/GuitarTutor/Core/Engine/EngineProtocol.cs` and
`engine/src/GameLink.cpp`, `engine/src/Band.cpp`. The plan is to generate both from one definition here once
the message set settles.

## Game → engine

| `type` | Fields | Meaning |
|---|---|---|
| `hello` | `client`, `protocol` | First message after connecting |
| `load` | `title`, `style`, `tempos` [[atQuarter, bpm]...], `meters` [[atBar, numerator, denominator]...], `notes` [[part, pitch, at, length, velocity]...], `keys` | The song's tempo map and the band's parts (replaces what was loaded; stops playing). `part`: 0 drums (General MIDI drum notes), 1 bass, 2 keys. `keys`: the keyboard sound, a Numa Player sound name such as `2 - E_PIANO/1 - EP-Mark I` (empty: the engine's default) |
| `play` | `from`, `countIn` | Play from `from`, after a count-in of `countIn` quarter notes (0: none). The drummer counts in on sticks; band notes inside the count-in are not played |
| `pause` | | Stop; sounding notes are released |
| `seek` | `to` | Move to `to`; keeps playing if playing |
| `speed` | `value` | Practice speed: 1 is the song's tempo |
| `window` | `show` | Brings up a window of the engine: `controlPanel` (devices, faders, log), `guitar` (the guitar's TONE3000), `bass` (the bass amp), `drums` (the band's drum plugin) or `bandBass` (the band's bass plugin) |
| `quit` | | Close the engine (it saves its settings); the game sends it when it closes an engine it started |
| `mix` | `part`, `gain` | The level of a band part's sound: `drums`, `bass` or `keys`, 1 as played (defaults: drums 0.8, bass 0.8 after its amp, keys 1.3; the game sets the keys per style). The control panel's faders multiply it and are remembered by the engine |

## Engine → game

| `type` | Fields | Meaning |
|---|---|---|
| `hello` | `engine`, `protocol`, `sampleRate`, `clock` | Answer to the game's hello; `clock` names the shared memory |
| `loaded` | `notes`, `keys` | A `load` has been taken over by the audio thread; `keys`: the sound in use, or empty if there is no keyboard plugin |
| `command` | `command` | A control the engine read (the footswitch): `playPause`, `restart`, `sectionPrevious`, `sectionNext`. Tone presets are switched in the engine and not sent |

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
