# Vegetation and sun shadows

Porting stage of 05.11.2026. This is a new native implementation for the current C++ city,
not a full reproduction of the original PROMZONA renderer.

## Trees

`planTrees()` places birches, lindens and spruces in parks and free courtyards.
A separate random number generator keeps the existing sequence of
cars, people and objects. Clearances from buildings, sidewalks, water,
street objects and the whole reserved area of overpasses/tunnels are checked.
The base of the trunk is anchored to the terrain. The trunk gets a static circular
collider in the existing local contact system; the crown is decorative.

Each species has three silhouettes and two levels of detail. The GPU stores
36 shared meshes: a trunk and a crown for each variant/LOD. Plants differ in
scale, rotation, tint and wind phase. Instancing sends 32 bytes of parameters
per tree instead of a separate copy of all vertices. Lists of visible
instances are rebuilt for the camera and for the light; the capacity of CPU vectors
is reused. The streaming VBO is reused with orphaning.

Foliage uses alpha test: each card holds several twigs with small leaves
or needles. Geometry and wind deformation match in the color and shadow passes.
Near and far LODs are blended with dithering between 85 and 115 m, and trees fade
out at 560–620 m. Both passes use the distance to the camera,
and culling uses the camera's or the light's own volume.

The reference city with seed 20261105 has 2090 trees. After replacing
individual meshes with instancing, the buffer memory of the whole static scene dropped
from 924 to 545 MiB. This compares two implementations of this stage, not the C# version.
The counter includes scene vertices and indices but not textures, actors, the shadow map
or the small dynamic instance buffers; it is not the total video memory.

## Shadows

A single local 2048×2048 depth map covers a 220 m area in the light plane
around the camera target. The projection is snapped to the texel grid; this reduces shimmering
when the camera moves. Vertices of buildings, cars, people and foliage go through the same
transforms in the shadow and main passes. Water and small facade details
do not cast shadows at this stage.

3×3 PCF softens the edges; bias and polygon offset reduce self-shadow acne.
Shadows fade out at the edge of the map. When the sun is below the horizon the shadow pass
is skipped; rain reduces the contrast of direct sunlight.
Shadowing applies to direct light; ambient light remains.

