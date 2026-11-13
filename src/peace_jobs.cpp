#include "promzona/world.hpp"

#include <algorithm>
#include <cmath>
#include <numbers>

namespace pz
{
namespace
{
bool civilian(const Car &car)
{
    return car.kind == 0 || car.kind == 1 || car.kind == 2 || car.kind == 8;
}
} // namespace

Vec2 World::peaceOffset(int spot)
{
    if (spot < 0)
        return {};
    const double angle = spot * (2 * std::numbers::pi / 8), radius = 2.8 + spot / 8 * 1.2;
    return Vec2{std::sin(angle), std::cos(angle)} * radius;
}

bool World::peaceGround(Vec2 at, double margin) const
{
    return layout.urbanSite(at, margin) && CityLayout::waterLevel - terrain.bedHeight(at.x, at.z) <= .01;
}

bool World::startPeaceJob(JobKind kind)
{
    if (kind == JobKind::broadcast &&
        (controlledCar < 0 || !civilian(cars[controlledCar]) || cars[controlledCar].destroyed ||
         cars[controlledCar].falling || cars[controlledCar].fireTime > 0 || !cars[controlledCar].grounded() ||
         cars[controlledCar].body.v.length() > .8))
    {
        notify(L"Остановите гражданскую машину для установки громкоговорителя");
        return false;
    }
    Job next{kind,  kind == JobKind::broadcast ? controlledCar : -1,
             -1,    kind == JobKind::broadcast ? 500 : 300,
             false, time + 720};
    auto origin = position();
    // Port of the managed choice: the bus stops and the food kiosks within 700 m, nearest
    // first, no two of them within a hundred metres of each other; then a free point of
    // pavement beside each. A marker is never on a roof, a ramp, a carriageway or in water,
    // and never inside the shelter, a parked car or somebody who stands there.
    std::vector<int> candidates;
    for (int i = 0; i < int(sites.size() + busStops.size()); ++i)
        if (peacePlace(i) && (peacePoint(i) - origin).length2() < 700 * 700)
            candidates.push_back(i);
    std::ranges::stable_sort(candidates, {}, [&](int i) { return (peacePoint(i) - origin).length2(); });
    auto freePavement = [&](Vec2 at)
    {
        return peaceGround(at, 1) && layout.roadDistance(at) > layout.roadHalf() &&
               std::ranges::none_of(roadLevels,
                                    [&](const RoadLevel &level) { return level.reserved(at, 3); }) &&
               vehicleSpace(at, terrain.height(at) + .85, nullptr, false);
    };
    int count = 0;
    for (int i : candidates)
    {
        const Vec2 centre = peacePoint(i);
        bool nearby = false;
        for (int n = 0; n < count; ++n)
            if ((peaceSpot(next.route.sites[n], next.route.spots[n]) - centre).length2() < 100 * 100)
                nearby = true;
        if (nearby)
            continue;
        int spot = 0;
        while (spot < peaceSpots && !freePavement(centre + peaceOffset(spot)))
            ++spot;
        if (spot == peaceSpots)
            continue;
        next.route.sites[count] = i;
        next.route.spots[count++] = spot;
        if (count == next.stopCount())
            break;
    }
    if (count != next.stopCount())
    {
        notify(L"Поблизости мало подходящих площадок");
        return false;
    }
    next.route.previous = origin;
    next.destination = next.route.sites[0];
    job = next;
    notify(kind == JobKind::broadcast ? L"Включите громкоговоритель клавишей E и объедьте три района"
                                      : L"Раздайте листовки в четырёх отмеченных местах");
    return true;
}

void World::interactPeaceJob()
{
    if (job.escaping() || time < job.route.interactAt)
        return;
    if (job.kind == JobKind::leaflets)
    {
        if (controlledCar >= 0 || player.roadLevel || (player.p - jobTarget()).length() >= 2.2 ||
            std::abs(ground() - terrain.height(jobTarget())) > 1.6)
            return;
        job.route.enabled = true;
    }
    else
    {
        if (controlledCar != job.actor || cars[job.actor].fireTime > 0 || cars[job.actor].destroyed)
            return;
        job.route.enabled = !job.route.enabled;
        if (job.route.enabled)
            job.route.soundAt = time;
    }
    job.route.interactAt = time + .35;
}

void World::stepPeaceJob(double dt)
{
    if (job.escaping())
    {
        if (wantedLevel() == 0)
            finishJob(true);
        return;
    }
    double travel = (position() - job.route.previous).length();
    job.route.previous = position();
    if (job.kind == JobKind::leaflets)
    {
        if (controlledCar >= 0 || drone.active || player.roadLevel || aiming || playerBurnUntil > time ||
            (position() - jobTarget()).length() >= 2.2 || player.v.length() > .5 ||
            std::abs(ground() - terrain.height(jobTarget())) > 1.6)
        {
            job.route.enabled = false;
            job.stopped = 0;
            return;
        }
        if (!job.route.enabled)
            return;
        job.stopped += dt;
        if (job.stopped + 1e-9 < 3)
            return;
        sounds.push_back({"paper", position(), .55, false});
        addCrime(.65);
        job.route.enabled = false;
    }
    else
    {
        auto &car = cars[job.actor];
        if (car.destroyed || car.falling)
        {
            finishJob(false, L"Заказ сорван");
            return;
        }
        if (controlledCar != job.actor || car.fireTime > 0 || drone.active)
            job.route.enabled = false;
        if (!job.route.enabled)
            return;
        if (time >= job.route.soundAt)
        {
            sounds.push_back({"peace_pa", car.body.p, .7, false});
            job.route.soundAt = time + 7;
        }
        job.route.noise += dt;
        if (job.route.noise >= 8)
        {
            job.route.noise = std::fmod(job.route.noise, 8.);
            addCrime(.35);
        }
        double speed = car.body.v.length();
        if (speed < 2 || speed > 18 || !car.grounded() || car.body.roadLevel || travel < dt ||
            travel > 25 * dt + .05 || (position() - jobTarget()).length() >= 70 ||
            layout.roadDistance(position()) > layout.roadHalf())
            return;
        job.stopped += dt;
        if (job.stopped + 1e-9 < 30)
            return;
    }
    job.stopped = 0;
    ++job.route.completed;
    if (job.escaping())
    {
        job.route.enabled = false;
        job.destination = -1;
        notify(L"Скройтесь от преследования, чтобы получить оплату");
    }
    else
    {
        job.destination = job.route.sites[job.route.completed];
        notify(L"Следующее место отмечено на карте");
    }
}

bool World::validPeaceJob() const
{
    const auto &r = job.route;
    bool leaflets = job.kind == JobKind::leaflets;
    if (r.completed < 0 || r.completed > job.stopCount() || r.noise < 0 || r.noise >= 8 || r.soundAt < 0 ||
        r.interactAt < 0 || job.stopped > (leaflets ? 3 : 30) || job.fare != (leaflets ? 300 : 500))
        return false;
    for (int n = 0; n < job.stopCount(); ++n)
    {
        int i = r.sites[n];
        if (!peacePlace(i) || r.spots[n] < -1 || r.spots[n] >= peaceSpots ||
            (r.spots[n] >= 0 && !peaceGround(peaceSpot(i, r.spots[n]), .5)))
            return false;
        if (std::find(r.sites.begin(), r.sites.begin() + n, i) != r.sites.begin() + n)
            return false;
    }
    if (job.escaping() ? (job.destination != -1 || r.enabled) : job.destination != r.sites[r.completed])
        return false;
    if (leaflets)
        return job.actor == -1;
    return job.actor >= 0 && job.actor < int(cars.size()) && civilian(cars[job.actor]);
}
} // namespace pz
