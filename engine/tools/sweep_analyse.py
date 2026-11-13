"""Level and build-up time of each stroke in a plugin_probe --sweep-test recording.

usage: sweep_analyse.py <recording.wav> <first-stroke-beat> <gap-beats> <velocity,velocity,...> [lag-ms=15]

plugin_probe plays at 96 bpm and 48 kHz and writes its recording as a 24-bit WAV. Give the --strum (first stroke, default
5), --gap and --vels the test was run with. For each stroke it prints:

  peak dB          the loudest sample after the strum key, in dB (the probe's output can be above 0 dBFS: it is a float render)
  rms dB           the RMS over the first 400 ms after the key
  t10, t50, t90    when the 1 ms peak envelope first reaches 10, 50 and 90 % of the stroke's peak, in ms after the key:
                   how long the sweep across the strings takes to build (t50 is the steady one; t90 jumps to a later
                   swell of the chord's body)

and then the averages per velocity (a test with each velocity twice, up and down, shows any ordering effect).

Example (docs/shreddage-keyswitches.md, "What a stroke's velocity does"):

  plugin_probe "Kontakt 8.vst3" out --state sound.state --sweep-test --dirs dddddddddd --vels 20,40,64,96,127,127,96,64,40,20 --n 10 --gap 2 --key 400
  python sweep_analyse.py out/sound.sweep.wav 5 2 20,40,64,96,127,127,96,64,40,20

Pure Python, no libraries.
"""
import math
import sys
import wave

if len(sys.argv) < 5:
    sys.exit(__doc__)
path, first, gap, vels = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), [int(v) for v in sys.argv[4].split(',')]
lag_ms = float(sys.argv[5]) if len(sys.argv) > 5 else 15.0
rate = 48000
beat = rate * 60 / 96
ms = rate // 1000  # samples per millisecond

w = wave.open(path, 'rb')
if w.getsampwidth() != 3:
    sys.exit('expected a 24-bit WAV (plugin_probe writes one)')
channels, frames = w.getnchannels(), w.getnframes()
raw = w.readframes(frames)
mono = [0.0] * frames
for i in range(frames):
    total = 0
    for c in range(channels):
        o = (i * channels + c) * 3
        total += int.from_bytes(raw[o:o + 3], 'little', signed=True)
    mono[i] = total / 8388608.0


def db(x):
    return 20 * math.log10(x + 1e-9)


print('vel   peak dB   rms dB (0-400 ms)   t10    t50    t90 (ms after the key)')
rows = []
for i, v in enumerate(vels):
    s0 = int(round((first + gap * i) * beat + lag_ms * ms))
    window = mono[s0 - 10 * ms: s0 + 600 * ms]
    env = [max(abs(x) for x in window[k * ms:(k + 1) * ms]) for k in range(len(window) // ms)]
    peak = max(env)
    head = mono[s0: s0 + 400 * ms]
    rms = math.sqrt(sum(x * x for x in head) / len(head))

    def reach(fraction):
        for k, e in enumerate(env):
            if e >= fraction * peak:
                return k - 10
        return None
    rows.append((v, db(peak), db(rms), reach(0.10), reach(0.50), reach(0.90)))
    print('%3d  %8.1f  %10.1f          %4s  %5s  %5s' % rows[-1])

print()
by = {}
for row in rows:
    by.setdefault(row[0], []).append(row[1:])
print('averaged over repeats:')
print('vel   peak dB   rms dB   t10   t50   t90')
for v in sorted(by):
    xs = by[v]
    n = len(xs)
    print('%3d  %8.1f  %7.1f  %5.0f %5.0f %5.0f' % ((v,) + tuple(sum(x[k] for x in xs) / n for k in range(5))))
