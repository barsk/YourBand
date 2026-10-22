# Credits and third-party assets

Every third-party asset and library used by GuitarTutor, with its source and licence. Large downloads are not
in git; the "Local path" column says where they are expected, so a fresh checkout knows what to fetch.

Allowed licences: CC0, CC BY (with credit), MIT, BSD, royalty-free store licences. Not allowed: NC
(non-commercial), ND (no derivatives), editorial use, models ripped from games. See [docs/plan.md](docs/plan.md).

## In use

| Asset | Author | Source | Licence | Local path | Used for |
|---|---|---|---|---|---|
| Microsoft Rocketbox avatars | Microsoft | https://github.com/microsoft/Microsoft-Rocketbox | MIT | `mockup/avatars/` | Musicians |
| Mixamo animations: Guitar Playing (2 clips), Piano Playing (2), Playing Drums | Adobe | https://www.mixamo.com | Adobe Mixamo terms: use in projects; raw files not redistributed | `mockup/mocap/` | Musicians' motion |
| Home Studio | R-LAB | https://sketchfab.com/3d-models/home-studio-6d1a0713efc543a99d33e11e67099ef2 | CC BY 4.0 (we modify it) | `model_images/venues/home_studio/` | First desktop venue |
| TONE3000 plugin | TONE3000 | https://github.com/tone-3000/tone3000-plugin | MIT | Installed plugin (VST3/CLAP), not bundled | Guitar tone |
| JUCE 9.0.4 | Raw Material Software | https://github.com/juce-framework/JUCE | AGPLv3 while personal; a commercial JUCE licence may be required before publishing | `engine/third_party/JUCE/` | Audio engine |
| Numa Player 2.2.2 | Studiologic | https://www.studiologic-music.com | Free; no licence text found. Installed by you, not bundled; check terms before publishing | Installed plugin (VST3) | Keyboardist's sounds |
| MONSTER Drums v3 (3.2024.05), MONSTER Bass v2 | Agus Hardiman (Monster DAW), built with Maizesoft's Maize Sampler | https://agushardiman.tv (listed on https://www.kvraudio.com/developer/monster-daw) | Free; no licence text found in the download or on the site. Downloaded by you, not bundled; check terms before publishing | `monster_plugins/` (not in git) | The band's drums and bass |
| ASIO headers (bundled with JUCE) | Steinberg | https://www.steinberg.net/developers/ | Steinberg ASIO licence or GPLv3; publishing under Steinberg's licence needs their free signed agreement | `engine/third_party/JUCE/modules/juce_audio_devices/native/asio/` | ASIO audio |

## Licence still to record

These came without a licence note. The folder names (`*.snapshot.N`) suggest GrabCAD; GrabCAD community
models need their licence checked one by one before any release.

| Asset | Local path | Used for |
|---|---|---|
| 1989 Fender Eric Clapton Stratocaster | `model_images/1989-fender-eric-clapton-stratocaster-1.snapshot.6/` | Band guitarist's guitar |
| Stratocaster Classic Player | `model_images/Stratocaster/` | Your guitar in the mockup |
| Precision Bass (SolidWorks), incl. `sunburn.png` | `model_images/p-bass-2/` | Bassist's bass |
| Sonor drum set | `model_images/drum-set/` | Drum kit |
| Yamaha EZ-220 keyboard | `model_images/yamaha-keyboard-ex-220/` | Keyboard |
| Korg Kronos | `model_images/korg-kronos-x-1/` | Not used |

## Candidates

| Asset | Author | Source | Licence | Local path |
|---|---|---|---|---|
| Office Studio | R-LAB | https://sketchfab.com/3d-models/office-studio-8fc5286645384538a7c405b4457f562a | CC BY 4.0 | `model_images/venues/office_studio/` |

## Planned libraries and sounds

| Asset | Source | Licence |
|---|---|---|
| NeuralAmpModelerCore | https://github.com/sdatkinson/NeuralAmpModelerCore | MIT |
| sfizz | https://github.com/sfztools/sfizz | BSD-2-Clause |
| Salamander Grand Piano | https://github.com/sfzinstruments/SalamanderGrandPiano | CC BY 3.0 |
| AVL Drumkits | https://www.bandshed.net/avldrumkits/ | CC BY-SA 3.0 |
| Virtuosity Drums | https://www.kvraudio.com/product/21550 | CC0 |
| Karoryfer Growlybass | https://www.kvraudio.com/product/10087 | Royalty-free; check the download's licence file |
| OpenAIR impulse responses | https://www.openair.hosted.york.ac.uk | Per response, mostly CC BY |

## Song content

Songs imported from Ultimate Guitar stay on your own machine and are never redistributed. The demo song
(`mockup/songs/demo.mid`) is our own.
