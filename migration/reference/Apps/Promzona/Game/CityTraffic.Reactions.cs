namespace Promzona.Game;

public readonly record struct FearResponse(double Radius,double Gain,double Recovery,bool Fly);

public sealed class CityShot { public CityPoint From,To;public double Height,EndHeight,Age; }

public sealed partial class CityTraffic
{
    public List<CityShot> Shots { get; } = [];
    public int ShotsFired { get; private set; }

    public bool ClearSight(CityPoint from, CityPoint to)
    {
        if (_useScenery &&
            !CityTerrain.Current.ClearLine(from, CityTerrain.Ground(from) + 1, to, CityTerrain.Ground(to) + 1))
            return false;
        var d = to - from;
        int count = Math.Max(1, (int)Math.Ceiling(d.Length / .6));
        for (int i = 1; i < count; i++)
            if (!ClearWalk(from + d * (i / (double)count)))
                return false;
        return true;
    }

    private void CombatStep(double dt)
    {
        for (int i = Shots.Count - 1; i >= 0; i--)
            if ((Shots[i].Age += dt) > .12)
                Shots.RemoveAt(i);
        RocketStep(dt);
        PlayerCombat();
        foreach (var p in Pedestrians)
        {
            if(p.IsPlayer) continue;
            var weapon = (!p.Physical || p.Dead || p.KnockedFor > 0) ? null : CityWeapons.Find(p.Weapon);
            if (weapon is null)
            {
                p.Aiming = false;
                p.AimTime = 0;
                p.BurstRemaining = 0;
                continue;
            }

            if (!OnFoot && Threat(Hero, p.Position, out _)) p.AggressionUntil = Time + (p.Persona == 3 ? 8 : 5);
            var delta = PlayerPosition - p.Position;
            bool aim = CoverAllowsAim(p) && Time < p.AggressionUntil && !PlayerUnavailable && delta.Length < 24 && delta.Length > 3 &&
                       !(p.Officer && WantedLevel < 4 && Surrendering) &&
                       ClearLevelSight(p.Position,p.RoadLevel,PlayerPosition,OnFoot?Player.RoadLevel:Hero.RoadLevel);
            p.Aiming = aim;
            if (!aim)
            {
                p.AimTime = 0;
                p.BurstRemaining = 0;
                continue;
            }

            double desired = Math.Atan2(delta.X, delta.Z);
            p.Heading += Math.Clamp(Math.IEEERemainder(desired - p.Heading, Math.Tau), -dt * 6, dt * 6);
            p.AimTime += dt;
            if (p.AimTime < .65 || Time < p.ShotAt ||
                Math.Abs(Math.IEEERemainder(desired - p.Heading, Math.Tau)) > .12) continue;
            if (p.BurstRemaining <= 0) p.BurstRemaining = weapon.NpcBurst;
            p.BurstRemaining--;
            p.ShotAt = Time + (p.BurstRemaining > 0 ? weapon.NpcInterval : weapon.NpcRest + (p.Id % 5) * .04);
            ShotsFired++;
            FireRay(p.Position, delta * (1 / delta.Length), weapon, false, p.Height * .72, p);
        }
    }

