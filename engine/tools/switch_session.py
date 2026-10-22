"""A test session without a sound or the game: the band's bass switches plugins while a song plays.

  python switch_session.py <out-folder> [--bass-only] [--once]

Starts `GuitarTutor Engine.exe --silent --record <out>/band.wav`, connects as the game would, loads a made-up
song (a bass line on every quarter at 120 BPM, with drums and keys unless --bass-only), plays it, and every
3.5 s asks for another bass (`bandBass`): Ample Bass P Lite, MONSTER Bass, built-in, and so on (with --once:
one switch to MONSTER Bass, then nothing). Then check band.wav with wav_check.py: the band should play on
through every switch, and the engine should exit with 0.
"""
import json
import os
import socket
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ENGINE = os.path.join(HERE, "..", "build", "Release", "gt_engine_artefacts", "Release", "GuitarTutor Engine.exe")


def main(args):
    if not args:
        sys.exit(__doc__)
    out = os.path.abspath(args[0])
    os.makedirs(out, exist_ok=True)
    engine = subprocess.Popen([os.path.normpath(ENGINE), "--silent", "--record", os.path.join(out, "band.wav"),
                               "--run-for", "45"])
    time.sleep(8)  # plugins load

    link = socket.create_connection(("127.0.0.1", 47800), timeout=10)
    reader = link.makefile("r", encoding="utf-8")

    def send(message):
        link.sendall((json.dumps(message) + "\n").encode("utf-8"))

    send({"type": "hello", "client": "switch-session", "protocol": 1})
    print("engine:", reader.readline().strip()[:100])
    # parts: 0 drums, 1 bass, 2 keys; [part, pitch, at, length, velocity]
    roots = [28, 40, 31, 43, 33, 45, 35, 47, 38, 50]
    notes = [[1, roots[q % len(roots)], q, 0.9, 100] for q in range(240)]
    if "--bass-only" not in args:
        notes += [[0, 42, q, 0.25, 90] for q in range(240)]
        notes += [[0, 36 if q % 2 == 0 else 38, q, 0.25, 110] for q in range(240)]
        notes += [[2, p, q, 1.9, 80] for q in range(0, 240, 2) for p in (52, 55, 59)]
    send({"type": "load", "title": "Switch test", "style": "Rock", "tempos": [[0, 120]], "meters": [[0, 4, 4]],
          "notes": notes, "keys": ""})
    print("engine:", reader.readline().strip()[:100])
    send({"type": "play", "from": 0, "countIn": 0})
    start = time.time()
    order = ["ABPL", "MONSTER Bass v2", "synthesized", "ABPL", "synthesized", "MONSTER Bass v2", "ABPL", "MONSTER Bass v2"]
    if "--once" in args:
        order = ["ABPL", "MONSTER Bass v2"] + [None] * 6
    for plugin in order:
        time.sleep(3.5)
        if plugin:
            print("%5.1f s: bass on %s" % (time.time() - start, plugin))
            send({"type": "bandBass", "plugin": plugin})
    time.sleep(4)
    send({"type": "quit"})
    link.close()
    engine.wait(timeout=90)
    print("engine exit %d, results in %s" % (engine.returncode, out))


if __name__ == "__main__":
    main(sys.argv[1:])
