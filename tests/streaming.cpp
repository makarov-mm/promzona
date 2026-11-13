#include "promzona/detail_stream.hpp"
#include "promzona/sector_stream.hpp"
#include <chrono>
#include <cmath>
#include <iostream>
#include <memory>
#include <numeric>
#include <stdexcept>
using namespace pz;
namespace
{
void check(bool ok, const char *message)
{
    if (!ok)
        throw std::runtime_error(message);
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
            cache.trim(cells, eye);
            for (auto id : detailOrder(cells, eye, 200))
                if (!cache.contains(id))
                {
                    cache.trim(cells, eye, 20);
                    cache.add(id, 20);
                    check(cache.bytes() <= 100, "route never exceeds cache budget");
                }
        }
    bool rejected = false;
    try
    {
        cache.trim(cells, {}, 101);
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
    std::iota(ids.begin(), ids.end(), size_t{});
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
        auto order = detailOrder(shortLived.cells(), {}, detailPrefetchDistance);
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
        planSectors(row, std::vector<bool>(3), eye, 400);
    }
    catch (const std::invalid_argument &)
    {
        refused = true;
    }
    check(refused, "residency of another city is refused");
}
void sectorWorker()
{
    auto world = std::make_unique<World>();
    world->generate(20261105u);
    SectorStream stream(*world);
    const auto sectors = stream.sectors();
    const double edge = std::ceil(world->landEdge / Sector::size) * Sector::size;
    const auto side = size_t(std::lround(2 * edge / Sector::size));
    check(sectors.size() == side * side && sectors.front().x == -edge && sectors.front().z == -edge &&
              sectors.back().x == edge - Sector::size && sectors.back().z == edge - Sector::size,
          "the sectors cover the island and its coast");
    // A sector in the middle of the city, and what a direct build of it gives.
    const size_t centre = (side / 2) * side + side / 2;
    const auto reference = cityChunk(*world, sectors[centre].x, sectors[centre].z, Sector::size);
    check(chunkBytes(reference) > 100000, "a sector of the centre has geometry");
    auto same = [&](const CityChunk &a, const CityChunk &b)
    {
        for (size_t m = 0; m < a.size(); ++m)
        {
            if (a[m].indices != b[m].indices || a[m].vertices.size() != b[m].vertices.size())
                return false;
            for (size_t v = 0; v < a[m].vertices.size(); ++v)
                if ((a[m].vertices[v].position - b[m].vertices[v].position).length() > 1e-7f ||
                    (a[m].vertices[v].normal - b[m].vertices[v].normal).length() > 1e-7f ||
                    a[m].vertices[v].uv != b[m].vertices[v].uv ||
                    a[m].vertices[v].joints != b[m].vertices[v].joints)
                    return false;
        }
        return true;
    };
    // The stream keeps its own world: the owner may start another city or leave.
    world.reset();
    const auto direct = stream.build(centre);
    check(direct.id == centre && direct.bytes == chunkBytes(reference) && same(direct.geometry, reference),
          "a sector built at once is the sector of the city");
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
    check(first && first->id == all.front().id && first->bytes == chunkBytes(first->geometry),
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
    check(streamed->id == centre && streamed->bytes == direct.bytes && same(streamed->geometry, reference),
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
        std::cout << "Streaming: deterministic geometry, cancellation, bounded queues, residency and "
                     "shutdown passed\n";
        return 0;
    }
    catch (const std::exception &e)
    {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
