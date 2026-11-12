#include "promzona/world.hpp"
#include <optional>

namespace pz
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
} // namespace

// Managed cruise speeds: pensioners with bags .92, drunks .70, the homeless 1.05 and
// 1.3--1.7 for everybody else. The native streets are a little slower.
double World::walkingPace(const Person &p)
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

double World::fleeingPace(const Person &p)
{
    if (p.uniform != Person::Uniform::none)
        return 3.5;
    return p.elder() ? 2. : p.drunk() ? 2.6 : 3.5;
}

// The managed city keeps up to 420 of about five thousand people on the sand and at
// most forty on the night shift; the same shares of a native population.
size_t World::roleQuota(Person::Role role) const
{
    return role == Person::Role::beach   ? people.size() / 16
           : role == Person::Role::night ? people.size() / 80
                                         : 0;
}

// Ground a person with this habit may stand on: dry, open and off the carriageway;
// sand of the outer shore for the beach, a pavement inside the street grid otherwise.
bool World::roleGround(Person::Role role, Vec2 at) const
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
bool World::roleCandidate(int actor) const
{
    const auto &p = people[actor];
    return !p.dead && p.riding < 0 && p.role == Person::Role::none && p.uniform == Person::Uniform::none &&
           !p.courier && !p.inCompany && p.ownedScooter < 0 && p.waitingLine < 0 && !p.body.roadLevel &&
           p.fear <= 0 && p.burnUntil <= time && p.alertUntil <= time && p.watchUntil <= time &&
           p.injuredUntil <= time && p.knockedUntil <= time && p.health > .7 && !personEngaged(actor);
}

// Pedestrians use the northeast corner of each junction as their path anchor.
void World::anchorWalk(Person &p)
{
    const int x = clamp(int(std::round((p.body.p.x - 9.1 - layout.coordinate(0)) / spacing)), 0, nodes - 1);
    const int z = clamp(int(std::round((p.body.p.z - 9.1 - layout.coordinate(0)) / spacing)), 0, nodes - 1);
    p.node = p.next = z * nodes + x;
    p.target = nodePoint(p.node) + Vec2{9.1, 9.1};
}

void World::leaveRole(Person &p)
{
    if (p.role == Person::Role::none)
        return;
    // A night worker and a homeless person are already on a pavement; a beach visitor
    // has to find the nearest corner first.
    if (p.role == Person::Role::beach)
        anchorWalk(p);
    p.role = Person::Role::none;
    p.roleHome = {};
    ++rolesLeft;
}

