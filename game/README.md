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
| `GuitarTutor.Runtime` (to come) | `Runtime/` | Unity side: views, band animation, the connection to the engine. Depends on the core, never the other way round. |
| `GuitarTutor.Editor` (to come) | `Editor/` | Editor tools, and commands for the Unity CLI (`[CliCommand]`) |

What is there now:

- `Core/Music/TempoMap.cs`: musical time. Positions are in quarter notes from the start of the song (as in
  MIDI); time signatures give bars, tempos give seconds, both ways.
- `Core/Songs/Song.cs`: a song in layers: sections, chords, lyrics (words at positions), strumming patterns,
  chord voicings (frets, fingers, barre), riffs as tab notes, and the sources each part came from.
- `Core/Songs/SongJson.cs`: songs on disk as versioned JSON.

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
