# State of the PROMZONA port to C++23

Base version: PROMZONA **0.44.1** from `Apps/Promzona` of the makarov-physics2 repository.
Checked on: 2026-11-13. The new project lives separately from the Suites solution.

## Readiness boundary

The native EXE builds and runs on its own. Full functional and
visual equivalence with the original game has **not yet been reached**. It is still too early to remove the original
PROMZONA from Suites on the basis of this build.

The original dependent part of the code is kept together with the assets: 11 projects,
581 unique C#/F# files, 1946 extracted files in total. The manifest contains SHA-256 hashes.
The copy in reference is meant for continuing the port and comparing behaviour;
it is not compiled as part of the C++ project. The old csproj files are kept as a description
of the dependencies; building this archived C# copy on its own is not promised.

## Already ported and verified

| Subsystem | Implementation and extent of verification |
|---|---|
| Vehicle dynamics | C++ port of VehicleDynamics: steering, longitudinal motion, brake, handbrake, lateral grip. 60 C# references, 120 steps each; tolerance 1e-9 for velocities/angular velocity, 1e-12 for steering. The references use a flat surface. |
| Layout | New games use revision 4. Six districts, residential block layouts, the industrial zone freight branch and seaside beaches have been ported. Old saves use revision 0, whose topology and plot sizes have been checked against 12 C# references. |
| Paths | Straight lines and cubic curves, length table, point lookup by distance, reversal. Streets and intersections are connected through a graph without position jumps; U-turns in dead ends. Verified: 1000 route transitions, all dead ends of the reference city and four minutes of local AI traffic. On four-lane streets drivers use both lanes of their direction and can smoothly overtake a slow car or a jam. Headway ahead, vehicles approaching from behind, pedestrians and road height are checked. The chosen lane and the current manoeuvre are kept when a game is loaded. |
| Terrain and canals | The natural terrain formulas with two canals and levelled building plots have been ported. The bed and the bridge surface are separate; the support and the road grid use one height function. Hits on water and falling street objects create splashes and spreading rings. Objects slow down in water. Deck edges, the absence of markings on water and bridges without road connection are checked separately. |
| Overpasses and tunnels | Five overpasses and four tunnels for the new map, two-way AI routes, a single height profile for geometry and support, walls/pillars/girders, cut-outs in the ground and the tunnel ceiling. A car keeps its level, a fall keeps the world height; leaving a car inside a tunnel does not move the player to the surface. Three seeds and physical AI runs in both directions are verified. This is an adaptation for the current native generator, not a bit-exact match with the revision 4 C# city. |
| Contacts | Local grid, circles/rectangles, SAT, impulses and friction, angular velocity, sleeping, filtering by height. Contacts with walls and pillars of the new road levels are added. Longitudinal support is the road profile with gravity on lift-off, not a general-purpose 3D rigid-body engine. |
| Car wrecks | Finite mass after destruction, falling and response to impulses. A downed police helicopter loses lift, falls and remains a physical object. Port of `WreckAfterContacts` and `WreckVerticalStep`: a wreck that is hit clatters, bounces (up to 1.5 m/s) and drops six fragments; one that falls faster than 2 m/s lands with a sound, raises smoke and bounces once more (12 % of the fall speed). A car on its wheels bounces after a jump at 22 % of its landing speed (`AfterContacts`). A wreck is burnt out, as in the managed `Vehicle.frag`: matt soot all over, with no reflections, no lamps and sooted glass. The wheels of an imported vehicle turn about the pivots of its `manifest.json`; `tools/repair_wheels.py` reports the models whose pivots were guessed by the importer and puts their wheels back on the axles (19.11.2026: `free_zuk_3d_model`). |
| Inventory | Four weapon definitions, ammunition issue, selection and switching, automatic switch when ammunition runs out. The bazooka gets 20 rounds. |
| Combat and damage | Fists, pistol, assault rifle, flying rockets and flamethrower; nearest hit by volumes taking height, obstacles and terrain into account. Explosions transfer impulse, damage nearby cars and can cause chain reactions. Contact damage depends on the change in velocity, not just on the car's speed. Where a bullet stopped short of its range, three sparks fly; a hit on the player on foot and a non-fatal hit on a passer-by are audible (`impact`), as is a punch against a car or a wall. |
| Fire | The flamethrower does not hit the shooter directly; fire from a burning car can spread to them. The player's burning is time-limited, an NPC's is usually fatal; the fire of the player's car can stop after safe driving. Critical damage and the transition to a movable wreck are checked separately. A burning car is heard every three seconds, with flames and a column of smoke above it; a car below 60 % durability smokes from under the bonnet, the thicker the more damaged it is (`VehicleLife`, `AfterContacts`). The driver of a burning car does not wait in traffic: the pedal goes to the floor, towards a way picked at random and changed every few seconds, and the car backs off whatever stops it (`panic_drive`). A tram, bus or trolleybus on fire stops as before, and a car with nobody in it stays where it is. |
| Wanted level | Six thresholds. Militia, reinforcements, Rosgvardia, the FSB and an APC come for the player in turn. At the sixth level a shooting helicopter appears. Port of crew movement from `CityTraffic.Pursuit.cs` (13.11.2026): when responding, a crew does not queue and waits neither for a traffic light nor for the queue at an intersection. On a street it goes around those ahead by shifting out of its lane to where there is room (within the carriageway), and brakes only for whoever is right in front of its bumper: down to 4 m/s behind a car and 5 m/s behind a pedestrian; oncoming traffic is accounted for with a margin of one and a half seconds of closing. In the distant model it moves 2.8 m towards the street axis and passes at no more than 12 m/s (`PoliceRuleStep`). In a direct chase, when the offender is visible, it steers away from obstacles like `PolicePursuit`. After the wanted level is cleared, a crew that left the street or stopped across it returns to the road on its own (`RejoinRoad`). Differences from C#: on a street, overtaking is done by shifting the lane rather than steering away from the bonnet line (with steering a car behind two stationary cars pulled out, lost sight of them and came back into their bumper); returning to the road leads along an arc to the nearest lane in the travel direction and its stop line, rather than in a straight line to the nearest stop line (five crews left at an intersection chose the same stop line on the opposite side and blocked the intersection); crew speeds are native — up to 13 m/s on a street and 12 + level in a direct chase (23 and up to 26 m/s in C#). A crew does not drive up to an offender on foot: seeing them closer than 19 m it stops, as in C#, and it approaches at a speed at which it can still stop. It approaches stationary vehicles ahead the same way: the C# rule "1.8 m/s for each metre to the obstacle" cannot be met from full speed, and crews ran into each other. An ambulance on its way to a victim does not wait at a red light (`IsResponding`), but gives way to those already in the intersection and does not enter if there is no room beyond it. Next to a victim with nowhere to stop (on a bridge above the street or behind a wall), an ambulance no longer circles until the end of the call: while it drives past, the call time runs nine times faster, and it will be sent to such a victim again after four minutes. |
| People animation | CityPersonMotion and CSK1 frames, state transitions, walking/running while aiming, the fallen pose. |
| Clock | Non-linear day cycle with longer mornings and evenings, start at 07:00. |
| Sound | WAV PCM, native stereo mixer, spatial attenuation and panning by camera, up to 32 effect voices. Engines, sirens, shots, rain, tyres, puddles and passers-by's remarks can be heard. Sound events are checked against C# by the call sites of `Sound`: added are a hit on a wreck and its fall, the crackle of a burning car, a hit on the player and on a passer-by, a punch on metal, running over an animal. Volumes set earlier in the native version (remarks, wings, the player's shots) are unchanged. The `pickup` event when filming with the drone is silent in C# (there is no such file); here `energy` and `cash` play. "Radio Pustyr" plays two tracks. Game and radio volume are kept between launches. |
| Assets | GLB, embedded images, WIC for PNG/JPEG, CSK1. All files of these types in Content load. Normalized game models are supported; this is not a universal importer for all glTF extensions. |
| Buildings | Deterministic facade plan, metric texture scale, normal/ORM mapping, windows with room depth and curtains, slowly changing lights, balconies, entrances, air conditioners. A single plan for the distant and near representation; details fade out at 135–185 m. This is an adaptation of part of the original decoration, not a full port of all building types. |
| Vegetation | Birches, lindens, spruces; safe placement on the terrain, trunk colliders, shared instanced models, two LODs, wind. This is an adaptation for the native generator; the original models of all trees have not been ported yet. |
| Sun shadows | Local 2048×2048 depth map, texel snapping, 3×3 PCF, shadows of buildings, cars, animated people and alpha-tested foliage. The pass is disabled at night; distant and point-light shadows are not implemented yet. |
| Run-overs and wanted level | The C# rules are ported: a 1.1 m/s threshold with the car moving towards the person, the survivor falling and limping, a fatal hit from 5.5 m/s, a hit on the player on foot, wanted level for a killing only with witnesses, wanted level decaying faster out of the militia's sight. The knocked-down state is saved. Before the port a car only pushed passers-by. |
| Localization | Russian, English and Ukrainian. Game text is written in Russian and translated on output using a phrase table (a port of the `Ukrainian.Translate` algorithm: longest phrase, word boundaries). A test requires a translation for every Russian string in the sources. 49 streets and 49 avenues from `CityNames` in three languages. |
| Importing C# saves | `--import-save`: formats 1–3, with the same checks as `GameSession.ReadSave`. Imported are the seed, time, weather, money, health and weapons, and from the `CityTraffic` state (`World.Clock`) the wanted level, job and series counters, earnings and the energy drink effect. A player saved behind the wheel gets a car of the same kind with its damage and color (not a tram or trolleybus); one saved on foot stands next to the car in which a new game starts. Position is restored only for revision 0 on the 49×49 grid, whose street plan the native generator reproduces: there the player's car or the player stands in the same place, ordinary traffic (cars, vans, trucks, buses, gas service) is put on its streets in its lanes, and passers-by on their sidewalks. The native composition of the city does not change: a saved car takes a native car of the same kind, a passer-by takes an ordinary native passer-by; routes, groups, roles, animals, items, pickups and services are not imported. A car saved in an intersection is placed at the near end of its street: native routes run from nine metres past the intersection to the stop line. In the test city at full population 1195 of 1320 ordinary cars and 3206 of 5151 passers-by returned to their places. The city is populated with the population chosen for a new game (before 12.11.2026 — always low). Verified on files composed from the C# code (`CityState.Capture`, field names and `<Name>k__BackingField` of auto-properties), not on a save from the real game. |
| Night lighting | Street lamps are computed in the shader from a height map of light fixtures (a port of `LocalLighting.glsl`), plus the headlights of the four nearest cars and up to four beacons. In the covered part of a tunnel the sky and sun are disabled and ceiling lights every nine metres are used. These sources cast no shadows in C# either; SSAO and reflections have their own rows below. |
| City renewal | The `LifeAfterContacts` and `VehicleLifeAfter` rules: a body left without help after 180 s, a wreck after 6 s and an abandoned car are replaced by a new passer-by and an ordinary car more than 245 m from the player. In C# actors are removed and created anew; in the native version the slot itself is renewed, so the population keeps its size. The area under an active drone counts as visible. The time of death is saved. |
| Water | A car deeper than 0.85 m sinks (the `falling` flag was previously never set) and returns to traffic after three seconds. A passer-by deeper than 1.15 m drowns without an incident. Water deeper than 0.35 m puts out burning people. An animal on the ground dies deeper than 0.75 m. A drowned body floats half a metre below the surface and sinks to the bottom at 0.6 m/s, as in `WaterStep`. |
| Potholes | The `CityRoadDetail.Holes` generator with the same xorshift sequence; values checked against an independent port. A jolt and body sway above 4 m/s. The `Textures/Road/pothole_*.png` photos are drawn as decals with dissolved edges. Potholes on bridges, ramps and at portals are skipped. |
| Interface | Weapon panel with `Textures/Hud` icons, separate health and car meters, district and street above the minimap. |
| Population | Four levels: 260/720, 540/1800, 1080/3240 and 1485/5265 cars and residents. The third is the initial population of the city in C# (`CityTraffic.cs`). The fourth ("full") is the population C# keeps in the game (`CityTraffic.Population.cs`): `ExpandPopulation` adds half again as many cars and brings the residents up to 4860, `ReplenishPopulation` tops the city up to its targets every 0.4 s — a tenth more cars of each kind and 5265 residents. In C# the city reaches this during the first three minutes of play; here it is created that way at once. Losses in both versions are replenished out of sight (the "City renewal" row), so the C# limits (6165 residents, 2200 cars) are not needed. The traffic mix is the same at all levels (`streetModel`): at the full level there are 892 cars — as many as the C# target; 90 buses and 90 gas service vans versus about 74 in C#, 40 ambulances versus 74. There are as many animals at the full level as in C# (`ResetLife`): 120 flocks of pigeons and 160 cats, dogs and birds; at the other levels 354, as in earlier saves. The level is chosen for a new game, written to the save and not changed on loading; saves without it open with low. The low level keeps the old generator sequence, so old cities do not change. |
| Clothing and build | The `Actor.frag` palette: 19 clothing variants, six skin tones, hair color, long or short sleeves and trousers; height, width and depth of the figure by resident number. Uniforms of the militia, Rosgvardia, FSB, army and ambulance with caps, bags for pensioners, a sack for the homeless. Coloring is done in the scene shader by vertex height in the standing pose, not with separate textures. |
| Resident roles | Port of `RolesStep`: beachgoers in groups at their towels, night workers at the kerbs from 21:00 to 05:00, homeless people at their spot, changes of cast only out of sight, end of duty for crews after a chase. Gaits: pensioners, drunks, homeless, office workers. The role is saved. |
| Pedestrians | Port of `ChooseWalk`: a resident walks from corner to corner of an intersection — across one of the two streets or along the block to the next intersection, on both sidewalks of each street and through all four crossings. They do not turn back immediately. A crossing with a traffic light is chosen only while the signal allows it (C# needs four seconds of margin, here nine and a half: the streets are wider); those who cannot cross walk on along the block instead of waiting. This way signals are obeyed throughout the city, not only near the player. The path bends to the right, as in C#; along the block only the one with the kerb on their right gives way, because facades stand right on the building line. Sides for new residents are taken from a separate sequence: the city of earlier saves does not change. Saves without corners open, and residents walk to the nearest corner. At the kerb they wait while cross traffic is moving or will move within the next eight seconds; those caught on the road hurry. A passer-by looks at where each moving car will be in the next two seconds. Oncoming and standing people are walked around (in C# — mutual repulsion; here a step aside), a stationary car is walked around its near end, a group lets the leader pass and lines up behind them. Someone who walks without making progress goes around the obstacle — on the side away from the carriageway while there is a way through; light litter is pushed aside, and the resident slides past a lamppost or wall without friction. Waiting at the kerb and hurrying on a crossing apply throughout the city: whoever comes into view is standing at the kerb rather than in front of cars that have a green light. Looking at cars and avoiding oncoming people and obstacles apply within 90 m of the player — where residents have a physical body; the C# boundary is the same (`PhysicsRadius`). Night workers stand on either of the two sidewalks in the middle third of a block: previously the spot was measured along the old route with a crossing and could fall on the corner of an intersection. |
| Intersections | One car per intersection (a port of the `CityTraffic.Enter` reservation), entry only when there is room beyond the intersection, long vehicles stop further from the stop line. In a turn the driver follows the curve rather than the direction of the bonnet; on a straight, the route, with a short check in front of the bumper. The bumper path is computed taking steering into account: in a turn it follows the curve, and for a car standing at an angle it returns to the lane. A tram and a trolleybus ready to go on green take the intersection next, one at a time: the second waits in front of it until the first has passed. They see a vehicle standing ahead even on a steep street (`World::onSameSurface`, the same rule as for cars). Near the player they brake for those on the rails ahead, including around a bend, and for the player on foot. On an overpass and in a tunnel they pass someone standing in their own oncoming lane, even though on the bend at the entrance that one ends up in front of the bonnet. The speed of a car far from the player equals the distance covered per step: a long car waiting at the stop line is stationary for those looking at it too. A turn started in the distant model is finished in it. Near the player, a driver who is off the route or across it and is blocked by what is ahead reverses and tries again (a port of `Stuck` and `ReverseFor`). A driver who has been pressing the throttle for 3.5 s while standing still also reverses: they have run into a post or tree they cannot see in front of the bumper. Service vehicles in a chase reverse by the `CityTraffic.Pursuit.cs` rule: if the target is more than three metres behind and the car is almost stationary, or if it cannot move off for 1.8 s, it reverses for 1.1 s with the wheels turned; if there is a car or a person behind, it holds the brake. A car that has stood for two minutes out of sight is replaced (`jamSeconds`); in test runs after the fixes this never happened. |
| Buses and gas service | Kinds 5 and 8 from `MakeCar`: one bus and one gas service van per sixteen traffic cars, with size and mass as in C#. Both vehicles are assembled from boxes, as in C# (there are no models for them); the lettering on the van is translated together with the interface. A bus keeps to the outer lane and does not change lanes; those following overtake it at once. Stops are a port of `CityAmenities.BuildStops`: an `outdated_soviet_bus_stop` shelter with a back wall and a bench, 94 in the reference city. In C# the generator reserves space for the shelter; here a stop that would touch a building, tree or street object is skipped. A bus stands at the shelter for 4–7 seconds and no earlier than 38 seconds after the previous stop (`BusStop`). Beyond C#: a bus does not turn into a dead end, and heavy vehicles have a low gear — otherwise the bus and the MAZ near the player could not move off on streets with a one-in-four gradient. Passengers do not board buses in C# either. A taxi ride ends at a bus stop 160–520 m from the passenger (`TaxiDestination`); which one is determined by the passenger's number, not by chance. Previously the taxi drove to a tram or trolleybus stop; saves with such a ride open as before. Taxis are hailed at a bus stop 45–210 m from the player, at the kerb between the shelter and the road (`MaintainHailers`). C# creates a new resident for this; here the population is constant, and a passer-by from the stop's block walks up to it. If there is nobody at the nearby stops, a passer-by at the roadside hails, as before. Locations of peaceful jobs are chosen among stops and kiosks within 700 m, nearest first, no closer than a hundred metres to each other (`CityTraffic.PeaceJobs`); near each location the first free sidewalk point is taken on three rings of eight points (2.8, 4.0 and 5.2 m), as in C#: not on the carriageway, not at an overpass or tunnel ramp, not in water, not in a shelter, car or passer-by. A location without such a point is skipped. A route from a save made before this points to the stops and kiosk entrances themselves, as before. |
| Visibility | Six camera planes and distance cull meshes and actors. Near facade details (72 m cells) are built in the background, uploaded to the GPU in portions and unloaded when far away. |
| City streaming | A port of the `CityDistricts` behaviour for base geometry: 144 m sectors within the draw distance are built by a worker thread with its own copy of the world, nearest first; a sector is uploaded to the GPU in portions (2 MiB or 1.5 ms per frame) and released 40 m after leaving the range. When a game is loaded, the sectors around the player are built at once. A sector closer than 60 m to the camera is built immediately. The simplified distant district levels (LOD 2 and 3) have not been ported. Tests: sector plan, geometry match, cancellation, queues, completion; the `streaming` scenario. |
| Sky and haze | A port of `CityAtmosphere` and `Sky/Weather.frag`: horizon and zenith colors, golden and twilight tints, sun, moon, clouds, stars; layered haze for the scene and effects. Tests compare colors and density for a clear noon, rain, night and a low sun. The look of the sky was judged from frames only. |
| Reflections | A port of `CityReflections` and `Reflections.glsl`: a local cube map of 128 pixels per face, one face per frame, the C# recapture rules (22 m, a quarter of an hour, rain, 12 seconds), parallax and a computed environment in the distance. Windows, water, puddles, wet asphalt, glass, chrome and car paint reflect. Tests: recapture schedule and face orientation. |
| SSAO | A port of `PostProcess/Occlusion.frag` and the occlusion from `Compose.frag`: half resolution, 8 or 12 samples depending on quality, depth-aware blur. Verified from frames. |
| Wet streets and weather | `Wetness` from `CityModel`: a street gets wet within half a minute and dries over minutes; grip, wet tracks and spray depend on it. A downpour lasts 45–135 seconds, dry spells 2–6 minutes; durations depend only on the seed and the moment. Saved; old saves are as wet as the rain had made them. |
| Particles and collision marks | A port of `CityParticle` and `CityTraffic.Emit`: sparks, smoke, debris, water, fire, sand and flamethrower tongues — each kind has its own lifetime, size, color and motion (smoke rises, expands and drifts; the rest falls and bounces off the ground). They are emitted by: car collisions (18 sparks, 8 fragments; a flash of fire for a hit that almost finishes the car off), car and rocket explosions, a rocket trail, a burning car and person, smoke from a damaged engine, a knocked-down street object (water from a hydrant), a splash, a skid (smoke) and driving on sand (dust), a bullet hit. After a collision dark streaks are visible on the car's sides for 100 seconds and a stain on the wall of a nearby building for 90 seconds (the `CarId` and facade marks from `AfterContacts`). Effects take random numbers from their own sequence (`Noise` in C#) and do not change the course of the simulation; previously sparks and explosions consumed the shared one. Differences: the frame here is 8-bit, so colors brighter than one (fire in C# is 3.0 in red) are multiplied by opacity before clamping rather than after: flames and sparks stay bright while fading, as in the C# floating-point frame. Smoke, debris and sand darken at night; bullet tracers remain the old particles. Tests: parameters of each kind, motion, events; the `wreckage` scenario. |

## Transport and jobs

- About ten percent of citizens are armed. After a run-over or a killing in front of them, armed witnesses may take aim and open fire. The pistol fires single shots, the assault rifle bursts of three. After death the weapon can be picked up. The line of fire takes people, vehicles, buildings and road levels into account. The sight of the weapon in the player's hand is drawn where the shot will land (`AimHit`), not in the middle of the screen: the camera looks from over the shoulder, to the side of the line of fire.
- 19 car model variants from Content are used; the bus and the gas service van are built without a model. The APC and the tank have a controllable turret, the helicopter can fly and land.
- Four city routes are served by trams and trolleybuses. Rails and overhead wires are shown in the world. Passengers take seats and get off at stops. Passers-by wait at a stop and walk to the door. The player boards as a passenger with the B key, can look around and request a stop at the next station. The ride and the waiting passengers are saved. The F key from the street lets the player take over control. A tram runs in both directions along its track. A trolleybus driven by the player loses traction more than three metres from its line (`TrolleyPower`); brakes and steering work. Unlike in C#, the driver is told about this.
- Seven jobs work. Checked against `CityTraffic.Contracts.cs` and `Jobs.cs` (13.11.2026): the car to be moved stands without a driver, as in C# (previously its driver was pulled out, and this counted as a carjacking); the contract target does not hail a taxi or leave on a tram while the contract is active; a ride ends 11 m from the address after standing for 0.8 s (was 9 m and one second); each further 12 % of damage frightens the passenger again, so one heavy hit counts twice. Fares, deadlines, the late penalty, the series of three contracts and its bonus already matched. Messages that existed in C# but not here have been added with the original texts in three languages: "We're late — they'll pay half" at the moment of being late, reasons for failure ("Job failed", "Dropped off without payment", "The passenger got tired of waiting and left") instead of the generic "Job closed without payment", and "Treatment is paid — without money it is slow" every six seconds at the clinic. The remaining notification texts are native and do not match the original word for word. Taxi and medical rides use the city's pedestrians. The passenger walks to the door, gets in, gets out and continues on their way. Delivering a patient to the clinic restores their health. A parcel has a sender and a recipient; moving a car requires the specified car with at least 50% health. The two civilian jobs have separate completion conditions and an escape-the-chase phase. Elimination checks the target's identity.
- Up to two ambulance crews respond to calls to victims. The paramedic gets out of the vehicle, walks to the patient, provides help and returns to the vehicle. Crew assignment, route and treatment progress are kept when a game is loaded.
- Reconnaissance uses a separate control state and camera. The player on the ground remains vulnerable. The drone has a battery, a range limit and three targets to film.
- Market stalls sell weapons with ammunition, separate ammunition, pills and energy drinks. Workshops repair vehicles and repaint a wanted car. The player can buy a garage for three cars. Ownership, position and color of each car are kept when a game is loaded.
- Vending machines and kiosks sell food; clinics heal and serve as respawn points. Energy drinks temporarily speed up movement on foot.
- Groups of up to three people and delivery workers with backpacks walk the sidewalks. Couriers also ride scooters. After a fall the scooter becomes a separate physical object. The owner may come back for it, and the player can pick it up and ride away.
- Witnesses react to a threat, scatter, may then stop at a distance from the incident and later return to their route. Armed residents use building corners and parked cars as cover, step out of cover to shoot and react when the car covering them drives off.
- On foot, the player can talk to a passer-by with the F key. Residents give directions to the nearest kiosk, market or clinic, ask for change or offer a ride. If the player agrees, that very person becomes the passenger.
- Flocks of 6–10 pigeons gather in parks. When frightened, a flock flies to another park. As in C#, some animals live on the sidewalks: cats and dogs walk from corner to corner of a block, 22 % of flocks settle by the streets in two rows along the kerb, single birds on the sidewalk. The kind and flock of each animal are the same as in earlier saves; the place is taken from a separate sequence. A car closing on an animal faster than 1.1 m/s throws it aside and stuns it for three seconds; it dies when closing faster than 4 m/s (`LifeBeforeContacts`). Previously any car faster than 5 m/s killed it on the spot. Cats react to dogs and passers-by; birds react to people, vehicles and other animals. Far from the player only movement is updated; nearby, threat perception and collisions too.
- All 12 kinds of carryable and knock-down street objects have their own size and mass. The city has bins, hydrants, tanks, benches, payphones, soda machines, mailboxes, bags, bottles, cigarette packs, tin cans and garbage bags. Wind picks up bags; bottles and cans can roll down a slope. A hit by a car moves or tips them over. Water gushes from a broken hydrant for 16 seconds. If the player has driven away from both the fallen object and its original spot, the object is restored. The save remembers the position of knocked-down objects and how long the fountain has been running.
- Version 7 saves store animals in flight, scooter owners and riders, witnesses' interest, cover and job stages. When loading versions 5 and 6 the population is supplemented with the new roles.

The minimap bakes static objects when the city loads. Vehicle markers, targets and the player's heading are drawn on top of the map. When the interface changes, only the screen strips it occupies are updated. Key hints switch when the vehicle or the available action changes.

On foot, press F at a stopped car. The character walks to a free front door. Pressing F again, walking or shooting interrupts boarding. In a carjacking the crew gets out onto the street. An abandoned vehicle is left without a driver. B is used to ride public transport as a passenger.

In an almost stationary car the R key tows it to the nearest free lane; for a tram and trolleybus, to their own route. Towing can be repeated after eight seconds. Side impacts rock the body. If the player's car lies on its roof for four seconds, it lifts itself and lands on its wheels. When the doors of a stationary car are blocked, F looks for a free spot on the same road level.

At the first three wanted levels a patrol can arrest the player. An officer gets out of the car and walks up to the player on foot or to the door of the stopped car. After the arrest the player comes out on foot at a militia station. The game applies a fine, elapsed hours, confiscation of weapons and a reset of the wanted level. An unfinished arrest is saved together with the world.

The job log offers repeatable series of three contracts. Passers-by can hail a taxi at the roadside, and the fare takes into account the arrival time, accidents and crimes on the way. The game saves the series stage, earnings and the passenger's state. The F1 console supports history, editing and command completion. The help command shows the available commands.

The menu and pause support three slots, writing a world snapshot in the background, autosave and choosing settings with the mouse or keyboard. Draw distance, detail, particles, glow, resolution and sound settings apply in the current game.

## What remains to match C# 0.44.1

1. Population and services. Animals on the sidewalks, reversing of service vehicles, pedestrian signals throughout the
   city and crew movement when responding were ported on 13.11.2026 (see the table and "Verification").
   Crews getting out of their cars (`DeployCrew` in `ServicesStep`) is not ported: in C# a militia car
   stops 19 m from the player on foot, the crew gets out, keeps 11 m away and shoots or
   arrests one who has surrendered. Here at levels 1–3 a single officer gets out of the nearest car and
   makes the arrest while the other crews wait; at levels 4–5 militia cars stop 19 m from
   the player on foot and block the street, and shooting is done only from vehicles: the FSB, the APC and the helicopter. The car
   keeps its crew and drives off on its own after the wanted level is cleared (in C# it is abandoned and the crew leaves
   on foot). After a fight, wrecks at an intersection hold up
   the traffic around it while the player is near: drivers do not go around a wreck in a turn, and wrecks and
   stuck cars are removed only out of sight, as in C#. Where there is a ramp or a portal next to the player,
   crews do not see them and circle the block. After a long blockage of
   intersections on a tram street a gridlock is possible: a tram that entered the
   intersection after another one waits; cars going straight along the tracks wait for any tram in the
   intersection, even an oncoming one; distant trams do not check for room beyond the intersection. Found in
   a run before the chase changes and not fixed; in normal traffic there is no such gridlock. At full
   population, night, rain and driving have not been measured for frame time.
2. Graphics. The simplified distant district levels are not ported (in C# a district beyond 198 m
   is built without small parts, beyond 350 m in groups of four blocks; here a loaded
   sector is complete). Scene lighting is native (GGX in linear light), not the C# shaders: the sky,
   haze, reflections, street lamps and particles are ported; the rest of the lighting has not been compared with the original pixel by pixel. The frame
   is 8-bit; fire and sparks, which are brighter than white in C#, keep their brightness while fading thanks to
   premultiplied blending (`effects.frag`), but the overall "shoulder" compressor of the C# frame
   (`Compose.frag`) has not been ported — the native scene has its own color grading and glow.
3. Gameplay. Console commands have been compared: all C# commands are present (`money`, `health`, `repair`,
   `god`, `wanted`, `weapon`, `spawn`, `hour`, `rain`, `cycle`, `speed`, `help`, `clear`),
   with the native `weapons`, `recover`, `jobs`, `spawn van/truck` on top. Contracts and rides
   have been compared (see "Transport and jobs"). The events the game reports have been compared by calls to
   `Message`; the notification and hint texts themselves are native and have not been compared with C# word for word.
