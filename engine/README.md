# GuitarTutor audio engine

The C++ audio engine (see [../docs/plan.md](../docs/plan.md)). It owns the audio interface, hosts the TONE3000
plugin for your guitar and the band's bass amp, plays the band, and will grow the listener and the recorder.

What it does now:
- Your guitar through TONE3000, with TONE3000's own presets switched from the footswitch (E1).
- The band (M2): it plays the parts the game arranges, on the song's tempo map, with a count-in. Drums are
  synthesized, the bass is a finger-played string through the second TONE3000 instance (your bass amp
  capture), and the keys are Numa Player. The band owns the song's transport. The game asks it to play,
  pause, seek and change speed over a local connection, and reads its clock from shared memory
  ([../schema/engine-protocol.md](../schema/engine-protocol.md)).
- Latency and CPU readouts, the footswitch, and a log.

## Build

Needs the Visual Studio Build Tools (C++ workload; CMake and Ninja come with them) and JUCE in
`third_party/JUCE` (see [third_party/README.md](third_party/README.md)).

```
engine\build.cmd            (Release; or: build.cmd Debug)
```

The program is `build\Release\gt_engine_artefacts\Release\GuitarTutor Engine.exe`.

## Trying experiment E1

1. Close the TONE3000 standalone app first, or you will hear your guitar through both.
2. Start the engine. It opens Focusrite's ASIO driver at 48 kHz with a 64-sample buffer (change it in the
   device panel at the top). The guitar input is input 2, the Scarlett Solo's instrument jack.
3. "Guitar tone..." opens the guitar's TONE3000 window. Its saved presets (shared with the standalone app)
   appear in the preset list; step through them with the buttons or the footswitch.
4. "Bass amp..." opens the second instance: load a bass amp capture there, and the band's bass plays
   through it.
5. Footswitch: button 1 next preset, button 2 previous preset. Buttons 3-6 go to the game: play / pause,
   previous section, next section, restart. Any game controller works as a footswitch until the Arduino
   pedal exists.

Tone switching uses TONE3000's presets: it builds the new model in the background and fades its output across
the swap (25 ms fades), so a switch in a pause between phrases is not heard. An instance runs its amp model
whether you play or not, which is why there is one guitar instance rather than one per tone.

Device settings and both TONE3000 instances (with their tones) are remembered in `%APPDATA%\GuitarTutor\GuitarTutor Engine.settings`;
the log is kept in `engine.log` in the same folder.

## Measured (2026-10-21, Scarlett Solo, Focusrite USB ASIO, 48 kHz)

Round trip as reported by the driver:

| Buffer | Round trip | Audio CPU, no models | Audio CPU, guitar and bass amp models playing |
|---|---|---|---|
| 32 | 5.5 ms | 3 % | |
| 64 | 7.8 ms | 2 % | about 25 %, peak 28 % |
| 128 | 14.5 ms | 2 % | |

"Audio CPU" is the share of each buffer's time spent processing (JUCE's measure), not of the whole processor:
at 64 samples a buffer lasts 1.33 ms, so the two amp models take about 0.33 ms of it. Preset switches are
quick and not disturbing.

Still to measure: the true round trip with a loopback cable.

## The band (M2)

Start the game (`game/Builds/Desktop/GuitarTutor.exe`). It starts the engine itself, hidden, with
`--parent <the game's process>`: the engine then closes when the game does, even if the game crashes. C in
the game shows the engine's window, the control panel, and T shows the guitar's TONE3000. Closing those
windows only hides them. You can also start the engine yourself first, as before; the game then uses it and
leaves it running.

Play from the game (Space) or the footswitch. Playing from the start, or from wherever you moved to, begins
with a bar of count-in on sticks; after a pause the band plays on from where it stopped.

- The game writes the parts (`game/Assets/GuitarTutor/Core/Band/Arranger.cs`) in one of three styles:
  Ballad (the default below 120 BPM), Rock and Pop. B switches style in the game.
- Drums: MONSTER Drums (a free sampled drum plugin) once you have chosen a kit in it; until then, and
  without it, a synthesized kit (`src/Instruments.*`). The count-in sticks are always the synthesized ones.
- Instrument plugins for the band: the engine looks for `MONSTER Drums*.vst3` and `MONSTER Bass*.vst3` in the
  VST3 folder, then in `monster_plugins/Library files/` next to the repository. Open "Band drums..." or "Band
  bass..." in the control panel, choose a kit or a bass in the plugin's window (each is one `.mse` file from
  its library folder), and close the window: from then on the band plays it, and the engine remembers it. The
  band sends each part on its own MIDI channel (keys 1, bass 2, drums 10) and each plugin hears only its own.
- Bass: MONSTER Bass once you have chosen a bass in it, or a finger-played string built from its harmonics
  (`BassVoice`). The synthesized one goes through the "Bass amp" TONE3000 instance; the plugin has its own amp
  and effects, so it goes straight to the mixer unless you tick "through bass amp" in the control panel. The first version (a Karplus-Strong string plucked with noise) had almost
  no fundamental and sounded thin and too high: its strongest partials sat at 200 Hz-1.3 kHz. Now the
  fundamental leads, with the next harmonics about 3, 7 and 13 dB under it.
