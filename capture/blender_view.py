"""Show extracted hand motion as animated 3D skeleton hands in Blender (3.5+).

Builds everything inside a "GuitarTutor <clip>" collection (re-running replaces
it), plus a camera matched to the filming camera with the tracking-overlay video
as its background, so the 3D hands can be compared against the footage.

Usage (opens Blender with the scene built):
  blender --python capture/blender_view.py -- output/ackord_hands.json
  blender --python capture/blender_view.py -- output/ackord_hands.json --video videos/ackord.mp4

Coordinates: OpenCV camera space (x right, y down, z forward) is mapped to
Blender as (x, z, -y), with the camera at the origin looking along +Y.
"""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy

ROLE_COLORS = {"fretting": (1.0, 0.42, 0.05, 1.0), "picking": (0.08, 0.5, 1.0, 1.0)}
JOINT_RADIUS = 0.0045
TIP_RADIUS = 0.0055
BONE_RADIUS = 0.0028
FINGERTIPS = {4, 8, 12, 16, 20}


def cv_to_blender(p):
    return (p[0], p[2], -p[1])


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    p = argparse.ArgumentParser(prog="blender_view.py")
    p.add_argument("json", type=Path)
    p.add_argument("--video", type=Path, help="bakgrundsvideo (standard: overlay-videon)")
    return p.parse_args(argv)


def fresh_collection(name):
    old = bpy.data.collections.get(name)
    if old:
        for obj in list(old.all_objects):
            bpy.data.objects.remove(obj, do_unlink=True)
        bpy.data.collections.remove(old)
    coll = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(coll)
    return coll


def material(name, rgba):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = rgba
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs[0].default_value = rgba
    return mat


def new_object(coll, name, mesh, mat):
    """Object sharing `mesh` but with its own material, so both hands can differ in color."""
    if not mesh.materials:
        mesh.materials.append(None)
    obj = bpy.data.objects.new(name, mesh)
    obj.material_slots[0].link = "OBJECT"
    obj.material_slots[0].material = mat
    coll.objects.link(obj)
    return obj


def sphere_mesh():
    mesh = bpy.data.meshes.get("GT_joint")
    if mesh:
        return mesh
    import bmesh
    mesh = bpy.data.meshes.new("GT_joint")
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=1.0)
    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = True
    return mesh


def bone_mesh():
    """Unit-length cylinder along +Y, so Stretch To scales it to the joint distance."""
    mesh = bpy.data.meshes.get("GT_bone")
    if mesh:
        return mesh
    segs = 10
    verts, faces = [], []
    for y in (0.0, 1.0):
        for k in range(segs):
            a = 2 * math.pi * k / segs
            verts.append((BONE_RADIUS * math.cos(a), y, BONE_RADIUS * math.sin(a)))
    for k in range(segs):
        n = (k + 1) % segs
        faces.append((k, n, segs + n, segs + k))
    mesh = bpy.data.meshes.new("GT_bone")
    mesh.from_pydata(verts, [], faces)
    for poly in mesh.polygons:
        poly.use_smooth = True
    return mesh


def fcurves_of(obj):
    action = obj.animation_data.action
    fcurves = getattr(action, "fcurves", None)
    if fcurves is None:  # Blender 5.0+ layered actions
        from bpy_extras import anim_utils
        fcurves = anim_utils.action_get_channelbag_for_slot(action, obj.animation_data.action_slot).fcurves
    return fcurves


def bulk_location_keys(obj, frames, coords):
    """Write all location keys at once; keyframe_insert per frame is far too slow."""
    obj.location = coords[0]
    obj.keyframe_insert("location", frame=frames[0])
    fcurves = fcurves_of(obj)
    for axis in range(3):
        fc = fcurves.find("location", index=axis)
        fc.keyframe_points.clear()
        fc.keyframe_points.add(len(frames))
        fc.keyframe_points.foreach_set("co", [v for f, c in zip(frames, coords) for v in (f, c[axis])])
        fc.update()


