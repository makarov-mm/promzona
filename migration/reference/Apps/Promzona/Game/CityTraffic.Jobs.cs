namespace Promzona.Game;

public enum JobKind { None, Taxi, Courier, Patient, VehicleDelivery, Elimination, Leaflets, PeaceBroadcast }

/// <summary>What the E key would do right now: pick up a passenger, take or
/// hand over a parcel. The HUD shows it as the prompt line.</summary>
public readonly record struct JobOffer(JobKind Kind, int Fare, CityPoint Target, bool Deliver);

/// <summary>The "day of a gypsy cab" loop (29.10.2026): taxi fares hailed at
/// bus stops, parcels between kiosks and survivors of car hits taken to a
/// clinic. One job at a time; E starts and finishes it, the car doing the
/// driving is whatever the player sits in. The job state is a handful of
/// primitive fields, so the format 3 save stores it with the clock and the
/// passenger rides as an id and a persona, rebuilt beside the car on arrival.
/// Fares are arcade numbers, not a tariff model.</summary>
public sealed partial class CityTraffic
{
    private const int HailerCap = 3;
    private const double BoardingPatience = 25, PassengerPatience = 25;
    private JobKind _jobKind;
    private int _jobStage; // 0 passenger walking to the door, 1 under way
    private int _jobPassengerId = -1, _jobPersona, _jobCarId = -1, _jobFare, _jobScares;
    private CityPoint _jobPickup, _jobTarget, _jobCarAt;
    private double _jobDeadline, _jobStarted, _jobDamageAt, _jobOffFoot, _jobStopped, _jobClock, _jobInteractAt, _jobCrimeSeen = -100;
    private bool _jobLate;
    public JobKind JobKind => _jobKind;
    public int JobStage => _jobStage;
    public CityPoint JobTarget => _jobTarget;
    public int JobFare => _jobFare;
    public bool JobLate => _jobLate;
    public double JobTimeLeft => _jobKind == JobKind.None ? 0 : _jobDeadline - Time;
    public double JobDistance => _jobKind == JobKind.None ? 0 : (_jobTarget - PlayerPosition).Length;
    public int JobsCompleted { get; private set; }
    public int JobsFailed { get; private set; }
    public int JobEarnings { get; private set; }
    public int LastJobPay { get; private set; }
    public string JobTargetName(bool ru) => CityNames.StreetAt(_jobTarget, ru);
    public CityPedestrian? JobPassenger => _jobKind is JobKind.Taxi or JobKind.Patient && _jobStage == 0 ? Pedestrians.Find(p => p.Id == _jobPassengerId) : null;
    public IEnumerable<CityPedestrian> Hailers => Pedestrians.Where(p => p.Hailing && !p.Dead);

    private void ResetJobs()
    {
        ClearJob();ChainStage=ChainsCompleted=0;
        _jobClock = _jobInteractAt = 0;
        JobsCompleted = JobsFailed = JobEarnings = LastJobPay = 0;
    }

    private void ClearJob()
    {
        _jobKind = JobKind.None; _jobStage = 0; _jobPassengerId = _jobCarId = -1; _jobPersona = _jobFare = _jobScares = 0;
        _jobPickup = _jobTarget = _jobCarAt = default; _jobDeadline = _jobStarted = _jobDamageAt = _jobOffFoot = _jobStopped = 0;
        _contractDestination=default; _chainContract=false; _jobCrimeSeen = -100; _jobLate = false; ResetPeaceJob();
    }

    private static int RoundFare(double v) => Math.Max(5, (int)Math.Round(v / 5) * 5);
    public static int Fare(JobKind kind, double distance) => kind switch
    {
        JobKind.Taxi => RoundFare(20 + distance * .11),
        JobKind.Courier => RoundFare(15 + distance * .08),
        _ => RoundFare(35 + distance * .10)
    };
    private static double Allowance(JobKind kind, double distance) => kind switch
    {
        JobKind.Taxi => 45 + distance / 7,
        JobKind.Courier => 60 + distance / 6,
        _ => 40 + distance / 8
    };
    private double ArriveRadius => _jobKind == JobKind.Patient ? 14 : 11;

    /// <summary>Destinations are chosen by the hailer's or kiosk's identity, not
    /// the random stream: the HUD asks every frame and must show the same place
    /// the E key will commit to.</summary>
    private CityPoint? TaxiDestination(CityPedestrian p)
    {
        var stops = BusStops; if (stops.Count < 2) return null;
        var far = new List<CityPoint>();
        foreach (var s in stops) { double d = (s - p.Position).Length; if (d >= 160 && d <= 520) far.Add(s); }
        if (far.Count == 0) foreach (var s in stops) if ((s - p.Position).Length > 40) far.Add(s);
        if (far.Count == 0) return null;
        far.Sort((a, b) => (a - p.Position).Length.CompareTo((b - p.Position).Length));
        return far[p.Id % far.Count];
    }

