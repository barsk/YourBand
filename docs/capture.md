# Hand capture from video
> [!Note]
> The feature documented here resembles an early planned feature, that is not in the current release, and may never be. Kept here as a references document.


An early experiment for the teacher avatar of the plan's "Later" list ([plan.md](plan.md)): how well can the hands
of a guitarist be extracted from ordinary phone video, as a cheaper alternative to motion capture? The scripts are
in [`capture/`](../capture), the printable markers in [`markers/`](../markers). Nothing in the band or the game
depends on them.

## Step 1: film

**Camera:** a phone on a tripod, 1-1.5 m away, level with the neck. Film from the front and a little from the
neck side, so that the fretboard and the fretting hand are clearly visible.

- **60 fps** is enough for chords; use 120 or 240 fps for solos.
- 1080p, even and strong light with no backlight, and a calm background.
- Roll up your sleeves and take off your rings.
- Start slowly. Hold each chord for about 3 seconds and strum it a few times.

**Suggested test clips:**

1. `oppna_ackord.mp4`: C, G, D, Am, Em and E, in that order.
2. `barre.mp4`: F (E shape), Bm (A shape), then a change F, C, G.
3. *(optional)* a short pentatonic scale or a riff, to see where the limit is.

Write down which chords are played in which order. Later it is used to calibrate the hands' position against the
fretboard.

With two phones you can film from another angle at the same time, for example obliquely from above the neck, as
the student sees it. Each angle is processed separately for now.

**Markers (optional but recommended):** print `markers/aruco_markers.pdf` at 100 %. Tape id 0 on the front of the
headstock and id 1 on the body near the neck joint. They are not needed for the first test, but the same films can
then be used later to work out the exact position of the fretboard without filming again.

Put the films in `videos/` (not in git).

## Step 2: extract the hands

```powershell
.\.venv\Scripts\python.exe capture\extract_hands.py videos\oppna_ackord.mp4
```

The script writes two files to `output/`:

- `*_overlay.mp4`, the video with the tracking drawn on it. Look at it first. Orange is the fretting hand and
  blue is the strumming hand.
- `*_hands.json`, all 21 hand landmarks per frame in pixels, in the hand's own coordinate system and in the
  camera's 3D space (metres).

Useful flags:

| Flag | When |
|---|---|
| `--lefty` | Left-handed guitarist |
| `--mirrored` | The video is mirrored (some selfie modes) |
| `--fov 72` | The camera's field of view along the long side, 67° by default |
| `--rotate 90` | If the picture is rotated wrongly |
| `--no-smooth` | Look at the raw data, without smoothing |

If the hands have swapped colours in the overlay video (orange on the strumming hand), try `--mirrored`.

## Step 3: look in Blender

```powershell
& "C:\Program Files\Blender Foundation\Blender 3.5\blender.exe" --python capture\blender_view.py -- output\oppna_ackord_hands.json
```

Blender opens and shows the scene through a camera that matches the phone, with the overlay video in the
background and the animated 3D hands on top. Press space to play. Rotate the view to see what the depth looks
like from the side.

## Installation

```powershell
py -3.13 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r capture\requirements.txt
```

The hand model (`models/hand_landmarker.task`) is downloaded automatically on the first run.

## Known limitations of step 1

- The depth is rough. It is estimated from a single camera with an estimated focal length.
- The hands are in the camera's space and not yet relative to the fretboard. That is the next step, through the
  markers or a calibration against known chord shapes.
- MediaPipe is fast but coarse on finger detail. If that is not enough we switch to HaMeR or WiLoR, which can run
  on the graphics card.
