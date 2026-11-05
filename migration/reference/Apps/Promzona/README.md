# PROMZONA 0.33.0

## Source audit — 03.11.2026 (0.35.0 patch)

Static audit of the whole game source; report in `Docs/PROMZONA-AUDIT-2026-11-03.md`.
Build: the explicit `Game\FoodSite.cs` compile item duplicated the SDK's default
glob (NETSDK1022) and is removed. Fixed: the fault screen's Esc no longer saves
through a broken session; saves with walker grid keys outside the tables are
refused at load instead of throwing in the first step; the scooter/car id check
compared against nothing; a dumpster restored from a save had a different body
than the one streaming creates; `CityPoint.Length` was written into every saved
point; imported-model manifests are validated at load instead of failing in
`Draw`; a loader failure leaked the vehicle bodies already on the GPU; audio
device errors are shown once in the menu. Logic: the handover car of a running
contract is no longer swept as abandoned; a respawn after a passenger ride no
longer strands the tram; a chase that loses the player across road levels
rejoins the street graph; `RejoinRoad` keeps lane 0 consistent; knocked-down
survivors crawl out at a fixed speed; fire blame is no longer sticky; a person
mid-crossing is not turned into a kerbside hailer. Rendering: the wet sand band
follows the layout's coast instead of the 33-node island; a tram without its
imported model no longer wears the guard-van body; the terrain lamp map clamps
negative heights; `CityGpuPreparation` invalidates the shader binding cache.
Performance: the actor grid is refreshed incrementally per substep and rebuilt
once per frame; `Enter`, service lookups, dispatch, reversing, the drone,
lamp heads, imported-model lookups and the minimap avoid full scans. Tests:
`--audit`.

## Occupied transit and district art direction — 0.33.0

Moving trams and trolleybuses now show their driver and actual saved passengers.
Passengers use a seated skeletal pose, move with the vehicle's terrain alignment,
and remain the same citizens when boarding, saving and alighting. Cabins are drawn
only nearby and inside the view; there are no additional passenger physics bodies.
The player is also visible when riding with B. On a full vehicle the player stands
in the aisle. B/F exits when the vehicle is slow enough. Transparent trolleybus
windows are drawn after occupants, with repaired upper window frames and a soft
night-time cabin fill. The trolleybus medium LOD is rebuilt from welded near
geometry, retaining small panels and window openings instead of deleting faces.
The parked industrial metro wagons remain scenery.

A new city starts with existing off-screen citizens already riding its services.
Loading preserves saved passengers without reseeding or duplicating them. Older
saves with empty vehicles acquire riders at the existing stops. No new game is
required for the rendering changes.

The visual direction is a restrained, worn provincial city: coherent materials per
district, subdued plaster and concrete, finer brickwork, aligned balconies, roof
copings, drainpipes and enamel house numbers. Commercial signs and posters are
less frequent, with fewer glowing colours. Road paint has small worn areas rather
than a camouflage pattern; asphalt and shaded facades retain readable midtones.
Park trees have smaller leaf clusters and looser planting. Existing street layout,
collision geometry, population targets and draw distance are unchanged.

Authoring utilities `Content/Tools/bake-transit-seated.py` and
`Content/Tools/repair-transit-frames.py` regenerate the new prepared assets.
They require NumPy and the repository's existing source assets.
`Content/Tools/rebuild-transit-middle.py` uses Blender to regenerate the trolleybus
medium LOD from its prepared near LOD; normal game builds
use the prepared files and require neither Python nor Blender. The first 320
human poses are retained; the seated clip occupies poses 320–383.

Art direction rule for future additions: choose a district's material family first,
then vary age, repairs and a few accents. Avoid independently randomizing every
facade, floor, window and sign. Keep bright colour for gameplay cues and selected
landmarks; do not turn every building into an advertisement.

## Actor GPU preparation — 0.29.0

Loading now submits every imported animal LOD/material, human skin and rigid
fallback to small private depth, HDR and LDR targets before entering play. One
model/LOD is prepared per loading frame. This addresses measured first-use stalls
when previously unseen animals enter the colour or shadow pass. GPU completion
is awaited only during loading; no synchronous finish was added to gameplay.

Preparation restores the loading UI's framebuffer, viewport, program, vertex
and buffer bindings, texture units, masks, clear colour and enable state. Its
small temporary targets are released on completion or cancellation. It does not
advance actors, change saves, reduce population or replace any geometry/textures.
New game and saved-game loading both pass through preparation. Saves are compatible.

Normal Debug and protected Release builds are supported. An opt-in native regression
is in `Apps/Promzona.Tests/Rendering/GpuPreparationChecks.csproj`; run it with
`-p:GameDir=<unprotected publish directory>` and pass that directory as its argument.
This is a targeted hitch fix, not a claim that all long frames have been eliminated.


## District streaming — 0.28.0

