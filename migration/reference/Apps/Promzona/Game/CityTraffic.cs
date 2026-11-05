namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public static int Nodes=>CityLayout.GridSize;
    public const double StopLine=11;
    public const double Spacing=36,Lane=1.9;
    public static double RoadHalf=>CityLayout.WideRoads?7.3:4.8;
    public static double Sidewalk=>CityLayout.WideRoads?9.1:6.5;
    public static double EdgeHigh=>(Nodes-1)*Spacing*.5+60;
    public static double EdgeLow=>-EdgeHigh;
    public static double LandEdge=>EdgeHigh-6;
    public double SpawnDistance=245;
    public List<CityCar> Cars {get;}=[];
    public List<CityPedestrian> Pedestrians {get;}=[];
    public CityCar Hero=>Cars[0];
    public double Time {get;private set;}
    public int SpawnedCars {get;private set;}
    public int ExitedCars {get;private set;}
    public int SpawnedPedestrians {get;private set;}
    public int ExitedPedestrians {get;private set;}
    public int SafetyStops {get;private set;}
    private uint _random=73921;
    private int _nextId;
    private double _spawn;
    private readonly int[] _reservations=new int[Nodes*Nodes];
    private readonly int[] _pedPlaces=new int[Nodes*Nodes*4];
    // Half-open blocks extend beyond the urban grid at the coast. Do not clamp here.
    internal static int BlockIndex(double position) => (int)Math.Floor((position - Coordinate(0)) / Spacing);
    internal static double BlockCentre(int index) => Coordinate(index) + Spacing / 2;
    public static double Coordinate(int i)=>-(Nodes-1)*Spacing*.5+i*Spacing;
    internal static bool InBlock(CityPoint point, double minX, double minZ) =>
        point.X >= minX && point.X < minX + Spacing && point.Z >= minZ && point.Z < minZ + Spacing;
    public static CityPoint Forward(int direction)=>(direction%4) switch{0=>new(0,1),1=>new(1,0),2=>new(0,-1),_=>new(-1,0)};
    public static CityPoint Right(CityPoint f)=>new(-f.Z,f.X);
    private double Random(){_random^=_random<<13;_random^=_random>>17;_random^=_random<<5;return (_random+.5)/4294967296d;}
    private int Pick(int n)=>(int)(Random()*n);
    public const double SignalCycle=28;
    public static double SignalPhase(double time,int i,int j)=>(time+(i*3+j*5)%9)%SignalCycle;
    public static bool HasRoad(int i,int j,int direction)=>CityLayout.Current.Road(i,j,direction);
    public static int Degree(int i,int j){int n=0;for(int d=0;d<4;d++)if(HasRoad(i,j,d))n++;return n;}
    public static bool Signalised(int i,int j)=>Degree(i,j)>=3;
    public static bool Amber(double time,int i,int j,int direction){double p=SignalPhase(time,i,j);return direction%2==0?p>=26&&p<28:p>=12&&p<14;}
    public bool Green(int i,int j,int direction){double p=SignalPhase(Time,i,j);return !Signalised(i,j)||(direction%2==0?p>=14&&p<26:p<12);}
    public bool Walk(int i,int j,double duration,int axis=0){double p=SignalPhase(Time,i,j);return !Signalised(i,j)||(axis==1?p<12&&p+duration<13.7:p>=14&&p+duration<27.7);}
    private readonly bool _useScenery;
    private readonly Action<double,string>? _progress;
    private readonly CancellationToken _cancel;
    public double Grip=1;
    public CityTraffic(bool scenery=true,Action<double,string>? progress=null,CancellationToken cancel=default){_useScenery=scenery;_progress=progress;_cancel=cancel;Reset();}
    public void Reset()
    {
        OnFoot=false;_foodSites=null;_busStops=null;_garages=null;_markets=null;_walkDetours.Clear();_clearanceChecks.Clear();World.Clear();_cancel.ThrowIfCancellationRequested();AddScenery();_progress?.Invoke(.55,"Building collision world");ResetStreet();Cars.Clear();Pedestrians.Clear();Array.Fill(_reservations,-1);Array.Fill(_pedPlaces,-1);Time=_spawn=_flockClock=0;_random=(uint)CityLayout.Current.Seed|1;_nextId=0;SpawnedCars=ExitedCars=SpawnedPedestrians=ExitedPedestrians=SafetyStops=0;
        var hero=MakeCar(Nodes/2,Nodes/2,0,28,true);Cars.Add(hero);
        for(int tries=0;Cars.Count<1080&&tries<40000;tries++){int i=Pick(Nodes),j=Pick(Nodes),d=Pick(4);if(!HasRoad(i,j,(d+2)%4))continue;var c=MakeCar(i,j,d,12+Random()*15,false);if(GroundCarSpawnClear(c)&&(c.Position-PlayerPosition).Length>22&&CarClear(c.Position,c.Heading,c))Cars.Add(c);}
        for(int tries=0;Pedestrians.Count<3240&&tries<60000;tries++){int i=Pick(Nodes),j=Pick(Nodes),sx=Pick(2)*2-1,sz=Pick(2)*2-1;int key=PedKey(i,j,sx,sz);if(_pedPlaces[key]>=0||(_useScenery&&!ClearWalk(Corner(i,j,sx,sz))))continue;var person=MakePerson(i,j,sx,sz,Corner(i,j,sx,sz));person.OriginKey=key;_pedPlaces[key]=person.Id;Pedestrians.Add(person);}
        _cancel.ThrowIfCancellationRequested();_progress?.Invoke(.65,"Creating traffic");
        foreach(var car in Cars)Register(car);
        foreach(var person in Pedestrians){Register(person);ChooseWalk(person);}
        // The actor grid must exist before the first grid-pruned query (ResetRoles
        // places beach visitors through ClearPlayerPoint); Step rebuilds it per substep.
        _cancel.ThrowIfCancellationRequested();ResetLife();ResetSocial();ExpandPopulation();SeedLevelTraffic();ResetPickups();ResetPlayer();ResetPassengers();ResetJobs();ResetMarket();ResetTalk();RebuildActorGrid();ResetRoles();ResetScooters();EnsureTransit(true);_transitClock=8;_recoveryAt=0;_controlClock=_sceneryClock=_detailClock=0;RefreshDetail();RefreshScenery();_progress?.Invoke(.82,"Preparing the view");
    }
    private CityCar MakeCar(int i,int j,int direction,double before,bool hero)
    {
        var f=Forward(direction);var centre=new CityPoint(Coordinate(i),Coordinate(j));var start=centre-f*before+Right(f)*Lane;
        var car=new CityCar{Id=++_nextId,Hero=hero,I=i,J=j,Direction=direction,Outgoing=direction,Position=start,Heading=Math.Atan2(f.X,f.Z),Kind=hero?0:Pick(16) switch{0=>3,1=>4,2 or 3=>2,4 or 5=>1,6=>5,7=>8,_=>0},Cruise=hero?4:4+Random()*1.8,Path=new(start,centre-f*StopLine+Right(f)*Lane)};
        if(CityLayout.WideRoads&&!hero){car.LaneIndex=car.Kind==5?1:Pick(2);double shift=LanePosition(car)-Lane;car.Position+=Right(f)*shift;car.Path=new(car.Position,centre-f*StopLine+Right(f)*LanePosition(car));}
        ChooseTurn(car);return car;
    }
    private CityPedestrian MakePerson(int i,int j,int sx,int sz,CityPoint start)=>new(){Id=++_nextId,I=i,J=j,Sx=sx,Sz=sz,Position=start,Cruise=1.3+Random()*.4,Wait=Random()*2,Persona=Pick(18) switch{0=>8,1=>9,2=>10,3 or 4=>2,_=>Pick(3)==0?1:0},VoiceAt=Time+5+Random()*40};
    private static CityPoint Corner(int i,int j,int sx,int sz)=>new(Coordinate(i)+sx*Sidewalk,Coordinate(j)+sz*Sidewalk);
    private static int Key(int i,int j)=>i+Nodes*j;
    private bool InMap(int i,int j)=>i>=0&&i<Nodes&&j>=0&&j<Nodes;
    private (int I,int J) Next(int i,int j,int d){var f=Forward(d);return(i+(int)f.X,j+(int)f.Z);}
    private void ChooseTurn(CityCar car)
    {
        if(ServiceTurn(car))return;
        var candidates=new List<int>();for(int d=0;d<4;d++){if(d==(car.Direction+2)%4)continue;var n=Next(car.I,car.J,d);if(HasRoad(car.I,car.J,d)&&(!car.Hero||InMap(n.I,n.J)))candidates.Add(d);}
        car.Outgoing=candidates.Count>0?candidates[Pick(candidates.Count)]:(car.Direction+2)%4;
        foreach(var level in CityLayout.Current.Levels)if(car.J==level.J){
            int toward=car.I==level.I-4?1:car.I==level.I+4?3:-1;
            if(candidates.Contains(toward)&&Random()<.72){car.Outgoing=toward;break;}
        }
    }
    private bool Enter(CityCar car)
    {
        int key=Key(car.I,car.J);if(_reservations[key]>=0&&_reservations[key]!=car.Id)return false;
        if(!IsResponding(car)&&!Green(car.I,car.J,car.Direction))return false;
        var centre=new CityPoint(Coordinate(car.I),Coordinate(car.J));
        var route=CrossingPath(car);
        // Wait before the crossing if the exit cannot hold the whole vehicle.
        // The stop line also leaves clearance for the longest truck ahead of pedestrians.
        var exit=route.Sample(route.Length);
        if(!CarClearNear(exit.Point,exit.Heading,car))return false;
        var departure=Forward(car.Outgoing);
        var tailClear=centre+departure*(Sidewalk+car.HalfLength+1.3)+Right(departure)*LanePosition(car);
        if(!CarClearNear(tailClear,Math.Atan2(departure.X,departure.Z),car))return false;
        foreach(var p in NearbyPeople(centre,10))if(p.Crossing&&p.Path is not null&&Math.Abs(p.Position.X-centre.X)<10&&Math.Abs(p.Position.Z-centre.Z)<10)
            if(PathsConflict(route,0,car.HalfLength,car.HalfWidth,p.Path,p.Along))return false;
        return true;
    }
    private static bool PathsConflict(CityPath vehicle,double from,double length,double width,CityPath walk,double walkingFrom)
    {
        for(double t=from;t<=vehicle.Length+.7;t+=.7){var pose=vehicle.Sample(t);
            for(double u=walkingFrom;u<=walk.Length+.6;u+=.6)if(CarPersonOverlap(pose.Point,pose.Heading,walk.Sample(u).Point,.55,length,width))return true;
        }
        return false;
    }
    private CityPath CrossingPath(CityCar car)
    {
        CityPoint f=Forward(car.Direction),to=Forward(car.Outgoing),centre=new(Coordinate(car.I),Coordinate(car.J));
        if(car.Outgoing==(car.Direction+2)%4)return CityPath.Turnaround(centre,f,LanePosition(car));
        CityPoint start=centre-f*StopLine+Right(f)*LanePosition(car),end=centre+to*9+Right(to)*LanePosition(car),exitDirection=to;
        if(DepartureLevel(car) is {} road){
            bool east=car.Outgoing==1;end=new(end.X,road.CentreZ(end.X)+(east?Lane:-Lane));
            double slope=(road.CentreZ(end.X+.1)-road.CentreZ(end.X-.1))/.2;
            exitDirection=new CityPoint(east?1:-1,(east?1:-1)*slope)*(1/Math.Sqrt(1+slope*slope));
        }
        double handles=car.Direction==car.Outgoing?6:car.Outgoing==(car.Direction+3)%4?(9-Lane)*.82:(9+Lane)*.55228475;
        return new(start,start+f*handles,end-exitDirection*handles,end);
    }
    private void Cross(CityCar car)
    {
        int reservation=Key(car.I,car.J);if(Mission(car) is not {Kind:CityService.Police}){_reservations[reservation]=car.Id;car.Reservation=reservation;}
        car.Path=CrossingPath(car);car.Along=0;car.Phase=1;if(car.Direction!=car.Outgoing)car.Turns++;
    }
    private void Continue(CityCar car)
    {
        if(TryLevelRoute(car))return;
        car.Direction=car.Outgoing;CityPoint f=Forward(car.Direction);var next=Next(car.I,car.J,car.Direction);car.I=next.I;car.J=next.J;
        CityPoint end=new(Coordinate(car.I),Coordinate(car.J));end=end-f*StopLine+Right(f)*LanePosition(car);
        car.Path=new(car.Position,end);car.Along=0;car.Phase=InMap(car.I,car.J)?0:2;if(car.Phase==0)ChooseTurn(car);
    }
    public static bool CarsOverlap(CityPoint pa,double ha,CityPoint pb,double hb,double margin=0,double lengthA=1.78,double widthA=.94,double lengthB=1.78,double widthB=.94)
    {
        var fa=new CityPoint(Math.Sin(ha),Math.Cos(ha));var fb=new CityPoint(Math.Sin(hb),Math.Cos(hb));var ra=Right(fa);var rb=Right(fb);var delta=pb-pa;
        ReadOnlySpan<CityPoint> axes=stackalloc CityPoint[]{fa,ra,fb,rb};
        foreach(var axis in axes){
            double aa=(lengthA+margin)*Math.Abs(CityPoint.Dot(fa,axis))+(widthA+margin)*Math.Abs(CityPoint.Dot(ra,axis));
            double bb=(lengthB+margin)*Math.Abs(CityPoint.Dot(fb,axis))+(widthB+margin)*Math.Abs(CityPoint.Dot(rb,axis));
            if(Math.Abs(CityPoint.Dot(delta,axis))>=aa+bb)return false;
        }return true;
    }
    public static bool CarPersonOverlap(CityPoint car,double heading,CityPoint person,double margin=.1,double length=1.78,double width=.94)
    {
        var f=new CityPoint(Math.Sin(heading),Math.Cos(heading));var d=person-car;
        double x=Math.Max(0,Math.Abs(CityPoint.Dot(d,Right(f)))-width),z=Math.Max(0,Math.Abs(CityPoint.Dot(d,f))-length);
        return x*x+z*z<Math.Pow(.25+margin,2);
    }
    private bool CarClear(CityPoint position,double heading,CityCar car)
    {
        foreach(var other in Cars)if(other!=car&&(other.Position-position).Length<car.HalfLength+7&&SameHeight(car,other)&&CarsOverlap(position,heading,other.Position,other.Heading,.15,car.HalfLength,car.HalfWidth,other.HalfLength,other.HalfWidth))return false;
        foreach(var p in Pedestrians)if((p.Position-position).Length<car.HalfLength+1.5&&SameHeight(car,p)&&CarPersonOverlap(position,heading,p.Position,.3,car.HalfLength,car.HalfWidth))return false;
        return true;
    }
    // The same predicate pruned by the actor grid. Valid once RebuildActorGrid has
    // run for the current actor set (every physics substep); the initial city
    // build and the spawners keep the full scan, where the grid may be stale.
    // Every driver at a stop line asked CarClear twice per control tick, and
    // each call walked all 2 200 cars and 6 000 people of the island.
    private bool CarClearNear(CityPoint position,double heading,CityCar car)
    {
        foreach(var other in NearbyCars(position,car.HalfLength+7))if(other!=car&&(other.Position-position).Length<car.HalfLength+7&&SameHeight(car,other)&&CarsOverlap(position,heading,other.Position,other.Heading,.15,car.HalfLength,car.HalfWidth,other.HalfLength,other.HalfWidth))return false;
        foreach(var p in NearbyPeople(position,car.HalfLength+1.5))if((p.Position-position).Length<car.HalfLength+1.5&&SameHeight(car,p)&&CarPersonOverlap(position,heading,p.Position,.3,car.HalfLength,car.HalfWidth))return false;
        return true;
    }
    public bool CollisionFree()
    {
        for(int a=0;a<Cars.Count;a++){for(int b=a+1;b<Cars.Count;b++)if(CarsOverlap(Cars[a].Position,Cars[a].Heading,Cars[b].Position,Cars[b].Heading,0,Cars[a].HalfLength,Cars[a].HalfWidth,Cars[b].HalfLength,Cars[b].HalfWidth))return false;foreach(var p in Pedestrians)if(CarPersonOverlap(Cars[a].Position,Cars[a].Heading,p.Position,0,Cars[a].HalfLength,Cars[a].HalfWidth))return false;}
        return true;
    }
    private void SpawnCar(bool? passenger=null)
    {
        for(int attempt=0;attempt<12;attempt++){
            int i=1+Pick(Nodes-2),j=1+Pick(Nodes-2),d=Pick(4);if(!HasRoad(i,j,(d+2)%4))continue;
            var car=MakeCar(i,j,d,15+Random()*12,false);
            if(passenger is true)car.Kind=0;else if(passenger is false&&car.Kind==0)continue;
            if(!GroundCarSpawnClear(car)||(car.Position-PlayerPosition).Length<SpawnDistance||!CarClear(car.Position,car.Heading,car))continue;
            Cars.Add(car);Register(car);SpawnedCars++;break;
        }
    }
    private static int PedKey(int i,int j,int sx,int sz)=>(i+Nodes*j)*4+(sx>0?1:0)+(sz>0?2:0);
    private void SpawnPerson(){if(TrySidewalkPerson(true))SpawnedPedestrians++;}
    // Additional citizens start along pavements, not only at the finite corner
    // reservation slots. They share pavement space using local soft separation.
    private bool TrySidewalkPerson(bool remote){
        for(int attempt=0;attempt<16;attempt++){
            int i=1+Pick(Nodes-2),j=1+Pick(Nodes-2),sx=Pick(2)*2-1,sz=Pick(2)*2-1;
            var start=Corner(i,j,sx,sz);double offset=2+Random()*13;
            start+=Pick(2)==0?new CityPoint(sx*offset,0):new CityPoint(0,sz*offset);
            if(!ClearWalk(start)||(remote&&(start-PlayerPosition).Length<SpawnDistance))continue;
            if(Pedestrians.Any(p=>!p.Dead&&(p.Position-start).Length<1.05))continue;
            if(Cars.Any(c=>CarPersonOverlap(c.Position,c.Heading,start,.45,c.HalfLength,c.HalfWidth)))continue;
            var p=MakePerson(i,j,sx,sz,start);Pedestrians.Add(p);Register(p);ChooseWalk(p);return true;
        }return false;
    }
    private void ChooseWalk(CityPedestrian p)
    {
        // Pavements are continuous shared space, not one exclusive slot per corner.
        // Local separation handles the crowd; corner reservations deadlocked dense cities.
        var options=new List<int>();
        for(int choice=0;choice<4;choice++){
            int ni=p.I,nj=p.J,sx=p.Sx,sz=p.Sz;
            if(choice==0)sx=-sx;else if(choice==1)sz=-sz;else if(choice==2){ni+=sx;sx=-sx;}else{nj+=sz;sz=-sz;}
            if(!InMap(ni,nj)||choice==2&&!CityLayout.AllowedEdge(p.I,p.J,p.Sx>0?1:3))continue;
            if(_useScenery&&!CityRoadLevels.GroundWalkEdge(p.Position,Corner(ni,nj,sx,sz)))continue;

            if(choice<2){
                if(!Walk(p.I,p.J,Math.Min(4,(Corner(ni,nj,sx,sz)-p.Position).Length/p.Cruise+1),choice==0?1:0))continue;
                var crossing=new CityPath(p.Position,Corner(ni,nj,sx,sz));
                if(NearbyCars(new(Coordinate(p.I),Coordinate(p.J)),40).Any(c=>c.Reservation==Key(p.I,p.J)&&PathsConflict(c.Path,c.Along,c.HalfLength,c.HalfWidth,crossing,0)))continue;
            }
            options.Add(choice);
        }
        if(options.Count==0){p.Wait=.15+Random()*.3;return;}
        if(options.Count>1)options.Remove(p.Last);
        int selected=options[Pick(options.Count)],nextI=p.I,nextJ=p.J,nextSx=p.Sx,nextSz=p.Sz;
        if(selected==0)nextSx=-nextSx;else if(selected==1)nextSz=-nextSz;else if(selected==2){nextI+=nextSx;nextSx=-nextSx;}else{nextJ+=nextSz;nextSz=-nextSz;}
        CityPoint end=Corner(nextI,nextJ,nextSx,nextSz),delta=end-p.Position;
        var right=Right(delta*(1/Math.Max(.001,delta.Length)))*.35;
        p.Path=new(p.Position,p.Position+delta*(1d/3)+right,p.Position+delta*(2d/3)+right,end);p.Along=0;p.Crossing=selected<2;
        p.NextI=nextI;p.NextJ=nextJ;p.NextSx=nextSx;p.NextSz=nextSz;p.Exiting=!InMap(nextI,nextJ);p.Last=selected;
        p.DestinationKey=-1;
    }
}
