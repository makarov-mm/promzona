namespace Promzona.Game;

/// <summary>Possession is independent of the car's identity and appearance.
/// The pedestrian enters the same local contact world as other people only while outside.</summary>
public sealed partial class CityTraffic
{
    public CityPedestrian Player {get;private set;}=null!;
    public bool OnFoot {get;private set;}
    public bool PassengerMode {get;private set;}
    private bool _passengerEntry;
    public CityPoint PlayerPosition=>OnFoot&&Player is not null?Player.Position:Hero.Position;
    public CityPoint PlayerVelocity=>OnFoot?new(Player.Body.Vx,Player.Body.Vy):new(Hero.Body.Vx,Hero.Body.Vy);
    public double PlayerHeading=>OnFoot?Player.Heading:Hero.Heading;
    public double PlayerHealth=>Player.Dead?0:Math.Clamp(1-Player.GunDamage,0,1);
    public double VehicleHealth=>Hero.HealthRemaining;
    public bool PlayerUnavailable=>Player.Dead||(!OnFoot&&(Hero.Destroyed||Hero.Falling||VehicleHealth<=0));
    public CityPoint WalkInput;
    public bool Sprint;
    public double PlayerViewHeading;
    public double PlayerViewPitch;
    public const double EntryReach=5;
    public int EnteringCarId {get;private set;}=-1;
    public double EntryProgress {get;private set;}
    public int CarsTaken {get;private set;}
    public string InteractionMessage {get;private set;}="";
    public double MessageUntil {get;private set;}
    private CityPoint _entryPoint; private int _entrySide;
    private double _interactAt;
    public bool PlayerControls(CityCar c)=>c.Hero&&!OnFoot&&!PassengerMode;
    private void ResetPlayer(){
        OnFoot=false;PassengerMode=_passengerEntry=false;WalkInput=default;Sprint=false;EnteringCarId=-1;EntryProgress=0;CarsTaken=0;_interactAt=0;
        InteractionMessage="";MessageUntil=0;PlayerViewHeading=Hero.Heading;
        Player=new(){Id=++_nextId,IsPlayer=true,Position=Hero.Position,Heading=Hero.Heading,Cruise=3.1,Persona=1};
        Register(Player);Remove(Player.Body);Player.Physical=false;
    }
    public void ClearPlayerInput(){ScooterBrake=false;WalkInput=default;Sprint=false;PlayerFireHeld=false;DroneInput=default;DroneLiftInput=0;DroneScanHeld=false;HelicopterLiftInput=0;}
    private bool Message(string key,double duration=2.5){InteractionMessage=key;MessageUntil=Time+duration;return false;}
    private static CityPoint DoorPoint(CityCar c,int side,double extra=0){
        var f=new CityPoint(Math.Sin(c.Heading),Math.Cos(c.Heading));
        return c.Position+Right(f)*(side*(c.HalfWidth+.85+extra))+f*Math.Min(1,c.HalfLength*.25);
    }
    private bool ClearPlayerPoint(CityPoint at,CityCar? ignore=null,CityPedestrian? ignorePerson=null,int? roadLevel=null){
        int level=roadLevel??ignore?.RoadLevel??Player.RoadLevel; double y=CityRoadLevels.Surface(at.X,at.Z,level);
        if(Math.Abs(at.X)>LandEdge-.8||Math.Abs(at.Z)>LandEdge-.8||(_useScenery&&!CityRoadLevels.ClearAt(at,level)&&!ClearWalk(at)))return false;
        // Actor-grid queries: the door hint asks this for every point of the
        // approach path each frame; the full scans walked every car and person
        // of the island per point. A car reaches a point only within its half
        // length plus the .43 + .25 m disc margin (< 8 m, including transit), a person within .7 m.
        foreach(var c in NearbyCars(at,8))if(c!=ignore&&!c.Falling&&(!_useScenery||Math.Abs(c.SurfaceHeight-y)<2)&&CarPersonOverlap(c.Position,c.Heading,at,.43,c.HalfLength,c.HalfWidth))return false;
        foreach(var p in NearbyPeople(at,1))if(p!=Player&&p!=ignorePerson&&!p.Dead&&(p.Position-at).Length<.7)return false;
        if(_useScenery&&!CityRoadLevels.ClearAt(at,level)&&CityLayout.Current.FurnitureNear(at.X,at.Z,.4))return false;
        foreach(var p in NearbyProps(at,2.5))if((p.Position-at).Length<p.Radius+.4&&p.Kind is not (StreetKind.Bag or StreetKind.Bottle or StreetKind.Can))return false;
        return true;
    }
    private bool DoorClear(CityCar c,CityPoint at){
        if(!ClearPlayerPoint(at,c))return false;
        if(_useScenery&&Math.Abs(CityRoadLevels.Surface(at.X,at.Z,c.RoadLevel)-c.SurfaceHeight)>1.15)return false;
        var d=at-c.Position;
        for(int n=1;n<=12;n++){
            var p=c.Position+d*(n/12d);
            if(_useScenery&&!CityRoadLevels.ClearAt(p,c.RoadLevel)&&!ClearWalk(p))return false;
            if(NearbyCars(p,9).Any(v=>v!=c&&!v.Falling&&CarPersonOverlap(v.Position,v.Heading,p,.32,v.HalfLength,v.HalfWidth)))return false;
        }
        return true;
    }

