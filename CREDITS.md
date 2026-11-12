# Credits and third-party assets

Every third-party asset and library used by YourBand, with its source and licence. Large downloads are not
in git; the "Local path" column says where they are expected, so a fresh checkout knows what to fetch.

Allowed licences: CC0, CC BY (with credit), MIT, BSD, royalty-free store licences. Not allowed: NC
(non-commercial), ND (no derivatives), editorial use, models ripped from games. See [docs/plan.md](docs/plan.md).

## In use

An asset's name links to its source. The notes under the table say what a licence column leaves out.

| Asset | Author | Licence | Used for | Local path |
|---|---|---|---|---|
| [Microsoft Rocketbox avatars](https://github.com/microsoft/Microsoft-Rocketbox) | Microsoft | MIT | Musicians | `mockup/avatars/` |
| [Mixamo animations](https://www.mixamo.com): Guitar Playing (2 clips), Piano Playing (2), Playing Drums | Adobe | Mixamo terms (1) | Musicians' motion | `mockup/mocap/` |
| [Home Studio](https://sketchfab.com/3d-models/home-studio-6d1a0713efc543a99d33e11e67099ef2) | R-LAB | CC BY 4.0 (we modify it) | First desktop venue | `model_images/venues/home_studio/` |
| [TONE3000 plugin](https://github.com/tone-3000/tone3000-plugin) | TONE3000 | MIT | Guitar tone | Installed plugin (VST3/CLAP), not bundled |
| [JUCE 9.0.4](https://github.com/juce-framework/JUCE) | Raw Material Software | AGPLv3 (2) | Audio engine | `engine/third_party/JUCE/` |
| [Numa Player 2.2.2](https://www.studiologic-music.com) | Studiologic | Free, no licence text (3) | Keyboardist's sounds | Installed plugin (VST3), not bundled |
| [MONSTER Drums v3 (3.2024.05), MONSTER Bass v2](https://agushardiman.tv) | Agus Hardiman (Monster DAW), built with Maizesoft's Maize Sampler | Free, no licence text (3) | The band's drums and bass | `monster_plugins/` (not in git) |
| [MONSTER Guitar v2 (2.10.4)](https://agushardiman.tv) | Agus Hardiman (Monster DAW) | Free, no licence text (3) | The band's rhythm guitar | `monster_plugins/` (not in git) |
| Kontakt 8 (8.14) with Shreddage 3 Stratus FREE: [Native Instruments](https://www.native-instruments.com), [Impact Soundworks](https://impactsoundworks.com) | Native Instruments (Kontakt Player); Impact Soundworks (Shreddage) | Free; Native Instruments' EULA (4) | The band's rhythm guitar | Installed plugin (VST3); library in `C:\Users\Public\Documents\Shreddage 3 Stratus FREE` |
| [Ample Bass P Lite 4.0.0](https://www.amplesound.net) | Ample Sound | Free for private use only (5) | The band's bass (an alternative to MONSTER Bass) | Installed plugin (VST3); library in `plugins/` (not in git) |
| [ASIO headers](https://www.steinberg.net/developers/) (bundled with JUCE) | Steinberg | Steinberg ASIO licence or GPLv3 (6) | ASIO audio | `engine/third_party/JUCE/modules/juce_audio_devices/native/asio/` |
| [Unity 6 (6000.6.7f1)](https://unity.com) and its packages (OpenXR plugin, XR Management, Input System) | Unity Technologies | Unity's terms; the packages: Unity Companion License (7) | The game | Installed with the Unity Editor |
| [alphaTab 1.8.4](https://www.alphatab.net/) | Danielku15 and contributors | MPL-2.0 | Reading Guitar Pro files (`songtool`, bundled with it) | NuGet package, not in git |
| [Json.NET (Newtonsoft.Json)](https://www.newtonsoft.com/json), through Unity's package `com.unity.nuget.newtonsoft-json` 3.2.2 | James Newton-King | MIT (the Unity package: Unity Companion License) | Song, band and message files (JSON) | Unity package, not in git |

1. Adobe Mixamo terms: use in projects; the raw files are not redistributed.
2. AGPLv3 while the project is personal; a commercial JUCE licence may be required before publishing.
3. No licence text was found in the download or on the site (MONSTER plugins are also listed on
   [KVR](https://www.kvraudio.com/developer/monster-daw)). Installed or downloaded by you, not bundled; check the
   terms before publishing.
4. Native Instruments' EULA is in the library's Documentation folder. Installed by you with Native Access, not bundled.
5. Installed by you (plugin and library), not bundled; each user installs their own.
6. Publishing under Steinberg's licence needs their free signed agreement.
7. Unity's packages (the Newtonsoft Json one among them) are under the
   [Unity Companion License](http://www.unity3d.com/legal/licenses/Unity_Companion_License); the Json.NET library
   inside is MIT.

## GrabCAD models

The instruments' 3D models are from GrabCAD's community library. None of them came with licence terms, or the terms
were permissive; the project owner checked this when each was added.

| Asset | Source | Licence | Local path | Used for |
|---|---|---|---|---|
| 1989 Fender Eric Clapton Stratocaster | GrabCAD | None stated or permissive | `model_images/1989-fender-eric-clapton-stratocaster-1.snapshot.6/` | Band guitarist's guitar |
| Stratocaster Classic Player | GrabCAD | None stated or permissive | `model_images/Stratocaster/` | Your guitar in the mockup |
| Precision Bass (SolidWorks), incl. `sunburn.png` | GrabCAD | None stated or permissive | `model_images/p-bass-2/` | Bassist's bass |
| Sonor drum set | GrabCAD | None stated or permissive | `model_images/drum-set/` | Drum kit |
| Yamaha EZ-220 keyboard | GrabCAD | None stated or permissive | `model_images/yamaha-keyboard-ex-220/` | Keyboard |
| Korg Kronos | GrabCAD | None stated or permissive | `model_images/korg-kronos-x-1/` | Not used |

## Development tools (not shipped)

Used to make assets and run the capture experiment ([docs/capture.md](docs/capture.md)); none is part of a release.

| Tool | Licence | Used for |
|---|---|---|
| [Blender](https://www.blender.org) | GPL | The 3D instruments and the mockup scene (`mockup/`, `tools/band_assets/`) |
| [MediaPipe](https://github.com/google-ai-edge/mediapipe) (with its Hand Landmarker model, downloaded on the first run) | Apache-2.0 | Finding the hands in a video (`capture/`) |
| [OpenCV](https://opencv.org), [NumPy](https://numpy.org), [Pillow](https://python-pillow.org) | Apache-2.0, BSD-3-Clause, HPND | Reading video, the numbers, and the printable markers (`capture/`) |

## Candidates

| Asset | Author | Source | Licence | Local path |
|---|---|---|---|---|
| Office Studio | R-LAB | https://sketchfab.com/3d-models/office-studio-8fc5286645384538a7c405b4457f562a | CC BY 4.0 | `model_images/venues/office_studio/` |
