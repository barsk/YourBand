# YourBand game (Unity)

The game client: Unity 6.6 (6000.6.7f1) with URP, decided in experiment E4 (2026-10-21: see docs/plan.md; the
experiments are no longer in the repository, only in its history). See
[../docs/plan.md](../docs/plan.md) for the whole design, [../docs/architecture.md](../docs/architecture.md) for how it is built and
[../docs/band-arrangement.md](../docs/band-arrangement.md) for how the band's parts are made; the audio engine is a separate program in
[../engine](../engine).

## Structure

Code lives in `Assets/YourBand/`, split into assemblies that enforce the plan's dependency rules:

| Assembly | Folder | Rule |
|---|---|---|
| `YourBand.Core` | `Core/` | Plain C#, no Unity references (`noEngineReferences`): song model, timing, later the bandleader and the score follower. Tested without a scene. |
| `YourBand.Core.Tests` | `Tests/EditMode/` | NUnit tests for the core |
| `YourBand.Runtime` | `Runtime/` | Unity side: the desktop screen, later band animation and the connection to the engine. Depends on the core, never the other way round. |
| `YourBand.Editor` | `Editor/` | Project setup and the desktop build, from code (`ProjectSetup`) |

What is there now:

- `Core/Music/TempoMap.cs`: musical time. Positions are in quarter notes from the start of the song (as in
  MIDI); time signatures give bars, tempos give seconds, both ways.
- `Core/Songs/Song.cs`: a song in layers: sections, chords, lyrics (words at positions), strumming patterns,
  chord voicings (frets, fingers, barre), riffs as tab notes, and the sources each part came from.
- `Core/Songs/SongJson.cs`: songs on disk as versioned JSON.
- `Core/Songs/DemoSong.cs`: a short made-up song with every layer, played when there is no song to load (and
  shown in the screenshots in the repository).
- `Core/Chart/SongChart.cs`: the song laid out bar by bar for the chart, and what to show at a position: the
  bar, the lyric line and the words sung, the next chord change, the strumming pattern and stroke.
- `Core/Playback/SongClock.cs`, `Transport.cs`: play, pause, seek, practice speed and count-ins. The audio
  engine runs the transport when it is there; without it, a wall clock keeps time silently.
