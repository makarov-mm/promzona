#pragma once
#include "architecture.hpp"
#include <condition_variable>
#include <deque>
#include <mutex>
#include <optional>
#include "stop_thread.hpp"
#include <thread>
#include <exception>
#include <memory>

namespace Promzona
{
    // Only immutable building plans cross the worker boundary. No World, GL or actor pointers.
    struct DetailCell
    {
        Vec3 centre;
        float radius{};
        std::vector<BuildingPlan> buildings;

        float distance(const Vec3 eye) const
        {
            return std::max(0.f, (centre - eye).length() - radius);
        }
    };

    constexpr float detail_prefetch_distance = 260, detail_keep_distance = 370;
    constexpr size_t detail_gpu_budget = 64 * 1024 * 1024;
    size_t mesh_bytes(const MeshData &mesh);
    size_t chunk_bytes(const CityChunk &chunk);
    std::vector<size_t> detail_order(std::span<const DetailCell> cells, Vec3 eye, float range);

    // Owns only byte counts, not GPU objects; testable without an OpenGL context.
    class DetailResidency
    {
        std::vector<size_t> bytes_;
        size_t total_{}, limit_;

    public:
        explicit DetailResidency(const size_t& count, const size_t& limit = detail_gpu_budget)
            : bytes_(count), limit_(limit) { }

        size_t bytes() const { return total_; }
        bool contains(const size_t& id) const { return bytes_.at(id) != 0; }
        void add(size_t id, size_t bytes);

        // Remove far cells, then the most distant cells until the reservation fits.
        [[nodiscard]] std::vector<size_t> trim(std::span<const DetailCell> cells, Vec3 eye, size_t reservation = 0);
    };

    class DetailStream
    {
    public:
        struct Packet
        {
            size_t id{}, bytes{};
            CityChunk geometry;
        };

        struct Stats
        {
            size_t queued{}, ready{}, readyBytes{}, built{}, discarded{};
            bool busy{};
        };

        // At most two completed packets + one in flight; no unbounded geometry cache.
        static constexpr size_t max_packet_bytes = 16 * 1024 * 1024, max_ready = 2, max_queued = 8;
        explicit DetailStream(const World &world);
        ~DetailStream();
        DetailStream(const DetailStream &) = delete;
        DetailStream &operator=(const DetailStream &) = delete;
        std::span<const DetailCell> cells() const { return cells_; }
        void request(std::span<const size_t> ids);
        [[nodiscard]] std::unique_ptr<Packet> poll();
        Stats stats() const;

      private:
        std::vector<DetailCell> cells_;
        mutable std::mutex mutex_;
        std::condition_variable_any wake_; // the owner wakes it to stop as well
        std::vector<bool> wanted_;
        std::deque<size_t> jobs_;
        std::deque<std::unique_ptr<Packet>> ready_;
        std::optional<size_t> active_; // the cell in the making
        size_t readyBytes_{}, built_{}, discarded_{};
        std::exception_ptr error_;
        StopThread worker_;
        void run(StopToken stop);
    };
}