4. Verification. Comparing long sessions and identical scenes with the original in behaviour,
   graphics, frame time and memory. Save import has not been verified on a file from the real game.

## Verification

24 CTest groups cover calculations, assets, the city, combat, transport, pursuit, jobs, run-overs, city renewal, localization, save import, population density, residents, road traffic and buses. `tools/smoke.ps1` is used for graphical checks on Windows. The set includes arrest on foot and in a car, all six districts, the beach, the canal mouth, streaming, a tunnel, night rain, braking on wet asphalt, a chase, the tank, a tram, a taxi, delivering a parcel, buying at a stall and repainting a car in a bought garage, leaflets, the drone, a scooter ride, a courier with a backpack, a flock taking off, helping a victim, hitting a hydrant, an explosion, a burning and smoking car (`wreckage`), a payphone, a soda machine and driving through a puddle in dry weather. Separate scenarios check the radio menu, shooting from the embankment into the canal and an NPC car changing lanes to get around an obstacle. Images and logs are saved to `build/smoke`. Numeric reference tests and short gameplay runs complement each other. A separate tram run went through 36,000 frames (600 seconds of simulation), including saving and loading the game. There is no comparable long benchmark of both versions yet.

The changes of 11.11.2026 were checked in two stages. First stage (run-overs and wanted level, localization,
save import, night lighting, controls, formatting): the game EXE was built with MSVC through
`Promzona.vcxproj` in Debug and Release without errors, logs in `build/msbuild/x64/*/obj/Promzona.log`.
The second stage (city renewal, water, potholes, tunnel lights, weapon panel) and the CMake test projects
of both stages were not built with MSVC. All the code was checked with GCC 13 on Linux (19 of 20 CTest groups:
`native_assets` builds only on Windows and was not run), a Clang 18 syntax pass
and a MinGW-w64 build; this EXE passed 59 of 61 `smoke.ps1` scenarios under Wine with software
OpenGL (Mesa llvmpipe).
`tram-ride` and `trolley-ride` (4200 frames each) were not run. The software renderer checks
scenario logic and shader compilation, but not performance or the behaviour of graphics card
drivers. Save import was verified on files composed according to the C# format, not on a
save from the real game.