    private void PlayerCombat()
    {
        if (!PlayerFireHeld || OnScooter || PassengerMode || DroneActive || PlayerUnavailable || PlayerHealth <= 0 || EnteringCarId >= 0 ||
            Time < _playerShotAt) return;
        if (OnFoot ? (Player.KnockedFor > 0 || Player.Detached) : (Hero.Recovering || !Hero.Grounded)) return;
        if(!OnFoot&&Hero.Kind==11){_playerShotAt=Time+1.8;LaunchRocket(new(Math.Sin(Hero.Heading+Hero.SpecialYaw),Math.Cos(Hero.Heading+Hero.SpecialYaw)),CityWeapons.Find(CityWeaponId.Bazooka)!);ReportCrime(.3,80,sight:false);return;}
        if (!OnFoot && Hero.Kind == 14)
        {
            _playerShotAt = Time + CarrierGun.Interval;
            FireCarrierGun(Hero, true);
            PlayerShotsFired++;
            ReportCrime(.055, 60, sight: false);
            return;
        }
        var weapon = Inventory.Active;
        if (weapon is null) { if(OnFoot) Punch(); return; }
        if (!Inventory.Consume(weapon.Id)) return;
        _playerShotAt = Time + weapon.Interval;
        PlayerShotsFired++;
        ReportCrime(.055, 60, sight: false); // a shot is heard, not seen
        double heading = OnFoot ? PlayerViewHeading : Hero.Heading;
        var direction = new CityPoint(Math.Sin(heading), Math.Cos(heading));
        if (OnFoot)
        {
            Player.Aiming = true;
            Player.PlayerWeapon = weapon.Id;
            Player.Heading = heading;
        }

        if(weapon.Id==CityWeaponId.Bazooka) LaunchRocket(direction,weapon);
        else if(weapon.Id==CityWeaponId.Flamethrower)Flame(direction);
        else FireRay(PlayerPosition, direction, weapon, true, OnFoot ? 1.32 : .95, OnFoot ? Player : null);
        Player.PlayerWeapon=Inventory.Active?.Id??CityWeaponId.None;
    }

    private static readonly CityWeapon CarrierGun = new(CityWeaponId.Automatic, 0,
        "MOUNTED MG", "ПУЛЕМЁТ", 0, .12, 90, .19, .65, 4, .15, 1.2, .065, "automatic");

    private (CityPoint Point, double Height) FireCarrierGun(CityCar car, bool player, bool preview=false)
    {
        double yaw = car.Heading + car.SpecialYaw;
        var forward = new CityPoint(Math.Sin(car.Heading), Math.Cos(car.Heading));
        var pivot = car.Position + forward * (.55 / 3.6 * car.HalfLength);
        var dir = new CityPoint(Math.Sin(yaw), Math.Cos(yaw));
        double height = car.Height * (2.31/2.64) + car.Lift;
        return TraceAttack(pivot, dir, CarrierGun, player, height, null, preview,
            worldY: (_useScenery ? car.SurfaceHeight : 0) + height,
            slope: Math.Tan(car.GunElevation), ignoreCar: car.Id);
    }

    private void AimCarrierAtPlayer(CityCar car, double dt)
    {
        if (WantedLevel < 5 || PlayerUnavailable || PlayerControls(car) || car.Driverless ||
            car.CrewDeployed || car.Burning || car.Recovering || !car.Grounded) return;
        var forward = new CityPoint(Math.Sin(car.Heading), Math.Cos(car.Heading));
        var pivot = car.Position + forward * (.55 / 3.6 * car.HalfLength);
        var delta = PlayerPosition - pivot;
        double distance = delta.Length;
        if (distance < 1 || distance > CarrierGun.Range) return;
        double targetYaw = Math.Atan2(delta.X,delta.Z) - car.Heading;
        double yawError = Math.IEEERemainder(targetYaw - car.SpecialYaw, Math.Tau);
        car.SpecialYaw = Math.IEEERemainder(car.SpecialYaw + Math.Clamp(yawError,-1.8*dt,1.8*dt),Math.Tau);
        double gunY = (_useScenery ? car.SurfaceHeight : 0) + car.Lift + car.Height*(2.31/2.64);
        double targetY = (_useScenery ? PlayerGround : 0) + (OnFoot ? .95 : Hero.Height*.55);
        double elevation = Math.Atan2(targetY-gunY,distance);
        car.GunElevation += Math.Clamp(Math.Clamp(elevation,-.28,.65)-car.GunElevation,-dt,dt);
        if (Math.Abs(yawError) > .04 || Math.Abs(elevation-car.GunElevation) > .03 || Time < car.NextTurretShot) return;
        if (!ClearLevelSight(car.Position,car.RoadLevel,PlayerPosition,OnFoot ? Player.RoadLevel : Hero.RoadLevel))
        {
            car.NextTurretShot = Time + .2;
            return;
        }
        ShotsFired++;
        FireCarrierGun(car, false);
        car.TurretBurst = (car.TurretBurst+1) % CarrierGun.NpcBurst;
        car.NextTurretShot = Time + (car.TurretBurst == 0 ? CarrierGun.NpcRest : CarrierGun.NpcInterval);
    }

