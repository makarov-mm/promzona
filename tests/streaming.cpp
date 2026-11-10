#include "promzona/detail_stream.hpp"
#include <chrono>
#include <iostream>
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
    check(count == world.buildings.size(), "every building belongs to exactly one cell");
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
} // namespace
int main()
{
    try
    {
        residency();
        worker();
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
