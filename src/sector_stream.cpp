#include "promzona/sector_stream.hpp"
#include <algorithm>
#include <cstddef>
#include <cstring>
#include <stdexcept>
#include <utility>
#include <ranges>

namespace Promzona
{
    SectorPlan planSectors(std::span<const Sector> sectors, const std::vector<bool> &resident, Vec3 eye, float range)
    {
        if (sectors.size() != resident.size())
            throw std::invalid_argument("Sector residency does not match the city");

        SectorPlan plan;

        for (size_t id = 0; id < sectors.size(); ++id)
        {
            const float distance = sectors[id].distance(eye);

            if (!resident[id])
            {
                if (distance <= range + sector_reach)
                    plan.load.push_back({
                        .id = id,
                        .distance = distance
                    });
            }
            else if (distance > range + sector_reach + sector_keep)
            {
                plan.evict.push_back(id);
            }
        }

        std::ranges::stable_sort(plan.load, {}, &SectorOrder::distance);
        return plan;
    }

    SectorStream::SectorStream(const World &world) : world_(std::make_unique<const World>(world))
    {
        // The sectors cover the whole island and its coast, whatever the size of the grid.
        const double low = -std::ceil(World::landEdge / Sector::size) * Sector::size;

        for (double z = low; z < -low; z += Sector::size)
            for (double x = low; x < -low; x += Sector::size)
            {
                sectors_.push_back({
                    .x = x,
                    .z = z
                });
            }

        wanted_.resize(sectors_.size());
        worker_ = StopThread([this](StopToken stop) { run(std::move(stop)); });
    }

    SectorStream::~SectorStream()
    {
        // The worker uses every other member, so it is stopped here and not by the order
        // in which the members happen to be declared.
        worker_.request_stop();
        {
            // Taking the lock once orders the request before the worker's next look at it.
            std::scoped_lock lock(mutex_);
        }

        wake_.notify_all();
        worker_.join();
    }

    SectorStream::Packet SectorStream::Packet::pack(const size_t id, const CityChunk &geometry)
    {
        static_assert(stride == 48 && offsetof(Vertex, joints) == 32);
        Packet packet;
        packet.id = id;
        size_t vertex_bytes{}, index_bytes{};

        for (auto &&[mesh, surface] : std::views::zip(geometry, packet.surfaces))
        {
            if (mesh.indices.empty())
                continue;

            surface.narrow = mesh.vertices.size() <= 65536;
            const size_t width = surface.narrow ? 2 : 4;
            index_bytes = index_bytes + width - 1 & ~(width - 1);
            surface.first_vertex = vertex_bytes / stride;
            surface.first_index_byte = index_bytes;
            surface.count = mesh.indices.size();
            vertex_bytes += mesh.vertices.size() * stride;
            index_bytes += surface.count * width;
        }

        packet.vertices.resize(vertex_bytes);
        packet.indices.resize(index_bytes);
        packet.bytes = vertex_bytes + index_bytes;

        for (auto &&[mesh, s] : std::views::zip(geometry, packet.surfaces))
        {
            if (!s.count)
                continue;

            unsigned char *dst = packet.vertices.data() + s.first_vertex * stride;
            Vec3 lo = mesh.vertices.front().position, hi = lo;

            for (const auto &v : mesh.vertices)
            {
                std::memcpy(dst, &v, stride);
                dst += stride;
                auto p = v.position;
                lo = {std::min(lo.x, p.x), std::min(lo.y, p.y), std::min(lo.z, p.z)};
                hi = {std::max(hi.x, p.x), std::max(hi.y, p.y), std::max(hi.z, p.z)};
            }

            s.centre = (lo + hi) * .5f;
            s.radius = (hi - lo).length() * .5f;
            auto *indices = packet.indices.data() + s.first_index_byte;

            if (s.narrow)
            {
                for (const uint32_t index : mesh.indices)
                {
                    const auto narrow = static_cast<std::uint16_t>(index);
                    std::memcpy(indices, &narrow, sizeof(narrow));
                    indices += sizeof(narrow);
                }
            }
            else
                std::memcpy(indices, mesh.indices.data(), s.count * sizeof(std::uint32_t));
        }
        return packet;
    }

    SectorStream::Packet SectorStream::build(size_t id) const
    {
        const auto &sector = sectors_.at(id);
        std::vector<LawnTile> lawn;
        auto packet = Packet::pack(id, city_chunk(*world_, sector.x, sector.z, Sector::size, &lawn));
        packet.lawn = std::move(lawn);
        return packet;
    }

    void SectorStream::request(std::span<const SectorOrder> orders)
    {
        std::scoped_lock lock(mutex_);

        if (error_)
            std::rethrow_exception(error_);

        std::ranges::fill(wanted_, false);

        for (const SectorOrder &order : orders)
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

        for (const SectorOrder &order : orders)
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

    SectorStream::Stats SectorStream::stats() const
    {
        std::scoped_lock lock(mutex_);

        return {
            .queued = jobs_.size(), 
            .ready = ready_.size(), 
            .ready_bytes = readyBytes_, 
            .built = built_,
            .discarded = discarded_, 
            .busy = active_.has_value()
        };
    }

    void SectorStream::run(StopToken stop)
    {
        try
        {
            for (;;)
            {
                size_t id;
                {
                    std::unique_lock lock(mutex_);
                    wake_.wait(lock, [&] { return stop.stop_requested() || (!jobs_.empty() && ready_.size() < maxReady); });
                    if (stop.stop_requested())
                        return;
                    id = jobs_.front();
                    jobs_.pop_front();
                    active_ = id;
                }
                auto packet = std::make_unique<Packet>(build(id));
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
                    {
                        ++discarded_;
                    }
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
}
