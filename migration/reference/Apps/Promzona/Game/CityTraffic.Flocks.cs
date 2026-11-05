namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private double _flockClock;

    private void MaintainFlocks(double dt)
    {
        _flockClock -= dt;
        if (_flockClock > 0) return;
        _flockClock = .35;

        foreach (IGrouping<int, CityAnimal> group in Animals
            .Where(animal => animal is { Kind: AnimalKind.Pigeon, Dead: false, Flying: false })
            .GroupBy(animal => animal.Flock))
        {
            CityAnimal leader = group.First();
            if (leader.Flock < 0) continue;

            if (leader.FlockHome == default)
            {
                int rank = 0;

                foreach (CityAnimal animal in group)
                {
                    animal.FlockHome = leader.Position;
                    animal.FlockNeedsGather = true;
                    double angle = rank * 2.399963, spread = .6 * Math.Sqrt(rank++);
                    animal.FlockOffset = new CityPoint(Math.Sin(angle) * spread, Math.Cos(angle) * spread);
                    animal.WanderAt = 0;
                }
            }

            // Old saves contain flocks that used to wander independently. Re-form
            // them only when every member and its destination are beyond view.
            if (group.Any(animal => animal.FlockNeedsGather) 
                &&
                group.All(animal => (animal.Position - PlayerPosition).Length > SpawnDistance)
                && 
                group.All(animal => (animal.FlockHome + animal.FlockOffset - PlayerPosition).Length > SpawnDistance && ClearWalk(animal.FlockHome + animal.FlockOffset) && !OnCarriageway(animal.FlockHome + animal.FlockOffset)))
            {
                foreach (CityAnimal animal in group)
                {
                    animal.Position = animal.FlockHome + animal.FlockOffset;
                    animal.Target = animal.Position;
                    animal.FlockNeedsGather = false;
                    animal.Physical = false;

                    if (animal.Body is { } b)
                    {
                        Remove(b);
                        b.X = animal.Position.X;
                        b.Y = animal.Position.Z;
                        b.Vx = b.Vy = 0;
                    }
                }
            }

            if (Time < leader.WanderAt) continue;
            CityPoint goal = leader.FlockHome;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                double angle = Random() * Math.Tau;
                CityPoint candidate = leader.FlockHome + new CityPoint(Math.Sin(angle), Math.Cos(angle)) * (Random() * 1.6);

                if (group.All(a => ClearWalk(candidate + a.FlockOffset) && !OnCarriageway(candidate + a.FlockOffset)))
                {
                    goal = candidate;
                    break;
                }
            }

            double next = Time + 4 + Random() * 5;

            foreach (CityAnimal animal in group)
            {
                animal.Target = goal + animal.FlockOffset;
                animal.WanderAt = next;
            }
        }
    }

    private CityPoint FlockVelocity(CityAnimal animal)
    {
        CityPoint d = animal.Target - animal.Position;

        if (d.Length < .12)
        {
            return default;
        }

        double speed = Math.Min(d.Length > 4 ? 1 : .45, d.Length * 1.8);
        CityPoint v = d * (speed / d.Length);

        if (!ClearWalk(animal.Position + v * .6))
        {
            CityPoint side = Right(v);
            v = ClearWalk(animal.Position + side * .6) ? side : ClearWalk(animal.Position - side * .6) ? side * -1 : default;
        }

        if (v.Length > .05)
        {
            animal.Heading = Math.Atan2(v.X, v.Z);
        }

        return v;
    }

    private void LaunchFlock(CityAnimal source)
    {
        if (source.Flying) return;
        CityPoint d = source.Position - source.FearFrom;
        double heading = d.Length > .1 ? Math.Atan2(d.X, d.Z) : source.Heading;

        foreach (CityAnimal animal in Animals)
        {
            if (animal == source || (source.Flock >= 0 && animal.Flock == source.Flock))
            {
                if (animal.Dead || animal.Flying) continue;
                animal.Flying = true;
                animal.FlightTime = 0;
                animal.Fear = Math.Max(.5, animal.Fear);
                animal.FearFrom = source.FearFrom;
                animal.Heading = heading + (animal.Id % 5 - 2) * .08;
            }
        }

        Sound("wings", source.Position, .45);
    }
}