The changes of 12.11.2026 (population, clothing and roles, pedestrian signals, traffic rules) were not built
with MSVC: the computer with Visual Studio was unavailable during the work. Checked with GCC 13 on Linux
(22 of 23 CTest groups, without `native_assets`), clang-format 18 formatting, a Clang 18 syntax
pass over the Windows files and a MinGW-w64 build without warnings.

Before these changes traffic in the city came to a halt by itself: in the build of 11.11.2026, after 30 minutes
of simulated time 117 of 204 ordinary cars were standing, and with 1080 cars the whole city stopped within
25 minutes. The causes were found one by one and fixed: trucks in adjacent lanes held each other up;
a car in a turn took vehicles standing at the oncoming stop line, those arriving from an overpass
and a tram at the edge of the intersection for obstacles; on steep streets two cars entered the intersection; cars
entered without room beyond the intersection; trams stopped across the intersection and at busy
intersections could not get out at all; oncoming lanes on diagonal sections of overpasses and tunnels
ran 2.6 m apart; tall vehicles ran into the girder under the deck; the leader of a group and its
members pushed each other; people waiting for a tram at a stop on a bend stood on the carriageway.
After the fixes two cities (seed 20261105 and 7) at high density ran for 30 minutes each without
a single car standing for 100 seconds and without replacing stuck ones. The first city also ran 60
minutes at high density and 30 minutes each at medium and low: at the checkpoints every five minutes
there were no cars standing longer than a minute and no replacements of stuck ones. Average traffic speed was 3.1–3.2 m/s at
high density and 3.3–3.4 m/s at low. NPC cars hitting pedestrians were counted in runs before
the last changes: from zero to two in 30 minutes at high density, with no deaths.
This is a check of the model without the player at the wheel, from a single observation point. GPU frame time
at high density was measured later, see below.

