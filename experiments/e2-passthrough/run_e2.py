"""Experiment E2: a musician in your room through Virtual Desktop's chroma-key passthrough.

  python run_e2.py [setup] [check]      (default: both)

setup: copies the avatar, the Mixamo clip and the E2 scripts into the throwaway project (made from Unity's VR
       template) and runs the editor in batch mode (E2Setup.Run): scene, rig, rendering, and the app in
       <project>/Builds/E2.
check: runs the app on the desktop without XR and checks what Virtual Desktop will key: every background pixel
       exactly the key colour, and no musician edge pixel tinted towards it. Results in results/.
The headset test is described in README.md.
"""
import os
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
EDITOR = r"C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
PROJECT = r"C:\Users\krist\Documents\Unity\GuitarTutor-E2\E2-VD"
APP = os.path.join(PROJECT, "Builds", "E2", "E2 Passthrough.exe")
AVATAR = os.path.join(REPO, "mockup", "avatars", "Male_Adult_04")
CLIP = os.path.join(REPO, "mockup", "mocap", "guitar_playing_111490901.fbx")


def setup():
    e2 = os.path.join(PROJECT, "Assets", "E2")
    for sub in ("Export", "Textures"):
        shutil.copytree(os.path.join(AVATAR, sub), os.path.join(e2, "Avatar", sub), dirs_exist_ok=True)
    os.makedirs(os.path.join(e2, "Mocap"), exist_ok=True)
    shutil.copy2(CLIP, os.path.join(e2, "Mocap"))
    for folder in ("Editor", "Runtime"):
        os.makedirs(os.path.join(e2, folder), exist_ok=True)
        for f in os.listdir(os.path.join(HERE, folder)):
            shutil.copy2(os.path.join(HERE, folder, f), os.path.join(e2, folder, f))
    log = os.path.join(PROJECT, "Logs", "e2_setup.log")
    os.makedirs(os.path.dirname(log), exist_ok=True)
    started = time.time()
    code = subprocess.call([EDITOR, "-batchmode", "-quit", "-projectPath", PROJECT, "-executeMethod", "E2Setup.Run", "-logFile", log])
    lines = open(log, encoding="utf-8", errors="replace").read().splitlines()
    print("setup: exit %d in %.0f s" % (code, time.time() - started))
    for l in [l for l in lines if l.startswith("E2:") or "error CS" in l or "threw exception" in l][:15]:
        print("   ", l)
    return code == 0


def check():
    out = os.path.join(HERE, "results")
    os.makedirs(out, exist_ok=True)
    code = subprocess.call([APP, "-noxr", "-e2check", out, "-logFile", os.path.join(out, "player.log")], timeout=300)
    print("check: exit %d" % code)
    path = os.path.join(out, "e2_check.txt")
    if os.path.exists(path):
        print("   ", open(path, encoding="utf-8").read().strip())


if __name__ == "__main__":
    steps = [a for a in sys.argv[1:] if a in ("setup", "check")] or ["setup", "check"]
    if "setup" in steps and not setup():
        sys.exit(1)
    if "check" in steps:
        check()
