namespace Promzona.Game;
public sealed partial class CityTraffic
{
    public int OwnedGarageId {get;private set;}
    public const int GaragePurchasePrice=400;
    public int OwnedVehicleCount=>Cars.Count(c=>c.OwnedGarageId==OwnedGarageId&&OwnedGarageId!=0&&!c.Destroyed&&!c.Falling);
    public GarageSite? OwnershipGarage()
    {
        if(PlayerUnavailable||(OnFoot?Player.RoadLevel:Hero.RoadLevel)!=0)return null;
        foreach(var g in Garages)if((g.Door - PlayerPosition).Length < GarageSite.ServiceRadius)return g;
        return null;
    }
    // Retained parking, not an inventory clone: identity, damage, paint and position
    // remain the same physical car in the versioned world save.
    public bool UseOwnedGarage()
    {
        if(OwnershipGarage() is not {} g)return false;
        if(OwnedGarageId==0)
        {
            if(Money<GaragePurchasePrice)return Message("poor");
            Money-=GaragePurchasePrice;OwnedGarageId=g.Id;Sound("spend",PlayerPosition,.7,ui:true);return true;
        }
        if(g.Id!=OwnedGarageId||OnFoot||Math.Abs(Hero.Speed)>.5||Hero.Kind is not (0 or 1)||Hero.Burning||ContractActive)return false;
        if(Hero.OwnedGarageId==0&&OwnedVehicleCount>=3)return Message("full");
        if(!InteractVehicle()||!OnFoot)return false;
        Hero.OwnedGarageId=g.Id;Hero.Driverless=true;
        return true;
    }
}
