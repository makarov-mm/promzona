#pragma once
#include "physics.hpp"
#include "surface_effects.hpp"
#include <deque>
#include <set>
#include "combat.hpp"
#include "jobs.hpp"
#include "transit.hpp"
#include "layout.hpp"
#include "road_levels.hpp"
#include "vegetation.hpp"
#include <cstdint>
#include <filesystem>
#include <functional>
#include <utility>

namespace Promzona
{
    class Terrain
    {
        int size_{};
        double origin_{};
        std::vector<float> heights_;
        int nodes_{33};
        bool wide_{}, rivers_{};
        std::array<bool, 98> bridges_{};
        double river_distance(double x) const;
        bool bridge(Vec2 p) const;

      public:
        void generate(std::uint32_t seed, int nodes, std::span<const Building> buildings, bool wide = false, const CityLayout *layout = nullptr);
        double bed_height(double x, double z) const;
        double height(double x, double z) const;

        double height(Vec2 p) const
        {
            return height(p.x, p.z);
        }

        Vec2 gradient(Vec2 p) const
        {
            return {
                .x = height(p.x + .5, p.z) - height(p.x - .5, p.z), 
                .z = height(p.x, p.z + .5) - height(p.x, p.z - .5)
            };
        }
    };

    class Clock
    {
        double phase_{};

    public:
        double hour{7}, cycleMinutes{20};
        Clock() { set_hour(7); }
        void set_hour(double h);
        void step(double dt);
        double sun_height() const { return std::sin((hour - 6) * pi / 12); }
        double daylight() const { return smooth((sun_height() + .12) / .38); }
    };

