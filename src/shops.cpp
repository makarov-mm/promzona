#include "promzona/world.hpp"

namespace pz
{
void World::generateShops()
{
    ownedGarage = openMarket = -1;
    // Append after the older services and street fixtures. Both use stable indices in saves.
    for (const auto &building : buildings)
    {
        bool stall = building.kind == BuildingKind::stall;
        if (!stall && building.kind != BuildingKind::garage)
            continue;
        const double xInBlock = std::fmod(building.p.x - layout.coordinate(0), spacing);
        // One public workshop per garage row, with an entrance facing its access lane.
        if (!stall && (xInBlock >= spacing * .5 || building.front > 0))
            continue;
        Vec2 door = building.p + Vec2{0, building.front * (building.half.z + (stall ? .85 : 1.4))};
        if (!layout.urbanSite(door, 1) || levelAt(door, 1) ||
            insideBuilding(door, terrain.height(door) + .85, .35))
            continue;
        sites.push_back({stall ? ServiceKind::market : ServiceKind::garage, building.p, door, building.half.x,
                         building.half.z, building.height, true});
    }

    for (int j = 1; j < nodes - 2; ++j)
        for (int i = 1; i < nodes - 2; ++i)
        {
            if ((i * 3 + j * 5) % 31 || !layout.park(i, j) || !layout.road(i, j, 1) ||
                layout.canalColumn(i) || layout.clinic(i, j) || layout.station(i, j) ||
                layout.industrial(i, j))
                continue;
            for (int n : {-1, 0, 1})
            {
                Vec2 centre{layout.coordinate(i) + spacing * .5 + n * 4.2,
                            layout.coordinate(j) + layout.sidewalk() + 1.3};
                Vec2 door = centre + Vec2{0, -1.65};
                if (!layout.urbanSite(centre, 3) || levelAt(centre, 4) ||
                    terrain.height(door) <= CityLayout::waterLevel + .5)
                    continue;
                auto nearCounter = [&](const ServiceSite &s)
                { return s.kind != ServiceKind::market && (s.entrance - door).length2() < 12 * 12; };
                auto obstructs = [&](const Body &b)
                {
                    return std::abs(b.p.x - centre.x) < 1.85 + (b.circle ? b.radius : b.halfW) &&
                           b.p.z + (b.circle ? b.radius : b.halfH) > door.z - .65 &&
                           b.p.z - (b.circle ? b.radius : b.halfH) < centre.z + 1.15;
                };
                if (std::ranges::any_of(sites, nearCounter) || std::ranges::any_of(staticBodies, obstructs))
                    continue;
                sites.push_back({ServiceKind::market, centre, door, 1.5, .8, 2.4});
                Body body;
                body.id = 0x50000000u + static_cast<unsigned>(sites.size());
                body.p = centre;
                body.halfW = 1.5;
                body.halfH = .8;
                body.verticalExtent = 2.4;
                body.height = terrain.height(centre) + 1.2;
                staticBodies.push_back(body);
            }
        }
}

// Port of CityAmenities.BuildStops: a shelter for every fourth street that runs north, in
// every third row. Nothing here draws from the city's random sequence, and the colliders
// go after everything older: saves hold indices into both.
void World::generateBusStops()
{
    busStops.clear();
    busStopOfNode.assign(size_t(nodes * nodes), -1);
    // The shelter itself, with a hand's breadth to spare. The managed game keeps a wider box
    // free for its passengers; here nobody walks on this side of the street, the houses stand
    // nearer to it, and a lamp post at the kerb in front of a shelter is no obstacle.
    constexpr double footprintX = .75, footprintZ = 1.65;
    const size_t older = staticBodies.size();
    for (int j = 1; j < nodes - 2; j += 3)
        for (int i = 1; i < nodes - 2; i += 4)
        {
            if (!layout.road(i, j, 0))
                continue;
            const Vec2 sign{layout.coordinate(i) - layout.sidewalk(), layout.coordinate(j) + spacing * .5};
            const Vec2 centre = shelter(sign);
            if (levelAt(sign, 4) || !layout.urbanSite(sign, 3) ||
                terrain.height(centre) <= CityLayout::waterLevel + .5)
                continue;
            auto taken = [&](Vec2 p, double halfX, double halfZ) {
                return std::abs(p.x - centre.x) < footprintX + halfX &&
                       std::abs(p.z - centre.z) < footprintZ + halfZ;
            };
            // A kiosk this near would offer its counter on the same key. Whatever else already
            // stands here keeps its place.
            if (std::ranges::any_of(sites, [&](const ServiceSite &s)
                                    { return (s.centre - sign).length2() <= 36; }) ||
                std::ranges::any_of(buildings,
                                    [&](const Building &b) { return taken(b.p, b.half.x, b.half.z); }) ||
                std::ranges::any_of(trees, [&](const TreePlant &t)
                                    { return taken({t.foot.x, t.foot.z}, t.radius + .5, t.radius + .5); }) ||
                std::any_of(staticBodies.begin(), staticBodies.begin() + std::ptrdiff_t(older),
                            [&](const Body &b)
                            {
                                const bool square = b.circle || std::abs(std::sin(b.angle)) > .05;
                                const double reach = b.circle ? b.radius : std::max(b.halfW, b.halfH);
                                return taken(b.p, square ? reach : b.halfW, square ? reach : b.halfH);
                            }))
                continue;
            busStopOfNode[size_t(i + j * nodes)] = int(busStops.size());
            busStops.push_back(sign);
            // The rear wall and the bench; the roof is above everybody's head.
            const double ground = terrain.height(centre);
            for (const auto &[offset, halfX, halfZ, height] :
                 {std::tuple{-.48, .10, 1.48, 2.4}, std::tuple{-.25, .34, 1.12, .85}})
            {
                Body body;
                body.id = 0x52000000u + std::uint32_t(staticBodies.size() - older);
                body.p = centre + Vec2{offset, 0};
                body.halfW = halfX;
                body.halfH = halfZ;
                body.verticalExtent = height;
                body.height = ground + height * .5;
                staticBodies.push_back(body);
            }
        }
}

namespace
{
int forecourt(const World &w, ServiceKind kind, double radius)
{
    int best = -1;
    double bestDistance = radius * radius;
    for (int i = 0; i < int(w.sites.size()); ++i)
    {
        const auto &s = w.sites[i];
        if (s.kind != kind)
            continue;
        auto delta = w.position() - s.entrance;
        double distance = delta.length2();
        if (distance >= bestDistance ||
            dot(w.position() - s.centre, (s.entrance - s.centre).unit()) < s.halfDepth ||
            std::abs(w.ground() - w.terrain.height(s.entrance)) > 1.6)
            continue;
        best = i;
        bestDistance = distance;
    }
    return best;
}
} // namespace

int World::marketInReach() const
{
    if (controlledCar >= 0 || playerScooter >= 0 || player.roadLevel || health <= 0 || drone.active)
        return -1;
    int i = forecourt(*this, ServiceKind::market, 1.8);
    if (i < 0)
        return -1;
    Vec2 middle = (sites[i].entrance + player.p) * .5;
    return insideBuilding(middle, ground() + .8, .05) ? -1 : i;
}

int World::garageInReach() const
{
    if (transitPassenger())
        return -1;
    if (drone.active || health <= 0 || playerScooter >= 0)
        return -1;
    if (controlledCar >= 0)
    {
        const auto &c = cars[controlledCar];
        if (c.body.roadLevel || c.destroyed || c.falling || c.recovering || !c.grounded() ||
            c.body.v.length2() >= 2.25)
            return -1;
    }
    else if (player.roadLevel)
        return -1;
    return forecourt(*this, ServiceKind::garage, controlledCar >= 0 ? 10. : 2.5);
}

int World::garageQuote() const
{
    if (controlledCar < 0)
        return 0;
    return int(std::ceil(std::max(0., (1. - cars[controlledCar].health) * 24. - 1e-9))) * 5 +
           (wantedLevel() > 0 ? 30 : 0);
}

bool World::serviceGarage()
{
    if (controlledCar < 0 || garageInReach() < 0)
        return false;
    int price = garageQuote();
    if (!price)
        notify(L"Машина в порядке");
    else if (money < price)
        notify(L"Не хватает денег");
    else
    {
        auto &c = cars[controlledCar];
        c.health = 1;
        c.fireTime = c.safeDrivingTime = 0;
        c.firePlayerCaused = false;
        bool repaint = wantedLevel() > 0;
        if (repaint)
        {
            c.paint = c.paint % 12 + 1;
            wanted = wantedCooldown = 0;
            updateDispatch();
            for (auto &p : people)
                p.alertUntil = p.aimSeconds = 0;
        }
        money -= price;
        sounds.push_back({"spend", position(), .7, true});
        notify((repaint ? L"Машина перекрашена. Розыск снят  −$" : L"Машина отремонтирована  −$") +
               std::to_wstring(price));
    }
    return true;
}

int World::ownedVehicleCount() const
{
    return int(std::ranges::count(cars, true, &Car::personal));
}

void World::useOwnedGarage()
{
    int site = garageInReach();
    if (site < 0)
        return;
    if (ownedGarage < 0)
    {
        if (money < garagePrice)
        {
            notify(L"Не хватает денег на гараж");
            return;
        }
        money -= garagePrice;
        ownedGarage = site;
        sounds.push_back({"spend", position(), .7, true});
        notify(L"Теперь это ваш гараж  −$400");
        return;
    }
    if (site != ownedGarage || controlledCar < 0)
        return;
    auto &c = cars[controlledCar];
    if (c.kind > 1 || c.fireTime > 0 || job.kind != JobKind::none || c.body.v.length2() > .25)
    {
        notify(L"Для парковки остановите исправную легковую машину или фургон и завершите заказ");
        return;
    }
    if (!c.personal && ownedVehicleCount() >= garageCapacity)
    {
        notify(L"Все три места заняты");
        return;
    }
    enterExit();
    if (controlledCar >= 0)
    {
        notify(L"Оставьте место, чтобы открыть дверь");
        return;
    }
    c.personal = true;
    notify(L"Машина припаркована  " + std::to_wstring(ownedVehicleCount()) + L"/3");
}

int World::shopPrice(ShopItem item) const
{
    switch (item)
    {
    case ShopItem::pistol:
        return inventory.owns(WeaponId::pistol) ? 40 : 120;
    case ShopItem::automatic:
        return inventory.owns(WeaponId::automatic) ? 80 : 350;
    case ShopItem::pills:
        return 25;
    case ShopItem::energy:
        return 15;
    }
    return 0;
}

bool World::buyAtMarket(ShopItem item)
{
    if (openMarket < 0 || marketInReach() != openMarket)
        return false;
    int price = shopPrice(item);
    if (!price)
        return false;
    if (money < price)
    {
        notify(L"Не хватает денег");
        return false;
    }
    switch (item)
    {
    case ShopItem::pistol:
    case ShopItem::automatic:
        if (!inventory.give(item == ShopItem::pistol ? WeaponId::pistol : WeaponId::automatic))
        {
            notify(L"Боезапас полон");
            return false;
        }
        break;
    case ShopItem::pills:
        if (health >= .999)
        {
            notify(L"Здоровье уже восстановлено");
            return false;
        }
        health = std::min(1., health + pillHealing);
        break;
    case ShopItem::energy:
        if (energySeconds > 2)
        {
            notify(L"Энергетик ещё действует");
            return false;
        }
        energySeconds = 25;
        break;
    }
    money -= price;
    sounds.push_back({"spend", player.p, .7, true});
    notify(L"Покупка  −$" + std::to_wstring(price));
    return true;
}
} // namespace pz
