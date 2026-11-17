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
                for (const int sign : {-1, 1})
                {
                    std::array<float, 4> &p = planes_[axis * 2 + (sign > 0)];

                    for (int col = 0; col < 4; ++col)
                        p[col] = m.m[col * 4 + 3] + sign * m.m[col * 4 + axis];

                    float length = std::sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);

                    for (float &v : p)
                        v /= std::max(1e-8f, length);
                }
        }
        // Conservative hull of the camera volume swept toward the sun. An off-screen
        // caster is retained whenever its projected shadow can reach a visible receiver.
        Frustum extruded(const Vec3 displacement) const
        {
            Frustum result = *this;

            for (std::array<float, 4> &plane : result.planes_)
                plane[3] += std::max(0.f, -(plane[0] * displacement.x + plane[1] * displacement.y + plane[2] * displacement.z));

            return result;
        }
        bool sphere(const Vec3& p, const float radius) const
        {
            for (const std::array<float, 4> &plane : planes_)
                if (plane[0] * p.x + plane[1] * p.y + plane[2] * p.z + plane[3] < -radius)
                    return false;

            return true;
        }
    };
}