def visibility_keys(objs, frames_visible, first, last):
    """Hide the hand on frames where it was not tracked (keys only at transitions)."""
    state = None
    for f in range(first, last + 1):
        vis = f in frames_visible
        if vis != state:
            for obj in objs:
                obj.hide_viewport = obj.hide_render = not vis
                obj.keyframe_insert("hide_viewport", frame=f)
                obj.keyframe_insert("hide_render", frame=f)
            state = vis


def build_hand(coll, role, track, connections, mat, last_frame):
    frames = sorted(track)
    joints = []
    for j in range(21):
        obj = new_object(coll, f"{role}_j{j:02d}", sphere_mesh(), mat)
        obj.scale = (TIP_RADIUS if j in FINGERTIPS else JOINT_RADIUS,) * 3
        bulk_location_keys(obj, frames, [cv_to_blender(track[f][j]) for f in frames])
        joints.append(obj)

    bones = []
    for a, b in connections:
        obj = new_object(coll, f"{role}_b{a:02d}_{b:02d}", bone_mesh(), mat)
        loc = obj.constraints.new("COPY_LOCATION")
        loc.target = joints[a]
        stretch = obj.constraints.new("STRETCH_TO")
        stretch.target = joints[b]
        stretch.rest_length = 1.0
        stretch.volume = "NO_VOLUME"
        bones.append(obj)

    visibility_keys(joints + bones, set(frames), 1, last_frame)
    return joints + bones


def build_camera(coll, data, video):
    w, h = data["width"], data["height"]
    cam_data = bpy.data.cameras.new("GT_Camera")
    cam_data.sensor_fit = "HORIZONTAL" if w >= h else "VERTICAL"
    cam_data.sensor_width = cam_data.sensor_height = 36.0
    cam_data.lens = data["camera"]["K"][0][0] * 36.0 / max(w, h)
    cam_data.clip_start = 0.01
    cam = bpy.data.objects.new("GT_Camera", cam_data)
    cam.rotation_euler = (math.radians(90), 0, 0)
    coll.objects.link(cam)

    if video and video.exists():
        clip = bpy.data.movieclips.load(str(video))
        clip.frame_start = 1
        bg = cam_data.background_images.new()
        bg.source = "MOVIE_CLIP"
        bg.clip = clip
        bg.alpha = 0.85
        cam_data.show_background_images = True
    return cam


def look_through_camera():
    screen = bpy.context.screen
    if screen is None:  # background mode
        return
    for area in screen.areas:
        if area.type == "VIEW_3D":
            space = area.spaces.active
            space.region_3d.view_perspective = "CAMERA"
            space.shading.type = "SOLID"
            space.shading.color_type = "MATERIAL"


def main():
    args = parse_args()
    data = json.loads(args.json.read_text(encoding="utf-8"))
    clip = args.json.stem.removesuffix("_hands")
    coll = fresh_collection(f"GuitarTutor {clip}")

    scene = bpy.context.scene
    fps = data["fps"]
    scene.render.fps = max(1, round(fps))
    scene.render.fps_base = scene.render.fps / fps
    scene.render.resolution_x, scene.render.resolution_y = data["width"], data["height"]
    scene.frame_start, scene.frame_end = 1, len(data["frames"])

    video = args.video or (Path(data["overlay"]) if data.get("overlay") else None)
    scene.camera = build_camera(coll, data, video)

    connections = [tuple(c) for c in data["connections"]]
    for role, rgba in ROLE_COLORS.items():
        # Blender frame = video frame index + 1.
        track = {f["i"] + 1: f["hands"][role]["cam"] for f in data["frames"] if role in f["hands"]}
        if not track:
            print(f"GuitarTutor: ingen {role}-hand i {args.json.name}")
            continue
        build_hand(coll, role, track, connections, material(f"GT_{role}", rgba), scene.frame_end)
        print(f"GuitarTutor: {role}-hand, {len(track)} bildrutor")

    scene.frame_set(1)
    look_through_camera()


main()
