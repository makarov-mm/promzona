#pragma once
#include "promzona/gl.hpp"
#include "promzona/renderer.hpp"
#include "promzona/visibility.hpp"

namespace pz
{
class EffectBatch
{
    struct Vertex
    {
        Vec3 p, color;
        float alpha, u, v, shape;
    };
    struct Quad
    {
        std::array<Vec3, 4> corners;
        Vec3 color;
        float alpha, shape, depth;
    };
    struct RainCell
    {
        int x{}, z{};
        bool valid{};
        float ground{};
    };
    std::vector<Quad> quads_;
    std::vector<Vertex> vertices_;
    std::array<RainCell, 32 * 32> rainCells_{};
    GLuint vao_{}, vbo_{};
    GLint matrix_{};
    Vec3 eye_{}, forward_{}, right_{}, up_{};
    void append(const std::array<Vec3, 4> &corners, Vec3 color, float alpha, float shape);
    void billboard(Vec3 p, Vec3 r, Vec3 u, Vec3 color, float alpha, float shape);
    void rain(const World &w, const Frustum &frustum, float daylight);
    void hydrants(const World &w, const Frustum &frustum, float daylight);
    void marks(const World &w, const Frustum &frustum, float daylight);

  public:
    EffectBatch() = default;
    EffectBatch(const EffectBatch &) = delete;
    EffectBatch &operator=(const EffectBatch &) = delete;
    ~EffectBatch();
    void initialize(GLuint shader);
    void clearWeatherCache() { rainCells_.fill({}); }
    void draw(GLuint shader, const World &w, const View &view, const Mat4 &vp);
    size_t size() const { return quads_.size(); }
};
} // namespace pz
