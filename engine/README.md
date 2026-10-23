# GuitarTutor audio engine

The C++ audio engine (see [../docs/plan.md](../docs/plan.md)). It owns the audio interface, hosts the TONE3000
plugin for your guitar and the band guitarist's, plays the band, and will grow the listener and the recorder.

What it does now:
- Your guitar through TONE3000, with TONE3000's own presets switched from the footswitch (E1).
- The band (M2): it plays the parts the game arranges, on the song's tempo map, with a count-in: drums, bass
  and a rhythm guitarist (built-in or on instrument plugins), and the keys on Numa Player. The band guitarist
  plays through the second TONE3000 instance. The band owns the song's transport. The game asks it to play,
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
4. "Band guitar tone..." opens the second instance, the band guitarist's amp: pick its tone there, or in
   the "Band guitar tone" list (the same presets).
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
the game shows the engine's window, the control panel, and T shows the guitar's TONE3000, in front of the
game (the game allows the engine to take the front just before it asks). Closing those windows only hides
them. You can also start the engine yourself first, as before; the game then uses it and
leaves it running.

Play from the game (Space) or the footswitch. Playing from the start, or from wherever you moved to, begins
with a bar of count-in on sticks; after a pause the band plays on from where it stopped.

- The game writes the parts (`game/Assets/GuitarTutor/Core/Band/Arranger.cs`) in one of three styles:
  Ballad (the default below 120 BPM), Rock and Pop. B switches style in the game.
- The band's drums and bass each play their built-in sound or an instrument plugin, chosen in the control
  panel's "Band drums" and "Band bass" dropdowns, also while the band plays:
  - Built-in sound: a synthesized kit, and a finger-played bass string built from its harmonics
    (`src/Instruments.*`). The count-in sticks are always the built-in ones.
  - The plugins the engine knows, if you have them: `MONSTER Drums*.vst3`, `MONSTER Bass*.vst3` and
    `ABPL*.vst3` (Ample Bass P Lite), in the VST3 folder or in `monster_plugins/Library files/` next to the
    repository.
  - "Other plugin...": any VST3 instrument (a plugin that takes no notes is refused); it stays in the list.
  - "Pick another sound in MONSTER ...": a MONSTER plugin shows its sound picker only while it has no sound,
    so this starts it anew and opens its window. A MONSTER plugin is silent until you choose a kit or a bass
    in it (each is one `.mse` file from its library folder); the built-in sound plays until you close its
    window with one chosen.

  "Plugin window..." opens the plugin's own window. The engine remembers your choice and each plugin's sound
  (`bandBass.<plugin file>` in the settings). MONSTER Bass takes about 7 s to load and 5 s to let go of its
  samples; the control panel waits meanwhile, while the band plays on. The band sends each part on its own
  MIDI channel (keys 1, bass 2, drums 10) and each plugin hears only its own.
- The band guitarist (when "Band guitarist plays rhythm" is ticked, or G in the game; both are the same
  setting) strums the song's strumming pattern for each section on the chord shapes the chart shows (the
  game writes it: `Arranger.Strum`): a stroke crosses the strings 12 ms apart down, 9 ms up (the top four
  strings), and each string rings until it is struck again or the chord changes. It plays through the band
  guitar's TONE3000 ("Band guitar tone"), the built-in guitar (picked strings built from their harmonics,
  `GuitarVoice`, about -33 dB RMS a note, a guitar's level) or a plugin: MONSTER Guitar (its "[Notes]"
  instruments; a "[Chord]" one plays a whole chord for each string's note) or Kontakt with Shreddage 3
  Stratus FREE. A guitar plugin goes through a trim into the amp (MONSTER Guitar plays about 30 dB louder
  than a guitar: -30 dB), or straight to the mixer with "through band guitar tone" off. Guitar plugins get
  the notes as they sound (MONSTER Guitar: note 40 is E2).
  Kontakt's Shreddage strum speaks a little late (recorded silently: the first strum reaches half its peak
  36 ms after the beat, the built-in guitar's 11 ms), so the band sends Kontakt its guitar notes 20 ms early
  (`bandGuitarAdvanceMs` in the settings changes it; 0 turns it off; the log says "plays 20 ms ahead" when
  it is on). The band has a cursor of its own for the guitar's notes, so this works with seeking and the
  count-in. Shreddage's own keyswitches, effects and MIDI channel are saved with Kontakt's state: if the
  guitar is silent or muted, look at its window first.
- Bass plugins sound an octave below the note they get (as bass parts are written; MONSTER Bass and Ample
  Bass both), so the band sends them its bass notes an octave up. The built-in bass is a DI sound, and the
  plugins have their own amps and effects: all go straight to the mixer. Each known plugin has a level that
  puts it as loud as the built-in sound (measured straight to the mixer). The first built-in bass (a Karplus-Strong string plucked with
  noise) had almost no fundamental and sounded thin and too high: its strongest partials sat at
  200 Hz-1.3 kHz. Now the fundamental leads, with the next harmonics about 3, 7 and 13 dB under it.
- Changing plugins: the engine rewires the whole graph in one rebuild, so the audio thread goes from the
  whole old graph to the whole new one, and deletes a plugin it takes out itself, once the graph has let go
  of it. Left to the graph, MONSTER Bass was deleted while the graph held the lock the audio thread needs
  to take a newer graph, and with a rebuild per connection the band went silent for 5 s.
