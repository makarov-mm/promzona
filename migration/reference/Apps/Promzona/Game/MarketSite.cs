namespace Promzona.Game;

public readonly record struct MarketSite(int Id, CityPoint Position)
{
    public const double HalfX = 1.5, HalfZ = .8;
    internal const double CollisionHeight = 2.4;
    internal const double CustomerClearance = .75;
    internal const double PurchaseRadius = 1.8;
    public CityPoint Interaction => Position + new CityPoint(0, HalfZ + CustomerClearance);
}