    struct CivilianCover
    {
        Vec2 hide, peek, threat, vehicleAnchor;
        double searchAt{}, holdUntil{};
        int vehicle{-1};
        bool found{}, peeking{};
    };
    struct Person
    {
        Body body;
        Vec2 target;
        int node{}, next{};
        // Port of the managed I, J, Sx, Sz: the corner of the junction `node` a walk starts
        // from and the corner of `next` it leads to -- east or west of the one street, north or
        // south of the other. `lastWalk` is the kind of leg walked last: nobody turns straight
        // back. With nothing to walk, `walkWait` is when to look again.
        int sx{1}, sz{1}, nextSx{1}, nextSz{1}, lastWalk{-1};
        double walkWait{};
        // A company walks its leader's way.
        void walkLike(const Person &lead)
        {
            node = lead.node;
            next = lead.next;
            target = lead.target;
            sx = lead.sx;
            sz = lead.sz;
            nextSx = lead.nextSx;
            nextSz = lead.nextSz;
            lastWalk = lead.lastWalk;
            walkWait = lead.walkWait;
        }
        int riding{-1}, seat{}, alight{};
        int ownedScooter{-1};
        bool courier{}, inCompany{};
        // The response forces of the managed game wear their own colours.
        enum class Uniform
        {
            none,
            medical,
            police,
            rosguard,
            fsb,
            army
        } uniform{};
        bool officer() const { return uniform >= Uniform::police; }
        // A habit that keeps a person about one place: a towel on the beach, a night shift at
        // the kerb, a homeless corner. Port of the managed CityTraffic.Roles.
        enum class Role
        {
            none,
            beach,
            night,
            vagrant
        } role{};
        Vec2 roleHome{};
        Vec2 incident{}, danger{};
        double watchUntil{}, voiceAt{}, idleVoiceAt{}, fleeUntil{};
        double deliveryDistance{}, deliveryUntil{};
        double boardAfter{}, waitUntil{};
        int waitingLine{-1}, waitingStop{-1};
        Vec2 platformTarget{};
        bool dead{};
        double rescueAfter{};
        // A survivor of a car hit lies where it fell, then limps about that spot.
        double knockedUntil{}, injuredUntil{};
        Vec2 injuryHome{};
        // Walking and getting nowhere: for how long, and the way round that follows.
        double blockedFor{}, roundUntil{};
        int roundSide{};
        Vec2 roundFrom{};
        // Time of death; a body nobody treats is replaced by a new passer-by out of view.
        double diedAt{};
        bool drowned{};
        double fear{}, heading{}, phase{}, distance{}, health{1}, burnUntil{};
        bool burnPlayerCaused{};
        WeaponId sidearm{};
        CivilianCover cover;
        double alertUntil{}, aimSeconds{}, shotAt{};
        int burstLeft{};
        static WeaponId civilianWeapon(std::uint32_t citySeed, std::uint32_t id);
        // Fourteen street types; four of them are the managed personas with manners of their own.
        std::uint32_t style{};
        bool elder() const { return style == 2 || style == 8; }
        bool bagElder() const { return style == 8; }
        bool drunk() const { return style == 9; }
        bool homeless() const { return style == 10; }
    };
    struct Scooter
    {
        static constexpr int free = -1, playerRider = -2;
        Body body;
        int owner{-1}, rider{free};
        double returnAt{};
    };
    enum class Species
    {
        cat,
        dog,
        pigeon,
        bird
    };
    struct Animal
    {
        Species species{};
        int variant{}, flock{-1};
        Vec2 p, goal, velocity, offset;
        double altitude{}, heading{}, distance{}, fear{}, thinkAt{}, deadUntil{};
        // Thrown by a car: stunned until then, and the vertical speed of the flight. Not kept
        // in a save; a body in the air simply comes down.
        double knockedUntil{}, rise{};
    };
    struct Flock
    {
        Vec2 landing;
        bool flying{};
        double landedAt{};
    };
    struct Pickup
    {
        Vec2 p;
        WeaponId weapon{};
        bool health{};
        double wait{};
        bool energy{}, dropped{};
        int roadLevel{};
    };
    // The particles of the managed game (CityParticle.Kind) move and fade each in their own
    // way: smoke rises and spreads, the rest falls and bounces off the ground. `plain` is a
    // coloured point that only falls -- the tracers.
    enum class ParticleKind : std::uint8_t
    {
        plain,
        spark,
        smoke,
        debris,
        water,
        fire,
        sand,
        flame
    };
    struct Particle
    {
        Vec3 p, v, color;
        float life{}, size{};
        ParticleKind kind{ParticleKind::plain};
        // The life it began with and the surface under it.
        float span{}, floor{-1e9f};
    };
    struct PlayerInput
    {
        Input car{};
        Vec2 walking{};
        bool running{}, shooting{};
        double collective{};
        double lookHeading{}, lookPitch{};
    };
    struct ReconDrone
    {
        bool active{};
        Vec2 position{}, velocity{}, obstacleFocus{};
        double altitude{}, heading{}, pitch{}, battery{}, health{}, scanTime{}, readyAt{}, nextShot{};
        std::array<size_t, 3> targets{};
        int scanned{};
        std::vector<size_t> obstacles;
    };
    struct SoundEvent
    {
        std::string name;
        Vec2 source{};
        double gain{1};
        bool ui{};
        int speaker{-1};
    };
    // How many cars and passers-by a new city gets. `low` is the population of every save
    // made before the setting existed. `high` is the managed game's first fill; `full` is
    // what that game then keeps on the streets: half as many passenger cars again and a
    // tenth on top of every kind, and people up to its target (CityTraffic.Population.cs).
    enum class Density
    {
        low,
        medium,
        high,
        full,
        // Beyond the managed game: a city that looks lived in from a helicopter.
        max
    };
    struct PopulationSize
    {
        size_t cars, people;
    };
    constexpr PopulationSize populationSize(Density density)
    {
        return density == Density::max      ? PopulationSize{4000, 14000}
               : density == Density::full   ? PopulationSize{1485, 5265}
               : density == Density::high   ? PopulationSize{1080, 3240}
               : density == Density::medium ? PopulationSize{540, 1800}
                                            : PopulationSize{260, 720};
    }
    // What an imported managed (C#) save carried over; see World::importManaged.
    struct ManagedImport
    {
        int format{}, roadRevision{}, weapons{};
        std::uint32_t seed{};
        bool samePlan{}, positionKept{};
        // The player was driving; the vehicle of that kind and its damage were carried over.
        bool driving{}, carKept{};
        // On the same plan: cars of the traffic and passers-by put back where they were.
        int cars{}, people{};
    };
    struct World
    {
        static constexpr int nodes = 49;
        static constexpr double spacing = 36, landEdge = (nodes - 1) * spacing * .5 + 54;
        std::uint32_t seed{20261105};
        Random random;
        CityLayout layout;
        Terrain terrain;
        std::vector<RoadLevel> roadLevels;

