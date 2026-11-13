"""Writes a band instrument's saved plugin states out of a .sounds file, as files plugin_probe can load with --state.

usage: sounds_state.py <file.sounds> <out-folder> [instrument=rhythm]

A .sounds file (%APPDATA%\\YourBand\\band-defaults.sounds, or songs\\<song>.sounds) keeps each plugin state as JUCE's
base64 text, "<size>.<data>" (six bits a character, least significant bit first). This decodes the state of the
instrument (rhythm, bass, keys or drums) of every playstyle into <out-folder>/<instrument>-<playstyle>.state, complete
VST3 states (they start with "VC2!"), the form the engine saves and plugin_probe loads. The source file is only read.

  python sounds_state.py "%APPDATA%\\YourBand\\band-defaults.sounds" out\\states
  plugin_probe "Kontakt 8.vst3" out --state out\\states\\rhythm-ballad.state --sweep-test ...
"""
import hashlib
import json
import os
import sys

ALPHABET = ".ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+"


def decode(text):
    size_text, data = text.split('.', 1)
    size = int(size_text)
    out = bytearray(size)
    position = 0
    for ch in data:
        index = ALPHABET.index(ch)
        for bit in range(6):
            if index >> bit & 1 and position // 8 < size:
                out[position // 8] |= 1 << (position % 8)
            position += 1
    return bytes(out)


if len(sys.argv) < 3:
    sys.exit(__doc__)
source, folder = sys.argv[1], sys.argv[2]
instrument = sys.argv[3] if len(sys.argv) > 3 else 'rhythm'
os.makedirs(folder, exist_ok=True)
sounds = json.load(open(source, encoding='utf-8'))
for style, parts in sounds.get('styles', {}).items():
    sound = parts.get(instrument)
    if not sound or 'state' not in sound:
        print('%-7s no %s state' % (style, instrument))
        continue
    raw = decode(sound['state'])
    path = os.path.join(folder, '%s-%s.state' % (instrument, style.lower()))
    with open(path, 'wb') as f:
        f.write(raw)
    print('%-7s %7d bytes  %s  sha1 %s  plugin %s' % (style, len(raw), raw[:4], hashlib.sha1(raw).hexdigest()[:10],
                                                     os.path.basename(sound.get('plugin', ''))))