    public CityCar? NearbyEnterableCar()
    {
        if (!OnFoot || OnScooter || PlayerUnavailable) return null;
        CityCar? best = null;
        double distance = EntryReach;

        foreach (var c in Cars)
        {
            if (_useScenery&&Math.Abs(c.SurfaceHeight-CityRoadLevels.Ground(Player))>1.5 || c.Destroyed || c.Falling || c.Burning || !c.Grounded || c.Recovering ||
                Math.Abs(c.Speed) > 2.5) continue;

            if ((c.Position - Player.Position).Length > c.HalfLength + 4) continue;

            for (int side = -1; side <= 1; side += 2)
            {
                var at = DoorPoint(c, side);
                double d = (at - Player.Position).Length;
                if (d >= distance || !DoorClear(c, at) || !EntryPathClear(Player.Position, at, c)) continue;
                best = c;
                distance = d;
            }
        }

        return best;
    }

    private bool EntryPathClear(CityPoint a,CityPoint b,CityCar c){
        int steps=Math.Max(1,(int)Math.Ceiling((b-a).Length/.2));
        for(int i=1;i<=steps;i++){var p=a+(b-a)*(i/(double)steps);
            if(!ClearPlayerPoint(p,null)||CarPersonOverlap(c.Position,c.Heading,p,.32,c.HalfLength,c.HalfWidth))return false;
        }return true;
    }

    public bool InteractVehicle()
    {
        if(!OnFoot&&Hero.Kind==12&&Hero.Lift>.4)return false;

        if (Time < _interactAt || PlayerUnavailable) return false;
        _interactAt = Time + .35;
        if(OnScooter){DismountScooter(Player);return true;}
        if(OnFoot&&NearbyScooter() is {} scooter)return MountScooter(Player,scooter);
        if(OnFoot&&NearbyFood() is not null)return BuyFood();

        if (EnteringCarId >= 0)
        {
            CancelEntry();
            return true;
        }

        if (!OnFoot)
        {
            var c = Hero;
            if (c.Recovering || c.Falling || Math.Abs(c.Speed) > 2.5) return Message("stop");
            CityPoint? exit = null;
            foreach (int side in new[] { 1, -1 })
            {
                var at = DoorPoint(c, side);
                if (DoorClear(c, at))
                {
                    exit = at;
                    break;
                }
            }

            if (exit is null) exit=EmergencyExit(c);
            if (exit is null) return Message("blocked");
            c.DoorSide=CityPoint.Dot(exit.Value-c.Position,Right(new(Math.Sin(c.Heading),Math.Cos(c.Heading))))>0?1:-1; c.DoorHold=1.2;
            c.Driverless = !PassengerMode;PassengerMode=false;
            c.Command = new(0, 0, true);
            FreePoliceReservation(c);
            OnFoot = true;
            Player.Position = exit.Value;Player.RoadLevel=c.RoadLevel;
            Player.Heading = c.Heading;
            PlayerViewHeading = c.Heading;
            Player.Body.X = exit.Value.X;
            Player.Body.Y = exit.Value.Z;
            Player.Body.Vx = Player.Body.Vy = Player.Body.W = 0;
            Player.Detached = false;
            Player.Physical = true;
            Player.FlightHeight = Player.VerticalSpeed = Player.KnockedFor = 0;
            if (!Pedestrians.Contains(Player)) Pedestrians.Add(Player);
            Attach(Player.Body, true);
            ClearPlayerInput();
            _sceneryClock = _detailClock = 0;
            RebuildActorGrid();
            return true;
        }

        if (Player.KnockedFor > 0 || Player.Detached) return Message("recover");
        var target = NearbyEnterableCar();
        // Nobody's car door nearby: F addresses the person the player faces (29.10.2026).
        if (target is null) return Talk() || Message("near");
        var a = DoorPoint(target, 1);
        var b = DoorPoint(target, -1);
        _entryPoint = DoorClear(target, a) && EntryPathClear(Player.Position, a, target) &&
                      ((a - Player.Position).Length < (b - Player.Position).Length || !DoorClear(target, b) ||
                       !EntryPathClear(Player.Position, b, target))
            ? a
            : b;
        _entrySide=CityPoint.Dot(_entryPoint-target.Position,Right(new(Math.Sin(target.Heading),Math.Cos(target.Heading))))>0?1:-1; target.DoorSide=_entrySide;
        EnteringCarId = target.Id;
        EntryProgress = 0;
        PlayerFireHeld = false;
        return true;
    }

