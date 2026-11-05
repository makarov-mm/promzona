using System.Text.Json;

namespace Promzona.Game;

// Fixed game-owned schemas only: save data cannot name or instantiate CLR types.
public sealed partial class CityTraffic
{
    public CityWorldSave CaptureWorld(CityModel model) => new()
    {
        WideRoads=CityLayout.WideRoads, RoadRevision=CityLayout.Current.RoadRevision, TransitRevision=1, ScooterRevision=1, Scooters=Scooters.Select(s=>new ActorSave(CityState.Capture(s,compact:true),CityState.Body(s.Body),null)).ToList(), Clock = CityState.Capture(this), Weather = CityState.Capture(model),
        TransitRiders=TransitRiders.Select(r=>r with{}).ToList(),
        Player = new(CityState.Capture(Player, compact: true), CityState.Body(Player.Body), null),
        Pickups = Pickups.Select(p => p.Copy()).ToList(), Ammunition = Inventory.Capture(), SelectedWeapon = Inventory.Selected,
        Cars = [.. Cars.Select(c => new ActorSave(CityState.Capture(c, compact: true), CityState.Body(c.Body), c.Path.ExportCompact(), c.Path.IsBezier))],
        People = Pedestrians.Where(p => !p.IsPlayer).Select(p => new ActorSave(CityState.Capture(p, compact: true), CityState.Body(p.Body), p.Path?.ExportCompact(), p.Path?.IsBezier ?? false)).ToList(),
        Animals = Animals.Select(a => new ActorSave(CityState.Capture(a, compact: true), CityState.Body(a.Body), null)).ToList(),
        Props = Props.Where(p => p.Body is not null || p.Moved).Select(p => new ActorSave(CityState.Capture(p, compact: true), CityState.Body(p.Body), null)).ToList(),
        Marks = JsonSerializer.Deserialize<List<CityMark>>(JsonSerializer.Serialize(Marks, CityState.Json), CityState.Json)!,
        Cash = [.. Cash.Select(c => new CashDrop { Position = c.Position, RoadLevel=c.RoadLevel, Created = c.Created, Amount = c.Amount })],
        Services =
        [
            .. Services.Select(m => new ServiceMission
            {
                CarId = m.CarId, PatientId = m.PatientId, Stage = m.Stage, Kind = m.Kind, Timer = m.Timer,
                Repath = m.Repath, Destination = m.Destination, Medics = [.. m.Medics]
            })
        ],
        Groups =
        [
            .. Groups.Select(g => new CityGroup { Id = g.Id, Leader = g.Leader, Members = [.. g.Members], Trail = [.. g.Trail] })
        ],
        Incidents = [.. Incidents.Select(i => new CityIncident(i.Id, i.Position, i.Created))],
        Reservations = (int[])_reservations.Clone(),
        PedPlaces = (int[])_pedPlaces.Clone()
    };

