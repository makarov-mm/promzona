#pragma once
#include "headlights.hpp"

namespace Promzona
{
struct LightBlocker
{
    Vec3 centre, halfSize, forward;
};

struct RainCover
{
    Vec3 centre, halfSize;
};

// Static bounds are built once per city. Both selections have a fixed shader budget.
class StreetLighting
{
    std::vector<LightBlocker> buildings_;
    std::vector<RainCover> roofs_;

  public:
    std::array<LightBlocker, 12> blockers;
    std::array<RainCover, 12> covers;
    int blockerCount{}, coverCount{}, lampIndex{-1};

    void prepare(const World &world)
    {
        buildings_.clear();
        roofs_.clear();
        for (const auto &b : world.buildings)
        {
            const float ground = float(world.terrain.height(b.p));
            const Vec3 half{float(b.half.x), float(b.height * .5), float(b.half.z)};
            buildings_.push_back({{float(b.p.x), ground + half.y, float(b.p.z)}, half, {0, 0, 1}});
            if (b.kind != BuildingKind::apartment)
                continue;
            const auto n = entranceNormal(b.half);
            const auto depth = n.x ? b.half.x : b.half.z;
            const auto p = b.p + n * (depth + .52);
            roofs_.push_back(
                {{float(p.x), ground + 2.53f, float(p.z)}, {n.x ? .60f : 1.11f, 0, n.z ? .60f : 1.11f}});
        }
        for (const auto sign : world.busStops)
        {
            const auto p = World::shelter(sign);
            roofs_.push_back(
                {{float(p.x), float(world.terrain.height(p)) + 2.40f, float(p.z)}, {.56f, 0, 1.47f}});
        }
    }

    void update(const World &world, Vec3 eye, const HeadlightSelection &lights)
    {
        auto distanceSquared = [](Vec3 a, Vec3 b) { return dot(a - b, a - b); };
        auto insert = [](auto &items, auto &scores, int &count, auto item, float score)
        {
            const auto at = std::lower_bound(scores.begin(), scores.begin() + count, score);
            const auto index = size_t(at - scores.begin());
            if (index == items.size())
                return;
            count = std::min(count + 1, int(items.size()));
            std::move_backward(items.begin() + index, items.begin() + count - 1, items.begin() + count);
            std::move_backward(at, scores.begin() + count - 1, scores.begin() + count);
            scores[index] = score;
            items[index] = item;
        };
        std::array<float, 12> scores{};
        coverCount = 0;
        if (world.rain > .001 || world.wetness > .001)
            for (const auto &roof : roofs_)
            {
                const float d = distanceSquared(eye, roof.centre);
                if (d < 65.f * 65.f)
                    insert(covers, scores, coverCount, roof, d);
            }
        blockerCount = 0;
        lampIndex = -1;
        float closest = std::numeric_limits<float>::max();
        for (size_t i = 0; i < lights.slots.size(); ++i)
        {
            const auto slot = lights.slots[i];
            if (slot.car < 0 || slot.intensity < .1f)
                continue;
            const auto &body = world.cars[slot.car].body;
            const float d = distanceSquared(eye, {float(body.p.x), float(body.height), float(body.p.z)});
            if (d < closest)
            {
                lampIndex = int(i) * 2;
                closest = d;
            }
        }
        if (lampIndex < 0)
            return;
        const int source = lights.slots[lampIndex / 2].car;
        const auto &car = world.cars[source];
        const auto f = forward(car.heading());
        const Vec3 axis{float(f.x), 0, float(f.z)};
        const Vec3 origin = Vec3{float(car.body.p.x), float(car.body.height), float(car.body.p.z)} +
                            axis * float(car.body.halfH);
        auto consider = [&](const LightBlocker &box)
        {
            const Vec3 delta = box.centre - origin;
            const float radius = std::hypot(box.halfSize.x, box.halfSize.z);
            const float along = dot(delta, axis);
            const float side = std::abs(delta.x * axis.z - delta.z * axis.x);
            if (along + radius < 0 || along - radius > 34 ||
                side > radius + 1.f + .85f * std::max(along, 0.f))
                return;
            insert(blockers, scores, blockerCount, box, std::max(0.f, along - radius) + side * .2f);
        };
        for (const auto &box : buildings_)
            consider(box);
        for (size_t i = 0; i < world.cars.size(); ++i)
        {
            if (int(i) == source)
                continue;
            const auto &other = world.cars[i];
            const auto &body = other.body;
            if ((body.p - car.body.p).length2() > 45 * 45)
                continue;
            const auto direction = forward(other.heading());
            const float h = float(body.verticalExtent);
            consider({{float(body.p.x), float(body.height - .8) + .12f + h * .47f, float(body.p.z)},
                      {float(body.halfW) * .92f, h * .45f, float(body.halfH) * .93f},
                      {float(direction.x), 0, float(direction.z)}});
        }
    }
};
} // namespace Promzona
