namespace Promzona.Game;

public enum ShopItem { Pistol, Automatic, Pills, EnergyDrink }

/// <summary>Where the money goes (29.10.2026): a garage repairs and resprays the
/// car the player sits in, a market stall sells weapons, ammunition, pills and
/// energy drinks. Both are reached with the E key like the jobs; the market
/// opens a small list in the session, Buy is what the list calls.</summary>
public sealed partial class CityTraffic
{
    public const int RepairFull = 120, ResprayPrice = 30;
    public int RepairsBought { get; private set; }
    public int Resprays { get; private set; }
    public int Purchases { get; private set; }
    private double _serviceAt;

    private void ResetMarket() { RepairsBought = Resprays = Purchases = 0; _serviceAt = 0; OwnedGarageId=0; }

    /// <summary>The garage whose forecourt the stopped car is on.</summary>
    public GarageSite? NearbyGarage()
    {
        if (OnFoot || PlayerUnavailable || Cars.Count == 0 || Hero.RoadLevel != 0 || Math.Abs(Hero.Speed) >= 1.5 || !Hero.Grounded || Hero.Recovering) return null;
        foreach (var g in Garages) if ((g.Door - Hero.Position).Length < GarageSite.ServiceRadius) return g;
        return null;
    }

    /// <summary>Repair is priced by the damage, a respray is offered only to a wanted driver.</summary>
    public int RepairCost => Cars.Count == 0 ? 0 : (int)Math.Ceiling(Hero.Damage * RepairFull / 5) * 5;
    public bool ResprayOffered => WantedLevel > 0;
    public int GarageCost => RepairCost + (ResprayOffered ? ResprayPrice : 0);

    public bool InteractGarage()
    {
        if (NearbyGarage() is null || Time < _serviceAt) return false;
        _serviceAt = Time + .5;
        int cost = GarageCost;
        if (cost == 0) return Message("garage_fine");
        if (Money < cost) return Message("poor");
        Money -= cost;
        if (Hero.Damage > 0)
        {
            Hero.Damage = 0; Hero.Burning = false; Hero.FireAge = Hero.SafeDrivingTime = 0; RepairsBought++;
        }
        if (ResprayOffered)
        {
            // A fresh colour and new plates: the chase is called off and the witnesses forget the car.
            var previousPaint = CityAppearance.Paint(Hero);
            Hero.Paint = Hero.Paint % 12 + 1;
            if (CityAppearance.Paint(Hero) == previousPaint) Hero.Paint = Hero.Paint % 12 + 1;
            Resprays++; ClearPlayerPursuit();
        }
        Sound("spend", PlayerPosition, .7, ui: true);
        Message("garage");
        return true;
    }

    /// <summary>The stall whose counter the walker stands at.</summary>
    public MarketSite? NearbyMarket()
    {
        if (!OnFoot || PlayerUnavailable || Player.KnockedFor > 0 || Player.RoadLevel != 0) return null;
        foreach (var m in Markets) if ((m.Interaction - PlayerPosition).Length < MarketSite.PurchaseRadius && ClearSight(PlayerPosition, m.Interaction)) return m;
        return null;
    }

    /// <summary>A weapon is sold with its thirty rounds; once owned, the same entry sells rounds only.</summary>
    public int Price(ShopItem item) => item switch
    {
        ShopItem.Pistol => Inventory.Owns(CityWeaponId.Pistol) ? 40 : 120,
        ShopItem.Automatic => Inventory.Owns(CityWeaponId.Automatic) ? 80 : 350,
        ShopItem.Pills => 25,
        _ => 15
    };

    public bool Buy(ShopItem item)
    {
        if (NearbyMarket() is null || !Enum.IsDefined(item)) return false;
        int price = Price(item);
        if (Money < price) return Message("poor");
        switch (item)
        {
            case ShopItem.Pistol:
                if (!Inventory.Give(CityWeaponId.Pistol)) return Message("full");
                break;
            case ShopItem.Automatic:
                if (!Inventory.Give(CityWeaponId.Automatic)) return Message("full");
                break;
            case ShopItem.Pills:
                if (Player.GunDamage <= .001) return Message("full");
                Player.GunDamage = Math.Max(0, Player.GunDamage - PillHealing);
                break;
            default:
                if (EnergySeconds > 2) return Message("full");
                EnergySeconds = 25;
                break;
        }
        Money -= price; Purchases++;
        Sound("spend", PlayerPosition, .7, ui: true);
        Message("bought");
        return true;
    }

    /// <summary>The E key: a peace-job site first, then the garage forecourt, then a job. The market is opened by the session.</summary>
    public bool Interact() => InteractPeaceJob() || InteractGarage() || InteractJob();
}
