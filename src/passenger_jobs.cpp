#include "promzona/world.hpp"
#include <limits>

namespace Promzona
{
    namespace
    {
        int passengerFare(JobKind kind, double distance)
        {
            const double fare = kind == JobKind::taxi ? 20 + distance * .11 : 35 + distance * .10;
            return std::max(5, 5 * static_cast<int>(std::round(fare / 5)));
        }
    }

    Vec2 World::passengerDoor(const Car &c, Vec2 from)
    {
        auto side = rotate(Vec2{c.body.halfW + .65, 0}, c.body.angle);
        return c.body.p + side * (dot(from - c.body.p, side) < 0 ? -1. : 1.);
    }

    bool World::canTakePassengerJob(int actor) const
    {
        const auto &p = people.at(actor);
        return p.waitingLine < 0 && actor != patrol.officer && !paramedic_call(actor) && !p.dead &&
               p.knockedUntil <= time && p.ownedScooter < 0 && !p.courier && !p.inCompany && p.riding < 0 &&
               p.health > 0 && !p.body.roadLevel && p.burnUntil <= time && p.fear <= 0 &&
               layout.urbanSite(p.body.p) && !levelAt(p.body.p, 2) &&
               !inside_building(p.body.p, p.body.height, .5);
    }

    bool World::waitingForTaxi(int actor) const
    {
        return actor >= 0 && std::ranges::any_of(hailers, [actor](auto h) { return h.actor == actor; });
    }

    const World::StreetHail *World::hailOf(int actor) const
    {
        const auto hail = std::ranges::find(hailers, actor, &StreetHail::actor);
        return actor >= 0 && hail != hailers.end() ? &*hail : nullptr;
    }

    int World::hailerInReach() const
    {
        if (job.kind != JobKind::none || controlledCar < 0 || drone.active)
            return -1;
        const auto &c = cars[controlledCar];
        if (c.kind > 2 || c.responding || c.destroyed || c.fireTime || c.falling || !c.grounded() ||
            c.body.roadLevel || c.body.v.length() >= 1.5)
            return -1;
        int best = -1;
        double distance = 8 * 8;
        for (const auto &h : hailers)
        {
            if (h.actor < 0 || time >= h.until)
                continue;
            const auto &p = people[h.actor];
            double d = (p.body.p - c.body.p).length2();
            if (d < distance && std::abs(p.body.height - c.body.height) < 1.8 && canTakePassengerJob(h.actor))
            {
                best = h.actor;
                distance = d;
            }
        }
        return best;
    }

    void World::updateHailers()
    {
        if (time < nextHail)
            return;
        nextHail = time + 3;
        Vec2 here = position();
        for (auto &h : hailers)
            if (h.actor >= 0 &&
                (time >= h.until || job.kind != JobKind::none ||
                 (people[h.actor].body.p - here).length2() > 240 * 240 || !canTakePassengerJob(h.actor)))
                h = {};
        if (job.kind != JobKind::none || controlledCar < 0 || cars[controlledCar].kind > 2 ||
            cars[controlledCar].destroyed || drone.active)
            return;
        auto slot = std::ranges::find(hailers, -1, &StreetHail::actor);
        if (slot == hailers.end() || people.empty())
            return;
        // Port of the managed MaintainHailers: somebody at a bus stop 45 to 210 m away, at the
        // kerb between the shelter and the road. The managed game makes a new person there; the
        // population here is fixed, and whoever is walking past the stop goes over to it.
        const size_t start = (size_t(time / 3) * 53) % std::max(1uz, busStops.size());
        for (size_t k = 0; k < busStops.size(); ++k)
        {
            const Vec2 stop = busStops[(start + k) % busStops.size()];
            if (const double away = (stop - position()).length(); away < 45 || away > 210)
                continue;
            if (std::ranges::any_of(
                    hailers, [&](const StreetHail &h)
                    { return h.actor >= 0 && (people[h.actor].body.p - stop).length2() < 12 * 12; }))
                continue;
            int nearest = -1;
            double distance = 1e18;
            for (int actor = 0; actor < int(people.size()); ++actor)
            {
                // On the pavement of the stop, on its block.
                const Vec2 off = people[actor].body.p - stop;
                if (std::abs(off.x) < 1.5 && std::abs(off.z) < 9 && off.length2() < distance &&
                    canTakePassengerJob(actor))
                {
                    nearest = actor;
                    distance = off.length2();
                }
            }
            if (nearest < 0)
                continue;
            const double along = .3 + double((size_t(nearest) * 29) % 10) * .09;
            *slot = {nearest, time + 70 + nearest % 50, stop + Vec2{1.3, along}, true};
            return;
        }
        // With nobody near a stop, a pedestrian already at a usable kerb. No extra actors or
        // traffic RNG.
        const size_t first = (size_t(time / 3) * 137) % people.size();
        for (size_t n = 0; n < people.size(); ++n)
        {
            const int actor = int((first + n) % people.size());
            const auto at = people[actor].body.p;
            const double distance = (at - here).length2();
            if (distance < 45 * 45 || distance > 210 * 210)
                continue;
            double curb = layout.roadDistance(at) - layout.roadHalf();
            if (curb < .45 || curb > 3.0 || !canTakePassengerJob(actor))
                continue;
            if (std::ranges::any_of(
                    hailers,
                    [&](auto h) { return h.actor >= 0 && (people[h.actor].body.p - at).length2() < 14 * 14; }))
                continue;
            *slot = {actor, time + 70 + actor % 50};
            break;
        }
    }

