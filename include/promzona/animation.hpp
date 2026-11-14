#pragma once
#include "math.hpp"
#include <limits>

namespace Promzona
{
    struct ActorState
    {
        std::uint32_t id{};
        double heading{}, speed{}, distance{}, damage{}, punch{}, entry{};
        bool player{}, dead{}, knocked{}, aiming{}, scooter{};
    };

    struct ActorPose
    {
        float from{}, to{}, blend{1};
    };

    // Port of CityPersonMotion. Rendering samples this clock, never advances simulation.
    class ActorMotion
    {
        double time_{std::numeric_limits<double>::quiet_NaN()}, damage_{};
        int from_{}, to_{};
        float blend_{1}, flinch_{};

      public:
        float heading{}, lean{};
        ActorPose sample(const ActorState &actor, double time, int poseCount = 576);
    };
}
