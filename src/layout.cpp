#include "promzona/layout.hpp"
#include <limits>
#include <stdexcept>
#include <bit>

namespace pz
{
namespace
{
int subtract(int a, int b)
{
    return std::bit_cast<int>(std::uint32_t(a) - std::uint32_t(b));
}
int add(int a, int b)
{
    return std::bit_cast<int>(std::uint32_t(a) + std::uint32_t(b));
}
} // namespace
LayoutRandom::LayoutRandom(std::int32_t seed)
{
    constexpr int big = 2147483647;
    int subtraction = seed == std::numeric_limits<int>::min() ? big : std::abs(seed);
    int mj = 161803398 - subtraction, mk = 1;
    values_[55] = mj;
    for (int i = 1; i < 55; ++i)
    {
        int ii = (21 * i) % 55;
        values_[ii] = mk;
        mk = subtract(mj, mk);
        if (mk < 0)
            mk = add(mk, big);
        mj = values_[ii];
    }
    for (int k = 1; k < 5; ++k)
        for (int i = 1; i < 56; ++i)
        {
            // Explicit two's-complement wrap reproduces unchecked .NET arithmetic,
            // including int.MinValue/int.MaxValue seeds, without C++ signed overflow.
            int value = subtract(values_[i], values_[1 + (i + 30) % 55]);
            if (value < 0)
                value = add(value, big);
            values_[i] = value;
        }
}
double LayoutRandom::next()
{
    if (++next_ >= 56)
        next_ = 1;
    if (++other_ >= 56)
        other_ = 1;
    int value = subtract(values_[next_], values_[other_]);
    if (value == 2147483647)
        --value;
    if (value < 0)
        value = add(value, 2147483647);
    values_[next_] = value;
    return value * (1. / 2147483647);
}
int LayoutRandom::index(int maximum)
{
    if (maximum <= 0)
        throw std::invalid_argument("Empty layout random range");
    return int(next() * maximum);
}
Vec2 CityLayout::direction(int d)
{
    constexpr std::array<Vec2, 4> f{{{0, 1}, {1, 0}, {0, -1}, {-1, 0}}};
    return f[d & 3];
}
bool CityLayout::clinic(int i, int j)
{
    return (i == 14 && j == 14) || (i == 19 && j == 16) || (i == 12 && j == 20) || (i == 20 && j == 10);
}
bool CityLayout::allowedEdge(int i, int j, int d) const
{
    return d % 2 == 0 || !canalColumn(d == 1 ? i : i - 1) || bridgeRow(j);
}
bool CityLayout::road(int i, int j, int d) const
{
    return i >= 0 && j >= 0 && i < nodes_ && j < nodes_ && !roads_.empty() &&
           roads_[(i + j * nodes_) * 4 + (d & 3)];
}
bool CityLayout::park(int i, int j) const
{
    return i >= 0 && j >= 0 && i < nodes_ - 1 && j < nodes_ - 1 && !parks_.empty() && parks_[i + j * nodes_];
}
std::vector<int> CityLayout::neighbours(int node) const
{
    std::vector<int> result;
    for (int d = 0; d < 4; ++d)
        if (road(node % nodes_, node / nodes_, d))
        {
            auto f = direction(d);
            result.push_back(node + int(f.x) + int(f.z) * nodes_);
        }
    return result;
}
int CityLayout::degree(int node) const
{
    int n = 0;
    for (int d = 0; d < 4; ++d)
        n += road(node % nodes_, node / nodes_, d) ? 1 : 0;
    return n;
}
double CityLayout::roadDistance(Vec2 p) const
{
    int bi = int(std::floor((p.x - coordinate(0)) / spacing)),
        bj = int(std::floor((p.z - coordinate(0)) / spacing));
    double distance = 1e9;
    for (int j = bj - 1; j <= bj + 1; ++j)
        for (int i = bi - 1; i <= bi + 1; ++i)
            for (int d = 0; d < 2; ++d)
                if (road(i, j, d))
                {
                    auto a = Vec2{coordinate(i), coordinate(j)}, f = direction(d);
                    // Square caps produce square junctions, matching the original roads.
                    auto q = p - a;
                    double along = dot(q, f);
                    distance = std::min(distance,
                                        std::max(std::abs(cross(q, f)), std::max(-along, along - spacing)));
                }
    return distance;
}
double CityLayout::riverDistance(double x) const
{
    return rivers() ? std::min(std::abs(x - coordinate(nodes_ / 3) - 18),
                               std::abs(x - coordinate(2 * nodes_ / 3) - 18))
                    : 1e9;
}
bool CityLayout::bridge(Vec2 p) const
{
    if (!rivers() || riverDistance(p.x) > deckHalf())
        return false;
    int row = int(std::nearbyint((p.z - coordinate(0)) / spacing));
    int column = std::abs(p.x - coordinate(nodes_ / 3) - 18) < std::abs(p.x - coordinate(2 * nodes_ / 3) - 18)
                     ? nodes_ / 3
                     : 2 * nodes_ / 3;
    return bridgeRow(row) && road(column, row, 1) && std::abs(p.z - coordinate(row)) < (wide_ ? 10.55 : 8.05);
}
double CityLayout::deckHeight(double x) const
{
    double c = std::cos(pi * std::min(1., riverDistance(x) / deckHalf()) * .5);
    return 2.2 + 1.5 * c * c;
}
bool CityLayout::urbanSite(Vec2 p, double radius) const
{
    return std::max(std::abs(p.x), std::abs(p.z)) + radius <= coordinate(nodes_ - 1) + 7 &&
           riverDistance(p.x) > 18 + radius;
}
bool CityLayout::green(int i, int j, int d, double time)
{
    double phase = std::fmod(time + (i * 3 + j * 5) % 9, 28.);
    return d % 2 == 0 ? phase >= 14 && phase < 26 : phase < 12;
}
void CityLayout::generate(std::int32_t seed, int n, bool wide, std::function<void(float)> progress)
{
    if (n != 33 && n != 49)
        throw std::invalid_argument("City size must be 33 or 49 nodes");
    nodes_ = n;
    wide_ = wide;
    roads_.assign(n * n * 4, false);
    parks_.assign(n * n, false);
    buildings.clear();
    furniture.clear();
    LayoutRandom random(seed);
    auto report = [&](double p)
    {
        if (progress)
            progress(float(p));
    };
    auto link = [&](int i, int j, int d)
    {
        auto f = direction(d);
        int x = i + int(f.x), z = j + int(f.z);
        roads_[(i + j * n) * 4 + d] = true;
        if (x >= 0 && z >= 0 && x < n && z < n)
            roads_[(x + z * n) * 4 + (d + 2) % 4] = true;
    };
    std::vector<bool> seen(n * n);
    std::vector<int> stack{n / 2 + n * (n / 2)};
    seen[stack.back()] = true;
    int visited = 1;
    while (!stack.empty())
    {
        int i = stack.back() % n, j = stack.back() / n;
        std::vector<int> options;
        for (int d = 0; d < 4; ++d)
        {
            auto f = direction(d);
            int x = i + int(f.x), z = j + int(f.z);
            if (x >= 0 && z >= 0 && x < n && z < n && !seen[x + z * n] && allowedEdge(i, j, d))
                options.push_back(d);
        }
        if (options.empty())
        {
            stack.pop_back();
            continue;
        }
        int d = options[random.index(int(options.size()))];
        link(i, j, d);
        auto f = direction(d);
        int at = i + int(f.x) + (j + int(f.z)) * n;
        seen[at] = true;
        stack.push_back(at);
        report(.18 * ++visited / (n * n));
    }
    for (int j = 0; j < n; ++j)
        for (int i = 0; i < n; ++i)
        {
            for (int d = 0; d < 2; ++d)
            {
                auto f = direction(d);
                int x = i + int(f.x), z = j + int(f.z);
                bool artery = d == 0 ? (i % 8 == 0 || i == n / 2) : (j % 8 == 0 || j == n / 2);
                if (x < n && z < n && allowedEdge(i, j, d) && (artery || random.next() < .61))
                    link(i, j, d);
            }
            report(.18 + .07 * (i + j * n + 1) / (n * n));
        }
    link(n / 2, n / 2, 2);
    for (int j = 0; j < n - 1; ++j)
        for (int i = 0; i < n - 1; ++i)
        {
            size_t begin = buildings.size();
            double x = coordinate(i) + 18, z = coordinate(j) + 18;
            bool isPark =
                !clinic(i, j) && (random.next() < .19 || (i / 3 + j / 4 + std::abs(seed % 7)) % 19 == 0);
            parks_[i + j * n] = isPark && !canalColumn(i);
            auto add = [&](double px, double pz, double hx, double hz, double h)
            {
                // A visual material index is derived, not drawn from the layout random stream.
                int style = int((std::uint32_t(i) * 31u + std::uint32_t(j) * 17u +
                                 std::uint32_t(buildings.size() - begin)) %
                                8);
                buildings.push_back({{px, pz}, {hx, hz}, h, style});
            };
            if (!isPark && !canalColumn(i))
            {
                int style = random.index(6);
                if (clinic(i, j))
                {
                    add(x, z, 9, 8, 8);
                    style = -1;
                }
                if (style == 0)
                {
                    // Preserve C# left-to-right evaluation (C++ function arguments are unordered).
                    double px = x + random.next() * 2 - 1, hx = 9 + random.next() * 2,
                           hz = 7 + random.next() * 2;
                    double h = 5 + random.index(4) * 3;
                    add(px, z, hx, hz, h);
                }
                else if (style == 1)
                {
                    add(x, z - 6, 11, 4, 10 + random.index(5) * 3);
                    add(x, z + 8, 7, 2.4, 2.8);
                }
                else if (style >= 0)
                {
                    for (int k = 0; k < 4; ++k)
                    {
                        double hx = 3.4 + random.next() * 1.4, hz = 3.4 + random.next() * 1.4;
                        double px = x + (k % 2 == 0 ? -7 : 7) + random.next() - .5,
                               pz = z + (k < 2 ? -7 : 7) + random.next() - .5;
                        double h = 4 + random.index(7) * 2.5;
                        add(px, pz, hx, hz, h);
                    }
                    if (style == 2)
                        add(x, z, 2.2, 1.65, 2.8);
                }
            }
            if (isPark && !canalColumn(i) && (i + j) % 4 == 0)
                furniture.push_back({{x + 9, z + 9}, {.15, .15}, 3.95});
            if (station(i, j) && !canalColumn(i))
            {
                parks_[i + j * n] = false;
                buildings.resize(begin);
                add(x, z, 9, 8, 8);
            }
            report(.25 + .16 * (i + j * (n - 1) + 1) / ((n - 1) * (n - 1)));
        }
    for (int j = 0; j < n; ++j)
        for (int i = 0; i < n; ++i)
        {
            double x = coordinate(i), z = coordinate(j), offset = wide ? 8.2 : 5.15;
            for (int side : {-1, 1})
            {
                if (road(i, j, 0))
                {
                    furniture.push_back({{x + side * offset, z + 12}, {.09, .09}, 4.2});
                    furniture.push_back({{x + side * offset, z + 18}, {.25, .25}, .9});
                }
                if (road(i, j, 1) && !canalColumn(i))
                {
                    furniture.push_back({{x + 12, z + side * offset}, {.09, .09}, 4.2});
                    furniture.push_back({{x + 18, z + side * offset}, {.25, .25}, .9});
                }
            }
            report(.41 + .04 * (i + j * n + 1) / (n * n));
        }
    if (wide)
        for (auto &b : buildings)
        {
            int i = int(std::floor((b.p.x - coordinate(0)) / 36)),
                j = int(std::floor((b.p.z - coordinate(0)) / 36));
            if (clinic(i, j) || station(i, j))
                continue;
            auto centre = Vec2{coordinate(i) + 18, coordinate(j) + 18};
            b.p = centre + (b.p - centre) * .73;
            b.half *= .73;
        }
    std::erase_if(furniture,
                  [&](auto &b)
                  {
                      if (!urbanSite(b.p, .65))
                          return true;
                      for (auto ij : std::array<Vec2, 5>{{{14, 14}, {19, 16}, {12, 20}, {20, 10}, {21, 13}}})
                      {
                          auto entrance = Vec2{coordinate(int(ij.x)) + 18, coordinate(int(ij.z)) + 27.25};
                          if ((entrance - b.p).length() <= 3)
                              return true;
                      }
                      return false;
                  });
}
} // namespace pz
