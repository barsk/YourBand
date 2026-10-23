# GuitarTutor: game plan

Living document, started 2026-10-21. Decisions made so far are in the table below; the rest of the
document explains how the pieces fit together.

## Goal

You play your real guitar, and a virtual band (drums, bass, keys, a second guitar) plays the song with you.
It is a practice tool and a jam partner, not a scored game:

- Practice mode: the band keeps steady time. You get the chart, what you played against what was expected
  ("A, expected E"), timing hints, section loops and a speed trainer.
- Jam mode: the band follows your tempo, keeps a groove going when you pause, and plays fills and variations.

There are two ways to see it:

- Desktop: a monitor shows the band in a venue (home studio, garage, studio, arena), with chart panels.
- VR: mixed reality on a Quest 3. The band and the panels appear in your own room.

It is for personal use first and may be published later, so licences are kept clean from the start.

## Decisions

| Topic | Decision | Why |
|---|---|---|
| Game type | Practice and jam partner; no scoring | Your call |
| Game client | Unity, C# | Mecanim retargets Mixamo clips onto Rocketbox avatars; IK, OpenXR and UI are mature |
| Audio engine | Separate C++ process, built on JUCE, started and closed by the game (2026-10-22) | ASIO, VST3 hosting and the amp model engine are all C++; audio stays glitch-free whatever Unity does. Not inside the game: a plugin crash would take the game down, the Editor would lock the engine's DLL, and linking JUCE would put the game under JUCE's licence |
| Offline tools | Python | Import, alignment and the Blender asset pipeline already live there |
| Guitar tone | The engine hosts the TONE3000 plugin (VST3); one instance, tones switched with its own presets | Same tones as the standalone app, one program owns the audio interface; TONE3000 swaps presets with a short fade, which a pause between phrases hides |
| Audio driver | Focusrite's own ASIO driver, not ASIO4ALL | Lower latency |
| Band bass and guitar tone | Clean sampled notes through more TONE3000 instances | Real amp tone for the band from the same library |
| VR | PC VR through Virtual Desktop (Wi-Fi or USB) | Better quality than Link; audio stays on the PC |
| Mixed reality | Virtual Desktop chroma key, key colour configurable (default magenta) | Works without Meta's developer-only passthrough; could ship |
| XR API | Unity's standard OpenXR, no Meta-only SDK features | Virtual Desktop has its own OpenXR runtime |
| Venues | Desktop only; VR shows your real room | Your call |
| Footswitch | Arduino HID joystick, read by the audio engine | Instant tone switching, independent of the game's focus |
| Bandleader | Rule-based; no language model in the timing loop | Predictable, testable, real-time |
| Renderer | URP (decided 2026-10-21 after E4) | Both are fast enough (one player at Quest 3 stereo resolution: URP 1.5 ms, HDRP 2.1-2.7 ms); HDRP's skin looks better, but Unity keeps HDRP in maintenance mode and develops URP. Skin gets a better shader in URP |

## Architecture

Two programs on the PC, talking over a local connection:

