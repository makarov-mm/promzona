#include "promzona/world.hpp"
#include "../src/street_lighting.hpp"
#include "../src/cloud_map.hpp"
#include "promzona/atmosphere.hpp"
#include "promzona/city_mesh.hpp"
#include "promzona/reflection_probe.hpp"
#include "promzona/sun_shadow.hpp"
#include "promzona/minimap.hpp"
#include "promzona/control_hints.hpp"
#include "promzona/hud_damage.hpp"
#include "promzona/visibility.hpp"
#include "json.hpp"
#include <array>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <print>
#include <limits>
#include <map>
#include <stdexcept>
#include <string>
#include <source_location>
#include <format>
using namespace Promzona;
namespace
{
int checks{};
void check(bool ok, const char *message, std::source_location where = std::source_location::current())
{
    ++checks;
    if (!ok)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
Vec3 project(const Mat4 &m, Vec3 p)
{
    return {m.m[0] * p.x + m.m[4] * p.y + m.m[8] * p.z + m.m[12],
            m.m[1] * p.x + m.m[5] * p.y + m.m[9] * p.z + m.m[13],
            m.m[2] * p.x + m.m[6] * p.y + m.m[10] * p.z + m.m[14]};
}
void headlightSelection()
{
    World world;
    world.cars.resize(5);
    for (size_t i = 0; i < world.cars.size(); ++i)
        world.cars[i].body.p = {double(i * 4), 0};
    Mat4 projection = Mat4::identity();
    projection.m[0] = projection.m[5] = projection.m[10] = .01f;
    Frustum frustum(projection);
    HeadlightSelection lights;
    auto slotFor = [&](int car)
    { return std::ranges::find(lights.slots, car, &HeadlightSelection::Slot::car); };
    lights.update(world, {}, frustum, true);
    check(slotFor(0) != lights.slots.end() && slotFor(4) == lights.slots.end(),
          "four closest beams selected");
    world.cars[0].body.p.x = 200;
    world.time += .125;
    lights.update(world, {}, frustum, true);
    check(slotFor(0) != lights.slots.end() && slotFor(0)->intensity == .5f,
          "displaced headlights fade in place");
    check(slotFor(4) == lights.slots.end(), "replacement waits for fading pair");
    world.time += .125;
    lights.update(world, {}, frustum, true);
    check(slotFor(0) == lights.slots.end() && slotFor(4) != lights.slots.end() &&
              slotFor(4)->intensity == .5f,
          "new headlights fade in");
    lights.update(world, {}, frustum, false);
    check(std::ranges::all_of(lights.slots, [](auto slot) { return slot.car < 0; }),
          "daylight clears active pairs");
}
void streetLighting()
{
    World world;
    world.buildings.push_back({{0, 15}, {3, 2}, 7});
    world.busStops.push_back({10, 0});
    world.terrain.generate(12, 33, world.buildings);
    world.setWeather(1);
    world.cars.resize(3);
    world.cars[0].body.p = {0, 0};
    world.cars[1].body.p = {0, 6};
    world.cars[2].body.p = {0, -16};
    HeadlightSelection lights;
    lights.slots[2] = {0, 1.f};
    StreetLighting street;
    street.prepare(world);
    street.update(world, {}, lights);
    check(street.lampIndex == 4 && street.blockerCount == 2,
          "beam retains the crossing car and the wall ahead");
    check(street.coverCount == 2, "both a bus stop and an apartment canopy keep rain off the pavement");
    const auto centre = World::shelter(world.busStops.front());
    check(std::abs(street.covers[0].centre.x - centre.x) < .001, "bus shelter rain uses model origin");
    world.cars[1].body.p = {35, 2};
    world.setWeather(0);
    street.update(world, {}, lights);
    check(street.coverCount == 0 && street.blockerCount == 1,
          "dry weather and moving traffic update independently");
    street.update(world, {}, {});
    check(street.lampIndex == -1 && street.blockerCount == 0, "daylight clears occluders");
    Frustum view(Mat4::identity());
    const auto casters = view.extruded({10, 0, 0});
    check(!view.sphere({5, 0, 0}, .1f) && casters.sphere({5, 0, 0}, .1f),
          "off-screen casters toward the sun remain visible");
    check(!casters.sphere({-4, 0, 0}, .1f), "caster beyond the viewing volume casts away from it");
    const auto clouds = cloudMap(64);
    const auto [low, high] = std::ranges::minmax(clouds);
    check(clouds == cloudMap(64) && low < 100 && high > 160,
          "cloud deck has repeatable clear and overcast patches");
}
void shadows()
{
    FarShadowCache cache;
    using Portion = FarShadowCache::Portion;
    const Vec3 testSun = Vec3{1, 1, 0}.unit();
    const auto first = cache.advance(true, {}, testSun, 1);
    const auto left = cache.advance(true, {5, 0, 0}, testSun, 1);
    const auto right = cache.advance(true, {6, 0, 0}, testSun, 2);
    check(first.portion == Portion::full && left.portion == Portion::left && right.portion == Portion::right,
          "first map is complete and normal refresh takes two frames");
    check(left.readTarget != left.writeTarget && left.visible.m == first.visible.m &&
              right.readTarget == left.writeTarget && right.visible.m == left.draw.m,
          "the visible shadow map keeps a matching matrix through either half");
    check(cache.advance(true, {6, 0, 0}, testSun, 2).portion == Portion::left,
          "streaming changes survive a pending capture");
    check(cache.advance(true, {100, 0, 0}, testSun, 2).portion == Portion::full,
          "teleport replaces a partial capture");
    check(cache.advance(false, {}, testSun, 2).portion == Portion::none &&
              cache.advance(true, {}, testSun, 2).portion == Portion::full,
          "enabling daylight restores a complete map");
    for (auto sun : {Vec3{1, 1, .3f}, Vec3{0, 1, 0}, Vec3{-1, .12f, .3f}, Vec3{1, -1, 0}})
    {
        Vec3 focus{127, 19, -235};
        auto s = sunShadow(focus, sun);
        for (auto x : s.matrix.m)
            check(std::isfinite(x), "finite shadow matrix including zenith");
        auto centre = project(s.matrix, s.centre);
        check(centre.length() < .0001f, "shadow volume centred on focus");
        auto toward = project(s.matrix, s.centre + sun.unit() * 20);
        check(toward.z < centre.z, "occluder toward sun has smaller depth");
        check(std::abs(toward.x) < .0001f && std::abs(toward.y) < .0001f, "sun rays share shadow texel");
        Frustum f(s.matrix);
        check(f.sphere(focus, 1), "focus inside shadow volume");
        check(!f.sphere(focus + sun.unit() * 600, 1), "outside light depth rejected");
        check(sun.y < 0 ? s.strength == 0 : s.strength > 0, "night shadow disabled");
        Vec3 across = cross(sun.unit(), Vec3{0, 1, 0}).unit();
        if (across.length() > .5f)
        {
            const auto farMap = sunShadow(focus, sun, SunShadow::farHalfSize, SunShadow::farResolution);
            const Vec3 distant = focus + across * 240.f;
            check(!f.sphere(distant, 1) && Frustum(farMap.matrix).sphere(distant, 1),
                  "far cascade covers distant casters");
        }
        auto repeat = sunShadow(s.centre + Vec3{.001f, .001f, .001f}, sun);
        check(std::abs(repeat.matrix.m[12] - s.matrix.m[12]) < 1e-6f &&
                  std::abs(repeat.matrix.m[13] - s.matrix.m[13]) < 1e-6f,
              "subtexel motion preserves xy projection");
    }
}
void geometry()
{
    for (auto kind : {TreeKind::birch, TreeKind::linden, TreeKind::spruce})
        for (float height : {5.8f, 10.3f})
        {
            TreePlant tree{{17, 4, -18}, height, height * .28f, kind, 123};
            size_t nearTriangles{};
            for (bool far : {false, true})
            {
                TreeMesh m;
                appendTree(m, tree, far);
                size_t triangles{};
                for (const auto &part : m)
                {
                    check(!part.vertices.empty(), "tree has bark and foliage");
                    check(part.indices.size() % 3 == 0, "complete tree triangles");
                    for (auto index : part.indices)
                        check(index < part.vertices.size(), "valid tree index");
                    for (const auto &v : part.vertices)
                    {
                        check(std::isfinite(v.position.x + v.position.y + v.position.z), "finite vertex");
                        check(std::abs(v.normal.length() - 1) < .001f, "unit tree normal");
                        check(v.position.y >= tree.foot.y - .025f &&
                                  v.position.y < tree.foot.y + height * 1.3f,
                              "bounded tree height");
                        check((v.position - tree.foot).length() < height * 1.35f, "bounded crown");
                    }
                    for (size_t k = 0; k < part.indices.size(); k += 3)
                    {
                        auto a = part.vertices[part.indices[k]].position;
                        auto b = part.vertices[part.indices[k + 1]].position;
                        auto c = part.vertices[part.indices[k + 2]].position;
                        check(cross(b - a, c - a).length() > 1e-7f, "nondegenerate tree faces");
                    }
                    triangles += part.indices.size() / 3;
                }
                if (!far)
                    nearTriangles = triangles;
                else
                    check(triangles < nearTriangles * .5, "far tree halves triangle cost");
            }
        }
}
// The colours of the sky and the haze of the managed game for a few known hours, and
// when the reflection probe is taken again.
void skyAndProbe()
{
    auto same = [](Vec3 a, Vec3 b) { return (a - b).length() < 1e-5f; };
    const auto noon = Atmosphere::create(0, 1, 1, 0);
    check(same(noon.horizon, {.64f, .73f, .80f}) && same(noon.zenith, {.19f, .39f, .65f}) &&
              noon.golden == 0 && std::abs(noon.density - .00052f) < 1e-8f && noon.heightScale == 140,
          "clear noon: pale horizon, blue zenith, thin haze");
    const auto downpour = Atmosphere::create(1, 1, 1, 1);
    check(same(downpour.horizon, {.39f, .44f, .49f}) && same(downpour.zenith, {.24f, .28f, .33f}) &&
              std::abs(downpour.density - .00332f) < 1e-7f && downpour.heightScale == 240,
          "rain at noon: grey sky, thick and tall haze");
    const auto night = Atmosphere::create(0, 0, -1, 0), dampNight = Atmosphere::create(0, 0, -1, 1);
    check(same(night.horizon, {.025f, .036f, .058f}) && same(night.zenith, {.006f, .012f, .027f}) &&
              night.golden == 0,
          "clear midnight is nearly black");
    check(std::abs(dampNight.density - night.density - .00018f) < 1e-8f, "a wet street steams at night");
    // A sun a little above the horizon: the hour of golden light, redder in the evening.
    const auto morning = Atmosphere::create(0, .63, .1, 0, false),
               evening = Atmosphere::create(0, .63, .1, 0, true);
    check(std::abs(morning.golden - 1) < 1e-6f && std::abs(morning.density - .00076f) < 1e-7f,
          "low sun in clear air is golden");
    check(evening.horizon.y < morning.horizon.y - .05f && evening.horizon.z < morning.horizon.z - .05f &&
              evening.horizon.x > evening.horizon.z + .3f && same(evening.zenith, morning.zenith),
          "the evening horizon is redder than the morning one");
    check(Atmosphere::create(1, .63, .1, 1, true).golden == 0, "rain puts the golden light out");
    const double nothing = std::numeric_limits<double>::quiet_NaN();
    const auto broken = Atmosphere::create(nothing, nothing, nothing, nothing);
    check(same(broken.horizon, noon.horizon) && broken.density == noon.density,
          "bad input means a clear noon");
    check(std::abs(noon.transmittance(1000) - std::exp(-.52)) < 1e-6 &&
              std::abs(noon.transmittance(1000, 0, 140) - std::exp(-.52 * (1 - std::exp(-1.)))) < 1e-6 &&
              noon.transmittance(1000, 0, 300) > noon.transmittance(1000, 0, 140) &&
              downpour.transmittance(1000) < noon.transmittance(1000) && noon.transmittance(0) == 1 &&
              noon.transmittance(1e9) > 0,
          "haze thins with height and thickens with rain");

    ReflectionProbe probe;
    const Vec3 at{10, 2, 20};
    check(!probe.ready() && probe.captured() == 0, "no cube before the first frame");
    double time = 5;
    for (int face = 0; face < 6; ++face, time += 1. / 60)
    {
        // The player moves on while the cube is taken; it is finished where it began.
        check(probe.begin(at + Vec3{face * 9.f, 0, 0}, 12 + face, face * .1, time, face) == face,
              "one face of the cube a frame");
        check(same(probe.pending(), at) && !probe.ready(), "the cube in use changes only when complete");
        check(probe.finish(time) == (face == 5), "the sixth face completes the cube");
    }
    check(probe.ready() && same(probe.centre(), at) && probe.captured() == 6, "the finished cube is in use");
    const double done = time - 1. / 60;
    check(probe.begin(at + Vec3{21, 0, 0}, 12.2, .1, done + 11.9, 0) == -1,
          "the cube holds within 22 m, a quarter of an hour, a little rain and twelve seconds");
    auto begun = [&](Vec3 centre, double hour, double rain, double seconds, int generation)
    {
        auto copy = probe;
        return copy.begin(centre, hour, rain, seconds, generation) == 0;
    };
    check(begun(at + Vec3{0, 0, 23}, 12, 0, done + 1, 0), "a new cube 22 m further");
    check(begun(at, 12.3, 0, done + 1, 0), "a new cube when the light has changed");
    check(begun(at, 12, .2, done + 1, 0), "a new cube when the rain has changed");
    check(begun(at, 12, 0, done + 12.1, 0), "a new cube every twelve seconds");
    check(begun(at, 12, 0, done + 1, 1), "a new cube when other scenery is loaded");
    check(begun(at, 12, 0, done - 30, 0), "a new cube when the clock went back");
    for (int face = 0; face < 6; ++face)
    {
        const Mat4 m = ReflectionProbe::projection() * ReflectionProbe::view(at, face);
        auto clip = [&](Vec3 p)
        {
            return std::array<float, 4>{m.m[0] * p.x + m.m[4] * p.y + m.m[8] * p.z + m.m[12],
                                        m.m[1] * p.x + m.m[5] * p.y + m.m[9] * p.z + m.m[13],
                                        m.m[2] * p.x + m.m[6] * p.y + m.m[10] * p.z + m.m[14],
                                        m.m[3] * p.x + m.m[7] * p.y + m.m[11] * p.z + m.m[15]};
        };
        const Vec3 along = ReflectionProbe::direction(face), up = ReflectionProbe::up(face);
        const auto centre = clip(at + along * 10), top = clip(at + along * 10 + up * 9.9f),
                   behind = clip(at + along * -10);
        check(std::abs(centre[0]) < 1e-4f && std::abs(centre[1]) < 1e-4f &&
                  std::abs(centre[3] - 10) < 1e-4f && behind[3] < 0,
              "each face looks along its axis");
        check(std::abs(top[0]) < 1e-4f && top[1] > .98f * top[3] && top[1] < top[3],
              "a face spans a right angle, its up vector at the top");
        check(dot(along, up) == 0, "the up vector of a face lies in it");
    }
    probe.reset();
    check(!probe.ready() && probe.captured() == 0 && probe.begin(at, 12, 0, 0, 0) == 0,
          "a new city, a new cube");
}
// Weather of the managed game: showers of uneven length, and streets that soak within
// half a minute and stay wet for minutes after the rain. Grip follows the wet street.
void weather()
{
    const double frame = 1. / 60;
    {
        World w;
        w.generate(20261105);
        check(w.wetness == 0 && w.weatherClock == 90, "a new game starts dry, the first change in 90 s");
        w.setRain(true);
        check(w.rainTarget == 1 && w.weatherClock == 90, "the console holds its weather for 90 s");
        for (int i = 0; i < 30 * 60; ++i)
            w.step({}, frame);
        check(w.rain > .98 && w.wetness > .72 && w.wetness < .82, "half a minute of rain soaks the street");
        w.setRain(false);
        for (int i = 0; i < 60 * 60; ++i)
            w.step({}, frame);
        check(w.rain < .01 && w.wetness > .5 && w.wetness < .6, "the street is wet a minute after the rain");

        const auto file = std::filesystem::temp_directory_path() /
                          std::format("promzona-weather-{}.json",
                                      std::chrono::steady_clock::now().time_since_epoch().count());
        w.rain = .25;
        w.save(file);
        World restored;
        restored.load(file);
        check(restored.wetness == w.wetness && restored.rain == .25, "the wet street survives saving");
        nlohmann::json old;
        {
            std::ifstream in(file);
            in >> old;
        }
        old.erase("wetness");
        {
            std::ofstream out(file);
            out << old;
        }
        restored.load(file);
        check(restored.wetness == .25, "an older save is as wet as it rained");
        old["wetness"] = 1.5;
        {
            std::ofstream out(file);
            out << old;
        }
        bool refused = false;
        try
        {
            restored.load(file);
        }
        catch (const std::exception &)
        {
            refused = true;
        }
        std::filesystem::remove(file);
        check(refused, "an impossible wetness is refused");

        double shortest = 1e9, longest = 0, driest = 1e9, calmest = 0;
        for (int i = 0; i < 400; ++i)
        {
            w.time = 100 + i * 7.3;
            const double shower = w.weatherSpell(true), spell = w.weatherSpell(false);
            check(shower >= 45 && shower < 135 && spell >= 120 && spell < 360,
                  "showers of 45 to 135 s, dry spells of two to six minutes");
            shortest = std::min(shortest, shower);
            longest = std::max(longest, shower);
            driest = std::min(driest, spell);
            calmest = std::max(calmest, spell);
        }
        check(shortest < 55 && longest > 125 && driest < 145 && calmest > 335, "spells differ in length");
        World same;
        same.seed = w.seed;
        same.time = w.time;
        check(same.weatherSpell(true) == w.weatherSpell(true), "a game replays its weather");
        same.seed += 1;
        check(same.weatherSpell(true) != w.weatherSpell(true), "another city has other weather");
    }
    // The same stop on a dry and on a wet street, with no rain falling in either.
    auto speedAfterBraking = [&](double wetness)
    {
        World w;
        w.generate(20261105);
        w.weatherClock = 1000;
        w.wetness = wetness;
        auto &car = w.cars[0];
        car.body.v = forward(car.heading()) * 20;
        PlayerInput stop;
        stop.car.brake = true;
        for (int i = 0; i < 30; ++i)
            w.step(stop, frame);
        return car.body.v.length();
    };
    const double dry = speedAfterBraking(0), wet = speedAfterBraking(1);
    check(dry < 19 && wet > dry + .3, "a wet street lengthens the stop after the rain is over");
}
void precipitationAndTracks()
{
    World w;
    w.generate(20261105);
    for (double t : {0., 72., 732., 86401.})
        for (double eye : {-20., 0., 45.})
        {
            double height = rain_height(.32, t, eye);
            check(height >= eye - 8 && height < eye + 20, "long-running rain stays in the camera column");
            double follow = rain_height(.32, t + .001, eye + .05) - height;
            check(std::abs(std::remainder(follow, 28.) + .014) < 1e-7,
                  "rain falls in world space as camera moves");
        }
    for (const auto &level : w.roadLevels)
    {
        Vec2 centre{level.x, level.centreZ(level.x)};
        check(w.precipitationSurface(centre) >=
                  level.floor(w.terrain, centre) + (level.tunnel ? level.clearance : 0) - .001,
              "the upper surface shelters traffic");
        if (level.tunnel)
        {
            Vec2 ramp{level.x - 85, level.centreZ(level.x - 85)};
            check(std::abs(w.precipitationSurface(ramp) - level.floor(w.terrain, ramp)) < .01,
                  "rain reaches open cut");
        }
    }
    auto &building = w.buildings.front();
    check(w.precipitationSurface(building.p) >= w.terrain.height(building.p) + building.height - .001,
          "rain stops on building roof");
    Vec2 canal{w.layout.coordinate(w.nodes / 3) + 18., w.layout.coordinate(23) + 18.};
    double surfaceY = CityLayout::waterLevel;
    Vec2 east{1, 0};
    auto water = w.traceAttack(canal - east * 2., east, surfaceY + 2., -1., 6., -1);
    check(water.kind == HitKind::water && std::abs(water.distance - 2.) < 1e-8,
          "downward ray first hits the water plane, before the canal bed");
    check(w.traceAttack(canal - east * 2., east, surfaceY + 2., 0., 3., -1).kind == HitKind::none,
          "horizontal shot above canal stays in air");
    bool bridgeTested = false;
    for (int row = 0; row < w.nodes; ++row)
    {
        Vec2 deck{canal.x, w.layout.coordinate(row)};
        if (!w.layout.bridge(deck))
            continue;
        auto hit = w.traceAttack(deck - east, east, w.terrain.height(deck) + 2., -1., 20., -1);
        check(hit.kind != HitKind::none && hit.kind != HitKind::water, "deck intercepts shot above water");
        auto under = w.traceAttack(deck - east, east, surfaceY + 1., -1., 3., -1);
        check(under.kind == HitKind::water && std::abs(under.distance - 1.) < 1e-8,
              "the space between the canal water and the underside of the bridge is open");
        bridgeTested = true;
        break;
    }
    check(bridgeTested, "found a canal bridge");
    int originalCar = w.controlledCar;
    Vec2 driver = w.cars[originalCar].body.p;
    w.cars[originalCar].body.p = canal;
    auto state = w.random.state;
    for (int i = 0; i < 100; ++i)
        w.splash(canal, .35);
    check(w.ripples.size() == 96 && w.random.state == state,
          "local waves bounded and cosmetic RNG independent");
    w.cars[originalCar].body.p = driver;
    w.ripples.clear();
    w.sounds.clear();
    w.particles.clear();
    w.cars.resize(1);
    auto &c = w.cars.front();
    c.body.v = {0, 10};
    c.braking = true;
    w.setWeather(0);
    c.body.p = {0, 0};
    c.body.angle = 0;
    w.updateTireMarks({}, .016);
    c.body.p.z += .55;
    w.updateTireMarks({}, .016);
    check(w.surfaceMarks.size() == 2 && w.surfaceMarks.front().kind == MarkKind::rubber,
          "both braking wheels mark the road");
    check(std::ranges::count(w.sounds, std::string{"brake"}, &SoundEvent::name) == 1,
          "held braking starts one tyre squeal");
    auto count = w.surfaceMarks.size();
    c.body.p.x += 40;
    w.updateTireMarks({}, .016);
    check(w.surfaceMarks.size() == count, "teleport leaves no streak between roads");
    w.time = 100.001;
    c.body.v = {};
    w.updateTireMarks({}, .016);
    check(w.surfaceMarks.empty(), "old marks fade and are retired");
    {
        // The same holes as the managed CityRoadDetail.Holes; values from an independent port.
        struct Reference
        {
            double x, z, size;
            int style;
        };
        auto same = [](std::uint32_t seed, int i, int j, std::initializer_list<Reference> expected)
        {
            const auto holes = blockPotholes(seed, i, j, true, true);
            if (holes.size() != expected.size())
                return false;
            size_t n = 0;
            for (const auto &e : expected)
            {
                const auto &h = holes[n++];
                if (std::abs(h.p.x - e.x) > 1e-8 || std::abs(h.p.z - e.z) > 1e-8 ||
                    std::abs(h.size - e.size) > 1e-8 || h.style != e.style)
                    return false;
            }
            return true;
        };
        check(same(20261105u, 7, 9,
                   {{-1.193432116, 14.571315605, 0.778603234, 2},
                    {15.641075066, -2.583215809, 0.640165977, 2},
                    {24.459320775, -2.369727615, 0.49362853, 0}}) &&
                  same(20261105u, 20, 31, {}) &&
                  same(424242u, 3, 44, {{20.655780616, 1.621442316, 0.971985232, 1}}) && same(1u, 0, 0, {}) &&
                  same(2147483647u, 47, 47,
                       {{-2.844013667, 19.355881756, 0.788490725, 1},
                        {15.606715962, -1.515348952, 0.95434953, 2}}),
              "potholes follow the managed sequence");
        check(blockPotholes(20261105u, 7, 9, false, false).empty() &&
                  blockPotholes(20261105u, 7, 9, true, false).size() == 1,
              "a block without a street has no potholes");
        // A car above 4 m/s is kicked once; slow cars and cars beside the hole are not.
        check(!w.potholes.empty(), "generated city has potholes");
        const auto hole = w.potholes.front();
        Car jolted = w.cars.front();
        jolted.destroyed = jolted.falling = false;
        jolted.body.roadLevel = 0;
        jolted.lift = jolted.roll = jolted.pitch = jolted.pitchRate = jolted.verticalVelocity = 0;
        jolted.impactCooldown = 0;
        jolted.body.angle = 0;
        jolted.body.p = hole.p + Vec2{hole.size * .5, 0};
        jolted.body.v = forward(jolted.heading()) * 3.9;
        w.potholeJolt(jolted);
        check(jolted.verticalVelocity == 0 && w.potholeJolts == 0, "a slow car rolls through a pothole");
        jolted.body.v = forward(jolted.heading()) * 10;
        jolted.body.p = hole.p + Vec2{hole.size * .7, 0};
        w.potholeJolt(jolted);
        check(jolted.verticalVelocity == 0 && w.potholeJolts == 0, "a car beside the pothole is not kicked");
        jolted.body.p = hole.p + Vec2{hole.size * .5, 0};
        w.potholeJolt(jolted);
        check(std::abs(jolted.verticalVelocity - .35) < 1e-9 && std::abs(jolted.pitchRate - .18) < 1e-9 &&
                  jolted.impactCooldown == .5 && w.potholeJolts == 1,
              "a pothole kicks the car");
        w.potholeJolt(jolted);
        check(w.potholeJolts == 1, "one jolt per half second");
        jolted.impactCooldown = 0;
        jolted.body.v = forward(jolted.heading()) * 40;
        w.potholeJolt(jolted);
        check(jolted.verticalVelocity == .8 && w.potholeJolts == 2, "the kick is capped");
        // Every hole in a chunk is one textured quad just above the lane paint.
        const double x0 = std::floor(hole.p.x / 144) * 144, z0 = std::floor(hole.p.z / 144) * 144;
        const auto chunk = city_chunk(w, x0, z0, 144);
        size_t owned = 0, vertices = 0;
        for (const auto &other : w.potholes)
            owned += other.p.x >= x0 && other.p.x < x0 + 144 && other.p.z >= z0 && other.p.z < z0 + 144;
        for (int style = 0; style < 3; ++style)
        {
            const auto &mesh = chunk[PotholeDecal + style];
            vertices += mesh.vertices.size();
            check(mesh.indices.size() == mesh.vertices.size() / 4 * 6, "pothole decals are quads");
            for (const auto &v : mesh.vertices)
                check(v.normal.y > .9f && v.uv[0] >= 0 && v.uv[0] <= 1 && v.uv[1] >= 0 && v.uv[1] <= 1 &&
                          std::abs(v.position.y - float(w.terrain.height(v.position.x, v.position.z) + .03)) <
                              1e-3f,
                      "pothole decal lies flat on the road");
        }
        check(owned > 0 && vertices == owned * 4, "one decal per pothole in the chunk");
    }
    check(!w.puddles.empty(), "city has standing water even in dry weather");
    auto p = w.puddles.front().centre;
    check(w.inRoadPuddle(p) && !w.inRoadPuddle(p + Vec2{2, 0}), "wet contact follows the puddle footprint");
    w.tireContacts.clear();
    w.setWeather(0);
    c.braking = false;
    c.body.v = {0, 7};
    c.body.p = p + Vec2{c.body.halfW * .83, c.wheelbase * .5};
    w.updateTireMarks(c.body.p, .016);
    check(w.tireContacts.front().wetUntil == w.time + 8., "dry-weather puddle wets the tyres");
    w.updateTireMarks(c.body.p, .016);
    check(std::ranges::count(w.sounds, std::string{"splash"}, &SoundEvent::name) == 1,
          "wet wheels produce one splash on puddle entry");
    for (int n = 0; n < 16; ++n)
    {
        w.time += .1;
        c.body.p.z += .65;
        w.updateTireMarks(c.body.p, .1);
    }
    check(w.surfaceMarks.size() >= 24 && w.surfaceMarks.back().kind == MarkKind::wet,
          "a moving car leaves twin wet tracks beyond the puddle");
    w.time += 8.1;
    c.body.p.z += .65;
    w.updateTireMarks(c.body.p, .1);
    check(!w.tireContacts.front().active, "dry tyres stop painting the road after eight seconds");
    w.surfaceMarks.clear();
    for (int i = 0; i < 2000; ++i)
        w.addMark({{}, {}, w.time + i, 10., 0, MarkKind::blood});
    check(w.surfaceMarks.size() == 1800 && w.surfaceMarks.front().born == w.time + 200,
          "mark memory stays bounded");
}
void hudAndMap()
{
    HudDamage dirty;
    dirty.resize(100, 80);
    using Rect = std::array<int, 4>;
    auto regions = [&](bool current)
    {
        std::vector<Rect> result;
        dirty.regions(current, [&](int x, int y, int w, int h) { result.push_back({x, y, w, h}); });
        return result;
    };
    dirty.begin();
    dirty.touch(-4, -5, 22, 8);
    check(regions(true) == std::vector<Rect>{{0, 0, 18, 32}}, "HUD clips negative bounds");
    dirty.begin();
    dirty.touch(40, 71, 77, 45);
    check(regions(false) == std::vector<Rect>{{0, 0, 18, 32}}, "previous HUD cleared before paint");
    check(regions(true) == std::vector<Rect>{{0, 0, 18, 32}, {40, 64, 60, 16}},
          "moved panels refresh both footprints");
    dirty.begin();
    check(regions(true) == std::vector<Rect>{{40, 64, 60, 16}}, "hidden panels erase their last footprint");
    dirty.begin();
    check(regions(true).empty(), "no dirty strips when idle");
    dirty.touch(0, 0, 100, 80);
    check(regions(true) == std::vector<Rect>{{0, 0, 100, 80}}, "full menu merges adjacent upload bands");

    auto upload = [&](size_t slot)
    {
        std::vector<Rect> result;
        dirty.uploadRegions(slot, [&](int x, int y, int w, int h) { result.push_back({x, y, w, h}); });
        return result;
    };
    check(upload(0) == std::vector<Rect>{{0, 0, 100, 80}}, "first texture receives full menu");
    dirty.begin();
    dirty.touch(2, 35, 8, 2);
    check(upload(1) == std::vector<Rect>{{2, 32, 8, 32}}, "new texture receives only visible HUD");
    dirty.begin();
    check(upload(2).empty(), "unused texture stays clear");
    dirty.begin();
    check(upload(0) == std::vector<Rect>{{0, 0, 100, 80}}, "reused texture clears its own old menu");
    dirty.resize(40, 20);
    check(upload(1).empty(), "resizing discards all old texture footprints");

    World world;
    world.generate(42);
    auto offers = [&](const wchar_t *key, const wchar_t *label)
    {
        auto hints = control_hints(world);
        return std::ranges::any_of(hints, [&](const auto &h) { return h.key == key && h.action == label; });
    };
    check(offers(L"A/D", L"Руль"), "driving controls at spawn");
    auto &vehicle = world.cars.at(world.controlledCar);
    check(!offers(L"ЛКМ", L"Огонь вперёд"), "unarmed driver has no forward-fire action");
    world.inventory.give(WeaponId::pistol);
    check(offers(L"ЛКМ", L"Огонь вперёд"), "armed driver can shoot straight ahead");
    world.inventory.clear();
    vehicle.kind = 12;
    check(offers(L"Z / Ctrl", L"Снижение") && !offers(L"Space", L"Ручник"), "helicopter shows descent");
    check(offers(L"L", L"Автопосадка"), "helicopter offers the assisted landing");
    world.helicopterLanding = true;
    check(offers(L"L", L"Отменить автопосадку"), "assisted landing can be cancelled");
    world.helicopterLanding = false;
    vehicle.kind = 9;
    check(offers(L"W/S", L"Вперёд / назад") && !offers(L"A/D", L"Руль"), "tram controls follow the track");
    world.controlledCar = -1;
    check(offers(L"ЛКМ", L"Удар"), "unarmed pedestrian can punch");
    world.playerScooter = 0;
    check(offers(L"F", L"Сойти"), "scooter dismount visible");
    world.playerScooter = -1;
    world.drone.active = true;
    check(offers(L"K", L"Вернуться") && !offers(L"J", L"Заказы"), "drone controls take precedence");
    auto clinic = std::ranges::find(world.sites, ServiceKind::clinic, &ServiceSite::kind);
    check(clinic != world.sites.end(), "seed contains a clinic");
    Minimap map;
    map.rebuild(world);
    constexpr int n = Minimap::size;
    std::array<std::uint32_t, n * n> pixels{};
    map.copy_view(clinic->entrance, pixels);
    check(pixels[n / 2 * n + n / 2] == 0x78cdf9, "centred clinic marker aligns with the player");
    Vec2 delta{12, 22}, dot = Minimap::offset(delta);
    check(dot.x < 0 && dot.z < 0, "north is up and the camera's right is east on the map");
    map.copy_view(clinic->entrance - delta, pixels);
    check(pixels[(n / 2 + int(std::lround(dot.z))) * n + n / 2 + int(std::lround(dot.x))] == 0x78cdf9,
          "moving player keeps cached map and HUD markers aligned");
    for (int side = 0; side < 4; ++side)
        for (int inland = 2; inland <= 24; inland += 2)
        {
            const Vec2 shore = CityLayout::direction(side) * (world.layout.edge() - inland);
            // Atlas texels correspond to rounded world coordinates.
            const double x = std::lround(shore.x * Minimap::scale) / Minimap::scale;
            const double z = std::lround(shore.z * Minimap::scale) / Minimap::scale;
            map.copy_view(shore, pixels);
            const bool submerged = world.terrain.bed_height(x, z) <= CityLayout::waterLevel;
            check(pixels[n / 2 * n + n / 2] == (submerged ? 0x2b5565u : 0xab976cu),
                  "minimap coastline follows the physical waterline on each shore");
        }
    map.copy_view({10000, -10000}, pixels);
    check(std::ranges::all_of(pixels, [](auto p) { return p == 0x2b5565; }),
          "outside atlas draws open water");
}
void streetFixtures()
{
    World world;
    world.generate(20261105);
    using Kind = World::FixtureKind;
    for (auto [kind, mass] : {std::pair{Kind::bin, 22.},
                              {Kind::hydrant, 65.},
                              {Kind::dumpster, 110.},
                              {Kind::bench, 80.},
                              {Kind::phoneBooth, 180.},
                              {Kind::drinkMachine, 140.},
                              {Kind::mailbox, 28.},
                              {Kind::bag, .08},
                              {Kind::bottle, .4},
                              {Kind::packet, .06},
                              {Kind::can, .06},
                              {Kind::garbageBag, 5.}})
    {
        auto found = std::ranges::find(world.fixtures, kind, &World::StreetFixture::kind);
        check(found != world.fixtures.end(), "generated map has each original street fixture");
        const auto &b = world.staticBodies[found->body];
        check(std::abs(1 / b.invMass - mass) < .001 && b.sleeping, "correct sleeping fixture mass");
    }
    size_t index = size_t(std::ranges::find(world.fixtures, Kind::hydrant, &World::StreetFixture::kind) -
                          world.fixtures.begin());
    auto &f = world.fixtures[index];
    auto &b = world.staticBodies[f.body];
    // Drive across the hydrant using the same dynamic OBB and contact solver as the game.
    Body car = world.cars.at(0).body;
    car.angle = 0;
    car.p = f.home - Vec2{0, car.halfH + 1};
    car.v = {0, 13};
    car.height = b.height + .25;
    car.wake();
    world.nearbyStaticBodies = {f.body};
    std::array<Body *, 2> contacts = {&b, &car};
    for (int i = 0; i < 35; ++i)
    {
        world.time += 1. / 60;
        world.physics.step(contacts, 1. / 60);
        world.updateFixtures(f.home, 1. / 60);
    }
    check(f.displaced && (b.p - f.home).length() > 1 && f.sprayUntil > world.time + 15,
          "impact frees hydrant and opens water at its home");
    check(std::ranges::count(world.particles, ParticleKind::water, &Particle::kind) >= 10,
          "the struck hydrant throws no water");
    // Loose bags drift near the player even before the first impact. Distant bags stay dormant.
    auto bag =
        std::ranges::find_if(world.fixtures, [&](const auto &other)
                             { return other.kind == Kind::bag && (other.home - f.home).length() < 135; });
    check(bag != world.fixtures.end(), "bag near hydrant");
    auto &bagBody = world.staticBodies.at(bag->body);
    auto distant =
        std::ranges::find_if(bag + 1, world.fixtures.end(), [&](const auto &other)
                             { return other.kind == Kind::bag && (other.home - bag->home).length() > 160; });
    check(distant != world.fixtures.end(), "remote bag generated");
    world.nearbyStaticBodies = {bag->body, distant->body};
    for (int i = 0; i < 120; ++i)
    {
        std::array<Body *, 1> local{&bagBody};
        world.time += 1. / 60;
        world.physics.step(local, 1. / 60);
        world.updateFixtures(bag->home, 1. / 60);
    }
    check(bag->displaced && (bagBody.p - bag->home).length() > .2 && !distant->displaced,
          "wind affects nearby litter, remote litter stays put");
    // Query across a block boundary after an object is knocked out of its home cell.
    bagBody.p.x += 52;
    std::vector<size_t> nearby;
    size_t bagId = size_t(bag - world.fixtures.begin());
    world.fixturesNear(bagBody.p, .05, nearby);
    check(std::ranges::count(nearby, bagId) == 1, "moved fixture indexed once at actual position");
    world.fixturesNear(bag->home, .05, nearby);
    check(std::ranges::count(nearby, bagId) == 0, "no phantom fixture at old position");
    // Drop a can into deep water. Submerged horizontal motion has water drag on
    // top of the normal contact friction; remaining submerged emits no more entries.
    auto can = std::ranges::find(world.fixtures, Kind::can, &World::StreetFixture::kind);
    auto &canBody = world.staticBodies[can->body];
    Vec2 canal{world.layout.coordinate(world.nodes / 3) + 18., world.layout.coordinate(23) + 18.};
    auto oldPlayer = world.position();
    world.cars[world.controlledCar].body.p = canal;
    canBody.p = canal;
    canBody.v = {4, 0};
    canBody.height = CityLayout::waterLevel + 1.;
    canBody.wake();
    world.nearbyStaticBodies = {can->body};
    world.updateFixtures(canal, .1);
    check(world.ripples.size() == 1 && canBody.v.length() < 4. * std::exp(-2. * .1),
          "a loose object splashes and slows as it enters the canal");
    world.updateFixtures(canal, .1);
    check(world.ripples.size() == 1, "submerged litter never splashes every frame");
    world.cars[world.controlledCar].body.p = oldPlayer;
    world.nearbyStaticBodies = {f.body};
    auto saved = std::filesystem::temp_directory_path() / "promzona-fixture-test.json";
    world.save(saved);
    World restored;
    restored.load(saved);
    std::filesystem::remove(saved);
    restored.fixturesNear(bagBody.p, .05, nearby);
    check(std::ranges::count(nearby, bagId) == 1 && restored.fixtures[bagId].kind == Kind::bag,
          "a moved lightweight prop survives save/load and spatial query");
    auto &other = restored.fixtures.at(index);
    check(other.displaced && (restored.staticBodies.at(other.body).p - b.p).length() < .001 &&
              std::abs(other.sprayUntil - f.sprayUntil) < .001,
          "hydrant position and water deadline survive load");
    for (int i = 0; i < 1100; ++i)
    {
        world.time += 1. / 60;
        world.physics.step(std::span<Body *>(&contacts[0], 1), 1. / 60);
        world.updateFixtures(f.home, 1. / 60);
    }
    check(f.displaced && f.lift == 0 && world.time > f.sprayUntil,
          "jet ends and hydrant settles near player");
    auto before = b.p;
    car.p = b.p - Vec2{0, car.halfH + 1};
    car.v = {0, 11};
    car.height = b.height + .25;
    car.wake();
    for (int i = 0; i < 40; ++i)
    {
        world.physics.step(contacts, 1. / 60);
        world.updateFixtures(f.home, 1. / 60);
    }
    check((b.p - before).length() > .8 && world.time > f.sprayUntil,
          "second impact moves wreckage without reopening the pipe");
    world.time += 2;
    world.updateFixtures(f.home + Vec2{250, 250}, 1. / 60);
    check(!f.displaced && b.sleeping && (b.p - f.home).length() < .001 && f.sprayUntil == 0,
          "unseen fixture is restored once");
}

void shoreEntry()
{
    World w;
    w.generate(20261105);
    w.controlledCar = -1;
    Vec2 shallows{0, w.layout.edge()};
    while (shallows.z > w.layout.edge() - 45 && w.terrain.height(shallows) < CityLayout::waterLevel - .4)
        shallows.z -= .1;
    const double bed = w.terrain.height(shallows);
    check(bed > CityLayout::waterLevel - .5 && bed < CityLayout::waterLevel - .2,
          "found a shallow shelf inside the playable beach");
    w.player.p = shallows;
    w.player.height = bed + .85;
    w.player.v = {};
    w.player.roadLevel = 0;
    w.step({}, 1. / 120);
    check((w.position() - shallows).length() < .2, "wading at the beach does not trigger a clinic respawn");
    Vec2 canal{w.layout.coordinate(w.nodes / 3) + 18., w.layout.coordinate(23) + 18.};
    w.player.p = canal;
    w.player.height = w.terrain.height(canal) + .85;
    w.wanted = 2.;
    w.step({}, 1. / 120);
    check(w.dying() && w.wanted == 0 && !w.ripples.empty() && (w.ripples.back().centre - canal).length() < .1,
          "deep water kills the player and leaves a splash at the point of entry");
    check(w.screenDarkness() < .01, "the picture stays visible while the hero has just died");
    w.time += World::deathSeconds - World::deathFadeSeconds / 2;
    w.step({}, 1. / 120);
    check(w.dying() && w.screenDarkness() > .3 && w.screenDarkness() < .7 && w.screenDrain() == 1,
          "the drained picture fades to black near the end of the death");
    w.time += World::deathFadeSeconds;
    w.step({}, 1. / 120);
    check(!w.dying() && (w.position() - canal).length() > 5 && w.health > .99 && w.screenDarkness() > .9,
          "after the death the player wakes at the clinic in the dark");
    w.time += World::respawnFadeSeconds;
    w.step({}, 1. / 120);
    check(w.screenDarkness() == 0, "the clinic fades in from black");
    w.time += WaterRipple::lifetime + .1;
    w.step({}, 1. / 120);
    check(w.ripples.empty(), "expired water rings are removed");
}

void placement()
{
    for (auto seed : {20261105u, 123u, 878787u})
    {
        World world;
        world.generate(seed);
        auto repeat = planTrees(world);
        check(world.trees.size() > 50, "city contains vegetation");
        check(world.trees.size() == repeat.size(), "deterministic tree count");
        std::map<unsigned, const Body *> ids;
        for (const auto &b : world.staticBodies)
            check(ids.emplace(b.id, &b).second, "unique static body IDs");
        for (const auto &fixture : world.fixtures)
        {
            if (fixture.kind < World::FixtureKind::bench)
                continue;
            Vec2 p = fixture.home;
            check(world.layout.urbanSite(p, .2), "street props stay on dry urban ground");
            check(world.layout.roadDistance(p) > world.layout.roadHalf(), "furniture clears traffic lanes");
            for (const auto &level : world.roadLevels)
                check(!level.reserved(p, .3), "furniture clears bridge and tunnel approaches");
            if ((fixture.body % 31) == 0)
                check(!world.insideBuilding(p, world.terrain.height(p) + .3, .10),
                      "street prop clears walls");
        }
        for (const auto &puddle : world.puddles)
            check(world.layout.urbanSite(puddle.centre, RoadPuddle::radiusX) &&
                      world.layout.roadDistance(puddle.centre) < world.layout.roadHalf() &&
                      !world.levelAt(puddle.centre, RoadPuddle::radiusX),
                  "puddles lie on open streets");
        check(world.potholes.size() > 400, "streets have potholes");
        check(world.potholeFirst.size() == size_t(World::nodes - 1) * (World::nodes - 1) + 1 &&
                  world.potholeFirst.back() == world.potholes.size(),
              "pothole index covers every block");
        for (const auto &hole : world.potholes)
            check(hole.size >= .48 && hole.size < 1.131 && hole.style >= 0 && hole.style < 3 &&
                      world.layout.urbanSite(hole.p, hole.size) &&
                      world.layout.roadDistance(hole.p) < world.layout.roadHalf() &&
                      !world.levelAt(hole.p, hole.size) && !world.openCut(hole.p, hole.size),
                  "potholes lie on open carriageways");
        std::array<int, 3> kinds{};
        for (size_t k = 0; k < world.trees.size(); ++k)
        {
            const auto &t = world.trees[k];
            Vec2 p{t.foot.x, t.foot.z};
            ++kinds[size_t(t.kind)];
            check((t.foot - repeat[k].foot).length() < 1e-5f && t.seed == repeat[k].seed,
                  "stable tree identity");
            check(world.layout.urbanSite(p, t.radius - .001), "tree away from coast and river");
            check(world.layout.roadDistance(p) >= world.layout.roadHalf() + 3.79,
                  "sidewalk and road clearance");
            check(std::abs(t.foot.y - world.terrain.height(p) + .06) < .002, "trunk planted on terrain");
            for (const auto &b : world.buildings)
                check(std::abs(p.x - b.p.x) >= b.half.x + t.radius + 1.39 ||
                          std::abs(p.z - b.p.z) >= b.half.z + t.radius + 1.39,
                      "crown clears buildings");
            for (const auto &s : world.roadLevels)
                check(!s.reserved(p, t.radius + 1.99), "bridge and tunnel approaches clear");
            for (const auto &b : world.layout.furniture)
                check((p - b.p).length() >= 1.799, "tree clears street furniture");
            check(ids.contains(0x40000000u + unsigned(k)), "trunk has fixed collider");
            const auto &body = *ids.at(0x40000000u + unsigned(k));
            check(body.circle && body.invMass == 0 && (body.p - p).length() < .001, "collider matches trunk");
        }
        for (int count : kinds)
            check(count > 0, "all tree species represented");
        std::println("seed {}: {} trees", seed, world.trees.size());
    }
}
// Port of the particles of the managed game and of what throws them.
void effects()
{
    World w;
    w.generate(20261105u);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    w.particles.clear();
    w.sounds.clear();
    auto count = [&](ParticleKind kind) { return std::ranges::count(w.particles, kind, &Particle::kind); };
    auto heard = [&](const char *name)
    { return std::ranges::count(w.sounds, std::string{name}, &SoundEvent::name); };
    // The sequence of the effects is the xorshift of the managed game, apart from the
    // one the city is driven by.
    {
        std::uint32_t seed = 4191;
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        const auto state = w.random.state;
        check(w.effectSeed == 4191 && std::abs(w.effectNoise() - (seed + .5) / 4294967296.) < 1e-15 &&
                  w.random.state == state,
              "effect noise is not the managed sequence");
    }
    const Vec2 street = w.cars[w.controlledCar].body.p;
    const double ground = w.surface(street, 0);
    using enum ParticleKind;
    struct Range
    {
        ParticleKind kind;
        float lifeLow, lifeHigh, sizeLow, sizeHigh;
    };
    const auto state = w.random.state;
    for (const Range &r : {Range{spark, .3f, .9f, .035f, .095f}, Range{smoke, 2, 4, .25f, .65f},
                           Range{debris, .5f, 1.5f, .025f, .025f}, Range{water, 1.5f, 1.5f, .035f, .095f},
                           Range{fire, .5f, 1, .25f, .65f}, Range{sand, .3f, .9f, .035f, .095f}})
    {
        w.particles.clear();
        w.emit(street, .65, 0, r.kind, 200, 4);
        check(w.particles.size() == 200, "a burst is short of particles");
        float lifeLow = 1e9f, lifeHigh = 0, sizeLow = 1e9f, sizeHigh = 0, fastest = 0, slowestRise = 1e9f,
              fastestRise = 0;
        for (const auto &p : w.particles)
        {
            check(p.kind == r.kind && p.span == p.life && std::abs(p.p.y - float(ground + .65)) < 1e-4f &&
                      std::abs(p.floor - float(ground + .03)) < 1e-4f,
                  "a particle does not start where it was thrown");
            lifeLow = std::min(lifeLow, p.life);
            lifeHigh = std::max(lifeHigh, p.life);
            sizeLow = std::min(sizeLow, p.size);
            sizeHigh = std::max(sizeHigh, p.size);
            fastest = std::max(fastest, std::hypot(p.v.x, p.v.z));
            slowestRise = std::min(slowestRise, p.v.y);
            fastestRise = std::max(fastestRise, p.v.y);
        }
        check(lifeLow >= r.lifeLow - 1e-4f && lifeHigh <= r.lifeHigh + 1e-4f &&
                  sizeLow >= r.sizeLow - 1e-4f && sizeHigh <= r.sizeHigh + 1e-4f,
              "life or size of a kind is not that of the managed game");
        check(r.lifeLow == r.lifeHigh || (lifeHigh - lifeLow > (r.lifeHigh - r.lifeLow) * .8f),
              "lives of a burst do not vary");
        check(fastest <= 4.0001f && fastest > 3.5f && slowestRise >= 1.2f - 1e-4f && fastestRise <= 5.2001f,
              "a burst is not a cone of the asked speed");
    }
    check(w.random.state == state, "effects draw from the sequence of the city");
    // Smoke rises, spreads and drifts; the rest falls and bounces off the ground.
    {
        w.particles.clear();
        w.emit(street, 1, 0, smoke, 1, 0);
        w.emit(street, 1, 0, spark, 1, 0);
        w.particles[0].life = w.particles[0].span = w.particles[1].life = w.particles[1].span = 3;
        const float size = w.particles[0].size;
        for (int i = 0; i < 60; ++i)
            w.stepParticles(1. / 60);
        const auto &puff = w.particles[0];
        const auto &fleck = w.particles[1];
        check(puff.kind == smoke && puff.v.y > .45f && std::abs(puff.size - size - .22f) < .01f &&
                  puff.v.x > .39f,
              "smoke does not rise and spread");
        check(fleck.kind == spark && std::abs(fleck.p.y - fleck.floor) < .02f,
              "a spark does not come to rest");
        for (int i = 0; i < 125; ++i)
            w.stepParticles(1. / 60);
        check(w.particles.empty(), "particles outlive their span");
        // Tracers stay what they were: a point that falls at six metres a second squared.
        w.particles.push_back({{0, 10, 0}, {}, {1, 1, 1}, 1.f, .04f});
        w.stepParticles(.5);
        check(w.particles.size() == 1 && std::abs(w.particles[0].v.y + 3) < 1e-5f,
              "a tracer changed its fall");
        w.particles.clear();
    }
    // A collision: sparks and scrap, dark strips on the car and a patch on the wall beside.
    auto &c = w.cars[w.controlledCar];
    {
        const auto &b = w.buildings.front();
        c.body.p = b.p + Vec2{b.half.x + 1.5, 0};
        c.body.height = w.terrain.height(c.body.p) + .8;
        c.impactCooldown = 0;
        c.health = 1;
        w.vehicleImpact(c, 4, {4, 0});
        check(count(spark) == 18 && count(debris) == 8 && count(fire) == 0 && heard("impact") == 1,
              "a collision throws the wrong sparks");
        check(w.scuffs.size() == 2 && !w.scuffs[0].wall && w.scuffs[0].car == c.id && w.scuffs[1].wall &&
                  w.scuffs[1].facesX && std::abs(w.scuffs[1].at.x - float(b.p.x + b.half.x + .022)) < 1e-3f &&
                  std::abs(w.scuffs[1].at.z - float(b.p.z)) < 1e-3f,
              "a collision leaves no marks on the car and the wall");
        // Out in the street there is no wall to mark; a car hit again in the same moment is not marked twice.
        w.vehicleImpact(c, 4, {4, 0});
        check(w.scuffs.size() == 2 && heard("impact") == 1, "the impact cooldown does not hold");
        w.time += Scuff::onWall + .01;
        w.stepParticles(.01);
        check(w.scuffs.size() == 1 && !w.scuffs[0].wall, "the patch on the wall does not fade first");
        w.time += Scuff::onCar;
        w.stepParticles(.01);
        check(w.scuffs.empty(), "scuffs never fade");
        // The blow that all but finishes the car sets off one burst of fire.
        w.particles.clear();
        c.impactCooldown = 0;
        // Half of the blow counts: the durability budget of damageCar.
        c.health = .05 + 7 * .025 * .5 - .001;
        c.fireTime = 0;
        w.vehicleImpact(c, 7, {7, 0});
        check(c.health > 0 && c.health <= .05 && count(fire) == 36 && count(smoke) == 24 &&
                  count(debris) == 32,
              "no burst of fire from the blow that finishes a car");
    }
    // A burning car is heard every three seconds and smokes; a damaged one smokes less.
    {
        w.particles.clear();
        w.sounds.clear();
        c.body.p = street;
        c.body.height = ground + .8;
        c.body.v = {};
        c.health = .2;
        c.fireTime = .001;
        w.controlledCar = -1;
        w.player.p = street + Vec2{6, 0};
        w.player.height = w.terrain.height(w.player.p) + .85;
        for (int i = 0; i < 60 * 7; ++i)
            w.combatStep(1. / 60);
        check(heard("fire") == 2, "a burning car is not heard every three seconds");
        check(count(fire) > 20 && count(smoke) > 150, "a burning car does not burn to the eye");
        c.fireTime = 0;
        c.health = .5;
        w.particles.clear();
        for (int i = 0; i < 60 * 4; ++i)
            w.combatStep(1. / 60);
        check(count(fire) == 0 && count(smoke) > 3 && count(smoke) < 30, "a damaged engine does not smoke");
        c.health = 1;
        w.particles.clear();
        for (int i = 0; i < 60; ++i)
            w.combatStep(1. / 60);
        check(w.particles.empty(), "a sound car smokes");
    }
    // A car on its wheels that comes down hops with 22 % of the speed, silently, and settles.
    {
        w.sounds.clear();
        w.particles.clear();
        c.fireTime = 0;
        c.health = 1;
        c.body.p = street;
        c.body.v = {};
        c.body.height = ground + .8 + 2;
        c.lift = 2;
        c.verticalVelocity = 0;
        double hop = 0;
        int landings = 0;
        for (int i = 0; i < 240; ++i)
        {
            const double before = c.verticalVelocity;
            w.supportCar(c, c.body.p, 1. / 60);
            if (before < 0 && c.verticalVelocity >= 0)
            {
                if (!landings)
                    hop = c.verticalVelocity;
                ++landings;
            }
        }
        const double landing = std::sqrt(2 * 9.81 * 2);
        check(std::abs(hop - landing * .22) < .05 && landings >= 2 && landings <= 4 && c.lift == 0 &&
                  c.verticalVelocity == 0 && w.sounds.empty() && w.particles.empty(),
              "a car that comes down does not hop and settle");
    }
    // A wreck that is struck clangs and hops; dropped, it thuds, raises smoke and hops once.
    {
        w.sounds.clear();
        w.scuffs.clear();
        c.destroy();
        c.impactCooldown = 0;
        c.verticalVelocity = 0;
        w.vehicleImpact(c, 12, {12, 0});
        check(heard("impact") == 1 && std::abs(w.sounds.back().gain - .8) < 1e-9 && count(debris) == 6 &&
                  std::abs(c.verticalVelocity - .6) < 1e-9 && w.scuffs.empty(),
              "a struck wreck does not clang");
        w.sounds.clear();
        w.particles.clear();
        c.body.v = {};
        c.body.height = ground + .8 + 2;
        c.lift = 2;
        c.verticalVelocity = 0;
        double bounce = 0;
        for (int i = 0; i < 120 && heard("impact") == 0; ++i)
        {
            w.supportCar(c, c.body.p, 1. / 60);
            bounce = c.verticalVelocity;
        }
        const double landing = std::sqrt(2 * 9.81 * 2);
        check(heard("impact") == 1 && std::abs(w.sounds.back().gain - landing / 12) < .03 &&
                  count(smoke) == 8 && std::abs(bounce - landing * .12) < .05 && !c.wreckSupported,
              "a dropped wreck does not thud and hop");
        for (int i = 0; i < 120; ++i)
            w.supportCar(c, c.body.p, 1. / 60);
        check(c.lift == 0 && c.verticalVelocity == 0 && c.wreckSupported && heard("impact") == 1,
              "a wreck does not come to rest");
    }
    // A bullet or a blast that reaches the player on foot is heard; fire is not.
    {
        w.sounds.clear();
        w.health = 1;
        w.wound_player(.1);
        check(heard("impact") == 1 && std::abs(w.sounds.back().gain - .5) < 1e-9 &&
                  std::abs(w.health - .9) < 1e-9,
              "a hit on the player is silent");
        w.hurt_player(.1);
        w.godMode = true;
        w.wound_player(.1);
        w.godMode = false;
        check(heard("impact") == 1 && std::abs(w.health - .8) < 1e-9,
              "burns or an immortal player are heard");
    }
}
} // namespace
int main()
{
    try
    {
        effects();
        headlightSelection();
        streetLighting();
        shadows();
        skyAndProbe();
        weather();
        hudAndMap();
        precipitationAndTracks();
        shoreEntry();
        streetFixtures();
        geometry();
        placement();
        std::println("{} environment checks passed", checks);
        return 0;
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "{}", e.what());
        return 1;
    }
}
