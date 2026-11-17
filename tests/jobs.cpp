#include "promzona/world.hpp"
#include "promzona/game_console.hpp"
#include "json.hpp"
#include <print>
#include <fstream>
#include <chrono>
#include <source_location>
#include <format>
#include <exception>
#include <stdexcept>

using namespace Promzona;
namespace
{
void check(bool ok, const char *message, std::source_location where = std::source_location::current())
{
    if (!ok)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
void visit(World &w, int i)
{
    check(i >= 0 && i < int(w.sites.size()), "service must exist");
    w.controlledCar = -1;
    w.player.p = w.sites[i].entrance;
    w.player.height = w.terrain.height(w.player.p) + .85;
    w.player.roadLevel = 0;
}
void test(World &w)
{
    const int kiosk = w.nearestSite(ServiceKind::kiosk, {});
    const int clinic = w.nearestSite(ServiceKind::clinic, {});
    const int vending = w.nearestSite(ServiceKind::vending, {});
    visit(w, kiosk);
    w.health = .2;
    w.money = 40;
    check(w.buyFood() && w.health == .7 && w.money == 25, "kiosk purchase");
    check(w.buyFood() && w.money == 25, "purchase debounce");
    w.time += 2;
    w.money = 0;
    check(w.buyFood() && w.money == 0 && w.health == .7, "cannot buy on credit");
    visit(w, vending);
    w.time += 2;
    w.money = 6;
    check(w.buyFood() && w.money == 1, "vending price");
    w.interactJob();
    check(w.job.kind == JobKind::courier && w.job.destination != vending, "courier has a distinct address");
    int fare = w.job.fare;
    w.interactJob();
    check(w.jobsCompleted == 0, "wrong counter cannot pay");
    visit(w, w.job.destination);
    w.interactJob();
    check(w.jobsCompleted == 1 && w.money == 1 + fare, "deliver parcel once");
    w.interactJob();
    int lateFare = w.job.fare;
    w.time = w.job.deadline + 1;
    visit(w, w.job.destination);
    w.interactJob();
    check(w.lastPay >= 5 && w.lastPay < lateFare && w.jobsCompleted == 2, "late delivery completes");
    check(w.startContract(JobKind::vehicle), "vehicle contract available");
    auto actor = w.job.actor;
    // The car of the contract stands with nobody in it, as in the managed game: whoever
    // takes it throws nobody out and commits no crime.
    {
        const double wantedBefore = w.wanted;
        const auto afoot = std::ranges::count_if(w.people, [](const Person &p) { return p.riding < 0; });
        check(w.cars[actor].driverless && w.unload_crew(w.cars[actor]) && w.wanted == wantedBefore &&
                  std::ranges::count_if(w.people, [](const Person &p) { return p.riding < 0; }) == afoot,
              "taking the car of a contract is a carjacking");
    }
    w.controlledCar = actor;
    auto &c = w.cars[actor];
    c.body.p = w.sites[w.job.destination].entrance;
    c.body.v = {};
    c.health = .49;
    w.servicesStep(1.4);
    check(w.job.acquired && w.jobsCompleted == 2, "damaged car refused");
    c.health = .5;
    c.body.v = {3, 0};
    w.servicesStep(1.4);
    check(w.job.stopped == 0, "delivery requires parking");
    c.body.v = {};
    w.servicesStep(.6);
    w.servicesStep(.6);
    check(w.jobsCompleted == 3 && w.lastPay == 250, "qualified car delivered");
    visit(w, clinic);
    check(w.startContract(JobKind::elimination), "target available");
    check(w.people[w.job.actor].boardAfter >= w.time + 600 && w.people[w.job.actor].waitingLine < 0 &&
              !w.waitingForTaxi(w.job.actor),
          "the target of a contract may leave by tram or taxi");
    w.killPerson(w.people[w.job.actor], {}, false);
    check(w.jobsFailed == 1 && w.job.kind == JobKind::none, "third party kill fails contract");
    check(w.notice == L"Заказ сорван", "a contract that fails does not say so");
    check(w.startContract(JobKind::elimination), "new target available");
    w.killPerson(w.people[w.job.actor], {}, true);
    check(w.jobsCompleted == 4 && w.lastPay == 350, "player kill rewarded");
    w.health = .35;
    w.money = 0;
    w.servicesStep(1);
    check(w.health > .35 && w.money == 0, "clinic treats broke player");
    // A patient without money is told why it is slow, once in six seconds.
    check(w.notice == L"Лечение платное — без денег лечат медленно",
          "the clinic does not explain the slow cure");
    w.notice.clear();
    w.time += 5;
    w.servicesStep(.1);
    check(w.notice.empty(), "the clinic repeats itself");
    w.time += 1.5;
    w.servicesStep(.1);
    check(!w.notice.empty(), "the clinic explains only once");
    w.money = 100;
    w.startContract(JobKind::elimination);
    w.clinicRespawn(w.sites[clinic].entrance);
    check(w.money == 75 && w.job.kind == JobKind::none &&
              (w.player.p - w.sites[clinic].entrance).length() < 4,
          "nearest clinic after death");
    check(w.startContract(JobKind::vehicle), "contract for save");
}
void shopAndGarage(const std::filesystem::path &path)
{
    World w;
    w.generate(42);
    const int market = w.nearestSite(ServiceKind::market, {});
    visit(w, market);
    w.money = 119;
    w.inventory.clear();
    w.interactJob();
    check(w.openMarket == market, "E opens the counter on foot");
    check(!w.buy_at_market(ShopItem::pistol) && w.money == 119, "no charge for a refused purchase");
    w.money = 600;
    check(w.buy_at_market(ShopItem::pistol) && w.money == 480 && w.inventory.ammo(WeaponId::pistol) == 30,
          "first pistol includes ammunition");
    for (int i = 0; i < 30; ++i)
        w.inventory.consume(WeaponId::pistol);
    check(w.buy_at_market(ShopItem::pistol) && w.money == 440 && w.inventory.ammo(WeaponId::pistol) == 30,
          "empty owned pistol receives the ammunition price");
    check(w.buy_at_market(ShopItem::automatic) && w.money == 90 &&
              w.inventory.ammo(WeaponId::automatic) == 30,
          "automatic sold with thirty rounds");
    w.health = .7;
    check(w.buy_at_market(ShopItem::pills) && w.health == 1 && w.money == 65, "pills restore health");
    check(!w.buy_at_market(ShopItem::pills) && w.money == 65, "full health does not cost money");
    check(w.buy_at_market(ShopItem::energy) && w.energySeconds == 25 && w.money == 50, "energy purchase");
    check(!w.buy_at_market(ShopItem::energy) && w.money == 50, "active energy cannot be bought twice");
    w.player.height += 4;
    w.servicesStep(.1);
    check(w.openMarket < 0 && !w.buy_at_market(ShopItem::energy), "leaving the counter closes the shop");

    const int garage = w.nearestSite(ServiceKind::garage, {});
    visit(w, garage);
    w.use_owned_garage();
    check(w.ownedGarage < 0 && w.money == 50, "insufficient funds leave ownership unchanged");
    w.money = 500;
    w.use_owned_garage();
    check(w.ownedGarage == garage && w.money == 100, "garage purchased at its entrance");
    w.controlledCar = 0;
    auto &car = w.cars[0];
    const auto &site = w.sites[garage];
    car.body.p = site.entrance + (site.entrance - site.centre).unit() * 4;
    car.body.height = w.terrain.height(car.body.p) + .8;
    car.body.roadLevel = -1;
    car.health = .5;
    w.wanted = 2;
    check(!w.service_garage(), "upper street workshop ignores traffic on another level");
    car.body.roadLevel = 0;
    car.body.v = {3, 0};
    check(!w.service_garage(), "moving vehicle cannot buy service");
    car.body.v = {};
    check(w.garage_quote() == 90, "scaled repair and respray have one quote");
    w.money = 89;
    w.interactJob();
    check(car.health == .5 && car.paint == 0 && w.wanted > 0 && w.money == 89,
          "unpaid repair changes nothing");
    w.money = 100;
    w.interactJob();
    check(car.health == 1 && car.paint > 0 && w.wanted == 0 && w.money == 10, "repair and repaint in game");
    w.interactJob();
    check(w.money == 10, "serviced car is not billed again");
    auto population = w.cars.size();
    w.use_owned_garage();
    check(w.controlledCar < 0 && car.personal && w.owned_vehicle_count() == 1 && w.cars.size() == population,
          "parking retains the same physical vehicle");
    w.save(path);
    World restored;
    restored.load(path);
    check(restored.ownedGarage == garage && restored.cars[0].personal &&
              restored.cars[0].paint == car.paint && restored.cars[0].id == car.id &&
              (restored.cars[0].body.p - car.body.p).length2() < .001 && restored.money == 10 &&
              restored.inventory.ammo(WeaponId::automatic) == 30,
          "owned garage, physical car, paint, money and purchases survive load");
    restored.player.p = {car.body.p.x > 0 ? -400. : 400., 40};
    restored.player.height = restored.terrain.height(restored.player.p) + .85;
    for (int frame = 0; frame < 60; ++frame)
        restored.step({}, 1. / 60);
    check((restored.cars[0].body.p - car.body.p).length() < .4,
          "distant traffic leaves the owned car parked");
    w.controlledCar = 0;
    car.personal = false;
    int count = 0;
    for (auto &other : w.cars)
        if (other.id != car.id && other.kind <= 1 && count < World::garageCapacity)
        {
            other.personal = true;
            ++count;
        }
    w.use_owned_garage();
    check(count == 3 && w.controlledCar == 0 && !car.personal,
          "fourth car refused without ejecting its driver");
}
// On foot at the place the job points to: a food kiosk or a bus stop.
void reach(World &w)
{
    w.controlledCar = -1;
    w.player.p = w.jobTarget();
    w.player.height = w.terrain.height(w.player.p) + .85;
    w.player.roadLevel = 0;
}
// Port of the managed choice of places for the peace jobs: bus stops count as well as kiosks.
void peacePlaces(const std::filesystem::path &path)
{
    World w;
    w.generate(20261105);
    const int stop = int(w.busStops.size() / 2);
    w.controlledCar = -1;
    w.player.p = w.busStops[size_t(stop)] + Vec2{1.5, 0};
    w.player.height = w.terrain.height(w.player.p) + .85;
    check(w.startContract(JobKind::leaflets), "no leaflet route from a bus stop");
    const auto &route = w.job.route.sites;
    const auto &spots = w.job.route.spots;
    check(route[0] == int(w.sites.size()) + stop, "the bus stop next to the player is not the first place");
    // The mark is not on the stop itself but on free pavement a few steps from it.
    check(std::abs(World::peaceOffset(0).z - 2.8) < 1e-9 && std::abs(World::peaceOffset(0).x) < 1e-9 &&
              std::abs(World::peaceOffset(10).x - 4) < 1e-9 && std::abs(World::peaceOffset(10).z) < 1e-9 &&
              std::abs(World::peaceOffset(23).length() - 5.2) < 1e-9 && World::peaceOffset(-1).length() == 0,
          "the rings about a place are not those of the managed game");
    check((w.jobTarget() - w.peaceSpot(route[0], spots[0])).length() < 1e-9,
          "the route does not point at the chosen spot");
    for (int a = 0; a < w.job.stop_count(); ++a)
    {
        const int place = route[size_t(a)], spot = spots[size_t(a)];
        check(w.peacePlace(place), "a place of the route is neither a kiosk nor a bus stop");
        const Vec2 at = w.peaceSpot(place, spot);
        const double away = (at - w.peacePoint(place)).length();
        check(spot >= 0 && spot < World::peaceSpots && away > 2.79 && away < 5.21,
              "a place of the route has no spot beside it");
        check(w.layout.roadDistance(at) > w.layout.roadHalf() && w.layout.urbanSite(at, 1) &&
                  !w.levelAt(at) && w.vehicleSpace(at, w.terrain.height(at) + .85, nullptr, false),
              "a spot of the route is not free pavement");
        // The first free point of the rings, not any free point.
        for (int n = 0; n < spot; ++n)
        {
            const Vec2 before = w.peaceSpot(place, n);
            check(w.layout.roadDistance(before) <= w.layout.roadHalf() || !w.layout.urbanSite(before, 1) ||
                      w.levelAt(before, 5) ||
                      !w.vehicleSpace(before, w.terrain.height(before) + .85, nullptr, false),
                  "a free point nearer the place was passed over");
        }
        for (int b = 0; b < a; ++b)
            check((w.peacePoint(place) - w.peaceSpot(route[size_t(b)], spots[size_t(b)])).length() >= 100,
                  "two places of the route are next to each other");
    }
    w.save(path);
    World restored;
    restored.load(path);
    check(restored.job.kind == JobKind::leaflets && restored.job.route.sites == route &&
              restored.job.route.spots == spots && (restored.jobTarget() - w.jobTarget()).length() < 1e-9,
          "a route by way of a bus stop is lost in a save");
    // A route saved before the search points at the places themselves, as it did then.
    {
        nlohmann::json saved;
        {
            std::ifstream in(path, std::ios::binary);
            saved = nlohmann::json::parse(in);
        }
        auto store = [&](const nlohmann::json &document)
        {
            std::ofstream out(path, std::ios::binary | std::ios::trunc);
            out << document;
        };
        check(saved.at("job").at("route").contains("spots"), "the save does not keep the spots");
        auto earlier = saved;
        earlier["job"]["route"].erase("spots");
        store(earlier);
        World opened;
        opened.load(path);
        check(opened.job.route.sites == route &&
                  opened.job.route.spots == std::array<int, 4>{-1, -1, -1, -1} &&
                  (opened.jobTarget() - w.peacePoint(route[0])).length() < 1e-9,
              "a route of an earlier save does not open as it was");
        // A number off the rings is refused.
        for (int wrong : {World::peaceSpots, -2})
        {
            auto bad = saved;
            bad["job"]["route"]["spots"][1] = wrong;
            store(bad);
            bool refused = false;
            try
            {
                World broken;
                broken.load(path);
            }
            catch (const std::exception &)
            {
                refused = true;
            }
            check(refused, "a save with a spot off the rings is accepted");
        }
    }
    std::filesystem::remove(path);
    // Somebody standing on the nearest point moves the mark to the next one.
    {
        World busy;
        busy.generate(20261105);
        busy.controlledCar = -1;
        busy.player.p = busy.busStops[size_t(stop)] + Vec2{1.5, 0};
        busy.player.height = busy.terrain.height(busy.player.p) + .85;
        auto &walker = busy.people[0];
        walker.dead = false;
        walker.riding = -1;
        walker.body.p = w.peaceSpot(route[0], spots[0]);
        walker.body.height = busy.terrain.height(walker.body.p) + .85;
        check(busy.startContract(JobKind::leaflets) && busy.job.route.sites[0] == route[0] &&
                  busy.job.route.spots[0] > spots[0],
              "the mark is put where somebody stands");
    }
}

void peaceContracts(const std::filesystem::path &path)
{
    World w;
    w.generate(42);
    auto tick = [&](double dt)
    {
        w.time += dt;
        w.servicesStep(dt);
    };
    check(w.startContract(JobKind::leaflets), "leaflet route available");
    w.interactJob();
    check(!w.job.route.enabled, "cannot distribute from car");
    reach(w);
    w.interactJob();
    tick(1.);
    w.player.v = {1, 0};
    tick(.1);
    check(!w.job.route.enabled && w.job.stopped == 0, "walking interrupts handout");
    w.player.v = {};
    for (int i = 0; i < 4; ++i)
    {
        reach(w);
        w.interactJob();
        tick(1.);
        if (i == 1)
        {
            w.save(path);
            World restored;
            restored.load(path);
            check(restored.job.route.completed == 1 && restored.job.route.enabled &&
                      restored.job.stopped == 1 && restored.job.route.sites == w.job.route.sites,
                  "restore mid-handout route");
        }
        tick(2.);
    }
    check(w.job.escaping() && w.wantedLevel() > 0 && w.money == 0, "must escape before pay");
    w.wanted = 0;
    tick(.1);
    check(w.money == 300 && w.jobsCompleted == 1, "leaflet payout");
    check(!w.startContract(JobKind::broadcast), "speakers require a parked civilian car");
    w.controlledCar = 0;
    auto &car = w.cars[0];
    car.body.v = {};
    check(w.startContract(JobKind::broadcast), "broadcast starts");
    w.interactJob();
    tick(8.1);
    check(w.job.stopped == 0 && w.wanted > 0, "stationary broadcast raises wanted only");
    w.interactJob();
    check(!w.job.route.enabled, "speaker switches off");
    tick(.4);
    w.interactJob();
    for (int i = 0; i < 3; ++i)
    {
        Vec2 street = w.jobTarget() - Vec2{0, w.layout.sidewalk() - 2};
        car.body.p = street;
        car.body.v = {6, 0};
        tick(.05);
        for (int n = 0; n < 320 && w.job.route.completed == i; ++n)
        {
            car.body.p.x += (n / 10) % 2 == 0 ? .6 : -.6;
            tick(.1);
        }
        check(w.job.route.completed == i + 1, "broadcast needs actual street movement");
    }
    check(w.job.escaping() && w.money == 300, "broadcast payout pending escape");
    w.wanted = 0;
    tick(.1);
    check(w.money == 800 && w.jobsCompleted == 2, "broadcast payout once");
}
void careerSeries(const std::filesystem::path &path)
{
    World w;
    w.generate(17);
    check(w.startSeries() && w.job.fare == 300 && w.job.deadline == w.time + 600,
          "start series at first vehicle");
    w.finishJob(false);
    check(w.seriesStage == 0 && w.jobEarnings == 0 && w.startSeries(), "failed stage can be replayed");
    auto deliver = [&](World &city, double health)
    {
        city.controlledCar = city.job.actor;
        auto &car = city.cars.at(city.controlledCar);
        car.body.p = city.sites.at(city.job.destination).entrance;
        car.body.height = city.terrain.height(car.body.p) + .8;
        car.body.v = {};
        car.health = health;
        city.servicesStep(1.1);
    };
    deliver(w, .49);
    check(w.job.acquired && w.seriesStage == 0, "damaged car cannot complete first stage");
    deliver(w, .5);
    check(w.seriesStage == 1 && w.lastPay == 300 && w.jobEarnings == 300, "first reward credited");
    check(w.startContract(JobKind::elimination), "ordinary contract remains available");
    w.killPerson(w.people.at(w.job.actor), {}, true);
    check(w.seriesStage == 1 && w.jobEarnings == 650, "ordinary contract does not advance series");
    check(w.startSeries() && w.job.kind == JobKind::elimination && w.job.deadline == w.time + 510,
          "second stage selected");
    w.save(path);
    World resumed;
    resumed.load(path);
    check(resumed.job.series && resumed.seriesStage == 1 && resumed.jobEarnings == 650,
          "active series restored after loading");
    resumed.killPerson(resumed.people.at(resumed.job.actor), {}, false);
    check(resumed.seriesStage == 1 && resumed.startSeries(), "third-party kill does not advance stage");
    resumed.killPerson(resumed.people.at(resumed.job.actor), {}, true);
    check(resumed.lastPay == 500 && resumed.startSeries() && resumed.job.deadline == resumed.time + 420,
          "third stage unlocked");
    deliver(resumed, .69);
    check(resumed.seriesStage == 2 && resumed.job.acquired, "last vehicle needs seventy percent health");
    deliver(resumed, .70);
    check(resumed.lastPay == 1200 && resumed.jobEarnings == 2350 && resumed.seriesStage == 0 &&
              resumed.seriesCompleted == 1 && resumed.startSeries(),
          "final bonus paid once and new series available");
}
void consoleAndHail(const std::filesystem::path &path)
{
    World w;
    w.generate(17);
    GameConsole console;
    for (wchar_t c : std::wstring_view(L"moey 75"))
        console.insert(c);
    console.move(-5);
    console.insert(L'n');
    console.execute(w, console.submit());
    check(w.money == 75, "middle-of-command insertion uses caret");
    for (wchar_t c : std::wstring_view(L"spe"))
        console.insert(c);
    console.history(-1);
    check(console.input() == L"money 75", "up recalls previous command");
    console.history(1);
    console.complete();
    check(console.input() == L"speed ", "down restores draft before completion");
    console.insert(L'2');
    console.execute(w, console.submit());
    console.execute(w, L"speed nan");
    console.execute(w, L"money 9 extra");
    check(w.simulationSpeed == 2 && w.money == 75, "invalid command does not mutate world");
    console.execute(w, L"cycle off");
    w.inventory.clear();
    console.execute(w, L"weapon pistol 12");
    check(w.inventory.ammo(WeaponId::pistol) == 12, "weapon command sets requested ammunition");
    console.execute(w, L"help");
    console.scroll(6);
    check(console.visible_lines(3).size() == 3, "help history scrolls");
    console.execute(w, L"clear");
    check(console.visible_lines(3).empty(), "clear resets visible output");
    auto count = w.people.size();
    w.updateHailers();
    int actor = w.hailers.front().actor;
    check(actor >= 0 && w.waitingForTaxi(actor) && w.people.size() == count,
          "street hail selects an existing pedestrian");
    w.save(path);
    World restored;
    restored.load(path);
    check(restored.waitingForTaxi(actor) && !restored.dayNightCycle && restored.simulationSpeed == 2,
          "street hail and world settings persist");
    auto &car = restored.cars.at(restored.controlledCar);
    auto &p = restored.people.at(actor);
    car.body.angle = 0;
    car.body.p = p.body.p - Vec2{car.body.halfW + 1.25, 0};
    car.body.height = p.body.height - .05;
    car.body.v = {5, 0};
    check(restored.hailerInReach() < 0, "cannot take a hail at speed");
    car.body.v = {};
    check(restored.hailerInReach() == actor, "waiting passenger reachable when parked");
    restored.interactJob();
    check(restored.job.actor == actor && restored.job.ride.car == restored.controlledCar &&
              !restored.waitingForTaxi(actor),
          "E accepts hail and begins normal boarding");
}
void passengerContracts(const std::filesystem::path &path)
{
    World w;
    auto board = [&](JobKind kind)
    {
        w.generate(17);
        w.controlledCar = 0;
        check(w.startContract(kind), "passenger contract started");
        auto &p = w.people[w.job.actor];
        auto &car = w.cars[0];
        car.body.v = {};
        car.body.w = 0;
        // Along the kerb beside the passenger, whichever pavement that is.
        const Vec2 road = w.roadward(p.body.p), along = perpendicular(road);
        car.body.angle = -std::atan2(along.x, along.z);
        car.body.p = p.body.p + road * (car.body.halfW + 1.25);
        car.body.height = p.body.height - .05;
        w.interactJob();
        check(!w.job.acquired && w.job.ride.car == 0, "passenger directed to a real car door");
        for (int i = 0; i < 100 && !w.job.acquired; ++i)
            w.step({}, 1. / 60);
        check(w.job.acquired && p.riding == 0, "passenger seated by the simulation");
    };
    for (auto kind : {JobKind::taxi, JobKind::patient})
    {
        board(kind);
        int passenger = w.job.actor;
        if (kind == JobKind::taxi)
        {
            // As in the managed game the ride goes to a bus stop some way off, and whoever
            // the passenger is decides which.
            check(w.job.ride.line == World::busStopRide && w.job.destination >= 0 &&
                      w.job.destination < int(w.busStops.size()) &&
                      (w.jobTarget() - w.busStops[w.job.destination]).length() < 1e-9,
                  "a taxi ride does not end at a bus stop");
            const Vec2 from = w.cars[0].body.p;
            std::vector<double> within;
            for (Vec2 stop : w.busStops)
                if (const double d = (stop - from).length(); d >= 150 && d <= 530)
                    within.push_back(d);
            const double ride = (w.jobTarget() - from).length();
            check(!within.empty() && ride >= 150 && ride <= 530,
                  "the bus stop of a taxi ride is next door or across town");
        }
        w.save(path);
        World loaded;
        loaded.load(path);
        check(loaded.job.kind == kind && loaded.people[passenger].riding == 0 &&
                  (loaded.jobTarget() - w.jobTarget()).length() < .01,
              "mid-trip save restores passenger and destination");
        auto &car = w.cars[0];
        auto &rider = w.people[passenger];
        if (kind == JobKind::patient)
            check(rider.health < 1, "patient needs treatment");
        car.body.p = w.jobTarget();
        car.body.v = {};
        car.body.height = w.terrain.height(car.body.p) + .8;
        car.lift = 0;
        car.body.roadLevel = 0;
        w.servicesStep(1.1);
        check(w.job.kind == JobKind::none, "parked passenger trip closes");
        check(rider.riding < 0 && !rider.dead, "passenger walks after drop-off");
        check(w.lastPay > 0, "passenger fare paid");
        if (kind == JobKind::patient)
            check(rider.health == 1, "clinic treats delivered patient");
    }
    board(JobKind::taxi);
    auto &taxi = w.cars.at(w.controlledCar);
    const int originalPay = w.expectedPay();
    taxi.health -= .13;
    w.servicesStep(.01);
    check(w.job.ride.scares == 1 && w.expectedPay() < originalPay, "collision reduces expected fare");
    // Every further 12 % counts once, also when one blow brings two of them.
    w.servicesStep(.01);
    check(w.job.ride.scares == 1, "one blow is counted again and again");
    // The moment the ride becomes late is said once; setting the passenger down away
    // from the address and leaving the car for too long have their own words.
    {
        World late = w;
        late.notice.clear();
        late.time = late.job.deadline + .005;
        late.servicesStep(.01);
        check(late.notice == L"Опаздываем — заплатят половину", "a late ride is not announced");
        late.notice.clear();
        late.time += .01;
        late.servicesStep(.01);
        check(late.notice.empty(), "a late ride is announced again");
        World dropped = w;
        auto &cab = dropped.cars.at(dropped.controlledCar);
        cab.body.v = {};
        if (dropped.vehicleExit(cab) && (dropped.jobTarget() - cab.body.p).length() > 20)
        {
            dropped.interactJob();
            check(dropped.job.kind == JobKind::none && dropped.notice == L"Высадили без оплаты",
                  "a passenger set down early is not mentioned");
        }
        World left = w;
        left.controlledCar = -1;
        left.player.p = left.cars.at(left.job.ride.car).body.p + Vec2{3, 0};
        left.servicesStep(25.5);
        check(left.job.kind == JobKind::none && left.notice == L"Пассажир не дождался и ушёл",
              "a passenger left alone does not give up");
    }
    {
        World hard = w;
        auto &struck = hard.cars.at(hard.controlledCar);
        struck.health -= .25;
        hard.servicesStep(.01);
        hard.servicesStep(.01);
        hard.servicesStep(.01);
        check(hard.job.ride.scares == 3, "a hard blow frightens the passenger only once");
        // The ride ends within eleven metres of the address, after 0.8 s at rest.
        struck.body.p = hard.jobTarget() + Vec2{10.5, 0};
        struck.body.v = {};
        struck.body.height = hard.terrain.height(struck.body.p) + .8;
        struck.lift = 0;
        struck.body.roadLevel = 0;
        if (hard.vehicleExit(struck))
        {
            hard.servicesStep(.45);
            check(hard.job.kind == JobKind::taxi, "the ride ends at once");
            hard.servicesStep(.45);
            check(hard.job.kind == JobKind::none && hard.lastPay > 0,
                  "a ride does not end ten metres from the address");
        }
    }
    w.addCrime(.1);
    w.addCrime(.1);
    check(w.job.ride.scares == 2, "same event does not double-charge passenger");
    w.time = w.job.deadline + 1;
    check(w.expectedPay() <= originalPay / 4 + 3, "late and frightened passenger pays less");
    w.save(path);
    World resumed;
    resumed.load(path);
    check(resumed.job.ride.scares == 2 && resumed.expectedPay() == w.expectedPay(),
          "adjusted fare survives reload");
    nlohmann::json old;
    {
        std::ifstream in(path);
        in >> old;
    }
    for (auto key : {"hailers", "nextHail", "seriesStage", "seriesCompleted", "jobEarnings", "dayNightCycle",
                     "simulationSpeed"})
        old.erase(key);
    for (auto key : {"healthAtLastScare", "crimeAt", "scares"})
        old["job"]["ride"].erase(key);
    {
        std::ofstream out(path);
        out << old;
    }
    resumed.load(path);
    resumed.servicesStep(.01);
    check(resumed.job.acquired && resumed.job.ride.scares == 0 && resumed.job.fare == w.job.fare &&
              resumed.seriesStage == 0 && resumed.dayNightCycle && resumed.simulationSpeed == 1,
          "older mid-trip save retains the fare and starts tracking damage at its actual vehicle health");
    taxi.body.v = {3, 0};
    w.interactJob();
    check(w.job.acquired, "cannot eject passenger while moving");
    taxi.body.v = {};
    w.interactJob();
    check(w.job.kind == JobKind::none && w.jobsFailed == 1 && w.jobEarnings == 0,
          "early drop-off has no fare");
    board(JobKind::taxi);
    int passenger = w.job.actor;
    w.controlledCar = -1;
    w.servicesStep(5);
    check(w.job.acquired, "passenger waits for a brief exit");
    w.servicesStep(21);
    check(w.job.kind == JobKind::none && w.jobsFailed == 1 && w.people[passenger].riding == -1,
          "abandoned passenger returns to sidewalk");
}
} // namespace
// Port of the managed MaintainHailers: whoever flags the taxi down does it at a bus stop,
// at the kerb between the shelter and the road. Nobody is made for it: a passer-by on the
// block of the stop goes over to it.
void hailAtStop(const std::filesystem::path &path)
{
    World w;
    w.generate(20261105);
    w.dispatchAt = w.patrolScanAt = w.nextRescueDispatch = 1e9;
    check(w.busStops.size() >= 2 && w.controlledCar >= 0, "no bus stops or no car to start in");
    const Vec2 stop = w.busStops[w.busStops.size() / 2];
    auto &car = w.cars[w.controlledCar];
    // In the kerb lane of the street of the stop, sixty metres short of it.
    car.body.p = stop + Vec2{3.85, -60};
    car.body.angle = 0;
    car.body.v = {};
    car.body.height = w.surface(car.body.p, 0) + .8;
    int actor = -1;
    for (int i = 0; i < int(w.people.size()); ++i)
        if ((w.people[i].body.p - stop).length() < 30)
            w.people[i].body.p += Vec2{0, 12 * World::spacing};
        else if (actor < 0 && w.canTakePassengerJob(i) && w.people[i].role == Person::Role::none)
            actor = i;
    check(actor >= 0, "nobody to walk past the stop");
    auto &p = w.people[actor];
    p.body.p = stop + Vec2{.2, -6};
    p.body.height = w.terrain.height(p.body.p) + .85;
    p.body.v = {};
    w.anchor_walk(p);
    w.hailers = {};
    w.nextHail = 0;
    w.updateHailers();
    const auto *hail = w.hailOf(actor);
    check(hail && hail->posted && std::abs(hail->spot.x - stop.x - 1.3) < 1e-9 &&
              hail->spot.z - stop.z > .29 && hail->spot.z - stop.z < 1.21 &&
              w.layout.roadDistance(hail->spot) > w.layout.roadHalf() + .3,
          "nobody at the bus stop hails the taxi");
    const Vec2 spot = hail->spot;
    for (int i = 0; i < 60 * 12; ++i)
        w.step({}, 1. / 60);
    check(w.hailOf(actor) && (p.body.p - spot).length() < .4 && p.body.v.length() < .05 &&
              std::abs(p.heading - pi / 2) < 1e-6,
          "the hailer does not wait at the kerb of the stop, facing the road");
    w.save(path);
    World restored;
    restored.load(path);
    std::filesystem::remove(path);
    check(restored.hailOf(actor) && restored.hailOf(actor)->posted &&
              (restored.hailOf(actor)->spot - spot).length() < 1e-9,
          "the place of a hailer is lost in a save");
    // With the stops out of reach somebody at a kerb does it, as before.
    w.hailers = {};
    for (int i = 0; i < int(w.people.size()); ++i)
        for (Vec2 other : w.busStops)
            if (std::abs(w.people[i].body.p.x - other.x) < 1.5 &&
                std::abs(w.people[i].body.p.z - other.z) < 9)
                w.people[i].body.p += Vec2{4, 0};
    w.nextHail = 0;
    w.updateHailers();
    check(w.hailers.front().actor >= 0 && !w.hailers.front().posted,
          "nobody hails a taxi away from the bus stops");
}

int main()
{
    auto path =
        std::filesystem::temp_directory_path() /
        std::format("promzona-jobs-{}.json", std::chrono::steady_clock::now().time_since_epoch().count());
    try
    {
        careerSeries(path);
        consoleAndHail(path);
        hailAtStop(path);
        shopAndGarage(path);
        peaceContracts(path);
        peacePlaces(path);
        passengerContracts(path);
        World w;
        for (auto seed : {20261105u, 42u, 987654u})
        {
            w.generate(seed);
            test(w);
        }
        auto armed = std::ranges::find_if(w.people, [](const Person &p)
                                          { return !p.dead && p.sidearm != WeaponId::none; });
        check(armed != w.people.end(), "city has armed civilians");
        auto index = size_t(armed - w.people.begin());
        armed->alertUntil = w.time + 5;
        armed->aimSeconds = .7;
        armed->shotAt = w.time + .3;
        w.energySeconds = 17;
        w.save(path);
        World resumed;
        resumed.load(path);
        check(resumed.people[index].sidearm == armed->sidearm && resumed.people[index].aimSeconds == .7 &&
                  resumed.people[index].shotAt == armed->shotAt &&
                  resumed.people[index].alertUntil == armed->alertUntil,
              "civilian weapon and interrupted firing cadence survive save");
        check(resumed.job.actor == w.job.actor && resumed.job.destination == w.job.destination &&
                  resumed.energySeconds == 17 && resumed.jobsCompleted == 4,
              "active contract and energy survive save");
        nlohmann::json j;
        {
            std::ifstream in(path);
            in >> j;
        }
        auto old = j;
        old["format"] = "promzona-cpp-5";
        old.erase("ownedGarage");
        for (auto &c : old["cars"])
        {
            c.erase("paint");
            c.erase("personal");
        }
        for (auto &p : old["people"])
            p.erase("defence");
        auto &pickups = old["pickups"];
        auto first = std::remove_if(pickups.begin(), pickups.end(),
                                    [](const auto &p) { return p.at("energy") == true; });
        pickups.erase(first, pickups.end());
        for (auto &p : pickups)
            p.erase("energy");
        {
            std::ofstream out(path);
            out << old;
        }
        resumed.load(path);
        check(resumed.people[index].sidearm == armed->sidearm && resumed.people[index].alertUntil == 0,
              "legacy civilians receive deterministic equipment without aggression");
        check(resumed.job.kind == JobKind::none && resumed.energySeconds == 0 &&
                  std::ranges::count_if(resumed.pickups, [](const auto &p) { return p.energy; }) == 48,
              "v5 save upgrades with fresh energy pickups");
        j["job"]["actor"] = 999999;
        {
            std::ofstream out(path);
            out << j;
        }
        bool rejected = false;
        try
        {
            resumed.load(path);
        }
        catch (const std::exception &)
        {
            rejected = true;
        }
        check(rejected && resumed.job.kind == JobKind::none, "invalid target rejected atomically");
        std::filesystem::remove(path);
        std::println("Native services, contracts and save migration passed");
        return 0;
    }
    catch (const std::exception &e)
    {
        std::filesystem::remove(path);
        std::println(stderr, "{}", e.what());
        return 1;
    }
}
