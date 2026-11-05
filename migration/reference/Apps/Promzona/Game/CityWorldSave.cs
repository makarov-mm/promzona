namespace Promzona.Game;

public sealed class CityWorldSave
{
    public bool WideRoads;
    public int RoadRevision;
    public int TransitRevision;
    public int ScooterRevision;
    public List<ActorSave> Scooters=[];
    public ActorSave? Player;
    public List<TransitRider> TransitRiders=[];
    public Dictionary<string, object?> Clock = [];
    public Dictionary<string, object?> Weather = [];
    public List<ActorSave> Cars = [], People = [], Animals = [], Props = [];
    public List<CityMark> Marks = [];
    public List<CashDrop> Cash = [];
    public List<CityPickup>? Pickups;
    public Dictionary<CityWeaponId, int>? Ammunition;
    public CityWeaponId? SelectedWeapon;
    public List<ServiceMission> Services = [];
    public List<CityGroup> Groups = [];
    public List<CityIncident> Incidents = [];
    public int[] Reservations = [], PedPlaces = [];
}
