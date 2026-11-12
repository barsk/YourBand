"""A silent test of the band's two lead channels, without a game build.

  python lead_session.py <out-folder> [--seconds 16] [--lead1 "Numa Player"] [--lead2 "Kontakt 8"] [--tab]

With --tab the song instead has the tab's single-string notes (the guitarist's, flagged single) and no lead parts:
they are played by the lead 1 plugin, while the band's guitarist plays (it always does since 2026-11-12).

Starts the engine silently with a recording and the given plugins on the lead channels, loads a short song
whose only notes are a C major scale on lead 1 (part 4, bars 1-2) and an A minor scale on lead 2 (part 5,
bars 3-4), plays it, and waits for the engine to run out. Then check the recording with:

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


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    out = os.path.abspath(sys.argv[1])
    seconds, lead1, lead2 = 16, "Numa Player", "synthesized"
    rest = sys.argv[2:]
    tab = "--tab" in rest
    if tab:
        rest.remove("--tab")
    for option in ("--seconds", "--lead1", "--lead2"):
        if option in rest:
            value = rest[rest.index(option) + 1]
            del rest[rest.index(option):rest.index(option) + 2]
            if option == "--seconds":
                seconds = int(value)
            elif option == "--lead1":
                lead1 = value
            else:
                lead2 = value
    if rest:
        sys.exit("unknown options: %s" % rest)
    os.makedirs(out, exist_ok=True)
    engine = subprocess.Popen([ENGINE, "--silent", "--record", os.path.join(out, "band.wav"), "--run-for", str(seconds),
                               "--lead1", lead1, "--lead2", lead2])
    conn = None
    deadline = time.time() + 90
    while time.time() < deadline:
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

    send({"type": "hello", "client": "lead_session", "protocol": 1})
    print("engine:", receive())
    notes = []
    for i, p in enumerate([60, 62, 64, 65, 67, 69, 71, 72]):  # lead 1: C major, eighths over bars 1-2
        notes.append([4, p, i * 0.5, 0.45, 96])
    for i, p in enumerate([57, 59, 60, 62, 64, 65, 67, 69]):  # lead 2: A minor, bars 3-4
        notes.append([5, p, 4 + i * 0.5, 0.45, 96])
    leads = ["Lead one", "Lead two"]
    if tab:  # the tab's single-string notes (guitar, part 3, flag 1) and no lead parts: they go to lead 1
        notes = [[3, p, i * 0.5, 0.45, 96, 1] for i, p in enumerate([60, 62, 64, 65, 67, 69, 71, 72])]
        leads = []
    send({"type": "load", "title": "Lead Test", "style": "Rock", "tempos": [[0, BPM]], "meters": [[0, 4, 4]],
          "notes": notes, "voicings": [], "strokes": [], "spans": [], "leads": leads, "keys": ""})
    print("loaded:", receive())
    send({"type": "play", "from": 0, "countIn": 4})
    code = engine.wait(timeout=seconds + 120)
    print("engine exit %d, recording in %s" % (code, out))


if __name__ == "__main__":
    main()