These are not cascaded shadows for the whole city. Shadows of distant objects and shadows from street lamps
are not implemented. Since 10.11.2026 near building details are loaded in the background, and since
13.11.2026 the sectors of the base city are loaded the same way.
See [rendering cost](ARCHITECTURE.md#rendering-cost).

## Sky, haze and reflections

Since 13.11.2026 the sky, haze and reflections have been ported from the C# game. The horizon and
zenith colors, haze density and height, and golden and twilight tints are computed by
`Atmosphere::create` (a port of `CityAtmosphere`) from rain, daylight, sun
height and street wetness. The sky is drawn as a single triangle on the far plane
after the static scene (a port of `Sky/Weather.frag`): sun, moon, four octaves of
clouds, stars. Haze is an exponential layer: a surface blends into the horizon color
the more, the farther and lower it is. This is not volumetric weather.

Reflections use a local cube map (a port of `CityReflections`): 128 pixels
per face, RGBA16F with mip levels, two maps — the finished one and the one being captured. While a map
is being captured, one face is drawn per frame: the static city without facade details,
trees and sky, without cars and people. The map is recaptured when the player has moved
22 m, game time has advanced by a quarter of an hour, rain has changed by 0.12, a city sector
has appeared or disappeared nearby, and every 12 seconds. A surface reads the map
with parallax correction (a sphere of 70 m radius) and between 28 and 100 m from its centre
switches to a computed environment: ground, a jagged skyline, sky and the sun's highlight.
Windows, water, puddles, wet patches of asphalt, glass, chrome and car paint reflect;
roughness selects the mip level. A burnt-out car reflects nothing.

SSAO (a port of `PostProcess/Occlusion.frag` and part of `Compose.frag`) is computed from
the depth of the finished frame at half resolution and blurred 3×3 with respect to
depth; beyond 160 m there is no occlusion. The target takes one byte per pixel.

The scene computes light in linear space and tone-maps it at the end, while the C# game
worked in screen colors. Sky and haze colors are therefore given as on screen and
converted back by the inverse transform for reflections: a mirror shows the sky at the
same brightness as the sky itself.

The scene, effects and sky shaders include the shared file `environment.glsl` with an
`#include` line; it is expanded by the shader loader, as GLSL has no such directive.

## Coast, clouds and wet road

Coastal water is built over the seabed on a 3 m grid. Neighbouring sectors use the same
depths at their boundary. Surf amplitude fades out in the canals and where it meets the open sea.
A wave lifts the surface and the hit rings with the same phase. Near the shore the depth
sets the color of the shallows and the broken foam. Looking out to sea, the distant reflection
blends into the sky; towards the city the skyline silhouette is kept.

The upper cloud layer and the low wisps move at different speeds. The lighting of an
edge depends on the density on the sun's side. On wet asphalt the distribution of the water film
changes roughness and reflection. A skid mark takes into account the water under the tyre when
it appears. Up close, the bus and the gas service van show their interior and wheel
arches; far away the previous models are used.

To check camera motion, build a package and run from the repository root:

```powershell
.\build.ps1 -Configuration Release -Package
.\tools\smoke.ps1 -VisualRoute clear -Scenarios seafront,facade
.\tools\smoke.ps1 -VisualRoute rain -Scenarios seafront,portal
```

Each route runs 60 warm-up frames and 600 frames of motion with a fixed
step. `build/smoke/Release/routes/<weather>/<scene>` receives pairs of adjacent
frames every 15 frames and the last frame. Compare the same frame numbers. To
measure frame time, use a separate run with `--gpu-profile`: reading the frame back
from the GPU and writing images change the frame duration on a route.

To compare headlights and rain at one crossing and to check small details,
use the same route frames from two builds.

```powershell
.\tools\smoke.ps1 -VisualRoute rain -Scenarios night-crossing
.\tools\smoke.ps1 -VisualRoute clear -Scenarios wire-street,shadow-avenue,entrance,entrance-night
```

At the crossing a bus and four cars pass in front of a static camera.
On the `wire-street` route the camera moves slowly along the overhead wires.
GPU time for shadows, reflections, the main scene, particles and post-processing
is recorded separately when running with `--gpu-profile`.

### Headlight beams, shelters and cloud shadows

```powershell
.\tools\smoke.ps1 -VisualRoute rain -Scenarios beam-wall,dry-canopy
.\tools\smoke.ps1 -VisualRoute clear -Scenarios bus-puddle,cloud-coast
```

On the `beam-wall` route a bus crosses the beam between a car and a facade.
The pair of active headlights nearest to the camera takes into account the oriented volumes of cars and
walls. On `dry-canopy` the rain stops in the middle of the recording while the open paving
stays wet. Bus stop roofs and canopies use the same dimensions for blocking
raindrops and for the wetness mask.

On `bus-puddle` a bus passes behind a puddle. At high quality with post-processing
the reflection ray searches for an intersection with the depth of the current frame. At the screen edge and
beyond the working distance the environment reflection remains. To compare
clouds over 20 seconds, run `cloud-coast` with `--frames 1261`. The camera is static.

To measure shadows, run `--smoke --scenario shadow-avenue --gpu-profile
--quality 2 --scene-mode quality --smoke-size 1920 1080 --frames 421`.
The camera travels 96 metres while turning. For each measured frame the log
gives its number, whether the far cascade was drawn and the GPU time in milliseconds.
With smooth motion the two halves of the map are built in adjacent frames. The finished
map and its matrix are switched together. On a teleport the map is built at once.

## Particles and collision marks

Particles are a port of `CityParticle` and `CityTraffic.Emit`: `World::emit(point, floor,
kind, count, speed)` throws a spray scattered around the vertical. There are seven kinds:
spark, smoke, debris, water, fire, sand and flamethrower tongue. Each has its own
lifetime, size and color (`CityEffects.Draw`); smoke rises, expands and
drifts sideways, the others fall with an acceleration of 9.81 and bounce off the floor
remembered at the throw (street, overpass deck or tunnel floor). The eighth kind,
`plain`, is the port's earlier particle: a colored point that only falls; bullet
tracers are drawn this way. There are at most 5000 particles in total.

Effects take random numbers from a separate sequence
(`World::effectNoise`, xorshift with the same seed 4191 as `Noise` in
C#): what is seen does not change what happens. Saves do not store particles or
marks.

A collision mark (`Scuff`) is dark streaks on both sides of a car for 100
seconds, or a stain on the wall of the nearest building for 90; `EffectBatch::scuffs` draws
them every frame at the car's current position. Smoke, debris, sand, water and marks
darken at night together with the scene; fire, sparks and flames do not. Effects are blended
premultiplied: the shader outputs a color already multiplied by opacity and clamped
to one. This way colors brighter than white (fire is 3.0 in red) do not dim while the
particle fades, even though the frame is 8-bit.

## Checks

`native_environment` checks three seeds: placement and reproducibility,
anchoring to the ground, uniqueness of collider IDs, the geometry of both LODs and the triangle
budget. Light projection checks cover noon, a low sun,
night, the sun directly overhead, culling and texel snapping.
The remaining groups check AI routes, road level crossings and saves.

Particles are checked there too: the parameters of each kind against C#, independence from
the city's sequence, the motion of smoke and sparks, the events of a collision, a burning
and a damaged car, wreckage and a hit on the player. The GPU scenario `wreckage`
blows up a car next to a burning and smoking one and counts particles and sounds.

The GPU set includes `park`, `park-unshadowed` and `park-evening`.
The first two give the same view and state with and without shadows; the third shows
evening lighting. The scenarios use the real OpenGL renderer, not an external
visualiser. Reference frames of this stage are in `docs/screens`; each run of the
GPU check writes fresh frames to `build/smoke`.

The `promzona-cpp-5` format is kept. On loading, trees and their colliders
are restored from the seed; there is no separate record of thousands of decorative objects.