        // The cells of the city a road level comes near. Nearly every actor stands in a cell
        // none comes near, and is spared the search through the levels that levelAt would
        // otherwise make for it on every step. Built with the levels in generate().
        struct LevelCells
        {
            // The side of a cell, and how far beyond a level a question may reach.
            static constexpr double cell = 8, margin = 2;
            double x{}, z{};
            int columns{}, rows{};
            std::vector<char> occupied;
            void build(std::span<const RoadLevel> levels);
            // No level holds `p`, whatever margin up to `margin` it is asked with.
            bool none_near(Vec2 p) const;
        } level_cells;

        Clock clock;
        bool dayNightCycle{true};
        double simulationSpeed{1};
        Inventory inventory;
        std::vector<Building> buildings;
        std::vector<std::vector<unsigned>> roofCells;
        void index_roofs();
        double flightFloor(Vec2 pos) const;
        double precipitationSurface(Vec2 pos) const;
        double highestRoof(Vec2 pos, double floor, double margin) const;
        bool inside_building(Vec2 pos, double y, double radius) const;
        std::vector<TreePlant> trees;
        std::vector<Car> cars;
        std::vector<Person> people;
        std::vector<Pickup> pickups;
        std::vector<Scooter> scooters;
        std::vector<Animal> animals;
        std::vector<Flock> flocks;
        std::vector<Vec2> habitat;
        Random streetRandom;
        double animalClock{};
        int playerScooter{-1};
        enum class StreetOffer
        {
            none,
            gift,
            taxi,
            patient
        };
        struct Conversation
        {
            int actor{-1};
            double nextLine{}, pauseUntil{}, offerUntil{};
            StreetOffer offer{};
        } conversation;
        bool talk();
        void generate_street_life();
        void form_companies(bool loading);
        void social_intent(Person &person, Vec2 &direction, double &speed);
        // Dry land off the canal banks, and the bridges.
        bool dryGround(Vec2 at) const;
        // How far a walker who is held up turns aside, in radians.
        static constexpr double roundTurn = 1.4;
        void goRound(Person &person, Vec2 dir);
        // Walkers keep out of each other's way; `afoot` is everybody walking near the player.
        void passWalkers(const Person &person, const std::vector<const Person *> &afoot, Vec2 &direction,
                         double speed) const;
        void scare_person(Person &person, Vec2 origin, double fear);
        void releaseScooter(int index, Vec2 velocity);
        int nearby_scooter() const;
        bool use_scooter();
        void rider_intent(Person &person, Vec2 &direction, double &speed);
        void update_scooters(double dt);
        void step_wildlife(double dt);
        void scare_wildlife(const Vec2& origin, double height, double lethalRadius);
        // A place for an animal, and whether it is a pavement rather than the lawn of a park.
        struct Perch
        {
            Vec2 at;
            bool pavement{};
        };
        // Where the managed game puts an animal (SpawnAnimal): a block taken at random; in a
        // park somewhere on its lawn, otherwise on the pavement of the street beside the
        // block. `lawn` looks for a park first, as most flocks of pigeons do.
        std::optional<Perch> animal_perch(Random &source, bool lawn) const;
        // The place of the n-th member of a flock around its home: a spiral on a lawn, two
        // files along a pavement.
        static Vec2 flock_offset(int member, bool pavement);
        Perch landing_site(Vec2 near, Vec2 away);
        void take_flight(int flock, const Vec2& from, const Vec2& away);
        std::vector<TransitLine> lines;
        // When the driver of a trolleybus that has left its wires is told so again.
        double wiresNoticeAt{};
        int playerTransitSeat{-1};
        bool transitExitRequested{};
        double nextPlatformScan{};
        bool transitPassenger() const { return controlledCar >= 0 && playerTransitSeat >= 0; }
        int boardableTransit() const;
        bool rideTransit();
        bool leaveTransitAt(const Car &car, int stop);
        void disembarkPlayer(const Car &car, Vec2 at);
        void updatePlatforms();
        static double placeTransitRider(Body &body, const Car &car, int seat);
        std::optional<Vec2> platformExit(const Car &car, int stop) const;
        int freeTransitSeat(const Car &car) const;
        void walkers_near(Vec2 pos, double radius, std::vector<int> &out) const;
        void generateTransit();
        void runTransit(Car &car, Input controls, bool driving, bool local, double dt);
        void alignTram(Car &car, double dt);
        void exchangePassengers(Car &car, int stop);
        void seatPassenger(Person &person);
        std::vector<ServiceSite> sites;
        Job job;
        struct StreetHail
        {
            int actor{-1};
            double until{};
            // Port of the managed MaintainHailers: the place at the kerb of a bus stop where
            // the hailer waits. Without one -- no stop with anybody near it, or an older save --
            // they wait where they stood.
            Vec2 spot{};
            bool posted{};
        };
        const StreetHail *hailOf(int actor) const;
        std::array<StreetHail, 3> hailers{};
        double nextHail{};
        void updateHailers();
        int hailerInReach() const;
        bool waitingForTaxi(int actor) const;
        ReconDrone drone;
        void toggle_drone(double heading);
        void stop_drone(const wchar_t *message, double delay);
        void step_drone(const PlayerInput &input, double dt);
        void refresh_drone_obstacles(Vec2 centre);
        bool droneClear(Vec2 point, double height, double radius) const;
        int jobsCompleted{}, jobsFailed{}, lastPay{}, seriesStage{}, seriesCompleted{};
        std::int64_t jobEarnings{};
        double energySeconds{}, clinicCredit{}, purchaseAt{}, noticeUntil{};
        // When a patient without money is next told why the treatment is slow.
        double clinicPoorAt{};
        std::wstring notice;
        std::vector<Particle> particles;
        static constexpr size_t maxParticles = 5000;
        // Port of CityTraffic.Emit. Effects draw from a sequence of their own (Noise in the
        // managed game): what is seen never changes what happens.
        std::uint32_t effectSeed{4191};
        double effectNoise();
        void emit(Vec3 at, double floor, ParticleKind kind, int count, double speed = 3);
        // The same, `above` the street, deck or tunnel floor of the level.
        void emit(Vec2 at, double above, int level, ParticleKind kind, int count, double speed = 3);
        // The ground, deck or tunnel floor under a point in the air.
        double floorBelow(Vec2 at, double y) const;
        void stepParticles(double dt);
        std::vector<Scuff> scuffs;
        void addScuffs(const Car &car);
        std::deque<SurfaceMark> surfaceMarks;
        std::deque<WaterRipple> ripples;
        void splash(Vec2 point, double strength);
        std::vector<TireContact> tireContacts;
        std::vector<RoadPuddle> puddles;
        std::vector<int> puddleCells;
        void generatePuddles();
        bool inRoadPuddle(Vec2 wheel) const;
        // Holes of block (i, j) are potholes[potholeFirst[b] .. potholeFirst[b + 1]), b = j * (nodes - 1) + i.
        std::vector<Pothole> potholes;
        std::vector<std::uint32_t> potholeFirst;
        int potholeJolts{};
        void generatePotholes();
        void potholeJolt(Car &car);
        void addMark(SurfaceMark mark);
        void updateTireMarks(Vec2 focus, double dt);
        std::vector<Rocket> rockets;
        enum class FixtureKind
        {
            bin,
            hydrant,
            dumpster,
            bench,
            phoneBooth,
            drinkMachine,
            mailbox,
            bag,
            bottle,
            packet,
            can,
            garbageBag,
            count
        };
        struct StreetFixture
        {
            size_t body{};
            Vec2 home;
            FixtureKind kind{};
            double standingHeight{}, lift{}, tilt{}, spin{}, sprayUntil{};
            bool displaced{};
            double homeAngle{};
        };
        std::vector<StreetFixture> fixtures;
        std::vector<int> fixtureByBody;
        double nextFixtureRestore{};
        std::vector<std::vector<size_t>> fixtureCells;
        std::set<size_t> displacedFixtures;
        void registerFixture(Body &body, FixtureKind kind);
        void generateStreetFurniture();
        void fixturesNear(Vec2 centre, double radius, std::vector<size_t> &out) const;
        void updateFixtures(Vec2 focus, double dt);
        void support_fixture(StreetFixture &fixture);
        std::vector<Body> staticBodies;
        std::vector<size_t> nearbyStaticBodies;
        Vec2 staticQueryCentre{};
        size_t staticQueryCount{size_t(-1)};
        Body player;
        int controlledCar{0}, money{};
        double wanted{};
        double health{1}, time{}, shotCooldown{}, wantedCooldown{}, rain{}, rainTarget{}, weatherClock{90};
        // How wet the streets are (Wetness of the managed game): they soak within half a
        // minute of rain and take minutes to dry. Grip and the look of the road follow it,
        // not the rain itself.
        double wetness{};
        // Rain, its target and the wet streets at once, for scenarios and imported games.
        void setWeather(double level) { rain = rainTarget = wetness = level; }
        // The console and the preview key start or stop the rain and hold the weather for a
        // minute and a half (SetRain of the managed game).
        void setRain(bool on)
        {
            rainTarget = on ? 1 : 0;
            weatherClock = 90;
        }
        // Seconds until the weather turns again. As in the managed game a shower lasts 45
        // to 135 seconds and a dry spell two to six minutes. The length depends only on the
        // city and the moment: a game replays its weather, and the main generator is left alone.
        double weatherSpell(bool raining) const;
        // Console cheat of the managed game: the player and the driven vehicle take no damage.
        bool godMode{};
        double playerKnockedUntil{};
        // Death is not a cut: the hero lies where he fell for a few seconds under a hovering
        // camera and a caption, the picture goes dark, and the clinic fades in out of the black.
        static constexpr double deathSeconds = 6, deathFadeSeconds = 1.4, respawnFadeSeconds = 1.8;
        double deathAt{-1}, respawnedAt{-1e9};
        Vec2 deathPlace{};
        double deathHeight{};
        bool dying() const
        {
            return deathAt >= 0;
        }
        // Seconds since the hero died, or a negative number while he lives.
        double deathSince() const
        {
            return dying() ? time - deathAt : -1;
        }
        // How dark the picture is (0 clear .. 1 black) and how drained of colour (0 .. 1).
        double screenDarkness() const;
        double screenDrain() const;
        double lastCrimeAt{-100}, lastPoliceSightAt{-100}, policeSightCheckAt{};
        int pedestrianImpacts{};
        static constexpr double fatalImpactSpeed = 5.5;
        static bool car_touches(const Car &car, Vec2 point, double margin);
        void strike_person(Person &person, const Car &car, double closingSpeed, bool playerDriving);
        // The nearest spot off the carriageway where somebody hurt in the road can wait.
        Vec2 kerbside(Vec2 from) const;
        void strike_player(const Car &car, double closingSpeed);
        bool detect_impact(Person &person, const Car &car, bool playerDriving);
        void detect_player_impact(std::span<const Car *const> traffic);
        void hurt_player(double amount);
        // A bullet or a blast that reaches the player on foot (DamagePlayer): heard as well.
        void wound_player(double amount);
        bool policeSees() const;
        void fade_wanted(double dt);
        // Depth of the canal or the sea under a body whose lowest point is at `feet`;
        // zero on dry land and on a bridge deck.
        double waterDepth(Vec2 point, double feet) const;
        bool outOfSight(Vec2 point) const;
        double nextWaterCheck{}, nextRenewal{};
        size_t renewalCursor{};
        int renewedPeople{}, renewedCars{}, drownings{}, sunkCars{}, clearedJams{};
        void waterStep();
        void sinkCar(Car &car);
        void drownPerson(Person &person);
        bool personEngaged(int actor) const;
        bool carEngaged(int car) const;
        bool renewPerson(int actor);
        bool renewCar(int car);
        void renewCity();
        // Unhurried and frightened speed of a walker, metres per second.
        static double walking_pace(const Person &person);
        static double fleeing_pace(const Person &person);
        double nextRoleStep{};
        size_t roleCursor{};
        int rolesTaken{}, rolesLeft{}, dutiesEnded{};
        // Its own sequence: habits must not change the seeded traffic or the wildlife.
        Random roleRandom;
        Vec2 towel{};
        int towelGuests{};
        size_t role_quota(Person::Role role) const;
        bool role_ground(Person::Role role, Vec2 at) const;
        bool role_candidate(int actor) const;
        bool take_role(int actor, Person::Role role, bool unseen);
        void leave_role(Person &person);
        void anchor_walk(Person &person) const;
        // The pavements run 9.1 m either side of the middle of a street, and people walk from
        // corner to corner of the junctions: across a street, or along the block to the next.
        static constexpr double walkLine = 9.1;
        static Vec2 corner(int node, int sx, int sz)
        {
            return nodePoint(node) + Vec2{sx * walkLine, sz * walkLine};
        }
        // Which legs can be walked at each junction. Every kind of leg has two bits: the lower
        // for the south or west side, the upper for the north or east side.
        enum class WalkLeg : std::uint8_t
        {
            acrossNorthSouth = 1 << 0, // across the north-south street, on the south or north side
            acrossEastWest = 1 << 2,   // across the east-west street, on the west or east side
            blockEast = 1 << 4,        // the block towards +x, on the south or north pavement
            blockNorth = 1 << 6,       // the block towards +z, on the west or east pavement
        };
        static constexpr std::uint8_t walkLegBit(WalkLeg leg, bool upperSide)
        {
            return static_cast<std::uint8_t>(std::to_underlying(leg) << (upperSide ? 1 : 0));
        }
        std::vector<std::uint8_t> walkLegs;
        void buildWalkLegs();
        bool crossingLights(int node, bool alongX, int side) const;
        bool walk_signal(int node, bool alongX, double seconds) const;
        void choose_walk(Person &person);
        bool place_on_block(Person &person, int from, int to, int side, double along) const;
        static Vec2 walk_aim(const Person &person);
        static Vec2 roadward(Vec2 at);
        void role_intent(Person &person, Vec2 &direction, double &speed) const;
        void settle_roles();
        void step_roles();
        // Seconds a walker has to wait before stepping off the kerb in front of it; zero
        // away from a kerb, at a corner without lights and while the walk signal lasts.
        double kerb_wait(const Person &person) const;
        bool on_crossing(const Person &person, bool caught) const;
        bool firing{}, aiming{};
        double playerBurnUntil{}, fireClock{}, nextFlameSound{};
        double playerDistance{};
        ContactWorld physics;
        std::vector<SoundEvent> sounds;
        Density density{};
        void generate(std::uint32_t newSeed, std::function<void(float)> progress = {},
                      int layoutRevision = CityLayout::districtRevision, Density population = Density::low);
        void step(PlayerInput input, double dt);
        Vec2 position() const { return controlledCar >= 0 ? cars[controlledCar].body.p : player.p; }
        double ground() const
        {
            return controlledCar >= 0 ? cars[controlledCar].body.height - .8 : player.height - .85;
        }
        const RoadLevel *levelById(int id) const;
        const RoadLevel *levelAt(Vec2 p, double margin = 0) const;
        const RoadLevel *levelEdge(int from, int to) const;
        double surface(Vec2 p, int level) const;
        int selectLevel(Vec2 p, int previous, double foot) const;
        bool scenerySolid(Vec2 p, double y, double radius = .03) const;
        bool openCut(Vec2 p, double margin = 0) const;
        // The floor under an actor where it was last supported. The floor of a step ago, and
        // the floor under whoever stands still, are then read back and not worked out again.
        // Not saved.
        struct Footing
        {
            Vec2 at{};
            int level{};
            double floor{};
            bool known{};
        };
        // The slope of the ground under a car, kept the same way for a car that stands.
        struct Slope
        {
            Vec2 at{};
            int level{};
            Vec2 gradient{};
            bool known{};
        };
        std::vector<Footing> car_footing, people_footing;
        std::vector<Slope> car_slope;
        Footing player_footing;
        double floor_under(Vec2 p, int level, Footing *footing) const;
        Vec2 slope_under(Vec2 p, int level, Slope &slope) const;
        void supportCar(Car &car, Vec2 previousPosition, double dt, Footing *footing = nullptr);
        void supportPerson(Body &body, Vec2 previousPosition, double dt, Footing *footing = nullptr);
        void appendLevelContacts(std::uint32_t &id);
        // `source` replaces the city's own sequence where a choice must not disturb it.
        int chooseWalkNext(int n, int previous, Random *source = nullptr);
        std::optional<Vec2> vehicleExit(const Car &car, std::optional<Vec2> toward = {}) const;
        struct VehicleEntry
        {
            int car{-1}, side{};
            double progress{}, deadline{};
        } vehicleEntry;
        static Vec2 driver_door(const Car &car, int side);
        bool vehicleSpace(Vec2 at, double height, const Car *ignore, bool includePlayer) const;
        bool entry_path(const Car &car, Vec2 door) const;
        bool unload_crew(Car &car);
        // Everyone a vehicle carries, driver included, before any of them left it.
        static int crew_size(const Car &car);
        void step_entry(PlayerInput &input, double dt);
        void enter_exit();
        void generateServices();
        void generate_shops();
        // Bus shelters, by their roadside signs: on the west pavement of a street that runs
        // north, half-way along the block. The second list gives the stop of the block that
        // leaves a node towards +z, or -1.
        std::vector<Vec2> busStops;
        std::vector<int> busStopOfNode;
        void generate_bus_stops();
        // The shelter stands behind its sign.
        static constexpr Vec2 shelter(Vec2 sign) { return {sign.x - .8, sign.z}; }
        // A bus at its stop, or pulling up to it: the speed it may still do.
        double busStopLimit(Car &bus);
        // A taxi ride that ends at a bus stop keeps this in place of the number of a transit line.
        static constexpr int busStopRide = -2;
        Vec2 rideEnd(int line, int destination) const;
        // The public places of the peace jobs, as in the managed game: the food kiosks and,
        // numbered on from them, the bus stops.
        bool peacePlace(int index) const
        {
            return index >= 0 && (index < int(sites.size()) ? sites[size_t(index)].food()
                                                            : index < int(sites.size() + busStops.size()));
        }
        Vec2 peacePoint(int index) const
        {
            return index < int(sites.size()) ? sites[size_t(index)].entrance
                                             : busStops[size_t(index) - sites.size()];
        }
        // The managed game marks not the kiosk or the stop itself but the first free point of
        // pavement on three rings of eight about it (CityTraffic.StartPeaceJob).
        static constexpr int peaceSpots = 24;
        static Vec2 peace_offset(int spot);
        Vec2 peaceSpot(int place, int spot) const { return peacePoint(place) + peace_offset(spot); }
        // Dry land of the city, clear of its edge and of the canals by the margin.
        bool peace_ground(Vec2 at, double margin) const;
        int market_in_reach() const;
        int garage_in_reach() const;
        int ownedGarage{-1}, openMarket{-1};
        static constexpr int garagePrice = 400, garageCapacity = 3;
        static constexpr double pillHealing = .35;
        bool spawn_pickup(bool energy);
        int owned_vehicle_count() const;
        int garage_quote() const;
        bool service_garage();
        void use_owned_garage();
        int shop_price(ShopItem item) const;
        bool buy_at_market(ShopItem item);
        int nearestSite(ServiceKind kind, Vec2 from) const;
        int counterInReach(bool parkedAllowed) const;
        bool buyFood();
        void interactJob();
        bool canTakePassengerJob(int actor) const;
        bool startPassengerJob(JobKind kind, int requestedActor = -1);
        void interactPassengerJob();
        void stepPassengerJob(double dt);
        void releasePassenger(const Job &trip, bool delivered);
        static Vec2 passengerDoor(const Car &car, Vec2 from);
        void resumeWalk(Person &person, Vec2 position, int level);
        bool start_peace_job(JobKind kind);
        void interact_peace_job();
        void step_peace_job(double dt);
        bool valid_peace_job() const;
        bool startContract(JobKind kind);
        bool startSeries();
        double deliveryHealth() const { return job.series ? (50 + 10 * seriesStage) / 100. : .5; }
        int expectedPay() const;
        // A job that fails for a reason of its own says so, as the managed game does.
        void finishJob(bool success, const wchar_t *failure = nullptr);
        Vec2 jobTarget() const;
        void servicesStep(double dt);
        void clinicRespawn(Vec2 fallen);
        void beginDeath();
        void finishDeath();
        void notify(std::wstring text);
        bool validJob() const;
        // Ordinary traffic occupies the first slots of `cars`; response and summoned vehicles follow.
        size_t trafficPopulation = populationSize(Density::low).cars;
        static constexpr size_t maxResponseVehicles = 32, maxSpawnVehicles = 8;
        bool spawnVehicle(unsigned model);
        int pursuitNode{-1};
        std::array<int, nodes * nodes> pursuitDistances{};
        double dispatchAt{};
        void updateDispatch();
        enum class PatrolStage
        {
            stopping,
            approaching,
            returning,
            seated
        };
        struct ArrestPatrol
        {
            int car{-1}, officer{-1};
            PatrolStage stage{};
            double hold{}, deadline{};
        } patrol;
        Vec2 stationEntrance{};
        int arrests{}, lastFine{};
        double patrolScanAt{};
        void updatePatrol();
        void officerIntent(Vec2 &direction, double &speed) const;
        // The suspect gives himself up: still on foot with no weapon raised, or stopped in a car.
        bool surrendering() const;
        void arrestStep(double dt);
        std::optional<Vec2> clearEntrance(Vec2 door) const;
        enum class RescueStage
        {
            standby,
            driving,
            care,
            returning
        };
        struct Rescue
        {
            int car{-1}, medic{-1}, patient{-1}, destination{-1};
            RescueStage stage{};
            double deadline{}, treatment{};
            bool braking{};
            std::vector<int> distances;
        };
        std::array<Rescue, 2> rescues{};
        double nextRescueScan{}, nextRescueDispatch{};
        size_t casualtyCursor{};
        const Rescue *ambulanceCall(int car) const;
        const Rescue *paramedic_call(int actor) const;
        void route_ambulance(Rescue &call) const;
        int ambulance_turn(const Rescue &call, int node, int previous);
        void update_rescues();
        void medic_intent(int actor, Vec2 &direction, double &speed) const;
        int pursuitNext(int node, int previous);
        bool deployResponder(int kind);
        void responseFire(Car &car);
        void flyPatrol(Car &car, const PlayerInput &input, double dt);
        // L in a piloted helicopter: descend and brake until the skids touch; ascending cancels.
        bool helicopterLanding{};
        void toggleHelicopterLanding();
        double recoveryReadyAt{};
        bool recover();
        bool safe_recovery_pose(const Car &car, Vec2 at, double heading) const;
        std::optional<Vec2> player_vehicle_exit(const Car &car) const;
        bool safe_exit_point(const Car &car, Vec2 at) const;
        void shoot(double heading, double pitch = 0);
        // Where a shot from the gun of the armoured vehicle the player drives would land, laid
        // on this heading: the point the sight on the screen marks. Empty without such a gun.
        std::optional<Vec3> gun_sight(double heading) const;
        void explode(Vec2 p, double y, double strength = 1, bool vehicleBlast = false, bool playerCaused = false);
        void destroyCar(Car &car);
        void damageCar(Car &car, double amount, bool playerCaused = false);
        void vehicleImpact(Car &car, double speedChange, Vec2 velocityChange = {});
        // A drowning is silent: no blood, no alarmed witnesses and no ambulance call.
        void killPerson(Person &person, Vec2 velocity = {}, bool playerCaused = false, bool drowned = false);
        void bulletSparks(Vec2 from, Vec2 direction, double y, double slope, const AttackHit &hit);
        AttackHit traceAttack(Vec2 from, Vec2 direction, double y, double slope, double range, int ignoreCar = -1,
                              bool includePlayer = false, const Person *shooter = nullptr) const;
        bool blastVisible(Vec2 from, double y, Vec2 to, double targetY) const;
        void combatStep(double dt);
        bool cover_allows_aim(Person &person, std::span<const Car *const> traffic, Vec2 &direction, double &speed) const;
        void civilianCombat(Person &person, double dt);
        void alarmWitnesses(Vec2 origin, double height, bool playerCaused);
        void flame(Vec2 direction, double y);
        void addCrime(double severity);
        void reportCrime(double severity, double radius = 45, bool sight = true);
        int wantedLevel() const { return wanted <= .001 ? 0 : int(std::ceil(wanted)); }
        [[nodiscard]] std::function<void()> captureSave(const std::filesystem::path &path) const;
        void save(const std::filesystem::path &path) const;
        void load(const std::filesystem::path &path);
        ManagedImport importManaged(const std::filesystem::path &file, Density population = Density::low);
        std::wstring command(const std::string &text);
        static int nearest_node(Vec2 p);
        static Vec2 nodePoint(int n)
        {
            return {(n % nodes - (nodes - 1) * .5) * spacing, (n / nodes - (nodes - 1) * .5) * spacing};
        }
        std::vector<int> neighbours(int n) const;
        // The number of neighbours, counted without building their list.
        int degree(int n) const;
        int choose_next(int n, int previous);
        void new_route(Car &car);
        bool intersection_clear(const Car &car) const;
        void plan_exit(Car &car);
        // A car is crossing this junction (the one reservation it has); `except` is not asked.
        bool junction_held(int node, const Car *except) const;
        // Whether two bodies stand on one surface: the same street however steep, not the
        // deck above it or the tunnel below.
        bool on_same_surface(const Body &a, const Body &b) const;
        // Actors are binned by city block for nearby drivers' periodic perception.
        struct TrafficCell
        {
            int car{-1}, person{-1};
        };
        std::vector<TrafficCell> trafficCells;
        std::vector<int> nextTrafficCar, nextTrafficPerson;
        double trafficIndexAt{};
        void index_traffic();
        void senseTraffic(Car &car, bool local);
        void sensePursuit(Car &car, bool local);
        void standDown(Car &car);
        bool rejoinRoad(Car &car);
        bool chaseGround(Vec2 at) const;
        bool tryChangeLane(Car &car, int destination);
        Vec2 pickup_site();
    };
}
