#pragma once
#include "assets.hpp"
#include "world.hpp"
#include <memory>
namespace pz
{
struct View
{
    Vec3 eye{0, 12, -20}, target{};
    float distance{20};
    double heading{};
    bool shadows{true};
};
struct StreamingStats
{
    size_t residentCells{}, totalCells{}, gpuBytes{}, peakGpuBytes{}, uploads{}, evictions{}, queued{},
        ready{};
    double maxUploadMs{};
};
class Renderer
{
    struct Impl;
    std::unique_ptr<Impl> impl_;

  public:
    explicit Renderer(const std::filesystem::path &content);
    ~Renderer();
    void prepare(const World &world);
    void render(const World &world, const View &view, int width, int height);
    void overlay(const World *world, int width, int height, double fps, bool menu, bool console,
                 const std::wstring &command, const std::wstring &message, float progress = -1, bool jobsOpen = false);
    void screenshot(const std::filesystem::path &file, int width, int height);
    std::string graphicsInfo() const;
    std::string sceneryStats() const;
    StreamingStats streamingStats() const;
};
} // namespace pz