Buses, gas service vans and stops (12.11.2026, second part) were not built with MSVC either.
Checked with GCC 13 on Linux (23 of 24 CTest groups, without `native_assets`), clang-format 18, a GCC and Clang 18
warnings pass and a MinGW-w64 build without warnings. Under Wine `bus`
(in Russian and English), `day`, `drive`, `traffic-lanes`, `transit`, `people`, `night` and
`day --density high` passed; the other `smoke.ps1` scenarios were not run after these changes.

With buses in the traffic, the same runs found four more causes of traffic stopping near the player:
a bus turned around in a dead end; a passer-by who stopped right next to a cigarette pack could not
push it aside, and a truck waited for him at the turn; a passer-by ran into a lamppost, and a bus in the turn
waited for him to step away; a truck coming out of a left turn took a truck in the
oncoming lane for an obstacle. After the fixes two cities (seed 20261105 and 7) at high density and the first
city at medium and low ran for 30 minutes each without a car standing for 100 seconds and without replacing
stuck ones. There was one NPC car hitting a pedestrian in 30 minutes at high density (seed 20261105);
the victim survived.

Known limitation. Near the player cars are driven by a physical model that steers towards a point
ahead on the route (like `DriveRoute` in C#, where this point is even farther: 2.8 m + 0.55 s of travel versus
0.35 s here). When turning right from the outer lane, a car cuts the corner of the sidewalk: at an empty
intersection a passenger car goes about 0.5 m onto it, a truck and a bus 1.2 m; the turning curve
from the outer lane is sharper than the steering of long vehicles allows. Hence the rare hits on
people standing right at the corner. This was not measured in C#.

