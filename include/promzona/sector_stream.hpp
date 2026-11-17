#pragma once
#include "city_mesh.hpp"
#include <condition_variable>
#include <deque>
#include <memory>
#include <mutex>
#include <optional>
#include <span>
#include "stop_thread.hpp"
#include <thread>
#include <exception>

namespace Promzona
{
    // The city that stands still is cut into square sectors. Which of them are in memory
    // follows the eye and the draw distance, as the district streaming of the managed game
    // did (CityDistricts): a sector is built when it comes into range, the nearest first,
    // and released when it is left behind.
    struct Sector
    {
        static constexpr double size = 144;
        double x{}, z{}; // the lower corner

        // Horizontal distance from the eye to the square; zero inside it.
        float distance(Vec3 eye) const
        {
            const double dx = std::max({x - eye.x, 0., eye.x - (x + size)}),
                         dz = std::max({z - eye.z, 0., eye.z - (z + size)});
            return static_cast<float>(std::hypot(dx, dz));
        }
    };

    // A sector is wanted while its square is within the draw distance plus `reach`: the
    // slack keeps in memory everything a draw at that distance can show. It is kept for
    // `keep` metres more, so that a walk along the border does not rebuild it every step.
    // A sector this near the eye cannot wait for the worker and is built at once.
    constexpr float sectorReach = 30, sectorKeep = 40, sectorUrgent = 60;

    struct SectorOrder
    {
        size_t id{};
        float distance{};
    };

    struct SectorPlan
    {
        std::vector<SectorOrder> load; // the nearest first
        std::vector<size_t> evict;
    };

    // What to build and what to release for an eye and a draw distance. `resident` tells
    // which sectors are in memory or on their way into it.
    [[nodiscard]] SectorPlan planSectors(std::span<const Sector> sectors, const std::vector<bool> &resident, Vec3 eye, float range);

    // Builds sectors on a worker thread from its own copy of the world, so that nothing
    // the worker reads can change or go away under it.
    class SectorStream
    {
    public:
        struct Packet
        {
            size_t id{}, bytes{};
            struct Surface
            {
                size_t firstVertex{}, firstIndexByte{}, count{};
                Vec3 centre{};
                float radius{};
                bool narrow{};
            };
            std::array<Surface, SurfaceCount> surfaces{};
            std::vector<unsigned char> vertices, indices;
            std::vector<LawnTile> lawn;
            static constexpr size_t stride = offsetof(Vertex, weights);
            [[nodiscard]] static Packet pack(size_t id, const CityChunk &geometry);
        };

        struct Stats
        {
            size_t queued{}, ready{}, readyBytes{}, built{}, discarded{};
            bool busy{};
        };

        // At most two finished sectors wait for the renderer, and one is in the making.
        static constexpr size_t maxReady = 2, maxQueued = 16;
        explicit SectorStream(const World &world);
        ~SectorStream();
        SectorStream(const SectorStream &) = delete;
        SectorStream &operator=(const SectorStream &) = delete;
        std::span<const Sector> sectors() const { return sectors_; }
        // The same sector the worker would deliver, built by the caller: for the loading
        // screen and for a sector the eye has jumped into.
        [[nodiscard]] Packet build(size_t id) const;
        // The sectors still to come, the nearest first. Anything else queued or finished
        // is dropped: after a jump of the eye the old neighbourhood is no longer wanted.
        void request(std::span<const SectorOrder> orders);
        [[nodiscard]] std::unique_ptr<Packet> poll();
        Stats stats() const;

    private:
        std::unique_ptr<const World> world_;
        std::vector<Sector> sectors_;
        mutable std::mutex mutex_;
        std::condition_variable_any wake_; // the owner wakes it to stop as well
        std::vector<bool> wanted_;
        std::deque<size_t> jobs_;
        std::deque<std::unique_ptr<Packet>> ready_;
        std::optional<size_t> active_; // the sector in the making
        size_t readyBytes_{}, built_{}, discarded_{};
        std::exception_ptr error_;
        StopThread worker_;
        void run(StopToken stop);
    };
}