- Keys: Numa Player, its sound chosen by the style (Rhodes for Ballad, B3 organ for Rock, grand piano for
  Pop), made into a plugin state from the Numa Player app's last state (`src/NumaKeys.*`). Without Numa
  Player installed, the band plays without keys. A new sound mutes the keys for half a second: Numa Player
  loads it in the background and once crashed the engine when it played during the load, so the engine
  pauses a plugin's audio whenever it gives it a new state.
- Mixer: each part's sound has a level (drums 0.8, the built-in bass 1.75 or the bass plugin's own, keys set
  by the game per style, guitar 0.8 after its amp),
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
option). Its other options go to the game: `-song <file>`, `-demo`, `-style Rock`, `-parts drums`; and
`--band-bass ABPL` (a plugin's file name, or `synthesized`) goes to the engine. `wav_check.py` prints the
levels and the onsets against the beat grid.

```
python tools\switch_session.py <out-folder>
```

`switch_session.py` plays a made-up song on a silent engine without the game and switches the band's bass
between its plugins every 3.5 s: the band should play on through every switch. `--snapshot <file.png>`
makes the engine save a picture of its control panel 3 s after it starts.

Checked this way (2026-10-21):
- Count-in sticks exactly a beat apart, the band in on the next beat, and every drum hit on the eighth-note
  grid.
- Pause, a bar on, play again (it counts in again from that bar); 110 % speed (123 BPM for Wicked Game).
- Switching styles while playing: the band picks up where it was.

## Checking an instrument plugin

```
build\Release\plugin_probe_artefacts\Release\plugin_probe.exe <plugin.vst3> out\probe [program-filter ... | --state <file> ...]
```

With `--map [--velocity n]` (with a `--state` file per sound, or the plugin's own sound) it plays every note
from 24 to 96 on its own and lists the ones that sound, how loud, how much of each is low, mid and high, and
the pitch it sounds at: a drum kit's note layout, or an instrument's range and octave.
`--map` plays on MIDI channels 10 and 1; `--channel n` on that one (0: all sixteen), and it reports the loudest
note, so a silent plugin shows as -120 dB. The state is loaded with the audio running (Kontakt only finishes
loading an instrument then) for 12 s, or `PROBE_WAIT_MS` milliseconds. A `--state` file holds the plugin's own
data (the VST3 state's `<IComponent>`, as the engine keeps it under `bandGuitar.<plugin>` in its settings),
not the whole wrapped state.
With `--load-time` (and two or more `--state` files) it switches between the sounds and plays a note 100 ms to
3 s after each switch: how long the plugin takes to load a sound.

MONSTER Drums v3 (2026-10-22): VST3, 32 outputs (16 stereo pairs; many kits put each drum on its own pair, so
the engine mixes all of them), no latency, about 1 % audio CPU. The kits tried (Acoustica Hits, Sonic Rock,
G-Linn) follow the General MIDI drum layout. Their hi-hats are about 26 dB under the kick at the same velocity.
A kit is a state that starts with the path of its `.mse` file.

MONSTER Bass v2 (2026-10-22): 32 outputs; a five-string bass from note 35 (sounding B0) to 64, each note
sounding an octave below its number.

Ample Bass P Lite 4.0.0 (2026-10-22): VST3 instrument, 2 in / 2 out, no latency, about 2.5 % mean audio CPU;
plays its default sound without a state. Notes 40 to 76 are the bass (40 sounds as the low E, an octave
below its number); notes above 82 are noises (slaps, hits). Its library is a separate download, installed
where you choose (`plugins/ABPL_Library/` here, not in git); the plugin finds it through the registry.

Numa Player (2026-10-21): VST3 instrument, 0 in / 2 out, no latency, no program list. A new instance is silent
until it gets a state with a sound (`tools/numa_state.py`). Grand, upright, Rhodes and rock B3: 1-3 % mean audio
CPU at 64 samples, peaks 6-18 %. A new sound plays at full level 100 ms after the state (2026-10-22,
`--load-time`).

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
| `src/WindowFront.*` | Brings a window of the engine in front of the game |
| `src/MainComponent.*`, `src/Main.cpp` | The window, the command line, the game's messages |
| `tools/PluginProbe.cpp` | `plugin_probe`: loads a plugin without a window, lists what it offers, plays a keyboard phrase in 64-sample blocks at real-time pace, reports audio CPU and writes WAVs |
| `tools/numa_state.py` | Numa Player states: list its sounds, take the standalone's state, make a state for any sound |
| `tools/band_session.py` | A silent test session: engine and game, the game played by key script |
| `tools/switch_session.py` | A silent test session without the game: the band's bass switches plugins while it plays |
| `tools/wav_check.py` | Levels and onsets of a recording, against the beat grid |

## Licences

JUCE is used under the AGPLv3 while GuitarTutor is personal; a commercial JUCE licence may be required before
publishing. The ASIO headers bundled with JUCE are Steinberg's (Steinberg ASIO licence or GPLv3; publishing
under Steinberg's licence needs their free signed agreement). See [../CREDITS.md](../CREDITS.md).