Static district meshes now share one reusable CPU packing workspace. Vertex
indexing resumes across frames in small batches instead of completing an entire
detailed district inside one frame. A previous LOD stays visible until the new
mesh is uploaded; teleporting or changing quality cancels obsolete queued work.
Only used buffer ranges are uploaded, and resident meshes retain no CPU staging
arrays. Triangle order, normals, material IDs, UVs and terrain geometry are kept.

The six-millisecond streaming budget is soft: geometry construction and graphics
driver calls cannot be interrupted. This change does not guarantee a maximum
frame time. Draw distance, populations, local physics and saves are unchanged;
no new city is required. The optional native regression can be run against an
unprotected build with `Apps/Promzona.Tests/Rendering/StreamingChecks.csproj`,
passing `-p:GameDir=<absolute build directory>` and the same asset directory
after `--`. It checks GPU buffer contents, queue completion, quality changes,
teleport eviction and resource disposal. Normal game builds need no Python.

## Gameplay, progression and neighbourhoods — 0.27.0

Human health and vehicle condition have separate HUD meters. Pills and food heal
the person; paid garage repairs repair the car. Entering and leaving a vehicle
preserves the person's health. Distant walkers update expensive routine decisions
at staggered intervals while movement remains continuous and local collisions stay
active. Human locomotion blends between clips, and threatened armed NPCs can move
between cover and a firing position behind nearby parked cars or building corners.

Three independent save slots are selectable in the menu. The first keeps the
existing `save.json`; slots two and three use `save-2.json` and `save-3.json`.
Autosave stays in the active world's slot. Saving a copy to another selected slot
does not switch the active world. The menu shows both active and selected slots.

G at a garage buys ownership for $400; G while stopped there retains the current
car and exits it. Up to three cars can be retained, including their identity,
paint and damage. This is persistent physical parking, not a closed garage interior
or an inventory of cloned cars. J, then 3 starts a three-contract chain: vehicle
handover, marked target, stricter vehicle handover. Rewards, time limits and the
required final vehicle condition increase; completing the chain adds a bonus.

B at a stopped tram/trolleybus boards as a passenger with the driver still active;
F still takes the driver's position. B/F exits when slow enough. Existing NPCs can
wait at stops, walk to the door, ride and get off at a later stop. Their identities
survive saving. Passenger interiors and a paid timetable are not implemented.

New games use road-layout revision 4: low houses, garage rows, courtyard blocks,
a denser centre, housing estates and the existing industrial district. Rendering,
building collisions and minimap use the same building footprints. Older saves
retain their original layout; start a new city in another slot for the new districts.

Independent Windows city game. The project, executable and C# namespaces are now
`Promzona`; game models use `CityModel`, `CityGame` and `CityScene`. The game shares
engine libraries with the Suites but does not load their module catalogue.

## Citywide transit and combat — 0.26.0

Two tram and two trolleybus circuits cross multiple districts using existing
streets and canal bridges. Route length determines the number of vehicles and
stops. Rails, wires, steering, stops and the minimap share the same curved route.
The network avoids flooded streets and the separate overpass/tunnel templates.
It is four fixed circuits, not a switchable rail network or a passenger timetable.
Existing saves migrate their transit vehicles to the new routes and fill missing
fleets; a new game is not required.

Two rare bazooka pickups are maintained on the map, out of view when replenished.
Each gives three rockets. NPCs never carry or drop this weapon. Swept projectiles
hit geometry and actors; blast damage falls off with distance and is blocked by
buildings and road decks. Nearby players can be hurt by their own explosions.
When ammunition runs out, selection falls back from bazooka to automatic to
pistol. On foot, LMB with no loaded weapon performs an animated short-range punch
with one contact per swing. Punching is unavailable inside a vehicle.

The 0.25 motion/LOD fixes are retained: interpolated animal animation, intact
distant vehicle wheels, stable vehicle colours and batched distant flocks.
Authoring tools and source assets remain outside the runtime distribution.

## Camera, night visibility and build verification — 0.23.0

Camera collision queries use a city spatial index, include bridge supports and
tunnel portals, and validate the current pose after respawn. Thin slabs remain
solid for longer camera booms. Night skylight keeps unlit surfaces more legible.

See [controls](CONTROLS.md) for driving, walking, transit, contracts and saving.
Protected distributions now carry `protection.json`; the build independently
verifies assembly coverage and hashes and rejects leaked private symbol maps.
Optional protection also works for staged Suites with `-Mode Steam -Obfuscate`
or `-Mode Store -Obfuscate`. See [build protection](../../Docs/BUILD-PROTECTION.md).

## Distribution and code protection — 0.22.0

Release packaging uses a fresh staging directory and an explicit resource list
in `RuntimeContent.props`. The runtime contains the EXE, required assemblies and
.NET runtime, game assets, EN/RU interface resources, and asset licence notices.
Source models, Python bakers, tests, development scripts, unused animation frames
and Release debug symbols remain outside the runtime ZIP. Authoring files remain
in the source tree. When adding a runtime asset, update the explicit resource list.

