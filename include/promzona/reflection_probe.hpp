#pragma once
#include "math.hpp"

namespace Promzona
{
    // The local reflection probe: a cube map of the static city and the sky around the
    // player. This is the bookkeeping of the managed game's CityReflections without its GL:
    // when the cube is taken again, and how its faces look out. One face is rendered a
    // frame while a refresh runs, into a second cube, so that no frame pays for six views
    // and the cube in use is always complete.
    class ReflectionProbe
    {
        Vec3 centre_{}, pending_{};
        double hour_{-100}, rain_{-100}, pendingHour_{}, pendingRain_{}, nextTime_{};
        int face_{-1}, generation_{}, captured_{};
        bool ready_{};

    public:
        static constexpr int size = 128; // pixels along a face
        static constexpr float nearPlane = .2f, farPlane = 180;
        // Faces in the order of a cube map: +x, -x, +y, -y, +z, -z.
        static Vec3 direction(int face)
        {
            static constexpr Vec3 directions[6] = {
                { .x = 1,  .y = 0,  .z = 0},  
                { .x = -1, .y = 0,  .z = 0}, 
                { .x = 0,  .y = 1,  .z = 0},
                { .x = 0,  .y = -1, .z = 0}, 
                { .x = 0,  .y = 0,  .z = 1},  
                { .x = 0,  .y = 0,  .z = -1}
            };

            return directions[face];
        }

        static Vec3 up(const int face)
        {
            static constexpr Vec3 ups[6] = {{0, -1, 0}, {0, -1, 0}, {0, 0, 1},
                                            {0, 0, -1}, {0, -1, 0}, {0, -1, 0}};
            return ups[face];
        }

        static Mat4 view(const Vec3 centre, const int face)
        {
            return Mat4::look_at(centre, centre + direction(face), up(face));
        }

        static Mat4 projection()
        {
            return Mat4::perspective(pi / 2, 1, nearPlane, farPlane);
        }

        // The face to render in this frame, or -1 while the finished cube is still good. A
        // new cube is begun when there is none, when its centre is 22 m behind, when the
        // light or the weather has changed, when other scenery is loaded (`generation`),
        // and every twelve seconds.
        int begin(const Vec3& centre, const double hour, const double rain, const double time, const int generation)
        {
            if (face_ < 0)
            {
                if (ready_ && (centre - centre_).length() < 22 && std::abs(hour - hour_) < .25 && std::abs(rain - rain_) < .12 && generation == generation_ && time < nextTime_ && time > nextTime_ - 13)
                    return -1;

                pending_ = centre;
                pendingHour_ = hour;
                pendingRain_ = rain;
                generation_ = generation;
                face_ = 0;
            }

            return face_;
        }

        // The face that begin() gave is rendered. True when it was the sixth: the new cube
        // is complete and takes the place of the old one.
        bool finish(const double time)
        {
            ++captured_;
            if (++face_ < 6)
                return false;
            centre_ = pending_;
            hour_ = pendingHour_;
            rain_ = pendingRain_;
            ready_ = true;
            face_ = -1;
            nextTime_ = time + 12;
            return true;
        }

        bool ready() const { return ready_; }

        // Centre of the finished cube, and of the one being taken.
        Vec3 centre() const { return centre_; }
        Vec3 pending() const { return pending_; }
        int captured() const { return captured_; }
        void reset() { *this = {}; }
    };
}
