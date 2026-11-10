#include "promzona/world.hpp"
#include "json.hpp"
#include <iostream>
#include <fstream>
#include <chrono>

using namespace pz;
namespace
{
void check(bool ok, const char *message)
{
    if (!ok)
        throw std::runtime_error(message);
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
    w.killPerson(w.people[w.job.actor], {}, false);
    check(w.jobsFailed == 1 && w.job.kind == JobKind::none, "third party kill fails contract");
    check(w.startContract(JobKind::elimination), "new target available");
    w.killPerson(w.people[w.job.actor], {}, true);
    check(w.jobsCompleted == 4 && w.lastPay == 350, "player kill rewarded");
    w.health = .35;
    w.money = 0;
    w.servicesStep(1);
    check(w.health > .35 && w.money == 0, "clinic treats broke player");
    w.money = 100;
    w.startContract(JobKind::elimination);
    w.clinicRespawn(w.sites[clinic].entrance);
    check(w.money == 75 && w.job.kind == JobKind::none &&
              (w.player.p - w.sites[clinic].entrance).length() < 4,
          "nearest clinic after death");
    check(w.startContract(JobKind::vehicle), "contract for save");
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
    visit(w, w.job.destination);
    w.interactJob();
    tick(1.);
    w.player.v = {1, 0};
    tick(.1);
    check(!w.job.route.enabled && w.job.stopped == 0, "walking interrupts handout");
    w.player.v = {};
    for (int i = 0; i < 4; ++i)
    {
        visit(w, w.job.destination);
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
void passengerContracts(const std::filesystem::path &path)
{
    World w;
    auto board = [&](JobKind kind) {
        w.generate(17);
        w.controlledCar = 0;
        check(w.startContract(kind), "passenger contract started");
        auto &p = w.people[w.job.actor];
        auto &car = w.cars[0];
        car.body.v = {};
        car.body.w = 0;
        car.body.angle = 0;
        car.body.p = p.body.p - Vec2{car.body.halfW + 1.25, 0};
        car.body.height = p.body.height - .05;
        w.interactJob();
        check(!w.job.acquired && w.job.ride.car == 0, "passenger directed to a real car door");
        for (int i = 0; i < 100 && !w.job.acquired; ++i) w.step({}, 1./60);
        check(w.job.acquired && p.riding == 0, "passenger seated by the simulation");
    };
    for (auto kind : {JobKind::taxi, JobKind::patient})
    {
        board(kind);
        int passenger = w.job.actor;
        w.save(path);
        World loaded;
        loaded.load(path);
        check(loaded.job.kind == kind && loaded.people[passenger].riding == 0 &&
              (loaded.jobTarget() - w.jobTarget()).length() < .01, "mid-trip save restores passenger and destination");
        auto &car = w.cars[0];
        auto &rider = w.people[passenger];
        if (kind == JobKind::patient) check(rider.health < 1, "patient needs treatment");
        car.body.p = w.jobTarget();
        car.body.v = {};
        car.body.height = w.terrain.height(car.body.p) + .8;
        car.lift = 0;
        car.body.roadLevel = 0;
        w.servicesStep(1.1);
        check(w.job.kind == JobKind::none, "parked passenger trip closes");
        check(rider.riding < 0 && !rider.dead, "passenger walks after drop-off");
        check(w.lastPay > 0, "passenger fare paid");
        if (kind == JobKind::patient) check(rider.health == 1, "clinic treats delivered patient");
    }
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
int main()
{
    auto path = std::filesystem::temp_directory_path() /
                ("promzona-jobs-" +
                 std::to_string(std::chrono::steady_clock::now().time_since_epoch().count()) + ".json");
    try
    {
        peaceContracts(path);
        passengerContracts(path);
        World w;
        for (auto seed : {20261105u, 42u, 987654u})
        {
            w.generate(seed);
            test(w);
        }
        auto armed = std::ranges::find_if(w.people, [](const Person &p) { return !p.dead && p.sidearm != WeaponId::none; });
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
              resumed.people[index].shotAt == armed->shotAt && resumed.people[index].alertUntil == armed->alertUntil,
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
        for (auto &p : old["people"]) p.erase("defence");
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
        std::cout << "Native services, contracts and save migration passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::filesystem::remove(path);
        std::cerr << e.what() << '\n';
        return 1;
    }
}