MSVC build. On 12.11.2026 at 14:47 everything listed above as not built with MSVC was built on
the author's computer (`build.ps1`, directory `build/native-Release`): CTest — 24 groups of 24, including
`native_assets`. Compiler warnings were not reviewed. The full `tools/smoke.ps1` on
Windows was not run after the changes of 12.11.2026.

Pedestrians at intersection corners (12.11.2026, after the MSVC build) are not covered by that build.
Checked with GCC 13 on Linux (23 of 24 CTest groups), clang-format 18, a GCC and
Clang 18 warnings pass and a MinGW-w64 build without warnings; under Wine `people`, `taxi`, `street-hail`,
`transit`, `walk`, `day`, `night`, `bus`, `courier` and `delivery-rider` passed. Traffic runs with the new
walking: seed 20261105 and 7 at high density, seed 20261105 at medium and low and at night (23:00,
high density, night workers at their spots) — 30 minutes each without a car standing for 100 seconds
and without replacing stuck ones. There were no NPC cars hitting pedestrians in 30 minutes at high density.
About a third of the chosen path segments are crossings. People hailing at stops were added after these
runs: they appear only when the player is driving, and the runs go without the player. For them CTest,
the MinGW-w64 build and the `street-hail`, `taxi` and `day` scenarios under Wine were repeated; for peaceful jobs at
stops, `leaflets`, `job-board` and `contract-series`.