| Audio engine (C++) | Game client (Unity, C#) |
|---|---|
| Device I/O: the Scarlett through ASIO | Song library and chart |
| Your guitar: TONE3000 hosted in the engine | Bandleader |
| Band sound: scheduler, sampled instruments, TONE3000 for band bass and guitar, venue reverb | Band animation |
| Listener: strums, chord recognition, tuner, tempo and phase | Views: desktop and VR, panels |
| Recorder: your clean guitar signal and the clock, for replay tests | Room layouts (VR) and venues (desktop) |
| Footswitch | Input: every input becomes the same set of commands |

- Commands go from the game to the engine; events (strums, chords, pedal presses) come back.
- The beat clock is shared memory: the engine's sample position and the matching wall-clock time, so the
  game draws in exact sync with the sound.
- The engine schedules notes 0.3–0.5 s ahead. Animation needs that: a drummer's stick must be moving before
  the hit. Tempo changes are committed at beat boundaries inside that window.
- The audio thread never allocates memory or waits on locks.
- Only your guitar needs the low-latency path. The band (sampled instruments, band amps, reverb) is rendered
  ahead on its own thread in larger blocks and only mixed in the audio callback, so the band can grow without
  eating into your guitar's budget. (E1: two amp models take about 25 % of a 64-sample buffer.)

Offline Python tools feed song files and assets to the game.

Dependency rules in the game client:

- Core logic (song model, tempo map, chart timing, bandleader, score follower) is plain C# with no Unity
  dependencies, unit-tested without starting Unity.
- Unity code depends on the core, never the other way round.
- The song file format and the engine messages are defined once in `schema/`, and C# and C++ code is
  generated from them.

## Song data

A song is stored in our own versioned file format, in layers:

| Layer | Contents |
|---|---|
| Skeleton | Sections, chords per bar, lyrics |
| Rhythm guitar | Strumming pattern, chord voicings (frets, fingers, barre) |
| Lead and riffs | Tab notes |
| Timing | Tempo map, bars and beats |
| Arrangement | Generated band parts |
| Provenance | Source URLs and revisions, and which source was chosen where they disagree |

Importers: Ultimate Guitar (chords and tabs pages), ChordPro, MIDI, later Guitar Pro.

### What Ultimate Guitar pages contain

Checked on Wicked Game. The data is JSON embedded in the page (`js-store`).

| | Chords page | Tabs page |
|---|---|---|
| Sections, chord sequence, lyrics | Yes; chords tagged `[ch]` | Yes; same sequence, chords as plain text |
| Key, tuning | `meta.tonality`, `meta.tuning` | Same |
| Strumming pattern and tempo | `strummings` (bpm, strokes on an 8th-note grid) | No |
| Chord fingerings | `applicature`: frets, fingers, barre, sounding notes | No |
| Lead riff and fills | No | Yes (ASCII tab: frets, no note lengths) |
| Timestamps from the recording | No | Some sections (intro 00:00–00:17) |

Neither page has bar lines or chord lengths.

### Combining sources

- The pages share lyric lines and section names; those match the sources up, line by line.
- Where the chords agree, nothing to decide. Where they differ, the default is the source with tagged chords
  and more votes; the other is kept as an alternative, and the chart editor shows the differences for a
  one-click choice.
- Rhythm guitar comes from the chords page, riffs from the tabs page.

### Timing

1. First guess: 4/4 at the page's tempo, chords spread by their position in the line, repeats expanded.
   Tab timestamps check the bar count (17 s at 112 BPM is about 8 bars).
2. Learn by playing: you play the song once; the listener hears your chord changes. The chord order is known,
   so only the timing has to be found. The result is saved.
3. Chart editor for anything still wrong.
4. Later: align to a recording you own.

### Legal

Ultimate Guitar's terms don't allow automated scraping, and lyrics are licensed content. Import is something
you trigger on a page you have open; songs are stored locally and never redistributed. Ultimate Guitar import
cannot ship in a public product as is. The page format changes over time, so the importer is one isolated
piece of code, tested against saved pages.

## Audio

- Your guitar: Scarlett instrument input → TONE3000 → output. A clean copy goes to the listener and the
  recorder. Target: under 10 ms round trip.
- Band: arrangement → scheduler → instruments:
  - drums: a sampled kit (SFZ);
  - keys: Numa Player (Studiologic, free, installed by you): 47 sounds (grands, upright, Rhodes, Wurlitzer,
    clavinet, B3 organs, strings, pads), 1-3 % audio CPU per take at 64 samples. It has no program list:
    a sound is chosen by giving the plugin a state whose zone names the sound (`engine/tools/numa_state.py`).
    Salamander Grand Piano stays the fallback for a published version;
  - bass: clean sampled bass (for example Karoryfer Growlybass) → TONE3000 with a bass amp capture;
  - band guitar: clean notes recorded from your own guitar → TONE3000.
- Sample player: sfizz, inside the engine.
- Venue reverb (desktop only): convolution with impulse responses of real rooms (OpenAIR). Off in mixed
  reality, where your real room is the acoustics.
- Mixer: band parts, your guitar, click.
- Calibration: a short play-along with clicks measures your input delay.

## Listening, following, bandleader

- Listener: strum onsets; chord recognition against the chord and voicing the chart expects; tuner; level.
- Score follower: where in the chart you are.
- Tempo and phase: smoothed estimate, changes limited in how fast they can happen.
- Bandleader rules: count-in; keep time (practice) or follow (jam); keep a groove going when you stop and
  catch up when you start again; loop a section; speed trainer; fills at section changes; dynamics; endings.
- The bandleader sits behind an interface, so a trained model could replace it later. A language model is
  useful later for speech-to-command and coaching, outside the timing loop.

## Control

- Footswitch: an Arduino with a 32U4 chip (Pro Micro or Leonardo) as a HID joystick. Buttons are mapped in
  settings: start/stop, loop section, previous/next section, push-to-talk, tone preset up/down (TONE3000's
  own presets).
- Musical cues: strum to start (count-in), stop playing to stop or vamp.
- Voice: offline recognizer with fixed phrases ("stop", "from the top", "chorus again", "slower",
  "chart closer"). Push-to-talk on the pedal, or a wake word. Microphone: the Quest's (Virtual Desktop forwards
  it), one on the Scarlett's XLR input, or the PC's.
