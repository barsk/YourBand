# E4: URP or HDRP

Which Unity renderer the game uses (see [../../docs/plan.md](../../docs/plan.md)). The musicians render in both
desktop and VR mode, so the project needs one renderer for both. VR (mixed reality over Virtual Desktop) only
shows the musicians, their instruments and the panels, which makes HDRP possible as well as URP.

## The test

The same scene in two throwaway Unity 6.6 projects (outside the repository, see `run_e4.py`):

- The band's guitarist (Rocketbox Male_Adult_04) playing the Mixamo pick-guitar clip, retargeted by Unity's
  humanoid system, in front of a backdrop on a floor.
- Materials from the Rocketbox textures plus the maps made for the Blender mockup: the roughness map becomes
  smoothness in both renderers; in HDRP the skin also scatters light under the surface (HDRP's skin diffusion
  profile, masked by the mockup's skin map so clothes and hair stay out). URP has no subsurface scattering.
- The same lights: a warm key and a cool fill, ACES tone mapping. HDRP uses physical units with a fixed exposure;
  the values are converted so both scenes are about equally bright (see `Editor/E4Pipeline.HDRP.cs`).
- Two shots, a close-up of head and hands and the full player, rendered at 4128 x 2208 (about the pixel count
  of a Quest 3 in stereo) at every quality level of the project, 600 frames each after a warm-up.

## Running

```
python run_e4.py              (setup and benchmark for both)
python run_e4.py bench HDRP   (only one step or renderer)
```

| File | Contents |
|---|---|
| `Editor/E4Setup.cs` | Shared editor setup: imports, scene, player build (batch mode: `-executeMethod E4Setup.Run`) |
| `Editor/E4Pipeline.URP.cs`, `Editor/E4Pipeline.HDRP.cs` | Materials, lights, post-processing per renderer |
| `Runtime/E4Bench.cs` | The benchmark in the player: frame times, screenshots, `results.json` |
| `results/<renderer>/` | Screenshots and results |

## Results (2026-10-21, RTX 5090, Direct3D 12, Unity 6.6)

One guitarist at 4128 x 2208, vsync off, 600 frames per shot:

| Renderer, quality | Frame time | 95th percentile | CPU main thread |
|---|---|---|---|
| URP (PC) | 1.5-1.6 ms | 1.7 ms | 0.4 ms |
| HDRP Performant | 2.1 ms | 2.4 ms | 0.7 ms |
| HDRP Balanced | 2.2-2.3 ms | 2.6 ms | 0.7 ms |
| HDRP High Fidelity | 2.6-2.7 ms | 3.0 ms | 0.8 ms |

- Both are far inside a VR frame (11.1 ms at 90 Hz, 8.3 ms at 120 Hz). HDRP costs about 1.7 times URP.
- Unity's frame timing reports no GPU times on Direct3D 12 here (all zero), so the frame time is the measure.
  The CPU main thread takes well under the frame time and the render thread almost nothing, so the GPU sets
  the pace.
- Looks (`results/`): HDRP's subsurface skin is clearly softer and more natural; URP's face looks glossy. Hair
  and the leather jacket look about the same. HDRP's scene came out about a stop darker and cooler: the light
  conversion is not exact yet.
- Unity's render pipeline strategy for 2026 keeps HDRP in maintenance mode (stability, new platforms) and puts
  its development into URP, including new real-time 3D lighting.
- The humanoid retargeting of the Mixamo clip onto Rocketbox works out of the box (both avatars valid; the
  right hand moves 4-12 cm during a measurement). The pose is the clip's air-guitar stance without a guitar.

Recommendation: URP, with skin shading improved in URP (a skin shader with softened diffuse and lower specular),
because the renderer is for the long term and HDRP gets no new development. Decided by you, see the plan.
