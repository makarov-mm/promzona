#include "promzona/world.hpp"
#include <optional>

namespace Promzona
{
    namespace
    {
        // People with a habit pace along one line through their place: so far from it, metres.
        constexpr double reach(Person::Role role)
        {
            return role == Person::Role::beach ? 5 : role == Person::Role::vagrant ? 7 : 2.3;
        }

        // Chased off this far, nobody comes back.
        constexpr double lostDistance = 60;

        // Managed night shift: from nine in the evening until five in the morning.
        constexpr bool nightShift(double hour)
        {
            return hour >= 21 || hour < 5;
        }
    }

    // Managed cruise speeds: pensioners with bags .92, drunks .70, the homeless 1.05 and
    // 1.3--1.7 for everybody else. The native streets are a little slower.
    double World::walking_pace(const Person &p)
    {
        if (p.uniform != Person::Uniform::none)
            return 1.3;
        if (p.drunk())
            return .7;
        if (p.homeless())
            return 1.05;
        if (p.elder())
            return p.bagElder() ? .92 : 1.;
        return 1.15 + (p.body.id % 8) * .05;
    }

    double World::fleeing_pace(const Person &p)
    {
        if (p.uniform != Person::Uniform::none)
            return 3.5;
        return p.elder() ? 2. : p.drunk() ? 2.6 : 3.5;
    }

    // The managed city keeps up to 420 of about five thousand people on the sand and at
    // most forty on the night shift; the same shares of a native population.
    size_t World::role_quota(Person::Role role) const
    {
        return role == Person::Role::beach   ? people.size() / 16
               : role == Person::Role::night ? people.size() / 80
                                             : 0;
    }

    // Ground a person with this habit may stand on: dry, open and off the carriageway;
    // sand of the outer shore for the beach, a pavement inside the street grid otherwise.
    bool World::role_ground(Person::Role role, Vec2 at) const
    {
        const double floor = terrain.height(at), street = layout.roadDistance(at);
        if (floor < CityLayout::waterLevel + .6 || street < layout.roadHalf() + .6 || openCut(at, .5) ||
            levelAt(at, 2) || insideBuilding(at, floor + .85, .4))
            return false;
        if (role != Person::Role::beach)
            return layout.urbanSite(at, .5);
        const double rim = std::max(std::abs(at.x), std::abs(at.z)) - layout.coordinate(nodes - 1);
        return rim > 8 && rim < 48 && layout.riverDistance(at.x) > 30;
    }

    // Somebody with nothing else to do: no vehicle, no errand, no fright and nobody's business.
    bool World::role_candidate(int actor) const
    {
        const auto &p = people[actor];
        return !p.dead && p.riding < 0 && p.role == Person::Role::none && p.uniform == Person::Uniform::none &&
               !p.courier && !p.inCompany && p.ownedScooter < 0 && p.waitingLine < 0 && !p.body.roadLevel &&
               p.fear <= 0 && p.burnUntil <= time && p.alertUntil <= time && p.watchUntil <= time &&
               p.injuredUntil <= time && p.knockedUntil <= time && p.health > .7 && !personEngaged(actor);
    }