- `Core/Music/Chord.cs`: chord names read as notes (root, intervals, slash bass).
- `Core/Band/Arranger.cs`: the band's parts from the chart, in a style (Ballad, Rock, Pop). Drums with crashes
  at section starts and a fill into each new section, bass on the roots walking into changes, keys voiced
  close and moving as little as they can, and the guitarist strumming the song's patterns on its chord
  shapes (the rhythm guitar's row in the control panel can take it out: "None"). With a capo, the band plays that much higher.
- `Core/Engine/EngineProtocol.cs`, `Runtime/Engine/`: the connection to the audio engine
  ([../schema/engine-protocol.md](../schema/engine-protocol.md)): messages over TCP, and the engine's clock
  read from shared memory each frame, so the chart moves with the sound.
- `Runtime/Desktop/DesktopApp.cs` and `Runtime/UI/`: the desktop screen in UI Toolkit. The chart is rows of
  up to four bars (each section starts a new row), all bars as wide as they are long, the section's name over
  each row. Two rows are in view: the one being played, with the play marker running through it, and the
  next. A beat before the next row starts, the rows slide up smoothly. The tab runs across the screen above
  it as a tape under a fixed marker. The panels: chord diagrams drawn from the voicings, strumming, song info.
  Look in `Chart.uss`.

- `Core/Performance/`: how each musician plays the band's notes: the drummer's sticking (which hand or foot
  plays each hit), the guitarist's grips (fingers on frets, barres) and strokes read back from the strummed
  notes, the bassist's strings and frets (a hand that moves as little as it can), the keyboardist's fingers.
- `Runtime/Band/`: the band as 3D musicians (Rocketbox avatars on the Mixamo playing clips, made by
  `tools/band_assets` and YourBand > Set up project; not in git, and loaded at run time from the optional band
  pack, `Band/band.bundle` next to the game: `BandStage`). The clips are locked to the beat; arms
  and fingers go onto the real targets by IK in `LateUpdate`: sticks onto the drums and cymbals, fingers onto
  frets and strings, keys pressed down under the fingers.
- `Runtime/Vr/VrMode.cs`: VR mode (below).

![The desktop screen with the demo song](../docs/images/desktop.png)

## Building and running

```
game\build_desktop.cmd
game\Builds\Desktop\YourBand.exe
```

`build_desktop.cmd` sets the project up from code (`ProjectSetup.Run`: the UI Toolkit theme and panel
settings, the Desktop scene, player settings) and builds the Windows player (Mono) into `Builds/Desktop`. In the
Editor: menu YourBand > Set up project, then play `Assets/YourBand/Scenes/Desktop.unity`.

The game plays the first song in the repository's `songs-local/` (imported with `tools/songtool`), or the demo
song. The band needs the audio engine (`engine/`, built). The game starts it by itself, hidden, and closes it
when it closes (also if the game crashes). An engine you started yourself is used as it is, and left
running. The header says "band: starting the engine", then "band: Ballad" once the band has the song.

Keys:
- Space: play or pause. After a pause it plays on from where it stopped; from the start, or after moving
  somewhere else, it counts you in first.
- R: restart with a count-in.
- Left / Right: a bar back or on.
- PgUp / PgDn: a section back or on (or click it in the section map).
- Up / Down: the speed in 5 % steps.
- 1-4: the panels, in their order on screen: song, chords, strumming, tab.
- P: the next strumming pattern.
- B: the next band style.
- V: the band view: the band in 3D behind the chart, on from the start where the band pack is installed; V again for the chart alone.
- X: VR mode on or off (below).
- L: the next song.
- C: the control panel (audio device, band faders, log). T: your guitar's tone (TONE3000).
- M: the tracks of a Guitar Pro song bar by bar, as TuxGuitar shows them, over the chart: a row per track (with who
  of the band plays it), brighter the more it plays in a bar, the sections' names above and where the music is.
  Click a bar to go there; the TAB panel then shows that track's tab. The overlay stays open until M again.
- H: help. Esc: closes the help or the tracks overlay; with neither open, quits.

The footswitch's buttons 3-6 (play / pause, previous section, next section, restart) come through the engine.

### VR mode

The band in your room and the chart on a board in front of you, in the Quest through Virtual Desktop
(docs/plan.md, "VR mode"). Start the game with `-vr`, or press X. The game starts OpenXR itself, with Virtual
Desktop's runtime (VDXR) when it is installed, so it never starts SteamVR; without a headset it shows the room
on the desktop and says "VR: no headset". The camera clears to a key colour (magenta, #FF00FF) with nothing
blended into it; in Virtual Desktop, turn on passthrough by chroma key with that colour (RGB 255, 0, 255) and
raise similarity and smoothness until the thin magenta outline round the musicians is gone (experiment E2, 2026-10-21;
its app is no longer in the repository, see its history). The board is opaque, so it needs no key.

The first time, the band and the board go in front of wherever you are; after that they stay where you put
them (in `vr-settings.json` in the game's data folder, `%USERPROFILE%\AppData\LocalLow\YourBand\YourBand`).
The game was called GuitarTutor until 2026-11-12: at the first start under the new name it takes over the old
name's `vr-settings.json` and saved preferences (the registry key `HKCU\Software\GuitarTutor\GuitarTutor`, which
holds the window's size and place, the panels shown and the song open last), leaving the old ones as they were
(`Runtime/Settings/LegacyData.cs`).

Controllers:
- A: play or pause. B: restart with a count-in.
- Right stick: left / right a bar back or on, up / down the speed. Left stick: left / right a section back or
  on, up the help.
- Trigger, held: aimed at the board, it goes with the controller (the stick meanwhile sizes it); aimed at a
  musician, they go along the floor with the beam (the stick meanwhile pushes them away, pulls them in or
  turns them), to fit the band into your room. A ring shows their station on the floor, red where it crowds
  another's.
- Grip, held, aimed at a musician: the whole band goes with the beam the same way.
- Right stick click: the band and the board back in front of you.
- X: the next key colour (magenta, green, blue, black). Y, with the left controller resting on the floor: the
  floor's height.

The keyboard works too, and K changes the key colour.

The first time in the headset:
1. In the Virtual Desktop Streamer on the PC, the OpenXR runtime is VDXR (the game picks it itself, even when
   started from the desktop). Virtual Desktop's audio streaming off: the band plays through the Scarlett.
2. In Virtual Desktop in the headset, passthrough by chroma key with #FF00FF (RGB 255, 0, 255); start with a
   generous similarity and smoothness, not a few percent: the video stream blurs the edge between the band and
   the key colour a little, and a low setting leaves that blur as a thin magenta outline. Raise both until the
   outline is gone (nothing in the band is close to magenta, so high values cost nothing until the setting is
   very high; stop when skin or dark hair turns see-through).
3. Connect, then start `game\Builds\Desktop\YourBand.exe -vr` (or press X in the game). Half a second
   after the headset is tracked, the band and the board appear in front of you.
4. Look at: the key's edges (hair, dark clothes), whether the band stands on your floor (Y to calibrate) and
   fits the room (move the musicians), whether the board reads well at that size, latency against the room.
5. If the headset loses the connection the game stays open, showing the room; X twice looks for the headset
   again. The log is `%USERPROFILE%\AppData\LocalLow\YourBand\YourBand\Player.log`.

![VR mode on the desktop (-noxr): the band and the board on the key colour](../docs/images/m7-vr-room.png)

Command line:

| | |
|---|---|
| `-song <file>` | play that song file |
| `-demo` | play the demo song |
| `-shot <folder> -at 1.75,17.75,countin` | screenshots at those positions (quarter notes), every panel shown, then quit (without the band) |
| `-noengine` | do not look for the audio engine |
| `-engine <file>` | the engine program to start (otherwise next to the game in `Engine/`, or the repository's build) |
| `-engineargs "..."` | more for the started engine's command line, for example `"--silent --record band.wav"` |
| `-style Rock` | start in that band style |
| `-parts drums,bass` | give the band only those parts (to check them one at a time) |
| `-view band`, `-view chart` | start in the band view, or with the chart alone (without either: the band where the band pack is installed); `-cam guitarist` (or `drummer`, `bassist`, `keyboardist`, with `:left`, `:right`, `:hands`, `:above`; the drummer also `:feet`, `:kick`, `:kickl`, `:kit`, `:kitl`) frames one musician |
| `-nochart`, `-nopanels` | screenshots without the chart, or without the panels |
| `-guitarplays` | the band guitarist plays in screenshots (there is no engine to say so) |
| `-bandlog` | the musicians log what they play |
| `-vr` | start in VR mode; `-noxr` shows it on the desktop without starting XR; `-xrruntime <json>` uses that OpenXR runtime, `-xrruntime system` the system's |
| `-vrscript "3:head 0 1.6 0; 4:aim 0.25 1.1 0.3 > -1.5 1 1.6; 4.5:press trigger; 6:shot; 8:quit" -keyshots <folder>` | a scripted headset and Touch controllers on the game's own input (with `-vr -noxr`): tests VR mode's controls without a headset (`Runtime/Testing/VrScript.cs`) |
| `-vrsettings <file>` | VR mode's settings in that file instead of the game's data folder (for tests, so they leave yours alone) |

Without a headset, a real XR session can still be started against Unity's OpenXR mock runtime, which comes with
the OpenXR package: `-vr -xrruntime game\Library\PackageCache\com.unity.xr.openxr@<hash>\Runtime\MockRuntime\unity-mock-runtime.json`
(with `-vrsettings` and `-keys "...:shot"` for a look at the mirror view). It checks the session, the graphics
and the rendering in stereo; the mock headset has no controllers to press.
| `-keys "2:Space,6:DownArrow,7:shot,9:quit" -keyshots <folder>` | presses keys on the game's own keyboard at those seconds (never the desktop's), screenshots on "shot": tests the controls end to end |

## Tests

```
game\run_tests.cmd
```

Runs the EditMode tests in batch mode (the project must not be open in the Editor) and prints the result line
from `Logs/test-results.xml`. In the Editor: Window > General > Test Runner.

## Working with it from the command line

The project has Unity's agent package (`com.unity.pipeline`). With the project open in the Editor, the Unity
CLI can drive it: `unity command` lists its commands, `unity command eval "<C#>"` runs code in the Editor.
The Unity MCP server (`unity-editor-mcp` in Claude Code) talks to the same open Editor.

`Library/`, `Temp/`, `Logs/`, `UserSettings/` and builds are not in git (see the repository's `.gitignore`).
