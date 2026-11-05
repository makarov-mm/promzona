namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public List<CitySplash> Splashes { get; } = [];

    public void Splash(CityPoint at, double size)
    {
        if (!_useScenery || (at - PlayerPosition).Length > 200) return;

        Splashes.Add(new CitySplash
        {
            Position = at,
            Size = Math.Clamp(size, .15, 3)
        });

        if (Splashes.Count > 96)
        {
            Splashes.RemoveAt(0);
        }

        Emit(at.X, CityWater.Level - CityTerrain.Ground(at) + .08, at.Z, 3, (int)Math.Clamp(size * 22, 5, 65), Math.Min(7, 1 + size * 2));
        Sound("splash", at, Math.Clamp(size * .35, .12, .9));
    }

    private void WaterStep(double dt)
    {
        for (int n = Splashes.Count - 1; n >= 0; n--)
        {
            if ((Splashes[n].Age += dt) > 3)
            {
                Splashes.RemoveAt(n);
            }
        }

        if (!_useScenery) return;

        foreach (CityPedestrian p in Pedestrians)
        {
            if (!p.Drowned && (p.Physical || p.Detached) && CityWater.Depth(p.Position) > 1.15 && p.FlightHeight < CityWater.Depth(p.Position))
            {
                Splash(p.Position, .8);
                p.Drowned = true;
                FatalPerson(p, false);
                p.BloodPool = true;
                p.Body.Vx *= .2;
                p.Body.Vy *= .2;
            }

            if (p.Drowned)
            {
                p.FlightHeight = Math.Max(0, CityWater.Depth(p.Position) - .5 - (Time - p.DeathTime) * .6);
                p.BloodPool = true;
            }
        }

        foreach (CityAnimal animal in Animals)
        {
            if (animal is { Dead: false, Physical: true, Flying: false } && CityWater.Depth(animal.Position) > .75 && animal.Height < .5)
            {
                Splash(animal.Position, .4);
                animal.Dead = true;
                animal.DeathTime = Time;
                animal.BloodPool = true;
                animal.Detached = true;

                if (animal.Body is { } body)
                {
                    Remove(body);
                }
            }
        }

        foreach (StreetProp streetProp in _liveProps)
        {
            if (streetProp.Body is { } b && CityWater.Depth(streetProp.Position) > .15)
            {
                if (streetProp.Water <= 0)
                {
                    Splash(streetProp.Position, .3);
                }

                streetProp.Water = 5;
                b.Vx *= Math.Exp(-dt * 2);
                b.Vy *= Math.Exp(-dt * 2);
            }
        }
    }
}