    // Whoever has been somewhere else -- on the beach, in a tram -- makes for the nearest corner
    // that leads anywhere and walks on from there.
    void World::anchor_walk(Person &p) const
    {
        const int x = clamp(int(std::round((p.body.p.x - layout.coordinate(0)) / spacing)), 0, nodes - 1);
        const int z = clamp(int(std::round((p.body.p.z - layout.coordinate(0)) / spacing)), 0, nodes - 1);
        int node = z * nodes + x, sx = p.body.p.x < nodePoint(node).x ? -1 : 1,
            sz = p.body.p.z < nodePoint(node).z ? -1 : 1;
        // A corner is somewhere to walk from when a pavement or a crossing ends at it.
        auto leads = [&](int n, int ex, int ez)
        {
            if (walkLegs.size() != size_t(nodes * nodes))
                return true;
            const int i = n % nodes, j = n / nodes;
            const int west = ex > 0 ? n : n - 1, south = ez > 0 ? n : n - nodes;
            using enum WalkLeg;
            return (walkLegs[n] & walkLegBit(acrossNorthSouth, ez > 0)) ||
                   (walkLegs[n] & walkLegBit(acrossEastWest, ex > 0)) ||
                   ((ex > 0 || i > 0) && (walkLegs[west] & walkLegBit(blockEast, ez > 0))) ||
                   ((ez > 0 || j > 0) && (walkLegs[south] & walkLegBit(blockNorth, ex > 0)));
        };
        double nearest = 1e18;
        for (int dz = -1; dz <= 1; ++dz)
            for (int dx = -1; dx <= 1; ++dx)
            {
                const int i = x + dx, j = z + dz;
                if (i < 0 || j < 0 || i >= nodes || j >= nodes)
                    continue;
                for (int ez : {-1, 1})
                    for (int ex : {-1, 1})
                        if (const double d = (corner(j * nodes + i, ex, ez) - p.body.p).length2();
                            d < nearest && leads(j * nodes + i, ex, ez))
                        {
                            nearest = d;
                            node = j * nodes + i;
                            sx = ex;
                            sz = ez;
                        }
            }
        p.node = p.next = node;
        p.sx = p.nextSx = sx;
        p.sz = p.nextSz = sz;
        p.lastWalk = -1;
        p.walkWait = 0;
        p.target = corner(node, sx, sz);
    }

    static_assert(World::walkLegBit(World::WalkLeg::acrossNorthSouth, false) == 0x01 &&
                  World::walkLegBit(World::WalkLeg::acrossEastWest, true) == 0x08 &&
                  World::walkLegBit(World::WalkLeg::blockEast, false) == 0x10 &&
                  World::walkLegBit(World::WalkLeg::blockNorth, true) == 0x80);

    // Which pavements and crossings exist. A pavement runs along either side of every street;
    // a corner is crossed to where there is a street to cross or where the pavement of the
    // other street simply carries on. Ramps cut into the ground and water leave legs out.
    void World::buildWalkLegs()
    {
        walkLegs.assign(size_t(nodes * nodes), 0);
        auto clear = [&](Vec2 a, Vec2 b)
        {
            for (int k = 0; k <= 18; ++k)
                if (const Vec2 at = a + (b - a) * (k / 18.); openCut(at, .5) || !dryGround(at))
                    return false;
            return true;
        };
        for (int n = 0; n < nodes * nodes; ++n)
        {
            const int i = n % nodes, j = n / nodes;
            const bool north = layout.road(i, j, 0), east = layout.road(i, j, 1), south = layout.road(i, j, 2),
                       west = layout.road(i, j, 3);
            using enum WalkLeg;
            std::uint8_t legs = 0;
            for (int side : {-1, 1})
            {
                const bool upper = side > 0;
                if (((upper ? north : south) || (west && east)) &&
                    clear(corner(n, -1, side), corner(n, 1, side)))
                    legs |= walkLegBit(acrossNorthSouth, upper);
                if (((upper ? east : west) || (north && south)) &&
                    clear(corner(n, side, -1), corner(n, side, 1)))
                    legs |= walkLegBit(acrossEastWest, upper);
                if (east && clear(corner(n, 1, side), corner(n + 1, -1, side)))
                    legs |= walkLegBit(blockEast, upper);
                if (north && clear(corner(n, side, 1), corner(n + nodes, side, -1)))
                    legs |= walkLegBit(blockNorth, upper);
            }
            walkLegs[n] = legs;
        }
    }

    // Whether lights stop the cars on the stretch of street a walker crosses: `side` is the
    // side of the junction the crossing lies on, north or south for a walker along x.
    bool World::crossingLights(int node, bool alongX, int side) const
    {
        return layout.degree(node) >= 3 &&
               layout.road(node % nodes, node / nodes, alongX ? (side > 0 ? 0 : 2) : (side > 0 ? 1 : 3));
    }