The full population (12.11.2026, evening) was checked before the MSVC build with GCC 13 on Linux (23 of 24
CTest groups), clang-format 18, a GCC and Clang 18 warnings pass and a MinGW-w64 build without
warnings. Under Wine the final EXE passed the new `crowd` scenario — the `day` scenario in a
city with full population (1485 cars, 5265 residents, saving and loading such a city), as
well as `day`, `transit`, `traffic-lanes`, `people`, `bus` and `tram-recovery`; an earlier build of
this stage, with only the first of the fixes listed below, passed the C# save import with
`--density full`, `overpass`, `underpass`, `portal`, `tunnel`, `tunnel-exit` and `bridge`. The
`tram-ride` and `trolley-ride` scenarios were not run under Wine. A model step at the full level takes about
one and a half times longer than at high (3.5 versus 2.4 ms on the test machine).

Traffic runs at the full level and in a third city (seed 99) found four traffic stoppages
(items 1–3; the fourth had two causes, items 6 and 7), a bug that would have kept the first fix from
working for long vehicles (item 4), and the cause of run-overs (item 5). They do not
depend on the number of cars: the second stoppage reproduces at low density in the eleventh minute.

1. At the ends of overpasses and tunnels the road curves sideways, and a van that had just entered it
   took a truck queuing in the oncoming lane for an obstacle; the car behind the van
   stayed in the intersection, and the queue waited for the intersection to clear. Now someone
   standing in their own oncoming lane is passed; someone driving in it is still given the bend.
2. A tram and a trolleybus of different routes approached an intersection side by side: the tram turned
   right from the middle lane, the trolleybus left from the kerb. Both requested the intersection,
   entered and stopped nose to nose. Now trams and trolleybuses take the intersection one at a time.
3. On a steep street a tram did not see a tram standing ahead while the height difference between them was
   more than two metres, and stopped already inside it; after that neither could move off.
   Now "the same street" for trams and trolleybuses is determined the same way as for cars.
4. A long vehicle waiting at the stop line far from the player was counted as moving at traffic
   speed. Rules that look at the speed of the vehicle ahead (room beyond the intersection, the queue
   in the oncoming lane) took it for a moving one. Now its speed equals the distance covered per step.
5. Near the player a tram looked for people only along its body. When turning into a street it hit
   with its corner those walking across that street's crossing: in the third city there were two such hits in 30 minutes, one
   fatal. Now trams and trolleybuses look along the rails, including around a bend, and
   take into account where a passer-by will be in a second; they see the player on foot the same way as passers-by
   (previously they did not see the player at all).
6. A truck that started a turn in the distant model (along the paths, 7 m/s) switched to its
   own steering in the middle of the turn: the intersection was 104 m from the player, the model boundary at 110 m. At that
   speed it came out of the turn wide, nose towards a van at the oncoming stop line. Now a turn
   started in the distant model is finished in it.
7. After that the truck and the van stood nose to nose: each stopped in front of what was in front of its
   bumper, and the driver had no other way out. In C# there is one: a driver pushed off
   the route reverses and tries again (`Stuck` and `ReverseFor` in `CityTraffic.Physics.cs`).
   Ported: near the player, a driver who is off the route or across it and is blocked by
   what is ahead reverses for 1.7 s after 3.5 s if there is no one behind.

Checks were added to `native_flow` and `native_transit`; each of them fails without its fix.