`GameRelease -Obfuscate` protects the game assembly and its nine private copies of
the shared engine assemblies. It leaves Suite builds and shared source code alone.
The build downloads the official Obfuscar 2.2.50 package, checks its pinned SHA-256,
and runs it locally. Private implementation names and literal strings are obscured;
public API, reflection fields and JSON properties remain compatible. This makes
inspection harder, but does not prevent decompilation or protect editable assets.
No decompilation-proof build is promised. NativeAOT is not enabled: the current
WinForms application is not compatible with the required supported trimming path.

Keep `artifacts/Promzona/.protection/run-*/protected/Mapping.xml` privately with the
matching release for troubleshooting. It is never included in the runtime ZIP.
The obfuscation download needs Internet access; subsequent builds reuse the pinned
tool. To supply an offline installation, call `Tools/scripts/build-game.ps1` with
`-Configuration Release -Obfuscate -ObfuscarTool C:\path\to\GlobalTools.dll`.

The new default save is `%LOCALAPPDATA%/Promzona/save.json`, and settings use
`HKCU/Software/Promzona`. Earlier save formats 1–3 can be explicitly imported:

```powershell
.\Promzona.exe --import-save "C:\backup\save.json"
```

Import validates the file, leaves the original untouched, and refuses to replace
an existing destination save. Settings start with the new defaults. Use
`--save-directory "C:\temporary-game-save"` for an isolated test or import.
Normal play still autosaves to the selected slot. The gameplay and transit
features below are retained; older sections describe the changes in those releases.

## Transit, contracts and recovery — 0.21.0

FPS and average frame time now appear at the upper right. They use real frame
intervals rather than simulation time. The north-up minimap uses the same
handedness as the game camera; roads, actors, markers and arrows share that mapping.

The city finds two closed ground-level street routes: a tram line and a
trolleybus line. Rails, rounded corners, stops and overhead contact wires use the
same path as the vehicles. Two vehicles serve each available line, stop at four
stops, obey traffic signals and resume afterwards. The downloaded X-series tram
and ZiU-9 models are now moving vehicles as well as retained depot exhibits.
The map shows the tram route in orange and the trolleybus route in cyan.

- **F**: enter or leave a stopped vehicle, including the tram and trolleybus.
- **W / S in the tram**: accelerate forward, brake and reverse along the rails.
  Steering cannot move it off the track. **Space** brakes.
- The trolleybus uses normal steering, but loses traction outside its overhead
  wire corridor; braking and coasting remain available.
- **R**, while nearly stopped: recover a trapped vehicle onto a clear nearby
  road. A tram or trolleybus returns to its route. This does not repair damage,
  clear wanted status or restore money; there is an eight-second cooldown.
- **F** also searches for a clear nearby exit if both doors are blocked or the
  stationary car is overturned. The search retains the current road level.
- **J**: open contracts. **1**: collect the marked specific vehicle and deliver
  it to the marked garage with at least 50% health, for $250. Stop inside the
  destination marker for a second. A different vehicle does not count.
- **2**: eliminate the marked specific NPC, for $350. Only a death caused by the
  player completes the contract. **C** cancels; **J / Esc** closes the panel.
  There is one active job at a time and a ten-minute contract deadline.

Vehicle identities, possession, route position and active contracts survive
saving and loading. Existing saves can gain the routes without a new game when
their street layout has suitable continuous dry corridors. Back up
`%LOCALAPPDATA%/Promzona/save.json` and its `.bak` before trying a new build.

This is the first working transit implementation: two loops, no track switches,
passenger timetable, depots with dispatch logic or electrical grid simulation.
The trolley poles and pantograph are approximate visual mechanisms. Distant
traffic follows the routes by rules; detailed contact physics remains local.
Recovering a car is an explicit game assistance feature, not a tow-truck model.

Run focused regressions with:

```powershell
dotnet run --project Apps/Promzona.Tests -c Release -- --transit
dotnet run --project Apps/Promzona.Tests -c Release -- --player
```

## Imported fleet and city assets — 0.20.0

Sixteen downloaded models are now used in the game: nine civilian vehicles and
seven environment assets. Cars have their own stable identity, dimensions and
mass, rotating/steering wheels and opening front doors. Entry, theft, damage and
save/load use the existing gameplay. The original starter VAZ and emergency
vehicles remain. New kiosks, soda machines and shelters replace the corresponding
service visuals. Tyre decorations and old fuel pumps occupy reserved dry sites;
tram and trolleybus models are parked exhibits in small depot areas.