    private FoodSite? CourierDestination(FoodSite from)
    {
        var far = new List<FoodSite>();
        foreach (var s in FoodSites) { double d = (s.Interaction - from.Interaction).Length; if (s.Id != from.Id && d >= 120 && d <= 450) far.Add(s); }
        if (far.Count == 0) foreach (var s in FoodSites) if (s.Id != from.Id && (s.Interaction - from.Interaction).Length > 40) far.Add(s);
        if (far.Count == 0) return null;
        far.Sort((a, b) => (a.Interaction - from.Interaction).Length.CompareTo((b.Interaction - from.Interaction).Length));
        return far[(from.Id + JobsCompleted + JobsFailed) % far.Count];
    }

    private CityPoint? NearestClinic(CityPoint from)
    {
        CityPoint? best = null; double distance = double.MaxValue;
        foreach (var c in ClinicBlocks) { var at = CityAmenities.ClinicEntrance(c.I, c.J); double d = (at - from).Length; if (d < distance) { distance = d; best = at; } }
        return best;
    }

    private bool InReach(CityPoint at, bool parked) =>
        OnFoot ? (at - PlayerPosition).Length < 2.2 && Player.RoadLevel == 0 : parked && (at - Hero.Position).Length < 10;

    public JobOffer? NearbyJob()
    {
        if (PlayerUnavailable || Cars.Count == 0) return null;
        bool parked = !OnFoot && Math.Abs(Hero.Speed) < 1.5 && Hero.Grounded && !Hero.Recovering && Hero.RoadLevel == 0;
        if (_jobKind == JobKind.Courier) return InReach(_jobTarget, parked) ? new JobOffer(JobKind.Courier, _jobFare, _jobTarget, true) : null;
        if (_jobKind != JobKind.None) return null;
        if (parked)
        {
            CityPedestrian? best = null; double near = 8;
            foreach (var p in NearbyPeople(Hero.Position, 9))
            {
                if (p.IsPlayer || p.Dead || p.KnockedFor > 0 || p.Detached || p.RoadLevel != 0 || p.Boarding >= 0 || !(p.Hailing || p.Injured > 0)) continue;
                double d = (p.Position - Hero.Position).Length;
                if (d < near) { near = d; best = p; }
            }
            if (best is not null)
            {
                var kind = best.Injured > 0 && !best.Hailing ? JobKind.Patient : JobKind.Taxi;
                var to = kind == JobKind.Patient ? NearestClinic(best.Position) : TaxiDestination(best);
                if (to is { } target) return new JobOffer(kind, Fare(kind, (target - Hero.Position).Length), target, false);
            }
        }
        foreach (var s in FoodSites)
            if (InReach(s.Interaction, parked) && CourierDestination(s) is { } site)
                return new JobOffer(JobKind.Courier, Fare(JobKind.Courier, (site.Interaction - s.Interaction).Length), site.Interaction, false);
        return null;
    }

    /// <summary>The E key.</summary>
    public bool InteractJob()
    {
        if (Time < _jobInteractAt || PlayerUnavailable || Cars.Count == 0) return false;
        _jobInteractAt = Time + .35;
        var offer = NearbyJob();
        if (offer is null)
        {
            if (_jobKind is JobKind.Taxi or JobKind.Patient && _jobStage == 1 && !OnFoot && Math.Abs(Hero.Speed) < .8 && Hero.Id == _jobCarId)
            {
                AlightPassenger(Hero, false); JobsFailed++; ClearJob(); Message("job_dropped"); return true;
            }
            return false;
        }
        var o = offer.Value;
        if (o.Deliver)
        {
            int pay = Payout(); Money = Math.Min(999999999, Money + pay); LastJobPay = pay; JobEarnings += pay; JobsCompleted++;
            Sound("cash", PlayerPosition, .85, ui: true); ClearJob(); Message("job_paid"); return true;
        }
        if (o.Kind == JobKind.Courier)
        {
            _jobKind = JobKind.Courier; _jobStage = 1; _jobPickup = PlayerPosition; _jobTarget = o.Target; _jobFare = o.Fare;
            _jobStarted = Time; _jobDeadline = Time + Allowance(JobKind.Courier, (o.Target - PlayerPosition).Length); _jobLate = false;
            Message("job_parcel"); return true;
        }
        CityPedestrian? passenger = null; double near = 8;
        foreach (var p in NearbyPeople(Hero.Position, 9))
        {
            if (p.IsPlayer || p.Dead || p.KnockedFor > 0 || p.Detached || p.RoadLevel != 0 || p.Boarding >= 0 || !(p.Hailing || p.Injured > 0)) continue;
            double d = (p.Position - Hero.Position).Length;
            if (d < near) { near = d; passenger = p; }
        }
        if (passenger is null) return false;
        _jobKind = o.Kind; _jobStage = 0; _jobPassengerId = passenger.Id; _jobCarId = Hero.Id; _jobPickup = passenger.Position; _jobTarget = o.Target;
        _jobFare = o.Fare; _jobStarted = Time; _jobCarAt = Hero.Position; _jobLate = false;
        passenger.Boarding = Hero.Id; passenger.Hailing = false; passenger.Wait = 0;
        Hero.DoorSide = CityPoint.Dot(passenger.Position - Hero.Position, Right(new(Math.Sin(Hero.Heading), Math.Cos(Hero.Heading)))) > 0 ? 1 : -1;
        Message("job_wave"); return true;
    }

