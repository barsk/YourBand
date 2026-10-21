"""A small synthesizer: a song's notes (song.Song) to a WAV file, so the videos sound like what the band plays.

Every note is drawn additively with numpy, so the sound comes from the same events as the animation:
- bass and guitar: plucked strings - harmonics that start bright and die away, the higher ones faster;
- keyboard: a piano-like tone with a soft attack and a long decay;
- drums (General MIDI map): a kick that drops in pitch, a snare of noise and tone, hi-hats of filtered
  noise (open ones ring longer), toms that drop in pitch, and long crash and ride washes.
"""
import wave

import numpy as np

RATE = 44100


def _env(n, attack, decay):
    t = np.arange(n) / RATE
    return np.minimum(1.0, t / max(attack, 1e-4)) * np.exp(-t / decay)


def _pluck(freq, dur, vel, brightness=1.0, decay=1.2):
    n = int((dur + 0.35) * RATE)
    t = np.arange(n) / RATE
    out = np.zeros(n)
    for h in range(1, 13):
        f = freq * h * (1 + 0.0004 * h * h)  # a little inharmonicity
        if f > RATE / 2:
            break
        a = brightness ** (h - 1) / h ** 1.2
        out += a * np.sin(2 * np.pi * f * t) * np.exp(-t * h ** 0.8 / decay)
    out *= _env(n, 0.002, 10.0)
    tail = np.clip((dur + 0.3 - t) / 0.3, 0, 1)  # damped when the note ends
    return out * tail * (vel / 127.0)


def _piano(freq, dur, vel):
    n = int((dur + 0.6) * RATE)
    t = np.arange(n) / RATE
    out = sum((0.6 ** (h - 1)) * np.sin(2 * np.pi * freq * h * t) * np.exp(-t * (0.6 + 0.5 * h)) for h in range(1, 8))
    tail = np.clip((dur + 0.5 - t) / 0.5, 0, 1)
    return out * _env(n, 0.004, 3.0) * tail * (vel / 127.0) * 0.8


def _noise(n, rng, hp=0.0):
    x = rng.standard_normal(n)
    if hp:  # crude high-pass: difference of the signal and its running average
        k = max(1, int(hp))
        x = x - np.convolve(x, np.ones(k) / k, mode="same")
    return x


def _drum(pitch, vel, dur, rng):
    v = vel / 127.0
    if pitch in (35, 36):  # kick
        n = int(0.5 * RATE)
        t = np.arange(n) / RATE
        f = 45 + 90 * np.exp(-t / 0.04)
        return 1.4 * v * np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.exp(-t / 0.18)
    if pitch in (37, 38, 39, 40):  # snare
        n = int(0.35 * RATE)
        t = np.arange(n) / RATE
        return v * (0.7 * _noise(n, rng, 3) * np.exp(-t / 0.09) + 0.5 * np.sin(2 * np.pi * 185 * t) * np.exp(-t / 0.06))
    if pitch in (42, 44, 46):  # hi-hat, closed / pedal / open
        length = 0.45 if pitch == 46 else 0.08
        n = int(length * RATE)
        t = np.arange(n) / RATE
        return 0.35 * v * _noise(n, rng, 2) * np.exp(-t / (length / 3))
    if pitch in (41, 43, 45, 47, 48, 50):  # toms
        base = {41: 80, 43: 95, 45: 110, 47: 130, 48: 150, 50: 175}[pitch]
        n = int(0.6 * RATE)
        t = np.arange(n) / RATE
        f = base * (1 + 0.4 * np.exp(-t / 0.05))
        return v * np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.exp(-t / 0.25)
    if pitch in (49, 52, 55, 57, 51, 53, 59):  # crash / ride
        length = 2.5 if pitch in (49, 52, 55, 57) else 1.2
        n = int(length * RATE)
        t = np.arange(n) / RATE
        bell = np.sin(2 * np.pi * 620 * t) * 0.15 if pitch in (51, 53, 59) else 0
        return 0.35 * v * (_noise(n, rng, 2) + bell) * np.exp(-t / (length / 3)) * _env(n, 0.003, 99)
    return np.zeros(1)


def render(song, path, first_beat=0.0, last_beat=None, gain=0.9):
    """Write the song's notes between two beats as a 16-bit stereo WAV (the band spread across the stage)."""
    last_beat = song.bars * song.beats_per_bar if last_beat is None else last_beat
    t0 = song.seconds(first_beat)
    length = song.seconds(last_beat) - t0 + 2.5
    mix = np.zeros((int(length * RATE), 2))
    rng = np.random.default_rng(7)
    pans = {"drums": 0.5, "bass": 0.35, "guitar": 0.7, "keys": 0.3, "other": 0.5}
    for part, notes in song.parts.items():
        for nt in notes:
            if not first_beat <= nt.start < last_beat:
                continue
            start = song.seconds(nt.start) - t0
            dur = max(0.05, song.seconds(nt.end) - song.seconds(nt.start))
            freq = 440.0 * 2 ** ((nt.pitch - 69) / 12)
            if part == "drums":
                x = _drum(nt.pitch, nt.velocity, dur, rng)
            elif part == "bass":
                x = 1.1 * _pluck(freq, dur, nt.velocity, brightness=0.55, decay=0.9)
            elif part == "guitar":
                x = 0.35 * _pluck(freq, dur, nt.velocity, brightness=0.75, decay=0.7)
            else:
                x = 0.45 * _piano(freq, dur, nt.velocity)
            i = int(start * RATE)
            j = min(len(mix), i + len(x))
            if j <= i:
                continue
            p = pans.get(part, 0.5)
            mix[i:j, 0] += x[:j - i] * (1 - p)
            mix[i:j, 1] += x[:j - i] * p
    peak = np.max(np.abs(mix)) or 1.0
    data = (mix / peak * gain * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data.tobytes())
    return path
