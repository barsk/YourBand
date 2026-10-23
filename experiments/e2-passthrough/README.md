# E2: Virtual Desktop passthrough by chroma key

Does mixed reality work the way the plan wants (see [../../docs/plan.md](../../docs/plan.md), "VR mode")? The game
renders the band on a background of one exact colour; Virtual Desktop replaces that colour with the headset's
camera view of your room.

## The app

A throwaway Unity 6.6 project made from Unity's VR template (OpenXR, outside the repository: see `run_e2.py`),
set up by `Editor/E2Setup.cs`:

- The band's guitarist (Rocketbox, playing the Mixamo pick-guitar clip) standing 2 m in front of you, with a
  thin ring marking his footprint on your floor.
- The camera clears to the key colour (magenta, #FF00FF, by default). No post-processing (tone mapping would
  change the key) and no anti-aliasing (an edge pixel is either the musician or the key, never a blend), and the
  hair uses cutout transparency.
- A minimal rig: head and right controller straight from the Input System, no locomotion, so the stick is free.
- XR is started by the app, not at launch: `-noxr` runs it on the desktop without starting any VR runtime.

Controls, right controller:

| | |
|---|---|
| Trigger (hold) | aim the beam at the musician's feet and move him along the floor |
| Stick left/right | turn him |
| A | with the controller resting on the floor: calibrates the floor height |
| B | next key colour: magenta, green, blue, black (also K on the keyboard) |

The key colour, the floor offset and where the musician stands are remembered in
`%USERPROFILE%\AppData\LocalLow\DefaultCompany\E2 Passthrough\e2-settings.json`.

Found later (2026-10-23, in the game): this app has OpenXR's usual loader, which opens the system's OpenXR
runtime (here SteamVR) at every launch to choose the graphics adapter, `-noxr` included. The game's VR mode
uses the loader without that, and Virtual Desktop's runtime; it does everything this app does, with the whole
band, so the headset test can be done in the game instead (`game/README.md`, "VR mode").

## Desktop check (done)

`python run_e2.py check` renders a frame without XR and counts pixels (2026-10-21):

- every background pixel is exactly #FF00FF (1,937,574 of them);
- of the 2,440 pixels on the musician's edges, none is tinted towards the key colour: no pink fringe to key away.

`results/e2_desktop.png` is that frame.

## The headset test (for you)

The app is `C:\Users\krist\Documents\Unity\GuitarTutor-E2\E2-VD\Builds\E2\E2 Passthrough.exe`.

1. In the Virtual Desktop Streamer on the PC, set the OpenXR runtime to VDXR.
2. In Virtual Desktop in the headset, turn on passthrough by chroma key with the key colour #FF00FF
   (RGB 255, 0, 255). Raise similarity and smoothness until the thin magenta outline round the musician is gone (a low setting leaves the stream's blurred edge visible; found later, see `game/README.md`).
3. Connect, then start the app from Virtual Desktop's games list, or from the desktop while streaming. The PC's
   system OpenXR runtime is SteamVR: started outside Virtual Desktop, the app would start SteamVR instead.
4. Try:
   - does your room show through everywhere except the musician, with clean edges, including the hair;
   - move and turn him with the trigger and stick; calibrate the floor with A and check his feet stand on it;
   - how far the similarity can go before parts of him disappear, and whether the dark jacket and hair hold;
   - latency of the passthrough against the musician when you move your head;
   - B to try the other key colours.

What would change the plan: if the key cannot be made clean (fringes, flicker in dark areas), the next options
are Virtual Desktop's alpha passthrough through VDXR, or a different key colour.
