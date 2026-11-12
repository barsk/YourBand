# Credits and third-party assets

Every third-party asset and library used by GuitarTutor, with its source and licence. Large downloads are not
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

1. Adobe Mixamo terms: use in projects; the raw files are not redistributed.
2. AGPLv3 while the project is personal; a commercial JUCE licence may be required before publishing.
3. No licence text was found in the download or on the site (MONSTER plugins are also listed on
   [KVR](https://www.kvraudio.com/developer/monster-daw)). Installed or downloaded by you, not bundled; check the
   terms before publishing.
4. Native Instruments' EULA is in the library's Documentation folder. Installed by you with Native Access, not bundled.
5. Installed by you (plugin and library), not bundled; each user installs their own.
6. Publishing under Steinberg's licence needs their free signed agreement.

## Licence still to record

These models are from GrabCAD. GrabCAD community models need their licence checked one by one before any release.

| Asset | Source | Local path | Used for |
|---|---|---|---|
| 1989 Fender Eric Clapton Stratocaster | GrabCAD | `model_images/1989-fender-eric-clapton-stratocaster-1.snapshot.6/` | Band guitarist's guitar |
| Stratocaster Classic Player | GrabCAD | `model_images/Stratocaster/` | Your guitar in the mockup |
| Precision Bass (SolidWorks), incl. `sunburn.png` | GrabCAD | `model_images/p-bass-2/` | Bassist's bass |
| Sonor drum set | GrabCAD | `model_images/drum-set/` | Drum kit |
| Yamaha EZ-220 keyboard | GrabCAD | `model_images/yamaha-keyboard-ex-220/` | Keyboard |
| Korg Kronos | GrabCAD | `model_images/korg-kronos-x-1/` | Not used |

## Candidates

| Asset | Author | Source | Licence | Local path |
|---|---|---|---|---|
| Office Studio | R-LAB | https://sketchfab.com/3d-models/office-studio-8fc5286645384538a7c405b4457f562a | CC BY 4.0 | `model_images/venues/office_studio/` |
