"""A silent test of the band guitar's strum pipeline, without a game build.

  python strum_session.py <out-folder> [--seconds 30] [--band-guitar "Kontakt 8"]

Starts the engine silently with a recording and the given band guitar plugin, loads a small test song
(G, 96 BPM, 4-beat count-in, the D-DU-UDU strumming pattern for three bars and one held downstroke in
the last; it sends the per-string notes, the held voicings and the strokes, as the game's load does),
plays it, and waits for the engine to run out. Then check the recording with:

  python wav_check.py <out-folder>/band.wav --bpm 96
"""
import json
import os
import socket
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ENGINE = os.path.join(REPO, "engine", "build", "Release", "gt_engine_artefacts", "Release", "GuitarTutor Engine.exe")
PORT = 47800

BPM = 96
QUARTER = 60.0 / BPM  # 0.625 s
G = [40, 47, 55, 59, 62, 67]  # E2 B2 G3 B3 D4 G4: the G chord, lowest string first
DOWN_SPREAD_S, UP_SPREAD_S = 0.012, 0.009  # as Arranger.Strum


def build_song(perstring=True, muted_bar=None):
    """The test song: bars 1-3 D-DU-UDU (bar `muted_bar`, 1-based, with its upstrokes muted),
    bar 4 (last) one downstroke, held. Returns (notes, voicings, strokes)."""
    notes, voicings, strokes = [], [], []
    pattern = ["D", "R", "D", "U", "R", "U", "D", "U"]
    for bar in range(4):
        last = bar == 3
        for i in range(1 if last else 8):
            s = "D" if last else pattern[i]
            if s == "R":
                continue
            at = bar * 4 + i * 0.5
            down = s == "D"
            muted = not down and bar + 1 == muted_bar
            vel = 64 if muted else 96 if down and i % 2 == 0 else 84 if down else 72
            strokes.append([at, 0 if down else 1, vel, 1 if muted else 0])
            if perstring:
                # the per-string strikes: down all six low to high, up the top four high to low
                order = list(range(6)) if down else list(reversed(range(2, 6)))
                spread_q = (DOWN_SPREAD_S if down else UP_SPREAD_S) / QUARTER
                for k, st in enumerate(order):
                    notes.append([3, G[st], round(at + k * spread_q, 6), 0.25, vel])
    for p in G:
        voicings.append([p, 0.0, 16.0])
    return notes, voicings, strokes


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    out = os.path.abspath(sys.argv[1])
    seconds, band_guitar, perstring = 30, "Kontakt 8", True
    rest = sys.argv[2:]
    for option in ("--seconds", "--band-guitar"):
        if option in rest:
            value = rest[rest.index(option) + 1]
            del rest[rest.index(option):rest.index(option) + 2]
            if option == "--seconds":
                seconds = int(value)
            else:
                band_guitar = value
    muted_bar = None
    if "--no-perstring" in rest:
        perstring = False
        rest.remove("--no-perstring")
    if "--muted-bar" in rest:
        muted_bar = int(rest[rest.index("--muted-bar") + 1])
        del rest[rest.index("--muted-bar"):rest.index("--muted-bar") + 2]
    if rest:
        sys.exit("unknown options: %s" % rest)
    os.makedirs(out, exist_ok=True)

    engine = subprocess.Popen([ENGINE, "--silent", "--record", os.path.join(out, "band.wav"),
                               "--run-for", str(seconds), "--band-guitar", band_guitar])
    conn = None
    deadline = time.time() + 90
    while time.time() < deadline:  # the engine loads its plugins (Kontakt takes a while) before it listens
        try:
            conn = socket.create_connection(("127.0.0.1", PORT), timeout=1.0)
            break
        except OSError:
            time.sleep(0.5)
    if conn is None:
        engine.kill()
        sys.exit("the engine did not open the port")
    f = conn.makefile("rwb")

    def send(obj):
        f.write((json.dumps(obj) + "\n").encode("utf-8"))
        f.flush()

    def receive():
        return f.readline().decode("utf-8").strip()

    send({"type": "hello", "client": "strum_session", "protocol": 1})
    print("engine:", receive())
    notes, voicings, strokes = build_song(perstring, muted_bar)
    send({"type": "load", "title": "Strum Test", "style": "Rock",
          "tempos": [[0, BPM]], "meters": [[0, 4, 4]],
          "notes": notes, "voicings": voicings, "strokes": strokes, "keys": ""})
    print("loaded:", receive())
    send({"type": "bandGuitar", "plays": True})
    send({"type": "play", "from": 0, "countIn": 4})
    print("playing; the first strum lands %g s after play (count-in + 1 beat)" % (5 * QUARTER))

    try:
        code = engine.wait(timeout=seconds + 120)
    except subprocess.TimeoutExpired:
        engine.kill()
        sys.exit("the engine did not stop in time")
    print("engine exit %d, recording in %s" % (code, out))
    print("check it with: python wav_check.py %s --bpm 96" % os.path.join(out, "band.wav"))


if __name__ == "__main__":
    main()
