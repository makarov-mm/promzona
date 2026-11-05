namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public bool GodMode;
    public double HelicopterLiftInput;
    public bool HelicopterLanding;
    public void ToggleHelicopterLanding(){if(!OnFoot&&Hero.Kind==12&&!Hero.Recovering)HelicopterLanding=!HelicopterLanding;}
    public void ConsoleWanted(double n){Wanted=Math.Clamp(n,0,MaxWantedLevel);_lastCrime=Time;_policeClock=0;}
    public void ConsoleHeal(){if(Player.Dead)return;Player.GunDamage=0;Player.BurnUntil=0;}
    public void ConsoleRepair(){if(Hero.Destroyed||Hero.Falling)return;Hero.Damage=0;Hero.Burning=false;Hero.FireAge=Hero.SafeDrivingTime=0;}
    public string ConsoleSpawn(string name)
    {
        int kind=name switch{"tank"=>11,"helicopter"=>12,"car"=>0,"ambulance"=>4,"police"=>3,"rosguard"=>7,"fsb"=>13,"btr"=>14,_=>-1};
        if(kind<0&&name is not("pills" or "energy"))return "Unknown item";
        if(kind>=0&&Cars.Count>=CarCap+MaxPoliceUnits+2)return "Vehicle population limit reached";
        if(kind<0&&Pickups.Count>=PickupCap)return "Pickup limit reached";
        // Search near the player, validating the whole footprint, not just its centre.
        for(double radius=kind<0?3:9;radius<=45;radius+=3)for(int n=0;n<24;n++){
            double angle=n*Math.Tau/24;var at=PlayerPosition+new CityPoint(Math.Sin(angle),Math.Cos(angle))*radius;
            if(Math.Abs(at.X)>LandEdge-12||Math.Abs(at.Z)>LandEdge-12||!ClearWalk(at)||CityWater.Depth(at)>.1)continue;
            if(kind<0){Pickups.Add(new(){Id=++_pickupId,Kind=name=="pills"?CityPickupKind.Pills:CityPickupKind.EnergyDrink,Position=at,Created=Time-1});return "Spawned "+name;}
            int i=Math.Clamp((int)Math.Round(at.X/Spacing+(Nodes-1)*.5),0,Nodes-1),j=Math.Clamp((int)Math.Round(at.Z/Spacing+(Nodes-1)*.5),0,Nodes-1);
            var c=new CityCar{Id=_nextId+1,Kind=kind,Position=at,Heading=0,I=i,J=j,Driverless=true,Path=new(at,at+new CityPoint(0,2)),Cruise=4};
            bool clear=GroundCarSpawnClear(c)&&CarClear(at,0,c);
            for(double x=-c.HalfWidth-.3;clear&&x<=c.HalfWidth+.3;x+=.5)for(double z=-c.HalfLength-.3;clear&&z<=c.HalfLength+.3;z+=.5)
                if(!ClearWalk(at+new CityPoint(x,z))||CityWater.Depth(at+new CityPoint(x,z))>.1)clear=false;
            if(!clear)continue;
            _nextId++;Cars.Add(c);Register(c);SpawnedCars++;return "Spawned "+name+" — F to enter";
        }
        return "No safe space nearby; move into an open street";
    }
    private CityModel.Obstacle[]? _heliWorld;
    private CityModel.Obstacle[] _heliRoofs=[];
    private CityPoint _heliRoofFocus;
    private static bool HelicopterFootprint(CityCar c,double x,double z,double hx,double hz,double angle)
    {
        double a=-c.Body.Angle,ca=Math.Cos(a),sa=Math.Sin(a),cb=Math.Cos(angle),sb=Math.Sin(angle);
        double dx=c.Body.X-x,dz=c.Body.Y-z;
        // SAT against the full body rectangle on all four horizontal axes.
        foreach(var axis in new[]{new CityPoint(ca,-sa),new CityPoint(sa,ca),new CityPoint(cb,-sb),new CityPoint(sb,cb)}){
            double own=c.HalfWidth*Math.Abs(axis.X*ca-axis.Z*sa)+c.HalfLength*Math.Abs(axis.X*sa+axis.Z*ca);
            double other=hx*Math.Abs(axis.X*cb-axis.Z*sb)+hz*Math.Abs(axis.X*sb+axis.Z*cb);
            if(Math.Abs(dx*axis.X+dz*axis.Z)>own+other)return false;
        }
        return true;
    }
    private void HelicopterContact(CityCar c,double previous)
    {
        if(!_useScenery)return;
        double current=c.SurfaceHeight+c.Lift;
        void Slab(double low,double high){
            if(current<previous&&previous>=high-.03&&current<high+.025){current=high+.025;c.VerticalVelocity=0;}
            else if(current>previous&&previous+c.Height<=low+.03&&current+c.Height>low-.025){current=low-c.Height-.025;c.VerticalVelocity=0;}
        }
        if(!ReferenceEquals(_heliWorld,CityModel.Blocks)||(PlayerPosition-_heliRoofFocus).Length>20){
            _heliWorld=CityModel.Blocks;_heliRoofFocus=PlayerPosition;
            _heliRoofs=_heliWorld.Where(b=>Math.Abs(b.X-_heliRoofFocus.X)<150+b.HalfX&&Math.Abs(b.Z-_heliRoofFocus.Z)<150+b.HalfZ).ToArray();
        }
        // Facades are segmented in the planar solver; the full roof still supports flight.
        foreach(var b in _heliRoofs)if(HelicopterFootprint(c,b.X,b.Z,b.HalfX,b.HalfZ,0)){
            double floor=CityTerrain.Ground(b.X,b.Z);Slab(floor,floor+b.Height);
        }
        foreach(var body in _activeScenery){
            if(!_sceneryHeights.TryGetValue(body,out var h)&&!_levelStatics.TryGetValue(body,out h))continue;
            var e=SceneryExtent(body);
            if(Math.Abs(c.Body.X-body.X)>e.X+c.HalfLength||Math.Abs(c.Body.Y-body.Y)>e.Z+c.HalfLength)continue;
            if(!HelicopterFootprint(c,body.X,body.Y,Math.Max(body.HalfW,body.Radius),Math.Max(body.HalfH,body.Radius),-body.Angle))continue;
            Slab(h.Low,h.High);
        }
        c.Lift=Math.Max(0,current-c.SurfaceHeight);
    }
    private void SpecialDrive(CityCar c,DriveInput raw,double dt)
    {
        if(c.Kind==11){VehicleDynamics.Step(c,raw,dt,Grip);return;}
        if(c.Destroyed||c.Falling||c.Recovering)return;
        var input=raw.Sanitized();var b=c.Body;bool piloted=PlayerControls(c);
        // Assisted arcade helicopter: thrust balances weight; Q/Z changes vertical speed.
        if(piloted&&HelicopterLiftInput>.1)HelicopterLanding=false;
        double vertical=piloted?(HelicopterLanding?-2.2:Math.Clamp(HelicopterLiftInput,-1,1)*5):-2;
        if(piloted&&HelicopterLanding)input=new(0,0,false,true);
        if(c.Lift>65)vertical=Math.Min(vertical,-1);
        c.VerticalVelocity+=(vertical-c.VerticalVelocity)*(1-Math.Exp(-dt*2.6));
        double previous=c.SurfaceHeight+c.Lift;
        c.Lift=Math.Max(0,c.Lift+c.VerticalVelocity*dt);
        HelicopterContact(c,previous);
        c.RotorPhase=(c.RotorPhase+dt*(piloted||c.Lift>.2?28:4))%Math.Tau;
        if(c.Lift<=0&&c.VerticalVelocity<0)c.VerticalVelocity=0;
        double thrust=c.Lift>.25?8:1;
        b.W+=(-input.Steering*.85-b.W)*(1-Math.Exp(-dt*3));
        double h=-b.Angle;b.Vx+=Math.Sin(h)*input.Throttle*thrust*dt;b.Vy+=Math.Cos(h)*input.Throttle*thrust*dt;
        double drag=Math.Exp(-dt*(input.Handbrake?3:.34));b.Vx*=drag;b.Vy*=drag;
        c.Pitch+=(-input.Throttle*.14-c.Pitch)*(1-Math.Exp(-dt*3));
        c.Roll+=(input.Steering*.18-c.Roll)*(1-Math.Exp(-dt*3));c.Braking=input.Handbrake;b.Wake();
    }
}