    // Port of the managed Walk: the cars that cross the walker's way have red and keep it for
    // `seconds`.
    bool World::walk_signal(int node, bool alongX, double seconds) const
    {
        const double phase = CityLayout::signal_phase(node % nodes, node / nodes, time);
        // Cars along z have green for 14..26, cars along x for 0..12.
        const double start = alongX ? 14 : 0, end = alongX ? 26 : 12;
        return !(phase >= start && phase < end) && std::fmod(start - phase + 28, 28.) >= seconds;
    }

    // Port of the managed ChooseWalk. From a corner there are four ways on: across either
    // street, or along either block to the next junction. A street with lights is crossed
    // only while they allow it -- whoever cannot cross walks on instead of waiting -- and
    // nobody turns straight back while there is another way.
    void World::choose_walk(Person &p)
    {
        const int n = p.node, i = n % nodes, j = n / nodes;
        const std::uint8_t here = walkLegs[n];
        // The kerb is a few steps from the corner and nobody steps off it with less than
        // eight seconds left.
        constexpr double start = 9.5;
        using enum WalkLeg;
        int options[4], count = 0;
        if ((here & walkLegBit(acrossNorthSouth, p.sz > 0)) &&
            (!crossingLights(n, true, p.sz) || walk_signal(n, true, start)))
            options[count++] = 0;
        if ((here & walkLegBit(acrossEastWest, p.sx > 0)) &&
            (!crossingLights(n, false, p.sx) || walk_signal(n, false, start)))
            options[count++] = 1;
        if ((p.sx > 0 || i > 0) && (walkLegs[p.sx > 0 ? n : n - 1] & walkLegBit(blockEast, p.sz > 0)))
            options[count++] = 2;
        if ((p.sz > 0 || j > 0) && (walkLegs[p.sz > 0 ? n : n - nodes] & walkLegBit(blockNorth, p.sx > 0)))
            options[count++] = 3;
        if (count > 1)
            count = int(std::remove(options, options + count, p.lastWalk) - options);
        p.next = n;
        p.nextSx = p.sx;
        p.nextSz = p.sz;
        if (!count)
        {
            p.walkWait = time + .15 + random.next() * .3;
            p.target = corner(n, p.sx, p.sz);
            return;
        }
        const int way = options[random.index(count)];
        if (way == 0)
            p.nextSx = -p.sx;
        else if (way == 1)
            p.nextSz = -p.sz;
        else if (way == 2)
        {
            p.next = n + p.sx;
            p.nextSx = -p.sx;
        }
        else
        {
            p.next = n + p.sz * nodes;
            p.nextSz = -p.sz;
        }
        p.lastWalk = way;
        p.walkWait = 0;
        p.target = corner(p.next, p.nextSx, p.nextSz);
    }

    // Puts a walker on the pavement between two neighbouring junctions, on the side asked for
    // if that pavement exists and on the other if it does not.
    bool World::place_on_block(Person &p, int from, int to, int side, double along) const
    {
        if (from == to)
            return false;
        const bool alongX = std::abs(to - from) == 1;
        const int way = to > from ? 1 : -1, low = std::min(from, to);
        for (int s : {side, -side})
        {
            if (!(walkLegs[low] & walkLegBit(alongX ? WalkLeg::blockEast : WalkLeg::blockNorth, s > 0)))
                continue;
            p.node = from;
            p.next = to;
            p.sx = alongX ? way : s;
            p.sz = alongX ? s : way;
            p.nextSx = alongX ? -way : s;
            p.nextSz = alongX ? s : -way;
            p.lastWalk = alongX ? 2 : 3;
            p.walkWait = 0;
            p.target = corner(to, p.nextSx, p.nextSz);
            const Vec2 start = corner(from, p.sx, p.sz);
            p.body.p = start + (p.target - start) * along;
            return true;
        }
        return false;
    }

    // From a place beside a street, the way to its middle.
    Vec2 World::roadward(Vec2 at)
    {
        const Vec2 beside = at - nodePoint(nearestNode(at));
        return std::abs(beside.x) < std::abs(beside.z) ? Vec2{beside.x > 0 ? -1. : 1., 0}
                                                       : Vec2{0, beside.z > 0 ? -1. : 1.};
    }