    private void CancelEntry(){EnteringCarId=-1;EntryProgress=0;_passengerEntry=false;}
    private void EntryStep(double dt){
        if(EnteringCarId<0)return;var c=Cars.Find(c=>c.Id==EnteringCarId);
        if(c is not null)_entryPoint=DoorPoint(c,_entrySide);
        if(c is null||c.Destroyed||c.Falling||c.Burning||!c.Grounded||Math.Abs(c.Speed)>3||PlayerUnavailable||Player.KnockedFor>0
            ||(Player.Position-_entryPoint).Length>EntryReach+.75||!DoorClear(c,_entryPoint)){
            CancelEntry();Message("cancel");return;
        }
        // Input can cancel the approach. The actor walks to a clear door, never
        // through a car or a facade; the transfer commits only after it arrives.
        if(WalkInput.Length>.1){CancelEntry();return;}
        if((Player.Position-_entryPoint).Length>.35)return;
        c.DoorHold=.5; EntryProgress=Math.Min(1,EntryProgress+dt/.85);
        if(EntryProgress<1)return;
        var mission=Mission(c);
        // Transit riders are real saved people, not anonymous crew to duplicate.
        // They evacuate through PassengerStep after the driver is displaced.
        int riders=TransitRiders.Count(r=>r.CarId==c.Id),crew=Math.Max(0,c.OccupantCount-riders);
        int occupants=c.Occupants;if(riders>0)c.Occupants=Math.Max(0,crew);
        bool blocked=!_passengerEntry&&!c.Driverless&&DeployCrew(c,null,true)<crew;
        if(riders>0)c.Occupants=occupants;
        if(blocked){CancelEntry();Message("blocked");return;}
        if(mission is not null){ReleaseMedics(mission);Services.Remove(mission);}
        if(!_passengerEntry)c.Occupants=0;c.DoorHold=1.2;
        PassengerMode=_passengerEntry;
        Hero.Hero=false;int index=Cars.IndexOf(c);(Cars[0],Cars[index])=(Cars[index],Cars[0]);c.Hero=true;c.Driverless=!PassengerMode;
        c.Command=default;c.Stuck=c.ReverseFor=0;FreePoliceReservation(c);OnFoot=false;
        Remove(Player.Body);Pedestrians.Remove(Player);Player.Physical=false;Player.Aiming=false;ClearPlayerInput();CancelEntry();if(!PassengerMode)CarsTaken++;
        RebuildActorGrid();_sceneryClock=_detailClock=0;
    }
    private void WalkPlayer(double dt){
        if(OnScooter){RidePlayerScooter(dt);return;}
        var p=Player;var b=p.Body;b.Wake();p.PlayerWeapon=Inventory.Active?.Id??CityWeaponId.None;
        p.Aiming=PlayerFireHeld&&p.PlayerWeapon!=CityWeaponId.None&&EnteringCarId<0;
        CityPoint direction=EnteringCarId>=0?_entryPoint-p.Position:WalkInput;
        double length=direction.Length;double speed=EnteringCarId>=0?Math.Min(2.8,length*6):Sprint?5.6:p.Aiming?2.2:3.1;
        if(EnteringCarId<0&&EnergySeconds>0)speed*=1.65;
        var desired=length>.001?direction*(Math.Min(1,length)*speed/length):default;
        // Do not steer the user along an NPC route. Slide against blocked axes.
        if(_useScenery){
            var next=p.Position+desired*Math.Max(dt,.10);
            if(!CityRoadLevels.ClearAt(next,p.RoadLevel)&&!ClearWalk(next)&&CityWater.Depth(next)<=.65&&Math.Abs(next.X)<LandEdge-3&&Math.Abs(next.Z)<LandEdge-3){
                if(ClearWalk(new(next.X,p.Position.Z)))desired=new(desired.X,0);
                else if(ClearWalk(new(p.Position.X,next.Z)))desired=new(0,desired.Z);else desired=default;
            }
        }
        double mix=1-Math.Exp(-dt*14);b.Vx+=(desired.X-b.Vx)*mix;b.Vy+=(desired.Z-b.Vy)*mix;
        if(p.Aiming||PunchPhase>0||WalkInput.Length<.05)p.Heading=PlayerViewHeading;
    }
    public void DamagePlayer(double amount){
        if(GodMode||!OnFoot||Player.Dead||!double.IsFinite(amount)||amount<=0)return;
        Player.GunDamage=Math.Min(1,Player.GunDamage+amount/5);Sound("impact",Player.Position,.5);
        if(Player.GunDamage>=1)FatalPerson(Player,false);
    }

    private void PlayerLifeStep()
    {
        if (!OnFoot&&!Player.Dead) return;
        if (!Player.Dead && !CityLayout.Rivers && (Math.Abs(Player.Position.X) > LandEdge || Math.Abs(Player.Position.Z) > LandEdge))
        {
            Sound("splash", Player.Position, .8);
            FatalPerson(Player, false);
        }

        if (Player.Dead && Time - Player.DeathTime > 3)
        {
            if(!PlaceAtClinic())return;
            Respawns++;
            Sound("respawn", Player.Position, .5);
        }
    }
}
