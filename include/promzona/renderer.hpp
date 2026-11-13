#pragma once
#include "game_console.hpp"
#include "front_end.hpp"
#include "assets.hpp"
#include "world.hpp"
#include <memory>
namespace pz
{

struct AudioLevels;
struct View
{
    Vec3 eye{0, 12, -20}, target{};
    float distance{20};
    double heading{};
    bool shadows{true};
    bool weaponSight{};
};
struct StreamingStats
{
    // Facade details of the apartment blocks near the eye.
    size_t residentCells{}, totalCells{}, gpuBytes{}, peakGpuBytes{}, uploads{}, evictions{}, queued{},
        ready{};
    double maxUploadMs{};
    // The standing city itself, by sector.
    size_t residentSectors{}, totalSectors{}, sectorBytes{}, peakSectorBytes{}, sectorUploads{},
        sectorEvictions{}, sectorsQueued{}, sectorsReady{};
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
    void sceneMode(SceneMode mode);
    void graphics(const GraphicsPreferences &settings);
    void language(Language language);
    void profileGpu();
    std::string gpuReport() const;
    void prepare(const World &world);
    void render(const World &world, const View &view, int width, int height);
    void overlay(const World *world, int width, int height, double fps, bool menu, const GameConsole *console,
                 const std::wstring &message, const AudioLevels &levels, float progress = -1,
                 bool jobsOpen = false, const FrontEnd *front = nullptr);
    void screenshot(const std::filesystem::path &file, int width, int height);
    std::string graphicsInfo() const;
    std::string sceneryStats() const;
    StreamingStats streamingStats() const;
};
} // namespace pz
