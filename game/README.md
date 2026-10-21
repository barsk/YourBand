# GuitarTutor game (Unity)

The game client: Unity 6.6 (6000.6.7f1) with URP, decided in experiment E4
([../experiments/e4-renderer/README.md](../experiments/e4-renderer/README.md)). See
[../docs/plan.md](../docs/plan.md) for the whole design; the audio engine is a separate program in
[../engine](../engine).

## Structure

Code lives in `Assets/GuitarTutor/`, split into assemblies that enforce the plan's dependency rules:

| Assembly | Folder | Rule |
|---|---|---|
| `GuitarTutor.Core` | `Core/` | Plain C#, no Unity references (`noEngineReferences`): song model, timing, later the bandleader and the score follower. Tested without a scene. |
| `GuitarTutor.Core.Tests` | `Tests/EditMode/` | NUnit tests for the core |
| `GuitarTutor.Runtime` | `Runtime/` | Unity side: the desktop screen, later band animation and the connection to the engine. Depends on the core, never the other way round. |
| `GuitarTutor.Editor` | `Editor/` | Project setup and the desktop build, from code (`ProjectSetup`) |

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
- `Core/Playback/SongClock.cs`: play, pause, seek, practice speed and count-ins, on a wall clock for now.
- `Runtime/Desktop/DesktopApp.cs` and `Runtime/UI/`: the desktop screen in UI Toolkit (M1). The chart strip
  (section map, the current bar large with the beat cursor and the next three bars, lyrics karaoke style)
  and the panels: chord diagrams drawn from the voicings, strumming, tab, song info. Look in `Chart.uss`.

![The desktop screen with the demo song](../docs/images/m1-desktop-intro.png)

## Building and running

```
game\build_desktop.cmd
game\Builds\Desktop\GuitarTutor.exe
```

`build_desktop.cmd` sets the project up from code (`ProjectSetup.Run`: the UI Toolkit theme and panel
settings, the Desktop scene, player settings) and builds the Windows player (Mono) into `Builds/Desktop`. In the
Editor: menu GuitarTutor > Set up project, then play `Assets/GuitarTutor/Scenes/Desktop.unity`.

The game plays the first song in the repository's `songs-local/` (imported with `tools/songtool`), or the demo
song. Keys: Space play / pause (playing again counts you in from the start of the bar), R restart with a
count-in, Left / Right a bar, PgUp / PgDn a section (or click it in the section map), Up / Down the speed in
5% steps, 1-4 the panels, P the next strumming pattern, L the next song, H help, Esc quit.

Command line:

| | |
|---|---|
| `-song <file>` | play that song file |
| `-demo` | play the demo song |
| `-shot <folder> -at 1.75,17.75,countin` | screenshots at those positions (quarter notes), every panel shown, then quit |
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
