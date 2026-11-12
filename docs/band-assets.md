# The band's 3D assets and the band pack

The 3D musicians are an **optional download**. The main job of the game is playing jam tracks, and the band view is
an extra (`V`), so the musicians are not in the repository or in the game's own zip: they are one file, the *band
pack*, `Band/band.bundle` (a Unity asset bundle, about 110 MB, a 96 MB zip), which each release offers separately. The
game looks for it in a `Band` folder next to the game and shows the band view only if it is there.

## Using the pack

- **A release:** unzip the band pack zip into the release folder (the one with `Game` and `Engine` in it). It adds
  `Game\Band\band.bundle` (and a `NOTICE.txt` with the licences of what is inside).
- **A build from source:** put `band.bundle` in `game/Builds/Desktop/Band/` for the built player, or in `game/Band/`
  for the Unity Editor (the Editor also uses the musicians' prefabs directly, if you made them, below).
- **Without it** the game runs the same, the music, chart and audio band included: pressing `V` says the band pack is
  not installed.

## What is inside, and under which licence

The pack holds the four musicians with their instruments and animations. The sources are not in git; they are
downloads with their own licences, listed in [CREDITS.md](../CREDITS.md):

| What | Source | Licence |
|---|---|---|
| The musicians' bodies, heads and textures: four [Microsoft Rocketbox](https://github.com/microsoft/Microsoft-Rocketbox) avatars (`Male_Adult_09` the drummer, `Female_Adult_08` the bassist, `Female_Adult_12` the keyboardist, `Male_Adult_04` the guitarist) | Microsoft | MIT ([notice](licenses/Microsoft-Rocketbox-LICENSE.txt)) |
| The drum kit, bass, guitar and keyboard with their playing geometry (frets, strings, keys, drums) | GrabCAD models | see [CREDITS.md](../CREDITS.md) |
| Their playing: four [Mixamo](https://www.mixamo.com) clips (`playing_drums_113820901`, `guitar_playing_111430901`, `guitar_playing_111490901`, `piano_playing_112170902`) | Adobe | Mixamo terms: the raw files are not redistributed on their own, so they are only in the pack, compiled |
| Materials, animator controllers, the musicians' prefabs | made by `YourBand > Set up band` (`Editor/BandSetup.cs`) | ours |

## Making the pack

You only need this to change the musicians or to make a release.

1. `tools/band_assets/band_assets.py` gathers the sources (the Rocketbox downloads, the Mixamo clips and the
   instruments' Blender models, kept outside the repository in `mockup/` and `model_images/`) into
   `game/Assets/External/Band/`. It needs Pillow (`pip install pillow`): Rocketbox's 2K TGA textures become lossless
   PNGs (a third of the size), and its specular maps are left out, the game does not use them. The Mixamo clips
   come from the Mixamo site, as FBX for Unity (without skin): see the names in `band_assets.py`.
2. `game\build_desktop.cmd` (or in the Editor: `YourBand > Set up band`) makes the materials, controllers and prefabs
   into `game/Assets/External/Band/Prefabs/` and builds the pack into `game/Builds/BandPack/Band/band.bundle`
   (`YourBand > Build band pack` rebuilds just the pack).
3. `tools\make_release.cmd` puts the pack in a zip of its own next to the game's (`-WithBand` puts it in the game's zip
   instead, for a one-file release).

The pack was checked against the same band built into the player: the four musicians rendered the same, pixel
for pixel (within 2 of 255 on a few pixels), from the pack as from the player's own data.