    // Analytic horizontal ray tests; one projectile hits only the first object.
    // This is arcade hitscan, not a ballistics or penetration model.
    public static double RayBox(CityPoint origin, CityPoint direction, CityPoint center, double heading,
        double halfWidth, double halfLength, double limit)
    {
        var f = new CityPoint(Math.Sin(heading), Math.Cos(heading));
        var r = Right(f);
        var o = origin - center;
        double near = 0, far = limit;

        bool Slab(double a, double d, double extent)
        {
            if (Math.Abs(d) < 1e-10) return Math.Abs(a) <= extent;
            double lo = (-extent - a) / d, hi = (extent - a) / d;
            if (lo > hi) (lo, hi) = (hi, lo);
            near = Math.Max(near, lo);
            far = Math.Min(far, hi);
            return near <= far;
        }

        return Slab(CityPoint.Dot(o, r), CityPoint.Dot(direction, r), halfWidth) &&
               Slab(CityPoint.Dot(o, f), CityPoint.Dot(direction, f), halfLength) && far >= 0
            ? near
            : double.PositiveInfinity;
    }

    private static double RayCircle(CityPoint origin, CityPoint dir, CityPoint center, double radius)
    {
        var d = center - origin;
        double along = CityPoint.Dot(d, dir), side = d.Length * d.Length - along * along;
        if (side > radius * radius || along + radius < 0) return double.PositiveInfinity;
        return Math.Max(0, along - Math.Sqrt(Math.Max(0, radius * radius - side)));
    }

    private (CityPoint Point,double Height) FireRay(CityPoint from, CityPoint direction, CityWeapon weapon, bool player, double height,
        CityPedestrian? shooter,bool preview=false)=>TraceAttack(from,direction,weapon,player,height,shooter,preview);

    private (CityPoint Point,double Height) TraceAttack(CityPoint from, CityPoint direction, CityWeapon weapon, bool player, double height,
        CityPedestrian? shooter,bool preview=false,double? worldY=null,double? slope=null,int ignoreCar=-1,bool melee=false)
    {
        double originY = height + (_useScenery ? player?PlayerGround:shooter is not null?CityRoadLevels.Ground(shooter):CityTerrain.Ground(from) : 0);
        double verticalSlope = player ? Math.Tan(OnFoot?PlayerViewPitch:Hero.TerrainPitch+Hero.Pitch) : 0;
        if (!player && shooter is not null)
            verticalSlope = ((_useScenery ? PlayerGround : 0) + .95 - originY) /
                            Math.Max(1, (PlayerPosition - from).Length);
        originY=worldY??originY;verticalSlope=slope??verticalSlope;
        double nearest = weapon.Range;
        if (_useScenery)
            for (double d = .25; d < nearest; d += .25)
            {
                var p = from + direction * d;
                if (CityRoadLevels.Solid(p.X,originY+verticalSlope*d,p.Z,.05) || CityWater.Wet(p)&&CityWater.Level>originY+verticalSlope*d)
                {
                    nearest = d;
                    break;
                }
            }

        bool AtHeight(double d, CityPoint p, double bottom, double top)
        {
            double y = originY + verticalSlope * d - (_useScenery ? CityTerrain.Ground(p) : 0);
            return y >= bottom - .1 && y <= top + .1;
        }

        CityCar? hitCar = null;
        CityPedestrian? hitPerson = null;
        CityAnimal? hitAnimal = null;
        StreetProp? hitProp = null;

        // The reticle calls this every frame on foot. Buildings and furniture are
        // bucketed per 36 m block, so only the blocks under the ray's bounding
        // box are tested - a 60 m ray crosses at most nine of them - instead of
        // every footprint on the island (thousands of boxes per frame).
        {
            var layout = CityLayout.Current;
            double origin = Coordinate(0);
            CityPoint far = from + direction * nearest;
            int i0 = (int)Math.Floor((Math.Min(from.X, far.X) - origin) / Spacing) - 1,
                i1 = (int)Math.Floor((Math.Max(from.X, far.X) - origin) / Spacing) + 1,
                j0 = (int)Math.Floor((Math.Min(from.Z, far.Z) - origin) / Spacing) - 1,
                j1 = (int)Math.Floor((Math.Max(from.Z, far.Z) - origin) / Spacing) + 1;
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                foreach (int id in layout.BuildingIds(i, j))
                {
                    CityModel.Obstacle block = layout.Blocks[id];
                    double floor=_useScenery?CityTerrain.Ground(block.X,block.Z):0;
                    nearest=RayPrism(from,direction,originY,verticalSlope,new(block.X,block.Z),0,block.HalfX,block.HalfZ,floor,floor+block.Height,nearest);
                }

                foreach (CityModel.Obstacle b in layout.FurnitureInBlock(i, j))
                {
                    double floor=_useScenery?CityTerrain.Ground(b.X,b.Z):0;
                    nearest=RayPrism(from,direction,originY,verticalSlope,new(b.X,b.Z),0,b.HalfX,b.HalfZ,floor,floor+b.Height,nearest);
                }
            }
        }

