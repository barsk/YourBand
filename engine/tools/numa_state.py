"""Numa Player states: choose the keyboardist's sound without opening Numa Player's window.

Numa Player has no program list for a host. Its sounds sit in "zones" inside the plugin's state, each naming
its sound as "<library>/<sound>" (for example "5 - ORGANS/4 - ROCK B3"). A new plugin instance has no zone and
stays silent, so the engine gives it a state with one: taken from the standalone app (which remembers the
last sound) and with the enabled zone's sound swapped.

  python numa_state.py list
  python numa_state.py extract <out.bin>                  the standalone's current state
  python numa_state.py sound <in.bin> "<library>/<sound>" <out.bin>

A .bin file is the plugin's own state in JUCE's binary XML format, as engine/tools/PluginProbe.cpp takes with
--state. Test a sound with:
  plugin_probe "C:\\Program Files\\Common Files\\VST3\\Numa Player.vst3" out --state <file.bin>
"""
import os
import re
import struct
import sys

NUMA = os.path.join(os.environ.get("APPDATA", ""), "Studiologic", "Numa Player")
ALPHABET = ".ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+"


def juce_base64(text):
    """Decode JUCE's MemoryBlock::toBase64Encoding ("<size>.<6-bit characters>", least significant bit first)."""
    size, data = text.split(".", 1)
    out = bytearray(int(size))
    bit = 0
    for ch in data:
        v = ALPHABET.index(ch)
        for b in range(6):
            if v & (1 << b):
                byte, off = divmod(bit + b, 8)
                if byte < len(out):
                    out[byte] |= 1 << off
        bit += 6
    return bytes(out)


def split(state):
    """JUCE's binary XML: 'VC2!' + uint32 length (with the trailing zero) + UTF-8 XML; returns (xml, rest)."""
    if state[:4] != b"VC2!":
        raise ValueError("not a JUCE binary XML state")
    n = struct.unpack("<I", state[4:8])[0]
    return state[8:8 + n].rstrip(b"\0").decode("utf-8"), state[8 + n:]


def join(xml, rest=b""):
    data = xml.encode("utf-8") + b"\0"
    return b"VC2!" + struct.pack("<I", len(data)) + data + rest


def sounds():
    """Installed sounds as "<library>/<sound>" names."""
    root = os.path.join(NUMA, "Libraries")
    for lib in sorted(os.listdir(root)):
        folder = os.path.join(root, lib, "sounds")
        if lib.endswith(".numalib") and os.path.isdir(folder):
            for f in sorted(os.listdir(folder), key=lambda s: int(re.match(r"\d+", s).group()) if re.match(r"\d+", s) else 0):
                if f.endswith(".numares"):
                    yield "%s/%s" % (lib[:-len(".numalib")], f[:-len(".numares")])


def extract():
    text = open(os.path.join(NUMA, "Numa Player V2.settings"), encoding="utf-8").read()
    return juce_base64(re.search(r'name="filterState" val="([^"]+)"', text).group(1))


def with_sound(state, sound):
    """The state with its enabled zone (the first zone if none is enabled) playing `sound`."""
    xml, rest = split(state)
    zones = list(re.finditer(r'<ZONE_STATE [^>]*ZONE_LIBRARY_ID="([^"]*)"[^>]*>(.*?)</ZONE_STATE>', xml, re.S))
    if not zones:
        raise ValueError("the state has no zone to give a sound; save one from the standalone app first")
    enabled = [z for z in zones if re.search(r'<PARAM id="ZONE_ENABLE_ID" value="1(\.0)?"', z.group(2))]
    z = (enabled or zones)[0]
    start, end = z.span(1)
    return join(xml[:start] + sound + xml[end:], rest)


def main(args):
    if args[:1] == ["list"]:
        for s in sounds():
            print(s)
    elif args[:1] == ["extract"] and len(args) == 2:
        open(args[1], "wb").write(extract())
    elif args[:1] == ["sound"] and len(args) == 4:
        if args[2] not in set(sounds()):
            sys.exit("unknown sound %r (see: numa_state.py list)" % args[2])
        open(args[3], "wb").write(with_sound(open(args[1], "rb").read(), args[2]))
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
