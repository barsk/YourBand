# Venues (desktop mode)

Venues exist only in desktop mode; VR shows your real room. A venue is a Unity scene plus a definition:
station spots, your spot, camera shots, lighting, acoustics and ambience. See [plan.md](plan.md).

## Status

| Venue | Source | State |
|---|---|---|
| Home studio | Sketchfab "Home Studio" by R-LAB, CC BY 4.0 | Chosen as the first venue; needs props (below) |
| Office studio | Sketchfab "Office Studio" by R-LAB, CC BY 4.0 | Candidate for a larger "loft" venue later |
| Living room | Our Blender mockup (`mockup/build_vr_band_scene.py`) | Can be ported |
| Garage | Not found yet | |
| Studio | Not found yet | |
| Arena | Not found yet | |

Downloads live in `model_images/venues/<venue>/` (not in git).

### Home studio (checked 2026-10-21)

- An empty gallery-style room, 8.8 × 8.9 m, 4 m high: wood floor, white walls, a skylight with pendant bulbs,
  one door, 13 paintings, potted plants. Internally it is named "Art Gallery"; there is no studio equipment.
- Light: 90k triangles, 15 MB, 2K textures, 6 materials, no lights in the file.
- The Sketchfab export merged objects by material: plants, paintings, floor and frames are fused into a few
  large meshes. They must be separated in Blender (by loose parts) before anything can be moved or removed.
- To become a home studio it needs:
  - acoustic panels or foam on the walls, a rug or two, a sofa or armchair;
  - amps, a PA speaker pair, mic stands, guitar stands, cables;
  - music posters instead of the gallery paintings (we can make these);
  - real lights: warm practical lamps plus the skylight.
- CC BY allows changes: credit R-LAB and say that the model was modified.

### Office studio (checked 2026-10-21)

- An industrial loft, 20 × 20 m, 4.4 m high: speckled floor, windows on all sides, exposed ducts and pipes,
  wooden acoustic panels on the ceiling. Empty inside.
- Heavy: 226k triangles, 106 MB, 4K textures, 93 meshes but only 3 materials.
- Part of the roof is open; the windows need an outside view (an HDRI).

## Asset requirements

For anything new you find:

| | Requirement |
|---|---|
| Licence | Must allow use in a game that may be published and sold. CC0 is best; CC BY is fine (credit the author). No NC, no ND, no "editorial use", no models ripped from commercial games. |
| Style | Realistic PBR materials (colour, normal, roughness maps), matching the Rocketbox musicians. No low-poly or cartoon. |
| Formats | `.blend`, FBX, glTF/GLB, OBJ, or a Unity package |
| Structure | Separate objects, not one merged mesh. Real-world scale preferred (scale can be fixed). |
| Textures | 2K–4K. Lighting not painted into the textures, if there's a choice. |
| Budget | Up to a few million triangles per venue (desktop only) |
| Avoid | Scenes with people already in them; visible brand logos (generic look-alikes are fine) |

Where to look: Sketchfab (filter "Downloadable" and the CC0 or CC BY licence), Poly Haven (CC0 models,
textures, HDRIs), ambientCG (CC0 textures), BlenderKit (free tier), the Unity Asset Store free section.

Delivery: one folder per download under `model_images/venues/<venue>/`, with a text file holding the source
URL, the author and the licence.

### Per venue

- **Living room:** upgrades to the mockup room: sofa, armchair, coffee table, rug, bookshelf, lamps, plants,
  curtains, a TV or sideboard; an HDRI for the window view.
- **Garage rehearsal space,** about 6 × 6 m:
  - room: a garage interior, or a shell with concrete floor, block or brick walls, a garage door, exposed
    ceiling, strip lights or bare bulbs;
  - props: workbench, pegboard with tools, metal shelves with boxes, worn sofa or armchair, rug pieces,
    foam panels or moving blankets on the walls, cables, amp stacks, a small PA pair, mic stand,
    guitar stands.
- **Recording studio:**
  - live room, about 6 × 8 m: wood floor, acoustic panels and diffusers, a window into the control room,
    an isolation booth door;
  - props: mic stands with dynamic and condenser mics, headphones, cables, music stands, a movable acoustic
    screen, amp and cabinet, rugs;
  - control room (seen through the glass; lower detail is fine): mixing desk, monitors, chair.
- **Arena:**
  - stage about 12 × 8 m with a drum riser, a keys riser and a stage edge;
  - lighting rig: truss, moving-head lights, PAR cans (fixtures only; beams come from the renderer);
  - speaker arrays, monitor wedges, an LED wall or backdrop;
  - hall: a seating bowl or standing floor, simple, mostly in the dark;
  - crowd: optional (can be built from Rocketbox avatars).

Acoustics (impulse responses of a small room, a studio live room and a large hall) come from the OpenAIR
library; no need to search for those.
