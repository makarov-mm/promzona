#pragma once
#include "promzona/visibility.hpp"
#include "promzona/world.hpp"
#include <algorithm>
#include <array>
#include <limits>

namespace Promzona
{
struct HeadlightSelection
{
    struct Slot
    {
        int car{-1};
        float intensity{};
    };

    std::array<Slot, 4> slots{};
    double lastUpdate{-1};

    void update(const World& world, const Vec3& eye, const Frustum& frustum, const bool enabled)
    {
        if (!enabled)
        {
            *this = {};
            return;
        }

        const bool initial = lastUpdate < 0 || world.time < lastUpdate;
        const float step = initial ? 1.f : std::clamp(static_cast<float>(world.time - lastUpdate) * 4.f, 0.f, 1.f);
        lastUpdate = world.time;
        std::array wanted{-1, -1, -1, -1};
        std::array<float, 4> scores;
        scores.fill(std::numeric_limits<float>::max());

        for (size_t i = 0; i < world.cars.size(); ++i)
        {
            const auto &car = world.cars[i];

            if (car.destroyed || car.falling || car.kind == 12 || (car.driverless && static_cast<int>(i) != world.controlledCar))
            {
                continue;
            }

            const Vec2 f = forward(car.heading());

            const Vec3 axis{
                .x = static_cast<float>(f.x), 
                .y = 0.f, 
                .z = static_cast<float>(f.z)
            };

            const Vec3 origin{
                .x = static_cast<float>(car.body.p.x),
                .y = static_cast<float>(car.body.height),
                .z = static_cast<float>(car.body.p.z)
            };

            if (!frustum.sphere(origin + axis * 15.f, 20.f))
            {
                continue;
            }

            const Vec3 delta = eye - origin;
            const auto separation = delta - axis * std::clamp(dot(delta, axis), 0.f, 32.f);
            float score = dot(separation, separation) + .12f * dot(delta, delta);

            if (score > 125.f * 125.f)
                continue;

            if (std::ranges::find(slots, static_cast<int>(i), &Slot::car) != slots.end())
                score *= .82f;

            int candidate = static_cast<int>(i);

            for (size_t k = 0; k < wanted.size(); ++k)
                if (score < scores[k])
                {
                    std::swap(scores[k], score);
                    std::swap(wanted[k], candidate);
                }
        }

        // A displaced pair fades at its own position before its slot is reused.
        for (auto &slot : slots)
        {
            if (slot.car < 0)
                continue;

            const bool valid = static_cast<size_t>(slot.car) < world.cars.size() && !world.cars[slot.car].destroyed;

            if (!valid || std::ranges::find(wanted, slot.car) == wanted.end())
            {
                slot.intensity = valid ? std::max(0.f, slot.intensity - step) : 0.f;

                if (slot.intensity == 0.f)
                    slot = {};
            }
            else
            {
                slot.intensity = std::min(1.f, slot.intensity + step);
            }
        }

        for (int car : wanted)
        {
            if (car < 0 || std::ranges::find(slots, car, &Slot::car) != slots.end())
                continue;

            std::ranges::borrowed_iterator_t<std::array<Slot, 4> &> free = std::ranges::find(slots, -1, &Slot::car);

            if (free != slots.end())
            {
                *free = {
                    .car = car,
                    .intensity = step
                };
            }
        }
    }
};
}
