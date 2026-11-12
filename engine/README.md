# GuitarTutor audio engine

The C++ audio engine (see [../docs/plan.md](../docs/plan.md)). It owns the audio interface, hosts the TONE3000
plugin for your guitar and the band guitarist's, plays the band, and will grow the listener and the recorder.

What it does now:
- Your guitar through TONE3000, with TONE3000's own presets switched from the footswitch (E1).
- The band (M2): it plays the parts the game arranges, on the song's tempo map, with a count-in: drums, bass
  and a rhythm guitarist (built-in or on instrument plugins), and the keys on Numa Player. The band owns the
  song's transport. The game asks it to play,
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
   appear in the preset list; choose one there, or step through them with the footswitch.
4. Footswitch: button 1 next preset, button 2 previous preset. Buttons 3-6 go to the game: play / pause,
   previous section, next section, restart. Any game controller works as a footswitch until the Arduino
   pedal exists.

Tone switching uses TONE3000's presets: it builds the new model in the background and fades its output across
the swap (25 ms fades), so a switch in a pause between phrases is not heard. An instance runs its amp model
whether you play or not, which is why there is one guitar instance rather than one per tone.

Device settings and the TONE3000 instance (with its tone) are remembered in `%APPDATA%\GuitarTutor\GuitarTutor Engine.settings`;
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

The control panel's "Song" list (above "Guitar input") shows the songs the game can open, by file name without
the suffix (`heart-of-gold-gpx-ug`, `wicked-game`, ...; the file name tells the version, the log's `Song:` line the
origin), and the one that is open. Choosing one asks the game to open it (`songs` and `openSong` in
`schema/engine-protocol.md`). The list is the game's, so it is empty until the game is connected.

The **Import new...** button next to the list opens the import window (`src/ImportWindow.cpp`). With "Guitar Pro" checked
it takes a Guitar Pro file (`.gpx`, `.gp3`, `.gp4`, `.gp5`, `.gp`) and, optionally, the URL of an Ultimate Guitar chords
page whose strumming the song uses; unchecked, it takes an Ultimate Guitar chords page URL and/or a tabs page URL (at least
one). **Import** runs `songtool import` (`tools/songtool`: `songtool\songtool.exe` next to the engine, or the repository's
build of it, `dotnet build tools/songtool -c Release`) and shows what it prints in the window's log; the pages are read
from their URLs in memory and the URLs go into the song's sources. When the song is written to `songs-local` the engine
sends the game `importedSong`, which reads its song list again and opens the new song, and the list here follows.

