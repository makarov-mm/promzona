#pragma once
#include "math.hpp"

namespace Promzona
{
// Extract the six OpenGL clip planes once per camera, not for every object.
class Frustum
{
    std::array<std::array<float, 4>, 6> planes_{};

  public:
    explicit Frustum(const Mat4 &m)
    {
        for (int axis = 0; axis < 3; ++axis)
            for (int sign : {-1, 1})
            {
                auto &p = planes_[axis * 2 + (sign > 0)];
                for (int col = 0; col < 4; ++col)
                    p[col] = m.m[col * 4 + 3] + sign * m.m[col * 4 + axis];
                float length = std::sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
                for (auto &v : p)
                    v /= std::max(1e-8f, length);
            }
    }
    bool sphere(Vec3 p, float radius) const
    {
        for (auto &plane : planes_)
            if (plane[0] * p.x + plane[1] * p.y + plane[2] * p.z + plane[3] < -radius)
                return false;
        return true;
    }
};
} // namespace Promzona
