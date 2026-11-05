namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public List<ServiceMission> Services { get; } = [];
    public double Wanted { get; private set; }
    public int WantedLevel => Wanted <= .001 ? 0 : (int)Math.Ceiling(Wanted);
    public const int MaxWantedLevel = 6, MaxPoliceUnits = 9;
    public int PoliceBudget => PlayerUnavailable ? 0 : (WantedLevel * 3 + 1) / 2;
    public int PatientsRevived { get; private set; }
    public int UnseenCrimes { get; private set; }
    public int Arrests { get; private set; }
    public int LastFine { get; private set; }
    public int PendingHourSkip { get; private set; }
    private double _lastCrime = -100, _dispatchClock, _policeClock, _lastPoliceSight = -100, _sightClock, _arrestClock;

    public static double WantedPoints(double level)
    {
        double[] cost=[1,1.6,2.5,3.8,5.5,7.5]; double points=0;
        for(int i=0;i<MaxWantedLevel;i++)points+=Math.Clamp(level-i,0,1)*cost[i];
        return points;
    }
    public static double WantedFromPoints(double points)
    {
        double[] cost=[1,1.6,2.5,3.8,5.5,7.5]; points=Math.Max(0,points);
        for(int i=0;i<MaxWantedLevel;i++){if(points<=cost[i])return i+points/cost[i];points-=cost[i];}
        return MaxWantedLevel;
    }
    public static int[] PoliceRoster(int level)=>Math.Clamp(level,0,MaxWantedLevel) switch{
        1=>[3,6],2=>[3,6,3],3=>[3,6,3,7,7],4=>[3,6,7,7,13,13],5=>[3,6,7,7,13,13,14,14],6=>[3,6,7,7,13,13,14,14,12],_=>[]};
    private int NeededPoliceKind()
    {
        var kinds=Services.Where(m=>m.Kind==CityService.Police).Select(m=>Cars.Find(c=>c.Id==m.CarId)?.Kind??-1).ToList();
        foreach(int kind in PoliceRoster(WantedLevel)){int n=kinds.IndexOf(kind);if(n<0)return kind;kinds.RemoveAt(n);}
        return 3;
    }
    private void ResetServices()
    {
        Services.Clear();
        ResetDrone();HelicopterLanding=false;
        Wanted = 0;
        PatientsRevived = UnseenCrimes = Arrests = LastFine = PendingHourSkip = 0;
        _lastCrime = _lastPoliceSight = -100;
        _dispatchClock = _policeClock = _sightClock = _arrestClock = 0;
    }

    /// <summary>Raises the wanted level unconditionally: the caller has decided the crime counts.</summary>
    public void AddCrime(double severity)
    {
        if (PlayerUnavailable || !double.IsFinite(severity) || severity <= 0) return;
        // Save/UI retain the established 0..6 scale. Each subsequent interval
        // requires more crime points; a single large event crosses every interval correctly.
        Wanted = WantedFromPoints(WantedPoints(Wanted) + severity);
        _lastCrime = Time;
    }

    /// <summary>A crime raises the wanted level only when somebody saw or heard
    /// it (29.10.2026): a living person within the radius with a clear line of
    /// sight (any within earshot for a loud crime), an officer on foot within
    /// 60 m, or a crewed militia car within 70 m that can see the player. A
    /// collision or shot in an empty street raises nothing; the victim of a hit
    /// is a witness while alive. Plain scans: crimes are rare events and the
    /// actor grid may be stale between steps.</summary>
    public bool Witnessed(double radius = 45, bool sight = true)
    {
        var at = PlayerPosition;
        if(Cars.Any(c=>c.AirPatrol&&!c.AirDeparting&&!c.Destroyed&&AirPatrolSight(c)))return true;
        foreach (var c in Cars)
            if (c.PoliceVehicle && !c.Hero && !c.Destroyed && !c.Driverless && (c.Position - at).Length < 70 && (!sight || ClearSight(c.Position, at)))
                return true;
        foreach (var p in Pedestrians)
        {
            if (p.IsPlayer || p.Dead) continue;
            double d = (p.Position - at).Length;
            if (p.Officer && d < 60 || d < radius && (!sight || ClearSight(p.Position, at))) return true;
        }
        return false;
    }

    public void ReportCrime(double severity, double radius = 45, bool sight = true)
    {
        if (Witnessed(radius, sight)) AddCrime(severity);
        else UnseenCrimes++;
    }

    /// <summary>Any militia unit that can see the player now: a crewed car within 90 m or an officer within 45 m.</summary>
    private bool PoliceSees()
    {
        var at = PlayerPosition;
        if(Cars.Any(c=>c.AirPatrol&&!c.AirDeparting&&!c.Destroyed&&AirPatrolSight(c)))return true;
        foreach (var c in Cars)
            if (c.PoliceVehicle && !c.Hero && !c.Destroyed && !c.Driverless && (c.Position - at).Length < 90 && ClearSight(c.Position, at))
                return true;
        foreach (var p in Pedestrians)
            if (p.Officer && !p.Dead && (p.Position - at).Length < 45 && ClearSight(p.Position, at))
                return true;
        return false;
    }

    /// <summary>The model applies the hours spent in the cell and clears the request.</summary>
    public int TakeHourSkip() { int hours = PendingHourSkip; PendingHourSkip = 0; return hours; }

    /// <summary>An officer beside a subdued player (standing still and not aiming
    /// on foot, or sitting in a stopped car) for a moment arrests him: fine,
    /// weapons gone, a few hours lost, released at the militia department.
    /// At wanted level four and above the squad shoots instead.</summary>
    private void ArrestStep(double dt)
    {
        if (WantedLevel == 0 || WantedLevel >= 4 || PlayerUnavailable) { _arrestClock = 0; return; }
        bool reached = false;
        if (Surrendering)
        {
            int level = OnFoot ? Player.RoadLevel : Hero.RoadLevel;
            CityPoint a = OnFoot ? Player.Position : DoorPoint(Hero, 1), b = OnFoot ? Player.Position : DoorPoint(Hero, -1);
            foreach (var p in Pedestrians)
                if (p.Officer && !p.Dead && p.KnockedFor <= 0 && p.RoadLevel == level && ((p.Position - a).Length < 1.5 || (p.Position - b).Length < 1.5))
                {
                    reached = true;
                    break;
                }
        }
        _arrestClock = reached ? _arrestClock + dt : Math.Max(0, _arrestClock - dt * 2);
        if (_arrestClock > (OnFoot ? 1.5 : 2.5)) Arrest();
    }

    /// <summary>A pursuit car that has reached a stopped player below level four
    /// stops beside it and lets the crew out for the arrest instead of ramming on.</summary>
    private bool BoxedIn(CityCar c) => !OnFoot && WantedLevel < 4 && Math.Abs(Hero.Speed) < 1 && SameHeight(c, Hero) &&
                                       (c.Position - Hero.Position).Length < Hero.HalfLength + c.HalfLength + 4.5;

    private bool Surrendering => OnFoot
        ? Player.Speed < 1.2 && !Player.Aiming && Player.KnockedFor <= 0 && !Player.Detached
        : Math.Abs(Hero.Speed) < 1 && Hero.Grounded && !Hero.Recovering;

    private void Arrest()
    {
        int fine = Math.Min(Money, 40 + 60 * WantedLevel);
        int hours = 3 + WantedLevel;
        if (!PlaceAt(CityAmenities.StationEntrance())) return;
        Money -= fine; LastFine = fine; Inventory.Clear(); PendingHourSkip = hours; Arrests++; Respawns++;
        _arrestClock = 0;
        Sound("respawn", PlayerPosition, .5);
        Message("arrest", 6);
    }

    private void ClearPlayerPursuit()
    {
        ResetDrone();HelicopterLanding=false;
        Wanted = 0;
        _lastCrime = -100;
        _policeClock = 0;
        foreach (var m in Services.Where(m => m.Kind == CityService.Police).ToArray())
        {
            var c = Cars.Find(c => c.Id == m.CarId);
            Services.Remove(m);
            if (c is not null && !c.Destroyed && !c.Falling)
            {
                FreePoliceReservation(c);
                if(c.AirPatrol){c.AirDeparting=true;continue;}
                c.Cruise = 5;
                c.Command = default;
                c.Stuck = c.ReverseFor = 0;
                RejoinRoad(c);
            }
        }

        // Delayed explosions from the previous life must not open a new pursuit.
        foreach (var c in Cars) c.PlayerDamageUntil = 0;
        foreach (var p in Pedestrians)
        {
            p.AggressionUntil = 0;
            p.Aiming = false;
        }
    }

    public bool PatientClaimed(int id) => Services.Any(m => m.Kind == CityService.Ambulance && m.PatientId == id);
    private ServiceMission? Mission(CityCar c) => Services.Find(m => m.CarId == c.Id);
    private bool IsResponding(CityCar c) => Mission(c) is { Stage: 0 };

    private bool ServiceParked(CityCar c) => Mission(c) is { Kind: CityService.Ambulance, Stage: > 0 };

    // Breadth-first routing uses the same connected graph as ordinary drivers.
    // The first turn is cached only by the driver's existing crossing/path state.
    public static int RoadDirection(int i, int j, CityPoint destination)
    {
        int ti = Math.Clamp((int)Math.Round((destination.X - Coordinate(0)) / Spacing), 0, Nodes - 1),
            tj = Math.Clamp((int)Math.Round((destination.Z - Coordinate(0)) / Spacing), 0, Nodes - 1);
        int start = i + Nodes * j, target = ti + Nodes * tj;
        if (start == target) return -1;
        var first = new int[Nodes * Nodes];
        Array.Fill(first, -2);
        first[start] = -1;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int at = queue.Dequeue(), x = at % Nodes, z = at / Nodes;
            for (int d = 0; d < 4; d++)
            {
                if (!HasRoad(x, z, d)) continue;
                var f = Forward(d);
                int nx = x + (int)f.X, nz = z + (int)f.Z;
                if (nx < 0 || nx >= Nodes || nz < 0 || nz >= Nodes) continue;
                int next = nx + nz * Nodes;
                if (first[next] != -2) continue;
                first[next] = at == start ? d : first[at];
                if (next == target) return first[next];
                queue.Enqueue(next);
            }
        }

        return -1;
    }

    private bool ServiceTurn(CityCar c)
    {
        var m = Mission(c);
        if (m is null || m.Stage != 0) return false;
        CityPoint destination=m.Destination;
        if(CityRoadLevels.ById(MissionLevel(c)) is {} road && c.RoadLevel!=road.Id){
            bool east=c.Position.X<road.X;int entrance=road.I+(east?-4:4);
            destination=new(Coordinate(entrance),road.Z);
            if(c.I==entrance&&c.J==road.J){c.Outgoing=east?1:3;return true;}
        }
        int d = RoadDirection(c.I, c.J, destination);
        if (d < 0) return false;
        c.Outgoing = d;
        return true;
    }

    private void Dispatch(CityService kind, CityPedestrian? patient)
    {
        var destination = patient?.Position ?? PlayerPosition;
        CityCar? best = null;
        double score = double.MaxValue;
        // The roster gap is the same for all 160 trials; NeededPoliceKind walks
        // Services x Cars, which used to run once per trial.
        int carKind = kind == CityService.Ambulance ? 4 : NeededPoliceKind();
        if(carKind==12){DispatchAirPatrol();return;}
        for (int trial = 0; trial < 160; trial++)
        {
            int i = 1 + Pick(Nodes - 2), j = 1 + Pick(Nodes - 2), d = Pick(4);
            if (!HasRoad(i, j, (d + 2) % 4)) continue;
            var c = MakeCar(i, j, d, 18 + Random() * 8, false);
            c.Kind = carKind;
            double distance = (c.Position - PlayerPosition).Length;
            if (distance < SpawnDistance + 20 || !GroundCarSpawnClear(c) || !CarClear(c.Position, c.Heading, c)) continue;
            double cost = (c.Position - destination).Length;
            if (cost < score)
            {
                score = cost;
                best = c;
            }
        }

        if (best is null) return;
        best.Cruise = kind == CityService.Ambulance ? 10 : 23;
        Cars.Add(best);
        Register(best);
        Services.Add(new ServiceMission
        {
            CarId = best.Id, 
            Kind = kind, 
            PatientId = patient?.Id ?? 0, 
            Destination = destination
        });
        ChooseTurn(best);
        SpawnedCars++;
    }

    // One id -> person table per ServicesStep call, built on first use. Every
    // medic and patient lookup below used to be a Find over all ~6 000 walkers,
    // several per mission per frame. A person deployed during the same call is
    // not in the table yet and falls back to the list scan.
    private readonly Dictionary<int, CityPedestrian> _servicePeople = [];
    private bool _servicePeopleBuilt;

    private CityPedestrian? ServicePerson(int id)
    {
        if (id == 0) return null;

        if (!_servicePeopleBuilt)
        {
            _servicePeople.Clear();
            foreach (CityPedestrian p in Pedestrians) _servicePeople[p.Id] = p;
            _servicePeopleBuilt = true;
        }

        return _servicePeople.TryGetValue(id, out CityPedestrian? found) ? found : Pedestrians.Find(p => p.Id == id);
    }

    private void ServicesStep(double dt)
    {
        // Out of the militia's sight for 15 s the level fades five times faster:
        // hiding works, a chase that never saw the player again does not last minutes.
        if ((_sightClock -= dt) <= 0) { _sightClock = .5; if (WantedLevel > 0 && PoliceSees()) _lastPoliceSight = Time; }
        if (Time - _lastCrime > 12) Wanted = Math.Max(0, Wanted - dt * (Time - _lastPoliceSight > 15 && Time - _lastCrime > 25 ? .09 : .018));
        ArrestStep(dt);
        int pursuing = 0;
        _servicePeopleBuilt = false;

        foreach (ServiceMission m in Services.ToArray())
        {
            CityCar? c = Cars.Find(c => c.Id == m.CarId);

            if (c is null || c.Destroyed || c.Falling)
            {
                ReleaseMedics(m);
                Services.Remove(m);
                continue;
            }

            if (m.Kind == CityService.Police)
            {
                if(c.AirPatrol){
                    if(WantedLevel<6||PlayerUnavailable||c.Driverless||c.Hero){
                        c.AirDeparting=true;Services.Remove(m);
                    }else{m.Destination=PlayerPosition;pursuing++;}
                    continue;
                }
                if (c.Burning || BoxedIn(c) || (c.Kind != 14 && OnFoot && SameHeight(c,Player) && (c.Position - PlayerPosition).Length < 19 && ClearLevelSight(c.Position,c.RoadLevel,PlayerPosition,Player.RoadLevel)))
                {
                    c.Command = new DriveInput(0, 0, true);
                    c.PanicDriver = false;

                    if (Math.Abs(c.Speed) < 1.2 && !c.CrewDeployed)
                    {
                        DeployCrew(c, m, false);
                    }
                }

                if (c.CrewDeployed)
                {
                    m.Medics.RemoveAll(id => ServicePerson(id) is not { Dead: false });

                    if (m.Medics.Count == 0)
                    {
                        Services.Remove(m);
                        continue;
                    }

                    if (WantedLevel > 0)
                    {
                        foreach (int id in m.Medics)
                        {
                            if (ServicePerson(id) is { Dead: false } officer) officer.AggressionUntil = Time + 3;
                        }
                    }

                    if (++pursuing > PoliceBudget)
                    {
                        ReleaseMedics(m);
                        Services.Remove(m);
                    }

                    continue;
                }

                m.Destination = PlayerPosition;

                if (pursuing++ >= PoliceBudget)
                {
                    c.Cruise = 5;
                    if (m.Stage == 1) RejoinRoad(c);
                    Services.Remove(m);
                    ChooseTurn(c);
                    continue;
                }

                if (c.Kind == 14) AimCarrierAtPlayer(c, dt);

                // Replan before entering a crossing, never replace a curve mid-turn.
                if ((m.Repath -= dt) <= 0)
                {
                    m.Repath = 2d / 3;
                    if (c.Phase == 0) ChooseTurn(c);
                }

                continue;
            }

            for (int n = m.Medics.Count - 1; n >= 0; n--)
            {
                CityPedestrian? a = ServicePerson(m.Medics[n]);

                if (a is null || a.Dead)
                {
                    a?.ServiceId = 0;
                    m.Medics.RemoveAt(n);
                }
            }

            CityPedestrian? p = ServicePerson(m.PatientId);
            if (p is null || !p.Dead)
            {
                m.Stage = 3;
            }

            if (m.Stage == 0 && p is not null)
            {
                m.Destination = p.Position;
                if ((m.Repath -= dt) <= 0)
                {
                    m.Repath = 2;
                    if (c.Phase == 0) ChooseTurn(c);
                }

                if (SameHeight(c,p) && (c.Position - p.Position).Length < 19 && c.Phase is 0 or 3)
                {
                    m.Stage = 1;
                    m.Timer = 0;
                }
            }

            if (m.Stage == 1)
            {
                m.Timer += dt;

                if (Math.Abs(c.Speed) < .7)
                {
                    CityPoint right = Right(new CityPoint(Math.Sin(c.Heading), Math.Cos(c.Heading)));

                    for (int n = 0; n < 2; n++)
                    {
                        CityPoint at = c.Position + right * (c.HalfWidth + 1.2 + n * .75);
                        if (!CityRoadLevels.ClearAt(at,c.RoadLevel)&&!ClearWalk(at)) at = c.Position - right * (c.HalfWidth + 1.2 + n * .75);
                        if (!CityRoadLevels.ClearAt(at,c.RoadLevel)&&!ClearWalk(at)) continue;
                        CityPedestrian medic = MakePerson(c.I, c.J, 1, 1, at);
                        medic.RoadLevel=c.RoadLevel;
                        medic.ServiceId = c.Id;
                        medic.Persona = 4;
                        medic.Cruise = 1.8;
                        medic.Fear = 0;
                        Pedestrians.Add(medic);
                        Register(medic);
                        m.Medics.Add(medic.Id);
                    }

                    m.Stage = 2;
                    m.Timer = 0;
                }
            }
            else if (m.Stage == 2 && p is not null)
            {
                bool close = m.Medics.Any(id => ServicePerson(id) is { Dead: false } a && a.RoadLevel==p.RoadLevel && (a.Position - p.Position).Length < 1.5);
                if (close) m.Timer += dt;

                if (m.Timer > 6)
                {
                    RevivePatient(p);
                    m.Stage = 3;
                    m.Timer = 0;
                }

                if (m.Medics.Count == 0)
                {
                    m.Stage = 3;
                    m.Timer = 0;
                }
            }
            else if (m.Stage == 3)
            {
                m.Timer += dt;

                if (m.Timer > 20 || m.Medics.All(id => ServicePerson(id) is not { Dead: false } a || (a.Position - c.Position).Length <= 3.5))
                {
                    ReleaseMedics(m);
                    Services.Remove(m);
                    c.Cruise = 5;
                    ChooseTurn(c);
                }
            }
        }

        _servicePeople.Clear();
        _servicePeopleBuilt = false;
        PoliceDispatchStep(dt);
        if ((_dispatchClock -= dt) > 0) return;
        _dispatchClock = 3;
        int medical = Services.Count(m => m.Kind == CityService.Ambulance);

        if (medical < 2)
        {
            CityPedestrian? patient = Pedestrians
                .Where(p => p is { IsPlayer: false, Dead: true, Drowned: false, ServiceId: 0 } && Time - p.DeathTime > 9 && !PatientClaimed(p.Id))
                .OrderBy(p => p.DeathTime)
                .FirstOrDefault();

            if (patient is not null)
            {
                Dispatch(CityService.Ambulance, patient);
            }
        }

    }

    public void RevivePatient(CityPedestrian p)
    {
        if (!p.Dead || p.Drowned) return;
        p.Dead = false;
        p.BloodPool = false;
        p.FlightHeight = p.VerticalSpeed = p.KnockedFor = 0;
        p.Body.Vx = p.Body.Vy = 0;
        p.Fear = .3;
        // Existing blood decals belong to the scene, not the revived actor.
        p.GunDamage = 0;
        p.BurstRemaining = 0;
        p.Detached = true;
        p.ServiceId = 0;
        p.GroupId = -1;
        p.InterestUntil = 0;
        p.Aiming = false;
        p.AggressionUntil = 0;
        Reanchor(p);
        PatientsRevived++;
    }

    private void ReleaseMedics(ServiceMission m)
    {
        foreach (int id in m.Medics)
        {
            CityPedestrian? p = Pedestrians.Find(p => p.Id == id);
            if (p is null) continue;

            if (m.Kind == CityService.Police)
            {
                p.ServiceId = 0;
                p.AggressionUntil = WantedLevel > 0 ? Time + 12 : 0;
                continue;
            }

            if (!p.Dead)
            {
                ReleasePerson(p);
                Remove(p.Body);
                Pedestrians.Remove(p);
            }
            else
            {
                p.ServiceId = 0;
            }
        }

        m.Medics.Clear();
    }

    private bool ServiceWalk(CityPedestrian p, double dt)
    {
        if (p is { Officer: true, Dead: false } && WantedLevel > 0 && !PlayerUnavailable && p.RoadLevel==(OnFoot?Player.RoadLevel:Hero.RoadLevel))
        {
            p.AggressionUntil = Time + 3;
            // A subdued player is approached for the arrest (to the door of a
            // stopped car, to arm's length on foot); otherwise hold at eleven metres.
            bool arresting = WantedLevel < 4 && Surrendering;
            CityPoint goal = OnFoot || !arresting ? PlayerPosition : NearestDoor(Hero, p.Position);
            CityPoint offset = goal - p.Position;
            CityPoint v = offset.Length > (arresting ? .9 : 11) || !arresting && !ClearSight(p.Position, PlayerPosition)
                ? offset * (Math.Min(3.0, arresting ? offset.Length * 1.5 : 3.0) / Math.Max(1e-3, offset.Length))
                : default;

            if (p.Physical)
            {
                // The door of a stopped car is reached with the plain blend: the
                // walker's stopped-car sidestep would hold the officer off it.
                if (arresting && !OnFoot) MoveFarSocial(p, v, dt);
                else MovePerson(p, v, dt, v.Length > 0 ? goal : null);
            }
            else
            {
                MoveFarSocial(p, v, dt);
            }

            return true;
        }

        if (p.ServiceId == 0) return false;
        var m = Services.Find(m => m.CarId == p.ServiceId);
        if (m is null)
        {
            p.ServiceId = 0;
            return false;
        }

        var car = Cars.Find(c => c.Id == m.CarId);
        if (car is null) return false;
        // Police crews have no patient (PatientId 0), and the walk uses it only at
        // stage 2: the unconditional Find scanned all walkers per crew per substep.
        var patient = m.Stage == 2 && m.PatientId != 0 ? Pedestrians.Find(a => a.Id == m.PatientId) : null;
        var target = patient is not null ? patient.Position : car.Position;
        var d = target - p.Position;
        double stop = m.Stage == 2 ? 1.1 : 2.7;
        var desired = d.Length > stop ? d * (1.8 / Math.Max(.1, d.Length)) : default;
        // Use the same local collision/separation and facade avoidance as walkers.
        if (p.Physical) MovePerson(p, desired, dt, target);
        else MoveFarSocial(p, p.RoadLevel==0?NavigateWalker(p, desired, target, dt):desired, dt);
        return true;
    }

    private void RejoinRoad(CityCar c)
    {
        if (c.Reservation >= 0 && _reservations[c.Reservation] == c.Id) _reservations[c.Reservation] = -1;
        c.Reservation = -1;
        double best = double.MaxValue;

        int ci = Math.Clamp((int)Math.Round((c.Position.X - Coordinate(0)) / Spacing), 0, Nodes - 1),
            cj = Math.Clamp((int)Math.Round((c.Position.Z - Coordinate(0)) / Spacing), 0, Nodes - 1);
        CityPoint target = c.Position;

        for (int i = Math.Max(0, ci - 2); i <= Math.Min(Nodes - 1, ci + 2); i++)
        for (int j = Math.Max(0, cj - 2); j <= Math.Min(Nodes - 1, cj + 2); j++)
        for (int d = 0; d < 4; d++)
        {
            if (!HasRoad(i, j, (d + 2) % 4)) continue;
            CityPoint f = Forward(d);
            CityPoint p = new CityPoint(Coordinate(i), Coordinate(j)) - f * StopLine + Right(f) * Lane;
            double score = (p - c.Position).Length;
            if (score >= best || !ClearChase(c.Position, p)) continue;
            best = score;
            target = p;
            c.I = i;
            c.J = j;
            c.Direction = d;
        }

        if (best < double.MaxValue)
        {
            // The rejoin target sits in lane 0 while the following crossing path
            // uses LanePosition(c): a lane-1 car jumped 3.35 m sideways at the stop line.
            c.LaneIndex = 0;
            c.Path = new CityPath(c.Position, target);
            c.Along = 0;
            c.Phase = 0;
            ChooseTurn(c);
        }
    }

    private static bool ClearChase(CityPoint a, CityPoint b)
    {
        CityPoint d = b - a;
        int n = Math.Max(1, (int)Math.Ceiling(d.Length));
        CityPoint side = Right(d * (1 / Math.Max(.01, d.Length))) * 1.1;

        for (int k = 0; k <= n; k++)
        {
            CityPoint p = a + d * (k / (double)n);

            if (!ClearWalk(p) || !ClearWalk(p + side) || !ClearWalk(p - side))
            {
                return false;
            }
        }

        return true;
    }
}
