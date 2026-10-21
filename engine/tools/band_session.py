"""A test session without a sound: the engine runs silently and records, the game plays a song by key script.

  python band_session.py <out-folder> --keys "4:Space,20:quit" [--seconds 30] [game options ...]

Starts `GuitarTutor Engine.exe --silent --record <out>/band.wav --run-for <seconds>`, waits for it to load its
plugins, starts the game with `-keys <keys> -keyshots <out>` and the other options (for example -demo,
-song <file>, -parts drums), and waits for both. Then check the recording with wav_check.py.
"""
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ENGINE = os.path.join(REPO, "engine", "build", "Release", "gt_engine_artefacts", "Release", "GuitarTutor Engine.exe")
GAME = os.path.join(REPO, "game", "Builds", "Desktop", "GuitarTutor.exe")


def main(args):
    if len(args) < 3 or "--keys" not in args:
        sys.exit(__doc__)
    out = os.path.abspath(args[0])
    rest = args[1:]
    keys = rest[rest.index("--keys") + 1]
    del rest[rest.index("--keys"):rest.index("--keys") + 2]
    seconds = "30"
    if "--seconds" in rest:
        seconds = rest[rest.index("--seconds") + 1]
        del rest[rest.index("--seconds"):rest.index("--seconds") + 2]
    os.makedirs(out, exist_ok=True)

    engine = subprocess.Popen([ENGINE, "--silent", "--record", os.path.join(out, "band.wav"), "--run-for", seconds])
    time.sleep(7)  # plugins load, Numa Player loads its sound
    game = subprocess.Popen([GAME, "-keys", keys, "-keyshots", out, "-screen-width", "1920", "-screen-height", "1080",
                             "-screen-fullscreen", "0", "-logFile", os.path.join(out, "game.log")] + rest)
    game.wait(timeout=600)
    engine.wait(timeout=600)
    print("game exit %d, engine exit %d, results in %s" % (game.returncode, engine.returncode, out))


if __name__ == "__main__":
    main(sys.argv[1:])