Three geometry LODs, bounded texture sizes, shared GPU assets and local collision
proxies keep the source scans out of the live high-detail path. The 4.4-million-
triangle VAZ is reduced before shipment. These are prepared game adaptations,
not untouched source models. Tram/trolleybus routes and refuelling gameplay are
not included. Imported glazing is tinted reflective shading, and repainting uses
a material heuristic rather than individual painted-panel masks for every model.

No new city is required; existing saves retain actor identity and layout.
Editable models, maps and regeneration instructions are in
`Content/IMPORTED-ASSET-PIPELINE.txt`; all sixteen author/source/licence notices
are in `Content/IMPORTED-ASSET-CREDITS.txt` and the individual model folders.
Only the game and game tests changed. The Suites and Shared renderer are untouched.

## Contact shading correction — 0.19.1

Fixed faint horizontal bands on roads, ground and other depth-bearing surfaces.
Half-resolution SSAO now reconstructs positions at the actual full-resolution
depth texel centres, including integer-addressed neighbours. A nearby sample
must lie above the receiver's tangent plane to occlude it; a quantised sample
of the same flat surface no longer casts a false shadow. Genuine contact
shading remains enabled. This is independent of the surface textures and does
not change city generation, collision geometry or save files.

The optional native OpenGL regression draws untextured planes at four viewport
sizes (including 3440x1440 and odd dimensions), three pitches and both SSAO
qualities, then checks that a wall still casts contact shading. Run against
a published game, separately from the platform-independent gameplay tests:

```powershell
dotnet run --project Apps/Promzona.Tests/Rendering/PostProcessRenderChecks.csproj -c Release -p:GameDir=C:\path\to\published-game -- C:\path\to\evidence.json
```

## VAZ 2101 — 0.19.0

The player starts in the downloaded VAZ 2101 by **Javkal**, also used by part of
the civilian sedan traffic. Original worn body and glass textures, roughness and
metalness are retained; an editable paint mask connects it to the existing stable
vehicle colours and garage resprays. Four wheels rotate with signed travel, the
front pair steers, and both front doors open with their windows. Geometry fits
the existing physical sedan bounds and wheelbase. The other vehicle types and
all gameplay remain in place. Existing saves work without generating a new city.

The new model has about 6,000 prepared triangles. It uses the existing distance
limit and simpler distant silhouettes. Glass uses alpha blending and the existing
approximate environment reflections. Fine relief is derived from the worn colour
map; the supplied asset has no authored normal map. The original wheel-tag V-flip
error was also corrected for the ten earlier vehicle sets, in colour and shadows.

