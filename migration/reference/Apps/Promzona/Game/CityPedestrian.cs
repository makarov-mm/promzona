using MakarovPhysics.RigidBodies2D;

namespace Promzona.Game;

public sealed class CityPedestrian
{
    internal CityTraffic.FarRoutineState FarRoutine;
    public bool Courier;
    public int ScooterId, OwnedScooterId, Deliveries;
    public double DeliveryDistance, DeliveryWait;

    public double BurnUntil;
    public bool BurnPlayerCaused;

    public int WaitingTransitRoute,WaitingTransitStop; public double TransitWaitUntil,TransitRetryAt;

    public int Id {get;internal set;}
    public CityPoint Position {get;internal set;}
    public double Heading {get;internal set;}
    public double Speed {get;internal set;}
    public double Distance {get;internal set;}
    public bool Crossing {get;internal set;}
    public bool IsPlayer;public CityWeaponId PlayerWeapon;
    public int RoadLevel;
    public const double Radius=.30,Mass=75;
    public Body2D Body {get;internal set;}=null!;
    public double FlightHeight {get;internal set;}
    public double VerticalSpeed {get;internal set;}
    public double KnockedFor {get;internal set;}
    public bool Dead {get;internal set;}
    public double DeathTime {get;internal set;}
    public bool Physical=true;
    public double Height=>1.60+(Id%11)*.022;
    public double ShoulderWidth=>.40+(Id%7)*.018;
    public int ServiceId;
    public bool Officer=>Persona is 5 or 6 or 12 or 13;
    public bool Drowned;
    public double RoleClock; public CityPoint RoleHome;
    public bool WeaponReleased;public int BurstRemaining;public double GunDamage;
    public bool Armed=>IsPlayer?PlayerWeapon!=CityWeaponId.None:!Courier&&!WeaponReleased&&(Officer||ServiceId==0&&(Persona==3?Id%4!=0:Persona is 7 or 8 or 9 or 10 or 11?false:Id%10==0));
    public CityWeaponId Weapon=>IsPlayer?PlayerWeapon:!Armed?CityWeaponId.None:(Persona is 6 or 12 or 13||(Id/10)%4==1?CityWeaponId.Automatic:CityWeaponId.Pistol);
    public double AggressionUntil,ShotAt,AimTime;public bool Aiming;
    public int VoiceRevision;
    public bool CanSpeak=>!IsPlayer&&!Dead&&KnockedFor<=0&&!Detached;
    public bool Detached; public double Fear,InterestUntil,VoiceAt; public CityPoint FearFrom;
    public int GroupId=-1,InterestId=-1,Persona;
    // Jobs (29.10.2026): a hailer waits at a bus stop with an arm up until
    // HailUntil; Injured counts down the limping time of a survived car hit;
    // Boarding names the car whose door the person is walking to.
    public bool Hailing; public double HailUntil,Injured; public int Boarding=-1;
    internal double BloodClock;
    internal bool BloodPool;
    internal CityPath? Path;
    internal int I,J,Sx,Sz,NextI,NextJ,NextSx,NextSz,Last=-1;
    internal double Along,Wait,Cruise,WalkStall;
    internal CityPoint LastWalkPosition,NavigationLast;
    internal double NavigationStall,NextNavigation,NavigationClock;
    internal bool Exiting;
    internal int OriginKey=-1,DestinationKey=-1;
    // The actor-grid cell this person is listed in (a tuple: not a saved field).
    internal (int X, int Z) GridCell = (int.MinValue, int.MinValue);
}