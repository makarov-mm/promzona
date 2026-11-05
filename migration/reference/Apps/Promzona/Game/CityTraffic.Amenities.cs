namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public double EnergySeconds { get; private set; }
    private double _buyAt, _clinicBill, _clinicPoorAt;
    public const int ClinicPrice = 40; // a full recovery inside the clinic square
    public int LastBill { get; private set; }
    private FoodSite[]? _foodSites;
    private CityPoint[]? _busStops;
    private GarageSite[]? _garages;
    private MarketSite[]? _markets;
    public IReadOnlyList<GarageSite> Garages => _garages ??= [.. CityAmenities.Garages()];
    public IReadOnlyList<MarketSite> Markets => _markets ??= [.. CityAmenities.Markets()];
    public IReadOnlyList<FoodSite> FoodSites => _foodSites ??= [.. CityAmenities.Food()];
    public IReadOnlyList<CityPoint> BusStops => _busStops ??= [.. CityAmenities.Stops()];

    public FoodSite? NearbyFood()
    {
        if (!OnFoot || PlayerUnavailable || Player.KnockedFor > 0 || Player.RoadLevel != 0) return null;

        foreach (FoodSite s in FoodSites)
        {
            if ((s.Interaction - PlayerPosition).Length < FoodSite.PurchaseRadius && ClearSight(PlayerPosition, s.Interaction))
            {
                return s;
            }
        }

        return null;
    }

    public bool BuyFood()
    {
        FoodSite? found = NearbyFood();
        if (found is not { } s || Time < _buyAt) return false;
        _buyAt = Time + .8;
        if (PlayerHealth >= .999) return Message("full");
        if (Money < s.Price) return Message("poor");
        Money -= s.Price;
        Player.GunDamage = Math.Max(0, Player.GunDamage - s.Healing);
        Sound("spend", PlayerPosition, .7, ui: true);
        Message("food");
        return true;
    }

    private void AmenitiesStep(double dt)
    {
        EnergySeconds = Math.Max(0, EnergySeconds - dt);
        if (!OnFoot || PlayerUnavailable || Player.RoadLevel != 0) return;

        if (ClinicBlocks.Select(site => CityAmenities.ClinicEntrance(site.I, site.J)).Any(at => Math.Abs(at.X - PlayerPosition.X) < CityAmenities.ClinicHalf && Math.Abs(at.Z - PlayerPosition.Z) < CityAmenities.ClinicHalf))
        {
            ClinicHeal(dt);
        }
    }

    // The clinic square is paid (29.10.2026): ClinicPrice buys a full recovery,
    // charged by the dollar as the health comes back; an empty wallet is still
    // treated, three times slower, so death never leaves the player stuck.
    private void ClinicHeal(double dt)
    {
        if (Player.GunDamage <= 0)
        {
            _clinicBill = 0;
            return;
        }

        if (Money > 0)
        {
            double heal = Math.Min(Player.GunDamage, .075 * dt);
            Player.GunDamage = Math.Max(0, Player.GunDamage - heal);
            _clinicBill += heal * ClinicPrice;
            int due = (int)_clinicBill;
            if (due > 0)
            {
                due = Math.Min(due, Money);
                Money -= due;
                _clinicBill -= due;
            }
        }
        else
        {
            Player.GunDamage = Math.Max(0, Player.GunDamage - .025 * dt);

            if (Time >= _clinicPoorAt)
            {
                _clinicPoorAt = Time + 6;
                Message("clinic_poor");
            }
        }
    }

    private bool PlaceAtClinic()
    {
        // The nearest clinic to the place of death, not the first in the list:
        // a death on the far side of the island used to send the player back to
        // the same (14,14) doorway every time. The stay is billed: a quarter of
        // the wallet from forty dollars up, nothing from a poorer patient.
        CityPoint fallen = PlayerPosition;

        if (ClinicBlocks.OrderBy(c => (CityAmenities.ClinicEntrance(c.I, c.J) - fallen).Length).Any(clinic => PlaceAt(CityAmenities.ClinicEntrance(clinic.I, clinic.J))))
        {
            int bill = Money >= 40 ? Money / 4 : 0;
            Money -= bill;
            LastBill = bill;
            if (bill > 0) Message("bill", 5);
            return true;
        }

        return false;
    }

    /// <summary>Puts the player on foot at a doorway (clinic after death, the
    /// militia department after an arrest), healthy, unarmed in spirit but with
    /// the inventory untouched, pursuit and jobs ended.</summary>
    private bool PlaceAt(CityPoint origin)
    {
        for (int n = 0; n < 25; n++)
        {
            CityPoint at = origin + new CityPoint((n % 5 - 2) * .55, (n / 5 - 2) * .55);
            if (!ClearPlayerPoint(at, roadLevel: 0)) continue;
            AbandonScooter(Player);
            Remove(Player.Body);
            Pedestrians.Remove(Player);
            // Only a car the player was driving is left without a driver. Hero
            // stays Cars[0] after any exit, so a tram the player had ridden as a
            // passenger used to lose its driver here and block its line for good.
            if (!OnFoot && !PassengerMode)
            {
                Hero.Driverless = true;
                Hero.Command = new(0, 0, true);
            }

            OnFoot = true;
            PassengerMode = _passengerEntry = false;
            Player.RoadLevel = 0;
            Player.Position = at;
            Player.Body.X = at.X;
            Player.Body.Y = at.Z;
            Player.Body.Vx = Player.Body.Vy = Player.Body.W = 0;
            Player.BurnUntil = 0;
            Player.BurnPlayerCaused = false;
            Player.GunDamage = Player.FlightHeight = Player.VerticalSpeed = Player.KnockedFor = 0;
            Player.Dead = Player.Detached = Player.Drowned = Player.BloodPool = false;
            Player.Physical = true;
            Player.Heading = PlayerViewHeading = 0;
            Pedestrians.Add(Player);
            Attach(Player.Body, true);
            CancelEntry();
            ClearPlayerInput();
            ClearPlayerPursuit();
            JobsOnRespawn();
            EnergySeconds = 0;
            _clinicBill = 0;
            _detailClock = _sceneryClock = 0;
            RebuildActorGrid();
            return true;
        }

        return false;
    }

    private bool BusStop(CityCar c)
    {
        if (c.Kind != 5 || c.Hero || c.Burning || c.Driverless) return false;
        if (c.BusUntil > Time) return true;
        if (c.NextBusStop > Time || c.Phase != 0 || c.Direction != 0 || (CityLayout.WideRoads && c.LaneIndex == 0)) return false;

        if (BusStops.Any(stop => Math.Abs(c.Position.X - stop.X) < 5 && Math.Abs(c.Position.Z - stop.Z) < 2.5))
        {
            c.BusUntil = Time + 4 + (c.Id % 4);
            c.NextBusStop = Time + 38;
            return true;
        }

        return false;
    }
}