Source: [VAZ 2101 by Javkal](https://sketchfab.com/3d-models/vaz-2101-95d34f63b5b14e2197fd41383776e031),
[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Author, licence and changes
are recorded in `Content/Models/Source-Javkal/LICENSE-ATTRIBUTION.txt`. Original GLB
is preserved there; editable prepared meshes/textures are in `Content/Models/VAZ2101`.
Rebuild assets with `Content/Tools/bake-vaz2101.py` (Python + NumPy). Validate with
`python Apps/Promzona.Tests/VazAssetTests.py`.

## Build and run

```powershell
.\build.ps1 -Mode GameDebug
.\build.ps1 -Mode GameRelease
.\build.ps1 -Mode GameRelease -Obfuscate
.\artifacts\Promzona\Release\Promzona.exe --language ru
```

Requires the repository's .NET 11 SDK for building. The packaged Windows x64 Release includes its runtime and requires OpenGL 3.3. No Suite SelfTest or native GPU SDK build is required for GameDebug/GameRelease. Game tests: `dotnet run --project Apps/Promzona.Tests -c Release`. The distributable is `artifacts/Promzona/Promzona-Windows-x64.zip`; do not zip the whole artifacts directory. Debug is unprotected. Internal-type diagnostic render harnesses should use an unprotected build.

## Graphics — 0.18.0

Twelve existing Poly Haven facade materials now have matched OpenGL normal, roughness and occlusion maps. The city packs these linear maps separately from colour; districts and moving scenery share one reference-counted atlas. High and medium quality add half-resolution, depth-aware ambient contact shading, bilateral filtering and edge antialiasing. HUD and menu are drawn afterwards.

A double-buffered local cubemap captures the real static neighbourhood and weather sky at 128 pixels per face. One face is refreshed per frame while an update is needed; the main scene never renders six additional views every frame. Nearby car glass/paint/chrome, building windows, puddles and wet asphalt use the probe, with a distance fade to the sky. This is approximate local reflection with spherical parallax and mip blur, not SSR, ray tracing or exact mirror geometry. Moving actors are excluded. Low quality disables the probe updates and post-process.

Ten vehicle model sets retain their doors and now have driven/steered wheel geometry in both colour and shadow passes. Paint, rubber, chrome and glass have different finishes; rain beads are distance-filtered. Close cars add bounded exhaust and wet-tyre spray, and night/rain headlights gain soft depth-tested scattering slices with centre-ray building/ground clipping. These are visual approximations, not a volumetric transport simulation.

People use the existing credited Quaternius skeletons: one rest mesh per body and a texture of joint palettes, four influences per vertex, interpolated idle/walk/run and the existing aiming pose. This replaces separate runtime meshes for each baked animation frame. Clothing/skin variation, actor dimensions, aim, knock-down, death, road levels and gameplay remain in place. Old source/baked assets are retained for editing and compatibility. This does not add full inverse kinematics or animation blending between actions.

Editable assets, licences, formats and regeneration commands: `Content/GRAPHICS-PIPELINE.txt`. CPU asset checks: `python Apps/Promzona.Tests/GraphicsAssetTests.py`. Existing saves work; no new city is required for the graphics.

## Fresh-source fixes — 0.17.1

Restored compilation after the actor-file split and a minimap local-name conflict. The default game test run now includes the review, compact-save, jobs, consequences, market, industrial-district and conversation tests, plus regression coverage for these fixes.

An empty owned weapon retains its ammunition-only price, including after save/load. A paid respray always changes the visible paint. Passengers leaving a car on an overpass or in a tunnel retain its road level. Garages and markets reserve space from street furniture before visual and collision geometry is built. The clinic displays its free-treatment notice even at the beginning of a session. Legacy flat islands allow a car to sink beyond the old terrain boundary; current canal beds retain their depth limit. Loading while the market is open closes the old shop overlay. Industrial buildings and railway geometry no longer receive terrain height twice; warehouses and rails stay on the ground.

Existing saves remain compatible. Back up `%LOCALAPPDATA%/Promzona/save.json` and its `.bak` before replacing the game or starting a new city: the game uses a shared autosave slot. Only New game adds the industrial district to a city that predates road revision 3.

The industrial siding uses one continuous terrain bed and a shared level area under the parked wagons; separate building foundations no longer create abrupt steps through the rails. Older road revisions keep their original terrain.

## A day of the gypsy cab — 0.17.0

The city gets something to do and something to lose. Details in Russian: `Docs/PROMZONA-GAMEPLAY-2026-10-29.md`; the rule summary is in `Content/BEHAVIOR-RU.txt` (0.17.0 section).

**Jobs (E).** Up to three people wait at bus stops 45–210 m from a driving player with an arm raised; stop beside one and press E to take the fare to another stop (20 $ plus 11 ¢/m, rounded to five). Kiosks hand out parcels for another kiosk (15 $ + 8 ¢/m), taken and delivered on foot at the counter or from a stopped car. Survivors of a car hit limp about where they fell for a minute; a stopped car beside them offers a clinic run (35 $ + 10 ¢/m). One job at a time: the HUD panel shows the kind, the destination street, the distance, the fare and the time allowed; the minimap marks hailers, the injured and the destination (an arrow at the rim when it is far). A late arrival pays half, every fright (new damage, a witnessed crime) takes a quarter off, leaving the car for 25 s or taking another one loses the passenger, death ends the job. E in a stopped car away from the destination drops the passenger for nothing. Streets have names (`CityNames`); the HUD uses them.

**Consequences.** A crime raises the wanted level only when somebody saw it: a living person within 45 m with a clear line of sight (60 m for a shot, heard rather than seen), an officer within 60 m or a crewed militia car within 70 m. The victim of a knock-down is a witness; a kill with nobody left to see it is not. Out of the militia's sight for 15 s (and 25 s after the last crime) the level fades five times faster. Below wanted level four a standing, non-aiming player on foot, or a driver sitting in a stopped car, is arrested by an officer who reaches him: a fine of 40 $ plus 60 $ per level, weapons and ammunition seized, 3 + level hours in the cell, release at the militia department (block 21/13, blue lamp and sign). Pursuit cars that reach a stopped player box it in and let their crew out instead of ramming on; a moving or aiming suspect is still chased and shot at. The clinic square charges 40 $ for a full recovery as the health comes back; with no money it still heals, three times slower. A death bills a quarter of the wallet from 40 $ up.

**Garage and market (E).** Garage boxes on vacant lots (and two cooperatives in the industrial district) repair the car the player sits in for 120 $ per full damage and, when wanted, respray it for 30 $ — a new colour and the pursuit called off. Market stalls in parks (and at the district market) open a list on E: a pistol with 30 rounds (120 $, then 40 $ per 30 rounds), an automatic (350 $ / 80 $), pills (25 $, +35 % health) and an energy drink (15 $). Esc leaves the list; the game pauses while it is open.

**Industrial district — road revision 3.** New 49-node games add five block rows at the north edge between the canals: halls with gates and chimneys, tank farms, warehouses and yards behind concrete fences, a through street along the front with two garage cooperatives and a three-stall market, and a railway spur that replaces one street row from the canal bank to a buffer stop ("ТУПИК") with three wagons left on it. Level crossings carry the vertical streets. Revision-2 and older saves keep their cities; outside the district the revision-3 blocks are the revision-2 blocks of the same seed (the generator's random stream is untouched).

**People on F.** Facing someone within two metres on foot, F addresses them: officers move a citizen along or shout at a wanted one, toughs warn (ruder at night), elders point to the clinic, drunks and the homeless ask for five dollars (a second F gives them), ordinary people gossip, give directions to the market or ask for a lift — which puts them at the kerb for the car to collect.

Saves use format 3 (compact actor states, Bezier route handles; formats 1 and 2 still load). Tests: `--saveformat`, `--jobs`, `--consequences`, `--market`, `--industrial`, `--talk`.

## Source audit — 29.10.2026 (0.16.0 patch)

Static audit of the whole game source; report in `Docs/PROMZONA-AUDIT-2026-10-29.md`. Fixed: the vehicle door meshes leaked on the GPU at every New game / Load (`CityVehicleModels.Dispose` cleared its dictionary inside the loop); an empty or unreadable `.wav` could crash the audio thread or end in the fault screen; closing with a lost GL context skipped the save and left the audio device open; the ~35 MB save was serialized on the render thread every 120 s and on every Esc — it is now written on the thread pool. Logic: off-duty militia squads and abandoned driverless cars leave the city out of view instead of accumulating for the whole session; a sunken player car settles on the canal bed; only hydrants spray after a soaking; NPC bullets hit the first bystander in line and charge the player nothing; respawn uses the nearest clinic. Performance: grid-pruned right-of-way and door-clearance checks, bucketed furniture and building queries for the reticle ray, no per-call LINQ allocations in frustum tests. `ReviewTests.cs` (28.10 review guards) did not compile against this tree; six guards are wired to `--review`, two that need the missing layout-version delta are withdrawn.

## Road contacts and more structures — 0.16.0

New cities contain five overpasses and four tunnels, spread across three land bands with seeded district placement. Revision-1 saves keep their original two structures; revision-0 saves keep their original layout. Choose New game for the denser layout, after backing up the common save slot.

Static collision proxies now have finite heights. Buried tunnel walls no longer extend into the surface street, and short objects below an overpass no longer act like tall invisible columns. Wheel probes stay on the car's selected road while a side contact is resolved. Curved barriers follow the visible segments; bridge slabs and crossbeams also block vehicles approaching from below without pushing vehicles on top sideways. This remains local planar contact physics with vertical bounds, not a general 3D rigid-body solver.

Ambient traffic is more likely to choose a connector before reaching its junction, including turns from side streets. Outer-lane approaches merge smoothly into the curved entrance. Eight existing cars per structure start on two-way routes, without increasing the total population. Ordinary replenishment rejects exposed approaches, and unregistered spawn candidates use their actual terrain height for overlap checks. Lane avoidance and pedestrian recovery ignore cars on another level. Each tunnel receives its own interior lighting.

The flush ramp entry accepts a vehicle's front before its centre changes layer. Drivers look beyond the crossing/ramp seam so long buses finish their turn instead of circling the last waypoint. Both entry directions are covered by physical sedan/bus regression scenarios.

## Roads on separate levels — 0.15.0

New 49-node cities include an elevated two-way connector over surface streets and a tunnel underneath a built district. Each is approximately 298 m along its curved approach, with two lanes, raised side walks, concrete barriers, reserved approach space and a distinct route in the traffic graph. The tunnel has a 4.9 m driving clearance, a covered central section, actual excavated entrances and ceiling lights. The overpass pillars leave the lower carriageways clear. The two tested structure templates vary their district placement with the city seed; this is not an arbitrary interchange or subway generator.

Terrain/foundation height is separate from the additional road surface. Cars retain their road level, use four wheel support heights and slope forces, and ordinary traffic can follow a connector both nearby and in the distant rule simulation. Contact pairs are filtered by vertical extent in a game-local copy of the existing planar solver; shared libraries are unchanged. This is still reduced vehicle/vertical dynamics, not a general 3D rigid-body engine. Ground pedestrians avoid open excavations; occupants may leave cars on the deck or in the tunnel. Camera obstruction, shots, local lighting, rain exposure, medicine/weapon drops, cash and road decals use the relevant level. The minimap distinguishes the tunnel with a dashed line. Remote medics also use a bounded navigation detour when scenery blocks their approach, without enabling distant contact physics.

**Choose New game to generate these structures.** RoadRevision and actor layers are stored within compatible format-2 saves. Existing saves without the new field retain the original city, roads and building locations; loading them does not cut a tunnel through an occupied block. Back up the common save slot before starting a new city. The previous canal bridges remain as before; navigation beneath canal bridges is not provided by this change.

## Street layout and vegetation — 0.14.0

Bus stops reserve a waiting area, sign and grounded bench before placing trees and movable furniture. Benches participate in both pedestrian avoidance and local car contacts. Tree rendering and collision proxies also reject building footprints, including kiosks. Silver birches use tapered white bark, curved branches and smaller hanging leaf sprays, with geometry baked into the existing district LODs.

Shop posters retain their original aspect ratio instead of stretching to fill arbitrary fascia widths. Low kiosks use a centred, readable sign below the roof. Coastal sand and asphalt now partition the same road footprint, including the outside half of perimeter streets; crossings reject any paint outside that footprint.

Bridge decks end at the bank approaches rather than occupying the parallel quayside carriageways. Rails and their collision proxies share the same shorter span. Smooth support-height joins are checked along with actual drive-throughs. These fixes apply to existing cities on load; obsolete prop placements are discarded by the existing compatibility filter. River widths and road topology are retained. Version 0.15 adds separate road-level support for the new overpass and tunnel templates described above.

## Draw distance and weather — 0.13.0

Graphics settings cover 130–3,000 m, with a 1,000 m default. Previous untouched 205/600 m registry defaults migrate once; other preferences are preserved. A 600 m choice saved by this version stays at 600 m on restart. Existing cities and saves work without regeneration. People and vehicle silhouettes extend up to 320/420 m respectively, always inside the selected scenery radius. High-detail actor distances and the 90/112 m local contact-physics bubble are unchanged.

Districts share shader setup per pass, use indexed static geometry and release their CPU upload buffers. Distant ground uses adaptive patches with the same one-metre border vertices as near terrain. A spatial furniture index, reused generation buffers, cached terrain samples, bounded tile work and LOD hysteresis reduce rebuilding. Camera and light frusta reject off-screen tiles; this is not building-occlusion culling. Far actors retain simplified route/rule simulation.

The camera clipping range follows zoom and scenery distance. One height-dependent, metre-based atmospheric extinction model covers terrain, buildings, windows, cars, people, animals, water and particles. Bloom is attenuated rather than adding fog light a second time. Clear weather has light aerial perspective; rain thickens the haze, reduces shadow contrast and brings in a dark cloud deck. Sky and analytic reflections share the same weather colours. Transitions use the existing smooth rain state, including after save/load; no new save format is needed. This is an analytic approximation, not volumetric cloud or multiple-scattering simulation. Wide-road crossings and analytic lamp positions match their geometry. The test project now includes CitySound and CityIncident after the supplied source refactor.

The maximum radius is optional and can include the whole island, so it still costs memory and rendering time. Actor replenishment uses visible-actor range plus an 80 m margin, independent of scenery distance; a whole-island view no longer prevents remote cars, emergency services and supplies from finding spawn locations. Short controlled native measurements accompany this release; they are not a guarantee of a particular frame rate in every scene or on other GPUs.

## Controls

- F: buy food while standing in a service circle on foot; otherwise leave/enter/carjack a stopped car; with no car door nearby, talk to the person in front.
- E: take or finish a job (fare, parcel, patient), use a garage forecourt from a stopped car, open a market stall's list on foot; in a stopped car away from the destination, drop the passenger.
- WASD in a car: accelerate, steer, brake/reverse; Space: handbrake.
- WASD on foot: camera-relative walking; Shift: run.
- Mouse on foot: turn and aim horizontally/vertically; LMB: fire at the reticle.
- LMB in a car: fire forward along the car heading.
- V: chase / elevated distant / near-top-down camera.
- Esc: pause and menu; F5: save; F9: load.
- R: recover an overturned stationary car. M: next original radio track.
- N/P: developer time/rain preview controls.

On-foot aiming uses the existing animated human assets, with extended arms and a height-aware first-hit ray. Entering a car requires a clear approach and can be cancelled by walking. Doors open during entry/exit and then close. All occupants of a stolen occupied car become separate displaced NPCs. Vehicle identity, paint and damage are retained. This is an arcade boarding sequence, not a full hand-to-door/pull-out animation.

## New world

Choose **New game** for the expanded island: 49 by 49 road nodes, approximately 1.84 km across and 2.12 times the former island area. Two canals divide districts; selected crossings have broad arched bridges with pedestrian paths and railings. The connected road graph routes traffic across bridges. Broader hills and graded banks replace abrupt slopes; level foundations protect building footprints.

One immutable one-metre height field drives ground rendering and actor support. Cars use four support samples, slope forces and supplementary vertical/pitch/roll motion. The game uses a planar contact solver with terrain support and vertical contact filtering for the new road structures, not a full 3D suspension engine. Geometry streams by district and is culled by the camera/shadow frusta. Detailed contacts enter within 90 m of the player and leave at 112 m; distant actors follow route/rule updates.

A sandy beach slopes into water, with visitors, towels and umbrellas. Water entry emits bounded splash effects. Cars sink; the player respawns on foot at a clinic; people/animals drown at their depth thresholds. Water death does not create an explosion or a blood pool. Shots that actually intersect water generate smaller splashes; intervening scenery still blocks shots. The water is a surface/bed and game-depth model, not a fluid solver or a swimming system.

## Characters and pursuit

On-foot damage is one fifth of the earlier preview. Ordinary traffic still uses mass-dependent vehicle physics. The player and NPCs collide with local tree trunks, including conifers.

Militia responders include patrol cars and a two-person bobik. At high wanted levels, four-person guard vans join with automatic weapons. Burning police try to stop and evacuate; responders near a wanted pedestrian stop and deploy a pursuing armed crew. Crews have distinct clothing and hats; medics wear white coats. Crowded exits are reserved before deploying everyone, avoiding duplicate crews or half-completed ownership changes. Driving and walking AI remains heuristic and can queue around complex obstructions.

New role variants include an elderly woman carrying bags, a slow swaying drunk, a homeless wanderer and clothed adult night workers who pace/linger and despawn after dawn only out of view. Beach visitors remain independent actors. Cats/dogs have revised editable GLBs; bags, bottles, cans and garbage sacks populate streets and parks.

## Saves — back up before starting a new game

The slot is `%LOCALAPPDATA%\Promzona\save.json`; the previous write is `save.json.bak`. Copy both elsewhere before trying this update if you want to retain a previous city. New game, automatic saves, opening the menu and normal exit can replace the active slot. See the explicit import command above for a save from a prior application identity.

Format 2 preserves the world, foot/player state, inventory, role/crew state, vehicle identities, wanted level and city dimension. Older 33-node saves retain their previous layout instead of being expanded under existing buildings. To see canals, bridges and the larger world, start a new game. Format 1 remains supported with its limited original state. Short-lived voices, particles, splashes and camera interpolation are recreated after load.

## Editable assets

Source assets: `Content/Models`, `Content/Textures`, `Content/Audio`. In a built game these are `Models`, `Textures`, `Audio` next to Promzona.exe. Existing license records are retained in `Content/Licenses`.

`Content/Tools/bake-promzona-vehicles.py` produces body/left/right door GLBs with saved pivots, filtered smooth normals and new bobik/guard variants. Original input meshes are retained in `Models/Source-Promzona`; prior third-party attribution is unchanged. `bake-promzona-animals.py` creates the revised original animal geometry. Existing bakers remain available. The menu background `Textures/menu-city.png` is an actual game capture; the title is rasterized at high resolution at startup. No new third-party photographs, models, songs or voice recordings were downloaded for this update.

Audio remains positional with original radio tracks and the prior credited asset set. Reflection, interiors and water highlights are inexpensive shader approximations, not ray-traced scene reflections. Models and animation remain stylized. The build is a playable prototype, not a certification of all hardware or prolonged stability.

## Services and avenues — 0.11.0

The cursor now hides on the generation/progress screen. A fresh city uses two lanes in each direction, with widened intersections and bridges, relocated pavements, reduced building footprints, and reserved road-height corridors beside canals. Old saves explicitly retain their original road widths; choose New game to see avenues. Ambient cars may change to a clear adjacent lane after a slow leader or a queue, with front/rear clearance and a cooldown. This is heuristic traffic, not a certified traffic planner.

Food circles are on foot: F buys a $5 snack (+20 percentage points of health) or $15 shawarma (+50). A full-health player pays nothing; insufficient funds never overdraw. Payment has its own short original sound. Blue clinic squares heal gradually (+7.5 percentage points per second). Initial play starts in the car; after death the player returns on foot at a clear clinic doorway. Destroyed/abandoned vehicles remain separate world actors.

The map maintains 48 energy drinks in addition to pills and rare weapons. A drink gives 25 seconds of 1.65x walking/running speed, with a green edge glow and countdown. It accelerates the player's movement, not the world clock or all traffic. The effect is saved, expires naturally, and is cleared on death; another drink cannot be consumed until the last two seconds. Random replenishment stays outside the current view and off undeveloped banks.

Bus-stop locations have signs and benches. Eligible buses on the outer positive-Z lane dwell for 4–7 seconds, then observe a cooldown. Passenger boarding and complete public-transport routes are not implemented. Yellow gas-service vans are ambient vehicles with their own appearance and 3,100 kg mass; they have no special service missions.

Broken hydrants spray at the original pipe for about 16 seconds. The render emitter is a bounded ballistic spray batch; it is a visual game effect, not a fluid-pressure solver. Infrastructure, movable prop homes and map pickups exclude canals and beach sand. Intentional beach visitors, umbrellas and towels are retained, and actors can still walk or fall into water.

The application has an embedded multiresolution icon. Editable original artwork, derived poster textures and asset provenance are in Content/Textures/Originals, Content/Textures/Services and Content/SERVICES-ASSETS-RU.txt. Content/Tools/bake-services.py reproduces the poster crops, icon and payment/energy sounds. Existing third-party credits remain unchanged.
