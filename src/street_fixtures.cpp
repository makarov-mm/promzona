#include "promzona/world.hpp"

namespace pz
{
void World::registerFixture(Body &body)
{
    size_t ordinal = fixtures.size() + 1;
    auto kind = ordinal % 9 == 0 ? FixtureKind::dumpster :
                ordinal % 3 == 0 ? FixtureKind::hydrant : FixtureKind::bin;
    bool dumpster = kind == FixtureKind::dumpster, hydrant = kind == FixtureKind::hydrant;
    body.circle = !dumpster;
    body.radius = hydrant ? .25 : .30;
    body.halfW = dumpster ? .75 : body.radius;
    body.halfH = dumpster ? .55 : body.radius;
    body.verticalExtent = dumpster ? 1.15 : hydrant ? .82 : .85;
    body.height = terrain.height(body.p) + .08 + body.verticalExtent * .5;
    body.friction = .58;
    body.restitution = .18;
    body.mass(dumpster ? 110 : hydrant ? 65 : 22);
    body.sleeping = true;
    fixtureByBody.resize(staticBodies.size() + 1, -1);
    fixtureByBody.back() = int(fixtures.size());
    fixtures.push_back({staticBodies.size(), body.p, kind, body.verticalExtent});
}

void World::supportFixture(StreetFixture &f)
{
    auto &b = staticBodies[f.body];
    double foot = b.height - b.verticalExtent * .5;
    b.roadLevel = selectLevel(b.p, b.roadLevel, foot);
    b.verticalExtent = std::abs(std::cos(f.tilt)) * f.standingHeight +
                       std::abs(std::sin(f.tilt)) * 2 * b.halfW;
    b.height = surface(b.p, b.roadLevel) + .08 + f.lift + b.verticalExtent * .5;
}

void World::updateFixtures(Vec2 focus, double dt)
{
    for (auto index : nearbyStaticBodies)
    {
        if (index >= staticBodies.size() || index >= fixtureByBody.size() || fixtureByBody[index] < 0) continue;
        auto &b = staticBodies[index];
        auto &f = fixtures[fixtureByBody[index]];
        if ((b.p - focus).length2() > 130 * 130 || (b.sleeping && f.lift == 0 && f.spin == 0)) continue;
        double speed = b.v.length();
        if (!f.displaced && (b.p - f.home).length2() > .035 * .035)
        {
            f.displaced = true;
            b.verticalSpeed = std::min(4., speed * .19);
            f.spin = std::min(7., speed * .4) * (b.v.x + b.v.z < 0 ? -1 : 1);
            if (f.kind == FixtureKind::hydrant) f.sprayUntil = time + 16;
        }
        if (!f.displaced) continue;
        b.verticalSpeed -= 9.81 * dt;
        f.lift += b.verticalSpeed * dt;
        f.tilt = wrap(f.tilt + f.spin * dt);
        if (f.lift <= 0)
        {
            f.lift = 0;
            b.verticalSpeed = b.verticalSpeed < -1 ? -b.verticalSpeed * .28 : 0;
            double drag = std::exp(-2.2 * dt);
            b.v = b.v * drag;
            b.w *= drag;
            f.spin *= drag;
            if (std::abs(f.spin) < .05)
            {
                f.spin = 0;
                // Settle on the nearest face rather than balance on an edge.
                double rest = std::round(f.tilt / (pi / 2)) * (pi / 2);
                f.tilt += (rest - f.tilt) * std::min(1., 9 * dt);
                if (std::abs(rest-f.tilt) < .015) f.tilt = rest;
                else b.wake();
            }
        }
        supportFixture(f);
    }
    if (time < nextFixtureRestore) return;
    nextFixtureRestore = time + 1;
    for (auto &f : fixtures)
    {
        if (!f.displaced || f.body >= staticBodies.size() || (f.home - focus).length2() < 145 * 145) continue;
        auto &b = staticBodies[f.body];
        if ((b.p - focus).length2() < 145 * 145) continue;
        b.p = f.home;
        b.v = {};
        b.angle = b.w = b.verticalSpeed = 0;
        b.sleepTimer = 0;
        b.sleeping = true;
        b.roadLevel = 0;
        f.lift = f.tilt = f.spin = f.sprayUntil = 0;
        f.displaced = false;
        supportFixture(f);
        staticQueryCount = size_t(-1);
    }
}
} // namespace pz
