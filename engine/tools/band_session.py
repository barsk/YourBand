"""A test session without a sound: the engine runs silently and records, the game plays a song by key script.

  python band_session.py <out-folder> --keys "4:Space,20:quit" [--seconds 30] [--band-bass ABPL] [--band-guitar synthesized] [game options ...]

Starts `YourBand Engine.exe --silent --record <out>/band.wav --run-for <seconds>`, waits for it to load its
plugins, starts the game with `-keys <keys> -keyshots <out>` and the other options (for example -demo,
-song <file>, -parts drums), and waits for both. Then check the recording with wav_check.py.
"""
import os
import socket
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ENGINE = os.path.join(REPO, "engine", "build", "Release", "gt_engine_artefacts", "Release", "YourBand Engine.exe")
GAME = os.path.join(REPO, "game", "Builds", "Desktop", "YourBand.exe")


def require_port_free(port=47800):
    """The engine must be able to listen for the game; if another engine holds the port, the game would play
    through that engine and this one would record silence."""
    probe = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        probe.bind(("127.0.0.1", port))
    except OSError:
        sys.exit("port %d is in use: another YourBand Engine is running; close it (or its game) and try again" % port)
    finally:
        probe.close()


def main(args):
    if len(args) < 3 or "--keys" not in args:
        sys.exit(__doc__)
    require_port_free()
    out = os.path.abspath(args[0])
    rest = args[1:]
    keys = rest[rest.index("--keys") + 1]
    del rest[rest.index("--keys"):rest.index("--keys") + 2]
    seconds = "30"
    if "--seconds" in rest:
        seconds = rest[rest.index("--seconds") + 1]
        del rest[rest.index("--seconds"):rest.index("--seconds") + 2]
    engine_args = []
    for option in ("--band-bass", "--band-guitar"):  # a plugin's file name (ABPL) or synthesized, not your choice
        if option in rest:
            engine_args += [option, rest[rest.index(option) + 1]]
            del rest[rest.index(option):rest.index(option) + 2]
    os.makedirs(out, exist_ok=True)

    engine = subprocess.Popen([ENGINE, "--silent", "--record", os.path.join(out, "band.wav"), "--run-for", seconds]
                              + engine_args)
    time.sleep(7)  # plugins load, Numa Player loads its sound
    # -engineargs --silent: should this engine stop, the one the game starts in its place is silent too (not one on your
    # audio interface, keeping its sounds in your settings)
    game = subprocess.Popen([GAME, "-keys", keys, "-keyshots", out, "-screen-width", "1920", "-screen-height", "1080",
                             "-screen-fullscreen", "0", "-logFile", os.path.join(out, "game.log"),
                             "-engineargs", "--silent"] + rest)
    # the game quits by its key script; if it does not (a script it could not read), it is stopped
    try:
        game.wait(timeout=float(seconds) + 30)
    except subprocess.TimeoutExpired:
        game.kill()
        print("the game did not quit by its key script: stopped it")
    engine.wait(timeout=600)
    print("game exit %d, engine exit %d, results in %s" % (game.returncode, engine.returncode, out))


if __name__ == "__main__":
    main(sys.argv[1:])
