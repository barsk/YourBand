r"""Render stills and videos of the built VR band scene, from the saved .blend or right after the build.

The build (build_vr_band_scene.py) is slow (posing, solving, animating), rendering is a separate quick step:
build once, then render any of the scene's shots as often as needed:

  & "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background mockup\vr_band_mockup.blend ^
      --python mockup\render.py -- mockup\renders [what] [quality] [options]

what (default: the three stills - headset view, over the shoulder, overview):
  debug       hand, keys and drum close-ups at the still frame
  portraits   head-and-shoulders and face views of the musicians
  anim        the videos (fretting_hand, guitarist, strumming_hand, drummer, bassist, kick_pedal,
              keyboardist), with the song's soundtrack when the build had one; `videos=a,b` picks some
  only=a,b    just these shots (by name)
quality:
  draft       (default) EEVEE without ray tracing: shadow maps and a flat ambient instead of bounce light
              and reflections - seconds per still, quick videos; for checking poses and motion
  final       Cycles stills (GPU, denoised) and ray-traced EEVEE videos with baked light probes
  preview     half resolution and fewer samples (with either)
options: frame=N (stills at frame N), step=N (videos: every Nth frame)
"""
import json
import os
import sys
import time

import bpy

COLLECTIONS = ("Room", "Band", "User", "HUD", "Labels", "Lights")
DRAFT_AMBIENT = (0.16, 0.15, 0.14)  # world colour in draft renders, standing in for the room's bounce light


def colour(scene):
    scene.view_settings.view_transform = "Filmic"  # AgX pulls maple and skin towards pink
    scene.view_settings.look = "Medium High Contrast"
    scene.view_settings.exposure = -0.35


def use_gpu():
    """Cycles on the GPU (OptiX) when there is one."""
    prefs = bpy.context.preferences.addons["cycles"].preferences
    prefs.compute_device_type = "OPTIX"
    prefs.get_devices()
    for d in prefs.devices:
        d.use = d.type == "OPTIX"
    return any(d.use for d in prefs.devices)


def cycles(scene, preview):
    scene.render.engine = "CYCLES"
    scene.cycles.device = "GPU" if use_gpu() else "CPU"
    scene.cycles.samples = 16 if preview else 64
    scene.cycles.use_denoising = True
    scene.cycles.denoising_use_gpu = True
    try:
        scene.cycles.denoiser = "OPENIMAGEDENOISE"
    except TypeError:
        pass
    scene.cycles.max_bounces = 6
    scene.render.resolution_x, scene.render.resolution_y = (960, 540) if preview else (1920, 1080)


def _world(scene, color):
    bg = next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND")
    bg.inputs["Color"].default_value = (*color, 1)


def eevee(scene, quality="draft", preview=False, video=False):
    """EEVEE. final: ray-traced reflections and global illumination, soft ray-traced shadows, the room's
    bounce light baked into a volume probe and reflections from a sphere probe. draft: none of that -
    shadow maps and a flat ambient."""
    scene.render.engine = "BLENDER_EEVEE"
    ee = scene.eevee
    ee.use_shadows = True
    if quality != "final":
        # (a frame costs ~0.15 s of scene sync plus ~9 ms per sample: videos take fewer samples)
        ee.taa_render_samples = (4 if preview else 8) if video else (8 if preview else 16)
        scene.render.resolution_x, scene.render.resolution_y = (960, 540) if preview else (1280, 720)
        ee.use_raytracing = False
        ee.use_fast_gi = False
        ee.shadow_ray_count, ee.shadow_step_count = 1, 4
        _world(scene, DRAFT_AMBIENT)
        for o in bpy.data.objects:  # (light probes from a final render in this session)
            if o.type == "LIGHT_PROBE":
                o.hide_render = True
        return
    ee.taa_render_samples = 16 if preview else 64
    scene.render.resolution_x, scene.render.resolution_y = (960, 540) if preview else (1280, 720)
    ee.use_raytracing = True
    ee.ray_tracing_method = "SCREEN"
    ee.ray_tracing_options.resolution_scale = "2" if preview else "1"
    ee.use_fast_gi = True
    ee.fast_gi_method = "GLOBAL_ILLUMINATION"
    ee.shadow_ray_count, ee.shadow_step_count = 2, 12
    if "room_light" not in bpy.data.objects:
        lights = bpy.data.collections["Lights"]
        for name, kind, loc, scale in (("room_reflections", "SPHERE", (0, 0.4, 1.3), None),
                                        ("room_light", "VOLUME", (0, 0.4, 1.35), (3.5, 3.7, 1.3))):
            probe = bpy.data.lightprobes.new(name, kind)
            if kind == "SPHERE":
                probe.influence_distance = 4.5
            else:
                probe.resolution_x, probe.resolution_y, probe.resolution_z = 12, 12, 6
            o = bpy.data.objects.new(name, probe)
            lights.objects.link(o)
            o.location = loc
            if scale:
                o.scale = scale
        bpy.context.view_layer.update()
        bpy.ops.object.lightprobe_cache_bake(subset="ALL")


