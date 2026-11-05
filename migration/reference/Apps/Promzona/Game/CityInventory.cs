namespace Promzona.Game;

public sealed class CityInventory
{
    public const int AmmoLimit = 9999;
    private readonly Dictionary<CityWeaponId, int> _ammo = [];
    public int Ammo(CityWeaponId id) => _ammo.GetValueOrDefault(id);
    // Zero rounds still means the weapon is owned, including after save/load.
    public bool Owns(CityWeaponId id) => _ammo.ContainsKey(id);
    public bool HasWeapons => _ammo.Count > 0;

    public CityWeaponId? Selected { get; private set; }

    public bool Select(CityWeaponId id)
    {
        if (id != CityWeaponId.None && Ammo(id) <= 0) return false;
        Selected = id;
        return true;
    }

    public void Cycle(int direction)
    {
        int count = CityWeapons.All.Count + 1;
        int start = (int)(Active?.Id ?? CityWeaponId.None);
        for (int n = 1; n <= count; n++)
            if (Select((CityWeaponId)((start + count + n*Math.Sign(direction)) % count))) break;
    }

    public CityWeapon? Active
    {
        get
        {
            if (Selected is { } chosen && (chosen == CityWeaponId.None || Ammo(chosen) > 0))
                return CityWeapons.Find(chosen);
            CityWeapon? best = null;

            for (int i=0; i<CityWeapons.All.Count; i++)
            {
                var weapon=CityWeapons.All[i];
                if (Ammo(weapon.Id) > 0 && (best is null || weapon.Power > best.Power))
                    best = weapon;
            }

            return best;
        }
    }

    public bool Give(CityWeaponId id, int? amount=null)
    {
        CityWeapon? w = CityWeapons.Find(id);
        if (w is null || amount is <=0 or >AmmoLimit || Ammo(id) >= AmmoLimit) return false;
        if (Selected.HasValue && w.Power > (Active?.Power ?? 0)) Selected = null;
        _ammo[id] = Math.Min(AmmoLimit, Ammo(id) + (amount ?? w.PickupAmmo));
        return true;
    }

    public bool Consume(CityWeaponId id)
    {
        if (CityWeapons.Find(id) is null || Ammo(id) <= 0) return false;
        if (--_ammo[id] == 0 && Selected == id) Selected = null;
        return true;
    }

    public void Clear() { _ammo.Clear(); Selected = null; }
    public Dictionary<CityWeaponId, int> Capture() => new(_ammo);

    public void Restore(Dictionary<CityWeaponId, int> state)
    {
        Validate(state);
        Clear();

        foreach (KeyValuePair<CityWeaponId, int> e in state)
        {
            _ammo[e.Key] = e.Value;
        }
    }

    public static void Validate(Dictionary<CityWeaponId, int> state)
    {
        if (state.Count > CityWeapons.All.Count 
            ||
            state.Any(e => CityWeapons.Find(e.Key) is null || e.Value < 0 || e.Value > AmmoLimit))
        {
            throw new InvalidDataException("Invalid weapon inventory");
        }
    }
}
