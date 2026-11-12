"""Extract 3D hand motion from a guitar video with MediaPipe HandLandmarker.

For each input video this writes, into the output folder:
  <name>_hands.json    per-frame landmarks for the fretting and picking hand
                       (image pixels, hand-local metres, camera-space metres)
  <name>_overlay.mp4   the video with tracking drawn on top, for judging quality

Camera-space positions come from fitting MediaPipe's metric hand shape to the
2D landmarks with solvePnP, using an approximate phone-camera field of view.
Depth is therefore rough, but enough to see the hands move in 3D.

Usage:
  python capture/extract_hands.py videos/ackord.mp4
  python capture/extract_hands.py videos/a.mp4 videos/b.mp4 --fov 70 --lefty
"""
import argparse
import itertools
import json
import math
import os
import sys
import time
from pathlib import Path

os.environ.setdefault("GLOG_minloglevel", "2")  # silence MediaPipe's C++ log spam

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks.python import BaseOptions, vision

from make_markers import ARUCO_DICT, MARKER_SIZES_MM

ROOT = Path(__file__).resolve().parent.parent
MODEL_PATH = ROOT / "models" / "hand_landmarker.task"
MODEL_URL = ("https://storage.googleapis.com/mediapipe-models/hand_landmarker/"
             "hand_landmarker/float16/latest/hand_landmarker.task")

LANDMARK_NAMES = [
    "wrist",
    "thumb_cmc", "thumb_mcp", "thumb_ip", "thumb_tip",
    "index_mcp", "index_pip", "index_dip", "index_tip",
    "middle_mcp", "middle_pip", "middle_dip", "middle_tip",
    "ring_mcp", "ring_pip", "ring_dip", "ring_tip",
    "pinky_mcp", "pinky_pip", "pinky_dip", "pinky_tip",
]
CONNECTIONS = [(c.start, c.end) for c in vision.HandLandmarksConnections.HAND_CONNECTIONS]
PALM = [0, 5, 9, 13, 17]
ROLES = ("fretting", "picking")
ROLE_COLORS_BGR = {"fretting": (0, 140, 255), "picking": (255, 160, 0)}

# A role keeps its identity across gaps up to this long (seconds).
TRACK_MEMORY_S = 0.5
# Cost of contradicting MediaPipe's handedness label, as a fraction of the image diagonal.
LABEL_MISMATCH_COST = 0.15


class OneEuro:
    """One Euro filter (Casiez et al. 2012), applied element-wise to an array."""

    def __init__(self, min_cutoff, beta, d_cutoff=1.0):
        self.min_cutoff, self.beta, self.d_cutoff = min_cutoff, beta, d_cutoff
        self.x = self.dx = self.t = None

    @staticmethod
    def _alpha(cutoff, dt):
        tau = 1.0 / (2 * math.pi * cutoff)
        return 1.0 / (1.0 + tau / dt)

    def reset(self):
        self.x = self.dx = self.t = None

    def __call__(self, x, t):
        if self.x is None:
            self.x, self.dx, self.t = x.copy(), np.zeros_like(x), t
            return self.x
        dt = max(t - self.t, 1e-6)
        a_d = self._alpha(self.d_cutoff, dt)
        self.dx = a_d * (x - self.x) / dt + (1 - a_d) * self.dx
        a = self._alpha(self.min_cutoff + self.beta * np.abs(self.dx), dt)
        self.x = a * x + (1 - a) * self.x
        self.t = t
        return self.x


def ensure_model():
    if MODEL_PATH.exists():
        return
    import urllib.request
    MODEL_PATH.parent.mkdir(parents=True, exist_ok=True)
    print(f"Laddar ner handmodellen till {MODEL_PATH} ...")
    urllib.request.urlretrieve(MODEL_URL, MODEL_PATH)


