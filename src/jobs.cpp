#include "promzona/world.hpp"
#include <limits>

namespace Promzona
{
void World::generateServices()
{
    sites.clear();
    job = {};
    jobsCompleted = jobsFailed = lastPay = seriesStage = seriesCompleted = 0;
    jobEarnings = 0;
    hailers = {};
    nextHail = 0;
    energySeconds = clinicCredit = purchaseAt = noticeUntil = clinicPoorAt = 0;
    notice.clear();
    for (int j = 1; j < nodes - 2; ++j)
        for (int i = 1; i < nodes - 2; ++i)
        {
            Vec2 block = nodePoint(i + j * nodes) + Vec2{18, 18};
            if (layout.station(i, j))
                stationEntrance = block + Vec2{0, 9.25};
            if (layout.clinic(i, j))
            {
                auto building = std::ranges::find_if(
                    buildings, [&](const Building &b)
                    { return (b.p - block).length2() < .01 && b.half.x == 9 && b.half.z == 8; });
                if (building != buildings.end())
                    sites.push_back({ServiceKind::clinic, block, block + Vec2{0, 9.25}, 9, 8, 8});
                continue;
            }
            if (layout.canalColumn(i) || layout.station(i, j) || !layout.road(i, j, 1))
                continue;
            const bool food = (i * 7 + j * 11) % 23 == 0;
            const bool garage = !food && layout.park(i, j) && (i * 5 + j * 7) % 11 == 0;
            if (!food && !garage)
                continue;
            const bool kiosk = food && layout.park(i, j) && (i + j) % 2 == 0;
            const double hw = garage ? 2.45 : kiosk ? 1.65 : .5;
            const double hd = garage ? 2.8 : kiosk ? 1.2 : .4;
            const double h = garage ? 2.85 : kiosk ? 2.75 : 1.85;
            // The regular streets already have a bin on the midpoint of this curb.
            // Try each side so a tree or entrance on one side does not erase the service.
            for (double offset : {4.2, -4.2, 0.})
            {
                Vec2 front{block.x + offset, layout.coordinate(j) + layout.sidewalk()};
                Vec2 centre = front + Vec2{0, hd + 1.15};
                if (!layout.urbanSite(centre, hw + 1) || levelAt(centre, std::max(hw, hd) + 2) ||
                    terrain.height(front) <= CityLayout::waterLevel + .5)
                    continue;
                bool blocked = std::ranges::any_of(staticBodies,
                                                   [&](const Body &b)
                                                   {
                                                       double w = b.circle ? b.radius : b.halfW,
                                                              d = b.circle ? b.radius : b.halfH;
                                                       return std::abs(b.p.x - centre.x) < hw + w + .35 &&
                                                              b.p.z + d > front.z - .7 &&
                                                              b.p.z - d < centre.z + hd + .35;
                                                   });
                if (blocked)
                    continue;
                sites.push_back({garage  ? ServiceKind::garage
                                 : kiosk ? ServiceKind::kiosk
                                         : ServiceKind::vending,
                                 centre, front, hw, hd, h});
                Body body;
                body.id = 0x50000000u + static_cast<unsigned>(sites.size());
                body.p = centre;
                body.halfW = hw;
                body.halfH = hd;
                body.verticalExtent = h;
                body.height = terrain.height(centre) + h * .5;
                staticBodies.push_back(body);
                break;
            }
        }
    for (int i = 0; i < 48; ++i)
        pickups.push_back({pickupSite(), WeaponId::none, false, 0, true});
}

void World::notify(std::wstring text)
{
    notice = std::move(text);
    noticeUntil = time + 4;
}

int World::nearestSite(ServiceKind kind, Vec2 from) const
{
    int result = -1;
    double closest = std::numeric_limits<double>::max();
    for (int i = 0; i < int(sites.size()); ++i)
        if (sites[i].kind == kind)
        {
            double distance = (from - sites[i].entrance).length2();
            if (distance < closest)
            {
                closest = distance;
                result = i;
            }
        }
    return result;
}

int World::counterInReach(bool parkedAllowed) const
{
    if (controlledCar >= 0)
    {
        const auto &c = cars[controlledCar];
        if (!parkedAllowed || c.destroyed || c.body.roadLevel || !c.grounded() || c.body.v.length() >= 1.5)
            return -1;
    }
    else if (player.roadLevel || health <= 0)
        return -1;
    int best = -1;
    double reach = controlledCar >= 0 ? 10. : 1.35;
    for (int i = 0; i < int(sites.size()); ++i)
    {
        if (!sites[i].food())
            continue;
        double d = (position() - sites[i].entrance).length();
        if (d < reach && std::abs(ground() - terrain.height(sites[i].entrance)) < 1.6)
        {
            best = i;
            reach = d;
        }
    }
    return best;
}

bool World::buyFood()
{
    if (transitPassenger())
        return false;
    int site = counterInReach(false);
    if (site < 0)
        return false;
    if (time < purchaseAt)
        return true;
    purchaseAt = time + .8;
    const auto &s = sites[site];
    if (health >= .999)
        notify(L"Здоровье уже восстановлено");
    else if (money < s.price())
        notify(L"Не хватает денег");
    else
    {
        money -= s.price();
        health = std::min(1., health + (s.kind == ServiceKind::kiosk ? .5 : .2));
        sounds.push_back({"spend", position(), .7, true});
        notify(L"Перекус  −$" + std::to_wstring(s.price()));
    }
    return true;
}

std::optional<Vec2> World::clearEntrance(Vec2 door) const
{
    // Leave room for a passer-by or a car parked across the doorway.
    for (int n = 0; n < 25; ++n)
    {
        Vec2 p = door + Vec2{((n + 2) % 5 - 2) * .65, (n / 5) * .50};
        auto occupied = [&](const Body &b)
        {
            if (std::abs(b.height - terrain.height(p) - .85) > b.verticalExtent / 2 + .8)
                return false;
            auto d = rotate(p - b.p, -b.angle);
            return b.circle ? d.length() < b.radius + .33
                            : std::abs(d.x) < b.halfW + .33 && std::abs(d.z) < b.halfH + .33;
        };
        if (layout.urbanSite(p) && !openCut(p) && !std::ranges::any_of(staticBodies, occupied) &&
            !std::ranges::any_of(cars, [&](const Car &c) { return occupied(c.body); }) &&
            !std::ranges::any_of(people, [&](const Person &q) { return !q.dead && occupied(q.body); }))
        {
            return p;
        }
    }
    return std::nullopt;
}

void World::clinicRespawn(Vec2 fallen)
{
    vehicleEntry = {};
    conversation = {};
    openMarket = -1;
    releaseScooter(playerScooter, {});
    if (drone.active)
        stop_drone(L"Связь с дроном прервана", 20);
    int index = nearestSite(ServiceKind::clinic, fallen);
    player.p = index < 0 ? Vec2{7, 7} : sites[index].entrance;
    player.p = clearEntrance(player.p).value_or(player.p);
    player.v = {};
    player.w = 0;
    player.wake();
    if (job.kind != JobKind::none)
        finishJob(false);
    energySeconds = clinicCredit = 0;
    int bill = money >= 40 ? money / 4 : 0;
    money -= bill;
    notify(bill ? L"Поликлиника  −$" + std::to_wstring(bill) : L"Вы у поликлиники");
    sounds.push_back({"respawn", player.p, .8, true});
}

void World::interactJob()
{
    if (transitPassenger())
        return;
    if (drone.active || health <= 0)
        return;
    if (openMarket >= 0)
    {
        openMarket = -1;
        return;
    }
    if (int market = market_in_reach(); market >= 0)
    {
        openMarket = market;
        return;
    }
    if (job.passenger() && (job.acquired || (position() - jobTarget()).length2() <= 100))
    {
        interactPassengerJob();
        return;
    }
    if (job.peace())
    {
        interactPeaceJob();
        return;
    }
    if (int actor = hailerInReach();
        actor >= 0 && startPassengerJob(people[actor].health <= .7 ? JobKind::patient : JobKind::taxi, actor))
    {
        interactPassengerJob();
        return;
    }
    if (service_garage())
        return;
    if (job.passenger())
    {
        interactPassengerJob();
        return;
    }
    int source = counterInReach(true);
    if (source < 0)
        return;
    if (job.kind == JobKind::courier && source == job.destination)
    {
        finishJob(true);
        return;
    }
    if (job.kind != JobKind::none)
        return;
    std::vector<int> candidates;
    for (int i = 0; i < int(sites.size()); ++i)
    {
        double d = (sites[i].entrance - sites[source].entrance).length();
        if (sites[i].food() && d >= 120 && d <= 450)
            candidates.push_back(i);
    }
    if (candidates.empty())
    {
        notify(L"Пока нет заказов поблизости");
        return;
    }
    int dest = candidates[(size_t(source) + jobsCompleted + jobsFailed) % candidates.size()];
    double d = (sites[source].entrance - sites[dest].entrance).length();
    job = {JobKind::courier, -1, dest, int(std::round((15 + d * .08) / 5)) * 5, false, time + 60 + d / 6};
    notify(L"Посылка получена. Адрес отмечен на карте");
}

bool World::startSeries()
{
    if (!startContract(seriesStage == 1 ? JobKind::elimination : JobKind::vehicle))
        return false;
    job.series = true;
    job.fare = 300 + seriesStage * 200;
    job.deadline = time + 600 - seriesStage * 90;
    return true;
}

int World::expectedPay() const
{
    if (job.kind == JobKind::none)
        return 0;
    if (job.kind != JobKind::courier && !job.passenger())
        return job.fare + (job.series && seriesStage == 2 ? 500 : 0);
    const bool late = time > job.deadline && (!job.passenger() || job.acquired);
    const double comfort = std::max(.30, 1. - .25 * job.ride.scares);
    return std::max(5, 5 * int(std::round(job.fare * (late ? .5 : 1.) * comfort / 5.)));
}

bool World::startContract(JobKind kind)
{
    if (job.kind != JobKind::none)
    {
        notify(L"Сначала завершите текущий заказ");
        return false;
    }
    if (drone.active || health <= 0)
        return false;
    if (kind == JobKind::taxi || kind == JobKind::patient)
        return startPassengerJob(kind);
    if (kind == JobKind::leaflets || kind == JobKind::broadcast)
        return startPeaceJob(kind);
    int actor = -1, destination = -1;
    double best = 1e10;
    if (kind == JobKind::vehicle)
    {
        for (int i = 0; i < int(cars.size()); ++i)
        {
            auto &c = cars[i];
            double score = std::abs((c.body.p - position()).length() - 130);
            if (i != controlledCar && !c.personal && !c.responding && c.line < 0 && c.kind <= 2 &&
                !c.destroyed && !c.falling && !c.fireTime && !c.body.roadLevel && c.health >= .75 &&
                score < best)
            {
                best = score;
                actor = i;
            }
        }
        best = 1e10;
        for (int i = 0; i < int(sites.size()); ++i)
        {
            double d = (sites[i].entrance - position()).length(), score = std::abs(d - 260);
            if (sites[i].kind == ServiceKind::garage && d > 80 && score < best)
            {
                best = score;
                destination = i;
            }
        }
        if (actor >= 0 && destination >= 0)
        {
            // A parked car with nobody in it, as in the managed game: taking it is neither
            // a carjacking nor a crime.
            cars[actor].driverless = true;
            cars[actor].responding = false;
            cars[actor].body.v = {};
            cars[actor].body.w = 0;
        }
    }
    else if (kind == JobKind::elimination)
    {
        for (int i = 0; i < int(people.size()); ++i)
        {
            auto &p = people[i];
            double score =
                std::abs((p.body.p - position()).length() - 150) + (p.sidearm == WeaponId::none ? 100 : 0);
            if (i != patrol.officer && !paramedic_call(i) && !p.dead && !p.courier && p.ownedScooter < 0 &&
                p.riding < 0 && !waitingForTaxi(i) && !p.body.roadLevel && score < best)
            {
                actor = i;
                best = score;
            }
        }
    }
    else
        return false;
    if (actor < 0 || (kind == JobKind::vehicle && destination < 0))
    {
        notify(L"Сейчас нет подходящих заказов");
        return false;
    }
    job = {kind, actor, destination, kind == JobKind::vehicle ? 250 : 350, false, time + 600};
    if (kind == JobKind::elimination)
    {
        // The target stays in the streets: no tram takes it away for the length of the contract.
        people[actor].waitingLine = people[actor].waitingStop = -1;
        people[actor].boardAfter = std::max(people[actor].boardAfter, time + 600);
    }
    notify(kind == JobKind::vehicle ? L"Найдите отмеченную машину" : L"Цель отмечена на карте");
    return true;
}

Vec2 World::jobTarget() const
{
    if (job.kind == JobKind::vehicle && !job.acquired)
        return cars[job.actor].body.p;
    if ((job.passenger() && !job.acquired) || job.kind == JobKind::elimination)
        return people[job.actor].body.p;
    if (job.kind == JobKind::taxi)
        return rideEnd(job.ride.line, job.destination);
    if (job.kind == JobKind::leaflets || job.kind == JobKind::broadcast)
        return job.destination < 0 ? position()
                                   : peaceSpot(job.destination, job.route.spots[size_t(job.route.completed)]);
    return job.destination < 0 ? position() : sites[job.destination].entrance;
}

// Where a ride ends: at a bus stop, at the entrance of the clinic, or at the kerb of a tram or
// trolleybus stop -- in a city without bus stops and in saves from before there were any.
Vec2 World::rideEnd(int line, int destination) const
{
    return line == busStopRide ? busStops[destination]
           : line < 0          ? sites[destination].entrance
                               : lines[line].curb(destination);
}

void World::finishJob(bool success, const wchar_t *failure)
{
    if (job.kind == JobKind::none)
        return;
    const Job finished = job;
    if (success)
    {
        lastPay = expectedPay();
        jobEarnings += lastPay;
        if (job.series && ++seriesStage == 3)
        {
            seriesStage = 0;
            ++seriesCompleted;
        }
        money = std::min(999999999, money + lastPay);
        ++jobsCompleted;
        notify(L"Заказ выполнен  +$" + std::to_wstring(lastPay));
        sounds.push_back({"cash", position(), .85, true});
    }
    else
    {
        ++jobsFailed;
        notify(failure ? failure : L"Заказ закрыт без оплаты");
    }
    job = {};
    if (finished.passenger())
        releasePassenger(finished, success);
}

void World::servicesStep(double dt)
{
    updateHailers();
    if (openMarket >= 0 && market_in_reach() != openMarket)
        openMarket = -1;
    energySeconds = std::max(0., energySeconds - dt);
    if (controlledCar < 0 && !player.roadLevel && health < 1)
    {
        int clinic = nearestSite(ServiceKind::clinic, player.p);
        if (clinic >= 0)
        {
            auto d = player.p - sites[clinic].entrance;
            if (std::abs(d.x) < 1.15 && std::abs(d.z) < 1.15 &&
                std::abs(ground() - terrain.height(player.p)) < .6)
            {
                double heal = std::min(1 - health, (money ? .075 : .025) * dt);
                health += heal;
                if (!money && time >= clinicPoorAt)
                {
                    clinicPoorAt = time + 6;
                    notify(L"Лечение платное — без денег лечат медленно");
                }
                if (money)
                {
                    clinicCredit += heal * 40;
                    int bill = std::min(money, int(clinicCredit));
                    money -= bill;
                    clinicCredit -= bill;
                }
                if (health >= 1)
                    clinicCredit = 0;
            }
        }
    }
    // The moment a delivery or a ride becomes late is said once.
    if ((job.kind == JobKind::courier || (job.passenger() && job.acquired)) && time > job.deadline &&
        time - dt <= job.deadline)
        notify(L"Опаздываем — заплатят половину");
    if (job.kind == JobKind::none || job.kind == JobKind::courier)
        return;
    if (job.passenger())
    {
        stepPassengerJob(dt);
        return;
    }
    if (time > job.deadline)
    {
        finishJob(false, L"Заказ сорван");
        return;
    }
    if (job.peace())
    {
        stepPeaceJob(dt);
        return;
    }
    if (job.kind == JobKind::elimination)
    {
        if (people[job.actor].dead)
            finishJob(false, L"Заказ сорван");
        return;
    }
    const auto &c = cars[job.actor];
    if (c.destroyed || c.falling)
    {
        finishJob(false, L"Заказ сорван");
        return;
    }
    if (controlledCar == job.actor)
        job.acquired = true;
    bool delivered = controlledCar == job.actor && c.health >= deliveryHealth() && !c.fireTime &&
                     !c.body.roadLevel && c.grounded() && c.body.v.length() < .8 &&
                     (position() - sites[job.destination].entrance).length() < 10;
    job.stopped = delivered ? job.stopped + dt : 0;
    if (job.stopped > 1)
        finishJob(true);
}

bool World::validJob() const
{
    if (job.fare < 0 || job.fare > 100000 || job.stopped < 0 || job.deadline < 0)
        return false;
    if (job.series && job.kind != (seriesStage == 1 ? JobKind::elimination : JobKind::vehicle))
        return false;
    auto site = [&](int i) { return i >= 0 && i < int(sites.size()); };
    switch (job.kind)
    {
    case JobKind::none:
        return job.actor == -1 && job.destination == -1;
    case JobKind::taxi:
    case JobKind::patient:
        if (job.actor < 0 || job.actor >= int(people.size()) || job.ride.car < -1 ||
            job.ride.car >= int(cars.size()) || (job.acquired && job.ride.car < 0) || job.ride.away < 0 ||
            job.ride.board_by < 0 || job.ride.scares < 0 || job.ride.scares > 3 ||
            !(job.ride.health_at_last_scare >= 0 && job.ride.health_at_last_scare <= 1) ||
            !std::isfinite(job.ride.crime_at))
            return false;
        if (job.ride.car >= 0 &&
            (cars[job.ride.car].responding || cars[job.ride.car].line >= 0 || cars[job.ride.car].kind > 2))
            return false;
        if (job.kind == JobKind::patient)
            return job.ride.line == -1 && site(job.destination) &&
                   sites[job.destination].kind == ServiceKind::clinic;
        if (job.ride.line == busStopRide)
            return job.destination >= 0 && job.destination < int(busStops.size());
        return job.ride.line >= 0 && job.ride.line < int(lines.size()) && job.destination >= 0 &&
               job.destination < int(lines[job.ride.line].stops.size());
    case JobKind::leaflets:
    case JobKind::broadcast:
        return validPeaceJob();
    case JobKind::courier:
        return job.actor == -1 && site(job.destination) && sites[job.destination].food();
    case JobKind::vehicle:
        return job.actor >= 0 && job.actor < int(cars.size()) && site(job.destination) &&
               sites[job.destination].kind == ServiceKind::garage;
    case JobKind::elimination:
        return job.actor >= 0 && job.actor < int(people.size()) && job.destination == -1;
    default:
        return false;
    }
}
} // namespace Promzona
