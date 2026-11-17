#pragma once
#include "core.hpp"
#include <unordered_map>

namespace Promzona
{
    // Port of CityContactWorld's circle/OBB narrow phase and contact impulses.
    // Stable entity IDs replace managed object/list indices in the warm-start cache.
    class ContactWorld
    {
        struct Point
        {
            Vec2 p;
            double penetration{}, vn0{}, normal{}, tangent{}, mass_n{}, mass_t{}, bias{};
        };

        struct Contact
        {
            Body *a{}, *b{};
            Vec2 n;
            std::array<Point, 2> p{};
            int count{};
        };

        struct Warm
        {
            std::array<Vec2, 2> impulses{};
            Vec2 normal{};
        };

        struct Candidate
        {
            std::uint64_t key{};
            Body *a{}, *b{};
        };

        std::vector<Contact> contacts_;
        std::unordered_map<std::uint64_t, Warm> warm_;
        // The cells and the candidate list keep their memory from one step to the next.
        std::unordered_map<std::uint64_t, std::vector<Body *>> grid_;
        std::vector<Candidate> candidates_;
        void collide(Body &a, Body &b);
        static std::uint64_t pair(const Body &a, const Body &b);

      public:
        std::size_t candidate_pairs{}, contact_count{};
        void step(std::span<Body *> bodies, double dt, Vec2 gravity = {});

        void clear()
        {
            warm_.clear();
            grid_.clear();
            contacts_.clear();
        }
    };
}
