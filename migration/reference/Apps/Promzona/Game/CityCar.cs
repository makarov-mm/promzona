using MakarovPhysics.RigidBodies2D;

namespace Promzona.Game;

public sealed class CityCar
{
    public int Id { get; internal set; }
    public bool Hero { get; internal set; }
    public bool Physical = true;
    public bool Driverless;
    public int OwnedGarageId;
    public bool PoliceVehicle => Kind is 3 or 6 or 7 or 13 or 14;
    public int Occupants = -1;

    public int OccupantCount => Driverless ? 0 :
        Occupants >= 0 ? Occupants :
        PoliceVehicle ? (Kind is 7 or 14 ? 4 : 2) :
        Kind == 4 ? 2 : 1 + Id % 3;

    public double DoorOpen, DoorHold;
    public int DoorSide = 1;
    public bool CrewDeployed;
    public bool AirPatrol, AirDeparting;
    internal double AirSurveyAt, AirSafeHeight, AirShotAt;
    public int AirShots;
    public CityPoint Position { get; internal set; }
    public double Heading { get; internal set; }
    public double Speed { get; internal set; }
    public double Distance { get; internal set; }
    public double Steering { get; internal set; }
    public bool Braking { get; internal set; }
    public int Turns { get; internal set; }
    public double Cruise { get; internal set; }
    public int Kind { get; internal set; }
    public int TransitRoute;
    public double TransitAlong, TransitStopUntil, TransitNextStop;
    public int LaneIndex;
    public double LaneWait, NextLaneDecision, BusUntil, NextBusStop;

    public double HalfLength => CityFleet.Get(this) is { } fleet ? fleet.Length * .5 :
        Kind == 8 ? 2.55 :
        Kind == 7 ? 3.5 :
        Kind == 6 ? 2.25 :
        Kind == 5 ? 4.6 :
        Kind == 2 ? 3.05 :
        Kind == 4 ? 2.55 :
        Kind == 1 ? 2.15 : 2.05;

    public double HalfWidth => CityFleet.Get(this) is { } fleet ? fleet.Width * .5 :
        Kind == 8 ? 1.04 :
        Kind == 7 ? 1.16 :
        Kind == 6 ? 1.0 :
        Kind == 5 ? 1.16 :
        Kind == 2 ? 1.08 :
        Kind == 4 ? 1.04 :
        Kind == 1 ? 1.0 : .94;

    public double Height => CityFleet.Get(this) is { } fleet ? fleet.Height :
        Kind == 8 ? 2.65 :
        Kind == 7 ? 3.2 :
        Kind == 6 ? 2.2 :
        Kind == 5 ? 3.15 :
        Kind == 2 ? 2.9 :
        Kind == 4 ? 2.65 :
        Kind == 3 ? 1.64 :
        Kind == 1 ? 2.05 :
        Id != 1 && Id % 3 != 0 && Id % 2 != 0 ? 1.70 : 1.4;

    public double Mass => CityFleet.Get(this) is { } fleet
        ? fleet.Mass
        : Kind switch
        {
            8 => 3100, 7 => 7400, 6 => 2450, 5 => 10500, 1 => 2100, 2 => 6500, 3 => 1600, 4 => 3200, _ => 1250
        };

    public double Wheelbase => CityFleet.Get(this) is { } fleet
        ? fleet.Wheelbase
        : Kind switch { 8 => 3.5, 7 => 4.1, 6 => 2.55, 5 => 5.6, 2 => 3.85, 4 => 3.5, 1 => 2.7, _ => 2.45 };

    public Body2D Body { get; internal set; } = null!;
    internal DriveInput Command;
    internal double Stuck, ReverseFor;
    public double SpecialYaw, RotorPhase;
    public double GunElevation;
    internal double NextTurretShot;
    internal int TurretBurst;
    public double Lift, VerticalVelocity, Roll, Pitch, RollVelocity, PitchVelocity, Damage;
    internal bool WreckSupported;
    public double GroundHeight, TerrainPitch, TerrainRoll;
    public int RoadLevel;
    internal bool TerrainActive;
    public double SurfaceHeight => TerrainActive ? GroundHeight : 0;
    public bool Burning, Destroyed, Falling, PanicDriver;
    public int Paint; // 0 factory colour, else a palette slot chosen at a garage
    public double HealthRemaining => Destroyed ? 0 : Math.Clamp(1 - Damage, 0, 1);
    public double FireAge, DeadSince, FallTime, OvertakeOffset, WetTires;
    public double SafeDrivingTime, LastDamageAt = -100, PlayerDamageUntil;
    public double OverturnedTime, RecoveryTime, RecoveryRoll, RecoveryPitch;
    public bool Recovering => RecoveryTime > 0;
    internal double ImpactCooldown, LastVx, LastVz, SkidClock;
    public bool Grounded => Lift < .12 && Math.Cos(Roll) * Math.Cos(Pitch) > .45;
    internal CityPath Path = null!;
    internal double Along;
    internal int I, J, Direction, Outgoing, Phase, Reservation = -1;
    // The actor-grid cell this car is listed in (a tuple: not a saved field).
    internal (int X, int Z) GridCell = (int.MinValue, int.MinValue);
}
