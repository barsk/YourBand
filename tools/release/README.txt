YourBand
========

Unzip this folder anywhere (for example Documents\YourBand) and start Game\YourBand.exe. The game starts
its audio engine (Engine\YourBand Engine.exe) by itself and closes it when it closes. C opens the engine's
control panel, H the game's help.

  Game\          the game
  Engine\        the audio engine and its control panel; songtool\ imports songs (Import... in the control panel)
  songs-local\   the songs (*.song.json), their Guitar Pro files (gpx\) and which track each band member plays
                 (*.band.json); imports land here. Empty in a release made without songs: copy a songs-local
                 folder here, or import songs
  settings\      in a release made with them: the band's sounds as they were on the computer it was made on
  Game\Band\     the optional 3D band (shown at start, V hides it): made by unzipping the band pack over this folder. Without
                 it the game has no band view and is otherwise the same

Windows 10 or 11, 64-bit. Nothing else needs installing for the game and the engine themselves.

The whole band plays from the start, the rhythm guitar too. To leave the rhythm to yourself, or to take any other
instrument out of a song, choose "None" in the first list of its row in the control panel (C); the song remembers it.

The plugins
-----------
The band plays on plugins you install yourself; without them it plays its built-in sounds.

  Rhythm guitar   Kontakt 8 (Native Instruments' free Kontakt Player) with Shreddage 3.5 Stratus FREE
                  (Impact Soundworks), installed with Native Access
  Bass            Ample Bass P Lite (Ample Sound), or MONSTER Bass
  Drums           MONSTER Drums
  Keyboards       Numa Player (Studiologic)
  Lead channels   Numa Player or Kontakt
  Your guitar     the TONE3000 plugin, for your guitar's amp sound

VST3 plugins go in C:\Program Files\Common Files\VST3, where most installers put them. The MONSTER plugins (MONSTER
Drums, Bass and Guitar) are found there too, or in a folder named monster_plugins next to Engine\ (this folder:
monster_plugins\Library files\*.vst3 with their libraries beside them, as they unpack).

Shreddage, for the band guitarist: in Kontakt, Performance mode Rhythm, Fretting mode Polyphonic, Strumming Mode off,
Keyswitchable on, and the instrument's voice limits raised (Standard 32, High Quality 16). With fewer voices, chords
go silent. Kontakt keeps these settings per song and per playstyle: set them in the control panel's rhythm guitar
"..." window, and close it to keep them (Band playstyle defaults..., "Save defaults" and "Load defaults" carry them
to other songs).

The settings
------------
A release made with settings has settings\: the band's sounds of every song and the playstyle defaults (the plugins'
own settings, Shreddage's among them). To start with them, run settings\copy-settings.cmd once: it copies them to %APPDATA%\YourBand and
never overwrites a file that is already there. Without them each song starts from the built-in sounds.

The engine's own settings (your audio interface, TONE3000, the plugins you added) are not included: choose your
audio device and the plugins in the control panel on this computer. A plugin's sound refers to its library where it
was on the other computer; if a MONSTER plugin is silent, choose its sound again in its window.

To remove YourBand, delete this folder, and %APPDATA%\YourBand for its settings.
