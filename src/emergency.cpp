#include "promzona/world.hpp"
#include "promzona/fleet.hpp"
#include <queue>

namespace pz
{
const World::Rescue *World::ambulanceCall(int id) const
{
    for (const auto &r : rescues)
        if (r.car >= 0 && r.car == id)
            return &r;
    return nullptr;
}
const World::Rescue *World::paramedicCall(int actor) const
{
    for (const auto &r : rescues)
        if (r.medic >= 0 && r.medic == actor)
            return &r;
    return nullptr;
}

void World::routeAmbulance(Rescue &r)
{
    // The same road graph as ordinary traffic includes the signed grade-separated links.
    r.distances.assign(nodes * nodes, -1);
    std::queue<int> open;
    open.push(r.destination);
    r.distances[r.destination] = 0;
    while (!open.empty())
    {
        int n = open.front();
        open.pop();
        for (int other : neighbours(n))
            if (r.distances[other] < 0)
            {
                r.distances[other] = r.distances[n] + 1;
                open.push(other);
            }
    }
}
int World::ambulanceTurn(const Rescue &r, int node, int previous)
{
    if (r.distances.empty() || r.distances[node] <= 0)
        return chooseNext(node, previous);
    for (int n : neighbours(node))
        if (r.distances[n] == r.distances[node] - 1)
            return n;
    return chooseNext(node, previous);
}

void World::medicIntent(int actor, Vec2 &direction, double &speed)
{
    const auto *r = paramedicCall(actor);
    if (!r || (r->stage != RescueStage::care && r->stage != RescueStage::returning))
        return;
    auto at = people[actor].body.p;
    auto goal = r->stage == RescueStage::care ? people[r->patient].body.p : passengerDoor(cars[r->car], at);
    auto delta = goal - at;
    direction = delta.unit();
    speed = std::min(2.8, std::max(0., delta.length() - .8) * 2.);
}

void World::updateRescues()
{
    nextRescueScan = time + .25;
    auto seat = [&](Person &p, int car)
    {
        p.riding = car;
        p.seat = 0;
        p.alight = -1;
        p.body.v = {};
        seatPassenger(p);
    };
    for (auto &r : rescues)
    {
        if (r.car < 0)
            continue;
        auto &c = cars[r.car];
        auto &medic = people[r.medic];
        if (c.destroyed || c.falling || c.fireTime > 0 || controlledCar == r.car || medic.dead ||
            (r.stage != RescueStage::standby && time >= r.deadline))
        {
            if (medic.riding == r.car)
            {
                if (c.destroyed || c.falling)
                    seatPassenger(medic);
                else
                    resumeWalk(medic, vehicleExit(c).value_or(passengerDoor(c, medic.body.p)),
                               c.body.roadLevel);
            }
            if (r.patient >= 0 && people[r.patient].dead)
                people[r.patient].rescueAfter = time + 25;
            r = {};
            continue;
        }
        if (r.stage == RescueStage::standby)
            continue;
        auto &patient = people[r.patient];
        if (r.stage == RescueStage::driving)
        {
            if (!patient.dead)
            {
                r.stage = RescueStage::standby;
                r.patient = -1;
                r.braking = false;
                continue;
            }
            const auto door = passengerDoor(c, patient.body.p);
            const auto distance = (patient.body.p - door).length();
            r.braking = distance < 19 && c.body.roadLevel == patient.body.roadLevel &&
                        blastVisible(door, surface(door, c.body.roadLevel) + .85, patient.body.p,
                                     patient.body.height);
            if (r.braking && c.body.v.length() < 1.1 &&
                !scenerySolid(door, surface(door, c.body.roadLevel) + .85, .3))
            {
                resumeWalk(medic, door, c.body.roadLevel);
                r.stage = RescueStage::care;
                r.deadline = time + 50;
            }
        }
        else if (r.stage == RescueStage::care)
        {
            if (patient.dead && medic.body.roadLevel == patient.body.roadLevel &&
                (medic.body.p - patient.body.p).length2() <= 1.65 * 1.65 &&
                blastVisible(medic.body.p, medic.body.height, patient.body.p, patient.body.height))
            {
                if (!r.treatment)
                    r.treatment = time + 4;
                if (time >= r.treatment)
                {
                    patient.dead = false;
                    patient.health = .7;
                    patient.fear = patient.watchUntil = patient.fleeUntil = patient.rescueAfter = 0;
                    patient.cover = {};
                    resumeWalk(patient, patient.body.p, patient.body.roadLevel);
                }
            }
            else
                r.treatment = 0;
            if (!patient.dead)
            {
                r.stage = RescueStage::returning;
                r.deadline = time + 36;
            }
        }
        else if ((medic.body.p - passengerDoor(c, medic.body.p)).length2() < 1.6 * 1.6 &&
                 medic.body.roadLevel == c.body.roadLevel && c.body.v.length() < 1.5)
        {
            seat(medic, r.car);
            r.stage = RescueStage::standby;
            r.patient = -1;
            r.braking = false;
        }
    }

    if (time < nextRescueDispatch || people.empty())
        return;
    nextRescueDispatch = time + 2;
    auto free = std::ranges::find(rescues, RescueStage::standby, &Rescue::stage);
    if (free == rescues.end())
        return;
    for (size_t i = 0; i < people.size(); ++i)
    {
        int id = int((casualtyCursor + i) % people.size());
        auto &p = people[id];
        if (!p.dead || time < p.rescueAfter || paramedicCall(id) ||
            std::ranges::any_of(rescues, [&](const Rescue &r) { return r.patient == id; }))
            continue;
        casualtyCursor = (id + 1) % people.size();
        int x = clamp(int(std::lround(p.body.p.x / spacing)) + nodes / 2, 0, nodes - 1);
        int z = clamp(int(std::lround(p.body.p.z / spacing)) + nodes / 2, 0, nodes - 1);
        int goal = z * nodes + x;
        if (auto *level = levelById(p.body.roadLevel))
            goal = (p.body.p - nodePoint(level->leftNode)).length2() <
                           (p.body.p - nodePoint(level->rightNode)).length2()
                       ? level->leftNode
                       : level->rightNode;
        Rescue route;
        route.destination = goal;
        routeAmbulance(route);
        int car = -1;
        double best = 1e20;
        for (auto &c : cars)
        {
            if (c.id == 0 || int(c.id) == controlledCar || c.destroyed || c.falling || c.fireTime > 0 ||
                c.line >= 0 || c.responding || c.summoned || c.personal || route.distances[c.routeTo] < 0)
                continue;
            const auto *crew = ambulanceCall(c.id);
            if (crew && crew->stage != RescueStage::standby)
                continue;
            // Fill old savegames (which had no ambulances) from traffic outside the player's view.
            bool replace = c.kind != 4;
            if (replace &&
                (c.kind > 2 || (position() - c.body.p).length2() < 400 * 400 ||
                 (p.body.p - c.body.p).length2() < 50 * 50 ||
                 (job.kind != JobKind::none && (job.actor == int(c.id) || job.ride.car == int(c.id)))))
                continue;
            if (!crew && std::ranges::none_of(rescues, [](const Rescue &r) { return r.car < 0; }))
                continue;
            double score = spacing * route.distances[c.routeTo] + (nodePoint(c.routeTo) - c.body.p).length() +
                           (replace ? 650. : 0.);
            if (!replace && c.body.roadLevel == p.body.roadLevel && (c.body.p - p.body.p).length2() < 19 * 19)
            {
                // A crew across the street can stop immediately, even when its next road node is further
                // away.
                const auto door = passengerDoor(c, p.body.p);
                if (blastVisible(door, surface(door, c.body.roadLevel) + .85, p.body.p, p.body.height))
                    score = (door - p.body.p).length();
            }
            if (score < best)
            {
                best = score;
                car = int(c.id);
            }
        }
        if (car < 0)
        {
            p.rescueAfter = time + 12;
            return;
        }
        auto &c = cars[car];
        Rescue *slot = nullptr;
        for (auto &r : rescues)
            if (r.car == car)
                slot = &r;
        if (!slot)
        {
            for (auto &r : rescues)
                if (r.car < 0)
                {
                    slot = &r;
                    break;
                }
            if (!slot)
                return;
            int medic = -1;
            double distance = 1e20;
            for (size_t j = 0; j < people.size(); ++j)
            {
                const auto &candidate = people[j];
                if (candidate.dead || candidate.riding >= 0 || candidate.inCompany || candidate.courier ||
                    candidate.ownedScooter >= 0 || candidate.sidearm != WeaponId::none ||
                    candidate.burnUntil > time || int(j) == patrol.officer || paramedicCall(int(j)) ||
                    (job.kind != JobKind::none && job.actor == int(j)) ||
                    (candidate.body.p - position()).length2() < 140 * 140)
                    continue;
                double d = (candidate.body.p - c.body.p).length2();
                if (d < distance)
                {
                    distance = d;
                    medic = int(j);
                }
            }
            if (medic < 0)
                return;
            if (c.kind != 4)
                fitVehicle(c, ambulanceModel);
            slot->car = car;
            slot->medic = medic;
            seat(people[medic], car);
        }
        slot->stage = RescueStage::driving;
        slot->patient = id;
        slot->destination = goal;
        slot->distances = std::move(route.distances);
        slot->treatment = 0;
        slot->deadline = time + std::max(160., 60. + best / 3.);
        return;
    }
}
} // namespace pz