- Keyboard and mouse on the desktop; controllers in VR layout mode.

## Stations

A station is a musician plus everything that belongs to them, fitted together: the drummer with kit and
throne, the keyboardist with keyboard, stand and bench, the bassist with bass and amp. Venues place stations
on fixed spots; room layouts let you place them yourself. Moving a station never breaks its fit.

## Desktop mode

- Venues: home studio first, then garage, studio, arena; the mockup's living room can be ported. Status and
  asset requirements: [venues.md](venues.md).
- A venue is a Unity scene loaded on top of the band and UI, plus a definition: station spots, your spot,
  camera shots, lighting, acoustics, ambience.
- Camera: orbit, preset shots, optional auto-director. The director uses the scheduler's look-ahead to cut to
  the drummer before a fill.
- UI: a chart strip (current bar large with beat cursor and lyrics, next bar smaller, section map), plus
  panels toggled by key, pedal or voice: chord diagrams (drawn from the voicing data), strumming,
  key/tuning/capo, tuner, tab, mixer and tone.

## VR mode (mixed reality)

- Quest 3 through Virtual Desktop, on Wi-Fi or its USB mode. Virtual Desktop's audio streaming is off;
  you hear everything through the Scarlett.
- Passthrough by chroma key:
  - the camera clears to exactly the key colour, and nothing blends into it: no post-processing, no
    anti-aliasing, hair in cutout transparency, so every pixel is fully model or fully key and no despill is
    needed (E2's desktop check: no edge pixel tinted);
  - the game starts OpenXR itself, only in VR mode, with Virtual Desktop's runtime (VDXR), and without
    OpenXR's pre-initialisation, which would open the system's runtime (SteamVR) at every launch;
  - the key colour is configurable; a check tool renders the band, highlights pixels near the key and
    suggests the safest colour, and shows the values to enter in Virtual Desktop.
- Limits: no shadows on the real floor, opaque panels only, no real-world occlusion.
- Room layouts, one profile per room:
  - layout mode, with controllers in hand: stations move along the floor and turn (life size, upright),
    with footprint outlines and overlap warnings;
  - panels move freely, push and pull, scale, can face you, and anchor to the room or follow your head;
    "keep readable" scales a panel with distance;
  - play mode locks the layout; voice gives small panel adjustments;
  - floor calibration by touching the floor; two-point realignment when tracking has shifted;
  - simple light controls to match the room.

## Band animation

Rocketbox avatars as Unity humanoids. Mixamo clips are retargeted by Mecanim. Hands go onto real targets
(frets, keys, drums) with IK. Clips are time-scaled and locked to the beat, and driven by the band's notes:
the game arranges every part, so each musician's hits, strokes, chord changes and keys are known ahead and
played against the engine's clock (no look-ahead messages needed).
The Blender mockup (`mockup/`) is the reference for fitting and IK: `mocap.py`, `drumming.py`, `comping.py`,
`riding.py`, `fretting.py`.

## Repository

```
capture/   video hand-capture experiments
mockup/    Blender prototype (Python): avatars, instruments, mocap, MIDI band
engine/    C++ audio engine
game/      Unity project
schema/    song format and engine messages
tools/     offline Python tools
docs/      this plan, venues
```

Large third-party downloads are not in git; [CREDITS.md](../CREDITS.md) lists their sources and licences.
Binary files that are in git go through Git LFS (`.gitattributes`).

## Testing

- Core C# logic: unit tests.
- Importers: tests against saved pages.
- Listener, follower and bandleader: recorded sessions (your clean guitar signal plus the clock) replayed as
  tests. This keeps tempo following reliable while it changes.
- Engine DSP: unit tests.

## Licences

Every third-party asset and library gets an entry in CREDITS.md. Allowed: CC0, CC BY (with credit), MIT,
BSD, royalty-free store licences. Not allowed: non-commercial (NC), no-derivatives (ND), editorial use,
models ripped from games. JUCE's licence needs checking before any release.

## Order of work

Experiments first; each tests a risk that could change the plan:

| Experiment | What it proves |
|---|---|
| E1 Audio engine | Scarlett → TONE3000 hosted in our engine → output, latency measured; preset switching from the footswitch; a second TONE3000 instance on a bass sample; footswitch read by the engine |
| E2 Virtual Desktop passthrough | One musician in a Unity OpenXR scene over Virtual Desktop: exact key colour, edges and hair, Virtual Desktop settings, latency; moving and turning with controllers; floor calibration |
| E3 Song import | Both Wicked Game pages merged into one layered song file, first-guess timing checked against the tab timestamps |
| E4 Renderer | One musician in URP and in HDRP, side by side |

Then:

| Step | Contents |
|---|---|
| M1 | Chart and panels |
| M2 | Band at a fixed tempo: style templates, sampled instruments, TONE3000 on band bass and guitar, footswitch and keyboard control, count-in |
| M6 | The band as 3D musicians: Rocketbox avatars on their instruments, every hit, stroke and key played from the band's notes in real time; on the desktop an extra view to switch to (the chart stays the default) |
| M7 | VR: mixed reality with the band in your room by default, room layouts |
| M3 | Listening: tuner, strums, chord check, learn timing by playing |
| M4 | Tempo following and the bandleader |
| M5 | Voice commands |

On 2026-10-22 the order changed: the band in VR (M6 and M7) comes before listening, tempo following and voice
(M3-M5), which wait. M6 needs no venue: the desktop's band view is a plain stage, and in VR your room is the
venue. The band's motion is computed in the game (C#), from the arrangement it already has, not baked in
Blender per song: any song, style and tempo then works. Blender stays an offline asset step (the instruments'
meshes and playing geometry, `tools/band_assets/`).

Later: more venues, guitar tracking with ghost hands, a teacher avatar, a language-model coach, alignment
to recordings.

## Status

- 2026-10-21: plan written, repository set up; home studio chosen as the first venue.
- 2026-10-21: E1 started (`engine/`): JUCE 9 engine on Focusrite USB ASIO hosting TONE3000 for the guitar
  (its presets switched from the footswitch) and for a test bass line, plus a footswitch reader. Reported
  round trip 7.8 ms at a 64-sample buffer (5.5 ms at 32). Played through real tones: preset switching quick,
  audio CPU about 25 % (peak 28 %) of a 64-sample buffer with the guitar and bass amp models running.
  Speakers: the ADAM D3Vs are USB speakers, so the game's audio needs them on the Scarlett's line outputs
  (one device, one clock) or headphones on the Scarlett. Still open: the true round trip (loopback cable).
- 2026-10-21: Numa Player checked with `plugin_probe` (engine/tools): plays when hosted once given a state
  with a sound; grand, upright, Rhodes and rock B3 rendered; 1-3 % mean audio CPU, peaks 6-18 %.
  Unity 6.6 (6000.6.7f1) and the Unity CLI (1.0.0-beta.13) installed.
- 2026-10-21: E4 run (`experiments/e4-renderer/`): one guitarist at Quest 3 stereo resolution takes 1.5 ms in
  URP and 2.1-2.7 ms in HDRP; HDRP's skin looks clearly better; Unity keeps HDRP in maintenance mode and develops
  URP. Unity MCP server registered for Claude Code (`unity-editor-mcp`).
- 2026-10-21: URP chosen. `game/` created (Unity 6.6, URP, agent package): `GuitarTutor.Core` assembly without
  Unity references holding the tempo map and the layered song model with JSON files, and 13 passing EditMode
  tests (`game/run_tests.cmd`).
- 2026-10-21: E3 done: Ultimate Guitar importer in `GuitarTutor.Core` (`Import/UltimateGuitar/`), command line
  `tools/songtool`. Wicked Game from both pages: 133 bars, 4:45 against the album's ~4:49; the intro's guessed
  0:34 matches the tabs page's 0:00-0:34; all lyric lines matched, 25 riffs placed. 20 tests pass. Imported songs
  and saved pages live in `songs-local/` (not in git).
- 2026-10-21: E2 app built (`experiments/e2-passthrough/`): musician on an exact #FF00FF background, no post or
  anti-aliasing, cutout hair; controller placement, floor calibration, key colour switching; XR started by the app.
  Desktop check: background exact, no edge pixel tinted towards the key. Headset test with Virtual Desktop: yours.
- 2026-10-21: M1 done: the desktop screen in UI Toolkit (`game/`, see its README). Chart strip (section map,
  current bar with beat cursor plus three bars ahead, chords over a band per chord, words at their beats, the
  lyric line karaoke style and the next line), panels (chord diagrams from the voicings, strumming with the
  stroke lit, tab for the current and next bar, song info), internal clock with count-in, practice speed,
  keyboard transport. Checked by screenshots from the built player (`-shot`, `-keys`), with the demo song and
  Wicked Game. 34 core tests.
- 2026-10-21: M2 done: the band at a fixed tempo. The game arranges drums, bass and keys from the chart in
  three styles (Ballad, Rock, Pop). The engine plays them on the song's tempo map with a count-in on sticks,
  and owns the transport (play, pause, seek, speed); the game reads its clock from shared memory
  (`schema/engine-protocol.md`). Drums and bass are synthesized for now (bass through the TONE3000 bass amp);
  keys are Numa Player with a sound per style. Band mixer with a soft limiter. Footswitch buttons 3-6 go
  to the game. Checked without a sound (`engine --silent --record`, `tools/band_session.py`): hits on the
  beat grid, count-in, pause, seek, resume, 110 % speed, style switches while playing; audio CPU 2-4 %. Still
  open: a sampled drum kit and bass, the band guitar, and the band rendered ahead on its own thread.
- 2026-10-22: the band on plugins: MONSTER Drums, MONSTER Bass or Ample Bass P Lite (the user installs it:
  private use only), chosen and switched in the control panel without a gap in the band. The band guitarist
  strums the song's patterns on its chord shapes (G in the game, or the control panel) through its own amp
  tone, on MONSTER Guitar, Kontakt with Shreddage 3 Stratus FREE, or a synthesized guitar.
- 2026-10-22: M6 under way: the band as 3D musicians in a band view on the desktop (V; the chart alone stays
  the default). Rocketbox avatars on the Mixamo playing clips, locked to the beat; the instruments from the
  Blender mockup with their playing geometry (`tools/band_assets`). Computed in C# from the arrangement: the
  drummer's sticking and strokes onto the drums and cymbals, pedals; the guitarist's grips fretted and strums
  over the strings; the bassist's frets and plucks; the keyboardist's fingers pressing the keys.
- 2026-10-23: M7 started: VR mode (`-vr`, or X). The band in your room by default, the chart on an opaque
  board like a music stand. Room layout with the controllers: the trigger moves the board, or one musician
  along the floor (a ring shows their station, red where it crowds another's), the grip the whole band; the
  stick sizes the board or pushes, pulls and turns. A / B / sticks run the transport, floor calibration and
  key colour on the left controller; all remembered. Checked on the desktop: without a headset VDXR answers
  "no headset" at once and the room is shown on the desktop; every background pixel exactly #FF00FF, no edge
  pixel tinted; the controls driven by a scripted headset and controllers (`-vrscript`); a real XR session
  (Direct3D 11, focused, band and board in the mirror view) against Unity's OpenXR mock runtime. Headset
  test: yours.
- 2026-10-23: band animation brought up to the mockup after the first headset test: the mocap clips now play
  (they had not: a wrong animator state name left every musician on the clip's first frame, with only the IK
  moving); the guitarist's and bassist's instruments hang ahead of the player's own belly, high enough that the
  picking elbow flares out and the forearm rests on the body's upper corner, follow part of the chest's sway,
  and have a strap; the bassist is straightened as the mockup does; the strumming and plucking hands keep the
  captured pick grip; the drummer's throne is where his right foot reaches the kick pedal exactly (hands and feet
  reach every target, 0 mm), his elbows follow the capture, and the hi-hat opens on the and of 4 (Rock, Pop).
  Still different from the mockup: the bass roots are not moved to frets 5-10 (long reaches at the nut), and the
  three styles differ little in how the drums look.
