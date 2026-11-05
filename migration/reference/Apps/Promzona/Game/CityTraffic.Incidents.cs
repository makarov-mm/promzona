namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public int Respawns { get; private set; }
    public static readonly (int I, int J)[] ClinicBlocks = [(14, 14), (19, 16), (12, 20), (20, 10)];
    public static bool IsClinic(int i, int j) => ClinicBlocks.Contains((i, j));
    // The militia department (29.10.2026): an arrested player is released at its
    // doorway. One block, the same public-building footprint as a clinic.
    public static readonly (int I, int J) StationBlock = (21, 13);
    public static bool IsStation(int i, int j) => (i, j) == StationBlock;

    public void DamageCar(CityCar c, double amount, bool playerCaused = false)
    {
        if (c.Destroyed || c.Falling || amount <= 0 || GodMode&&PlayerControls(c)) return;
        if(c.Kind==11)amount*=.22;
        amount *= .5; // Twice the impact budget, for every vehicle type.
        c.LastDamageAt = Time;
        c.SafeDrivingTime = 0;
        if (playerCaused) c.PlayerDamageUntil = Time + 45;
        c.Damage = Math.Min(1.5, c.Damage + amount);
        if (c.Burning) c.FireAge += amount * 12;
        if (c is { Damage: >= .68, Burning: false })
        {
            c.Burning = true;
            c.FireAge = 0;
            c.PanicDriver = !PlayerControls(c) && Random() < .5;
            Sound("fire", c.Position, .6);
        }

        // Player fire is recoverable; fresh damage can still destroy a critical car.
        if (PlayerControls(c) && c.HealthRemaining <= 0) Explode(c);
    }

    private void VehicleLifeBefore(double dt)
    {
        foreach (var c in Cars)
        {
            c.DoorHold=Math.Max(0,c.DoorHold-dt);
            double doorTarget=c.DoorHold>0||EnteringCarId==c.Id&&EntryProgress>0?1:0;
            c.DoorOpen+=Math.Clamp(doorTarget-c.DoorOpen,-dt*2.4,dt*3.5);
            if(c.PoliceVehicle&&c.Burning&&!c.CrewDeployed&&!PlayerControls(c)){
                c.PanicDriver=false;c.Command=new(0,0,true);
                if(Math.Abs(c.Speed)<1.2)DeployCrew(c,Mission(c),false);
            }

            if (c.Falling)
            {
                c.FallTime += dt;
                c.VerticalVelocity = Math.Max(-1.1,c.VerticalVelocity-dt*.65);
                c.Lift += c.VerticalVelocity * dt;
                c.Body.Vx*=Math.Exp(-dt*2);c.Body.Vy*=Math.Exp(-dt*2);
                c.Body.X += c.Body.Vx * dt;
                c.Body.Y += c.Body.Vy * dt;
                // Other cars leave the list after three seconds; the player's own
                // car stays at index 0 for the whole session. It comes to rest on
                // the canal bed (GroundHeight is the water level, so the bed is
                // Lift = -Depth) instead of sinking and tumbling without bound
                // until the respawn swaps it out of the hero slot.
                // Legacy 33-node islands have a flat heightfield ending above
                // sea level. Outside that old boundary there is open water,
                // not a zero-depth floor at the heightfield's clamped edge.
                double depth = CityWater.Depth(new(c.Body.X, c.Body.Y));
                bool outside = Math.Max(Math.Abs(c.Body.X), Math.Abs(c.Body.Y)) > LandEdge;
                double bed = outside && !CityLayout.Rivers ? -Math.Max(2, depth) : -depth;
                if (c.Lift <= bed) { c.Lift = bed; c.VerticalVelocity = 0; }
                else c.Pitch += dt * .35;
                continue;
            }

            if ((_useScenery&&CityWater.Depth(c.Position)>.85&&c.Lift<CityWater.Depth(c.Position))||(Math.Abs(c.Position.X)>LandEdge||Math.Abs(c.Position.Z)>LandEdge)&&!(c.Kind==12&&c.Lift>3))
            {
                c.Falling = true;
                c.FallTime = 0;
                Remove(c.Body);
                c.Burning=false;c.PanicDriver=false;c.GroundHeight=CityWater.Level;c.TerrainActive=true;c.Lift=.15;c.VerticalVelocity=-.15;FreePoliceReservation(c);Splash(c.Position,2.2);
                continue;
            }

            if (c.Destroyed) continue;
            // Normalize critical vehicles from older saves before any fire recovery.
            if (PlayerControls(c) && c.HealthRemaining <= 0)
            {
                Explode(c);
                continue;
            }

            if (c.Burning)
            {
                double previousAge = c.FireAge;
                c.FireAge += dt;
                if ((int)(previousAge / 3) != (int)(c.FireAge / 3)) Sound("fire", c.Position, .5);
                if ((c.Position - PlayerPosition).Length < 190 && Noise() < dt * 35)
                {
                    Emit(c.Position.X, 1.1 + c.Lift, c.Position.Z, 4, 2, 1.6,c.RoadLevel);
                    Emit(c.Position.X, 1.6 + c.Lift, c.Position.Z, 1, 2, 1.4,c.RoadLevel);
                }

                if (PlayerControls(c))
                {
                    if (Time - c.LastDamageAt > 4 && Math.Abs(c.Speed) > 2 && c.Grounded) c.SafeDrivingTime += dt;
                    if (c.SafeDrivingTime >= 18)
                    {
                        c.Burning = false;
                        c.FireAge = c.SafeDrivingTime = 0;
                        c.PanicDriver = false;
                    }
                }
                else if (c.FireAge > 12) Explode(c);
            }
        }
    }

    private void Explode(CityCar c)
    {
        if (c.Destroyed) return;
        c.Destroyed = true;
        c.Burning = false;
        c.DeadSince = Time;
        c.Damage = 1.5;
        RestoreWreckBody(c);
        c.Physical = PlayerControls(c) || Detailed(c.Position, c.Physical);
        Attach(c.Body, c.Physical && !c.Falling);
        if (c.Reservation >= 0)
        {
            _reservations[c.Reservation] = -1;
            c.Reservation = -1;
        }

        bool playerCaused = PlayerControls(c) || c.PlayerDamageUntil > Time;
        Sound("explosion", c.Position, 1);
        double blastY = c.SurfaceHeight + c.Lift + c.Height * .5;
        Emit(c.Position.X, c.Lift + 1, c.Position.Z, 4, 55, 7,c.RoadLevel);
        Emit(c.Position.X, c.Lift + 1.5, c.Position.Z, 1, 32, 3,c.RoadLevel);
        Emit(c.Position.X, c.Lift + 1, c.Position.Z, 2, 40, 8,c.RoadLevel);
        AddIncident(c.Position, true, playerCaused);
        // Arcade area damage, with distance falloff and solid-building occlusion.
        // Iterate authoritative actors so a blast also works across the physics LOD.
        foreach (var other in Cars)
        {
            if (other == c || other.Falling) continue;
            var d = other.Position - c.Position;
            double vertical = other.SurfaceHeight + other.Lift + other.Height * .5 - blastY;
            double r = Math.Sqrt(d.Length * d.Length + vertical * vertical);
            if (r >= 10 || !ClearLevelSight(c.Position,c.RoadLevel,other.Position,other.RoadLevel)) continue;
            double strength = 1 - r / 10;
            DamageCar(other, 2.1 * strength, playerCaused);
            double impulse = 5 * strength * 1250 / other.Mass;
            other.Body.Vx += d.X / Math.Max(.5, r) * impulse;
            other.Body.Vy += d.Z / Math.Max(.5, r) * impulse;
            other.VerticalVelocity += strength * 1.4;
            other.WreckSupported = false;
            other.Body.Wake();
        }

        foreach (var p in Pedestrians)
        {
            var d = p.Position - c.Position;
            double vertical = (_useScenery ? CityRoadLevels.Ground(p) : 0) + p.FlightHeight + .9 - blastY;
            double r = Math.Sqrt(d.Length * d.Length + vertical * vertical);
            if (p.Dead || r >= 7.5 || !ClearLevelSight(c.Position,c.RoadLevel,p.Position,p.RoadLevel)) continue;
            p.KnockedFor = 4;
            p.VerticalSpeed = 2.5;
            p.FlightHeight = .02;
            p.Body.Vx = d.X / Math.Max(.5, r) * 6;
            p.Body.Vy = d.Z / Math.Max(.5, r) * 6;
            Remove(p.Body);
            p.Detached = true;
            p.VoiceRevision++;
            p.Aiming = false;
            FatalPerson(p, playerCaused);
        }

        foreach (var a in Animals)
        {
            var d = a.Position - c.Position;
            double vertical = (_useScenery ? CityTerrain.Ground(a.Position) : 0) + a.Height + .3 - blastY;
            double r = Math.Sqrt(d.Length * d.Length + vertical * vertical);
            if (a.Dead || a.Height > 3 || r >= 6 || !ClearLevelSight(c.Position,c.RoadLevel,a.Position,0)) continue;
            a.Dead = true;
            a.DeathTime = Time;
            a.Flying = false;
            a.KnockedFor = 4;
            a.VerticalSpeed = 2;
            a.Detached = true;
            if (a.Body is { } b)
            {
                Remove(b);
                b.Vx = d.X / Math.Max(.5, r) * 4;
                b.Vy = d.Z / Math.Max(.5, r) * 4;
            }
        }

    }

    private void VehicleLifeAfter(double dt)
    {
        for (int n = Cars.Count - 1; n >= 1; n--)
        {
            var c = Cars[n];
            // An ownerless parked car - a patrol car after its crew got out, a
            // stolen car the player walked away from - leaves with its scene
            // once it is out of view, like a wreck. Before this, abandoned cars
            // accumulated at every chase scene up to the car cap. The player's
            // own car (index 0), cars on a service mission and the car of the
            // running contract (a handover vehicle is parked driverless ~130 m
            // away; a detour past 245 m used to delete it and fail the job) are
            // never removed.
            bool abandoned = c.OwnedGarageId==0 && c.Driverless && !c.Falling && !c.Destroyed && c.Id != EnteringCarId && c.Id != ContractActorId && Mission(c) is null &&
                             (c.Position - PlayerPosition).Length > SpawnDistance;
            if (c.AirPatrol && c.AirDeparting && !c.Hero && (c.Position-PlayerPosition).Length>SpawnDistance || abandoned || (c.Falling && c.FallTime > 3) || (c.Destroyed && Time - c.DeadSince > 6 &&
                                                  (c.Position - PlayerPosition).Length > SpawnDistance))
            {
                if (c.Reservation >= 0) _reservations[c.Reservation] = -1;
                Remove(c.Body);
                Cars.RemoveAt(n);
                ExitedCars++;
            }
        }

        if (!OnFoot && (Hero.Falling && Hero.FallTime > 3 || Hero.Destroyed && Time - Hero.DeadSince > 3))
            RespawnPlayer();
    }

    private void RespawnPlayer()
    {
        if(!PlaceAtClinic())return;
        Respawns++;
        Sound("respawn", PlayerPosition, .5);
    }

    private void UpdateOvertake(CityCar c, double dt)
    {
        if(CityLayout.WideRoads){c.OvertakeOffset=0;return;}
        double target = 0;
        var f = new CityPoint(Math.Sin(c.Heading), Math.Cos(c.Heading));
        CityPoint right = Right(f);
        CityPoint nominal = c.Path.Sample(c.Along).Point;
        bool obstacle = false;

        if (c.Phase == 0 && c.Path.Length - c.Along > 8)
        {
            foreach (var p in NearbyPeople(c.Position, 20))
            {
                CityPoint d = p.Position - c.Position;
                double ahead = CityPoint.Dot(d, f);

                if (p.Dead && ahead > -c.HalfLength - 3 && ahead < 18 &&
                    Math.Abs(CityPoint.Dot(p.Position - nominal, right)) < c.HalfWidth + 1)
                {
                    obstacle = true;
                }
            }

            foreach (var car in NearbyCars(c.Position, 22))
            {
                CityPoint d = car.Position - c.Position;
                double ahead = CityPoint.Dot(d, f);

                if (car.Destroyed && ahead > -car.HalfLength - c.HalfLength - 2 && ahead < 19 &&
                    Math.Abs(CityPoint.Dot(car.Position - nominal, right)) < c.HalfWidth + car.HalfWidth)
                    obstacle = true;
            }

            if (obstacle)
            {
                bool safe = true;
                CityPoint shifted = c.Position - right * (2 * Lane);

                foreach (var other in NearbyCars(c.Position, 48))
                {
                    if (other == c || other.Destroyed) continue;
                    CityPoint future = other.Position + new CityPoint(other.Body.Vx, other.Body.Vy) * 2;
                    CityPoint d = future - shifted;
                    double along = CityPoint.Dot(d, f);

                    if (along is > -8 and < 32 
                        &&
                        Math.Abs(CityPoint.Dot(d, right)) < c.HalfWidth + other.HalfWidth + .7)
                    {
                        safe = false;
                        break;
                    }
                }

                if (safe) target = -Lane * 2;
            }
        }

        c.OvertakeOffset += Math.Clamp(target - c.OvertakeOffset, -dt * 1.2, dt * 1.2);
    }
}