    private int Payout()
    {
        double pay = _jobFare * (_jobLate ? .5 : 1) * Math.Max(.3, 1 - .25 * _jobScares);
        return Math.Max(5, (int)Math.Round(pay / 5) * 5);
    }

    private void JobsStep(double dt)
    {
        if (!Enum.IsDefined(_jobKind) || _jobKind == JobKind.None && _jobStage != 0 || !double.IsFinite(_jobDeadline) || _jobFare < 0 || _jobFare > 100000) ClearJob();
        _jobClock -= dt;
        if (_jobClock <= 0) { _jobClock = 2; MaintainHailers(); }
        if (_jobKind == JobKind.None) return;
        if(PeaceJobActive){PeaceJobStep(dt);return;}
        if(ContractActive){ContractStep(dt);return;}
        if (_jobKind == JobKind.Courier)
        {
            if (!_jobLate && Time > _jobDeadline) { _jobLate = true; Message("job_late"); }
            return;
        }
        if (_jobStage == 0) BoardingStep(); else RideStep(dt);
    }

    private void MaintainHailers()
    {
        int hailers = 0;
        for (int n = Pedestrians.Count - 1; n >= 0; n--)
        {
            var p = Pedestrians[n];
            if (!p.Hailing) continue;
            if (p.Dead || p.KnockedFor > 0 || Time > p.HailUntil || p.Fear > .3 || p.Boarding >= 0) { StopHailing(p); continue; }
            hailers++;
        }
        // Nobody flags down a militia car, an ambulance or a bus.
        if (OnFoot || PlayerUnavailable || _jobKind != JobKind.None || hailers >= HailerCap || Pedestrians.Count >= PeopleCap || Hero.PoliceVehicle || Hero.Kind is 4 or 5) return;
        var stops = BusStops; if (stops.Count < 2) return;
        for (int trial = 0; trial < 6; trial++)
        {
            var stop = stops[Pick(stops.Count)]; double d = (stop - PlayerPosition).Length;
            if (d < 45 || d > 210) continue;
            bool taken = false;
            foreach (var p in NearbyPeople(stop, 12)) if (p.Hailing) { taken = true; break; }
            if (taken) continue;
            // At the kerb, between the shelter and the road, facing the road (+X).
            var at = stop + new CityPoint(1.3, .3 + Random() * .9);
            if (!ClearPlayerPoint(at)) continue;
            int i = Math.Clamp((int)Math.Round((at.X - Coordinate(0)) / Spacing), 0, Nodes - 1), j = Math.Clamp((int)Math.Round((at.Z - Coordinate(0)) / Spacing), 0, Nodes - 1);
            var hailer = MakePerson(i, j, 1, 1, at);
            hailer.Persona = Pick(6) switch { 0 => 2, 1 => 8, _ => 0 };
            hailer.Heading = Math.PI / 2; hailer.Hailing = true; hailer.HailUntil = Time + 70 + Random() * 50; hailer.RoleHome = at; hailer.VoiceAt = Time + 3;
            Register(hailer); Pedestrians.Add(hailer); Reanchor(hailer);
            return;
        }
    }

    private void StopHailing(CityPedestrian p)
    {
        p.Hailing = false; p.HailUntil = 0;
        if (p.Id != _jobPassengerId) p.Boarding = -1;
        if (!p.Dead) Reanchor(p);
    }

