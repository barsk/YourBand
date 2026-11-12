"""Gathers the band's 3D assets into the game project (game/Assets/External/Band/, not in git).

  python band_assets.py [--no-instruments]

Needs Pillow (pip install pillow) for the textures.

- Avatars: the mockup's Rocketbox musicians (mockup/avatars/<name>/Export and Textures, with the mockup's
  look and hair maps), one per band role. Rocketbox's 2K TGA textures become lossless PNGs (a third of the size);
  its specular maps are left out, the game does not use them (the look maps give the materials' mask).
- Mo-cap: the Mixamo clips the mockup used (mockup/mocap/).
- Instruments: exported from the mockup's processed models by Blender (export_instruments.py): an FBX and the
  playing geometry as JSON for each.

The sources are downloads listed in CREDITS.md; this only copies and converts them. Then open the game
project (or run build_desktop.cmd): YourBand > Set up band (BandSetup) makes the musicians from them.
"""
import os
import shutil
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
MOCKUP = os.path.join(REPO, "mockup")
OUT = os.path.join(REPO, "game", "Assets", "External", "Band")
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"

# the mockup's casting (build_vr_band_scene.py, AVATARS)
AVATARS = {"Drummer": "Male_Adult_09", "Bassist": "Female_Adult_08", "Keyboardist": "Female_Adult_12",
           "Guitarist": "Male_Adult_04"}
CLIPS = ["playing_drums_113820901.fbx", "guitar_playing_111430901.fbx", "guitar_playing_111490901.fbx",
         "piano_playing_112170902.fbx"]


def tga_to_png(src, dst):
    """The same pixels, lossless (colour, normal and hair maps; the hair's alpha is kept)."""
    with Image.open(src) as image:
        image.load()
        image.save(dst, "PNG")


def copy_avatars():
    for role, name in AVATARS.items():
        src = os.path.join(MOCKUP, "avatars", name)
        dst = os.path.join(OUT, "Avatars", role)
        os.makedirs(dst, exist_ok=True)
        shutil.copy2(os.path.join(src, "Export", name + ".fbx"), os.path.join(dst, name + ".fbx"))
        for f in os.listdir(os.path.join(src, "Textures")):
            if "_specular" in f:
                continue  # (unused: the look maps give the materials' mask)
            if f.lower().endswith(".tga"):
                tga_to_png(os.path.join(src, "Textures", f), os.path.join(dst, os.path.splitext(f)[0] + ".png"))
            else:
                shutil.copy2(os.path.join(src, "Textures", f), os.path.join(dst, f))
        print("avatar %-11s %s" % (role, name))


def copy_clips():
    dst = os.path.join(OUT, "Mocap")
    os.makedirs(dst, exist_ok=True)
    for c in CLIPS:
        shutil.copy2(os.path.join(MOCKUP, "mocap", c), os.path.join(dst, c))
    print("mo-cap: %d clips" % len(CLIPS))


def export_instruments():
    dst = os.path.join(OUT, "Instruments")
    os.makedirs(dst, exist_ok=True)
    # the P-Bass's sunburst, which the game puts on the body itself (FBX's embedded media is not relied on)
    shutil.copy2(os.path.join(REPO, "model_images", "p-bass-2", "sunburn.png"), os.path.join(dst, "pbass_sunburst.png"))
    code = subprocess.call([BLENDER, "--background", "--factory-startup", "--python",
                            os.path.join(HERE, "export_instruments.py"), "--", dst])
    print("instruments: exit %d" % code)
    return code == 0


if __name__ == "__main__":
    copy_avatars()
    copy_clips()
    if "--no-instruments" not in sys.argv and not export_instruments():
        sys.exit(1)
