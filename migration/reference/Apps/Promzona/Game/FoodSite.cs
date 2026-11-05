namespace Promzona.Game;

internal sealed record FoodFixture(
    string Model, float Width, float Height, float Depth,
    CityPoint CollisionHalfSize, double CollisionHeight,
    double CounterOffset, int Price, double Healing)
{
    // The walking footprint includes a pedestrian's clearance around the solid body.
    internal CityPoint WalkHalfSize => CollisionHalfSize + new CityPoint(.4, .4);

    internal static readonly FoodFixture Kiosk = new(
        Model: "soviet_kiosk__3d_model", Width: 3.3f, Height: 2.75f, Depth: 2.2f,
        CollisionHalfSize: new(1.65, 1.2), CollisionHeight: 3.25,
        CounterOffset: 2.15, Price: 15, Healing: .50);

    internal static readonly FoodFixture VendingMachine = new(
        Model: "soviet_soda_machine", Width: .74f, Height: 1.85f, Depth: .75f,
        CollisionHalfSize: new(.5, .4), CollisionHeight: 2.15,
        CounterOffset: 1.25, Price: 5, Healing: .20);
}

public readonly record struct FoodSite(int Id, CityPoint Position, bool Shawarma)
{
    internal FoodFixture Fixture => Shawarma ? FoodFixture.Kiosk : FoodFixture.VendingMachine;
    internal const double PurchaseRadius = 1.35;
    public CityPoint Interaction => Position + new CityPoint(0, Fixture.CounterOffset);
    public int Price => Fixture.Price;
    public double Healing => Fixture.Healing;
}
