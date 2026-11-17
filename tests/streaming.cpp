#include "promzona/detail_stream.hpp"
#include "promzona/sector_stream.hpp"
#include <chrono>
#include <cstring>
#include <cmath>
#include <print>
#include <memory>
#include <numeric>
#include <stdexcept>
#include <source_location>
#include <format>
using namespace Promzona;
namespace
{
void check(bool ok, const char *message, std::source_location where = std::source_location::current())
{
    if (!ok)
        throw std::runtime_error(std::format("{} (line {})", message, where.line()));
}
template <class F> void until(F predicate)
{
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(12);
    while (!predicate())
    {
        check(std::chrono::steady_clock::now() < deadline, "streaming worker made no progress");
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
}
void residency()
{
    std::vector<DetailCell> cells(40);
    for (size_t i = 0; i < cells.size(); ++i)
        cells[i].centre = {float(i * 72), 0, 0};
    DetailResidency cache(cells.size(), 100);
    cache.add(0, 40);
    cache.add(1, 40);
    check(cache.trim(cells, {}, 0).empty(), "near cells stay resident across frames");
    auto evicted = cache.trim(cells, {}, 50);
    check(evicted == std::vector<size_t>{1}, "farthest cell evicted to reserve upload");
    cache.add(2, 50);
    check(cache.bytes() == 90, "uploaded bytes accounted");
    evicted = cache.trim(cells, {2000, 0, 0});
    check(evicted.size() == 2 && cache.bytes() == 0, "teleport releases distant meshes");
    for (int trip = 0; trip < 3; ++trip)
        for (size_t k = 0; k < cells.size(); ++k)
        {
            auto eye = cells[k].centre;
            (void)cache.trim(cells, eye);
            for (auto id : detail_order(cells, eye, 200))
                if (!cache.contains(id))
                {
                    (void)cache.trim(cells, eye, 20);
                    cache.add(id, 20);
                    check(cache.bytes() <= 100, "route never exceeds cache budget");
                }
        }
    bool rejected = false;
    try
    {
        (void)cache.trim(cells, {}, 101);
    }
    catch (const std::runtime_error &)
    {
        rejected = true;
    }
    check(rejected, "oversized upload rejected before allocation");
}
void worker()
{
    World world;
    world.generate(20261105u);
    DetailStream stream(world);
    const auto cells = stream.cells();
    check(cells.size() > 100, "city split into many bounded cells");
    size_t count{};
    for (const auto &cell : cells)
        count += cell.buildings.size();
    auto apartments = std::ranges::count(world.buildings, BuildingKind::apartment, &Building::kind);
    check(count == size_t(apartments), "every streamed apartment belongs to one cell");
    for (const auto &cell : cells)
        check(!cell.buildings.empty() && std::ranges::all_of(cell.buildings, [](const BuildingPlan &b)
                                                             { return b.kind == BuildingKind::apartment; }),
              "district meshes baked into the core have no streaming jobs");
    // The worker must use its immutable snapshot even if the owner starts a new city.
    world.buildings.clear();
    std::vector<size_t> ids(cells.size());
    std::iota(ids.begin(), ids.end(), 0uz);
    stream.request(ids);
    until([&] { return stream.stats().ready == DetailStream::maxReady; });
    auto stats = stream.stats();
    check(stats.queued <= DetailStream::maxQueued &&
              stats.readyBytes <= DetailStream::maxReady * DetailStream::maxPacketBytes,
          "worker backpressure bounds queued geometry");
    auto packet = stream.poll();
    check(packet && packet->bytes > 0, "background job returns geometry");
    CityChunk reference;
    for (const auto &plan : cells[packet->id].buildings)
        buildingDetails(reference, plan);
    check(packet->bytes == chunkBytes(reference), "streamed payload matches resident geometry size");
    for (size_t m = 0; m < reference.size(); ++m)
    {
        const auto &a = reference[m], &b = packet->geometry[m];
        check(a.indices == b.indices && a.vertices.size() == b.vertices.size(), "identical topology");
        for (size_t v = 0; v < a.vertices.size(); ++v)
        {
            check((a.vertices[v].position - b.vertices[v].position).length() < 1e-7f &&
                      (a.vertices[v].normal - b.vertices[v].normal).length() < 1e-7f &&
                      a.vertices[v].uv == b.vertices[v].uv,
                  "identical geometry, normals and UVs");
            check((b.vertices[v].position - cells[packet->id].centre).length() <=
                      cells[packet->id].radius + .01f,
                  "cell bounds contain generated facade details");
        }
    }
    stream.request({});
    until([&] { return !stream.stats().busy; });
    check(stream.stats().ready == 0 && stream.stats().queued == 0 && !stream.poll(),
          "teleport cancels all obsolete ready and queued work");
    std::array<size_t, 1> revisit{packet->id};
    stream.request(revisit);
    std::unique_ptr<DetailStream::Packet> repeat;
    until(
        [&]
        {
            repeat = stream.poll();
            return bool(repeat);
        });
    check(repeat->id == packet->id && repeat->bytes == packet->bytes, "revisiting regenerates same facade");
    for (int i = 0; i < 5; ++i)
    {
        // Destruction while the worker has outstanding jobs must join without deadlock.
        World next;
        next.generate(123u + i);
        DetailStream shortLived(next);
        auto order = detail_order(shortLived.cells(), {}, detailPrefetchDistance);
        shortLived.request(order);
    }
}
// Which sectors of the standing city are in memory for an eye and a draw distance.
void sectorPlan()
{
    std::vector<Sector> row;
    for (int i = 0; i < 20; ++i)
        row.push_back({i * Sector::size, 0});
    const Vec3 eye{72, 30, 72};
    check(row[0].distance(eye) == 0 && row[1].distance(eye) == 72 && row[4].distance(eye) == 504 &&
              Sector{-144, -144}.distance(eye) == float(std::hypot(72., 72.)),
          "distance to the square of a sector, height ignored");
    std::vector<bool> resident(row.size());
    auto plan = planSectors(row, resident, eye, 400);
    check(plan.evict.empty() && plan.load.size() == 4, "sectors within the draw distance and its slack");
    for (size_t k = 0; k < plan.load.size(); ++k)
        check(plan.load[k].id == k && plan.load[k].distance == row[k].distance(eye), "the nearest first");
    for (size_t k = 0; k < 4; ++k)
        resident[k] = true;
    plan = planSectors(row, resident, eye, 400);
    check(plan.load.empty() && plan.evict.empty(), "nothing to do while the eye stands");
    // Forty metres on, the fourth sector is past the draw distance but within the halo.
    Vec3 walked{32, 30, 72};
    check(row[3].distance(walked) == 400 && row[4].distance(walked) == 544, "walk back from the border");
    plan = planSectors(row, resident, {-8, 30, 72}, 400);
    check(plan.load.empty() && plan.evict.empty(), "a sector just out of range is kept");
    resident[3] = false;
    plan = planSectors(row, resident, {-8, 30, 72}, 400);
    check(plan.load.empty(), "but it is not built there");
    resident[3] = true;
    plan = planSectors(row, resident, {-40, 30, 72}, 400);
    check(plan.evict == std::vector<size_t>{3}, "past the halo it is released");
    // A jump across the city: the old neighbourhood goes, the new one comes nearest first.
    plan = planSectors(row, resident, {19.5f * float(Sector::size), 30, 72}, 400);
    check(plan.evict.size() == 4 && plan.load.size() == 4 && plan.load.front().id == 19 &&
              plan.load.back().id == 16,
          "a jump replaces the neighbourhood");
    // A smaller draw distance releases what a larger one had loaded.
    plan = planSectors(row, resident, eye, 130);
    check(plan.evict == (std::vector<size_t>{2, 3}) && plan.load.empty(), "the draw distance bounds memory");
    bool refused = false;
    try
    {
        (void)planSectors(row, std::vector<bool>(3), eye, 400);
    }
    catch (const std::invalid_argument &)
    {
        refused = true;
    }
    check(refused, "residency of another city is refused");
}
void checkCoastalMesh(const World &world, double edge)
{
    const double size = Sector::size;
    std::array<std::vector<std::pair<float, float>>, 2> seams;
    for (int side = 0; side < 2; ++side)
    {
        const double west = (side - 1) * size;
        const auto chunk = city_chunk(world, west, edge - size, size);
        const auto &water = chunk[Water];
        check(!water.indices.empty(), "coastal sector has water");
        for (const auto &v : water.vertices)
        {
            const auto &p = v.position;
            check(p.x >= west && p.x <= west + size && p.z >= edge - size && p.z <= edge,
                  "streamed water stays inside its sector and clear of the permanent sea");
            check(std::abs(v.uv[0] - (CityLayout::waterLevel - world.terrain.bed_height(p.x, p.z))) < .001,
                  "water depth follows the bed rather than elevated roads");
            if (p.x == 0)
                seams[side].emplace_back(p.z, v.uv[0]);
        }
        std::ranges::sort(seams[side]);
    }
    // One dry boundary cell can belong to only one mesh. All submerged samples must join.
    for (auto &seam : seams)
        std::erase_if(seam, [](const auto &sample) { return sample.second <= 0.f; });
    check(!seams.front().empty() && seams.front() == seams.back(), "wet sector edges agree");
}
void sectorWorker()
{
    auto world = std::make_unique<World>();
    world->generate(20261105u);
    SectorStream stream(*world);
    const auto sectors = stream.sectors();
    const double edge = std::ceil(world->landEdge / Sector::size) * Sector::size;
    checkCoastalMesh(*world, edge);
    const auto side = size_t(std::lround(2 * edge / Sector::size));
    check(sectors.size() == side * side && sectors.front().x == -edge && sectors.front().z == -edge &&
              sectors.back().x == edge - Sector::size && sectors.back().z == edge - Sector::size,
          "the sectors cover the island and its coast");
    // A sector in the middle of the city, and what a direct build of it gives.
    const size_t centre = (side / 2) * side + side / 2;
    std::vector<LawnTile> lawn;
    const auto reference = city_chunk(*world, sectors[centre].x, sectors[centre].z, Sector::size, &lawn);
    check(!lawn.empty(), "central sector contains lawn");
    // By construction every root lies on one of the two rendered triangles,
    // even when their four corners do not share a plane.
    const LawnTile incline{10, 20, 14, 24, 1, 3, 5, 9};
    check(incline.height(11, 21) == 2.5f && incline.height(13, 23) == 6.5f && incline.height(12, 22) == 4,
          "lawn roots agree across the terrain diagonal");
    check(chunkBytes(reference) > 100000, "a sector of the centre has geometry");
    auto same = [&](const SectorStream::Packet &packet, const CityChunk &source)
    {
        if (packet.bytes != packet.vertices.size() + packet.indices.size())
            return false;
        for (size_t i = 0; i < source.size(); ++i)
        {
            const auto &s = packet.surfaces[i];
            const auto &m = source[i];
            if (s.count != m.indices.size())
                return false;
            for (size_t v = 0; v < m.vertices.size(); ++v)
            {
                const size_t offset = (s.firstVertex + v) * packet.stride;
                if (offset + packet.stride > packet.vertices.size() ||
                    std::memcmp(&m.vertices[v], packet.vertices.data() + offset, packet.stride) ||
                    (m.vertices[v].position - s.centre).length() > s.radius + .001f)
                    return false;
            }
            size_t width = s.narrow ? 2 : 4;
            if (s.firstIndexByte % width || s.firstIndexByte + s.count * width > packet.indices.size())
                return false;
            for (size_t k = 0; k < s.count; ++k)
            {
                std::uint32_t index{};
                const auto *bytes = packet.indices.data() + s.firstIndexByte + k * width;
                if (s.narrow)
                {
                    std::uint16_t value{};
                    std::memcpy(&value, bytes, sizeof(value));
                    index = value;
                }
                else
                    std::memcpy(&index, bytes, sizeof(index));
                if (index != m.indices[k])
                    return false;
            }
        }
        return true;
    };
    CityChunk mixed;
    mixed[Road].vertices.resize(3);
    mixed[Road].indices = {0, 1, 2};
    mixed[Water].vertices.resize(65537);
    mixed[Water].indices = {65536, 0, 1};
    const auto packed = SectorStream::Packet::pack(0, mixed);
    check(packed.surfaces[Road].narrow && !packed.surfaces[Water].narrow && same(packed, mixed),
          "mixed index widths preserve material alignment and all vertex attributes");
    // The stream keeps its own world: the owner may start another city or leave.
    world.reset();
    const auto direct = stream.build(centre);
    check(direct.lawn.size() == lawn.size(), "resident sector carries the rendered lawn tiles");
    check(direct.id == centre && same(direct, reference), "a sector built at once is the sector of the city");
    std::vector<SectorOrder> all;
    for (size_t id = 0; id < sectors.size(); ++id)
        all.push_back({id, sectors[id].distance({})});
    std::ranges::stable_sort(all, {}, &SectorOrder::distance);
    stream.request(all);
    until([&] { return stream.stats().ready == SectorStream::maxReady; });
    auto stats = stream.stats();
    check(stats.queued <= SectorStream::maxQueued && stats.readyBytes > 0,
          "the worker waits for the renderer");
    auto first = stream.poll();
    check(first && first->id == all.front().id &&
              first->bytes == first->vertices.size() + first->indices.size(),
          "the nearest sector arrives first");
    stream.request({});
    until([&] { return !stream.stats().busy; });
    check(stream.stats().ready == 0 && stream.stats().queued == 0 && !stream.poll(),
          "a jump of the eye cancels what was queued and finished");
    const std::array<SectorOrder, 1> again{SectorOrder{centre, 0}};
    stream.request(again);
    std::unique_ptr<SectorStream::Packet> streamed;
    until(
        [&]
        {
            streamed = stream.poll();
            return bool(streamed);
        });
    check(streamed->id == centre && streamed->bytes == direct.bytes && same(*streamed, reference),
          "the worker delivers the same sector");
    bool refused = false;
    try
    {
        const std::array<SectorOrder, 1> bad{SectorOrder{sectors.size(), 0}};
        stream.request(bad);
    }
    catch (const std::out_of_range &)
    {
        refused = true;
    }
    check(refused, "a sector outside the city is refused");
    for (int i = 0; i < 3; ++i)
    {
        // Leaving while the worker still has sectors to build must not hang.
        World next;
        next.generate(321u + i);
        SectorStream shortLived(next);
        std::vector<SectorOrder> orders;
        for (size_t id = 0; id < shortLived.sectors().size(); ++id)
            orders.push_back({id, 0});
        shortLived.request(orders);
    }
}
} // namespace
int main()
{
    try
    {
        residency();
        worker();
        sectorPlan();
        sectorWorker();
        std::print("{}", "Streaming: deterministic geometry, cancellation, bounded queues, residency and "
                         "shutdown passed\n");
        return 0;
    }
    catch (const std::exception &e)
    {
        std::println(stderr, "{}", e.what());
        return 1;
    }
}
