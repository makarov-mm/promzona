# Buildings in the native renderer

This stage adapts part of CityArchitecture from the original PROMZONA. The city
layout and the physical dimensions of buildings do not change. This is not a full
port of the old renderer.

## Overall plan

`planBuilding()` creates a reproducible plan from a building's position, size and style.
Windows sit on the long walls, while the end walls stay blank. Balconies are arranged
in vertical rows. The base mesh and the near mesh share one plan, so windows
do not move as the camera approaches. Facade textures repeat every
3.2 m instead of stretching over the full height of the building.

The main mesh contains the walls, roof, plinth and opaque window rectangles.
The near mesh contains the entrance, canopy, parapet, drainpipe, window sills, air conditioners,
three balcony variants and some of the existing signs. Signs keep a 2:1
aspect ratio and are placed so they never overlap a door or windows. Exterior details are decorative:
the physical volume of the building stays the same.

## Materials and windows

The eight existing facades use color, normal and ORM (AO, roughness,
metallic) maps. Lighting uses GGX, approximate sky illumination and atmospheric
haze. Local sun shadows have been added — see [ENVIRONMENT.md](ENVIRONMENT.md).
Since 13.11.2026 windows reflect the sky and neighbouring buildings through a local
environment map — see [ENVIRONMENT.md](ENVIRONMENT.md#sky-haze-and-reflections).

In the window shader a ray intersects a virtual room box. Four variants of the
back wall show a rug with a TV on a cabinet, a wardrobe, a fridge or an
empty room. This is an illusion of depth, not an interior the player can enter.
Ten dressing variants include windows without curtains, with open, half-drawn
and drawn curtains, net curtains and blinds. The existing rug texture is reused.

Each window has a persistent identifier: it is passed through an unused joint
channel of the static mesh and a **flat** output of the vertex shader. It must not
be interpolated: a fractional seed produces a random pattern between pixels.
Whether a window is lit depends on the time of day and an individual period of 200–850 s
of simulated time; the transition is smooth. This is not a simulation of the residents.

## Rendering cost

The base scene is split into 144 m sectors, near details into 72 m cells.
A worker thread builds details from immutable building plans without access to OpenGL
or actors. The queue holds up to 8 jobs and is reordered by distance to the camera.
Up to two finished cells can wait. Railings use crossed planes instead of boxes.
The actual mesh bounds define the culling spheres; all six camera planes
are taken into account, including objects behind the camera.

Details fade out with screen-space dithering at 135–185 m.
Windows stay in the base mesh up to the original range of 1000 m. Near cells
are requested within 260 m of their boundary and released beyond 370 m.
The gap between the thresholds lets the player turn around without an immediate rebuild.
Details take up to 64 MiB on the GPU: the full size of a cell is accounted for before a new upload.
The render thread uploads materials in portions, aiming at 2 MiB and 1.5 ms per frame;
a single driver call can take longer. A cell is drawn once its upload has
finished. After an abrupt change of district the base buildings with windows are visible at once; decorations
appear as they become ready.

Since 13.11.2026 the base geometry is streamed the same way (a port of the
`CityDistricts` behaviour). A sector is needed while its square is closer than the draw distance plus
30 m, and is released 40 m further out. When a game is loaded, the sectors around the player
are built at once, so the first frame is complete. After that a second worker thread builds
sectors that come into range, nearest first; it has its own copy of the world, so a change of
city does not disturb it. The queue holds up to 16 jobs, and up to two finished sectors can wait;
a sector is uploaded to the GPU in the same portions (2 MiB or 1.5 ms per frame) and drawn
as a whole once uploaded. A sector closer than 60 m to the camera is built immediately: after
a camera jump the ground beneath it exists in the same frame. The open sea beyond the edge of the island
is always drawn and does not depend on loaded sectors. The simplified distant levels
of the C# game's districts (LOD 2 and 3) have not been ported: a loaded sector is complete.

A vertex of static geometry takes 48 bytes instead of 64: it needs no bone
weights, and indices are 16-bit where that is enough. The whole base city takes 254 MiB
instead of 348. The static city is drawn by material rather than by sector: meshes with
the same textures and constants are drawn one after another, and repeated values are not
sent to the driver again.
The GPU check log records the number of submitted meshes and triangles, the size of the
scene buffers, the number of loaded sectors and of reflection map faces. The size excludes textures and actors and is not the total video memory.

## Checks

`native_architecture` checks window orientation and size, reproducibility,
UV scale, the geometry of every building of the reference city, allowed protrusions,
sign placement, the triangle budget and the camera frustum bounds.
The GPU scenarios `facade` and `facade-night` show the same building in daylight
and at night. They verify that GLSL compiles on the driver, but judging the
look requires viewing the resulting images. The `streaming` scenario moves the
camera to four remote districts and back to the first one. It checks the queue and memory
limits, the release and reloading of cells and sectors on a
real driver, that there is always ground under the camera, and that by the end of
each stop the city within range is loaded.