        // Imported depot vehicles and yard equipment are scenery, not traffic
        // actors. Their solid hull must also stop shots and the aiming ray.
        if(_useScenery)foreach(var site in CityImportedSites.All())if(site.Extra&&Math.Abs(site.Position.X-from.X)<nearest+site.HalfX&&Math.Abs(site.Position.Z-from.Z)<nearest+site.HalfZ){
            nearest=RayPrism(from,direction,originY,verticalSlope,site.Position,site.Angle,
                site.Width*.5,site.Length*.5,site.Ground,site.Ground+site.Height,nearest);
        }

        foreach (CityCar nearbyCar in NearbyCars(from, weapon.Range + 8))
        {
            if (((ignoreCar >= 0 && nearbyCar.Id == ignoreCar) || (player && worldY is null && PlayerControls(nearbyCar))) || nearbyCar.Falling) continue;
            double floor=(_useScenery?nearbyCar.SurfaceHeight:0)+nearbyCar.Lift;
            double d = RayPrism(from,direction,originY,verticalSlope,nearbyCar.Position,nearbyCar.Heading,
                nearbyCar.HalfWidth,nearbyCar.HalfLength,floor,floor+nearbyCar.Height,nearest);

            if (d < nearest)
            {
                nearest = d;
                hitCar = nearbyCar;
            }
        }

        // Every ray stops at the first person in its line, whoever fired it: an
        // NPC burst aimed at the player hits the bystander in between instead of
        // passing through them. The player disc itself is tested separately below.
        foreach (CityPedestrian nearbyPedestrian in NearbyPeople(from, weapon.Range + 1))
        {
            if (nearbyPedestrian.IsPlayer || nearbyPedestrian.Dead || nearbyPedestrian == shooter) continue;
            double d = RayCircle(from, direction, nearbyPedestrian.Position, .33);
            double lift = nearbyPedestrian.FlightHeight + (_useScenery ? CityRoadLevels.Ground(nearbyPedestrian) - CityTerrain.Ground(nearbyPedestrian.Position) : 0);

            if (d < nearest && AtHeight(d, nearbyPedestrian.Position, lift, lift + nearbyPedestrian.Height))
            {
                nearest = d;
                hitPerson = nearbyPedestrian;
                hitCar = null;
            }
        }

        if (!player && OnFoot && !Player.Dead)
        {
            double d = RayCircle(from, direction, Player.Position, .33);

            if (d < nearest && AtHeight(d, Player.Position, Player.FlightHeight+(_useScenery?CityRoadLevels.Ground(Player)-CityTerrain.Ground(Player.Position):0), Player.FlightHeight + Player.Height+(_useScenery?CityRoadLevels.Ground(Player)-CityTerrain.Ground(Player.Position):0)))
            {
                nearest = d;
                hitPerson = Player;
                hitCar = null;
            }
        }

