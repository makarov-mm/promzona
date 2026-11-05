namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private void FreePoliceReservation(CityCar c)
    {
        if (c.Reservation >= 0 && _reservations[c.Reservation] == c.Id) _reservations[c.Reservation] = -1;
        c.Reservation = -1;
    }

    private void AssignPolice(CityCar c)
    {
        FreePoliceReservation(c);
        c.Cruise = 23;
        c.Stuck = c.ReverseFor = 0;
        Services.Add(new() { CarId = c.Id, Kind = CityService.Police, Destination = PlayerPosition });
        ChooseTurn(c);
    }

    private void PoliceDispatchStep(double dt)
    {
        if ((_policeClock -= dt) > 0) return;
        _policeClock = .8 / 1.5;
        if (WantedLevel == 0 || PlayerUnavailable) return;
        var remaining=PoliceRoster(WantedLevel).ToList();
        foreach(var mission in Services.Where(m=>m.Kind==CityService.Police).ToArray()){
            var unit=Cars.Find(c=>c.Id==mission.CarId); if(unit is null)continue;
            int slot=remaining.IndexOf(unit.Kind);
            if(slot>=0){remaining.RemoveAt(slot);continue;}
            ReleaseMedics(mission);Services.Remove(mission);
            if(unit.AirPatrol){unit.AirDeparting=true;continue;}
            unit.Cruise=5;RejoinRoad(unit);
        }
        int needed=NeededPoliceKind();
        var idle = Cars.Where(c => c.Kind==needed &&
                !c.Hero && !c.Driverless && c.PoliceVehicle && !c.Destroyed && !c.Falling && !c.Burning &&
                Mission(c) is null && (c.Position - PlayerPosition).Length < 160 &&
                ClearSight(c.Position, PlayerPosition))
            .OrderBy(c => (c.Position - PlayerPosition).Length).FirstOrDefault();
        int count = Services.Count(m => m.Kind == CityService.Police);
        if (idle is not null)
        {
            if (count >= PoliceBudget)
            {
                var distant = Services.Where(m => m.Kind == CityService.Police)
                    .Select(m => (Mission: m, Car: Cars.Find(c => c.Id == m.CarId)))
                    .Where(v => v.Car is not null && !v.Car.AirPatrol).OrderByDescending(v => (v.Car!.Position - PlayerPosition).Length)
                    .FirstOrDefault();
                if (distant.Car is not null && (distant.Car.Position - PlayerPosition).Length >
                    (idle.Position - PlayerPosition).Length + 80)
                {
                    Services.Remove(distant.Mission);
                    distant.Car.Cruise = 5;
                    RejoinRoad(distant.Car);
                    count--;
                }
            }

            if (count < PoliceBudget)
            {
                AssignPolice(idle);
                return;
            }
        }

        if (count < PoliceBudget) Dispatch(CityService.Police, null);
    }

    private CityPoint PoliceRoadTarget(CityCar c, double look)
    {
        FreePoliceReservation(c);
        c.Along = c.Path.ClosestDistance(c.Position);
        if (c.Path.Length - c.Along < .65 && (c.Position - c.Path.Sample(c.Path.Length).Point).Length < 2)
        {
            if (c.Phase == 0)
            {
                ChooseTurn(c);
                Cross(c);
            }
            else if (c.Phase == 1) Continue(c);
        }

        return c.Path.Sample(c.Along + look).Point;
    }

    private bool PolicePursuit(CityCar c, double dt)
    {
        if (Mission(c) is not { Kind: CityService.Police } mission || c.Burning || c.Destroyed || c.Falling)
            return false;
        // The player changed road level mid-chase: a car in free pursuit (stage 1)
        // has a stale path, so it rejoins the street graph instead of steering
        // back to - or, as a rule-driven car, snapping onto - its old segment.
        if(c.RoadLevel!=(OnFoot?Player.RoadLevel:Hero.RoadLevel)){if(mission.Stage==1)RejoinRoad(c);mission.Stage=0;return false;}
        if(c.CrewDeployed||OnFoot&&(c.Position-PlayerPosition).Length<19&&ClearSight(c.Position,PlayerPosition)){c.Command=new(0,0,true);return true;}
        // Beside a stopped player at a low level the car holds and the crew gets out (ServicesStep).
        if(BoxedIn(c)){c.Command=new(0,0,true);return true;}
        FreePoliceReservation(c);
        var offset = PlayerPosition - c.Position;
        CityPoint target;
        if (offset.Length < 150 && ClearChase(c.Position, PlayerPosition))
        {
            mission.Stage = 1;
            target = PlayerPosition + PlayerVelocity * Math.Min(.65, offset.Length / 24);
            if (!ClearChase(c.Position, target)) target = PlayerPosition;
        }
        else
        {
            if (mission.Stage == 1)
            {
                mission.Stage = 0;
                RejoinRoad(c);
            }

            target = PoliceRoadTarget(c, 3 + Math.Abs(c.Speed) * .5);
        }

        var d = target - c.Position;
        var f = new CityPoint(Math.Sin(c.Heading), Math.Cos(c.Heading));
        var right = new CityPoint(f.Z, -f.X);
        double side = CityPoint.Dot(d, right), aheadTarget = CityPoint.Dot(d, f);
        double steer = Math.Clamp(Math.Atan2(2 * c.Wheelbase * side, Math.Max(2, CityPoint.Dot(d, d))) / .68, -1, 1);
        double speed = mission.Stage == 1 ? Math.Min(26, 10 + offset.Length * .50) : c.Phase == 1 ? 8 : 23;
        // Emergency pursuit treats traffic as a steering cost, never a red-light,
        // reservation or pedestrian right-of-way lock. Contacts still use physics.
        foreach (var other in NearbyCars(c.Position, 18))
        {
            if (other == c || PlayerControls(other)) continue;
            var gap = other.Position - c.Position;
            double ahead = CityPoint.Dot(gap, f), lateral = CityPoint.Dot(gap, right);
            if (ahead > 0 && ahead < 16 && Math.Abs(lateral) < c.HalfWidth + other.HalfWidth + .8)
            {
                steer += Math.CopySign(.65, lateral == 0 ? (c.Id % 2 == 0 ? 1 : -1) : -lateral);
                speed = Math.Min(speed, Math.Max(4, (ahead - c.HalfLength - other.HalfLength) * 1.8));
            }
        }

        foreach (var p in NearbyPeople(c.Position, 10))
        {
            if (p.IsPlayer) continue;
            var gap = p.Position - c.Position;
            double ahead = CityPoint.Dot(gap, f), lateral = CityPoint.Dot(gap, right);
            if (ahead > 0 && ahead < 9 && Math.Abs(lateral) < 2)
            {
                steer += Math.CopySign(.5, lateral == 0 ? 1 : -lateral);
                speed = Math.Min(speed, 5);
            }
        }

        foreach (var a in Animals)
            if (!a.Dead && !a.Flying)
            {
                var gap = a.Position - c.Position;
                if (gap.Length < 7 && CityPoint.Dot(gap, f) > 0 && Math.Abs(CityPoint.Dot(gap, right)) < 1.8)
                {
                    steer += Math.CopySign(.3, -CityPoint.Dot(gap, right));
                    speed = Math.Min(speed, 7);
                }
            }

        speed *= Math.Clamp(1 - Math.Abs(steer) * .45, .5, 1);
        if (Math.Abs(c.Speed) < .5) c.Stuck += dt;
        else c.Stuck = 0;
        if (c.ReverseFor <= 0 && (c.Stuck > 1.8 || aheadTarget < -3 && Math.Abs(c.Speed) < 1.5))
        {
            c.ReverseFor = 1.1;
            c.Stuck = 0;
        }

        if (c.ReverseFor > 0)
        {
            c.ReverseFor -= dt;
            c.Command = new(-.65, -Math.CopySign(.85, side == 0 ? 1 : side));
        }
        else c.Command = new(Math.Clamp((speed - c.Speed) * .6, 0, 1), Math.Clamp(steer, -1, 1), c.Speed > speed + 1.5);

        return true;
    }

    private bool PoliceRuleStep(CityCar c, double dt)
    {
        if (Mission(c) is not { Kind: CityService.Police } mission || c.Burning) return false;
        if(c.RoadLevel!=(OnFoot?Player.RoadLevel:Hero.RoadLevel)){if(mission.Stage==1)RejoinRoad(c);mission.Stage=0;return false;}
        if (mission.Stage == 1)
        {
            mission.Stage = 0;
            RejoinRoad(c);
        }

        FreePoliceReservation(c);
        double speed = c.Phase == 1 ? 8 : 23;
        var f = new CityPoint(Math.Sin(c.Heading), Math.Cos(c.Heading));
        double lateral = 0;
        foreach (var other in NearbyCars(c.Position, 20))
        {
            if (other == c || PlayerControls(other)) continue;
            var d = other.Position - c.Position;
            double ahead = CityPoint.Dot(d, f);
            if (ahead > 0 && ahead < 18 && Math.Abs(CityPoint.Dot(d, Right(f))) < c.HalfWidth + other.HalfWidth + .5)
            {
                lateral = -2.8;
                speed = Math.Min(speed, 12);
            }
        }

        c.OvertakeOffset += Math.Clamp(lateral - c.OvertakeOffset, -dt * 3, dt * 3);
        c.Along = Math.Min(c.Path.Length, c.Along + speed * dt);
        var pose = c.Path.Sample(c.Along);
        var offset = Right(new(Math.Sin(pose.Heading), Math.Cos(pose.Heading))) * c.OvertakeOffset;
        if (!ClearWalk(pose.Point + offset)) offset = default;
        c.Body.X = pose.Point.X + offset.X;
        c.Body.Y = pose.Point.Z + offset.Z;
        c.Body.Angle = -pose.Heading;
        c.Body.Vx = Math.Sin(pose.Heading) * speed;
        c.Body.Vy = Math.Cos(pose.Heading) * speed;
        c.Body.W = 0;
        if (c.Along >= c.Path.Length - .001)
        {
            c.Position = new(c.Body.X, c.Body.Y);
            if (c.Phase == 0)
            {
                ChooseTurn(c);
                Cross(c);
            }
            else if (c.Phase == 1) Continue(c);
        }

        return true;
    }
}
