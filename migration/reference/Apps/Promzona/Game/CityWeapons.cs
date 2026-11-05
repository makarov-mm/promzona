namespace Promzona.Game;

public static class CityWeapons
{
    public static IReadOnlyList<CityWeapon> All { get; } = Array.AsReadOnly([
        new CityWeapon(CityWeaponId.Pistol, 1, "PISTOL", "ПИСТОЛЕТ", 30, .34, 45, .16, .60, 1, .34, .85, .04, "shot"),
        new CityWeapon(CityWeaponId.Automatic, 2, "AUTOMATIC", "АВТОМАТ", 30, .12, 60, .12, .38, 3, .12, 1.15, .028, "automatic"),
        new CityWeapon(CityWeaponId.Bazooka, 4, "BAZOOKA", "БАЗУКА", 20, 1.15, 110, 2.8, 1, 0, 0, 0, 0, "rocket"),
        new CityWeapon(CityWeaponId.Flamethrower, 3, "FLAMETHROWER", "ОГНЕМЁТ", 120, .1, 8, .1, .1, 0, 0, 0, 0, "fire")
    ]);

    public static CityWeapon? Find(CityWeaponId id)
    {
        int index = (int)id - 1;
        return (uint)index < (uint)All.Count && All[index].Id == id ? All[index] : null;
    }
}