    public static int SavedGridSize(CityWorldSave s){int n=(int)Math.Sqrt(s.Reservations.Length);if(n is not (33 or 49)||n*n!=s.Reservations.Length)throw new InvalidDataException("Invalid city size");return n;}
    public static void ValidateWorld(CityWorldSave s)
    {
        if(s.TransitRiders.Count>96||s.TransitRiders.Any(r=>!double.IsFinite(r.Destination)||r.Person.State.Count>110))throw new InvalidDataException("Invalid transit passengers");
        if(s.ScooterRevision is <0 or >1||s.Scooters.Count>ScooterCap)throw new InvalidDataException("Invalid scooter fleet");
        if(s.TransitRevision is <0 or >1)throw new InvalidDataException("Unsupported transit revision");
        if(s.RoadRevision is <0 or >4)throw new InvalidDataException("Unsupported road layout revision");
        int nodes=SavedGridSize(s);double edge=(nodes-1)*Spacing*.5+60,land=edge-6;
        if ((s.Pickups is null) != (s.Ammunition is null)) throw new InvalidDataException("Incomplete pickup state");
        if (s.Clock.Count > 1200 || s.Weather.Count > 200) throw new InvalidDataException("Invalid clock state");
        foreach (var v in s.Clock.Values) CityState.ValidateValue(v);
        foreach (var v in s.Weather.Values) CityState.ValidateValue(v);
        if (s.Ammunition is not null) CityInventory.Validate(s.Ammunition);
        if (s.Pickups is { } pickups)
        {
            if (pickups.Count > PickupCap || pickups.Select(p => p.Id).Distinct().Count() != pickups.Count ||
                pickups.Count(p => p.MapSpawn && p.Kind == CityPickupKind.Pills) > PillTarget ||
                pickups.Count(p => p.MapSpawn && p.Kind == CityPickupKind.Weapon) > MapWeaponTarget || pickups.Count(p=>p.MapSpawn&&p.Kind==CityPickupKind.EnergyDrink)>EnergyTarget)
                throw new InvalidDataException("Invalid pickup counts");
            if (pickups.Any(p => p.Id <= 0 || p.Id == int.MaxValue ||
                                 !double.IsFinite(p.Position.X + p.Position.Z + p.Created) ||
                                 Math.Abs(p.Position.X) > land || Math.Abs(p.Position.Z) > land ||
                                 !Enum.IsDefined(p.Kind) ||
                                 (p.Kind == CityPickupKind.Weapon
                                     ? CityWeapons.Find(p.Weapon) is null || p.Weapon is CityWeaponId.Bazooka or CityWeaponId.Flamethrower&&!p.MapSpawn
                                     : p.Weapon != CityWeaponId.None)))
                throw new InvalidDataException("Invalid pickup state");
        }

        if (s.Cars.Count < 1 || s.Cars.Count > CarCap + MaxPoliceUnits + 2 || s.People.Count > PeopleCap + 64 ||
            s.Animals.Count > AnimalCap || s.Props.Count > 20000 || s.Marks.Count > 1000 || s.Cash.Count > 256 ||
            s.Services.Count > MaxPoliceUnits + 2 || s.Groups.Count > 240 || s.Incidents.Count > 48 ||
            s.Reservations.Length != nodes * nodes ||
            s.PedPlaces.Length != nodes * nodes * 4) throw new InvalidDataException("Unsupported world counts");
        foreach (var a in s.Scooters.Concat(s.Cars).Concat(s.People).Concat(s.TransitRiders.Select(r=>r.Person)).Concat(s.Animals).Concat(s.Props)
                     .Concat(s.Player is null ? [] : new[] { s.Player }))
        {
            if (a.State.Count > 110 || a.Path is { Length: < 2 or > 300 } || (a.Bezier && a.Path is not { Length: 4 }))
                throw new InvalidDataException("Invalid actor state");
            if (a.Body is { } b && (!double.IsFinite(b.X + b.Z + b.Angle + b.Vx + b.Vz + b.W) ||
                                    Math.Abs(b.X) > edge + 100 ||
                                    Math.Abs(b.Z) > edge + 100))
                throw new InvalidDataException("Actor outside save bounds");
            foreach (var v in a.State.Values) CityState.ValidateValue(v);
            if (a.Path is not null && a.Path.Any(p =>
                    !double.IsFinite(p.X + p.Z) || Math.Abs(p.X) > edge + 100 || Math.Abs(p.Z) > edge + 100))
                throw new InvalidDataException("Invalid route");
        }

        var cars = s.Cars.Select(a =>
        {
            var c = new CityCar();
            CityState.Restore(c, a.State);
            return c;
        }).ToArray();
        var people = s.People.Select(a =>
        {
            var p = new CityPedestrian();
            CityState.Restore(p, a.State);
            return p;
        }).ToArray();
        ValidateScooterSave(s,people,cars,nodes);
        var riders=s.TransitRiders.Select(r=>{var p=new CityPedestrian();CityState.Restore(p,r.Person.State);return p;}).ToArray();
        // Walker grid state is indexed straight into the node and place tables on
        // the first Step; an edited file used to load and then throw there.
        if (people.Concat(riders).Any(p =>
                p.I < 0 || p.J < 0 || p.I >= nodes || p.J >= nodes || p.NextI < 0 || p.NextJ < 0 || p.NextI >= nodes || p.NextJ >= nodes ||
                p.OriginKey < -1 || p.OriginKey >= nodes * nodes * 4 || p.DestinationKey < -1 || p.DestinationKey >= nodes * nodes * 4))
            throw new InvalidDataException("Invalid walker route state");
        if(riders.Any(p=>p.IsPlayer||p.Id<=0)||riders.Select(p=>p.Id).Concat(people.Select(p=>p.Id)).Distinct().Count()!=riders.Length+people.Length||s.TransitRiders.Any(r=>!cars.Any(c=>c.Id==r.CarId&&c.TransitRoute>0)))throw new InvalidDataException("Invalid transit passenger identities");
        if (people.Any(p => p.IsPlayer) || !cars[0].Hero || cars.Count(c => c.Hero) != 1 ||
            cars.Select(c => c.Id).Distinct().Count() != cars.Length ||
            people.Select(p => p.Id).Distinct().Count() != people.Length)
            throw new InvalidDataException("Invalid actor identities");
        if (s.Player is { } ps)
        {
            var p = new CityPedestrian();
            CityState.Restore(p, ps.State);
            if (!p.IsPlayer || p.Id <= 0 || ps.Body is null || people.Any(a => a.Id == p.Id)||riders.Any(a=>a.Id==p.Id))
                throw new InvalidDataException("Invalid player state");
        }

        if (cars.Any(c =>
                c.I < 0 || c.J < 0 || c.I >= nodes || c.J >= nodes || c.Direction < 0 || c.Direction > 3 ||
                c.Kind < 0 || c.Kind > 14 || (c.Kind<9||c.Kind>10)&&c.TransitRoute!=0 || c.TransitRoute is <0 or >4 || c.Kind==9&&c.TransitRoute is not (1 or 3) || c.Kind==10&&c.TransitRoute is not (2 or 4) || c.Reservation >= nodes * nodes) ||
            s.Cars.Any(c => c.Path is null)) throw new InvalidDataException("Invalid vehicle route state");
        if (s.Services.Count(m => m.Kind == CityService.Police) > MaxPoliceUnits ||
            s.Services.Count(m => m.Kind == CityService.Ambulance) > 2 ||
            s.Services.Select(m => m.CarId).Distinct().Count() != s.Services.Count ||
            s.Services.Any(m =>
                !cars.Any(c => c.Id == m.CarId) || m.Medics.Count > 4 ||
                m.Medics.Any(id => !people.Any(p => p.Id == id))))
            throw new InvalidDataException("Invalid dispatch state");
    }