    // Where a walker heads. Port of the managed walking path, which bows to the right of the
    // straight line between the corners so that people who meet pass each other. Along a block
    // the house fronts stand a hand's breadth from that line: there only whoever has the kerb
    // on the right gives way, by the width of both bows.
    Vec2 World::walk_aim(const Person &p)
    {
        const Vec2 straight = (p.target - p.body.p).unit(), from = corner(p.node, p.sx, p.sz),
                   leg = p.target - from;
        const double length = leg.length();
        if (length < 4 || (p.target - corner(p.next, p.nextSx, p.nextSz)).length2() > .01)
            return straight;
        const Vec2 along = leg / length, right = perpendicular(along);
        const double ahead = dot(p.body.p - from, along) + 2.5;
        if (ahead >= length - .5)
            return straight;
        double bow = .26;
        if (p.node != p.next)
        {
            const Vec2 beside = from - nodePoint(p.node);
            const Vec2 kerb = std::abs(along.x) > std::abs(along.z) ? Vec2{0, -beside.z} : Vec2{-beside.x, 0};
            bow = dot(right, kerb) > 0 ? .52 : 0;
        }

        return (from + along * ahead + right * (bow * std::sin(pi * clamp(ahead / length, 0., 1.))) - p.body.p).unit();
    }

    void World::leave_role(Person &p)
    {
        if (p.role == Person::Role::none)
            return;
        // A night worker and a homeless person are already on a pavement; a beach visitor
        // has to find the nearest corner first.
        if (p.role == Person::Role::beach)
            anchor_walk(p);
        p.role = Person::Role::none;
        p.roleHome = {};
        ++rolesLeft;
    }

    // A walker nobody is looking at becomes a beach visitor or a night worker somewhere
    // else nobody is looking at. The homeless stay where they are: their corner is the
    // place they happen to be.
    bool World::take_role(int actor, Person::Role role, bool unseen)
    {
        auto &p = people[actor];
        if (role == Person::Role::vagrant)
        {
            if (!role_ground(role, p.body.p))
                return false;
            p.role = role;
            p.roleHome = p.body.p;
            ++rolesTaken;
            return true;
        }
        for (int attempt = 0; attempt < 12; ++attempt)
        {
            Vec2 at;
            int from = p.node, to = p.next;
            if (role == Person::Role::beach)
            {
                // Managed spots: 34--44 m inside the land edge on any of the four shores,
                // in loose groups of three about one towel.
                if (towelGuests > 0 && towelGuests < 3 && attempt == 0)
                    at = towel + Vec2{1.2, 1.7} * double(towelGuests);
                else
                {
                    const double along = (roleRandom.next() * 2 - 1) * (landEdge - 45);
                    const double shore = landEdge - 34 - roleRandom.next() * 10;
                    const int side = roleRandom.index(4);
                    at = side == 0   ? Vec2{shore, along}
                         : side == 1 ? Vec2{-shore, along}
                         : side == 2 ? Vec2{along, shore}
                                     : Vec2{along, -shore};
                    towelGuests = 0;
                }
            }
            else
            {
                // A place at the kerb of either pavement, a third to two thirds along the block.
                // Measured along the old walk, which took in the crossing at its end, the place
                // could be the corner of a junction, within reach of every truck that turned there.
                from = roleRandom.index(nodes * nodes);
                to = chooseWalkNext(from, -1, &roleRandom);
                Person spot;
                if (!place_on_block(spot, from, to, roleRandom.index(2) * 2 - 1, .34 + roleRandom.next() * .33))
                    continue;
                at = spot.body.p + roadward(spot.body.p) * 1.1;
            }
            const double centre = terrain.height(at) + .85;
            if ((unseen && !outOfSight(at)) || !role_ground(role, at) ||
                !vehicleSpace(at, centre, nullptr, true) ||
                std::ranges::any_of(people, [&](const Person &other)
                                    { return other.role == role && (other.roleHome - at).length2() < 1; }))
            {
                towelGuests = 0;
                continue;
            }
            p.body.p = at;
            p.body.v = {};
            p.body.w = 0;
            p.body.roadLevel = 0;
            p.body.height = centre;
            p.body.verticalSpeed = 0;
            p.body.wake();
            // Whoever leaves the place later starts from the corner nearest to it.
            anchor_walk(p);
            if (role == Person::Role::beach)
            {
                if (towelGuests == 0)
                    towel = at;
                ++towelGuests;
            }
            p.role = role;
            p.roleHome = at;
            p.heading = roleRandom.next() * 2 * pi;
            ++rolesTaken;
            return true;
        }
        return false;
    }

