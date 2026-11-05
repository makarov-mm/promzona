namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public const int PillTarget = 96, EnergyTarget=48, MapWeaponTarget = 16, PickupCap = 240;
    public const double PillHealing = .35;
    public List<CityPickup> Pickups { get; } = [];
    public CityInventory Inventory { get; } = new();
    public bool PlayerFireHeld;
    public int PlayerShotsFired { get; private set; }
    private uint _pickupRandom;
    private int _pickupId;
    private double _pickupClock, _playerShotAt;

    private double PickupRandom()
    {
        _pickupRandom = unchecked(_pickupRandom * 1664525 + 1013904223);
        return _pickupRandom / 4294967296d;
    }

    private void ResetPickups()
    {
        Pickups.Clear();
        Inventory.Clear();
        PlayerFireHeld = false;
        PlayerShotsFired = PunchesThrown = 0; Rockets.Clear(); _punchUntil=0;_punchPending=false;EnergySeconds=0;_buyAt=0;
        _playerShotAt = 0;
        _pickupRandom = unchecked((uint)CityLayout.Current.Seed ^ 0x52a98317u);
        _pickupId = 0;
        _pickupClock = 0;
        FillMapPickups(true);
    }

    private void FillMapPickups(bool initial)
    {
        if (!_useScenery) return;

        int flamers=Pickups.Count(p=>p.MapSpawn&&p.Weapon==CityWeaponId.Flamethrower);
        int launchers=Pickups.Count(p=>p.MapSpawn&&p.Weapon==CityWeaponId.Bazooka);
        // Upgrade legacy supply populations out of sight without creating extra items.
        if(!initial&&launchers<2)foreach(var item in Pickups.Where(p=>p.MapSpawn&&p.Kind==CityPickupKind.Weapon&&p.Weapon!=CityWeaponId.Bazooka&&p.Weapon!=CityWeaponId.Flamethrower&&(p.Position-PlayerPosition).Length>SpawnDistance)){
            item.Weapon=CityWeaponId.Bazooka;if(++launchers>=2)break;
        }
        if(!initial&&flamers<2)foreach(var item in Pickups.Where(p=>p.MapSpawn&&p.Kind==CityPickupKind.Weapon&&p.Weapon is CityWeaponId.Pistol or CityWeaponId.Automatic&&(p.Position-PlayerPosition).Length>SpawnDistance)){
            item.Weapon=CityWeaponId.Flamethrower;if(++flamers>=2)break;
        }
        int pills = Pickups.Count(p => p is { MapSpawn: true, Kind: CityPickupKind.Pills }),
            energy = Pickups.Count(p=>p.MapSpawn&&p.Kind==CityPickupKind.EnergyDrink),
            weapons = Pickups.Count(p => p is { MapSpawn: true, Kind: CityPickupKind.Weapon });

        // Separate deterministic random stream does not perturb traffic or animals.
        int budget = initial ? 2000 : 100;

        while (budget-- > 0 && Pickups.Count < PickupCap && (pills < PillTarget || weapons < MapWeaponTarget || energy<EnergyTarget))
        {
            int i = (int)(PickupRandom() * (Nodes - 1)), j = (int)(PickupRandom() * (Nodes - 1));
            bool vertical = PickupRandom() < .5;
            double across = PickupRandom() < .5 ? Sidewalk : Spacing - Sidewalk, along = 9 + PickupRandom() * (Spacing - 18);
            var at = new CityPoint(Coordinate(i) + (vertical ? across : along),
                Coordinate(j) + (vertical ? along : across));
            if (!CityWater.UrbanSite(at,1)||!ClearWalk(at) || Math.Abs(at.X) > LandEdge - 12 || Math.Abs(at.Z) > LandEdge - 12 ||
                (!initial && (at - PlayerPosition).Length < SpawnDistance)) continue;
            if (Pickups.Any(p => (p.Position - at).Length < 12) || CityLayout.Current.FurnitureNear(at.X, at.Z, .8)
                                                                || NearbyProps(at, 3).Any(p =>
                                                                    (p.Position - at).Length < p.Radius + .9) ||
                                                                Cars.Any(c => CarPersonOverlap(c.Position, c.Heading,
                                                                    at, .4, c.HalfLength, c.HalfWidth))) continue;
            bool pill = pills < PillTarget, drink=!pill&&energy<EnergyTarget;
            var weapon = pill||drink ? CityWeaponId.None :
                launchers<2 ? CityWeaponId.Bazooka : flamers<2?CityWeaponId.Flamethrower:
                (initial ? weapons % 4 == 3 : PickupRandom() < .25) ? CityWeaponId.Automatic : CityWeaponId.Pistol;
            if(weapon==CityWeaponId.Bazooka)launchers++;if(weapon==CityWeaponId.Flamethrower)flamers++;
            Pickups.Add(new()
            {
                Id = ++_pickupId, Kind = pill ? CityPickupKind.Pills : drink?CityPickupKind.EnergyDrink:CityPickupKind.Weapon, Weapon = weapon,
                Position = at, Created = Time - 1, MapSpawn = true
            });
            if (pill) pills++;
            else if(drink)energy++;else weapons++;
        }
    }

    private void DropWeapon(CityPedestrian p)
    {
        CityWeaponId weapon = p.Weapon;
        if (weapon is CityWeaponId.None or CityWeaponId.Bazooka or CityWeaponId.Flamethrower) return;
        p.WeaponReleased = true;
        p.BurstRemaining = 0;
        if (Math.Abs(p.Position.X) > LandEdge - 1 || Math.Abs(p.Position.Z) > LandEdge - 1) return;
        // Bound drops independently of the permanent map population. Evict only
        // an old distant dropped item, never a visible pickup or map resource.
        if (Pickups.Count >= PickupCap)
        {
            int old = Pickups.FindIndex(x =>
                !x.MapSpawn && Time - x.Created > 20 && (x.Position - PlayerPosition).Length > SpawnDistance);
            if (old >= 0) Pickups.RemoveAt(old);
        }

        if (Pickups.Count < PickupCap)
        {
            Pickups.Add(new CityPickup
            {
                Id = ++_pickupId,
                Kind = CityPickupKind.Weapon,
                Weapon = weapon,
                Position = p.Position, RoadLevel=p.RoadLevel,
                Created = Time
            });
        }
    }

    private void PickupStep(double dt)
    {
        bool pills = false, weapons = false;
        CityCar hero = Hero;

        for (int i = Pickups.Count - 1; i >= 0; i--)
        {
            CityPickup p = Pickups[i];

            if (!p.MapSpawn && Time - p.Created > 300 && (p.Position - PlayerPosition).Length > SpawnDistance)
            {
                Pickups.RemoveAt(i);
                continue;
            }

            if (_useScenery&&Math.Abs(PlayerGround-CityRoadLevels.Surface(p.Position.X,p.Position.Z,p.RoadLevel))>1.5)continue;
            if (PlayerUnavailable || PlayerHealth <= 0 || Time - p.Created < 1) continue;

            if (OnFoot
                    ? Player.KnockedFor > 0 || Player.FlightHeight > .5 || (p.Position - PlayerPosition).Length > 1.05
                    : hero.Lift > .8 || !CarPersonOverlap(hero.Position, hero.Heading, p.Position, .6, hero.HalfLength,
                        hero.HalfWidth))
            {
                continue;
            }

            if (!ClearLevelSight(PlayerPosition,OnFoot?Player.RoadLevel:Hero.RoadLevel,p.Position,p.RoadLevel)) continue;
            if (p.Kind == CityPickupKind.Pills)
            {
                // Medicine belongs to the driver, also when collected through a car.
                // Repairs and fire suppression remain vehicle/garage mechanics.
                if (Player.GunDamage <= .001) continue;
                Player.GunDamage = Math.Max(0, Player.GunDamage - PillHealing);

                pills = true;
            }
            else if(p.Kind==CityPickupKind.EnergyDrink){
                if(EnergySeconds>2)continue;
                EnergySeconds=25;Sound("energy",PlayerPosition,.6,ui:true);
            }
            else
            {
                if (!Inventory.Give(p.Weapon)) continue;
                weapons = true;
            }

            Pickups.RemoveAt(i);
        }

        if (pills) Sound("heal", PlayerPosition, .65, ui: true);
        if (weapons) Sound("weapon_pickup", PlayerPosition, .65, ui: true);
        _pickupClock -= dt;
        if (_pickupClock <= 0)
        {
            _pickupClock = 4;
            FillMapPickups(false);
        }
    }
}