// A walker nobody is looking at becomes a beach visitor or a night worker somewhere
// else nobody is looking at. The homeless stay where they are: their corner is the
// place they happen to be.
bool World::takeRole(int actor, Person::Role role, bool unseen)
{
    auto &p = people[actor];
    if (role == Person::Role::vagrant)
    {
        if (!roleGround(role, p.body.p))
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
            // A place at the kerb of a walked pavement, a third to two thirds along the block.
            from = roleRandom.index(nodes * nodes);
            to = chooseWalkNext(from, -1, &roleRandom);
            if (to == from)
                continue;
            const Vec2 a = nodePoint(from) + Vec2{9.1, 9.1}, b = nodePoint(to) + Vec2{9.1, 9.1};
            const Vec2 along = (b - a).unit();
            // The walked line runs 9.1 m beside the street axis: the kerb is towards the axis.
            const Vec2 kerb = std::abs(along.x) > std::abs(along.z) ? Vec2{0, -1} : Vec2{-1, 0};
            at = a + (b - a) * (.34 + roleRandom.next() * .33) + kerb * 1.1;
        }
        const double centre = terrain.height(at) + .85;
        if ((unseen && !outOfSight(at)) || !roleGround(role, at) ||
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
        if (role == Person::Role::beach)
        {
            anchorWalk(p);
            if (towelGuests == 0)
                towel = at;
            ++towelGuests;
        }
        else
        {
            p.node = from;
            p.next = to;
            p.target = nodePoint(to) + Vec2{9.1, 9.1};
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
void World::roleIntent(Person &p, Vec2 &dir, double &speed)
{
    if (p.role == Person::Role::none || p.fear > 0)
        return;
    const double far = reach(p.role);
    const double phase = std::fmod(time + p.body.id * 2., p.role == Person::Role::vagrant ? 35. : 22.);
    Vec2 goal = p.roleHome;
    if (phase >= 10)
    {
        const double angle = double(p.body.id);
        const Vec2 step = Vec2{std::sin(angle), std::cos(angle)} * (std::sin(phase * .3) * far);
        if (roleGround(p.role, p.roleHome + step))
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
    speed = std::min(distance > far + 1.5 ? walkingPace(p) : 1., distance);
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
std::optional<Crossing> crossingOf(const World &w, const Person &p)
{
    if (p.node == p.next || p.body.roadLevel)
        return {};
    const Vec2 from = World::nodePoint(p.node), to = World::nodePoint(p.next);
    Crossing c;
    c.alongX = std::abs(to.x - from.x) > std::abs(to.z - from.z);
    const double way = c.alongX ? to.x - from.x : to.z - from.z;
    // The walked line runs northeast of the street axis: the street that is crossed
    // belongs to the corner further east or further north.
    const int corner = way > 0 ? p.next : p.node;
    c.i = corner % World::nodes;
    c.j = corner / World::nodes;
    const Vec2 centre = World::nodePoint(corner);
    c.offset = (c.alongX ? p.body.p.x - centre.x : p.body.p.z - centre.z) * (way > 0 ? 1 : -1);
    // A walker along x crosses the arm that leaves the junction towards +z, and the reverse.
    c.signalled = w.layout.road(c.i, c.j, c.alongX ? 0 : 1) && w.layout.degree(corner) >= 3;
    return c;
}
} // namespace

// Port of the managed walk signal (CityTraffic.ChooseWalk asks for four seconds of it;
// the native streets are wider): nobody steps off the kerb while the cross traffic has
// green or gets it within eight seconds.
double World::kerbWait(const Person &p) const
{
    const auto c = crossingOf(*this, p);
    const double kerb = layout.roadHalf();
    // The wait begins a good step back from the kerb: a truck that turns the corner takes
    // its flank over the edge of the pavement.
    if (!c || !c->signalled || c->offset > -kerb || c->offset < -kerb - 1.6)
        return 0;
    const double phase = CityLayout::signalPhase(c->i, c->j, time);
    // Cars along z have green for 14..26, cars along x for 0..12.
    const double start = c->alongX ? 14 : 0, end = c->alongX ? 26 : 12;
    const double untilGreen = std::fmod(start - phase + 28, 28.);
    if (phase >= start && phase < end)
        return end - phase;
    return untilGreen >= 8 ? 0 : untilGreen + (end - start);
}

// In the road of a crossing with lights; `caught` when they changed halfway across.
bool World::onCrossing(const Person &p, bool caught) const
{
    const auto c = crossingOf(*this, p);
    return c && c->signalled && std::abs(c->offset) < layout.roadHalf() &&
           (!caught || CityLayout::green(c->i, c->j, c->alongX ? 0 : 1, time));
}

// The first inhabitants of the beach and the homeless corners. Uses its own sequence:
// the seeded city of every earlier save stays what it was.
void World::settleRoles()
{
    roleRandom = Random(seed ^ 0x524f4c45u);
    nextRoleStep = 0;
    roleCursor = 0;
    towelGuests = 0;
    for (int i = int(people.size()) - 1; i >= 0; --i)
        if (people[i].homeless() && roleCandidate(i))
            takeRole(i, Person::Role::vagrant, false);
    size_t places = roleQuota(Person::Role::beach);
    for (int i = int(people.size()) - 1; i >= 0 && places; --i)
    {
        const auto &p = people[i];
        if (p.elder() || p.drunk() || p.homeless() || !roleCandidate(i))
            continue;
        if (takeRole(i, Person::Role::beach, false))
            --places;
    }
    rolesTaken = rolesLeft = dutiesEnded = 0;
}

// Port of the managed RolesStep. Night workers leave at dawn, a squad whose chase is
// over goes off duty, and the beach and the night shift fill up again -- all of it where
// the player cannot see.
void World::stepRoles()
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
                leaveRole(p);
        }
        beach += p.role == Person::Role::beach;
        shift += p.role == Person::Role::night;
        if (p.homeless() && p.role == Person::Role::none && roleCandidate(int(i)))
            takeRole(int(i), Person::Role::vagrant, false);
        // Before this rule every pursuit left two to four armed officers walking the
        // streets as citizens for the rest of the session (managed RolesStep).
        if (p.uniform != Person::Uniform::none && !p.dead && p.riding < 0 && outOfSight(p.body.p) &&
            (p.officer() ? calm && int(i) != patrol.officer : !paramedicCall(int(i))) &&
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
    const Person::Role open = night && shift < roleQuota(Person::Role::night) ? Person::Role::night
                              : beach < roleQuota(Person::Role::beach)        ? Person::Role::beach
                                                                              : Person::Role::none;
    if (open == Person::Role::none)
        return;
    // One newcomer per pass, as in the managed game; elders, drunks and the homeless
    // keep their own manners.
    for (size_t n = 0; n < people.size(); ++n)
    {
        const size_t i = (roleCursor + n) % people.size();
        const auto &p = people[i];
        if (p.elder() || p.drunk() || p.homeless() || !roleCandidate(int(i)) || !outOfSight(p.body.p))
            continue;
        takeRole(int(i), open, true);
        roleCursor = (i + 131) % people.size();
        break;
    }
}
} // namespace pz
