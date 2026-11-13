#include "promzona/sector_stream.hpp"
#include "promzona/detail_stream.hpp"
#include <algorithm>
#include <stdexcept>

namespace pz
{
SectorPlan planSectors(std::span<const Sector> sectors, const std::vector<bool> &resident, Vec3 eye,
                       float range)
{
    if (sectors.size() != resident.size())
        throw std::invalid_argument("Sector residency does not match the city");
    SectorPlan plan;
    for (size_t id = 0; id < sectors.size(); ++id)
    {
        const float distance = sectors[id].distance(eye);
        if (!resident[id])
        {
            if (distance <= range + sectorReach)
                plan.load.push_back({id, distance});
        }
        else if (distance > range + sectorReach + sectorKeep)
            plan.evict.push_back(id);
    }
    std::ranges::stable_sort(plan.load, {}, &SectorOrder::distance);
    return plan;
}
SectorStream::SectorStream(const World &world) : world_(std::make_unique<const World>(world))
{
    // The sectors cover the whole island and its coast, whatever the size of the grid.
    const double low = -std::ceil(world.landEdge / Sector::size) * Sector::size;
    for (double z = low; z < -low; z += Sector::size)
        for (double x = low; x < -low; x += Sector::size)
            sectors_.push_back({x, z});
    wanted_.resize(sectors_.size());
    worker_ = std::thread([this] { run(); });
}
SectorStream::~SectorStream()
{
    {
        std::lock_guard lock(mutex_);
        stopping_ = true;
        jobs_.clear();
    }
    wake_.notify_all();
    if (worker_.joinable())
        worker_.join();
}
SectorStream::Packet SectorStream::build(size_t id) const
{
    const auto &sector = sectors_.at(id);
    Packet packet;
    packet.id = id;
    packet.geometry = cityChunk(*world_, sector.x, sector.z, Sector::size);
    packet.bytes = chunkBytes(packet.geometry);
    return packet;
}
void SectorStream::request(std::span<const SectorOrder> orders)
{
    std::lock_guard lock(mutex_);
    if (error_)
        std::rethrow_exception(error_);
    std::fill(wanted_.begin(), wanted_.end(), false);
    for (const auto &order : orders)
    {
        if (order.id >= sectors_.size())
            throw std::out_of_range("Invalid sector");
        wanted_[order.id] = true;
    }
    std::erase_if(ready_,
                  [&](const auto &packet)
                  {
                      if (wanted_[packet->id])
                          return false;
                      readyBytes_ -= packet->bytes;
                      ++discarded_;
                      return true;
                  });
    jobs_.clear();
    for (const auto &order : orders)
    {
        if (jobs_.size() == maxQueued)
            break;
        if (order.id == active_ ||
            std::ranges::any_of(ready_, [&](const auto &packet) { return packet->id == order.id; }) ||
            std::ranges::find(jobs_, order.id) != jobs_.end())
            continue;
        jobs_.push_back(order.id);
    }
    wake_.notify_one();
}
std::unique_ptr<SectorStream::Packet> SectorStream::poll()
{
    std::lock_guard lock(mutex_);
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
SectorStream::Stats SectorStream::stats() const
{
    std::lock_guard lock(mutex_);
    return {jobs_.size(), ready_.size(), readyBytes_, built_, discarded_, active_ != size_t(-1)};
}
void SectorStream::run()
{
    try
    {
        for (;;)
        {
            size_t id;
            {
                std::unique_lock lock(mutex_);
                wake_.wait(lock, [&] { return stopping_ || (!jobs_.empty() && ready_.size() < maxReady); });
                if (stopping_)
                    return;
                id = jobs_.front();
                jobs_.pop_front();
                active_ = id;
            }
            auto packet = std::make_unique<Packet>(build(id));
            {
                std::lock_guard lock(mutex_);
                active_ = size_t(-1);
                ++built_;
                if (stopping_)
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
        std::lock_guard lock(mutex_);
        active_ = size_t(-1);
        error_ = std::current_exception();
    }
}
} // namespace pz
