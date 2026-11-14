#include "promzona/world.hpp"
#include "promzona/minimap.hpp"
#include "promzona/names.hpp"
#include <format>

namespace Promzona
{
namespace
{
// Original neighbourhood cadence: up to three friends out of every 28 pedestrians.
constexpr size_t cohort = 28, members = 3;
} // namespace

void World::formCompanies(bool loading)
{
    for (size_t i = 0; i < people.size(); i += cohort)
    {
        Person *lead = nullptr;
        for (size_t n = i; n < std::min(i + members, people.size()); ++n)
        {
            auto &p = people[n];
            if (p.riding >= 0 || p.body.roadLevel || p.dead || p.ownedScooter >= 0 || p.courier ||
                ((job.passenger() || job.kind == JobKind::elimination) && job.actor == int(n)))
                continue;
            // Upgrading an existing save must not move anyone the player can see.
            if (loading && (p.body.p - position()).length2() < 100 * 100)
                continue;
            if (!lead)
            {
                lead = &p;
                continue;
            }
            if (loading && (lead->body.p - position()).length2() < 105 * 105)
                continue;
            auto heading = (lead->target - lead->body.p).unit();
            Vec2 at = lead->body.p - heading * (.9 * (n - i));
            if (openCut(at, .4) || insideBuilding(at, terrain.height(at) + .8, .35))
                continue;
            lead->inCompany = p.inCompany = true;
            p.walkLike(*lead);
            p.body.p = at;
            p.body.v = {};
            p.body.height = terrain.height(at) + .85;
            p.body.roadLevel = 0;
            p.heading = std::atan2(heading.x, heading.z);
        }
    }
}

void World::scarePerson(Person &p, Vec2 origin, double fear)
{
    if (conversation.actor == int(&p - people.data()))
        conversation = {};
    bool speak = p.fear < .25 && p.voiceAt <= time;
    p.fear = std::max(p.fear, p.inCompany ? fear * .55 : fear);
    p.danger = origin;
    p.fleeUntil = time + (p.inCompany ? 1.8 : 3.5);
    if (speak && (position() - p.body.p).length2() < 65 * 65)
    {
        const int count = p.inCompany ? 6 : 8;
        auto clip = std::format("{}{}", p.inCompany ? "tough_fear_" : "fear_", p.body.id % count);
        sounds.push_back({clip, p.body.p, .59, false, int(&p - people.data())});
        p.voiceAt = time + 5.;
    }
}

void World::socialIntent(Person &p, Vec2 &dir, double &speed)
{
    if (p.riding >= 0 || (job.passenger() && job.actor == int(&p - people.data())))
        return;
    if (conversation.actor == int(&p - people.data()) && time < conversation.pauseUntil &&
        controlledCar < 0 && !drone.active && playerScooter < 0 && p.fear <= .15 &&
        (player.p - p.body.p).length2() < 9)
    {
        auto toward = player.p - p.body.p;
        p.heading = std::atan2(toward.x, toward.z);
        speed = 0;
        return;
    }
    const bool elder = p.elder() && p.uniform == Person::Uniform::none;
    if (p.inCompany || elder)
    {
        bool audible = (position() - p.body.p).length2() < 28 * 28;
        if (!audible || p.fear > .08 || p.alertUntil > time || p.burnUntil > time || p.watchUntil > time)
            p.idleVoiceAt = 0;
        else if (time >= p.idleVoiceAt)
        {
            // Use a separate sequence: recording selection must not change city traffic.
            Random voice(seed ^ (p.body.id * 2654435761u) ^ unsigned(std::fmod(time, 86400.)));
            if (p.idleVoiceAt > 0 && p.voiceAt <= time && !paramedic_call(int(&p - people.data())))
            {
                auto clip = std::format("{}{}", p.inCompany ? "tough_" : "elder_", voice.index(6));
                sounds.push_back({clip, p.body.p, .5, false, int(&p - people.data())});
            }
            p.idleVoiceAt = time + 30. + 60. * voice.next();
        }
    }
    if (p.fear > .15 && p.fleeUntil > time)
    {
        auto away = p.body.p - p.danger;
        if (away.length2() > .001)
            dir = away.unit();
    }
    else if (p.watchUntil > time && p.fear <= .15 && !p.courier && p.ownedScooter < 0)
    {
        auto delta = p.incident - p.body.p;
        double standOff = 4. + (p.body.id % 5) * .55;
        speed = std::min(speed, std::max(0., delta.length() - standOff));
        dir = delta.unit();
        if (speed < .05)
            p.heading = std::atan2(dir.x, dir.z);
    }
    else if (p.inCompany && p.fear <= .15)
    {
        size_t index = size_t(&p - people.data());
        size_t base = index - index % cohort;
        if (index == base)
            return;
        bool led = false;
        for (size_t n = base; n < index; ++n)
        {
            const auto &lead = people[n];
            if (!lead.inCompany || lead.dead || lead.riding >= 0)
                continue;
            if (p.body.roadLevel != lead.body.roadLevel)
                return;
            led = true;
            auto along = forward(lead.heading);
            // Whoever has got in front of the leader lets him by and falls in behind. Making
            // for their places straight through him, two followers pushed him back as hard
            // as he pushed on, and after a fright the company stood locked where it had
            // stopped, in the road as often as not.
            if (dot(p.body.p - lead.body.p, along) > .3 && (p.body.p - lead.body.p).length2() < 12 * 12)
            {
                p.walkLike(lead);
                dir = -along;
                speed = 0;
                return;
            }
            auto target = lead.body.p - along * (.95 * (index - n)) +
                          perpendicular(along) * ((index - n) % 2 ? .28 : -.28);
            auto delta = target - p.body.p;
            // Remember the current stretch of pavement so a survivor can take over immediately.
            p.walkLike(lead);
            dir = delta.unit();
            speed = std::min(2.5, delta.length() * 2.);
            break;
        }
        // Whoever walks at the head of what is left of a company walks like anybody else.
        if (!led)
            return;
    }
    else
        return;

    // A frightened witness or separated follower may be aiming across a corner.
    // Take an open side step rather than crossing a facade or entering an open cut.
    auto clear = [&](Vec2 d)
    {
        auto at = p.body.p + d * .7;
        return dryGround(at) && (p.body.roadLevel || !openCut(at, .4)) &&
               !insideBuilding(at, surface(at, p.body.roadLevel) + .8, .36);
    };
    if (speed > .01 && !clear(dir))
    {
        auto side = perpendicular(dir);
        if (clear(side))
            dir = side;
        else if (clear(-side))
            dir = -side;
        else
            speed = 0;
    }
}

// The stricter test for building sites used before left out the embankment streets and
// the shore: whoever was frightened or walked in company there stopped for good, in the
// road as often as not, and the traffic behind them with them.
bool World::dryGround(Vec2 at) const
{
    return std::max(std::abs(at.x), std::abs(at.z)) < landEdge - 6 &&
           terrain.height(at) > CityLayout::waterLevel + .5 &&
           (layout.riverDistance(at.x) > layout.bankHalf() + .3 || layout.bridge(at));
}

// People give each other room: whoever stands in the way or comes the other way along
// the same line of the pavement is passed, on the right unless the left is nearer.
// Before this, a walker who met a group waiting at the kerb pushed it along the pavement,
// and now and then off it.
void World::passWalkers(const Person &p, const std::vector<const Person *> &afoot, Vec2 &dir,
                        double speed) const
{
    const size_t index = size_t(&p - people.data());
    // A company keeps its formation behind whoever leads it.
    if (int(index) == patrol.officer || (p.inCompany && index % cohort) || p.body.roadLevel)
        return;
    const Vec2 right = perpendicular(dir), wish = dir * speed;
    double nearest = 1e9, offset = 0;
    for (const Person *other : afoot)
    {
        if (other == &p || std::abs(other->body.height - p.body.height) > 1.5)
            continue;
        const Vec2 d = other->body.p - p.body.p;
        const double ahead = dot(d, dir), room = p.body.radius + other->body.radius;
        if (ahead <= 0 || ahead >= nearest || ahead > room + 2.2)
            continue;
        const double lateral = dot(d, right);
        if (std::abs(lateral) > room + .15)
            continue;
        // Somebody walking the same way at the same pace is company, not an obstacle.
        const double closing = dot(wish - other->body.v, d.unit());
        if (closing < .3 || ahead > room + .45 + closing * .5)
            continue;
        nearest = ahead;
        offset = lateral;
    }
    if (nearest > 1e8)
        return;
    const bool pavement = layout.roadDistance(p.body.p) >= layout.roadHalf();
    const double first = offset > .08 ? -1 : 1;
    for (double side : {first, -first})
    {
        const Vec2 turned = (dir + right * (side * (nearest < .9 ? 1.5 : 1.))).unit();
        const Vec2 at = p.body.p + turned * .8;
        // Nobody steps off the kerb, into the canal or into an open cut to let somebody by.
        if ((pavement && layout.roadDistance(at) < layout.roadHalf()) || !dryGround(at) || openCut(at, .4))
            continue;
        dir = turned;
        return;
    }
}

// The side on which a walker who is held up goes round whatever holds them: away from the
// traffic, then along the obstacle for as long as that gets anywhere, then the other way.
void World::goRound(Person &p, Vec2 dir)
{
    p.blockedFor = 0;
    const bool pavement = layout.roadDistance(p.body.p) >= layout.roadHalf();
    auto probe = [&](int side) { return p.body.p + rotate(dir, side * roundTurn) * .8; };
    // Nobody leaves the pavement, the dry land or the level of the street to get round.
    auto open = [&](int side)
    {
        const Vec2 at = probe(side);
        return !(pavement && layout.roadDistance(at) < layout.roadHalf()) && dryGround(at) &&
               !openCut(at, .4);
    };
    int side = p.body.id % 2 ? 1 : -1;
    if (p.roundSide && time - p.roundUntil < 2)
        side = (p.body.p - p.roundFrom).length2() > .09 ? p.roundSide : -p.roundSide;
    else if (const double gain = layout.roadDistance(probe(1)) - layout.roadDistance(probe(-1));
             std::abs(gain) > .1)
        side = gain > 0 ? 1 : -1;
    if (!open(side))
        side = -side;
    if (!open(side))
        return;
    p.roundSide = side;
    p.roundFrom = p.body.p;
    p.roundUntil = time + .7;
}

bool World::talk()
{
    if (controlledCar >= 0 || playerScooter >= 0 || drone.active || health <= 0 ||
        time < conversation.nextLine)
        return false;
    auto facing = forward(-player.angle);
    int actor = -1;
    double nearest = 1.9;
    for (int i = 0; i < int(people.size()); ++i)
    {
        const auto &p = people[i];
        auto delta = p.body.p - player.p;
        double d = delta.length();
        if (d < .01 || d > nearest || i == patrol.officer || paramedic_call(i) ||
            dot(delta, facing) < d * .35 || p.dead || p.knockedUntil > time || p.riding >= 0 ||
            (p.ownedScooter >= 0 && scooters[p.ownedScooter].rider == i) ||
            p.body.roadLevel != player.roadLevel || std::abs(p.body.height - player.height) > .9 ||
            !blastVisible(player.p, player.height, p.body.p, p.body.height))
            continue;
        actor = i;
        nearest = d;
    }
    if (actor < 0)
        return false;
    auto &p = people[actor];
    auto offer = conversation.actor == actor && time < conversation.offerUntil ? conversation.offer
                                                                               : StreetOffer::none;
    conversation = {actor, time + .5, time + 2.5, time + 4., StreetOffer::none};
    // Directions name the street, as in the managed game, and keep the bearing for the minimap.
    auto directionTo = [&](ServiceKind kind, const wchar_t *place, const wchar_t *ending = L".")
    {
        int site = nearestSite(kind, p.body.p);
        if (site < 0)
            return std::wstring(L"Не здешний я.");
        Vec2 delta = sites[site].entrance - p.body.p;
        if (delta.length() < 10)
            return std::wstring(place) + L"в паре шагов.";
        const Vec2 onMap = Minimap::offset(delta);
        const wchar_t *way = std::abs(onMap.x) > std::abs(onMap.z)
                                 ? (onMap.x < 0 ? L"на запад" : L"на восток")
                                 : (onMap.z < 0 ? L"на север" : L"на юг");
        return std::wstring(place) + L"на " + streetAt(sites[site].entrance, nodes, spacing) + L", " +
               std::to_wstring(int(std::round(delta.length() / 10)) * 10) + L" м " + way + ending;
    };
    const bool elder = p.elder();
    const bool injured = p.injuredUntil > time || p.health < .7;
    if (p.fear > .25 || p.burnUntil > time || p.alertUntil > time)
        notify(L"Не подходи!");
    else if (offer == StreetOffer::gift)
    {
        if (money < 5)
            notify(L"У тебя у самого пусто.");
        else
        {
            money -= 5;
            sounds.push_back({"spend", player.p, .5, true});
            notify(L"Спасибо, брат! Дай бог здоровья.");
        }
    }
    else if (job.kind == JobKind::none && (offer == StreetOffer::taxi || offer == StreetOffer::patient))
        startPassengerJob(offer == StreetOffer::patient ? JobKind::patient : JobKind::taxi, actor);
    else if (p.officer())
        notify(wantedLevel() > 0 ? L"Стоять! Руки за голову!" : L"Проходите, гражданин, не задерживайтесь.");
    else if (p.uniform == Person::Uniform::medical)
        notify(L"Не мешайте работать.");
    else if (p.inCompany)
        notify(clock.daylight() < .35 ? L"Чё надо? Иди отсюда, пока цел." : L"Не мешай, братан.");
    else if (p.courier)
        notify(L"Извини, заказ стынет.");
    else if (p.role == Person::Role::night)
        notify(L"Не сегодня, милый.");
    else if (p.role == Person::Role::beach)
        notify(L"Водичка — класс! Иди окунись.");
    else if (p.drunk() || p.homeless())
    {
        conversation.offer = StreetOffer::gift;
        notify(p.drunk() ? L"Братан, выручи пятёркой на пиво? [F] Дать $5"
                         : L"Мелочи не найдётся? [F] Дать $5");
    }
    else if (job.kind == JobKind::none && (injured || p.body.id % 5 == 0) && canTakePassengerJob(actor) &&
             layout.roadDistance(p.body.p) > layout.roadHalf())
    {
        conversation.offer = injured ? StreetOffer::patient : StreetOffer::taxi;
        notify(injured ? L"Брат, отвези в клинику, сам не дойду... [F] Согласиться"
                       : L"Таксуешь? Подбросишь? [F] Согласиться");
    }
    else if (elder && p.body.id % 3 == 0)
        notify(L"Ох, молодёжь... Раньше тут автобус ходил каждые десять минут.");
    else if (elder || injured)
        notify(directionTo(ServiceKind::clinic, L"Поликлиника — ", L", милок."));
    else
        switch ((p.body.id * 7) % 5)
        {
        case 1:
            notify(directionTo(ServiceKind::market, L"Рынок — "));
            break;
        case 2:
            notify(layout.revision() == CityLayout::districtRevision
                       ? L"Слышал, на промзоне тупик — вагоны загнали и бросили."
                       : L"Нормально всё. Живём.");
            break;
        case 3:
            notify(L"Бомбилы совсем обнаглели — по двадцатке за квартал.");
            break;
        case 4:
            notify(directionTo(ServiceKind::kiosk, L"Киоск — "));
            break;
        default:
            notify(L"Нормально всё. Живём.");
        }
    return true;
}
} // namespace Promzona