From this stage on a run counts a car standing for 300 seconds as a jam, and waits of 100 seconds or more
separately: in one of the intermediate runs a van waited 100 seconds to turn into a street where, in front of
the next traffic light, trolleybuses of two routes and a bus were queuing, and then drove off.
After the fixes the following passed: full level — seed 20261105 (60 minutes), 7, 99 and night (23:00, seed
20261105), 30 minutes each; high — seed 20261105, 7 and 99; medium — seed 20261105; low — seed
20261105 and 99. In no run did any car stand for 300 seconds, there were no replacements of stuck ones,
nobody died. There was one wait longer than 100 seconds: a bus at the full level stood for 106 seconds
and drove on. Average traffic speed was 3.1 m/s at the full level, 3.2 at high, 3.3 at medium
and 3.4 at low. NPC cars hitting pedestrians: at the full level none in 60 minutes in the first
city, three in the second and one in the third in 30 minutes, none at night; none at the other levels.
Two of the three hits in the second city were a bus in a right turn catching with its side someone
waiting at the corner of the sidewalk (corner cutting, see above); the victims survived.

Known limitation. On the bends at the ends of overpasses and tunnels long vehicles are cramped: along the paths
an oncoming bus and truck overlap at the corners by up to 0.4 m, two trucks by up to 0.2 m; a car and a
truck pass with a margin of 0.5 m, two cars with 1.3 m. Far from the player cars follow the paths
and do not collide with each other; near the player physics keeps them apart. The road profile is ported from
`CityRoadLevel`.

MSVC build of stages 6–8. On 12.11.2026 at 23:38 corner-to-corner walking, stops in jobs and the full
population with all fixes were built on the author's computer (`build.ps1`, directory
`build/native-Release`): CTest — 24 groups of 24. Compiler warnings were not reviewed.
The full `tools/smoke.ps1` on Windows was not run after the changes of 12.11.2026: of its scenarios only
`crowd` was run on the graphics card (measurement below).

Frame time on the author's computer: RTX 4060 Laptop, 3440×1440, quality mode, 360 frames from
a fixed point, `--gpu-profile` switch. High population (the `day --density high` scenario, before
the walking port): frame 9.0 ms median and 9.8 ms at the 95th percentile, GPU scene 4.1 and 6.4 ms.
Full population (the `crowd` scenario, 1485 cars and 5265 residents, final code of stage 8): frame
11.4 ms median and 12.4 ms at the 95th percentile, GPU scene 3.8 and 7.7 ms. The extra 2.4 ms
falls on the CPU: the median scene time on the graphics card did not increase. After this measurement
full population was made the default. Night, rain and driving were not measured.

Changes of 13.11.2026: graphics (SSAO, sky, haze, reflections, wet streets, sector
streaming), animals on the sidewalks, reversing of service vehicles, pedestrian signals throughout the city, the peaceful
job spot on the sidewalk, particles, collision marks and missing sounds, import of the career,
the player's car and the traffic of the old layout, comparison of contracts and rides. They were not built with MSVC and were not run on
a graphics card: the author's computer was unavailable during the work. Checked in the cloud
environment: GCC 13 on Linux — 23 CTest groups of 23 on the final code (without `native_assets`);
clang-format 18; a GCC and Clang 18 warnings pass over the changed files; shaders —
glslangValidator and the Mesa compiler; a MinGW-w64 build without warnings. Under Wine with software
OpenGL (Mesa llvmpipe, 640×360) this EXE passed all 63 `smoke.ps1` scenarios that were run;
`tram-ride` and `trolley-ride` (4200 frames each) were not run, as before. After that run
only the number check in the import and the `wreckage` scenario changed in the code: the scenario and the import of
a composed save of a whole city (25 MB; 1195 of 1320 ordinary cars and 3206 of 5151 passers-by
returned to their places) passed with the final EXE. A build that had only the new
graphics and sectors passed 40 of the 40 scenarios run before being stopped.

Traffic after these changes (core of the final code, 30 minutes of simulated time each): full
level — cities 20261105, 7, 99 and night; high and low — 20261105. No cars standing for 300
seconds, no replacements of stuck ones and no deaths. One wait of 101 s (a bus, city 7). NPC car hits:
two in 30 minutes in city 99, none in the other runs. Traffic speed 3.1 m/s at the full
level, 3.2 at high, 3.4 at low.

Not verified: the MSVC build and `native_assets`; `tools/smoke.ps1` on Windows; frame time of the new
graphics and particles on a graphics card (under the software renderer the new graphics cost 7–10 % of the frame
time, which is no estimate for a graphics card); driver behaviour. Sky, reflections, particles and marks
were judged from software-rendered frames at 640×360 and 960×540 and were not compared with C# pixel by pixel.
The import was verified on saves composed from the C# code, not written by the game.

Changes of 13.11.2026, second stage (after the verification described above): animals thrown aside by a car,
sinking bodies, a car bouncing after a fall, premultiplied particle blending, job
notifications, crew movement when responding, the ambulance on a red light and at a victim who cannot
be reached. Not built with MSVC: the computer with Visual Studio was unavailable. Checked with GCC 13 on Linux —
CTest 23 of 23 without `native_assets`, clang-format 18, a `-Wall -Wextra -Wshadow` pass over the changed
files without warnings — and a MinGW-w64 build. Under Wine with software OpenGL 41 of 63
scenarios were run, and all passed: `pursuit`, `arrest`, `arrest-car`, `carjack`, `tank`, `drone`, `wreckage`,
`wildlife`, `hydrant`, `canal-impact`, `taxi`, `contract-series`, `job-board`, `leaflets`, `drive`,
`rollover`, `day`, `night`, `storm`, `traffic-lanes`, `transit`, `crowd`, `people`, `ambulance`,
`bus`, `walk`, `canal`, `tunnel`, `market`, `console`, `street-hail`, `courier`, `recovery`,
`scooter`, `delivery-rider`, `overpass`, `underpass`, `tunnel-exit`, `streaming`, `bridge`,
`tram-recovery`. Most of them ran on intermediate builds of the stage; on the final code
`pursuit`, `arrest` and `ambulance` were repeated. The remaining 22 scenarios (districts, facades, park, menu, settings,
slots, workshop, vending machines, puddles, potholes, portal) were not run in this stage.

Services were checked with runs at full population: the offender stands on foot at the corner of an intersection, god
mode, five stars. Before the changes, at a corner by a ramp where crews could not see him, during eight minutes of wanted level
crews got stuck in queues 30–45 m from him: 62 cars were standing, one APC stood on the path for 471 s;
a quarter of an hour after the wanted level was cleared 84 cars had been standing for more than 100 s, and the last-resort
rule replaced 10 and 32 stuck vehicles in 23 minutes (cities 20261105 and 7). After the changes, at an ordinary
corner, three minutes of wanted level in four cities: no deaths, one car wrecked in one city
(before the changes, none in two cities). Four and eight minutes after the wanted level was cleared, if
the offender stays in place, 0 and 0 cars stand longer than 45 s in city 7, 22 and 39 in city 20261105,
35 and 56 in city 99, 13 and 30 in city 4242; before the changes, 40 and 52 in city 7, 22 and 18 in city
20261105. The jam is held by crews left at the scene of the chase in the oncoming lane or right next to each
other, and by a wreck; when the player leaves, the city renewal rules remove them.

