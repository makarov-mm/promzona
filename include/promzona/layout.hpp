#pragma once
#include "core.hpp"
#include <functional>

namespace Promzona
{
    enum class District
    {
        estate,
        courtyard,
        cottages,
        centre,
        garages,
        industry
    };

    enum class BuildingKind
    {
        apartment,
        cottage,
        garage,
        hall,
        chimney,
        tank,
        shed,
        pump,
        fence,
        crate,
        wagon,
        buffer,
        stall
    };

    struct Building
    {
        Vec2 p, half;
        double height{};
        int style{};
        BuildingKind kind{BuildingKind::apartment};
        int front{1}; // Z direction of the door
    };

    // Apartment canopies and courtyard trails use the same long facade and door centre.
    inline Vec2 entranceNormal(Vec2 half)
    {
        return half.x >= half.z
            ? Vec2 { .x = 0, .z = 1 } 
            : Vec2 { .x = 1, .z = 0 };
    }

    inline Vec2 apartmentDoor(const Building &b)
    {
        Vec2 n = entranceNormal(b.half);
        return b.p + n * (dot(n, b.half) + .09);
    }

    struct Footpath
    {
        std::array<Vec2, 4> points;
        double halfWidth{};
        double edgeDistance(Vec2 p) const;
    };

    struct YardCurb
    {
        Vec2 a, b;
    };

    struct Courtyard
    {
        std::vector<Footpath> paths;
        std::vector<YardCurb> curbs;
        double edge_distance(Vec2 p) const;
    };

    // The seeded .NET Random stream used by CityLayout (not the game's runtime PRNG).
    class LayoutRandom
    {
        std::array<int, 56> values_{};
        int next_{}, other_{21};

    public:
        explicit LayoutRandom(std::int32_t seed);
        double next();
        int index(int maximum);
    };


    // Seeded topology, plots and districts. Revision zero preserves existing native saves.
    // Instance-owned state allows background generation without changing the live city.
    class CityLayout
    {
        int nodes_{49}, revision_{};
        std::int32_t seed_{};
        bool wide_{true};
        std::vector<Courtyard> yards_;
        void planCourtyards();
        std::vector<bool> roads_, parks_;

    public:
        void set_road(int i, int j, int direction, bool value);
        static constexpr double spacing = 36, lane = 1.9, waterLevel = -2.8;
        static constexpr float canalDeckThickness = .38f;
        std::vector<Building> buildings, furniture;
        void generate(std::int32_t seed, int nodes = 49, bool wide = true, std::function<void(float)> progress = {}, int revision = 0);
        static constexpr int districtRevision = 4;
        int revision() const { return revision_; }
        District district(int i, int j) const;
        bool industrial(int i, int j) const;
        bool railEdge(int i, int j, int direction) const;
        int yardWest() const { return nodes_ / 3 + 1; }
        int yardEast() const { return 2 * nodes_ / 3; }
        int yardRow() const { return nodes_ - 8; }
        double railZ() const { return coordinate(yardRow() + 2) + 2; }
        double railStart() const { return coordinate(yardWest()) - 2; }
        double railEnd() const { return coordinate(yardEast() - 1) - 6; }
        int nodes() const { return nodes_; }
        bool wide() const { return wide_; }
        bool rivers() const { return nodes_ >= 49; }
        double coordinate(int i) const { return -(nodes_ - 1) * 18. + i * spacing; }
        Vec2 point(int n) const { return {coordinate(n % nodes_), coordinate(n / nodes_)}; }
        double edge() const { return coordinate(nodes_ - 1) + 54; }
        static constexpr double laneOffset(int index) { return lane + index * 3.35; }
        double roadHalf() const { return wide_ ? 7.3 : 4.8; }
        double sidewalk() const { return wide_ ? 9.1 : 6.5; }
        double bankHalf() const { return 18 - roadHalf() - 2.8; }
        double deckHalf() const { return 18 - roadHalf(); }
        bool canalColumn(int i) const { return rivers() && (i == nodes_ / 3 || i == 2 * nodes_ / 3); }
        bool bridgeRow(int j) const { return j >= 0 && j < nodes_ && j % 6 == 0; }
        bool allowedEdge(int i, int j, int d) const;
        bool road(int i, int j, int d) const;
        bool park(int i, int j) const;
        const Courtyard *courtyard(int i, int j) const;
        const Courtyard *courtyard(Vec2 p) const;
        std::vector<int> neighbours(int node) const;
        int degree(int node) const;
        double roadDistance(Vec2 p) const;
        double riverDistance(double x) const;
        bool sandyGround(Vec2 p, double streetDistance) const;
        bool bridge(Vec2 p) const;
        double deckHeight(double x) const;
        bool urbanSite(Vec2 p, double radius = 0) const;
        static bool clinic(int i, int j);
        static bool station(int i, int j) { return i == 21 && j == 13; }
        static Vec2 direction(int d);
        // Every junction runs one 28 s programme: 12 s for the east-west street, 2 s all red,
        // 12 s for the north-south street, 2 s all red.
        static double signal_phase(int i, int j, double time);
        static bool green(int i, int j, int direction, double time);
    };
}