    // Port of the managed RoleWalk: ten seconds on the spot, then a slow walk along one
    // line through it and back. Fright, a conversation and every errand take precedence.
    void World::role_intent(Person &p, Vec2 &dir, double &speed) const
    {
        if (p.role == Person::Role::none || p.fear > 0)
            return;
        const double far = reach(p.role);
        const double phase = std::fmod(time + p.body.id * 2., p.role == Person::Role::vagrant ? 35. : 22.);
        Vec2 goal = p.roleHome;
        if (phase >= 10)
        {
            const double angle = static_cast<double>(p.body.id);
            const Vec2 step = Vec2{std::sin(angle), std::cos(angle)} * (std::sin(phase * .3) * far);
            if (role_ground(p.role, p.roleHome + step))
                goal = p.roleHome + step;
        }
        const Vec2 delta = goal - p.body.p;
        const double distance = delta.length();
        // Standing time is spent wherever it finds the person, unless a fright took them away.
        if (distance < .3 || (phase < 10 && distance < far + 1.5))
        {
            speed = 0;
            if (p.role == Person::Role::vagrant)
                return;
            // Sunbathers look at the water, the night shift at the passing cars.
            Vec2 view{};
            if (p.role == Person::Role::beach)
                view = std::abs(p.roleHome.x) > std::abs(p.roleHome.z) ? Vec2{p.roleHome.x, 0}
                                                                       : Vec2{0, p.roleHome.z};
            else
            {
                double nearest = 1e9;
                for (Vec2 side : {Vec2{1, 0}, Vec2{-1, 0}, Vec2{0, 1}, Vec2{0, -1}})
                    if (const double street = layout.roadDistance(p.roleHome + side); street < nearest)
                    {
                        nearest = street;
                        view = side;
                    }
            }
            p.heading = std::atan2(view.x, view.z) + std::sin(double(p.body.id)) * .45;
            return;
        }
        dir = delta / distance;
        speed = std::min(distance > far + 1.5 ? walking_pace(p) : 1., distance);
    }

    namespace
    {
        // Where a walker is on the way across the street its stretch of pavement meets.
        struct Crossing
        {
            int i{}, j{};
            bool signalled{}, alongX{};

            // Metres past the middle of the crossed street: negative before it.
            double offset{};
        };

        std::optional<Crossing> crossing_of(const World &w, const Person &p)
        {
            // A leg from one corner of a junction to another of the same.
            if (p.node != p.next || p.body.roadLevel || (p.sx == p.nextSx) == (p.sz == p.nextSz))
                return {};

            Crossing c;
            c.alongX = p.sx != p.nextSx;
            c.i = p.node % World::nodes;
            c.j = p.node / World::nodes;
            const Vec2 centre = World::nodePoint(p.node);
            c.offset = c.alongX ? (p.body.p.x - centre.x) * p.nextSx : (p.body.p.z - centre.z) * p.nextSz;
            c.signalled = w.crossingLights(p.node, c.alongX, c.alongX ? p.sz : p.sx);
            return c;
        }
        } // namespace

        // Port of the managed walk signal (CityTraffic.ChooseWalk asks for four seconds of it;
        // the native streets are wider): nobody steps off the kerb while the cross traffic has
        // green or gets it within eight seconds.
        double World::kerb_wait(const Person &p) const
        {
            const auto c = crossing_of(*this, p);
            const double kerb = layout.roadHalf();
            // The wait begins a good step back from the kerb: a truck that turns the corner takes
            // its flank over the edge of the pavement.
            if (!c || !c->signalled || c->offset > -kerb || c->offset < -kerb - 1.6)
                return 0;
            const double phase = CityLayout::signal_phase(c->i, c->j, time);
            // Cars along z have green for 14..26, cars along x for 0..12.
            const double start = c->alongX ? 14 : 0, end = c->alongX ? 26 : 12;
            const double untilGreen = std::fmod(start - phase + 28, 28.);
            if (phase >= start && phase < end)
                return end - phase;
            return untilGreen >= 8 ? 0 : untilGreen + (end - start);
        }