The ambulance, twenty minutes and twelve victims across the city without a chase, final code:
in city 20261105 everyone is on their feet, the last one after 508 s, the longest trip 346 s (before the changes 625
and 296 s); in city 7, 256 and 166 s. Six in each run were replaced out of sight. No replacements
of stuck cars.

Traffic, 30 minutes each at the full level, cities 20261105 and 7 (core after the crew movement port,
before the ambulance and approach speed changes, which do not affect ordinary drivers): not a single car
standing for 100 seconds, no replacements of stuck ones and no deaths, speed 3.1 m/s, one NPC car hit.
Night and the other population levels were not repeated in this stage.

Not verified in this stage: the MSVC build, `native_assets`, `tools/smoke.ps1` on Windows, frame time
on a graphics card. Crew behaviour has not been compared with C# on identical scenes: the rules are taken from the
C# code, the numbers were obtained on the native model.

Code pass of 14.11.2026: standard, C++23 features, bug search. Starting point — the folder on the
PC as of 15:31 on 14.11 (after stage 10, courtyards, lawns, distant materials, the scenarios
`yard-*`, `lawn`, `far-vehicle` were added on the PC; renaming to `namespace Promzona` and snake_case was started).

Standard. Both builds on the PC already use `/std:c++latest`: CMake 4.3.1 maps
`CMAKE_CXX_STANDARD 23` to this switch, and `Promzona.vcxproj` has `stdcpplatest`; the compiler is
MSVC 14.52.37025, which implements C++23 completely and almost none of C++26. The build switches were not changed.

Fixed:
- the tests did not build after the renaming (`pz` → `Promzona`, 61 functions): they are not in `Promzona.vcxproj`,
  so a build from the IDE did not show this; the tests were brought in line with the new names;
- `SectorStream::stats().busy` was always true after the comparison was replaced with `std::cmp_not_equal(active_, -1)`
  (`active_` is a `size_t`), and the streaming test failed on this; the number of the cell being built
  is now `std::optional<size_t>` in both streams;
- nine MSVC C4804 warnings in `src/roles.cpp` (shift on a `bool`): the `walkLegs` bits got
  names (`World::WalkLeg`, `World::walkLegBit`), the layout is pinned with a `static_assert`;
- `src/vehicle_entry.cpp` did not build with GCC and Clang without `<utility>`; in 28 more files standard
  library headers came only through other headers — they are now included explicitly;
- the icon file is named `PROMZONA.ico`, while CMake, the project and the `.rc` called it `Promzona.ico`;
- `} // namespace pz` comments in 65 files; the `aiming` parameter in `src/civilian_cover.cpp`
  shadowed `World::aiming`; fields without initial values (`ServiceSite::kind`, `Building::height`,
  `ContactWorld::Contact::a/b`, `PlayerInput::car/walking`).

Language and library features:
- `std::jthread`, `std::stop_token` and `std::condition_variable_any` in `SectorStream` and
  `DetailStream` instead of a stop flag and a manual `join`;
- `std::views::zip` in eleven loops over two parallel containers; the remaining algorithms with
  iterator pairs were moved to `std::ranges` (`contains`, `iota`, `partial_sort` with a projection,
  `unique`, `upper_bound`…), `views::reverse`, `views::take`, `views::values`;
- `std::to_underlying` instead of casting enumerations to integers (50 places);
- `std::format` where a string was assembled from `std::to_string` and is not translated (12 places; HRESULT
  and OpenGL codes are now hexadecimal); `resize_and_overwrite` in `wide`/`utf8`; `uz` literals;
- tests: `std::println` instead of streams, `std::source_location` in `check`/`require` (a failure
  message names the line), `std::format` for temporary file names;
- `[[nodiscard]]` on results that must not be lost: `poll`, `build`, `pack`, `planSectors`,
  `trim`, `captureSave`, `saveLanguage`, `saveDensity`, `AudioLevels::save`, `SaveSlots::completed`.

Not done:
- Russian strings are still concatenated: translation goes by catalogue phrases, and `std::format` with
  placeholders would change the phrases themselves;
- index loops were not moved to `views::enumerate`: almost everywhere the index serves as the
  participant's number (`int(i) == controlledCar`);
- `roadDistance`, `Footpath::edgeDistance` and pair sorting in physics were left without adaptors and
  projections: in an MSVC debug build they cost more than a plain loop;
- `import std` and modules (the build would need restructuring, which cannot be verified here); `std::print` in the
  game itself, `ranges::to`, `flat_map`, `mdspan`, `generator` (they are missing in the MinGW-w64 GCC 13 used
  to check the EXE); `std::expected` (errors are reported with exceptions throughout);
- the renaming to snake_case and the replacement of `int(x)` with `static_cast` started on the PC were not continued;
- `SectorStream` and `DetailStream` still duplicate each other (queue, hand-off, stop).

Verification of this pass. Not built with MSVC: everything was checked in the cloud environment before being written to the PC.
GCC 14.2 and Clang 20 with `-Wall -Wextra -Wpedantic -Wshadow` build the core and tests without
warnings; CTest (GCC 14) — 23 of 23. The tests now need `<print>`: GCC 14 or MSVC.
GCC 14 sanitizers together with `_GLIBCXX_ASSERTIONS`: address and undefined behaviour — 23 groups
of 23 on the PC state and seven on the final code (`native_core`, `streaming`, `people`,
`drone`, `transit`, `front_end`, `environment`); undefined behaviour with float-to-integer
conversion checks — 23 of 23 on the final code; threads — `streaming` and `front_end`.
No findings. clang-tidy 20 (bugprone, performance, modernize, part of misc, cppcoreguidelines and
readability) over all 51 translation units, including the Windows files, and cppcheck 2.13 found no
defects. MinGW-w64 GCC 13 builds the EXE and `native_assets` without warnings; `native_assets`
passed under Wine for the first time, on a reduced set with the PC's distant materials: 354 GLB, 17 skins,
153 images, 51 WAV. Scenarios under Wine with software OpenGL: PC state — `day`, `drive`,
`streaming`, `pursuit`, `crowd`, `wreckage`; the EXE with `_GLIBCXX_ASSERTIONS` on an intermediate build —
the same six plus `settings`, `save-slots`, `job-board`, `console`; on the final one — `day`,
`far-vehicle`, `lawn`, `yard-car-day`, `people`, `transit`, `drone`. All passed; the other
scenarios were not run. Not verified: the MSVC build with `/W4`, `tools/smoke.ps1`, frame time.