def _show(hidden):
    for name in COLLECTIONS:
        if name in bpy.data.collections:
            bpy.data.collections[name].hide_render = name in hidden


def run(argv, out, scene=None):
    """Render what argv asks for (see the module doc) into `out`; returns the written files."""
    scene = scene or bpy.context.scene
    os.makedirs(out, exist_ok=True)
    quality = "final" if "final" in argv else "draft"
    preview = "preview" in argv
    shots = json.loads(scene["shots"])
    kinds = [k for k in ("debug", "portraits", "anim") if k in argv] or ["still"]
    only = [a[5:].split(",") for a in argv if a.startswith("only=")]
    pick = [a[7:].split(",") for a in argv if a.startswith("videos=")]
    world = tuple(next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND").inputs["Color"].default_value)
    colour(scene)
    written = []
    frame = next((int(a[6:]) for a in argv if a.startswith("frame=")), scene.get("still_frame", scene.frame_current))
    stills = [s for s in shots if s[3] in kinds and s[3] != "anim" and (not only or s[0] in only[0])]
    if stills:
        scene.frame_set(frame)
        if quality == "final":
            cycles(scene, preview)
        else:
            eevee(scene, quality, preview)
        scene.render.image_settings.media_type = "IMAGE"
        scene.render.image_settings.file_format = "PNG"
        for name, cam, hidden, _ in stills:
            _show(set(hidden))
            scene.camera = bpy.data.objects[cam]
            scene.render.filepath = os.path.join(out, name + ".png")
            t = time.perf_counter()
            bpy.ops.render.render(write_still=True)
            written.append(scene.render.filepath)
            print("RENDERED %s (%s, %.1f s)" % (scene.render.filepath, quality, time.perf_counter() - t))
    videos = [s for s in shots if s[3] == "anim" and "anim" in kinds and (not only or s[0] in only[0])
              and (not pick or s[0] in pick[0])]
    if videos:
        eevee(scene, quality, preview, video=True)
        scene.render.image_settings.media_type = "VIDEO"
        scene.render.image_settings.file_format = "FFMPEG"
        scene.render.ffmpeg.format = "MPEG4"
        scene.render.ffmpeg.codec = "H264"
        scene.render.ffmpeg.constant_rate_factor = "HIGH"
        if scene.sequence_editor:  # the song's soundtrack (a sound strip from the build)
            scene.render.ffmpeg.audio_codec = "AAC"
            scene.render.ffmpeg.audio_bitrate = 192
        scene.frame_step = next((int(a[5:]) for a in argv if a.startswith("step=")), 1)
        for name, cam, hidden, _ in videos:
            _show(set(hidden))
            scene.camera = bpy.data.objects[cam]
            scene.render.filepath = os.path.join(out, "%s_%s.mp4" % (name, scene.get("video_tag", "anim")))
            t = time.perf_counter()
            bpy.ops.render.render(animation=True)
            written.append(scene.render.filepath)
            print("RENDERED %s (%s, %.1f s)" % (scene.render.filepath, quality, time.perf_counter() - t))
    _world(scene, world[:3])
    if not written:
        print("Nothing to render: shots are", ", ".join("%s (%s)" % (s[0], s[3]) for s in shots))
    return written


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "shots" not in bpy.context.scene:
        sys.exit("render.py: open a .blend saved by build_vr_band_scene.py")
    out_dir = os.path.abspath(args[0]) if args and "=" not in args[0] and args[0] not in (
        "debug", "portraits", "anim", "draft", "final", "preview") else os.path.abspath("renders")
    run(args, out_dir)