- Keys: Numa Player, its sound chosen by the style (Rhodes for Ballad, B3 organ for Rock, grand piano for
  Pop), made into a plugin state from the Numa Player app's last state (`src/NumaKeys.*`). Without Numa
  Player installed, the band plays without keys.
- Mixer: each part's sound has a level (drums 0.8, bass 0.8 after its amp, keys set by the game per style),
  then your fader in the control panel (0 dB is the balanced mix; double-click for 0 dB again; remembered
  between runs), then a soft limiter on the band's sum (peaks above 0.7 bend towards 1).

Measured with the band playing (silent mode, 48 kHz, 64 samples): audio CPU 2-4 %; the mix at about -21 dB
RMS, peak 0.8.

## Testing without a sound

```
"GuitarTutor Engine.exe" --silent --record out\band.wav --run-for 40
```

`--silent` replaces the audio interface with a pretend one (`src/SilentDevice.*`). It runs the engine at real
time (48 kHz, 64-sample blocks) and records what the engine plays. A silent engine may run next to a normal
one, and it leaves your settings as they are.

```
python tools\band_session.py <out-folder> --keys "3:Space,20:quit" --seconds 30 -demo
.venv\Scripts\python.exe tools\wav_check.py <out-folder>\band.wav --bpm 96
```

`band_session.py` starts a silent engine and the game, and plays the game by key script (its `-keys`
option). Its other options go to the game: `-song <file>`, `-demo`, `-style Rock`, `-parts drums`.
`wav_check.py` prints the levels and the onsets against the beat grid.

Checked this way (2026-10-21):
- Count-in sticks exactly a beat apart, the band in on the next beat, and every drum hit on the eighth-note
  grid.
- Pause, a bar on, play again (it counts in again from that bar); 110 % speed (123 BPM for Wicked Game).
- Switching styles while playing: the band picks up where it was.

## Checking an instrument plugin

```
build\Release\plugin_probe_artefacts\Release\plugin_probe.exe <plugin.vst3> out\probe [program-filter ... | --state <file> ...]
```

With `--map [--velocity n]` (and a `--state` file per sound) it plays every note from 24 to 96 on its own and
lists the ones that sound, how loud, and how much of each is low, mid and high: a drum kit's note layout.

MONSTER Drums v3 (2026-10-22): VST3, 32 outputs (16 stereo pairs; many kits put each drum on its own pair, so
the engine mixes all of them), no latency, about 1 % audio CPU. The kits tried (Acoustica Hits, Sonic Rock,
G-Linn) follow the General MIDI drum layout. Their hi-hats are about 26 dB under the kick at the same velocity.
A kit is a state that starts with the path of its `.mse` file.

Numa Player (2026-10-21): VST3 instrument, 0 in / 2 out, no latency, no program list. A new instance is silent
until it gets a state with a sound (`tools/numa_state.py`). Grand, upright, Rhodes and rock B3: 1-3 % mean audio
CPU at 64 samples, peaks 6-18 %.

## Layout

| File | Contents |
|---|---|
| `src/EngineCore.*` | Audio device, the processing graph, the hosted plugins, the band's mixer |
| `src/Band.*` | The band: the song's notes and tempo map, the transport, the count-in, the clock |
| `src/Instruments.*` | Synthesized drum kit and plucked bass |
| `src/NumaKeys.*` | Numa Player states for a sound |
| `src/GameLink.*` | The connection to the game (TCP on 127.0.0.1:47800, JSON lines) |
| `src/SharedClock.*` | The clock in shared memory |
| `src/SilentDevice.*` | The pretend audio interface for `--silent` |
| `src/Processors.*` | Small processors: the mixer's volume controls and the limiter |
| `src/Footswitch.*` | HID joystick reader (Windows joystick API, 500 Hz polling) |
| `src/ParentWatch.*` | Started by the game: closes the engine when the game is gone |
| `src/MainComponent.*`, `src/Main.cpp` | The window, the command line, the game's messages |
| `tools/PluginProbe.cpp` | `plugin_probe`: loads a plugin without a window, lists what it offers, plays a keyboard phrase in 64-sample blocks at real-time pace, reports audio CPU and writes WAVs |
| `tools/numa_state.py` | Numa Player states: list its sounds, take the standalone's state, make a state for any sound |
| `tools/band_session.py` | A silent test session: engine and game, the game played by key script |
| `tools/wav_check.py` | Levels and onsets of a recording, against the beat grid |

## Licences

JUCE is used under the AGPLv3 while GuitarTutor is personal; a commercial JUCE licence may be required before
publishing. The ASIO headers bundled with JUCE are Steinberg's (Steinberg ASIO licence or GPLv3; publishing
under Steinberg's licence needs their free signed agreement). See [../CREDITS.md](../CREDITS.md).
