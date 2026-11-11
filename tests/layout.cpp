#include "promzona/world.hpp"
#include <fstream>
#include <iostream>
#include <set>
#include <stdexcept>
using namespace pz;
static int checks{};
static void check(bool b, const char *what)
{
    ++checks;
    if (!b)
        throw std::runtime_error(what);
}
int main(int argc, char **argv)
{
    try
    {
        check(argc == 2, "layout fixture argument");
        std::ifstream in(argv[1]);
        check(bool(in), "layout fixture present");
        std::string tag;
        int cities = 0, streams = 0;
        while (in >> tag && tag != "end")
        {
            std::int32_t seed;
            in >> seed;
            if (tag == "random")
            {
                LayoutRandom r(seed);
                for (int i = 0; i < 128; ++i)
                {
                    double expected;
                    in >> expected;
                    check(r.next() == expected, "seeded .NET Random parity");
                }
                ++streams;
                continue;
            }
            check(tag == "city", "fixture tag");
            int nodes, wide;
            in >> nodes >> wide;
            CityLayout layout;
            layout.generate(seed, nodes, bool(wide));
            std::string roads, parks;
            in >> roads >> parks;
            check(roads.size() == size_t(nodes * nodes * 4) && parks.size() == size_t(nodes * nodes),
                  "fixture grid dimensions");
            for (int j = 0; j < nodes; ++j)
                for (int i = 0; i < nodes; ++i)
                {
                    check(layout.park(i, j) == (parks[i + j * nodes] == '1'), "C# park parity");
                    for (int d = 0; d < 4; ++d)
                        check(layout.road(i, j, d) == (roads[(i + j * nodes) * 4 + d] == '1'),
                              "C# street parity");
                }
            for (auto *boxes : {&layout.buildings, &layout.furniture})
            {
                size_t count;
                in >> count;
                check(count == boxes->size(), "C# obstacle count parity");
                for (auto &b : *boxes)
                {
                    double x, z, hx, hz, h;
                    in >> x >> z >> hx >> hz >> h;
                    check(std::abs(b.p.x - x) < 1e-9 && std::abs(b.p.z - z) < 1e-9 &&
                              std::abs(b.half.x - hx) < 1e-9 && std::abs(b.half.z - hz) < 1e-9 &&
                              b.height == h,
                          "C# obstacle placement parity");
                }
            }
            std::set<int> seen{0};
            std::vector<int> todo{0};
            while (!todo.empty())
            {
                int n = todo.back();
                todo.pop_back();
                for (int next : layout.neighbours(n))
                {
                    check(next >= 0 && next < nodes * nodes, "no edge leaves island");
                    auto back = layout.neighbours(next);
                    check(std::ranges::find(back, n) != back.end(), "symmetric street");
                    if (seen.insert(next).second)
                        todo.push_back(next);
                }
            }
            check(seen.size() == size_t(nodes * nodes), "all road nodes connected");
            for (int j = 0; j < nodes; ++j)
                for (int i = 0; i < nodes; ++i)
                {
                    if (layout.canalColumn(i) && !layout.bridgeRow(j))
                        check(!layout.road(i, j, 1), "canal crossing only at bridges");
                    for (int d = 0; d < 2; ++d)
                    {
                        auto mid = layout.point(i + j * nodes) + CityLayout::direction(d) * 18;
                        if (i < nodes - 1 && j < nodes - 1)
                            check((layout.roadDistance(mid) < .1) == layout.road(i, j, d),
                                  "surface agrees with street topology");
                    }
                }
            for (auto &b : layout.buildings)
                check(layout.riverDistance(b.p.x) > b.half.x + layout.bankHalf(), "no building inside canal");
            ++cities;
        }
        check(tag == "end" && cities == 12 && streams == 6, "complete independent C# fixtures");
        for (int seed : {0, 123456, 20261105})
        {
            CityLayout city;
            city.generate(seed, 49, true, {}, CityLayout::districtRevision);
            check(city.district(12, 12) == District::cottages && city.district(22, 25) == District::centre &&
                      city.district(36, 11) == District::courtyard &&
                      city.district(9, 34) == District::garages &&
                      city.district(24, 40) == District::estate &&
                      city.district(24, 43) == District::industry,
                  "revised map has six distinct districts");
            check(std::ranges::count(city.buildings, BuildingKind::wagon, &Building::kind) == 3 &&
                      std::ranges::count(city.buildings, BuildingKind::buffer, &Building::kind) == 1,
                  "freight wagons and buffer stop generated once");
            for (int i = city.yardWest(); i < city.yardEast() - 1; ++i)
            {
                check(!city.road(i, city.yardRow() + 2, 1), "freight spur reserved from road traffic");
                check(city.road(i, city.yardRow(), 1), "road continues along yard entrance");
            }
            Vec2 street{0, city.coordinate(48)}, beach{0, city.edge() - 30};
            check(!city.sandyGround(street, city.roadDistance(street)) &&
                      city.sandyGround(beach, city.roadDistance(beach)),
                  "coastal road stays asphalt");
            Terrain coast;
            coast.generate(seed, 49, city.buildings, true, &city);
            for (int k = 0; k < 3; ++k)
            {
                const double x = city.railEnd() - 11.6 - k * city.spacing;
                const double rail = coast.bedHeight(x, city.railZ());
                for (double z : {-7., -.762, .762, 7.})
                    check(std::abs(coast.bedHeight(x, city.railZ() + z) - rail) < .15,
                          "wagon rails and loading apron share a level cross-section");
            }
            const double mouth = city.coordinate(city.nodes() / 3) + 18;
            check(coast.bedHeight(mouth, city.edge() + 1) < CityLayout::waterLevel,
                  "canal outlet opens beneath the sea surface");
        }
        // Heights sampled by rendering and physics must represent the same supported surface.
        CityLayout l;
        l.generate(20261105);
        Terrain terrain;
        terrain.generate(20261105, 49, l.buildings, true, &l);
        double x = l.coordinate(49 / 3) + 18;
        for (int j = 0; j < 49; ++j)
        {
            double z = l.coordinate(j);
            if (l.bridge({x, z}))
            {
                check(terrain.height(x, z) > 3.6, "bridge supports cars");
                check(terrain.bedHeight(x, z) < -7.9, "river bed remains below bridge");
            }
            else
                check(terrain.height(x, z) < CityLayout::waterLevel, "no invisible canal bridge");
        }
        std::cout << checks << " layout checks passed; 12 original C# cities matched\n";
    }
    catch (const std::exception &e)
    {
        std::cerr << "Layout check " << checks << ": " << e.what() << '\n';
        return 1;
    }
}
