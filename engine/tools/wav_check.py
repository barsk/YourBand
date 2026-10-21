"""Checks a recording of the band (as `GuitarTutor Engine.exe --silent --record <file.wav>` writes it).

  python wav_check.py <file.wav> [--bpm 96] [--grid 8] [--onsets 40]

Prints the level, where sound is (per second), and the onsets: the first one, and how far the others are
from a grid of eighth notes (--grid 8) at the tempo, counted from the first. Needs numpy (the repository's .venv).
"""
import argparse
import wave

import numpy as np


def read(path):
    with wave.open(path, "rb") as w:
        channels, width, rate, frames = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        data = w.readframes(frames)
    if width == 3:
        raw = np.frombuffer(data, dtype=np.uint8).reshape(-1, 3)
        ints = (raw[:, 0].astype(np.int32) | (raw[:, 1].astype(np.int32) << 8) | (raw[:, 2].astype(np.int32) << 16))
        ints = np.where(ints >= 1 << 23, ints - (1 << 24), ints)
        samples = ints / float(1 << 23)
    elif width == 2:
        samples = np.frombuffer(data, dtype=np.int16) / 32768.0
    else:
        raise SystemExit("unsupported sample width %d" % width)
    return samples.reshape(-1, channels), rate


def onsets(mono, rate, hop=0.005):
    """Times where the level jumps: a 5 ms energy envelope, rises above a running threshold."""
    n = int(rate * hop)
    frames = len(mono) // n
    energy = np.sqrt(np.mean(mono[: frames * n].reshape(frames, n) ** 2, axis=1) + 1e-12)
    db = 20 * np.log10(energy)
    rise = np.diff(db, prepend=db[0])
    found, last = [], -1.0
    for i in range(2, frames):
        t = i * hop
        # a rise of 6 dB within 10 ms, above -50 dB, at least 60 ms after the last
        if db[i] > -50 and db[i] - min(db[i - 2], db[i - 1]) > 6 and t - last > 0.06:
            found.append((t, db[i]))
            last = t
    return found, db


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("wav")
    parser.add_argument("--bpm", type=float, default=0)
    parser.add_argument("--grid", type=int, default=8)
    parser.add_argument("--onsets", type=int, default=40)
    a = parser.parse_args()

    samples, rate = read(a.wav)
    mono = samples.mean(axis=1)
    print("%s: %.1f s, %d channels, %d Hz, peak %.2f, RMS %.3f" % (a.wav, len(mono) / rate, samples.shape[1], rate,
                                                                 np.abs(samples).max(), np.sqrt(np.mean(samples ** 2))))
    seconds = len(mono) // rate
    levels = [20 * np.log10(np.sqrt(np.mean(mono[s * rate:(s + 1) * rate] ** 2)) + 1e-9) for s in range(seconds)]
    print("level per second (dB):", " ".join("%d" % max(-99, round(l)) for l in levels))

    found, _ = onsets(mono, rate)
    print("%d onsets" % len(found))
    if not found:
        return
    first = found[0][0]
    print("first at %.3f s" % first)
    for t, level in found[: a.onsets]:
        line = "  %7.3f s  %5.1f dB" % (t, level)
        if a.bpm:
            step = 60.0 / a.bpm * 4.0 / a.grid
            k = round((t - first) / step)
            line += "   grid %3d  off %+5.1f ms" % (k, (t - first - k * step) * 1000)
        print(line)
    if a.bpm:
        step = 60.0 / a.bpm * 4.0 / a.grid
        off = [abs((t - first) - round((t - first) / step) * step) * 1000 for t, _ in found]
        print("off the grid: median %.1f ms, 95%% %.1f ms, worst %.1f ms (the envelope has 5 ms steps)"
              % (np.median(off), np.percentile(off, 95), max(off)))


if __name__ == "__main__":
    main()
