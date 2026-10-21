# GuitarTutor audio engine

The C++ audio engine (see [../docs/plan.md](../docs/plan.md)). It owns the audio interface, hosts the TONE3000
plugin for your guitar and the band, and will grow the band's sound, the listener and the recorder.

Right now it is experiment E1: your guitar through TONE3000, switching between TONE3000's own presets, a test
bass line through a second TONE3000 instance, latency readouts, and the footswitch.

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
4. "Bass amp..." opens the second instance: load a bass amp capture, then "Start test bass line".
5. Footswitch: button 1 next preset, button 2 previous preset, button 3 starts and stops the bass line.
   Any game controller works as a footswitch until the Arduino pedal exists.

Tone switching uses TONE3000's presets: it builds the new model in the background and fades its output across
the swap (25 ms fades), so a switch in a pause between phrases is not heard. An instance runs its amp model
whether you play or not, which is why there is one guitar instance rather than one per tone.

Device settings and all three tones are remembered in `%APPDATA%\GuitarTutor\GuitarTutor Engine.settings`;
the log is kept in `engine.log` in the same folder.

## Measured (2026-10-21, Scarlett Solo, Focusrite USB ASIO, 48 kHz)

Round trip as reported by the driver, TONE3000 instances without models loaded:

| Buffer | Round trip | Audio CPU |
|---|---|---|
| 32 | 5.5 ms | 3 % |
| 64 | 7.8 ms | 2 % |
| 128 | 14.5 ms | 2 % |

Still to measure: CPU with real amp models loaded, and the true round trip with a loopback cable.

## Layout

| File | Contents |
|---|---|
| `src/EngineCore.*` | Audio device, the processing graph, the hosted plugins |
| `src/Processors.*` | Test bass line |
| `src/Footswitch.*` | HID joystick reader (Windows joystick API, 500 Hz polling) |
| `src/MainComponent.*`, `src/Main.cpp` | The window |

## Licences

JUCE is used under the AGPLv3 while GuitarTutor is personal; a commercial JUCE licence may be required before
publishing. The ASIO headers bundled with JUCE are Steinberg's (Steinberg ASIO licence or GPLv3; publishing
under Steinberg's licence needs their free signed agreement). See [../CREDITS.md](../CREDITS.md).
