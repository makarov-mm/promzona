#pragma once
#include "math.hpp"
#include <optional>

namespace Promzona
{
    // A local orthographic sun map. Its xy origin is snapped in light space, so
    // a sub-texel camera motion does not make stationary shadows crawl.
    struct SunShadow
    {
        static constexpr int resolution = 2048;
        static constexpr float halfSize = 110, depth = 520;
        static constexpr int farResolution = 1024;
        static constexpr float farHalfSize = 330;
        Mat4 matrix;
        Vec3 centre;
        float strength{};
    };

    inline SunShadow sunShadow(Vec3 focus, Vec3 sun, float extent = SunShadow::halfSize,
                               int resolution = SunShadow::resolution)
    {
        SunShadow s;
        Vec3 light = sun.unit();
        s.strength = static_cast<float>(smooth((light.y - .025) / .10));
        auto right = cross(light * -1, Vec3{0, 1, 0}).unit();
        if (right.length() < .1f)
            right = {1, 0, 0};
        auto up = cross(right, light * -1);
        const float texel = 2 * extent / float(resolution);
        const float depth = std::max(SunShadow::depth, 2.4f * extent);
        auto snap = [texel](float v) { return std::floor(v / texel + .5f) * texel; };
        s.centre = focus + right * (snap(dot(right, focus)) - dot(right, focus)) + up * (snap(dot(up, focus)) - dot(up, focus));
        // Explicit basis remains valid with a sun directly above the observer.
        Mat4 view = Mat4::identity();
        auto eye = s.centre + light * (depth * .5f);
        view.m = {right.x, up.x, light.x, 0, right.y, up.y, light.y, 0, right.z, up.z, light.z, 0, -dot(right, eye), -dot(up, eye), -dot(light, eye), 1};
        Mat4 projection = Mat4::identity();
        projection.m[0] = projection.m[5] = 1 / extent;
        projection.m[10] = -2 / depth;
        projection.m[14] = -1;
        s.matrix = projection * view;
        return s;
    }

    class FarShadowCache
    {
        struct Map
        {
            Mat4 matrix;
            Vec3 focus, sun;
            size_t revision;
        };
        std::optional<Map> front_, pending_;
        int frontTarget_{1};

      public:
        enum class Portion
        {
            none,
            full,
            left,
            right
        };
        struct Update
        {
            Portion portion{Portion::none};
            int readTarget{1}, writeTarget{1};
            Mat4 visible{Mat4::identity()}, draw{Mat4::identity()};
        };

        Update advance(bool enabled, Vec3 focus, Vec3 sun, size_t revision)
        {
            if (!enabled)
            {
                *this = {};
                return {};
            }
            auto capture = [&]
            {
                return Map{sunShadow(focus, sun, SunShadow::farHalfSize, SunShadow::farResolution).matrix,
                           focus, sun, revision};
            };
            Update update;
            const float motion = front_ ? (front_->focus - focus).length() : 0.f;
            const float rotation = front_ ? (front_->sun - sun).length() : 0.f;
            if (!front_ || motion > 24.f || rotation > .06f)
            {
                pending_.reset();
                front_ = capture();
                update.portion = Portion::full;
            }
            else if (pending_)
            {
                front_ = pending_;
                pending_.reset();
                frontTarget_ = 3 - frontTarget_;
                update.portion = Portion::right;
            }
            else if (motion > 4.f || rotation > .002f || front_->revision != revision)
            {
                pending_ = capture();
                update.portion = Portion::left;
            }
            // Publish the new centre, projection and texture together, only after both halves draw.
            update.readTarget = frontTarget_;
            update.writeTarget = pending_ ? 3 - frontTarget_ : frontTarget_;
            update.visible = front_->matrix;
            update.draw = pending_ ? pending_->matrix : front_->matrix;
            return update;
        }
    };
    } // namespace Promzona
