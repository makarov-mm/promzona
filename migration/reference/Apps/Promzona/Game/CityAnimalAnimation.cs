namespace Promzona.Game;

internal static class CityAnimalAnimation
{
    // Wait is also the route-retarget timer: it does not mean the animal is idle.
    internal static float Pose(CityAnimal a, double time, float idleSeconds, float walkStride, float runStride)
    {
        if (a.Dead || a.KnockedFor > 0) return 0;
        return Phase(a, Clip(a), time, idleSeconds, walkStride, runStride);
    }

    internal static int Clip(CityAnimal a)
    {
        if (a.Dead || a.KnockedFor > 0) return 0;
        if (a.Flying) return 3;

        bool moving = a.Body is { } b
            ? b.Vx * b.Vx + b.Vy * b.Vy > .01 && (a.Physical || a.Kind is AnimalKind.Pigeon or AnimalKind.Bird || (a.Target - a.Position).Length > .5)
            : a.Distance > .01;

        return !moving ? 0 : a.Fear > .2 ? 2 : 1;
    }

    internal static float Phase(CityAnimal a, int clip, double time, float idle, float walk, float run)
    {
        if (a.Dead || a.KnockedFor > 0) return 0;

        double phase = clip switch
        {
            0 => time / Math.Max(1, idle) + a.Id * .137, 3 => time / .19 + a.Id * .137,
            _ => a.Distance / Math.Max(.05, clip == 2 ? run : walk)
        };

        return clip * 64 + (float)(phase - Math.Floor(phase)) * 64;
    }
}
