<div align="center">

# 🎸 YourBand

### A jam track player and band that plays along with you.
![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?logo=windows&logoColor=white)
![Unity](https://img.shields.io/badge/game-Unity%206%20(URP)-000000?logo=unity&logoColor=white)
![JUCE](https://img.shields.io/badge/audio%20engine-C%2B%2B%20%2F%20JUCE%209-8DC63F)
![Plugins](https://img.shields.io/badge/band%20plugins-free-orange)
![VR](https://img.shields.io/badge/VR-Quest%203%20via%20Virtual%20Desktop-7B61FF)
![Licence](https://img.shields.io/badge/licence-GPL%20v3-blue)

Load a song from Ultimate Guitar Chord and/or Tab pages. Or a full-fledged Guitar Pro file. A drummer, a bassist, a keyboardist and a guitarist play the song with you, on real instrument plugins, in front of you on the screen or in your own room in (optional) mixed reality VR.

Or skip the band visuals, and focus solely on the chords with strumming patterns or tabs for lead, riffs and solos. All displayed on screen with easy to use controls and navigation.

Plug in your guitar or any other instrument and play along. Mute unwanted instruments or tracks, select instrument sounds from *real* studio plugins with exquisite quality. All based on open software and free studio plugins. A full studio band, for you, for free (except Virtual Desktop for mixed reality, see below).

<img src="docs/images/band-view.png" alt="The band view: a guitarist, a drummer, a bassist and a keyboardist playing behind the chart" width="900">




<br><br>

| [![Watch the video](https://img.youtube.com/vi/R3rfzmOm-Lc/hqdefault.jpg)](https://youtu.be/R3rfzmOm-Lc) | [![Watch the video](https://img.youtube.com/vi/kkk0E0CPo9E/hqdefault.jpg)](https://youtu.be/kkk0E0CPo9E) |
| -------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| Demo song (YouTube)                                                                                      | VR mixed reality (YouTube)                                                                               |                                                                                                          |                                                                                                          



YourBand is a practice tool and jam partner with a focus on guitarists, but you can play any instrument. Open a song, count in, and the band
is there: **the drummer hits what the song's drum track says, the bassist walks the bass line, the keyboardist
plays the piano part, the rhythm guitarist picks the real fingerpicking pattern**. Your own guitar goes through an
amp plugin (e.g. TONE3000) in the same program, so you hear yourself and the band together with a few milliseconds of latency. Or play with an external amp, or an acoustic guitar.


</div>

## ✨ What it does

- **Import a Guitar Pro file** (`.gp`, `.gpx`, `.gp3`-`.gp5`) and assign instruments
  to play its tracks: drums, the bass line, the keyboards, the guitar strums chords or picks string by string for fingerpicked techniques, leads with their bends, slides, vibrato, hammer-ons and palm mutes. Each instrument has a dropdown for its track; the game connects them sensibly by itself, and you can change them if needed.
- **Import from an [Ultimate Guitar web page](https://www.ultimate-guitar.com/)**: the application interprets the UG web page for the chords with strumming patterns and/or tabs, and lays out the song chart, ready to play along. The band plays its parts (drums, bass, rhythm and keyboards), from the chords and the strumming patterns decoded from the UG page, in one of three **playstyles** (Ballad, Rock, Pop), and counts you in on sticks.
- **A chart you can play from.** Chord diagrams drawn from the song, the strumming pattern with the stroke lit as it is played, a *scrolling tab* with bar numbers, lyrics, a section map to navigate with, speed adjustments in 5 % steps, and a matrix overview (`M`) of which track (instrument) plays in which bar.
- **Your guitar, your amp.** The engine hosts the free [TONE3000](https://www.tone3000.com/plugin/download) amp plugin featuring the highly acclaimed NAM A2 neural amp profiles
  for your guitar and switches its presets with a hotkey `T`, any USB game controller or a VR controller. An external ordinary amp can also be used.
- **A 3D band** (an optional download). Four musicians play their instruments behind the chart (`V` hides or shows them): the drummer's sticking on the drums and cymbals, the guitarist's fingers on the real frets, the bassist's plucks, the keyboardist's keys.
- **Mixed reality.** In VR (Quest 3 through Virtual Desktop) the band stands in your room and the chart hangs on a board like a music stand; you place them with the controllers.
- **A real mixer.** Mute, solo, volume and pan per instrument (pan follows the musicians' places on stage), and a sound of its own for every instrument, playstyle and song.

<div align="center">

<img src="docs/images/desktop.png" alt="The desktop screen: song info, chord diagrams, strumming pattern, tab and the chart" width="900">

*The desktop screen with the built-in demo song: chords, strumming, tab and the chart.*

</div>

## 🚀 Get started

### 1. What you need

- Windows 10 or 11, 64-bit.
- If using the internal amp plugin (TONE3000), an audio interface with an **ASIO** driver is strongly recommended: the author measured a 7.8 ms round trip with a Focusrite Scarlett Solo at 48 kHz and a 64-sample buffer.<br> 
Windows audio works perfectly well if you don't need the amp plugin, for playing along to the band with an acoustic guitar or with an external amp.
- For (optional) VR: a Quest 3 and [Virtual Desktop](https://www.vrdesktop.net). VD provides the passthrough: it makes the magenta chroma-key backdrop fully transparent, so the band appears in your room. VD is the only part of this setup that isn't free, but it's well worth its cost if you play other PCVR games too, IMHO. There are other passthrough solutions for SteamVR and OpenXR online, but none have been tested here. 
- A few free accounts are needed to download some of the free plugins and Native Instruments.
- A guitar (or any other instrument you want to practice with).

### 2. Download YourBand

Get the latest zip from the [Releases](https://github.com/barsk/YourBand/releases) page (or build it yourself: [Build from source](#-build-from-source)),
unzip it anywhere, for example to `Documents\YourBand`, and start `Game\YourBand.exe`. The game starts its audio
engine (`Engine\YourBand Engine.exe`) by itself. Nothing else needs installing for
the game and the engine; the band plays on plugins you install yourself. Without any plugins the band plays its built-in sounds: a synthesized drum kit, bass and guitar. The keyboards have no built-in sound, so they are silent until Numa Player is installed. The plugins are what make it sound like a real band, so install them next. 

**The 3D band is a separate, optional download** (about 100 MB): the Releases page has a *band pack* zip.
Unzip it into the release folder (the one with `Game` and `Engine` in it); it adds `Game\Band\band.bundle`, and the band now shows when the game starts (`V` hides it).
Without it the game is exactly the same, minus the band view: the music, the chart and the audio band do not need it.

### 3. Install the plugins

Only 64-bit VST3 plugins are supported. All of these are free. They are not bundled: each one has its own licence, and you install them yourself.

| Instrument | Plugin | Where to get it |
|---|---|---|
| Rhythm guitar | **Kontakt 8 Player** with **Shreddage 3 Stratus FREE** | [Native Access](https://www.native-instruments.com/en/specials/native-access/) installs both; see below |
| Bass | **Ample Bass P Lite** (or MONSTER Bass) | [Ample Sound](https://www.amplesound.net/en/pro-pd.asp?id=19) |
| Drums | **MONSTER Drums** | [Monster DAW on KVR](https://www.kvraudio.com/developer/monster-daw) (download links on the product pages) |
| Keyboards (and extra instruments) | **Numa Player** | [Studiologic](https://www.studiologic-music.com/products/numaplayer/) |
| Your guitar | **TONE3000 plugin** | [tone3000.com/plugin/download](https://www.tone3000.com/plugin/download) |

VST3 plugins go where their installers put them, `C:\Program Files\Common Files\VST3`; YourBand looks there. The MONSTER plugins come as a folder with the plugin and its sound library: put it in a folder named `monster_plugins\Library files\` next to the `Engine` folder, or install its `.vst3` in the VST3 folder.

#### Kontakt 8 Player and Shreddage 3 Stratus FREE (the rhythm guitar)

Shreddage is a sampled electric guitar that lives inside Native Instruments' free Kontakt Player. This is the longest install; the guitar is worth it! The application uses its keyswitches to dynamically play the plugin guitar beyond static MIDI tones with vibrato, bends, slides, palm mutes and muted strums (see [how it works](docs/shreddage-keyswitches.md)).

1. Download and install **[Native Access](https://www.native-instruments.com/en/specials/native-access/)**, Native
   Instruments' installer, and sign in with a free Native ID.
2. In Native Access open **Library**, then **Available**, choose the **Kontakt** category and install
   **Kontakt 8 Player** ([its free download page](https://www.native-instruments.com/products/kontakt-player)
   has the details). If you already own the full Kontakt 8, skip this step: it plays Shreddage too.
3. Go to the **[Shreddage 3 Stratus Free](https://impactsoundworks.com/product/shreddage-3-stratus-free/)**
   page at Impact Soundworks, add it to the cart (the price is $0; a tip is optional) and check out. You receive a serial number. Make sure it is the 3.5 version!
4. Add the serial in Native Access. **Shreddage 3 Stratus FREE** (about 1 GB) appears under your library; install it. (You may have to press the refresh button.) 

Kontakt then shows up as a VST3 plugin for the application.

### 4. First start: audio and your guitar

1. Start `Game\YourBand.exe`. The first demo song, *Highway Boogie*, is on screen. Press `Space` to count in and play.
2. Press `C` to open the engine's **control panel**. Choose your audio **Device** (your interface's ASIO
   driver, or Windows audio Low Latency if you don't want or need the guitar amp plugin), a 48 kHz sample rate and a small buffer (64 samples; raise it if you hear crackling). Pick your
   guitar's input channel in **Guitar input**.
3. **Guitar tone...** opens the TONE3000 plugin. Load an amp model and a cabinet (browse tones at
   [tone3000.com](https://www.tone3000.com)), and save them as presets: they also appear in the list beside the button, and the `T` key, a USB game controller, a VR controller or (coming soon) a footswitch steps through them.

<div align="center">

<img src="docs/images/control-panel.png" alt="The control panel: audio device, guitar input, band style, a row for every instrument with its track, plugin, mute, solo, volume and pan" width="560">

*The control panel (`C`): a row per instrument with the song's track it plays, its plugin and its mixer controls: mute, solo, volume and pan.*

</div>

### 5. Set up the band's sounds
> [!IMPORTANT]
> The settings for `Strumming mode`, `Keyswitchable` and `Voice Handling` absolutely need to be set as instructed below. The application will not be able to play the guitar correctly otherwise!

Every instrument has a sound for each playstyle (Ballad, Rock, Pop). Set them once, save them as the defaults, and every song you open starts with them.

1. In the control panel click **Band playstyle defaults...** (on the *Band style* line). A table opens: a row per
   band member, a column per playstyle, and in every cell a plugin dropdown and a **...** button for the plugin's own
   window.

   <img src="docs/images/playstyles.png" alt="The Band playstyle defaults window" width="560">

2. Choose a plugin in each cell: *Rhythm guitar* **Kontakt (Shreddage)**, *Bass* **Ample Bass P Lite**,
   *Keyboards* **Numa Player**, *Drums* **MONSTER Drums**. Numa Player starts on a sound chosen for each playstyle,
   and **Ample Bass plays at once**.
3. **MONSTER Drums and MONSTER Bass are silent until you choose a kit or a bass in them.** Click the **...**
   beside them, pick one in the plugin's window, and close it. (If the sound picker has gone, choose "Pick another
   sound in MONSTER ..." in the cell's dropdown.)
4. **Shreddage needs these settings.** Click **...** beside the rhythm guitar's **Kontakt**. In Kontakt open the
   *Libraries* tab and load the Shreddage 3 Stratus FREE instrument, then set:

   | Setting | Where | Value |
   |---|---|---|
   | Performance Style | Shreddage, *Main* tab, Performance Controls | **Rhythm** |
   | Fretting Mode | Shreddage, *Main* tab, Fretting Controls | **Polyphonic** |
   | Strumming Mode | Shreddage, *Strumming* tab | **Off** |
   | Keyswitchable | Shreddage, *Strumming* tab | **On** |
   | Voice limits | Kontakt, *Instrument Options* (the wrench in the instrument's header), *Voice Handling* (the names can differ a little between Kontakt versions) | **Standard 32, High Quality 16** |
   | Palm mutes (optional) | Shreddage, *Articulations* tab, *Map* | give **Mute** the rule `CC20 64-127` |

  The band plays Shreddage's strings one by one and rings them like a guitarist's hand, so Strumming Mode **has to be off** (notes play as notes) and **Keyswitchable on** (the application controls it *dynamically* for strumming parts vs picked notes or fingerstyle play). The default voice limits (8 and 4) run out as soon as a few strings ring, and chords then lose notes or go silent: raise them to 32 and 16. 
5. Close Kontakt's window: the sound, with these settings, is saved for the song and playstyle. *Repeat* for the other playstyles (Ballad, Rock, Pop). 
6. Click **Save defaults** in the playstyle window. A song opened for the first time now starts with these saved defaults, and **Load defaults** overrides what a song already has.

> [!Tip]
> The Shreddage `All-around Rhythm` preset is a good default for rhythm guitar, `American Lead` works for lead guitar tracks (if you opt for a Guitar Pro file containing multiple instrument tracks). Do try out other presets as well.<br>
> Numa Player has numerous tweakable sounds for piano, electric piano, organs, and strings. For instance a Pipe Diapason organ with some tweaks can be used as a harmonica replacement.

### 6. Importing songs
> [!Note]
> Four demo songs written for YourBand are included (artist *YourBand Demo*). Import your own songs: they stay on your computer in `songs-local\` and are never uploaded to the cloud.<br>
> The Ultimate Guitar import reads the pages whose URLs you give it, and does not save copies of them. Their terms do not allow automated scraping and the lyrics are licensed content, so use it for your own practice and see [docs/plan.md#legal](docs/plan.md#legal).

Make sure to set up the playstyle plugins and presets before importing the first song, as the songs you import inherit those settings. Having a good default saves you a bit of hassle later. Of course you can still change them per song afterwards.

The best import type is the Guitar Pro file, since it relies on a standardized file format and contains tracks with tabs for every instrument. But it can be too complicated for a beginner with lots of picked notes and stray chords; a chords page may then suit you better.

The chord and tab importers interpret the submitted page and build a song chart from it, but since page formats differ, correct decoding is not guaranteed, although many quirks have been worked out. If you run into problems, report an issue and I will look into it. 

There are many versions of all three file types for a particular song on Ultimate Guitar, so look through them to find a version that looks complete and tidy and not overly complicated – unless that is what you are looking for.

The different import options are as follows:

- **A Guitar Pro file:** the recommended import type that will give a full representation of the true sound of a song, with every lick, riff and solo, laying out every instrument track separately for you to fine-tune the plugin choice and sound presets for. 
Import a Guitar Pro **file** from the *Guitar Pro* section of the search result header. E.g. search for a song, for instance *Wicked Game* from Chris Isaak. In the result list header choose the Guitar Pro tab.<br><br>
![guitar pro tab](docs/images/guitar-pro-tab.png)<br>
Choose a version of the file that is appropriate, most often the one with the highest rating. Here I click on [**Wicked Game (ver 2)**](https://tabs.ultimate-guitar.com/tab/chris-isaak/wicked-game-guitar-pro-222293). Peek at the different instrument tracks and start the playback in the upper left to see if this version is useful. Then go to the bottom of the page and you will find a download link:
![download gp file](docs/images/guitar-pro-download.png)
<br>
Save this file in a folder. Then open the control panel with `C`. **Import new...** in the control panel takes a Guitar Pro file (and, optionally, an Ultimate Guitar chords page for its strumming in case the gp file lacks it), interprets the gp file and loads it as a song. **Delete song...**, to its right, deletes the currently open song after you confirm. Its files are moved to the Recycle Bin, so you can get them back if needed.<br>
  You now have the option of selecting which band member plays which instrument track. There are a few extra instruments that can also be set up to play, but they have no visible 3D model in the game view of the band. These are the Lead guitar and Instrument 1 slots. The application makes a default selection of which tracks are played by whom, but sometimes manual editing here is necessary. This is how I set up Wicked Game:<br><br>
![control-panel-wicked-game.png](docs/images/control-panel-wicked-game.png)<br>
For the rhythm guitar I use the All-around Rhythm preset, for Lead I use Classic Rock. This gp file *has* strumming stroke directions, so those are used. Some gp files may lack that, and therefore we have the option to include a *chord or tab page URL* from which this information can be taken. The strumming pattern needs to match the tabs for the rhythm guitar for this to work well though. Unfortunately this Wicked Game gp file has no lyrics, so they are missing here; otherwise the lyrics are shown in the song chart too.
<br>
This gp file has *identical* tracks for the left and right rhythm guitar so the importer will show only *one* of them with a note on the track name.

- **A basic jam track to train on chords and tempo:** 
This is a great starting point for any learning and practicing musician. It will provide a simplified view of a song without the solos and riffs (unless they are included as tabs in that page).
Import an Ultimate Guitar **URL** from the *Chord* section. E.g. search for a song, for instance *Heart of Gold* from Neil Young. In the result list header choose the Chords tab.<br><br>
![chord tab](docs/images/chord-tab.png)<br>
The band plays the song in one of the playstyles (Ballad, Rock or Pop), with the page's strumming pattern, or the common strum D - D U - U D U if the page has none. That gives you an easy-to-follow jam track.

- **A UG tab page:** this imports the song from a tab page: click Tab in the search result header mentioned above and choose a tab page. It is generally not recommended to import a Tab page, since the much better formalized Guitar Pro files include the same information and more. But for some situations, this import type may suit your needs.

- **UG Chord and Tab combined:** it is possible to combine both a Chord and Tab page for an import and the importer will make its best effort to combine the pieces into a complete song. It only works well if the two pages line up: the same tuning and capo, and mostly the same chords.  

### 7. Song window shortcuts

| Key                     | Function                                                         |
| ----------------------- | ---------------------------------------------------------------- |
| `L`                     | the next song                                                    |
| `Space`                 | play / pause (a count-in first from the top)                     |
| `R` / `Home`            | restart with a count-in / back to the top                        |
| `←` `→` / `PgUp` `PgDn` | a bar / a section back or on                                     |
| `↑` `↓`                 | practice speed, 5 % steps                                        |
| `1` `2` `3` `4`         | toggle the song, chords, strumming and tab panels                |
| `Tab`                   | the tab panel shows the next track the band plays                |
| `M`                     | toggle the tracks overview, bar by bar; click a bar to go there  |
| `B` / `P`               | the next band playstyle / strumming pattern                      |
| `V` / `X`               | toggle the 3D band (shown at start) / VR mode                    |
| `C` / `T`               | the control panel / your guitar's tone (if using the amp plugin) |
| `H`                     | help                                                             |

Want to play the rhythm yourself? Set the *Rhythm guitar* row to **None** (or click the mixer's **M** button to mute it). **None** takes that instrument out of the band entirely (which may save some CPU); **M** only silences it for a while.

## 🧠 How it works

Two programs talk over a local connection. The **audio engine** (C++ / JUCE) controls the audio interface and the plugins, and plays the notes. The **game** window application turns an imported song into what each band member plays and draws the chart and the 3D band. 

The details are in the documentation:

| | |
|---|---|
| [docs/architecture.md](docs/architecture.md) | The two programs, the audio graph, threads and real-time rules, the messages and the shared-memory clock, the code structure, where data is kept |
| [docs/band-arrangement.md](docs/band-arrangement.md) | How the band's parts are made: the made-up drums, bass, keys and guitar in each playstyle, and what each instrument plays from a Guitar Pro track |
| [docs/shreddage-keyswitches.md](docs/shreddage-keyswitches.md) | How the rhythm guitar drives Shreddage: the held chord, strum and string keyswitches, bends, slides, vibrato and palm mutes |

## 🛠 Build from source

<details>
<summary>What you need, and the commands</summary>

You need Git with [Git LFS](https://git-lfs.com/) (the screenshots are LFS files, a few MB), the Unity Editor **6000.6.7f1**,
the Visual Studio Build Tools with the C++ workload (CMake and Ninja come with them), the .NET 8 SDK (for the
song importer) and Python 3.

```powershell
git clone https://github.com/barsk/YourBand.git
cd YourBand

# the engine: JUCE is fetched, not committed
git clone --depth 1 --branch 9.0.4 https://github.com/juce-framework/JUCE.git engine\third_party\JUCE
engine\build.cmd                     # engine\build\Release\gt_engine_artefacts\Release\YourBand Engine.exe

# the song importer
dotnet build tools\songtool -c Release

# the game (Unity in batch mode), then run it
game\build_desktop.cmd               # game\Builds\Desktop\YourBand.exe
game\run_tests.cmd                   # the EditMode tests

# a release zip, as in step 2 (and the band pack's zip, if the pack is built)
tools\make_release.cmd -Build
```

The **3D musician models are not in the repository**: they have their own licences and are big (Microsoft Rocketbox
avatars, Mixamo animations, instrument models). The game loads them from the optional *band pack*
(`Band\band.bundle` next to the game). To see the band in a build from source, download the pack from
[Releases](https://github.com/barsk/YourBand/releases) and unzip it into `game\Builds\Desktop` (or `game\` for the
Unity Editor); [docs/band-assets.md](docs/band-assets.md) also tells how to make the pack yourself.

JUCE is used under the AGPLv3 while the project is personal; a commercial JUCE licence may be required before
publishing a product (see [CREDITS.md](CREDITS.md)).

</details>

## 📁 Folder structure

| Folder | |
|---|---|
| [`game/`](game/README.md) | The Unity game: song model, chart, the band's arranger, the desktop screen, the 3D band, VR |
| [`engine/`](engine/README.md) | The C++ audio engine and its control panel: devices, plugins, the band's playback, the mixer |
| [`tools/`](tools/songtool/README.md) | `songtool` (importers for Guitar Pro and Ultimate Guitar pages, MIDI export), the release script, the 3D band's asset pipeline |
| [`schema/`](schema/engine-protocol.md) | The messages between the game and the engine |
| [`docs/`](docs/architecture.md) | The architecture, how the band's parts are made, how Shreddage is driven, the game plan, the instrument-tracks design, the band's 3D assets, venue ideas, and the hand-capture experiment |
| [`capture/`](docs/capture.md), `markers/`, `mockup/` | Python and Blender work behind the hand capture and the 3D instruments |

Design and background: [docs/architecture.md](docs/architecture.md) (how it is built),
[docs/band-arrangement.md](docs/band-arrangement.md) (how the band's parts are made),
[docs/shreddage-keyswitches.md](docs/shreddage-keyswitches.md) (how the guitar is played), [docs/plan.md](docs/plan.md) (the whole plan),
[docs/instrument-tracks.md](docs/instrument-tracks.md) (how the band plays real tracks, with the measurements behind
it), [docs/capture.md](docs/capture.md) (video to hands).

## 🗺 Roadmap

Planned: 
- Better and more varied animations for the 3D game band models. 
- A custom-made footswitch for controlling the application, e.g. play/pause, back/forward, switch tones. 


## 🙏 Credits and licences

YourBand stands on other people's work: JUCE, TONE3000, the plugins above, Microsoft Rocketbox, Mixamo and more. Every
asset and library, its source and its licence is in [CREDITS.md](CREDITS.md). YourBand is not affiliated with or
endorsed by Native Instruments, Impact Soundworks, Studiologic, Ample Sound, Monster DAW or TONE3000.

## 📜 Licence

YourBand's source code is licensed under the **[GNU General Public License v3.0](LICENSE)**. The third-party assets
and libraries it uses keep their own licences, listed in [CREDITS.md](CREDITS.md); the audio engine includes JUCE
under the AGPLv3.
