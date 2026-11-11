#include "promzona/layout.hpp"
#include "promzona/road_levels.hpp"
#include <limits>
#include <stdexcept>
#include <bit>

namespace pz
{
void CityLayout::setRoad(int i, int j, int d, bool value)
{
    auto f = direction(d);
    int x = i + int(f.x), z = j + int(f.z);
    if (i < 0 || j < 0 || x < 0 || z < 0 || i >= nodes_ || j >= nodes_ || x >= nodes_ || z >= nodes_)
        return;
    roads_[(i + j * nodes_) * 4 + d] = value;
    roads_[(x + z * nodes_) * 4 + (d + 2) % 4] = value;
}
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
void residentialPlot(std::vector<Building> &out, District district, Vec2 centre, unsigned variant)
{
    using K = BuildingKind;
    auto place = [&](double x, double z, double hx, double hz, double h, int style, K kind = K::apartment,
                     int front = 1)
    { out.push_back({centre + Vec2{x, z}, {hx, hz}, h, style, kind, front}); };
    switch (district)
    {
    case District::cottages:
        place(-6, -5, 4.4, 5, 4.5 + .15 * variant, variant, K::cottage, -1);
        place(6, 5, 4, 4.5, 4.8, (variant + 1) % 4, K::cottage);
        break;
    case District::garages:
        for (int n : {-1, 0, 1})
            for (int side : {-1, 1})
                place(n * 6.7, side * 7, 3.1, 4, 3.4, variant, K::garage, -side);
        break;
    case District::courtyard:
        place(0, -8.6, 11, 3.2, 12.5, variant);
        place(-8.5, 1.7, 3.2, 7.1, 12.5, variant);
        place(8.5, 4.5, 3.2, 4.3, 10, variant);
        break;
    case District::centre:
        place(-6.5, 0, 4.7, 10, 19 + variant * 3, variant + 4);
        place(6.5, 0, 4.7, 9, 14 + variant * 2, (variant + 1) % 4 + 4);
        break;
    case District::estate:
        for (int side : {-1, 1})
            place(0, side * 7, 11, 3.8, 17 + variant * 3, variant + 2);
        break;
    case District::industry:
        break;
    }
}

void industrialPlot(CityLayout &l, int i, int j, Vec2 centre)
{
    using K = BuildingKind;
    auto place = [&](K kind, double x, double z, double hx, double hz, double h)
    { l.buildings.push_back({centre + Vec2{x, z}, {hx, hz}, h, (i + j) % 4, kind, -1}); };
    const double fence = l.sidewalk() + 1.3 - 18;
    auto gates = [&]
    {
        for (int side : {-1, 1})
            place(K::fence, side * 5.3, fence, 3.3, .12, 2.2);
    };
    if (j == l.yardRow() && (i == l.yardWest() + 3 || i == l.yardEast() - 3))
    {
        for (int side : {-1, 1})
            place(K::garage, side * 2.85, -5, 2.45, 2.8, 2.85);
    }
    else if (j == l.yardRow() && i == l.yardEast() - 7)
    {
        for (int n : {-1, 0, 1})
            place(K::stall, n * 5.2, -5, 2, 1.6, 2.7);
    }
    else
        switch ((i * 3 + j * 5 + i / 2) % 4)
        {
        case 0:
            place(K::hall, 0, 1.5, 8.4, 6, 9 + (i % 3) * 1.5);
            place(K::chimney, 6.6, -5.5, .9, .9, 24 + (j % 2) * 5);
            gates();
            break;
        case 1:
            for (int n : {-1, 0, 1})
                place(K::tank, n * 5.6, 3, 2.5, 2.5, 7 + ((n + 1) % 2) * 1.5);
            place(K::pump, 0, -4.5, 2.5, 2, 3.2);
            gates();
            break;
        case 2:
            place(K::shed, 0, 2.5, 8.4, 4.5, 6);
            place(K::shed, 0, -5.5, 5, 2, 3.5);
            break;
        default:
            place(K::hall, -2.5, 2, 6, 6.5, 6.5);
            for (int n = 0; n < 4; ++n)
                place(K::crate, 5.8 + n % 2 * 2., -6 + n / 2 * 2.2, .9, .9, 1 + n % 3 * .5);
            gates();
        }
    if (j != l.yardRow() + 2)
        return;
    for (int k = 0; k < 3; ++k)
    {
        const double x = l.railEnd() - 9.6 - 2 - k * l.spacing;
        if (x >= l.coordinate(i) && x < l.coordinate(i + 1))
            place(K::wagon, x - centre.x, l.railZ() - centre.z, 9.6, 1.45, 3.8);
    }
    if (l.railEnd() >= l.coordinate(i) && l.railEnd() < l.coordinate(i + 1))
        place(K::buffer, l.railEnd() + .4 - centre.x, l.railZ() - centre.z, .5, 1.3, 1.5);
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
bool CityLayout::industrial(int i, int j) const
{
    return revision_ == districtRevision && i >= yardWest() && i < yardEast() && j >= yardRow() &&
           j < yardRow() + 5;
}
bool CityLayout::railEdge(int i, int j, int d) const
{
    const int from = d == 1 ? i : i - 1;
    return revision_ == districtRevision && d % 2 == 1 && j == yardRow() + 2 && from >= yardWest() &&
           from < yardEast() - 1;
}
District CityLayout::district(int i, int j) const
{
    if (industrial(i, j))
        return District::industry;
    struct Centre
    {
        int i, j;
        District kind;
    };
    static constexpr Centre centres[] = {{12, 12, District::cottages},  {39, 36, District::cottages},
                                         {22, 25, District::centre},    {9, 34, District::garages},
                                         {36, 11, District::courtyard}, {24, 41, District::estate}};
    double best = std::numeric_limits<double>::max(), scale = (nodes_ - 1) / 48.;
    District result = District::estate;
    for (auto c : centres)
    {
        Vec2 delta{i - (c.i + seed_ % 5 - 2) * scale, j - (c.j + (seed_ / 5) % 5 - 2) * scale};
        if (delta.length2() < best)
        {
            best = delta.length2();
            result = c.kind;
        }
    }
    return result;
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
bool CityLayout::sandyGround(Vec2 p, double streetDistance) const
{
    if (riverDistance(p.x) < bankHalf()) return true;
    const double rim = std::max(std::abs(p.x), std::abs(p.z)) - coordinate(nodes_ - 1);
    if (revision_ == 0) return rim > 12.;
    return rim >= 0 && streetDistance > roadHalf();
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
void CityLayout::generate(std::int32_t seed, int n, bool wide, std::function<void(float)> progress, int revision)
{
    if (n != 33 && n != 49)
        throw std::invalid_argument("City size must be 33 or 49 nodes");
    if (revision != 0 && (revision != districtRevision || n != 49 || !wide))
        throw std::invalid_argument("Unsupported city layout revision");
    seed_ = seed;
    revision_ = revision;
    nodes_ = n;
    wide_ = wide;
    const auto levels = revision ? planRoadLevels(*this, std::uint32_t(seed)) : std::vector<RoadLevel>{};
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
            if (x >= 0 && z >= 0 && x < n && z < n && !seen[x + z * n] && allowedEdge(i, j, d) && !railEdge(i,j,d))
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
                if (x < n && z < n && allowedEdge(i, j, d) && (artery || random.next() < .61) && !railEdge(i,j,d))
                    link(i, j, d);
            }
            report(.18 + .07 * (i + j * n + 1) / (n * n));
        }
    for (const auto &level : levels)
    {
        int left = level.leftNode % n, row = level.leftNode / n;
        for (int i = left; i <= left + 8; ++i)
            for (int j = row - 1; j <= row + 2; ++j)
            {
                if (i < left+8) link(i,j,1);
                if (j < row+2) link(i,j,0);
            }
        for (int dx : {1,2,6,7}) setRoad(left+dx,row,0,false);
    }
    link(n / 2, n / 2, 2);
    if (revision)
        for (int i = yardWest(); i < yardEast()-1; ++i) link(i,yardRow(),1);
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
            const bool reserved = std::ranges::any_of(levels, [&](const RoadLevel &s) {
                constexpr double plotRadius = 16.;
                const double dx = std::abs(x-s.x);
                const double centre = s.z + 18*smooth((s.length-dx)/36.);
                return dx <= s.length+plotRadius+1.3 && std::abs(z-centre) <= s.halfWidth+plotRadius+1.3 &&
                       (!s.tunnel || dx+plotRadius > s.covered-.8);
            });
            if (!isPark && !canalColumn(i) && !reserved)
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
            if (industrial(i,j))
            {
                parks_[i+j*n] = false;
                buildings.resize(begin);
                industrialPlot(*this,i,j,{x,z});
            }
            else if (revision && !isPark && !canalColumn(i) && !reserved && !clinic(i,j) && !station(i,j))
            {
                buildings.resize(begin);
                unsigned variant = (std::uint32_t(seed) + i*7919u + j*104729u) % 4;
                residentialPlot(buildings,district(i,j),{x,z},variant);
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
            if (clinic(i, j) || station(i, j) || industrial(i,j))
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