def camera_matrix(w, h, fov_long_deg):
    f = (max(w, h) / 2) / math.tan(math.radians(fov_long_deg) / 2)
    return np.array([[f, 0, w / 2], [0, f, h / 2], [0, 0, 1]], dtype=np.float64)


def place_in_camera(world, img_px, K, guess):
    """Pose the metric hand shape so it projects onto the 2D landmarks.

    Returns (camera-space points, (rvec, tvec), reprojection RMS in px) or None.
    """
    if guess is None:
        ok, rvec, tvec = cv2.solvePnP(world, img_px, K, None, flags=cv2.SOLVEPNP_SQPNP)
    else:
        ok, rvec, tvec = cv2.solvePnP(world, img_px, K, None, guess[0].copy(), guess[1].copy(),
                                      useExtrinsicGuess=True, flags=cv2.SOLVEPNP_ITERATIVE)
    if not ok or tvec[2, 0] <= 0:
        return None
    R, _ = cv2.Rodrigues(rvec)
    cam = world @ R.T + tvec.reshape(1, 3)
    proj, _ = cv2.projectPoints(world, rvec, tvec, K, None)
    err = float(np.sqrt(np.mean(np.sum((proj.reshape(-1, 2) - img_px) ** 2, axis=1))))
    return cam, (rvec, tvec), err


def assign_roles(detected, last_seen, t, diag, lefty):
    """Map detected hands to fretting/picking.

    Continuity with where each role was last seen dominates; MediaPipe's
    handedness label only decides when there is no recent history, because it
    flickers when hands overlap or the fretting hand is half hidden by the neck.
    """
    recent = {r: c for r, (c, ts) in last_seen.items() if t - ts <= TRACK_MEMORY_S}

    def cost(hand, role):
        c = 0.0
        if role in recent:
            c += np.linalg.norm(hand["palm_px"] - recent[role]) / diag
        label_role = "fretting" if (hand["label"] == "Left") != lefty else "picking"
        if label_role != role:
            c += LABEL_MISMATCH_COST * hand["score"]
        return c

    best, best_cost = {}, math.inf
    for roles in itertools.permutations(ROLES, len(detected)):
        c = sum(cost(h, r) for h, r in zip(detected, roles))
        if c < best_cost:
            best, best_cost = dict(zip(roles, detected)), c
    return best


def draw_overlay(frame, hands, markers, frame_i):
    for role, h in hands.items():
        color = ROLE_COLORS_BGR[role]
        pts = h["img_px"].astype(int)
        for a, b in CONNECTIONS:
            cv2.line(frame, tuple(pts[a]), tuple(pts[b]), color, 2, cv2.LINE_AA)
        for p in pts:
            cv2.circle(frame, tuple(p), 4, (255, 255, 255), -1, cv2.LINE_AA)
            cv2.circle(frame, tuple(p), 4, color, 1, cv2.LINE_AA)
        x, y = pts[0]
        cv2.putText(frame, f"{role} ({h['label']} {h['score']:.2f})", (int(x) + 8, int(y) + 20),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2, cv2.LINE_AA)
    for m in markers:
        c = np.array(m["corners"], dtype=np.int32)
        cv2.polylines(frame, [c], True, (0, 255, 0), 2, cv2.LINE_AA)
        cv2.putText(frame, f"id {m['id']}", tuple(c[0]), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)
    cv2.putText(frame, f"frame {frame_i}", (12, 32), cv2.FONT_HERSHEY_SIMPLEX, 0.9, (255, 255, 255), 2)


def rounded(a, nd=5):
    return np.round(a, nd).tolist()


