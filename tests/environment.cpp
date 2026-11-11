#include "promzona/world.hpp"
#include "promzona/sun_shadow.hpp"
#include "promzona/minimap.hpp"
#include "promzona/control_hints.hpp"
#include "promzona/hud_damage.hpp"
#include "promzona/visibility.hpp"
#include <iostream>
#include <map>
#include <stdexcept>
using namespace pz;
namespace
{
int checks{};
void check(bool ok, const char *message)
{
    ++checks;
    if (!ok)
        throw std::runtime_error(message);
}
Vec3 project(const Mat4 &m, Vec3 p)
{
    return {m.m[0] * p.x + m.m[4] * p.y + m.m[8] * p.z + m.m[12],
            m.m[1] * p.x + m.m[5] * p.y + m.m[9] * p.z + m.m[13],
            m.m[2] * p.x + m.m[6] * p.y + m.m[10] * p.z + m.m[14]};
}
void shadows()
{
    for (auto sun : {Vec3{1, 1, .3f}, Vec3{0, 1, 0}, Vec3{-1, .12f, .3f}, Vec3{1, -1, 0}})
    {
        Vec3 focus{127, 19, -235};
        auto s = sunShadow(focus, sun);
        for (auto x : s.matrix.m)
            check(std::isfinite(x), "finite shadow matrix including zenith");
        auto centre = project(s.matrix, s.centre);
        check(centre.length() < .0001f, "shadow volume centred on focus");
        auto toward = project(s.matrix, s.centre + sun.unit() * 20);
        check(toward.z < centre.z, "occluder toward sun has smaller depth");
        check(std::abs(toward.x) < .0001f && std::abs(toward.y) < .0001f, "sun rays share shadow texel");
        Frustum f(s.matrix);
        check(f.sphere(focus, 1), "focus inside shadow volume");
        check(!f.sphere(focus + sun.unit() * 600, 1), "outside light depth rejected");
        check(sun.y < 0 ? s.strength == 0 : s.strength > 0, "night shadow disabled");
        auto repeat = sunShadow(s.centre + Vec3{.001f, .001f, .001f}, sun);
        check(std::abs(repeat.matrix.m[12] - s.matrix.m[12]) < 1e-6f &&
                  std::abs(repeat.matrix.m[13] - s.matrix.m[13]) < 1e-6f,
              "subtexel motion preserves xy projection");
    }
}
void geometry()
{
    for (auto kind : {TreeKind::birch, TreeKind::linden, TreeKind::spruce})
        for (float height : {5.8f, 10.3f})
        {
            TreePlant tree{{17, 4, -18}, height, height * .28f, kind, 123};
            size_t nearTriangles{};
            for (bool far : {false, true})
            {
                TreeMesh m;
                appendTree(m, tree, far);
                size_t triangles{};
                for (const auto &part : m)
                {
                    check(!part.vertices.empty(), "tree has bark and foliage");
                    check(part.indices.size() % 3 == 0, "complete tree triangles");
                    for (auto index : part.indices)
                        check(index < part.vertices.size(), "valid tree index");
                    for (const auto &v : part.vertices)
                    {
                        check(std::isfinite(v.position.x + v.position.y + v.position.z), "finite vertex");
                        check(std::abs(v.normal.length() - 1) < .001f, "unit tree normal");
                        check(v.position.y >= tree.foot.y - .025f &&
                                  v.position.y < tree.foot.y + height * 1.3f,
                              "bounded tree height");
                        check((v.position - tree.foot).length() < height * 1.35f, "bounded crown");
                    }
                    for (size_t k = 0; k < part.indices.size(); k += 3)
                    {
                        auto a = part.vertices[part.indices[k]].position;
                        auto b = part.vertices[part.indices[k + 1]].position;
                        auto c = part.vertices[part.indices[k + 2]].position;
                        check(cross(b - a, c - a).length() > 1e-7f, "nondegenerate tree faces");
                    }
                    triangles += part.indices.size() / 3;
                }
                if (!far)
                    nearTriangles = triangles;
                else
                    check(triangles < nearTriangles * .5, "far tree halves triangle cost");
            }
        }
}
void precipitationAndTracks()
{
    World w;
    w.generate(20261105);
    for(double t : {0., 72., 732., 86401.})
        for(double eye : {-20., 0., 45.})
        {
            double height = rainHeight(.32,t,eye);
            check(height >= eye-8 && height < eye+20, "long-running rain stays in the camera column");
            double follow=rainHeight(.32,t+.001,eye+.05)-height;
            check(std::abs(std::remainder(follow,28.)+.014)<1e-7,"rain falls in world space as camera moves");
        }
    for(const auto &level:w.roadLevels)
    {
        Vec2 centre{level.x,level.centreZ(level.x)};
        check(w.precipitationSurface(centre)>=level.floor(w.terrain,centre)+(level.tunnel?level.clearance:0)-.001,
              "the upper surface shelters traffic");
        if(level.tunnel)
        {
            Vec2 ramp{level.x-85,level.centreZ(level.x-85)};
            check(std::abs(w.precipitationSurface(ramp)-level.floor(w.terrain,ramp))<.01,"rain reaches open cut");
        }
    }
    auto &building=w.buildings.front();
    check(w.precipitationSurface(building.p)>=w.terrain.height(building.p)+building.height-.001,
          "rain stops on building roof");
    Vec2 canal{w.layout.coordinate(w.nodes/3) + 18., w.layout.coordinate(23) + 18.};
    double surfaceY = CityLayout::waterLevel;
    Vec2 east{1,0};
    auto water = w.traceAttack(canal - east * 2., east, surfaceY + 2., -1., 6., -1);
    check(water.kind == HitKind::water && std::abs(water.distance - 2.) < 1e-8,
          "downward ray first hits the water plane, before the canal bed");
    check(w.traceAttack(canal - east * 2., east, surfaceY+2., 0., 3., -1).kind == HitKind::none,
          "horizontal shot above canal stays in air");
    bool bridgeTested = false;
    for (int row = 0; row < w.nodes; ++row)
    {
        Vec2 deck{canal.x, w.layout.coordinate(row)};
        if (!w.layout.bridge(deck)) continue;
        auto hit = w.traceAttack(deck - east, east, w.terrain.height(deck) + 2., -1., 20., -1);
        check(hit.kind != HitKind::none && hit.kind != HitKind::water, "deck intercepts shot above water");
        auto under = w.traceAttack(deck - east, east, surfaceY + 1., -1., 3., -1);
        check(under.kind == HitKind::water && std::abs(under.distance-1.)<1e-8,
              "the space between the canal water and the underside of the bridge is open");
        bridgeTested = true;
        break;
    }
    check(bridgeTested, "found a canal bridge");
    int originalCar = w.controlledCar;
    Vec2 driver = w.cars[originalCar].body.p;
    w.cars[originalCar].body.p = canal;
    auto state = w.random.state;
    for (int i=0; i<100; ++i) w.splash(canal, .35);
    check(w.ripples.size()==96 && w.random.state==state, "local waves bounded and cosmetic RNG independent");
    w.cars[originalCar].body.p = driver;
    w.ripples.clear();
    w.sounds.clear();
    w.particles.clear();
    w.cars.resize(1);
    auto &c=w.cars.front();
    c.body.v={0,10};
    c.braking=true;
    w.rain=0;
    c.body.p={0,0};
    c.body.angle=0;
    w.updateTireMarks({},.016);
    c.body.p.z+=.55;
    w.updateTireMarks({},.016);
    check(w.surfaceMarks.size()==2 && w.surfaceMarks.front().kind==MarkKind::rubber,"both braking wheels mark the road");
    check(std::ranges::count(w.sounds, std::string{"brake"}, &SoundEvent::name) == 1,
          "held braking starts one tyre squeal");
    auto count=w.surfaceMarks.size();
    c.body.p.x+=40;
    w.updateTireMarks({},.016);
    check(w.surfaceMarks.size()==count,"teleport leaves no streak between roads");
    w.time=100.001;
    c.body.v={};
    w.updateTireMarks({},.016);
    check(w.surfaceMarks.empty(),"old marks fade and are retired");
    check(!w.puddles.empty(), "city has standing water even in dry weather");
    auto p = w.puddles.front().centre;
    check(w.inRoadPuddle(p) && !w.inRoadPuddle(p + Vec2{2,0}), "wet contact follows the puddle footprint");
    w.tireContacts.clear();
    w.rain = 0;
    c.braking = false;
    c.body.v = {0,7};
    c.body.p = p + Vec2{c.body.halfW*.83, c.wheelbase*.5};
    w.updateTireMarks(c.body.p,.016);
    check(w.tireContacts.front().wetUntil == w.time + 8., "dry-weather puddle wets the tyres");
    w.updateTireMarks(c.body.p,.016);
    check(std::ranges::count(w.sounds, std::string{"splash"}, &SoundEvent::name) == 1,
          "wet wheels produce one splash on puddle entry");
    for (int n=0; n<16; ++n)
    {
        w.time += .1;
        c.body.p.z += .65;
        w.updateTireMarks(c.body.p,.1);
    }
    check(w.surfaceMarks.size()>=24 && w.surfaceMarks.back().kind==MarkKind::wet,
          "a moving car leaves twin wet tracks beyond the puddle");
    w.time += 8.1;
    c.body.p.z += .65;
    w.updateTireMarks(c.body.p,.1);
    check(!w.tireContacts.front().active, "dry tyres stop painting the road after eight seconds");
    w.surfaceMarks.clear();
    for (int i=0;i<2000;++i) w.addMark({{}, {}, w.time+i, 10., 0, MarkKind::blood});
    check(w.surfaceMarks.size()==1800 && w.surfaceMarks.front().born==w.time+200,"mark memory stays bounded");
}
void hudAndMap()
{
    HudDamage dirty;
    dirty.resize(100, 80);
    using Rect = std::array<int, 4>;
    auto regions = [&](bool current) {
        std::vector<Rect> result;
        dirty.regions(current, [&](int x, int y, int w, int h) { result.push_back({x,y,w,h}); });
        return result;
    };
    dirty.begin();
    dirty.touch(-4,-5,22,8);
    check(regions(true)==std::vector<Rect>{{0,0,18,32}}, "HUD clips negative bounds");
    dirty.begin();
    dirty.touch(40,71,77,45);
    check(regions(false)==std::vector<Rect>{{0,0,18,32}}, "previous HUD cleared before paint");
    check(regions(true)==std::vector<Rect>{{0,0,18,32},{40,64,60,16}}, "moved panels refresh both footprints");
    dirty.begin();
    check(regions(true)==std::vector<Rect>{{40,64,60,16}}, "hidden panels erase their last footprint");
    dirty.begin();
    check(regions(true).empty(), "no dirty strips when idle");
    dirty.touch(0,0,100,80);
    check(regions(true)==std::vector<Rect>{{0,0,100,80}}, "full menu merges adjacent upload bands");

    World world;
    world.generate(42);
    auto offers = [&](const wchar_t *key,const wchar_t *label) {
        auto hints = controlHints(world);
        return std::ranges::any_of(hints,[&](const auto &h){return h.key==key && h.action==label;});
    };
    check(offers(L"A/D",L"Руль"),"driving controls at spawn");
    auto &vehicle = world.cars.at(world.controlledCar);
    check(!offers(L"ЛКМ",L"Огонь вперёд"),"unarmed driver has no forward-fire action");
    world.inventory.give(WeaponId::pistol);
    check(offers(L"ЛКМ",L"Огонь вперёд"),"armed driver can shoot straight ahead");
    world.inventory.clear();
    vehicle.kind=12;
    check(offers(L"Ctrl",L"Снижение") && !offers(L"Space",L"Ручник"),"helicopter shows descent");
    vehicle.kind=9;
    check(offers(L"W/S",L"Вперёд / назад") && !offers(L"A/D",L"Руль"),"tram controls follow the track");
    world.controlledCar=-1;
    check(offers(L"ЛКМ",L"Удар"),"unarmed pedestrian can punch");
    world.playerScooter=0;
    check(offers(L"F",L"Сойти"),"scooter dismount visible");
    world.playerScooter=-1;
    world.drone.active=true;
    check(offers(L"K",L"Вернуться") && !offers(L"J",L"Заказы"),"drone controls take precedence");
    auto clinic=std::find_if(world.sites.begin(),world.sites.end(),[](const auto &s){return s.kind==ServiceKind::clinic;});
    check(clinic!=world.sites.end(), "seed contains a clinic");
    Minimap map;
    map.rebuild(world);
    constexpr int n=Minimap::size;
    std::array<std::uint32_t,n*n> pixels{};
    map.copyView(clinic->entrance,pixels);
    check(pixels[n/2*n+n/2]==0x78cdf9, "centred clinic marker aligns with the player");
    Vec2 delta{12,22}, dot=Minimap::offset(delta);
    check(dot.x<0 && dot.z<0, "north is up and the camera's right is east on the map");
    map.copyView(clinic->entrance-delta,pixels);
    check(pixels[(n/2+int(std::lround(dot.z)))*n+n/2+int(std::lround(dot.x))]==0x78cdf9,
          "moving player keeps cached map and HUD markers aligned");
    for (int side = 0; side < 4; ++side)
        for (int inland = 2; inland <= 24; inland += 2)
        {
            const Vec2 shore = CityLayout::direction(side) * (world.layout.edge() - inland);
            // Atlas texels correspond to rounded world coordinates.
            const double x = std::lround(shore.x * Minimap::scale) / Minimap::scale;
            const double z = std::lround(shore.z * Minimap::scale) / Minimap::scale;
            map.copyView(shore, pixels);
            const bool submerged = world.terrain.bedHeight(x, z) <= CityLayout::waterLevel;
            check(pixels[n/2*n+n/2] == (submerged ? 0x2b5565u : 0xab976cu),
                  "minimap coastline follows the physical waterline on each shore");
        }
    map.copyView({10000,-10000},pixels);
    check(std::all_of(pixels.begin(),pixels.end(),[](auto p){return p==0x2b5565;}),"outside atlas draws open water");
}
void streetFixtures()
{
    World world;
    world.generate(20261105);
    using Kind = World::FixtureKind;
    for (auto [kind, mass] : {std::pair{Kind::bin,22.}, {Kind::hydrant,65.}, {Kind::dumpster,110.},
                             {Kind::bench,80.}, {Kind::phoneBooth,180.}, {Kind::drinkMachine,140.},
                             {Kind::mailbox,28.}, {Kind::bag,.08}, {Kind::bottle,.4}, {Kind::packet,.06},
                             {Kind::can,.06}, {Kind::garbageBag,5.}})
    {
        auto found=std::ranges::find(world.fixtures,kind,&World::StreetFixture::kind);
        check(found!=world.fixtures.end(), "generated map has each original street fixture");
        const auto &b=world.staticBodies[found->body];
        check(std::abs(1/b.invMass-mass)<.001 && b.sleeping, "correct sleeping fixture mass");
    }
    size_t index=size_t(std::ranges::find(world.fixtures,Kind::hydrant,&World::StreetFixture::kind)-world.fixtures.begin());
    auto &f=world.fixtures[index];
    auto &b=world.staticBodies[f.body];
    // Drive across the hydrant using the same dynamic OBB and contact solver as the game.
    Body car=world.cars.at(0).body;
    car.angle=0;
    car.p=f.home-Vec2{0,car.halfH+1};
    car.v={0,13};
    car.height=b.height+.25;
    car.wake();
    world.nearbyStaticBodies={f.body};
    std::array<Body*,2> contacts={&b,&car};
    for(int i=0;i<35;++i)
    {
        world.time+=1./60;
        world.physics.step(contacts,1./60);
        world.updateFixtures(f.home,1./60);
    }
    check(f.displaced && (b.p-f.home).length()>1 && f.sprayUntil>world.time+15, "impact frees hydrant and opens water at its home");
    // Loose bags drift near the player even before the first impact. Distant bags stay dormant.
    auto bag=std::ranges::find_if(world.fixtures,[&](const auto &other) {
        return other.kind==Kind::bag && (other.home-f.home).length()<135;
    });
    check(bag!=world.fixtures.end(),"bag near hydrant");
    auto &bagBody=world.staticBodies.at(bag->body);
    auto distant=std::find_if(bag+1,world.fixtures.end(),[&](const auto &other) {
        return other.kind==Kind::bag && (other.home-bag->home).length()>160;
    });
    check(distant!=world.fixtures.end(),"remote bag generated");
    world.nearbyStaticBodies={bag->body,distant->body};
    for (int i=0;i<120;++i)
    {
        std::array<Body*,1> local{&bagBody};
        world.time+=1./60;
        world.physics.step(local,1./60);
        world.updateFixtures(bag->home,1./60);
    }
    check(bag->displaced && (bagBody.p-bag->home).length()>.2 && !distant->displaced,
          "wind affects nearby litter, remote litter stays put");
    // Query across a block boundary after an object is knocked out of its home cell.
    bagBody.p.x+=52;
    std::vector<size_t> nearby;
    size_t bagId=size_t(bag-world.fixtures.begin());
    world.fixturesNear(bagBody.p,.05,nearby);
    check(std::ranges::count(nearby,bagId)==1,"moved fixture indexed once at actual position");
    world.fixturesNear(bag->home,.05,nearby);
    check(std::ranges::count(nearby,bagId)==0,"no phantom fixture at old position");
    // Drop a can into deep water. Submerged horizontal motion has water drag on
    // top of the normal contact friction; remaining submerged emits no more entries.
    auto can = std::ranges::find(world.fixtures, Kind::can, &World::StreetFixture::kind);
    auto &canBody = world.staticBodies[can->body];
    Vec2 canal{world.layout.coordinate(world.nodes/3)+18., world.layout.coordinate(23)+18.};
    auto oldPlayer = world.position();
    world.cars[world.controlledCar].body.p = canal;
    canBody.p = canal;
    canBody.v = {4,0};
    canBody.height = CityLayout::waterLevel + 1.;
    canBody.wake();
    world.nearbyStaticBodies = {can->body};
    world.updateFixtures(canal, .1);
    check(world.ripples.size()==1 && canBody.v.length()<4.*std::exp(-2.*.1),
          "a loose object splashes and slows as it enters the canal");
    world.updateFixtures(canal, .1);
    check(world.ripples.size()==1, "submerged litter never splashes every frame");
    world.cars[world.controlledCar].body.p = oldPlayer;
    world.nearbyStaticBodies={f.body};
    auto saved=std::filesystem::temp_directory_path()/"promzona-fixture-test.json";
    world.save(saved);
    World restored;
    restored.load(saved);
    std::filesystem::remove(saved);
    restored.fixturesNear(bagBody.p,.05,nearby);
    check(std::ranges::count(nearby,bagId)==1 && restored.fixtures[bagId].kind==Kind::bag,
          "a moved lightweight prop survives save/load and spatial query");
    auto &other=restored.fixtures.at(index);
    check(other.displaced && (restored.staticBodies.at(other.body).p-b.p).length()<.001 &&
          std::abs(other.sprayUntil-f.sprayUntil)<.001, "hydrant position and water deadline survive load");
    for(int i=0;i<1100;++i)
    {
        world.time+=1./60;
        world.physics.step(std::span<Body*>(&contacts[0],1),1./60);
        world.updateFixtures(f.home,1./60);
    }
    check(f.displaced && f.lift==0 && world.time>f.sprayUntil, "jet ends and hydrant settles near player");
    auto before=b.p;
    car.p=b.p-Vec2{0,car.halfH+1}; car.v={0,11}; car.height=b.height+.25; car.wake();
    for(int i=0;i<40;++i)
    {
        world.physics.step(contacts,1./60);
        world.updateFixtures(f.home,1./60);
    }
    check((b.p-before).length()>.8 && world.time>f.sprayUntil, "second impact moves wreckage without reopening the pipe");
    world.time+=2;
    world.updateFixtures(f.home+Vec2{250,250},1./60);
    check(!f.displaced && b.sleeping && (b.p-f.home).length()<.001 && f.sprayUntil==0, "unseen fixture is restored once");
}

void shoreEntry()
{
    World w;
    w.generate(20261105);
    w.controlledCar = -1;
    Vec2 shallows{0, w.layout.edge()};
    while (shallows.z > w.layout.edge() - 45 &&
           w.terrain.height(shallows) < CityLayout::waterLevel - .4)
        shallows.z -= .1;
    const double bed = w.terrain.height(shallows);
    check(bed > CityLayout::waterLevel - .5 && bed < CityLayout::waterLevel - .2,
          "found a shallow shelf inside the playable beach");
    w.player.p = shallows;
    w.player.height = bed + .85;
    w.player.v = {};
    w.player.roadLevel = 0;
    w.step({}, 1./120);
    check((w.position()-shallows).length()<.2, "wading at the beach does not trigger a clinic respawn");
    Vec2 canal{w.layout.coordinate(w.nodes/3)+18., w.layout.coordinate(23)+18.};
    w.player.p = canal;
    w.player.height = w.terrain.height(canal) + .85;
    w.wanted = 2.;
    w.step({}, 1./120);
    check((w.position()-canal).length()>5 && w.wanted==0 && !w.ripples.empty() &&
          (w.ripples.back().centre - canal).length() < .1,
          "deep water sends the player to the clinic and leaves a splash at the point of entry");
    w.time += WaterRipple::lifetime + .1;
    w.step({}, 1./120);
    check(w.ripples.empty(), "expired water rings are removed");
}

void placement()
{
    for (auto seed : {20261105u, 123u, 878787u})
    {
        World world;
        world.generate(seed);
        auto repeat = planTrees(world);
        check(world.trees.size() > 50, "city contains vegetation");
        check(world.trees.size() == repeat.size(), "deterministic tree count");
        std::map<unsigned,const Body*> ids;
        for (const auto &b : world.staticBodies)
            check(ids.emplace(b.id,&b).second, "unique static body IDs");
        for (const auto &fixture : world.fixtures)
        {
            if (fixture.kind < World::FixtureKind::bench) continue;
            Vec2 p = fixture.home;
            check(world.layout.urbanSite(p,.2), "street props stay on dry urban ground");
            check(world.layout.roadDistance(p) > world.layout.roadHalf(), "furniture clears traffic lanes");
            for (const auto &level : world.roadLevels)
                check(!level.reserved(p,.3), "furniture clears bridge and tunnel approaches");
            if ((fixture.body % 31) == 0)
                check(!world.insideBuilding(p, world.terrain.height(p) + .3, .10), "street prop clears walls");
        }
        for (const auto &puddle : world.puddles)
            check(world.layout.urbanSite(puddle.centre, RoadPuddle::radiusX) &&
                  world.layout.roadDistance(puddle.centre) < world.layout.roadHalf() &&
                  !world.levelAt(puddle.centre, RoadPuddle::radiusX), "puddles lie on open streets");
        std::array<int, 3> kinds{};
        for (size_t k = 0; k < world.trees.size(); ++k)
        {
            const auto &t = world.trees[k];
            Vec2 p{t.foot.x, t.foot.z};
            ++kinds[size_t(t.kind)];
            check((t.foot - repeat[k].foot).length() < 1e-5f && t.seed == repeat[k].seed,
                  "stable tree identity");
            check(world.layout.urbanSite(p, t.radius - .001), "tree away from coast and river");
            check(world.layout.roadDistance(p) >= world.layout.roadHalf() + 3.79,
                  "sidewalk and road clearance");
            check(std::abs(t.foot.y - world.terrain.height(p) + .06) < .002, "trunk planted on terrain");
            for (const auto &b : world.buildings)
                check(std::abs(p.x - b.p.x) >= b.half.x + t.radius + 1.39 ||
                          std::abs(p.z - b.p.z) >= b.half.z + t.radius + 1.39,
                      "crown clears buildings");
            for (const auto &s : world.roadLevels)
                check(!s.reserved(p, t.radius + 1.99), "bridge and tunnel approaches clear");
            for (const auto &b : world.layout.furniture)
                check((p - b.p).length() >= 1.799, "tree clears street furniture");
            check(ids.contains(0x40000000u + unsigned(k)), "trunk has fixed collider");
            const auto &body = *ids.at(0x40000000u + unsigned(k));
            check(body.circle && body.invMass == 0 && (body.p - p).length() < .001, "collider matches trunk");
        }
        for (int count : kinds)
            check(count > 0, "all tree species represented");
        std::cout << "seed " << seed << ": " << world.trees.size() << " trees\n";
    }
}
} // namespace
int main()
{
    try
    {
        shadows();
        hudAndMap();
        precipitationAndTracks();
        shoreEntry();
        streetFixtures();
        geometry();
        placement();
        std::cout << checks << " environment checks passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
