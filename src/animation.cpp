#include "promzona/animation.hpp"

namespace Promzona
{
ActorPose ActorMotion::sample(const ActorState &p, double time, int poseCount)
{
    const bool running = p.speed > (p.player ? 3.8 : 2.1);
    int clip = p.dead || p.knocked         ? 0
               : p.scooter                 ? 5
               : p.punch > 0               ? 4
               : p.aiming && p.speed < .12 ? 3
               : p.aiming                  ? (running ? 7 : 6)
               : p.speed < .1              ? 0
               : running                   ? 2
                                           : 1;
    // Legacy 193-frame models have no gun, scooter, or punch cycles.
    auto compatible = [poseCount](int c)
    {
        int start = c == 5 ? 384 : c == 6 ? 448 : c == 7 ? 512 : c * 64;
        return start + (c == 3 ? 1 : 64) <= poseCount ? c : c == 7 ? 2 : c == 6 ? 1 : 0;
    };
    clip = compatible(clip);
    double dt = time - time_;
    if (!std::isfinite(time_) || dt < 0 || dt > .5)
    {
        from_ = to_ = clip;
        blend_ = 1;
        heading = float(p.heading);
        flinch_ = 0;
    }
    else if (dt > 0)
    {
        if (clip != to_)
        {
            if (clip == from_)
            {
                std::swap(from_, to_);
                blend_ = 1 - blend_;
            }
            else
            {
                from_ = blend_ < .5f ? from_ : to_;
                to_ = clip;
                blend_ = 0;
            }
        }
        blend_ = std::min(1.f, blend_ + float(dt / (clip == 3 || clip == 4 ? .10 : .18)));
        heading += float(wrap(p.heading - heading) * (1 - std::exp(-dt * 18)));
        flinch_ *= std::exp(float(-dt * 9));
        if (p.damage > damage_ + .001)
            flinch_ = .23f;
    }
    if (p.dead || p.knocked)
    {
        from_ = to_ = 0;
        blend_ = 1;
        flinch_ = 0;
    }
    lean = flinch_ + float(.30 * std::sin(clamp(p.entry, 0., 1.) * pi));
    damage_ = p.damage;
    time_ = time;
    auto phase = [&](int c) -> float
    {
        c = compatible(c);
        switch (c)
        {
        case 0:
            return float(std::fmod(time / 4.1666665 + p.id * .137, 1.)) * 64;
        case 1:
            return 64 + float(std::fmod(p.distance * .6875, 1.)) * 64;
        case 2:
            return 128 + float(std::fmod(p.distance * .5, 1.)) * 64;
        case 3:
            return 192;
        case 5:
            return 384;
        case 6:
            return 448 + float(std::fmod(p.distance * .6875, 1.)) * 64;
        case 7:
            return 512 + float(std::fmod(p.distance * .5, 1.)) * 64;
        default:
            return 256 + float(clamp(p.punch * 63, 0., 63.));
        }
    };
    return {phase(from_), phase(to_), blend_};
}
} // namespace Promzona
