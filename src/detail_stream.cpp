#include "promzona/detail_stream.hpp"
#include <map>
#include <stdexcept>
#include <ranges>

namespace Promzona
{
size_t mesh_bytes(const MeshData &m)
{
    return m.vertices.size() * sizeof(Vertex) + m.indices.size() * sizeof(std::uint32_t);
}
size_t chunk_bytes(const CityChunk &c)
{
    size_t n{};
    for (const auto &m : c)
        n += mesh_bytes(m);
    return n;
}
std::vector<size_t> detail_order(std::span<const DetailCell> cells, Vec3 eye, float range)
{
    std::vector<std::pair<float, size_t>> ranked;
    for (size_t i = 0; i < cells.size(); ++i)
        if (auto distance = cells[i].distance(eye); distance <= range)
            ranked.emplace_back(distance, i);
    std::ranges::sort(ranked);
    std::vector<size_t> result(ranked.size());
    std::ranges::copy(ranked | std::views::values, result.begin());
    return result;
}
void DetailResidency::add(size_t id, size_t bytes)
{
    if (id >= bytes_.size() || bytes == 0 || bytes_[id] != 0 || total_ + bytes > limit_)
        throw std::runtime_error("Invalid detail residency reservation");
    bytes_[id] = bytes;
    total_ += bytes;
}
std::vector<size_t> DetailResidency::trim(std::span<const DetailCell> cells, Vec3 eye, size_t reservation)
{
    if (cells.size() != bytes_.size() || reservation > limit_)
        throw std::runtime_error("Detail residency budget exceeded");
    std::vector<std::pair<float, size_t>> ranked;
    for (size_t id = 0; id < bytes_.size(); ++id)
        if (bytes_[id])
            ranked.emplace_back(cells[id].distance(eye), id);
    std::ranges::sort(ranked, std::greater<>{});
    std::vector<size_t> evict;
    for (const auto &[distance, id] : ranked)
        if (distance > detail_keep_distance || total_ + reservation > limit_)
        {
            total_ -= bytes_[id];
            bytes_[id] = 0;
            evict.push_back(id);
        }
    return evict;
}
DetailStream::DetailStream(const World &world)
{
    std::map<std::pair<int, int>, std::vector<BuildingPlan>> grouped;
    for (const auto &b : world.buildings)
    {
        if (b.kind != BuildingKind::apartment)
            continue; // Other facades are complete in the core mesh.
        grouped[{int(std::floor(b.p.x / 72)), int(std::floor(b.p.z / 72))}].push_back(
            plan_building(b, world.terrain.height(b.p)));
    }
    for (auto &[key, plans] : grouped)
    {
        Vec3 low{1e9f, 1e9f, 1e9f}, high{-1e9f, -1e9f, -1e9f};
        for (const auto &p : plans)
        {
            low = {std::min(low.x, p.origin.x - p.half_width - 2), std::min(low.y, p.origin.y - 1),
                   std::min(low.z, p.origin.z - p.half_depth - 2)};
            high = {std::max(high.x, p.origin.x + p.half_width + 2),
                    std::max(high.y, p.origin.y + p.height + 2),
                    std::max(high.z, p.origin.z + p.half_depth + 2)};
        }
        cells_.push_back({(low + high) * .5f, (high - low).length() * .5f, std::move(plans)});
    }
    wanted_.resize(cells_.size());
    worker_ = StopThread([this](StopToken stop) { run(std::move(stop)); });
}
DetailStream::~DetailStream()
{
    // The worker uses every other member, so it is stopped here and not by the order in
    // which the members happen to be declared.
    worker_.request_stop();
    {
        // Taking the lock once orders the request before the worker's next look at it.
        std::scoped_lock lock(mutex_);
    }
    wake_.notify_all();
    worker_.join();
}
void DetailStream::request(std::span<const size_t> ids)
{
    std::scoped_lock lock(mutex_);
    if (error_)
        std::rethrow_exception(error_);
    std::ranges::fill(wanted_, false);
    for (auto id : ids)
    {
        if (id >= cells_.size())
            throw std::out_of_range("Invalid streaming cell");
        wanted_[id] = true;
    }
    std::erase_if(ready_,
                  [&](const auto &p)
                  {
                      if (wanted_[p->id])
                          return false;
                      readyBytes_ -= p->bytes;
                      ++discarded_;
                      return true;
                  });
    jobs_.clear();
    for (auto id : ids)
    {
        if (jobs_.size() == max_queued)
            break;
        if (id == active_ || std::ranges::any_of(ready_, [id](const auto &p) { return p->id == id; }) ||
            std::ranges::find(jobs_, id) != jobs_.end())
            continue;
        jobs_.push_back(id);
    }
    wake_.notify_one();
}
std::unique_ptr<DetailStream::Packet> DetailStream::poll()
{
    std::scoped_lock lock(mutex_);
    if (error_)
        std::rethrow_exception(error_);
    if (ready_.empty())
        return {};
    auto result = std::move(ready_.front());
    ready_.pop_front();
    readyBytes_ -= result->bytes;
    wake_.notify_one();
    return result;
}
DetailStream::Stats DetailStream::stats() const
{
    std::scoped_lock lock(mutex_);
    return {.queued = jobs_.size(),
            .ready = ready_.size(),
            .readyBytes = readyBytes_,
            .built = built_,
            .discarded = discarded_,
            .busy = active_.has_value()};
}
void DetailStream::run(StopToken stop)
{
    try
    {
        for (;;)
        {
            size_t id;
            {
                std::unique_lock lock(mutex_);
                wake_.wait(lock, [&] { return stop.stop_requested() || (!jobs_.empty() && ready_.size() < max_ready); });
                if (stop.stop_requested())
                    return;
                id = jobs_.front();
                jobs_.pop_front();
                active_ = id;
            }
            auto packet = std::make_unique<Packet>();
            packet->id = id;
            for (const auto &p : cells_[id].buildings)
            {
                building_details(packet->geometry, p);
                if (chunk_bytes(packet->geometry) > max_packet_bytes)
                    throw std::runtime_error("Detail cell exceeds geometry budget");
            }
            packet->bytes = chunk_bytes(packet->geometry);
            {
                std::scoped_lock lock(mutex_);
                active_.reset();
                ++built_;
                if (stop.stop_requested())
                    return;
                if (wanted_[id])
                {
                    readyBytes_ += packet->bytes;
                    ready_.push_back(std::move(packet));
                }
                else
                    ++discarded_;
            }
        }
    }
    catch (...)
    {
        std::scoped_lock lock(mutex_);
        active_.reset();
        error_ = std::current_exception();
    }
}
} // namespace Promzona
