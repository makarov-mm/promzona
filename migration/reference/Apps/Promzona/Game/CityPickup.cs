namespace Promzona.Game;

public sealed class CityPickup
{
    public int RoadLevel;
    public int Id;
    public CityPickupKind Kind;
    public CityWeaponId Weapon;
    public CityPoint Position;
    public double Created;
    public bool MapSpawn;
    public CityPickup Copy() => (CityPickup)MemberwiseClone();
}
