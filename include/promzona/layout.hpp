#pragma once
#include "core.hpp"
#include <functional>

namespace pz
{
struct Building
{
    Vec2 p, half;
    double height;
    int style{};
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

// Port of CityTraffic.Layout revision 0: topology, plots, parks and canals.
// Instance-owned state allows background generation without changing the live city.
class CityLayout
{
    int nodes_{49};
    bool wide_{true};
    std::vector<bool> roads_, parks_;

  public:
    void setRoad(int i, int j, int direction, bool value);
    static constexpr double spacing = 36, lane = 1.9, waterLevel = -2.8;
    std::vector<Building> buildings, furniture;
    void generate(std::int32_t seed, int nodes = 49, bool wide = true,
                  std::function<void(float)> progress = {});
    int nodes() const { return nodes_; }
    bool wide() const { return wide_; }
    bool rivers() const { return nodes_ >= 49; }
    double coordinate(int i) const { return -(nodes_ - 1) * 18. + i * spacing; }
    Vec2 point(int n) const { return {coordinate(n % nodes_), coordinate(n / nodes_)}; }
    double edge() const { return coordinate(nodes_ - 1) + 54; }
    double roadHalf() const { return wide_ ? 7.3 : 4.8; }
    double sidewalk() const { return wide_ ? 9.1 : 6.5; }
    double bankHalf() const { return 18 - roadHalf() - 2.8; }
    double deckHalf() const { return 18 - roadHalf(); }
    bool canalColumn(int i) const { return rivers() && (i == nodes_ / 3 || i == 2 * nodes_ / 3); }
    bool bridgeRow(int j) const { return j >= 0 && j < nodes_ && j % 6 == 0; }
    bool allowedEdge(int i, int j, int d) const;
    bool road(int i, int j, int d) const;
    bool park(int i, int j) const;
    std::vector<int> neighbours(int node) const;
    int degree(int node) const;
    double roadDistance(Vec2 p) const;
    double riverDistance(double x) const;
    bool bridge(Vec2 p) const;
    double deckHeight(double x) const;
    bool urbanSite(Vec2 p, double radius = 0) const;
    static bool clinic(int i, int j);
    static bool station(int i, int j) { return i == 21 && j == 13; }
    static Vec2 direction(int d);
    static bool green(int i, int j, int direction, double time);
};
} // namespace pz
