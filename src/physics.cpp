#include "promzona/physics.hpp"
#include <stdexcept>
namespace pz
{
static std::array<Vec2, 4> corners(const Body &b)
{
    std::array<Vec2, 4> r{
        {{b.halfW, b.halfH}, {-b.halfW, b.halfH}, {-b.halfW, -b.halfH}, {b.halfW, -b.halfH}}};
    for (auto &p : r)
        p = b.p + rotate(p, b.angle);
    return r;
}
static Vec2 faceNormal(const Body &b, int f)
{
    static constexpr Vec2 normals[] = {{1, 0}, {0, 1}, {-1, 0}, {0, -1}};
    return rotate(normals[f], b.angle);
}
static double faceSeparation(const Body &a, int f, const Body &b)
{
    auto n = faceNormal(a, f);
    double offset = dot(n, a.p) + (f % 2 ? a.halfH : a.halfW), best = INFINITY;
    for (auto p : corners(b))
        best = std::min(best, dot(n, p) - offset);
    return best;
}
static bool clip(Vec2 &p, Vec2 &q, Vec2 n, double offset)
{
    double a = dot(n, p) - offset, b = dot(n, q) - offset;
    if (a > 0 && b > 0)
        return false;
    if (a > 0)
        p += (q - p) * (a / (a - b));
    else if (b > 0)
        q += (p - q) * (b / (b - a));
    return true;
}
std::uint64_t ContactWorld::pair(const Body &a, const Body &b)
{
    return (std::uint64_t(std::min(a.id, b.id)) << 32) | std::max(a.id, b.id);
}
void ContactWorld::collide(Body &a, Body &b)
{
    if (std::abs(a.height - b.height) > (a.verticalExtent + b.verticalExtent) * .5)
        return;
    if (a.circle && b.circle)
    {
        auto d = b.p - a.p;
        double r = a.radius + b.radius, len = d.length();
        if (len >= r)
            return;
        auto n = len < 1e-6 ? Vec2{1, 0} : d / len;
        Contact c{&a, &b, n};
        c.count = 1;
        c.p[0].p = a.p + n * a.radius;
        c.p[0].penetration = r - len;
        contacts_.push_back(c);
        return;
    }
    if (a.circle || b.circle)
    {
        auto &box = a.circle ? b : a;
        auto &circle = a.circle ? a : b;
        auto local = rotate(circle.p - box.p, -box.angle);
        Vec2 near{clamp(local.x, -box.halfW, box.halfW), clamp(local.z, -box.halfH, box.halfH)},
            d = local - near, n;
        double len = d.length(), pen;
        if (len > 1e-6)
        {
            if (len >= circle.radius)
                return;
            n = d / len;
            pen = circle.radius - len;
        }
        else
        {
            double px = box.halfW - std::abs(local.x), pz = box.halfH - std::abs(local.z);
            if (px < pz)
            {
                n = {local.x >= 0 ? 1. : -1., 0};
                pen = px + circle.radius;
            }
            else
            {
                n = {0, local.z >= 0 ? 1. : -1.};
                pen = pz + circle.radius;
            }
        }
        n = rotate(n, box.angle);
        Contact c{&box, &circle, n};
        c.count = 1;
        c.p[0].p = circle.p - n * circle.radius;
        c.p[0].penetration = pen;
        contacts_.push_back(c);
        return;
    }
    int bestFace = 0;
    Body *ref = &a;
    Body *inc = &b;
    double best = -INFINITY;
    for (int f = 0; f < 4; f++)
    {
        double sep = faceSeparation(a, f, b);
        if (sep > 0)
            return;
        if (sep > best)
        {
            best = sep;
            bestFace = f;
        }
    }
    for (int f = 0; f < 4; f++)
    {
        double sep = faceSeparation(b, f, a);
        if (sep > 0)
            return;
        if (sep > best + 1e-4)
        {
            best = sep;
            bestFace = f;
            ref = &b;
            inc = &a;
        }
    }
    auto n = faceNormal(*ref, bestFace), t = perpendicular(n);
    int incident = 0;
    double opp = INFINITY;
    for (int f = 0; f < 4; f++)
    {
        double v = dot(faceNormal(*inc, f), n);
        if (v < opp)
        {
            opp = v;
            incident = f;
        }
    }
    auto verts = corners(*inc);
    auto p = verts[(incident + 3) & 3], q = verts[incident];
    double extent = bestFace % 2 ? ref->halfW : ref->halfH, centre = dot(t, ref->p);
    if (!clip(p, q, t, centre + extent) || !clip(p, q, -t, -centre + extent))
        return;
    double offset = dot(n, ref->p) + (bestFace % 2 ? ref->halfH : ref->halfW);
    Contact c{ref, inc, n};
    for (auto v : {p, q})
    {
        double pen = offset - dot(n, v);
        if (pen >= 0)
        {
            c.p[c.count].p = v;
            c.p[c.count].penetration = pen;
            c.count++;
        }
    }
    if (c.count)
        contacts_.push_back(c);
}
static void applyContact(Body &a, Body &b, Vec2 p, Vec2 impulse)
{
    if (!a.isStatic() && !a.sleeping)
    {
        a.v -= impulse * a.invMass;
        a.w -= cross(p - a.p, impulse) * a.invI;
    }
    if (!b.isStatic() && !b.sleeping)
    {
        b.v += impulse * b.invMass;
        b.w += cross(p - b.p, impulse) * b.invI;
    }
}
void ContactWorld::step(std::span<Body *> bodies, double dt, Vec2 gravity)
{
    if (!(dt > 0 && dt <= .05))
        throw std::invalid_argument("Contact timestep must be in (0,.05]");
    contacts_.clear();
    grid_.clear();
    pairs_.clear();
    candidatePairs = 0;
    for (auto b : bodies)
    {
        if (!b->isStatic() && !b->sleeping)
            b->v += gravity * dt;
        double extent = b->circle ? b->radius : std::hypot(b->halfW, b->halfH);
        int x0 = int(std::floor((b->p.x - extent) / 8)), x1 = int(std::floor((b->p.x + extent) / 8)),
            z0 = int(std::floor((b->p.z - extent) / 8)), z1 = int(std::floor((b->p.z + extent) / 8));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                auto key = (std::uint64_t(std::uint32_t(x)) << 32) | std::uint32_t(z);
                grid_[key].push_back(b);
            }
    }
    // Sorted candidate IDs make the solver deterministic independently of hash iteration.
    std::vector<std::pair<Body *, Body *>> candidates;
    for (auto &[key, cell] : grid_)
        for (size_t i = 0; i < cell.size(); i++)
            for (size_t j = i + 1; j < cell.size(); j++)
            {
                auto a = cell[i], b = cell[j];
                if ((a->isStatic() && b->isStatic()) || (a->sleeping && b->sleeping))
                    continue;
                if (pairs_.insert(pair(*a, *b)).second)
                {
                    if (a->id > b->id)
                        std::swap(a, b);
                    candidates.emplace_back(a, b);
                }
            }
    std::sort(candidates.begin(), candidates.end(),
              [](auto a, auto b) { return pair(*a.first, *a.second) < pair(*b.first, *b.second); });
    candidatePairs = candidates.size();
    for (auto [a, b] : candidates)
        collide(*a, *b);
    contactCount = contacts_.size();
    for (auto &c : contacts_)
    {
        auto &a = *c.a;
        auto &b = *c.b;
        if (a.sleeping != b.sleeping)
        {
            auto &awake = a.sleeping ? b : a;
            const auto &asleep = a.sleeping ? a : b;
            // Something heavy that leans on something light moves it, however slowly it
            // comes. Below the speed that wakes a sleeper, a walker who had stopped at a
            // cigarette packet could not set off again: the packet held him like a wall,
            // and the truck that waited for him to cross the street stood there with him.
            const double leaning = dot(awake.v, c.n) * (a.sleeping ? -1 : 1);
            if (awake.v.length() + std::abs(awake.w) > .35 ||
                (leaning > .02 && asleep.invMass > awake.invMass * 3))
            {
                a.wake();
                b.wake();
            }
        }
        auto found = warm_.find(pair(a, b));
        bool reuse = found != warm_.end() && dot(c.n, found->second.normal) > .95;
        for (int k = 0; k < c.count; k++)
        {
            auto &p = c.p[k];
            auto ra = p.p - a.p, rb = p.p - b.p, t = perpendicular(c.n);
            double ma = a.sleeping ? 0 : a.invMass, mb = b.sleeping ? 0 : b.invMass,
                   ia = a.sleeping ? 0 : a.invI, ib = b.sleeping ? 0 : b.invI;
            double kn = ma + mb + ia * std::pow(cross(ra, c.n), 2) + ib * std::pow(cross(rb, c.n), 2),
                   kt = ma + mb + ia * std::pow(cross(ra, t), 2) + ib * std::pow(cross(rb, t), 2);
            p.massN = kn > 0 ? 1 / kn : 0;
            p.massT = kt > 0 ? 1 / kt : 0;
            p.vn0 = dot(b.velocityAt(p.p) - a.velocityAt(p.p), c.n);
            p.bias = -.2 / dt * std::max(0., p.penetration - .005);
            if (reuse)
            {
                p.normal = found->second.impulses[k].x;
                p.tangent = found->second.impulses[k].z;
            }
        }
    }
    for (auto &c : contacts_)
        for (int k = 0; k < c.count; k++)
        {
            auto &p = c.p[k];
            applyContact(*c.a, *c.b, p.p, c.n * p.normal + perpendicular(c.n) * p.tangent);
        }
    for (int iteration = 0; iteration < 12; iteration++)
        for (auto &c : contacts_)
        {
            auto &a = *c.a;
            auto &b = *c.b;
            double friction = std::sqrt(a.friction * b.friction), e = std::max(a.restitution, b.restitution);
            for (int k = 0; k < c.count; k++)
            {
                auto &p = c.p[k];
                double vn = dot(b.velocityAt(p.p) - a.velocityAt(p.p), c.n),
                       push = std::max(p.vn0 < -1 ? -e * p.vn0 : 0., -p.bias);
                double nn = std::max(0., p.normal - (vn - push) * p.massN);
                applyContact(a, b, p.p, c.n * (nn - p.normal));
                p.normal = nn;
                auto t = perpendicular(c.n);
                double vt = dot(b.velocityAt(p.p) - a.velocityAt(p.p), t),
                       nt = clamp(p.tangent - vt * p.massT, -friction * nn, friction * nn);
                applyContact(a, b, p.p, t * (nt - p.tangent));
                p.tangent = nt;
            }
        }
    warm_.clear();
    for (auto &c : contacts_)
    {
        auto &w = warm_[pair(*c.a, *c.b)];
        w.normal = c.n;
        for (int k = 0; k < c.count; k++)
            w.impulses[k] = {c.p[k].normal, c.p[k].tangent};
    }
    for (auto b : bodies)
        if (!b->isStatic() && !b->sleeping)
        {
            b->p += b->v * dt;
            b->angle = wrap(b->angle + b->w * dt);
            if (b->v.length() < .012 && std::abs(b->w) < .05)
            {
                b->sleepTimer += dt;
                if (b->sleepTimer > .6)
                {
                    b->sleeping = true;
                    b->v = {};
                    b->w = 0;
                }
            }
            else
                b->sleepTimer = 0;
        }
}
} // namespace pz