    /// <summary>Hailers stand at the kerb, injured survivors limp about where
    /// they fell, and a passenger walks to the player's door. Called from both
    /// the physical and the far walker loops before the role behaviours.</summary>
    private bool JobWalk(CityPedestrian p, double dt)
    {
        if (p.Injured > 0) { p.Injured = Math.Max(0, p.Injured - dt); if (p.Injured == 0 && !p.Dead) Reanchor(p); }
        if (p.Boarding >= 0) return BoardWalk(p, dt);
        if (p.Fear > .12 || p.Aiming || p.Dead || p.KnockedFor > 0 || p.Detached) return false;
        if (p.Hailing)
        {
            if (p.Physical) MovePerson(p, default, dt); else MoveFarSocial(p, default, dt);
            if (p.Speed < .2) p.Heading = Math.PI / 2;
            return true;
        }
        if (p.Injured <= 0) return false;
        double phase = (Time + p.Id * 3) % 16;
        if (phase < 11)
        {
            if (p.Physical) MovePerson(p, default, dt); else MoveFarSocial(p, default, dt);
            return true;
        }
        var target = p.RoleHome + new CityPoint(Math.Sin(p.Id) * Math.Sin(phase * .4), Math.Cos(p.Id) * Math.Sin(phase * .4)) * 2.5;
        if (!ClearWalk(target)) target = p.RoleHome;
        var delta = target - p.Position;
        var velocity = delta.Length > .3 ? delta * (Math.Min(.55, delta.Length) / Math.Max(.01, delta.Length)) : default;
        if (p.Physical) MovePerson(p, velocity, dt, target); else MoveFarSocial(p, velocity, dt);
        return true;
    }

    private static CityPoint NearestDoor(CityCar c, CityPoint from)
    {
        CityPoint a = DoorPoint(c, 1), b = DoorPoint(c, -1);
        return (a - from).Length <= (b - from).Length ? a : b;
    }

    private bool BoardWalk(CityPedestrian p, double dt)
    {
        var car = Cars.Find(c => c.Id == p.Boarding);
        if (car is null || p.Id != _jobPassengerId || _jobStage != 0 || _jobKind is not (JobKind.Taxi or JobKind.Patient)) { p.Boarding = -1; return false; }
        var door = NearestDoor(car, p.Position);
        var delta = door - p.Position; double distance = delta.Length;
        if (p.Physical && (distance < .75 || CarPersonOverlap(car.Position, car.Heading, p.Position, .9, car.HalfLength, car.HalfWidth)))
        {
            BoardPassenger(p, car);
            return true;
        }
        double speed = p.Injured > 0 ? .9 : 1.6;
        var v = delta * (Math.Min(speed, distance * 2) / Math.Max(.01, distance));
        // The plain blend, not MovePerson: its stopped-car sidestep would walk the
        // passenger along the body of the very car it is trying to reach.
        MoveFarSocial(p, v, dt);
        return true;
    }

    private void BoardingStep()
    {
        var p = Pedestrians.Find(x => x.Id == _jobPassengerId); var car = Cars.Find(c => c.Id == _jobCarId);
        if (p is null || car is null || p.Dead || p.KnockedFor > 0 || OnFoot || !car.Hero || Time - _jobStarted > BoardingPatience || (p.Position - car.Position).Length > 16)
        {
            if (p is not null) { p.Boarding = -1; if (_jobKind == JobKind.Taxi && !p.Dead) { p.Hailing = true; p.HailUntil = Time + 40; } }
            ClearJob(); Message("job_cancel");
        }
    }

    private void BoardPassenger(CityPedestrian p, CityCar car)
    {
        _jobPersona = p.Persona; _jobStage = 1; _jobStarted = Time; _jobDamageAt = car.Damage; _jobOffFoot = _jobStopped = 0; _jobScares = 0; _jobLate = false; _jobCrimeSeen = _lastCrime;
        double distance = (_jobTarget - car.Position).Length;
        _jobFare = Fare(_jobKind, distance); _jobDeadline = Time + Allowance(_jobKind, distance); _jobCarAt = car.Position;
        car.DoorSide = CityPoint.Dot(p.Position - car.Position, Right(new(Math.Sin(car.Heading), Math.Cos(car.Heading)))) > 0 ? 1 : -1; car.DoorHold = 1.2;
        AbandonScooter(p);p.Hailing = false; p.Boarding = -1; ReleasePerson(p); Remove(p.Body); Pedestrians.Remove(p);
        Message(_jobKind == JobKind.Patient ? "job_patient" : "job_taxi");
    }