        if (player)
        {
            foreach (CityAnimal animal in Animals)
            {
                if (animal.Dead) continue;
                double d = RayCircle(from, direction, animal.Position, animal.Radius);

                if (d < nearest && AtHeight(d, animal.Position, animal.Height, animal.Height + (animal.Kind == AnimalKind.Dog ? .9 : .5)))
                {
                    nearest = d;
                    hitAnimal = animal;
                    hitPerson = null;
                    hitCar = null;
                }
            }
        }

        CityPoint rayEnd = from + direction * nearest;
        foreach (var prop in PropsInBox(Math.Min(from.X, rayEnd.X) - 2, Math.Min(from.Z, rayEnd.Z) - 2, Math.Max(from.X, rayEnd.X) + 2, Math.Max(from.Z, rayEnd.Z) + 2))
        {
            if (prop.Kind is StreetKind.Bottle or StreetKind.Can or StreetKind.Bag) continue;
            double d = RayCircle(from, direction, prop.Position, prop.Radius);

            if (d < nearest && AtHeight(d, prop.Position, prop.Height, prop.Height + 1.5))
            {
                nearest = d;
                hitProp = prop;
                hitAnimal = null;
                hitPerson = null;
                hitCar = null;
            }
        }

        CityPoint end = from + direction * nearest;
        if(preview)return(end,originY+verticalSlope*nearest);

        // Match the hand pose and barrel in CityPickupVisual; collision rays still
        // begin at the actor centre so a muzzle cannot shoot through a wall.
        double muzzle = ignoreCar >= 0 && !melee ? 2.18 : player && !OnFoot ? Hero.HalfLength + .08 :
            shooter is null ? .45 :
            .48 * (.94 + (shooter.Id % 5) * .035) + (weapon.Id == CityWeaponId.Automatic ? .52 : .185);
        var visibleFrom = from + direction * Math.Min(muzzle, nearest);
        double visibleHeight = height;
        if (shooter is not null && nearest > muzzle)
        {
            visibleFrom += new CityPoint(direction.Z, -direction.X) * (.16 * shooter.ShoulderWidth / .51);
            visibleHeight = .15 + 1.25 * shooter.Height / 1.72 + .04;
        }

        if(!melee)Shots.Add(new()
        {
            From = visibleFrom, To = end,
            Height = (worldY ?? (visibleHeight + (_useScenery ? CityTerrain.Ground(from) : 0))) +
                     verticalSlope * Math.Min(muzzle, nearest),
            EndHeight = originY + verticalSlope * nearest
        });
        if (Shots.Count > 64) Shots.RemoveAt(0);
        if(!melee)Sound(weapon.Sound, from, player ? .65 : .52);
        else if(hitCar is not null||hitProp is not null)Sound("impact",end,.35);
        if (hitCar is not null && !hitCar.Destroyed)
            DamageCar(hitCar, player ? weapon.CarDamage : weapon.NpcCarDamage, player);
        if (hitPerson?.IsPlayer == true)
        {
            DamagePlayer(weapon.PersonDamage * .45);
            hitPerson = null;
        }

        if (hitPerson is not null)
        {
            if(melee){
                hitPerson.Body.Wake();hitPerson.Body.Vx+=direction.X*1.5;hitPerson.Body.Vy+=direction.Z*1.5;
                ReportCrime(.12,24,sight:true);
            }
            hitPerson.GunDamage += weapon.PersonDamage;
            hitPerson.VoiceRevision++;
            // Aggression is always aimed at the player; a stray NPC bullet earns
            // the player neither a grudge nor a crime.
            if (player) hitPerson.AggressionUntil = Time + 8;
            hitPerson.Fear = 1;
            hitPerson.FearFrom = from;
            if (hitPerson.GunDamage >= 1)
            {
                hitPerson.KnockedFor = 3;
                hitPerson.VerticalSpeed = 1;
                hitPerson.FlightHeight = .02;
                hitPerson.Body.Vx = direction.X * 2;
                hitPerson.Body.Vy = direction.Z * 2;
                Remove(hitPerson.Body);
                hitPerson.Detached = true;
                hitPerson.Aiming = false;
                FatalPerson(hitPerson, player);
            }

            Sound("impact", hitPerson.Position, .4);
        }