        // In the road of a crossing with lights; `caught` when they changed halfway across.
        bool World::on_crossing(const Person &p, bool caught) const
        {
            const auto c = crossing_of(*this, p);
            return c && c->signalled && std::abs(c->offset) < layout.roadHalf() &&
                   (!caught || CityLayout::green(c->i, c->j, c->alongX ? 0 : 1, time));
        }

        // The first inhabitants of the beach and the homeless corners. Uses its own sequence:
        // the seeded city of every earlier save stays what it was.
        void World::settle_roles()
        {
            roleRandom = Random(seed ^ 0x524f4c45u);
            nextRoleStep = 0;
            roleCursor = 0;
            towelGuests = 0;
            for (int i = int(people.size()) - 1; i >= 0; --i)
                if (people[i].homeless() && role_candidate(i))
                    take_role(i, Person::Role::vagrant, false);
            size_t places = role_quota(Person::Role::beach);
            for (int i = int(people.size()) - 1; i >= 0 && places; --i)
            {
                const auto &p = people[i];
                if (p.elder() || p.drunk() || p.homeless() || !role_candidate(i))
                    continue;
                if (take_role(i, Person::Role::beach, false))
                    --places;
            }
            rolesTaken = rolesLeft = dutiesEnded = 0;
        }

        // Port of the managed RolesStep. Night workers leave at dawn, a squad whose chase is
        // over goes off duty, and the beach and the night shift fill up again -- all of it where
        // the player cannot see.
        void World::step_roles()
        {
            nextRoleStep = time + 2;
            const bool night = nightShift(clock.hour);
            const bool calm = wantedLevel() == 0;
            size_t beach = 0, shift = 0;
            for (size_t i = 0; i < people.size(); ++i)
            {
                auto &p = people[i];
                if (p.role != Person::Role::none)
                {
                    const bool lost = (p.body.p - p.roleHome).length2() > lostDistance * lostDistance;
                    const bool over = p.role == Person::Role::night && !night && outOfSight(p.body.p);
                    if (p.dead || p.riding >= 0 || p.uniform != Person::Uniform::none || lost || over)
                        leave_role(p);
                }
                beach += p.role == Person::Role::beach;
                shift += p.role == Person::Role::night;
                if (p.homeless() && p.role == Person::Role::none && role_candidate(int(i)))
                    take_role(int(i), Person::Role::vagrant, false);
                // Before this rule every pursuit left two to four armed officers walking the
                // streets as citizens for the rest of the session (managed RolesStep).
                if (p.uniform != Person::Uniform::none && !p.dead && p.riding < 0 && outOfSight(p.body.p) &&
                    (p.officer() ? calm && int(i) != patrol.officer : !paramedic_call(int(i))) &&
                    !personEngaged(int(i)))
                {
                    p.uniform = Person::Uniform::none;
                    p.sidearm = Person::civilianWeapon(seed, p.body.id);
                    p.alertUntil = p.aimSeconds = 0;
                    p.burstLeft = 0;
                    p.cover = {};
                    ++dutiesEnded;
                }
            }
            const Person::Role open = night && shift < role_quota(Person::Role::night) ? Person::Role::night
                                      : beach < role_quota(Person::Role::beach)        ? Person::Role::beach
                                                                                       : Person::Role::none;
            if (open == Person::Role::none)
                return;

            // One newcomer per pass, as in the managed game; elders, drunks and the homeless
            // keep their own manners.
            for (size_t n = 0; n < people.size(); ++n)
            {
                const size_t i = (roleCursor + n) % people.size();
                if (const auto &p = people[i]; p.elder() || p.drunk() || p.homeless() || !role_candidate(int(i)) || !outOfSight(p.body.p))
                    continue;
                take_role(static_cast<int>(i), open, true);
                roleCursor = (i + 131) % people.size();
                break;
            }
        }
}