    private void RideStep(double dt)
    {
        var car = Cars.Find(c => c.Id == _jobCarId);
        if (car is null || car.Destroyed || car.Falling || car.Burning && car.FireAge > 2 || CityWater.Depth(car.Position) > .85)
        {
            AlightPassenger(car, false); JobsFailed++; ClearJob(); Message("job_lost"); return;
        }
        _jobCarAt = car.Position;
        if (OnFoot || !car.Hero)
        {
            _jobOffFoot += dt;
            if (_jobOffFoot > PassengerPatience || !OnFoot && !car.Hero && _jobOffFoot > 3)
            {
                AlightPassenger(car, false); JobsFailed++; ClearJob(); Message("job_left"); return;
            }
        }
        else _jobOffFoot = 0;
        if (car.Damage - _jobDamageAt > .12 * (1 + _jobScares)) { _jobScares++; Message("job_scared"); }
        if (_lastCrime > _jobCrimeSeen + .01) { _jobCrimeSeen = _lastCrime; _jobScares++; Message("job_scared"); }
        if (!_jobLate && Time > _jobDeadline) { _jobLate = true; Message("job_late"); }
        bool there = !OnFoot && car.Hero && car.RoadLevel == 0 && car.Grounded && Math.Abs(car.Speed) < .8 && (car.Position - _jobTarget).Length < ArriveRadius;
        _jobStopped = there ? _jobStopped + dt : 0;
        if (_jobStopped > .8)
        {
            int pay = Payout(); Money = Math.Min(999999999, Money + pay); LastJobPay = pay; JobEarnings += pay; JobsCompleted++;
            AlightPassenger(car, true); Sound("cash", PlayerPosition, .85, ui: true); ClearJob(); Message("job_paid");
        }
    }

    /// <summary>The passenger steps out beside the car (or where the car was
    /// last seen) with the identity it boarded with; a patient delivered to the
    /// clinic walks in healed, a dropped one keeps limping.</summary>
    private void AlightPassenger(CityCar? car, bool paid)
    {
        if (_jobPassengerId < 0) return;
        CityPoint centre = car?.Position ?? _jobCarAt; double heading = car?.Heading ?? 0;
        var f = new CityPoint(Math.Sin(heading), Math.Cos(heading)); var right = Right(f);
        double halfWidth = car?.HalfWidth ?? 1, halfLength = car?.HalfLength ?? 2;
        CityPoint? chosen = null;
        for (int attempt = 0; attempt < 32 && chosen is null; attempt++)
        {
            int side = attempt % 2 == 0 ? 1 : -1;
            var at = centre + right * (side * (halfWidth + 1.0 + attempt / 8 * .55)) + f * (.6 + (attempt / 2 % 4 - 1.5) * .65);
            if (ClearPlayerPoint(at, car)) chosen = at;
        }
        if (chosen is not { } place) { ExitedPedestrians++; return; }
        int i = Math.Clamp((int)Math.Round((place.X - Coordinate(0)) / Spacing), 0, Nodes - 1), j = Math.Clamp((int)Math.Round((place.Z - Coordinate(0)) / Spacing), 0, Nodes - 1);
        var p = MakePerson(i, j, 1, 1, place);
        p.Id = _jobPassengerId; p.Persona = _jobPersona; p.VoiceAt = Time + 2;
        // Door clearance was checked on this car's layer. Keep that layer when
        // registering the walker, including an abandoned fare inside a tunnel.
        p.RoadLevel = car?.RoadLevel ?? 0;
        p.Injured = _jobKind == JobKind.Patient && !paid ? 30 : 0; p.RoleHome = place;
        if (!paid) { p.Fear = .35; p.FearFrom = centre; }
        if (car is not null) { car.DoorSide = CityPoint.Dot(place - centre, right) > 0 ? 1 : -1; car.DoorHold = 1.2; }
        Register(p); Pedestrians.Add(p); Reanchor(p);
    }

    /// <summary>Death ends the job: a passenger gets out where the car stands, a parcel is lost.</summary>
    private void JobsOnRespawn()
    {
        if (_jobKind == JobKind.None) return;
        if (_jobStage == 1 && _jobKind is JobKind.Taxi or JobKind.Patient) AlightPassenger(Cars.Find(c => c.Id == _jobCarId), false);
        else if (_jobStage == 0 && Pedestrians.Find(x => x.Id == _jobPassengerId) is { } p) p.Boarding = -1;
        JobsFailed++; ClearJob();
    }
}