        if (hitAnimal is not null)
        {
            hitAnimal.Dead = true;
            hitAnimal.DeathTime = Time;
            hitAnimal.Flying = false;
            hitAnimal.Detached = true;
            hitAnimal.KnockedFor = 3;
            if (hitAnimal.Body is { } b)
            {
                Remove(b);
                b.Vx = direction.X * 2;
                b.Vy = direction.Z * 2;
            }

            ReportCrime(.12);
        }

        if (hitProp?.Body is { } body)
        {
            body.Wake();
            body.Vx += direction.X * 10 / Math.Max(1, hitProp.Mass);
            body.Vy += direction.Z * 10 / Math.Max(1, hitProp.Mass);
        }

        if(nearest<weapon.Range&&CityWater.Wet(end)&&Math.Abs(originY+verticalSlope*nearest-CityWater.Level)<.4)Splash(end,.22);
        else if (nearest < weapon.Range)
            Emit(end.X, originY + verticalSlope * nearest - (_useScenery ? CityTerrain.Ground(end) : 0), end.Z, 0, 3,
                1.5);
        foreach (var witness in NearbyPeople(from, 22))
            if (!witness.IsPlayer && witness != shooter && !witness.Dead)
            {
                witness.Fear = Math.Max(witness.Fear, witness.Armed ? .2 : .9);
                witness.FearFrom = from;
                if (player && witness.Armed) witness.AggressionUntil = Time + 8;
            }

        foreach (var animal in Animals)
            if (animal.Physical && (animal.Position - from).Length < 24)
            {
                animal.Fear = 1;
                animal.FearFrom = from;
            }
        return(end,originY+verticalSlope*nearest);
    }

    public (CityPoint Point,double Height) AimHit()=>!OnFoot && Hero.Kind==14 ? FireCarrierGun(Hero,true,true) : FireRay(PlayerPosition,new(Math.Sin(PlayerViewHeading),Math.Cos(PlayerViewHeading)),Inventory.Active??Fist,true,1.32,Player,true);

    private void AnimalReactions(CityAnimal animal, double dt)
    {
        if (animal.Dead) return;

        foreach (CityPedestrian pedestrian in NearbyPeople(animal.Position, 4))
        {
            if (pedestrian is { Dead: false, KnockedFor: <= 0 })
            {
                Scare(pedestrian.Position, CityThreat.Person);
            }
        }

        double predatorRadius=Math.Max(CityReactions.Animal(animal.Kind,CityThreat.Cat).Radius,
            CityReactions.Animal(animal.Kind,CityThreat.Dog).Radius);
        foreach (var other in NearbyAnimalThreats(animal.Position,predatorRadius))
        {
            if (other != animal && other is { Physical: true, Dead: false, Height: < 1 })
            {
                switch (other.Kind)
                {
                    case AnimalKind.Cat:
                        Scare(other.Position, CityThreat.Cat);
                        break;

                    case AnimalKind.Dog:
                        Scare(other.Position, CityThreat.Dog);
                        break;
                }
            }
        }

        if (animal is { Fear: > .18, Kind: AnimalKind.Pigeon or AnimalKind.Bird })
        {
            animal.Heading = Math.Atan2(animal.Position.X - animal.FearFrom.X, animal.Position.Z - animal.FearFrom.Z);

            // LaunchFlock already launches and alarms every living flock mate
            // (only pigeons have flocks), so no separate alarm pass is needed.
            if (animal.Kind == AnimalKind.Bird) 
                StartBirdFlight(animal);
            else 
                LaunchFlock(animal);
        }

        return;

        void Scare(CityPoint at, CityThreat kind)
        {
            FearResponse rule = CityReactions.Animal(animal.Kind, kind);
            var delta=animal.Position-at;
            if (rule.Radius <= 0 || CityPoint.Dot(delta,delta) >= rule.Radius*rule.Radius) return;
            animal.Fear = Math.Min(1, animal.Fear + dt * rule.Gain);
            animal.FearFrom = at;
        }
    }
}