Below, a row per band instrument: **Rhythm guitar**, **Bass**, **Keyboards**, **Drums**, **Lead guitar** and
**Instrument 1** (docs/instrument-tracks.md). Each row's first list is the song's track the instrument plays, from the
game (`tracks`): "Band playstyle", the part the band makes up in its playstyle (the lead guitar and instrument 1 have
none: "None", they play no track), then a Guitar Pro song's tracks (a doubled track is one choice, named with its twin).
The game connects them automatically for a new song; choosing one here tells the game (`connect`), which keeps the
choice in a file next to the song (`<song>.band.json`) and arranges the band again. A song from Ultimate Guitar pages
has no tracks: the lists only offer the first. Then, for an instrument playing a track (the leads always), the row's
plugin and its window ("..."): the sound it plays the track on in this song. On "Band playstyle" they are hidden: the
playstyle's sound plays, set in the Band playstyle defaults window. The keyboards and the leads have no built-in sound,
so their first choice is "Off". Every instrument plays its track as the file has it (its velocities, no made-up fills).
The rhythm guitar plays its track one way, strummed or fingerpicked alike: the chart's chord is held as the track
strikes it, and every note is its string struck on Shreddage with a string key, so each string rings until it is
struck again, or until a moment before its chord ends (the bass rings under Redbud Tree's pinches). Strings struck
together are crossed as a hand does (down low to high, up high to low, a stroke's width apart; down or up as the file
records it, else down on the beat and up off it), and a strum on dead or palm-muted strings is a short chuck; with the
file's techniques (below). It needs Shreddage's Strumming Mode off, Keyswitchable on and its voice limits raised (32
and 16, in Kontakt's instrument options): too few voices left Wicked Game's D chords silent. A string key's velocity
changes its level only 2-3 dB, so a track's accents are fainter than a strum keyswitch's. (Until 2026-11-01 the row
offered three ways: strummed with the strum keyswitches, which hold only the strings a strum strikes, so the next pinch
cut the bass; picked note by note; and string by string, as an experiment.) A song strummed from an Ultimate Guitar
page keeps its guitarist on that strumming (Default) unless you choose the track.

Play from the game (Space) or the footswitch. Playing from the start, or from wherever you moved to, begins
with a bar of count-in on sticks; after a pause the band plays on from where it stopped.

- The game writes the parts (`game/Assets/GuitarTutor/Core/Band/Arranger.cs`) in one of three styles:
  Ballad (the default below 120 BPM), Rock and Pop. B switches style in the game.
- Two lead channels ("Lead 1", "Lead 2", MIDI channels 4 and 5) play the song's other instruments (a Guitar Pro
  file's harmonica, lead guitar...) as stand-ins on a plugin of your choice: Numa Player or Kontakt (any of its
  instruments; each lead is its own instance), or "Other plugin...". They have no built-in sound and are off
  until you choose a plugin; a lead on Numa Player starts with the keys' sound (change it in its window). The
  load message carries their notes as parts 4 and 5 and `leads`, their names, which the log shows. In the mixer
  "lead1" and "lead2". `tools/lead_session.py` records a silent test of them (`--lead1`, `--lead2` on the
  engine's command line choose the plugins).
  The tab's single-string notes (a riff the guitarist plays as notes, not strums) go to Lead 1 when it has a plugin
  and the song gives that channel no part of its own (a UG song with a tab, not the GPX Heart of Gold, whose Lead 1
  is the harmonica): the guitar plugin then only strums and keeps quiet while the tab plays, so the two do not
  compete for its voices. Without a Lead 1 plugin, or with Lead 1 muted or its volume all the way down, the guitar plays them as
  before. `lead_session.py --tab` tests it. A lead's or the guitar's note that the same key follows at once is let go 60 ms
  early: a plugin takes the repeat for the same note held on, with no new attack (Numa Player, a D2 in eighths: back to
  back the repeats rose 2-5 dB, 60 ms apart 13-16 dB; Shreddage choked a fingerpicked part's repeats 10 ms apart).
- The band's drums and bass each play their built-in sound or an instrument plugin, chosen in the control
  panel's "Drums" and "Bass" rows, also while the band plays:
  - Built-in sound: a synthesized kit, and a finger-played bass string built from its harmonics
    (`src/Instruments.*`). The count-in sticks are always the built-in ones.
  - The plugins the engine knows, if you have them: `MONSTER Drums*.vst3`, `MONSTER Bass*.vst3` and
    `ABPL*.vst3` (Ample Bass P Lite), in the VST3 folder or in `monster_plugins/Library files/` next to the
    repository.
  - "Other plugin...": any VST3 instrument (a plugin that takes no notes is refused); it stays in the list.
  - "Pick another sound in MONSTER ...": a MONSTER plugin shows its sound picker only while it has no sound,
    so this starts it anew and opens its window (only for MONSTER plugins: Kontakt and Numa Player choose theirs in
    their window, "..."; the Band playstyle defaults window's MONSTER cells have it too). A MONSTER plugin is silent until you choose a kit or a bass
    in it (each is one `.mse` file from its library folder); the built-in sound plays until you close its
    window with one chosen.

  "..." opens the plugin's own window. The engine remembers your choice and each plugin's sound
  (`bandBass.<plugin file>` in the settings). MONSTER Bass takes about 7 s to load and 5 s to let go of its
  samples; the control panel waits meanwhile, while the band plays on. The band sends each part on its own
  MIDI channel (keys 1, bass 2, drums 10) and each plugin hears only its own.

  **Sounds per song** (docs/instrument-tracks.md, step 7). Each song keeps, per band playstyle (Ballad, Rock, Pop),
  the plugin and its state the rhythm guitar, bass, keyboards and drums play their playstyle part on, and per
  instrument the one it plays a track on (the leads only play tracks) (`%APPDATA%\GuitarTutor\songs\<song>.sounds`).
  A song opened the first time gets a copy of the saved playstyle defaults (`%APPDATA%\GuitarTutor\band-defaults.sounds`;
  none saved: the built-in sounds, the keyboards on Numa Player with each playstyle's sound); an instrument playing a
  track the first time starts on its playstyle part's sound (a lead on the plugin chosen for it last). Choosing a
  plugin in a row, or closing its window, keeps it for the song's track. **Band playstyle defaults...** (on the Band
  style line, after its list of the game's playstyles: choosing one asks the game to play the song in it, `style`) shows the song's table, a plugin dropdown and its "..." per playstyle and band member: "..." puts the
  instrument on that playstyle's sound while its window is open; closed, the sound is kept and the instrument goes back
  to what it plays. The game sends `tracks` before `load`, so the engine knows which instruments play a track. "Save defaults" saves the table as
  the defaults; "Load defaults" gives the song the saved ones in place of its own (it asks first; the sounds its
  tracks play and its mix stay); "Ok" closes it. The game names the song and lists the playstyles in `load`; the engine loads only what
  differs from what plays, then answers `loaded` (the game shows its loading ring if that takes a moment). A silent
  engine reads the kept sounds but writes none (`src/SoundStore.*`). `--playstyles-test` opens the table 8 s after
  the start (with `--snapshot`, a picture of it).

  Drum notes are General MIDI. A drum the kit in use does not have is played on the nearest one it has (`Band.cpp`,
  `drumStandIns`: an acoustic bass drum 35 on the kick 36, a floor tom on another tom, a china or splash on a crash,
  ride 2 or the ride bell on the ride, a shaker on the closed hat, claves on the side stick, and hand percussion where a
  drummer plays it on a kit: congas, bongos and timbales on the toms, a cowbell or a triangle on the ride's bell, a
  tambourine on the open hi-hat); anything else (a whistle, a cuica) is left out. The built-in kit has 12 drums (kick,
  side stick, snare, closed, pedal and open hat, three toms, crash, ride, sticks); MONSTER Drums plays every General
  MIDI drum from 35 to 57 but the high tom 50, the tambourine 54, the splash 55 (in the kit in use; a standard kit
  had one), the cowbell 56 and ride 2 59, and nothing above (`plugin_probe --map`, 2026-10-29 and 2026-11-01); any
  other drum plugin is taken to play them all. This matters most for a song's own drum track (docs/instrument-tracks.md),
  which uses whatever drums the file has.
- The band guitarist (switched on and off with G in the game; remembered) strums the song's strumming pattern for each section on the chord shapes the chart shows (the
  game writes it: `Arranger.Strum`): a stroke crosses the strings 12 ms apart down, 9 ms up (the top four
  strings), and each string rings until it is struck again or the chord changes. It plays on the built-in
  guitar (picked strings built from their harmonics, `GuitarVoice`, a DI sound straight to the mixer) or a
  plugin with an amp of its own, straight to the mixer: MONSTER Guitar (its "[Notes]" instruments; a "[Chord]"
  one plays a whole chord for each string's note) or Kontakt with Shreddage 3 Stratus FREE. (The band guitar's
  own TONE3000 amp, with its tone list and the "through band guitar tone" switch, was removed on 2026-10-29: the
  band guitar plays on a plugin.) Guitar plugins get
  the notes as they sound (MONSTER Guitar: note 40 is E2), but a note below D#2 (Shreddage's lowest: a drop D song's
  low D2) goes an octave up, and a chord note that lands on another of its chord that way is played once.
  On Kontakt the band plays Shreddage in its strumming mode: the chord goes to it as a held voicing (silent
  there) and each stroke as a strum keyswitch (down G#5, up A5). The strumming-mode keyswitch F#5 is held
  automatically (let go on pause, seek or a new score). Where the song's tab has single-string notes (an onset
  of one note: the load message's `spans` say where, the notes carry a sixth element, 1) the strumming mode is
  let go for the stretch (the chord's strings and the strum key first, then F#5), the notes play as ordinary
  notes, and at the stretch's end F#5 goes down again and the chord held then is re-sent. The rhythm guitar playing
  a track sends `picks` instead of strokes: each note is a string key, F#6
  (90) plus the note's place in the held chord (`rank`, 0 its lowest note), held as long as the string rings
  (until it is struck again, 10 ms before, or until 70 ms before its chord ends). Measured with plugin_probe on
  Redbud Tree's ten chords, also after a chord change: key 90 + k plays the k-th lowest held note (the manual
  says the keys pick strings, lowest first, another one where a string holds no note; the plugin's own voicing
  makes that the k-th note); a held key rings its string, letting it go damps it; a key struck again 2 ms after
  its release attacks in full; its velocity hardly changes the level (2-3 dB from 20 to 127), but every other
  press is about 5 dB softer (Shreddage's alternate picking). So the bass rings under the pinches, which a
  strum cuts. Measured with plugin_probe: the plugin rings the
  strummed chord at full level for as long as the strum keyswitch is held and damps it (a fast release) when
  the key is let go, so the band starts each stroke exactly on its beat and holds its key until the next stroke starts
  (back to back, no gap), or 1.2 s at most; the last stroke of a chord lets go 70 ms before the chord ends, for the
  hand's change to the next chord (a held key sustains the chord at full level,
  so a longer hold rings it like a synth); the key must be held at least 80 ms (below ~40 ms the sweep is
  partial or silent, and a zero-length key plucks only one string on an up-strum). A chord change damps the
  active strum early, as a hand moving to the new chord would (a clean stroke rings about 1.1 s to -25 dB).
  A muted stroke adds the Mute keyswitch C6 around the strum (a few samples before it, let go 40 ms after):
  a fast chug (about 0.5 s to -25 dB) (C#6 would choke it; the song model has no choke yet). It needs the strumming-mode setup in the Shreddage
  window (Strumming Mode off + Keyswitchable on, so F#5 switches the mode; Strum Speed/Volume from velocity)
  and the TACT Mute articulation left off (an enabled Mute damps every strum). The strum's pick lands about 13-20 ms after its
  keyswitch (its chord body builds for another 100 ms or so, as a strummed guitar's does), so the band sends
  Kontakt its guitar 20 ms early and the pick lands just after the beat (`bandGuitarAdvanceMs` in the
  settings changes it; 0 turns it off; the log says "plays 20 ms ahead" when it is on). The band has a cursor
  of its own for the guitar's notes, so this works with seeking and the count-in. Shreddage's own
  keyswitches, effects and MIDI channel are saved with Kontakt's state: if the guitar is silent or muted,
  look at its window first.
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
- Pitch wheel (docs/instrument-tracks.md, step 9): the load message's `wheel` gives a part's bends as points in
  semitones; the band sends the part's plugin the wheel at each point and, while it moves between two, once a
  block (only when its value changes), before the block's notes so a bend at a note's start is there as it starts;
  the guitar's are early by its plugin's lag, as its notes. Stop, seek and a new song put the wheels back in the
  middle. Measured with plugin_probe `--bend` (2026-10-31): Shreddage, MONSTER Bass and Numa Player all bend 2
  semitones each way (linear, at once; a string key's pick too), so the engine scales 2 semitones to the wheel's
  end and a bend past it stops there; Ample Bass P Lite does not bend at all.
- Velocities (step 9.4): the drums, the bass and the keyboards play a track's velocities as the file has them (its
  dynamics, accents and hairpins). Measured with plugin_probe `--vels` (2026-10-31, RMS of each hit): MONSTER Drums'
  kick is 15 dB quieter at p (47) than at f (95), its snare 23 and its hi-hat 14; Ample Bass P Lite 6, MONSTER Bass 10.
  Louder only, not choked or faint as Shreddage was at soft velocities (the rhythm guitar has a mapping for that), so
  they get none.
- Techniques (step 9.3): the lead guitar, instrument 1, the bass and the rhythm guitar playing a track bend as the file
  draws it, wobble on a vibrato (about 5.5 times a second, a quarter semitone, half one wide), glide on a slide (2
  semitones at most), and play a hammer-on's, pull-off's or legato slide's next note softer, a palm mute shorter and
  softer, a staccato note half as long, a dead note as a short soft click and tremolo picking as repeated notes. The
  bass bends only where nothing else of it rings (the wheel bends a whole channel); the rhythm guitar lets go of the
  strings ringing as a bent or sliding note is struck, and that note rings for its own length. A dead note alone is a
  click on the guitars that play notes, and left out on Shreddage (a string key sounds its string in full). A palm
  mute also sends controller 20 (127 as the note starts, 0 as it ends): the free Shreddage has the Mute articulation
  but not its keyswitch, so in its window's Articulations, Map tab, give Mute the rule "CC20 64-127" (and keep it on)
  for the real palm-muted samples; without it the controller does nothing. The rule is kept in Kontakt's state, which is
  a song's sound (and the playstyle defaults' when saved).
- Mixer: each part's sound has a level (drums 0.8, the built-in bass 1.75 or the bass plugin's own, keys set
  by the game per style, the built-in guitar 1.75 (a DI sound, not measured) or the guitar plugin's own),
  then the instrument row's mixer channel (docs/instrument-tracks.md, step 8): M (mute), S (solo: only the soloed
  band instruments play; your guitar always does), a volume knob (0 dB is the balanced mix, up to +6 dB;
  double-click for 0 dB again) and a pan knob, then a soft limiter on the band's sum (peaks above 0.7 bend towards
  1). Volume, pan and mute are kept with the song (its `.sounds` file, `mix`), solo is not; a song not mixed yet
  starts at the levels the faders had before (`fader.<part>` in the settings). Pan is equal power with the middle
  at the part's level (a side up to +3 dB). An instrument pans from its musician's place on stage as the audience
  hears it (the game's `stage`: 45 degrees off the middle is all to one side; the rhythm guitar about -0.43, the
  bass +0.38, the keys +0.56, the drums the middle; the leads have no musician: the middle) until you turn its knob;
  turning it back to that place (double-click) follows the stage again, and "Pan from stage" puts them all back.
  Measured (silent, the rhythm guitar alone, Kontakt): from its place on stage, 6.3 dB louder left than right (the
  pan law gives 6.4).

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
makes the engine save a picture of its control panel 3 s after it starts. `--connect-test <instrument>:<track
index>` picks a track in an instrument's row 6 s after the start, as you would (`instrument1:0`; -1 for the default part),
and with `--snapshot` saves `<file>.connect.png` of the panel after it. (Passing several engine options through the game's
`-engineargs` from Windows PowerShell 5.1: put the whole value in quotes inside the string, `'"--silent --snapshot x.png"'`;
Start-Process does not quote it.)

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
`--sweep-test` (with `--state`) strums a G chord in Shreddage's strumming mode, `--n` strokes `--gap` beats apart
(`--dirs dduu`, `--keys` hold lengths in ms, `--vels` velocities): each stroke's peak, RMS and time to its peak. Measured
2026-10-30 on the band guitar's sound: down-strums at velocity 40 to 127 differ by under 2 dB, and not steadily.
`--voicing 50,57,62` holds another chord, `--press 90,91` presses those keys instead of strums (the string keys), and `--at 5,5.02` gives each its own beat;
with them the string keys were measured (above), and that a Shreddage state short of voices drops the oldest note, the
ringing bass, when several strings are picked at once (Kontakt's instrument voice limit).
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
| `src/SoundStore.*` | The band's sounds kept per song and per band style (JSON files under `%APPDATA%\GuitarTutor`) |
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
