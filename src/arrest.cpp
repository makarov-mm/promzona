#include "promzona/world.hpp"

namespace pz
{
void World::updatePatrol()
{
    patrolScanAt = time + .25;
    const int level = wantedLevel();
    const bool arrestable = level > 0 && level < 4 && health > 0;
    const auto &suspect = controlledCar < 0 ? player : cars[controlledCar].body;
    auto closeEnough = [&](const Car &c) {
        if (!arrestable || !c.responding || c.destroyed || c.falling || c.fireTime > 0 ||
            !c.grounded() || c.body.roadLevel != suspect.roadLevel ||
            std::abs(c.body.height - suspect.height) > 1.8) return false;
        double reach = 19;
        if (controlledCar >= 0)
        {
            const auto &target = cars[controlledCar];
            if (!target.grounded() || target.recovering || suspect.v.length() >= 1) return false;
            reach = target.body.halfH + c.body.halfH + 4.5;
        }
        return (c.body.p - suspect.p).length2() < reach * reach &&
               blastVisible(c.body.p, c.body.height, suspect.p, suspect.height);
    };

    if (patrol.car >= 0)
    {
        auto &c = cars[patrol.car];
        auto *cop = patrol.officer >= 0 ? &people[patrol.officer] : nullptr;
        bool invalid = c.destroyed || c.falling || c.fireTime > 0 || controlledCar == patrol.car ||
                       (cop && cop->dead) || (patrol.stage == PatrolStage::seated && !c.responding && arrestable);
        bool distant = (c.body.p - position()).length2() > 180 * 180;
        if (invalid || distant)
        {
            if (cop && cop->riding >= 0)
            {
                if (c.destroyed || c.falling) seatPassenger(*cop);
                else if (auto door = vehicleExit(c)) resumeWalk(*cop, *door, c.body.roadLevel);
                else return;
            }
            patrol = {};
        }
        else if (patrol.stage == PatrolStage::returning)
        {
            if ((cop->body.p - passengerDoor(c, cop->body.p)).length() < 1.5 &&
                cop->body.roadLevel == c.body.roadLevel && c.body.v.length() < 1.2)
            {
                cop->riding = patrol.car;
                cop->seat = 0;
                cop->alight = -1;
                seatPassenger(*cop);
                patrol.stage = PatrolStage::seated;
            }
            else if (time > patrol.deadline) patrol = {};
            return;
        }
        else if (patrol.stage == PatrolStage::approaching)
        {
            if (!arrestable || !c.responding || time > patrol.deadline ||
                (suspect.p - c.body.p).length2() > 32 * 32 || suspect.roadLevel != cop->body.roadLevel)
            {
                patrol.stage = PatrolStage::returning;
                patrol.hold = 0;
                patrol.deadline = time + 15;
            }
            return;
        }
        else if (patrol.stage == PatrolStage::seated)
        {
            if (!closeEnough(c)) return;
            patrol.stage = PatrolStage::stopping;
            patrol.deadline = time + 12;
        }
        else if (!closeEnough(c) || time > patrol.deadline)
        {
            if (cop) patrol.stage = PatrolStage::seated;
            else patrol = {};
            return;
        }
    }

    if (patrol.car < 0)
    {
        if (!arrestable) return;
        double best = 19 * 19;
        for (size_t i = trafficPopulation; i < cars.size(); ++i)
        {
            const auto &c = cars[i];
            double distance = (c.body.p - suspect.p).length2();
            if ((c.kind == 3 || c.kind == 6 || c.kind == 7) && distance < best && closeEnough(c))
            {
                patrol.car = int(i);
                best = distance;
            }
        }
        if (patrol.car < 0) return;
        patrol.deadline = time + 12;
    }

    auto &unit = cars[patrol.car];
    if (unit.body.v.length() >= 1.2) return;
    const auto exit = vehicleExit(unit, suspect.p);
    if (!exit) return;
    if (patrol.officer < 0)
    {
        for (int i = 0; i < int(people.size()); ++i)
        {
            const auto &candidate = people[i];
            if ((candidate.body.p - suspect.p).length2() > 130 * 130 &&
                candidate.sidearm == WeaponId::none && !candidate.dead && candidate.riding < 0 &&
                candidate.ownedScooter < 0 && !candidate.courier && !candidate.inCompany &&
                candidate.burnUntil <= time && i != conversation.actor && !paramedicCall(i) &&
                (job.kind == JobKind::none || i != job.actor))
            {
                patrol.officer = i;
                break;
            }
        }
        if (patrol.officer < 0) return;
    }
    auto &cop = people[patrol.officer];
    cop.fear = cop.watchUntil = cop.fleeUntil = 0;
    resumeWalk(cop, *exit, unit.body.roadLevel);
    patrol.stage = PatrolStage::approaching;
    patrol.deadline = time + 28;
}

void World::officerIntent(Vec2 &direction, double &speed) const
{
    auto at = people[patrol.officer].body.p;
    Vec2 target = position();
    if (patrol.stage == PatrolStage::returning)
        target = passengerDoor(cars[patrol.car], at);
    else if (controlledCar >= 0)
        target = passengerDoor(cars[controlledCar], at);
    auto delta = target - at;
    direction = delta.unit();
    speed = std::min(3.8, std::max(0., delta.length() - .72) * 3.);
}

void World::arrestStep(double dt)
{
    if (patrol.stage != PatrolStage::approaching) return;
    int level = wantedLevel();
    const auto &cop = people[patrol.officer];
    const auto &unit = cars[patrol.car];
    if (!level || level >= 4 || cop.dead || cop.burnUntil > time ||
        unit.destroyed || unit.fireTime > 0 || !unit.responding || controlledCar == patrol.car)
    {
        patrol.hold = 0;
        return;
    }
    bool walking = controlledCar < 0;
    const auto &body = walking ? player : cars[controlledCar].body;
    bool still = walking ? !aiming && playerScooter < 0 && body.v.length() < 1.2 &&
                           std::abs(ground() - surface(body.p, body.roadLevel)) < .2
                         : body.v.length() < 1 && cars[controlledCar].grounded() && !cars[controlledCar].recovering;
    auto target = walking ? body.p : passengerDoor(cars[controlledCar], cop.body.p);
    bool contact = still && cop.body.roadLevel == body.roadLevel && std::abs(cop.body.height - body.height) < 1.6 &&
                   (target - cop.body.p).length2() < 1.5 * 1.5;
    patrol.hold = contact ? patrol.hold + dt : std::max(0., patrol.hold - 2 * dt);
    if (patrol.hold < (walking ? 1.5 : 2.5)) return;
    // Check the actual line between the two actors before booking through a wall or parked between floors.
    if (!blastVisible(cop.body.p, cop.body.height, target, body.height)) { patrol.hold = 0; return; }
    auto door = clearEntrance(stationEntrance);
    if (!door) { patrol.hold = 0; return; }
    lastFine = std::min(money, 40 + level * 60);
    money -= lastFine;
    ++arrests;
    if (job.kind != JobKind::none) finishJob(false);
    if (drone.active) stopDrone(L"Связь с дроном прервана", 20);
    releaseScooter(playerScooter, {});
    controlledCar = -1;
    playerTransitSeat = -1;
    transitExitRequested = false;
    player.p = *door;
    player.angle = -pi; // Look towards the station, with room for the camera behind the player.
    player.v = {};
    player.w = player.verticalSpeed = 0;
    player.roadLevel = 0;
    player.height = terrain.height(*door) + .85;
    player.wake();
    inventory.clear();
    rockets.clear();
    physics.clear();
    playerBurnUntil = energySeconds = clinicCredit = 0;
    conversation = {};
    openMarket = -1;
    clock.setHour(clock.hour + 3 + level);
    wanted = wantedCooldown = dispatchAt = 0;
    for (auto &c : cars) c.responding = c.seesSuspect = c.firePlayerCaused = false;
    for (auto &p : people) { p.alertUntil = p.aimSeconds = 0; p.burnPlayerCaused = false; }
    patrol.stage = PatrolStage::returning;
    patrol.deadline = time + 15;
    patrol.hold = 0;
    sounds.push_back({"respawn", *door, .6, true});
    notify(L"Задержание · штраф $" + std::to_wstring(lastFine) + L" · прошло " +
           std::to_wstring(3 + level) + (level == 1 ? L" часа" : L" часов"));
}
} // namespace pz
