#pragma once
#include <array>
#include <chrono>

namespace Promzona
{
    // CPU wall time, including driver waits. Enabled only by the streaming smoke profiler.
    struct FrameTrace
    {
        enum Phase : size_t
        {
            other,
            messages,
            simulation,
            camera,
            audio,
            sectors,
            details,
            prepare,
            shadow,
            probe,
            scene,
            buildings,
            fixtures,
            trees,
            sky,
            actors,
            vehicles,
            animals,
            effects,
            post,
            hudPaint,
            hudUpload,
            swap,
            count
        };

        static constexpr const char *columns = "other,messages,simulation,camera,audio,sectors,details,prepare,shadow,probe,scene,buildings,fixtures,trees,sky,actors,vehicles,animals,effects,post,hudPaint,hudUpload,swap";
        using Clock = std::chrono::steady_clock;
        using Sample = std::array<double, count>;
        bool active{};
        Sample ms{};
        Phase phase = other;
        Clock::time_point last{};

        void enter(const Phase next)
        {
            if (!active) return;
            const std::chrono::time_point<std::chrono::steady_clock> now = Clock::now();
            ms[phase] += std::chrono::duration<double, std::milli>(now - last).count();
            last = now;
            phase = next;
        }
    };

    inline thread_local FrameTrace frame_trace;
}