    bool World::startPassengerJob(JobKind kind, int requestedActor)
    {
        if (job.kind != JobKind::none || health <= 0 || drone.active ||
            (kind != JobKind::taxi && kind != JobKind::patient))
            return false;
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
            if (score < best)
            {
                best = score;
                actor = int(i);
            }
        }
        if (actor < 0)
        {
            notify(L"Свободных пассажиров поблизости нет");
            return false;
        }

        auto &p = people[actor];
        int destination = -1, line = -1;
        if (kind == JobKind::patient)
            destination = nearestSite(ServiceKind::clinic, p.body.p);
        else if (busStops.size() >= 2)
        {
            // Port of the managed TaxiDestination: a bus stop 160 to 520 m away, or any stop
            // past 40 m where there is none, picked by who the passenger is and not by chance.
            std::vector<int> far;
            auto distance = [&](int stop) { return (busStops[stop] - p.body.p).length(); };
            for (int i = 0; i < int(busStops.size()); ++i)
                if (distance(i) >= 160 && distance(i) <= 520)
                    far.push_back(i);
            if (far.empty())
                for (int i = 0; i < int(busStops.size()); ++i)
                    if (distance(i) > 40)
                        far.push_back(i);
            if (!far.empty())
            {
                std::ranges::sort(far, {}, distance);
                destination = far[p.body.id % far.size()];
                line = busStopRide;
            }
        }
        else
        {
            // A city without bus stops: the kerb of a tram or trolleybus stop.
            double desired = 180. + double((size_t(actor) + jobsCompleted * 41u) % 260);
            best = std::numeric_limits<double>::max();
            for (size_t i = 0; i < lines.size(); ++i)
                for (size_t j = 0; j < lines[i].stops.size(); ++j)
                {
                    double d = (lines[i].curb(int(j)) - p.body.p).length();
                    double score = std::abs(d - desired);
                    if (d > 40 && score < best)
                    {
                        best = score;
                        destination = int(j);
                        line = int(i);
                    }
                }
        }
        if (destination < 0)
        {
            notify(L"Нет доступного пункта назначения");
            return false;
        }
        auto arrival = rideEnd(line, destination);
        double length = (arrival - p.body.p).length();
        if (kind == JobKind::patient)
            p.health = std::min(p.health, .65);
        job = {kind,        actor,
               destination, passengerFare(kind, length),
               false,       time + 180 + (p.body.p - position()).length() / 4};
        hailers = {};
        job.ride.line = line;
        notify(kind == JobKind::patient ? L"Вызов из поликлиники. Заберите пациента"
                                        : L"Пассажир ждёт на отмеченном тротуаре");
        return true;
    }

    void World::interactPassengerJob()
    {
        if (job.acquired)
        {
            if (controlledCar != job.ride.car)
                return;
            const auto &car = cars[controlledCar];
            if (!car.grounded() || car.body.roadLevel || car.body.v.length() >= .8 || !vehicleExit(car))
                return;
            finishJob((jobTarget() - car.body.p).length() < arrive_radius(job.kind), L"Высадили без оплаты");
            return;
        }
        if (controlledCar < 0 || job.ride.car >= 0)
            return;
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
        job.ride.board_by = time + 14;
        notify(L"Подождите, пока пассажир подойдёт к двери");
    }

    void World::stepPassengerJob(double dt)
    {
        auto &p = people[job.actor];
        if (p.dead || (!job.acquired && time > job.deadline))
        {
            finishJob(false, p.dead ? nullptr : L"Пассажир не дождался и ушёл");
            return;
        }
        if (job.ride.car < 0)
            return;
        auto &c = cars[job.ride.car];
        if (c.destroyed || c.falling || c.fireTime > 0)
        {
            finishJob(false, L"Заказ сорван");
            return;
        }
        if (!job.acquired)
        {
            if (controlledCar != job.ride.car || time > job.ride.board_by || c.body.v.length() > 2 ||
                (p.body.p - c.body.p).length() > 13)
            {
                job.ride.car = -1;
                notify(L"Посадка прервана. Остановитесь ближе");
                return;
            }
            if (c.body.v.length() >= 1.5 || p.fear > 0 ||
                (p.body.p - passengerDoor(c, p.body.p)).length() > .85 || c.body.roadLevel != p.body.roadLevel ||
                std::abs(c.body.height - p.body.height) > 1.8)
                return;
            p.riding = job.ride.car;
            p.seat = 1;
            p.alight = -1;
            seatPassenger(p);
            job.acquired = true;
            const double length = (jobTarget() - c.body.p).length();
            job.fare = passengerFare(job.kind, length);
            job.deadline = time + (job.kind == JobKind::taxi ? 45 + length / 7 : 40 + length / 8);
            job.ride.health_at_last_scare = c.health;
            notify(job.kind == JobKind::patient ? L"Везите пациента в поликлинику"
                                                : L"Пассажир в салоне. Адрес на карте");
            return;
        }
        if (controlledCar != job.ride.car)
        {
            job.stopped = 0;
            job.ride.away += dt;
            if (job.ride.away > (controlledCar < 0 ? 25 : 3))
                finishJob(false, L"Пассажир не дождался и ушёл");
            return;
        }
        job.ride.away = 0;
        // Every further 12 % of damage since boarding frightens the passenger once more, as
        // in the managed game: one hard blow can count twice.
        if (job.ride.scares < 3 && job.ride.health_at_last_scare - c.health > .12)
        {
            ++job.ride.scares;
            notify(L"Пассажир испугался удара. Оплата снижена");
            job.ride.health_at_last_scare -= .12;
        }
        job.ride.health_at_last_scare = std::max(c.health, job.ride.health_at_last_scare);
        bool parked = c.grounded() && !c.body.roadLevel && c.body.v.length() < .8 &&
                      (c.body.p - jobTarget()).length() < arrive_radius(job.kind);
        job.stopped = parked ? job.stopped + dt : 0;
        if (job.stopped > .8 && vehicleExit(c))
            finishJob(true);
    }

    void World::releasePassenger(const Job &trip, bool delivered)
    {
        auto &p = people[trip.actor];
        if (!trip.acquired || p.riding < 0)
            return;
        auto &c = cars[p.riding];
        if (p.dead)
        {
            p.riding = -1;
            return;
        }
        if (c.destroyed || c.falling)
        {
            seatPassenger(p);
            return;
        }
        auto exit = vehicleExit(c);
        // A cancelled trip can return the actor to its sidewalk if the vehicle was boxed in.
        resumeWalk(p, exit.value_or(p.target), exit ? c.body.roadLevel : 0);
        if (delivered && trip.kind == JobKind::patient)
            p.health = 1;
    }
}
