namespace Promzona.Game;

public sealed record CityWeapon(CityWeaponId Id, int Power, string English, string Russian, int PickupAmmo, double Interval, double Range, double CarDamage, double PersonDamage, int NpcBurst, double NpcInterval, double NpcRest, double NpcCarDamage, string Sound);