def process_video(path, out_dir, args):
    cap = cv2.VideoCapture(str(path))
    if not cap.isOpened():
        print(f"Kunde inte öppna {path}", file=sys.stderr)
        return False
    fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    n_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    ok, frame = cap.read()
    if not ok:
        print(f"Inga bildrutor i {path}", file=sys.stderr)
        return False
    if args.rotate:
        frame = rotate(frame, args.rotate)
    h, w = frame.shape[:2]
    K = camera_matrix(w, h, args.fov)
    diag = math.hypot(w, h)

    out_dir.mkdir(parents=True, exist_ok=True)
    overlay_path = out_dir / f"{path.stem}_overlay.mp4"
    json_path = out_dir / f"{path.stem}_hands.json"
    scale = min(1.0, args.overlay_max / max(w, h))
    ow, oh = int(w * scale) // 2 * 2, int(h * scale) // 2 * 2
    writer = None
    if not args.no_overlay:
        writer = cv2.VideoWriter(str(overlay_path), cv2.VideoWriter_fourcc(*"mp4v"), fps, (ow, oh))

    options = vision.HandLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=str(MODEL_PATH)),
        running_mode=vision.RunningMode.VIDEO,
        num_hands=2,
        min_hand_detection_confidence=args.min_conf,
        min_hand_presence_confidence=args.min_conf,
        min_tracking_confidence=args.min_conf,
    )
    aruco = cv2.aruco.ArucoDetector(cv2.aruco.getPredefinedDictionary(ARUCO_DICT),
                                    cv2.aruco.DetectorParameters())

    filters = {r: OneEuro(args.min_cutoff, args.beta) for r in ROLES}
    pose_guess = {}
    last_seen = {}
    frames_out = []
    seen = {r: 0 for r in ROLES}
    reproj = []
    started = time.time()

    print(f"{path.name}: {w}x{h} @ {fps:.2f} fps, ~{n_frames} bildrutor")
    with vision.HandLandmarker.create_from_options(options) as landmarker:
        i = 0
        while ok:
            t = i / fps
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            result = landmarker.detect_for_video(
                mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), int(round(t * 1000)))

            detected = []
            for lms, world_lms, handed in zip(result.hand_landmarks, result.hand_world_landmarks,
                                              result.handedness):
                label = handed[0].category_name
                if not args.mirrored:
                    # MediaPipe labels hands as if the image were a mirrored selfie.
                    label = {"Left": "Right", "Right": "Left"}.get(label, label)
                img_px = np.array([[p.x * w, p.y * h] for p in lms], dtype=np.float64)
                detected.append({
                    "label": label,
                    "score": float(handed[0].score),
                    "img_px": img_px,
                    "world": np.array([[p.x, p.y, p.z] for p in world_lms], dtype=np.float64),
                    "palm_px": img_px[PALM].mean(axis=0),
                })

            hands = assign_roles(detected, last_seen, t, diag, args.lefty)
            frame_hands = {}
            for role in ROLES:
                hand = hands.get(role)
                if hand is None:
                    if role in last_seen and t - last_seen[role][1] > TRACK_MEMORY_S:
                        filters[role].reset()
                        pose_guess.pop(role, None)
                    continue
                placed = place_in_camera(hand["world"], hand["img_px"], K, pose_guess.get(role))
                if placed is None:
                    pose_guess.pop(role, None)
                    continue
                cam, pose_guess[role], err = placed
                if not args.no_smooth:
                    cam = filters[role](cam, t)
                last_seen[role] = (hand["palm_px"], t)
                seen[role] += 1
                reproj.append(err)
                frame_hands[role] = {
                    "label": hand["label"],
                    "score": round(hand["score"], 3),
                    "img_px": rounded(hand["img_px"], 1),
                    "world": rounded(hand["world"]),
                    "cam": rounded(cam),
                    "reproj_px": round(err, 2),
                }

            corners, ids, _ = aruco.detectMarkers(cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY))
            markers = []
            if ids is not None:
                markers = [{"id": int(mid), "corners": rounded(c.reshape(4, 2), 1)}
                           for mid, c in zip(ids.flatten(), corners)]

            frames_out.append({"i": i, "t": round(t, 4), "hands": frame_hands, "markers": markers})

            if writer is not None:
                draw_overlay(frame, hands, markers, i)
                writer.write(cv2.resize(frame, (ow, oh)) if scale < 1 else frame)

            i += 1
            if i % 60 == 0:
                print(f"  {i}/{n_frames} ({i / (time.time() - started):.1f} bildrutor/s)", end="\r")
            ok, frame = cap.read()
            if ok and args.rotate:
                frame = rotate(frame, args.rotate)

    cap.release()
    if writer is not None:
        writer.release()

    total = len(frames_out)
    stats = {
        "frames": total,
        "detected_pct": {r: round(100 * seen[r] / max(total, 1), 1) for r in ROLES},
        "both_hands_pct": round(100 * sum(len(f["hands"]) == 2 for f in frames_out) / max(total, 1), 1),
        "reproj_px_median": round(float(np.median(reproj)), 2) if reproj else None,
        "marker_frames_pct": round(100 * sum(bool(f["markers"]) for f in frames_out) / max(total, 1), 1),
    }
    data = {
        "format": "yourband-hands/1",
        "source": str(path.resolve()),
        "overlay": str(overlay_path.resolve()) if writer is not None else None,
        "fps": fps,
        "width": w,
        "height": h,
        "camera": {"fov_long_deg": args.fov, "K": K.tolist()},
        "mirrored": args.mirrored,
        "lefty": args.lefty,
        "smoothing": None if args.no_smooth else {"min_cutoff": args.min_cutoff, "beta": args.beta},
        "landmark_names": LANDMARK_NAMES,
        "connections": CONNECTIONS,
        "marker_sizes_mm": MARKER_SIZES_MM,
        "stats": stats,
        "frames": frames_out,
    }
    json_path.write_text(json.dumps(data, separators=(",", ":")), encoding="utf-8")

    print(" " * 60, end="\r")
    print(f"  klar på {time.time() - started:.0f} s")
    print(f"  greppande hand hittad i {stats['detected_pct']['fretting']} % av bildrutorna, "
          f"anslagshand i {stats['detected_pct']['picking']} %, båda samtidigt i {stats['both_hands_pct']} %")
    print(f"  median reprojektionsfel {stats['reproj_px_median']} px, markörer synliga i {stats['marker_frames_pct']} %")
    print(f"  -> {json_path}")
    if writer is not None:
        print(f"  -> {overlay_path}")
    return True


