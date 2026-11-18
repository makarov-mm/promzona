#pragma once
#include "game_console.hpp"
#include "front_end.hpp"
#include "world.hpp"
#include <memory>

namespace Promzona
{
    struct AudioLevels;

    struct View
    {
        Vec3 eye {.x = 0, .y = 12, .z = -20 }, target{};
        float distance{20};
        double heading{};
        bool shadows{true};
        bool weapon_sight{};
        // The point the gun of an armoured vehicle is laid on. The camera stays where it is
        // when such a gun fires; the sight is drawn where this point falls on the screen.
        std::optional<Vec3> gun_sight;
    };

    struct StreamingStats
    {
        // Facade details of the apartment blocks near the eye.
        size_t resident_cells{}, total_cells{}, gpu_bytes{}, peak_gpu_bytes{}, uploads{}, evictions{}, queued{}, ready{};
        double max_upload_ms{};

        // The standing city itself, by sector.
        size_t resident_sectors{}, total_sectors{}, sector_bytes{}, peak_sector_bytes{}, sector_uploads{}, sector_evictions{}, sectors_queued{}, sectors_ready{};

        double maxSectorUploadMs{};

        // Distance from the eye to the nearest sector that is not in memory.
        float nearestMissingSector{1e9f};
    };

    class Renderer
    {
        struct Impl;
        std::unique_ptr<Impl> impl_;

    public:
        explicit Renderer(const std::filesystem::path &content);
        ~Renderer();
        void scene_mode(SceneMode mode);
        void graphics(const GraphicsPreferences &settings);
        void language(Language language);
        void profile_gpu();
        std::string gpu_report() const;
        void prepare(const World &world);
        void render(const World &world, const View &view, int width, int height);
        void overlay(const World *world, int width, int height, double fps, bool menu, const GameConsole *console, const std::wstring &message, const AudioLevels &levels, float progress = -1, bool jobsOpen = false, const FrontEnd *front = nullptr);
        void screenshot(const std::filesystem::path &file, int width, int height);
        std::string graphics_info() const;
        std::string scenery_stats() const;
        StreamingStats streaming_stats() const;
    };
}
