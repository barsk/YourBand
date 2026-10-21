"""Disk cache for slow, deterministic build steps (solver searches, processed instrument models).

An entry is keyed by a fingerprint of everything the step depends on - its numeric inputs (positions,
frames, measured geometry, rounded to a micrometre) and the source code of the modules that compute it -
so editing the code, moving a player or changing an instrument simply misses the cache and recomputes.
Entries live in mockup/cache/<name>.pkl; delete the folder (or pass `nocache` to the build) to start over.
"""
import hashlib
import os
import pickle
import re
import sys

DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "cache")
ENABLED = "nocache" not in sys.argv


def plain(x):
    """A value as nested tuples of rounded numbers (Vectors, Matrices, dicts, lists...), for hashing."""
    if isinstance(x, float):
        return round(x, 6) + 0.0  # (+0.0: -0.0 and 0.0 hash alike)
    if isinstance(x, (int, str, bool, type(None))):
        return x
    if isinstance(x, dict):
        return tuple(sorted((repr(k), plain(v)) for k, v in x.items()))
    if hasattr(x, "to_tuple") or hasattr(x, "row"):  # mathutils Vector / Matrix / Quaternion
        try:
            return tuple(plain(v) for v in x)
        except TypeError:
            pass
    if isinstance(x, (list, tuple, set, frozenset)):
        items = [plain(v) for v in x]
        return tuple(sorted(items, key=repr) if isinstance(x, (set, frozenset)) else items)
    try:
        return tuple(plain(v) for v in x)
    except TypeError:
        return repr(x)


def code_hash(*files):
    here = os.path.dirname(os.path.abspath(__file__))
    h = hashlib.sha1()
    for f in files:
        with open(os.path.join(here, f), "rb") as fh:
            h.update(fh.read())
    return h.hexdigest()[:12]


def fingerprint(*parts):
    return hashlib.sha1(repr(plain(parts)).encode()).hexdigest()


class Cache:
    """{key: value} for one kind of step; values must pickle (no mathutils types: use tuples)."""

    def __init__(self, name, code):
        self.path = os.path.join(DIR, name + ".pkl")
        self.code = code_hash(*code)
        self.data = None
        self.hits = self.misses = 0

    def _load(self):
        if self.data is None:
            self.data = {}
            if ENABLED and os.path.exists(self.path):
                try:
                    with open(self.path, "rb") as fh:
                        self.data = pickle.load(fh)
                except Exception as e:  # a broken cache is only a slower build
                    print("cache %s unreadable (%s): starting empty" % (self.path, e))

    def key(self, *parts):
        return fingerprint(self.code, *parts)

    def get(self, key):
        self._load()
        v = self.data.get(key) if ENABLED else None
        if v is None:
            self.misses += 1
        else:
            self.hits += 1
        return v

    def put(self, key, value):
        """Store and write through (merging with what other builds wrote meanwhile)."""
        self._load()
        self.data[key] = value
        if not ENABLED:
            return
        os.makedirs(DIR, exist_ok=True)
        disk = {}
        if os.path.exists(self.path):
            try:
                with open(self.path, "rb") as fh:
                    disk = pickle.load(fh)
            except Exception:
                pass
        disk.update(self.data)
        self.data = disk
        tmp = "%s.%d.tmp" % (self.path, os.getpid())
        with open(tmp, "wb") as fh:
            pickle.dump(disk, fh)
        os.replace(tmp, self.path)

    def stats(self):
        return "%s: %d hits, %d misses" % (os.path.basename(self.path), self.hits, self.misses)


# ------------------------------------------------------------------ models
def _freeze(x, keys):
    """Measured data as plain picklable values: objects as their part key, mathutils types as tuples."""
    import bpy
    from mathutils import Matrix, Quaternion, Vector
    if isinstance(x, bpy.types.Object):
        return ("__obj__", keys[x.as_pointer()])
    if isinstance(x, Vector):
        return ("__vec__", tuple(x))
    if isinstance(x, Matrix):
        return ("__mat__", tuple(tuple(r) for r in x))
    if isinstance(x, Quaternion):
        return ("__quat__", tuple(x))
    if isinstance(x, dict):
        return {k: _freeze(v, keys) for k, v in x.items()}
    if isinstance(x, list):
        return [_freeze(v, keys) for v in x]
    if isinstance(x, tuple):
        return tuple(_freeze(v, keys) for v in x)
    return x


def _thaw(x, parts):
    from mathutils import Matrix, Quaternion, Vector
    if isinstance(x, tuple) and len(x) == 2 and isinstance(x[0], str) and x[0].startswith("__"):
        tag, v = x
        if tag == "__obj__":
            return parts[v]
        return {"__vec__": Vector, "__mat__": Matrix, "__quat__": Quaternion}[tag](v)
    if isinstance(x, dict):
        return {k: _thaw(v, parts) for k, v in x.items()}
    if isinstance(x, list):
        return [_thaw(v, parts) for v in x]
    if isinstance(x, tuple):
        return tuple(_thaw(v, parts) for v in x)
    return x


def model(name, sources, code, prepare, *args):
    """A processed instrument model: prepare(*args) -> (parts {key: object}, measured data) imports, cleans
    and measures it in model space (unparented, before placing). The parts are kept in a .blend library
    (mockup/cache/<name>_<key>.blend) and the data next to it; later builds append them instead, as long
    as the source files (by size and time), the code and the arguments are unchanged."""
    import bpy
    stamp = [(os.path.basename(s), os.path.getsize(s), int(os.path.getmtime(s))) for s in sources
             if os.path.exists(s)]
    key = fingerprint(code_hash(*code), stamp, name, args)[:12]
    lib = os.path.join(DIR, "%s_%s.blend" % (name, key))
    meta = lib[:-6] + ".pkl"
    if ENABLED and os.path.exists(lib) and os.path.exists(meta):
        with open(meta, "rb") as fh:
            names, data = pickle.load(fh)
        with bpy.data.libraries.load(lib, link=False) as (_, dst):
            dst.objects = [names[k] for k in names]
        parts = dict(zip(names, dst.objects))
        for o in parts.values():
            bpy.context.scene.collection.objects.link(o)
        bpy.context.view_layer.update()
        print("    %s: appended from %s" % (name, os.path.basename(lib)))
        return parts, _thaw(data, parts)
    parts, data = prepare(*args)
    if ENABLED:
        os.makedirs(DIR, exist_ok=True)
        old = re.compile(re.escape(name) + r"_[0-9a-f]{12}\.(blend|pkl)$")
        for f in os.listdir(DIR):  # older versions of this model
            if old.match(f):
                os.remove(os.path.join(DIR, f))
        keys = {o.as_pointer(): k for k, o in parts.items()}
        bpy.data.libraries.write(lib, set(parts.values()), path_remap="NONE", fake_user=False)
        with open(meta, "wb") as fh:
            pickle.dump(({k: o.name for k, o in parts.items()}, _freeze(data, keys)), fh)
    return parts, data
