#include "promzona/world.hpp"
#include <limits>

namespace pz
{
Vec2 World::passengerDoor(const Car &c, Vec2 from) const
{
    auto side = rotate(Vec2{c.body.halfW + .65, 0}, c.body.angle);
    return c.body.p + side * (dot(from - c.body.p, side) < 0 ? -1. : 1.);
}

bool World::canTakePassengerJob(int actor) const
{
    const auto &p = people.at(actor);
    return !paramedicCall(actor) && !p.dead && p.ownedScooter < 0 && !p.courier && !p.inCompany && p.riding < 0 &&
           p.health > 0 && !p.body.roadLevel && p.burnUntil <= time && p.fear <= 0 &&
           layout.urbanSite(p.body.p) && !levelAt(p.body.p, 2) && !insideBuilding(p.body.p, p.body.height, .5);
}

bool World::startPassengerJob(JobKind kind, int requestedActor)
{
    int actor = -1;
    double best = std::numeric_limits<double>::max();
    for (size_t i = 0; i < people.size(); ++i)
    {
        const auto &p = people[i];
        if ((requestedActor >= 0 && int(i) != requestedActor) || !canTakePassengerJob(int(i)))
            continue;
        double distance = (p.body.p - position()).length();
        // Prefer an existing casualty for a medical call, then a civilian who has fallen ill.
        double score = distance + (kind == JobKind::patient && p.health > .7 ? 10000. : 0.);
        if (score < best) { best = score; actor = int(i); }
    }
    if (actor < 0) { notify(L"Свободных пассажиров поблизости нет"); return false; }

    auto &p = people[actor];
    int destination = -1, line = -1;
    if (kind == JobKind::patient)
        destination = nearestSite(ServiceKind::clinic, p.body.p);
    else
    {
        double desired = 180. + double((size_t(actor) + jobsCompleted * 41u) % 260);
        best = std::numeric_limits<double>::max();
        for (size_t i = 0; i < lines.size(); ++i)
            for (size_t j = 0; j < lines[i].stops.size(); ++j)
            {
                double d = (lines[i].curb(int(j)) - p.body.p).length();
                double score = std::abs(d - desired);
                if (d > 40 && score < best) { best = score; destination = int(j); line = int(i); }
            }
    }
    if (destination < 0) { notify(L"Нет доступного пункта назначения"); return false; }
    auto arrival = line < 0 ? sites[destination].entrance : lines[line].curb(destination);
    double length = (arrival - p.body.p).length();
    if (kind == JobKind::patient) p.health = std::min(p.health, .65);
    job = {kind, actor, destination, (kind == JobKind::patient ? 90 : 30) + 5 * int(length / 45),
           false, time + 180 + (p.body.p - position()).length() / 4};
    job.ride.line = line;
    notify(kind == JobKind::patient ? L"Вызов из поликлиники. Заберите пациента" : L"Пассажир ждёт на отмеченном тротуаре");
    return true;
}

void World::interactPassengerJob()
{
    if (job.acquired || controlledCar < 0 || job.ride.car >= 0) return;
    auto &c = cars[controlledCar];
    auto &p = people[job.actor];
    if (c.line >= 0 || c.responding || c.kind > 2 || c.fireTime || c.falling || c.destroyed || !c.grounded())
    {
        notify(L"Нужна исправная гражданская машина");
        return;
    }
    if (c.body.v.length() >= 1.5 || (c.body.p - p.body.p).length() > 10 ||
        p.body.roadLevel != c.body.roadLevel || std::abs(p.body.height - c.body.height) > 1.8)
    {
        notify(L"Остановитесь у пассажира");
        return;
    }
    job.ride.car = controlledCar;
    job.ride.boardBy = time + 14;
    notify(L"Подождите, пока пассажир подойдёт к двери");
}

void World::stepPassengerJob(double dt)
{
    auto &p = people[job.actor];
    if (p.dead || (!job.acquired && time > job.deadline)) { finishJob(false); return; }
    if (job.ride.car < 0) return;
    auto &c = cars[job.ride.car];
    if (c.destroyed || c.falling || c.fireTime > 0) { finishJob(false); return; }
    if (!job.acquired)
    {
        if (controlledCar != job.ride.car || time > job.ride.boardBy || c.body.v.length() > 2 ||
            (p.body.p - c.body.p).length() > 13)
        {
            job.ride.car = -1;
            notify(L"Посадка прервана. Остановитесь ближе");
            return;
        }
        if (c.body.v.length() >= 1.5 || p.fear > 0 || (p.body.p - passengerDoor(c,p.body.p)).length() > .85 ||
            c.body.roadLevel != p.body.roadLevel || std::abs(c.body.height - p.body.height) > 1.8) return;
        p.riding = job.ride.car;
        p.seat = 1;
        p.alight = -1;
        seatPassenger(p);
        job.acquired = true;
        job.deadline = time + 45 + (jobTarget() - c.body.p).length() / 5;
        notify(job.kind == JobKind::patient ? L"Везите пациента в поликлинику" : L"Пассажир в салоне. Адрес на карте");
        return;
    }
    if (controlledCar != job.ride.car)
    {
        job.stopped = 0;
        job.ride.away += dt;
        if (job.ride.away > (controlledCar < 0 ? 25 : 3)) finishJob(false);
        return;
    }
    job.ride.away = 0;
    double reach = job.kind == JobKind::patient ? 14 : 9;
    bool parked = c.grounded() && !c.body.roadLevel && c.body.v.length() < .8 &&
                  (c.body.p - jobTarget()).length() < reach;
    job.stopped = parked ? job.stopped + dt : 0;
    if (job.stopped >= 1 && vehicleExit(c)) finishJob(true);
}

void World::releasePassenger(const Job &trip, bool delivered)
{
    auto &p = people[trip.actor];
    if (!trip.acquired || p.riding < 0) return;
    auto &c = cars[p.riding];
    if (p.dead) { p.riding = -1; return; }
    if (c.destroyed || c.falling) { seatPassenger(p); return; }
    auto exit = vehicleExit(c);
    // A cancelled trip can return the actor to its sidewalk if the vehicle was boxed in.
    resumeWalk(p, exit.value_or(p.target), exit ? c.body.roadLevel : 0);
    if (delivered && trip.kind == JobKind::patient) p.health = 1;
}
} // namespace pz