    public void RestoreWorld(CityWorldSave s, CityModel model)
    {
        _walkDetours.Clear();
        _clearanceChecks.Clear();
        _socialPeople.Clear();
        _cover.Clear();
        ValidateWorld(s);
        ResetDrone();HelicopterLanding=false;
        OnFoot = false;
        World.Clear();
        AddScenery();
        Cars.Clear();
        Pedestrians.Clear();
        Animals.Clear();
        Services.Clear();
        Groups.Clear();
        Incidents.Clear();
        TransitRiders.Clear();TransitRiders.AddRange(s.TransitRiders);_passengersInitialized=true;
        Sounds.Clear();
        Particles.Clear();
        Shots.Clear();
        foreach (var a in s.Cars)
        {
            var c = new CityCar();
            CityState.Restore(c, a.State);
            c.Path = CityPath.Restore(a.Path!, a.Bezier);
            Cars.Add(c);
            Register(c);
            CityState.Restore(c, a.State);
            CityState.RestoreBody(c.Body, a.Body);
            UpdateTerrainSupport(c);
            Attach(c.Body, c.Physical && !c.Falling);
            if (c.Destroyed) RestoreWreckBody(c);
        }

        foreach (var a in s.People)
        {
            var p = new CityPedestrian();
            CityState.Restore(p, a.State);
            p.Path = a.Path is null ? null : CityPath.Restore(a.Path, a.Bezier);
            Pedestrians.Add(p);
            Register(p);
            CityState.Restore(p, a.State);
            CityState.RestoreBody(p.Body, a.Body);
            Attach(p.Body, p.Physical && !p.Detached && !p.Dead);
        }

        foreach (var saved in s.Animals)
        {
            var a = new CityAnimal();
            CityState.Restore(a, saved.State);
            if (saved.Body is not null)
            {
                int at = World.AddCircle(a.Position.X, a.Position.Z, a.Radius, a.Mass / (Math.PI * a.Radius * a.Radius),
                    .13, .04, 0);
                a.Body = World.Bodies[at];
                a.Body.InvI = 0;
                CityState.RestoreBody(a.Body, saved.Body);
                Attach(a.Body, a.Physical && !a.Detached && !a.Dead);
            }

            Animals.Add(a);
        }

        foreach (var p in Props)
        {
            p.Body = null;
            p.Height = p.Vy = p.Tilt = p.Spin = p.DisplacedFor = p.Water = 0;
        }

        foreach (var a in s.Props)
        {
            int id = CityState.Int(a.State, nameof(StreetProp.Id)) ?? -1;
            var p = Props.Find(p => p.Id == id);
            if (p is null)
            {
                // Older saves may contain props removed by new dry-land/amenity placement rules.
                var previous = new StreetProp();
                CityState.Restore(previous, a.State);
                if (!CityWater.UrbanSite(previous.Home, previous.Radius + .2) ||
                    !CityAmenities.ClinicClear(previous.Home) || !CityAmenities.PropClear(previous.Home, previous.Radius + .15)) continue;
                throw new InvalidDataException("Unknown prop");
            }
            CityState.Restore(p, a.State);
            if (a.Body is { } b)
            {
                p.Body = World.Bodies[AddPropBody(p, b.X, b.Z)];
                CityState.RestoreBody(p.Body, b);
            }
        }

        Marks.Clear();
        Marks.AddRange(s.Marks);
        Cash.Clear();
        Cash.AddRange(s.Cash);
        Services.AddRange(s.Services);
        Groups.AddRange(s.Groups);
        Incidents.AddRange(s.Incidents);
        Array.Copy(s.Reservations, _reservations, _reservations.Length);
        Array.Copy(s.PedPlaces, _pedPlaces, _pedPlaces.Length);
        // Fields introduced after format 3 must not leak from the world that was
        // running when an older slot (which has no such fields) is loaded.
        CompletedDeliveries=0;
        OwnedGarageId=ChainStage=ChainsCompleted=TransitBoardings=TransitAlightings=0;
        PassengerMode=_passengerEntry=_chainContract=false;_passengerClock=0;
        ResetPeaceJob();
        AnimalPopulationRevision=1;
        CityState.Restore(this, s.Clock);
        if(AnimalPopulationRevision<2){
            OriginalPigeons*=2;InitialPigeonTarget*=2;PigeonTarget*=2;OtherAnimalTarget*=2;
            AnimalPopulationRevision=2;
        }
        // Reflection restores public properties too; remote possession is never resumed from a save.
        ResetDrone();HelicopterLanding=false;
        CityState.Restore(model, s.Weather);
        if (s.Pickups is null) ResetPickups();
        else
        {
            Pickups.Clear();
            Pickups.AddRange(s.Pickups.Select(p => p.Copy()));
            Inventory.Restore(s.Ammunition!);
            if (s.SelectedWeapon is { } selected) Inventory.Select(selected);
            _pickupId = Math.Max(_pickupId, Pickups.Select(p => p.Id).DefaultIfEmpty(0).Max());
        }

        if (s.Player is { } savedPlayer)
        {
            Player = new();
            CityState.Restore(Player, savedPlayer.State);
            Register(Player);
            CityState.Restore(Player, savedPlayer.State);
            CityState.RestoreBody(Player.Body, savedPlayer.Body);
            Player.Physical = OnFoot;
            Attach(Player.Body, OnFoot && !Player.Dead && !Player.Detached);
            if (OnFoot) Pedestrians.Add(Player);
        }
        else 
            ResetPlayer();

        ValidatePeaceJob();
        _peaceEnabled=false;_peacePrevious=PlayerPosition;_peaceSoundAt=Time;
        CancelEntry();
        if(s.TransitRevision==0)foreach(var c in Cars.Where(c=>c.TransitRoute>0)){
            var route=CityTransit.Routes.Where(r=>r.Kind==c.Kind).MinBy(r=>(r.Sample(r.Closest(c.Position)).Point-c.Position).Length);
            if(route is null)continue;
            c.TransitRoute=route.Id;c.TransitAlong=route.Closest(c.Position);
            var pose=route.Sample(c.TransitAlong);
            for(int attempt=0;attempt<80&&!CarClear(pose.Point,pose.Heading,c);attempt++){
                c.TransitAlong=route.Wrap(c.TransitAlong+c.HalfLength*2+4);pose=route.Sample(c.TransitAlong);
            }
            c.Position=pose.Point;c.Heading=pose.Heading;
            c.Body.X=pose.Point.X;c.Body.Y=pose.Point.Z;c.Body.Angle=-pose.Heading;c.Body.Vx=c.Body.Vy=c.Body.W=0;
            c.TransitStopUntil=c.TransitNextStop=0;UpdateTerrainSupport(c);
        }
        Rockets.Clear();_punchUntil=0;_punchPending=false;
        EnsureTransit();
        RestoreScooters(s);
        ClearPlayerInput();
        _controlClock = _sceneryClock = _detailClock = 0;
        RebuildActorGrid();
        RebuildPropGrid();
        RefreshDetail();
        RefreshScenery();
    }
}
