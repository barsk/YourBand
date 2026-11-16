"""Plays a song's messages through the silent engine, with the song's own saved sounds, and records the result.

  songtool engine-messages songs-local\\x.song.json -o out\\x.jsonl
  python replay_session.py <out-folder> <x.jsonl> [--from <quarter>] [--count-in <quarters>] [--seconds 30]
                           [--only guitar|leads] [--speed 1.0]

engine-messages writes what the game sends the engine when it opens the song (tracks, then load: the band as the song's
.band.json connects it). The engine starts silently (no audio device) and records the band to <out-folder>/band.wav; as the
load message names the song, it puts the band on the sounds saved for that song (%APPDATA%\\YourBand\\songs\\<song>.sounds,
read, never written: a silent engine saves nothing), so what you hear in the game is what is recorded. --only guitar leaves
the drums, bass and keys out of the load, to hear the band guitar alone; --only leads keeps the two lead channels too.
--seconds is how long the engine runs after it starts playing.

Then look at the level over time, for instance with the repository's wav_check.py (needs numpy), or any editor.
"""
import json
import os
import socket
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ENGINE = os.path.join(REPO, "engine", "build", "Release", "gt_engine_artefacts", "Release", "YourBand Engine.exe")
PORT = 47800
PARTS = {"drums": 0, "bass": 1, "keys": 2, "guitar": 3, "lead1": 4, "lead2": 5}


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    out, messages = os.path.abspath(sys.argv[1]), sys.argv[2]
    rest = sys.argv[3:]

    def option(name, default, cast=str):
        if name in rest:
            value = cast(rest[rest.index(name) + 1])
            del rest[rest.index(name):rest.index(name) + 2]
            return value
        return default
    play_from, count_in = option("--from", 0.0, float), option("--count-in", 0.0, float)
    seconds, only, speed = option("--seconds", 30, int), option("--only", None), option("--speed", 1.0, float)
    if rest:
        sys.exit("unknown options: %s" % rest)
    lines = [json.loads(line) for line in open(messages, encoding="utf-8") if line.strip()]
    if only:
        keep = {PARTS["guitar"]} | ({PARTS["lead1"], PARTS["lead2"]} if only == "leads" else set())
        for message in lines:
            if message.get("type") == "load":
                message["notes"] = [n for n in message["notes"] if n[0] in keep]
                if only == "guitar":
                    message["wheel"] = [w for w in message["wheel"] if w[0] in keep]
    os.makedirs(out, exist_ok=True)
    engine = subprocess.Popen([ENGINE, "--silent", "--record", os.path.join(out, "band.wav"), "--run-for", str(seconds)])
    conn = None
    deadline = time.time() + 180
    while time.time() < deadline:  # the engine loads its plugins before it listens
        try:
            conn = socket.create_connection(("127.0.0.1", PORT), timeout=1.0)
            break
        except OSError:
            time.sleep(0.5)
    if conn is None:
        engine.kill()
        sys.exit("the engine did not open the port")
    conn.settimeout(180)
    f = conn.makefile("rwb")

    def send(obj):
        f.write((json.dumps(obj) + "\n").encode("utf-8"))
        f.flush()

    def receive():
        return json.loads(f.readline().decode("utf-8"))
    send({"type": "hello", "client": "replay_session", "protocol": 1})
    receive()
    for message in lines:
        send(message)
    while True:  # the engine puts the band on the song's sounds before it answers
        answer = receive()
        if answer.get("type") == "loaded":
            print("loaded:", {k: v for k, v in answer.items() if k != "type"})
            break
    if speed != 1.0:
        send({"type": "speed", "value": speed})
    send({"type": "play", "from": play_from, "countIn": count_in})
    print("playing from quarter %g after a count-in of %g quarters" % (play_from, count_in))
    try:
        code = engine.wait(timeout=seconds + 120)
    except subprocess.TimeoutExpired:
        engine.kill()
        sys.exit("the engine did not stop in time")
    print("engine exit %d, recording in %s" % (code, out))


if __name__ == "__main__":
    main()