def rotate(frame, degrees):
    return cv2.rotate(frame, {90: cv2.ROTATE_90_CLOCKWISE, 180: cv2.ROTATE_180,
                              270: cv2.ROTATE_90_COUNTERCLOCKWISE}[degrees])


def main():
    p = argparse.ArgumentParser(description="Extrahera handrörelser ur gitarrvideo.")
    p.add_argument("videos", nargs="+", type=Path)
    p.add_argument("--out", type=Path, default=ROOT / "output")
    p.add_argument("--fov", type=float, default=67.0,
                   help="kamerans synfält längs bildens långsida i grader (mobilens huvudkamera ~65-72)")
    p.add_argument("--rotate", type=int, choices=[90, 180, 270], help="rotera bilden medurs")
    p.add_argument("--mirrored", action="store_true",
                   help="videon är spegelvänd (selfie-läge som sparar spegelbilden)")
    p.add_argument("--lefty", action="store_true", help="vänsterhänt gitarrist (greppar med höger hand)")
    p.add_argument("--min-conf", type=float, default=0.5)
    p.add_argument("--no-smooth", action="store_true")
    p.add_argument("--min-cutoff", type=float, default=1.5, help="One Euro: utjämning vid stillastående (Hz)")
    p.add_argument("--beta", type=float, default=10.0, help="One Euro: hur snabbt filtret släpper igenom rörelse")
    p.add_argument("--overlay-max", type=int, default=1280, help="max långsida i pixlar för overlay-videon")
    p.add_argument("--no-overlay", action="store_true")
    args = p.parse_args()

    ensure_model()
    ok = True
    for video in args.videos:
        ok &= process_video(video, args.out, args)
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
