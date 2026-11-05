using MakarovPhysics.RigidBodies2D;
namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public CityContactWorld World {get;}=new(){GravityY=0};
    private double _controlClock,_sceneryClock;
    private readonly List<Body2D> _scenery=[];
    private readonly Dictionary<(int,int),List<Body2D>> _sceneryCells=[];
    private readonly HashSet<Body2D> _activeScenery=[];
    private readonly List<Body2D> _removeBatch=[];
    public int SceneryProxyCount=>_scenery.Count;
    public int PhysicalPeople=>Pedestrians.Count(p=>p.Physical);
    public int PedestrianImpacts {get;private set;}

    private void AddSiteBox(CityPoint position, double halfX, double halfZ, double height)
    {
        double ground = CityTerrain.Ground(position);
        var body = World.AddBox(position.X, position.Z, halfX, halfZ, 0, .35, .08, 0);
        SceneryHeight(body, ground, ground + height);
    }

    private void AddScenery()
    {
        PedestrianImpacts=0;_scenery.Clear();_activeScenery.Clear();_sceneryHeights.Clear();if(!_useScenery)return;
        foreach(var b in CityModel.Blocks){
            // The planar broadphase inserts a circumscribed circle. Feeding an
            // entire apartment block to it fills hundreds of empty interior cells.
            // Closed facade strips preserve the exterior contact plane and keep
            // the discrete solver's search local. No playable building interiors.
            int nx=(int)Math.Ceiling(b.HalfX*2/3),nz=(int)Math.Ceiling(b.HalfZ*2/3);
            double hx=b.HalfX/nx,hz=b.HalfZ/nz;
            for(int n=0;n<nx;n++)for(int side=-1;side<=1;side+=2)
                SceneryHeight(World.AddBox(b.X-b.HalfX+(n+.5)*2*hx,b.Z+side*(b.HalfZ-.18),hx,.18,0,.35,.08,0),CityTerrain.Ground(b.X,b.Z),CityTerrain.Ground(b.X,b.Z)+b.Height);
            for(int n=0;n<nz;n++)for(int side=-1;side<=1;side+=2)
                SceneryHeight(World.AddBox(b.X+side*(b.HalfX-.18),b.Z-b.HalfZ+(n+.5)*2*hz,.18,hz,0,.35,.08,0),CityTerrain.Ground(b.X,b.Z),CityTerrain.Ground(b.X,b.Z)+b.Height);
        }
        foreach(var b in CityModel.Furniture.Where(b=>b.Height>3.9&&CityAmenities.StopClear(new(b.X,b.Z),.2)))
            SceneryHeight(World.AddCircle(b.X,b.Z,Math.Max(.16,Math.Max(b.HalfX,b.HalfZ)),0,.3,.05,0),CityTerrain.Ground(b.X,b.Z),CityTerrain.Ground(b.X,b.Z)+b.Height);
        foreach (var food in CityAmenities.Food())
        {
            var fixture = food.Fixture;
            AddSiteBox(food.Position, fixture.CollisionHalfSize.X, fixture.CollisionHalfSize.Z, fixture.CollisionHeight);
        }
        foreach (var sign in CityAmenities.Stops())
        {
            double ground = CityTerrain.Ground(sign);
            var centre = BusStopFixture.Centre(sign);
            var wall = BusStopFixture.RearWall(centre);
            var seat = BusStopFixture.Bench(centre);
            var wallHalf = BusStopFixture.WallHalfSize;
            var seatHalf = BusStopFixture.BenchHalfSize;
            SceneryHeight(World.AddBox(wall.X, wall.Z, wallHalf.X, wallHalf.Z, 0, .35, .08, 0), ground, ground + BusStopFixture.Height);
            SceneryHeight(World.AddBox(seat.X, seat.Z, seatHalf.X, seatHalf.Z, 0, .35, .08, 0), ground, ground + BusStopFixture.BenchHeight);
        }
        foreach(var s in CityImportedSites.All().Where(s=>s.Extra))SceneryHeight(World.AddBox(s.Position.X,s.Position.Z,s.HalfX,s.HalfZ,0,.35,.08,0),CityTerrain.Ground(s.Position),s.Ground+s.Height);
        foreach (var garage in CityAmenities.Garages())
            AddSiteBox(garage.Position, GarageSite.HalfX, GarageSite.HalfZ, GarageSite.WallHeight);
        foreach (var stall in CityAmenities.Markets())
            AddSiteBox(stall.Position, MarketSite.HalfX, MarketSite.HalfZ, MarketSite.CollisionHeight);
        foreach(var t in CityScenery.Trees())SceneryHeight(World.AddCircle(t.Position.X,t.Position.Z,t.Radius,0,.45,.04,0),CityTerrain.Ground(t.Position),CityTerrain.Ground(t.Position)+5);
        if(CityLayout.Rivers)for(int i=0;i<Nodes-1;i++)if(CityLayout.CanalColumn(i))for(int j=0;j<Nodes;j++)if(CityLayout.BridgeRow(j))
            for(double a=-CityWater.RailHalf;a<CityWater.RailHalf;a+=2)for(int side=-1;side<=1;side+=2){
                double end=Math.Min(CityWater.RailHalf,a+2);
                double x=Coordinate(i)+18+(a+end)*.5,z=Coordinate(j)+side*CityWater.DeckSide;
                SceneryHeight(World.AddBox(x,z,(end-a)*.5,.08,0,.35,.08,0),CityTerrain.Ground(x,z),CityTerrain.Ground(x,z)+1.1);
            }
        foreach(var span in CityTransit.Supports)foreach(var p in new[]{span.Left,span.Right})SceneryHeight(World.AddCircle(p.X,p.Z,.14,0,.3,.05,0),CityTerrain.Ground(p),CityTerrain.Ground(p)+6.15);
        AddLevelScenery();
        _scenery.AddRange(World.Bodies);_activeScenery.UnionWith(_scenery);
        _sceneryCells.Clear();
        foreach(var b in _scenery){
            var (hx,hz)=SceneryExtent(b);
            for(int x=(int)Math.Floor((b.X-hx)/36);x<=(int)Math.Floor((b.X+hx)/36);x++)
            for(int z=(int)Math.Floor((b.Y-hz)/36);z<=(int)Math.Floor((b.Y+hz)/36);z++){
                if(!_sceneryCells.TryGetValue((x,z),out var list))_sceneryCells[(x,z)]=list=[];list.Add(b);
            }
        }
    }
    private void RefreshScenery()
    {
        if(!_useScenery)return;
        // Only stream collision proxies, never visual geometry or dynamic actors.
        // A 0.5 s speed-expanded margin exceeds the 0.25 s refresh interval, so
        // a fast vehicle or a thrown pedestrian cannot reach an inactive collider.
        // This avoids feeding the sandbox's 1.2 m grid the entire empty city.
        var wanted=new HashSet<Body2D>();
        foreach(var car in Cars)if(car.Physical&&!car.Falling)Gather(car.Body,car.HalfLength+2);
        foreach(var p in Pedestrians)if(p.Physical&&!p.Detached)Gather(p.Body,1.1);
        foreach(var a in Animals)if(a.Physical&&a.Body is {} ab)Gather(ab,a.Radius+1);
        foreach(var p in _liveProps)if(p.Body is {} b)Gather(b,p.Radius+1);
        foreach(var s in Scooters)if(s.Physical)Gather(s.Body,1.2);
        void Gather(Body2D actor,double radius){
            radius+=Math.Sqrt(actor.Vx*actor.Vx+actor.Vy*actor.Vy)*.5;
            int x0=(int)Math.Floor((actor.X-radius)/36),x1=(int)Math.Floor((actor.X+radius)/36);
            int z0=(int)Math.Floor((actor.Y-radius)/36),z1=(int)Math.Floor((actor.Y+radius)/36);
            for(int x=x0;x<=x1;x++)for(int z=z0;z<=z1;z++)
                if(_sceneryCells.TryGetValue((x,z),out var bodies))foreach(var b in bodies){
                    var (hx,hz)=SceneryExtent(b);
                    double dx=Math.Max(0,Math.Abs(actor.X-b.X)-hx),dz=Math.Max(0,Math.Abs(actor.Y-b.Y)-hz);
                    if(dx*dx+dz*dz<radius*radius)wanted.Add(b);
                }
        }
        // One compaction pass instead of an IndexOf + RemoveBody per proxy.
        _removeBatch.Clear();
        foreach(var b in _activeScenery)if(!wanted.Contains(b))_removeBatch.Add(b);
        if(_removeBatch.Count>0){World.RemoveBodies(_removeBatch);_activeScenery.ExceptWith(_removeBatch);_removeBatch.Clear();}
        foreach(var b in wanted)if(_activeScenery.Add(b))World.Bodies.Add(b);
    }

    public int LastPhysicsSubsteps {get;private set;}
    // Opt-in developer measurement; off in normal play. Slots: preparation,
    // jobs, services, flocks, far rules, reactions, local movement, contacts, lifecycle;
    // nested totals: far cars, far walkers, terrain support.
    public bool MeasureStep;
    public double[] LastStepMilliseconds {get;}=new double[12];
    // A deep overlap (for example after recovery/loading) can make penetration
    // bias inject energy, which used to shrink the adaptive step indefinitely.
    // This game-specific emergency bound is far above normal driving speeds;
    // it is not applied to the shared educational contact solver.
    private void BoundContactMotion(){
        foreach(var b in World.Bodies){
            if(!double.IsFinite(b.Vx+b.Vy+b.W)){b.Vx=b.Vy=b.W=0;continue;}
            double radius=b.Shape==Shape2D.Circle?b.Radius:Math.Sqrt(b.HalfW*b.HalfW+b.HalfH*b.HalfH);
            double speed=Math.Sqrt(b.Vx*b.Vx+b.Vy*b.Vy)+Math.Abs(b.W)*radius;
            if(speed>90){double scale=90/speed;b.Vx*=scale;b.Vy*=scale;b.W*=scale;}
        }
        // Upright pedestrians are walking character discs, not free projectiles.
        // Dense crowds / freshly activated scenery can create penetration-bias
        // impulses. Keep those correction velocities bounded; HitPerson detaches
        // actual struck actors, whose airborne impulse remains unaffected.
        foreach(var p in Pedestrians)if(p.Physical&&!p.Detached&&!p.Dead&&p.KnockedFor<=0){
            var b=p.Body;double speed=Math.Sqrt(b.Vx*b.Vx+b.Vy*b.Vy);
            if(speed>12){b.Vx*=12/speed;b.Vy*=12/speed;}
        }
    }
    private void Register(CityCar car)
    {
        int i=World.AddBox(car.Position.X,car.Position.Z,car.HalfWidth,car.HalfLength,
            car.Mass/(4*car.HalfWidth*car.HalfLength),.30,.12,car.Kind,-car.Heading);
        car.Body=World.Bodies[i];if(car.Kind==9)car.Body.InvI=0;UpdateTerrainSupport(car);car.Physical=car.Hero||Detailed(car.Position,false);if(!car.Physical)Remove(car.Body);
        GridInsert(car);
    }
    private void Register(CityPedestrian p)
    {
        int i=World.AddCircle(p.Position.X,p.Position.Z,CityPedestrian.Radius,
            CityPedestrian.Mass/(Math.PI*CityPedestrian.Radius*CityPedestrian.Radius),.12,.02,0);
        p.Body=World.Bodies[i];
        // Pedestrians are upright character discs. Their visual facing is controlled
        // independently; a friction impulse must not spin a person like a billiard ball.
        p.Body.InvI=0;p.Physical=Detailed(p.Position,false);if(!p.Physical)Remove(p.Body);
        // The player disc joins the grid with the Pedestrians list (exit, respawn), not here.
        if(!p.IsPlayer)GridInsert(p);
    }
    private void Remove(Body2D body){int i=World.Bodies.IndexOf(body);if(i>=0)World.RemoveBody(i);}
    public void Step(double dt,DriveInput input=default)
    {
        if(!double.IsFinite(dt)||dt<=0||Cars.Count==0)return;
        long measuredAt=MeasureStep?System.Diagnostics.Stopwatch.GetTimestamp():0;
        if(MeasureStep)Array.Clear(LastStepMilliseconds);
        void Mark(int slot){if(!MeasureStep)return;long now=System.Diagnostics.Stopwatch.GetTimestamp();
            LastStepMilliseconds[slot]+=(now-measuredAt)*1000d/System.Diagnostics.Stopwatch.Frequency;measuredAt=now;}
        double remaining=Math.Min(dt,.1);LastPhysicsSubsteps=0;BoundContactMotion();
        _detailClock-=remaining;if(_detailClock<=0){_detailClock=.25;RefreshDetail();}
        // The pre-loop passes query the actor grid too (RuleCanEnter, spawners'
        // clearance); make it current for this frame's actor set first.
        RebuildActorGrid();RebuildPropGrid();Mark(0);
        DroneStep(remaining);PassengerStep(remaining);JobsStep(remaining);Mark(1);ServicesStep(remaining);AirPatrolStep(remaining);Mark(2);
        MaintainFlocks(remaining);Mark(3);RuleStep(remaining);SyncFarPoses(remaining);Mark(4);
        while(remaining>1e-10){
            // The shared contact solver is discrete. Limit travel (including the
            // rotating car corner) to 10 cm per step to prevent thin-pole tunnelling.
            double fastest=0;
            foreach(var car in Cars)if(car.Physical)fastest=Math.Max(fastest,Math.Sqrt(car.Body.Vx*car.Body.Vx+car.Body.Vy*car.Body.Vy)+Math.Abs(car.Body.W)*car.HalfLength);
            foreach(var p in Pedestrians)if(p.Physical)fastest=Math.Max(fastest,Math.Sqrt(p.Body.Vx*p.Body.Vx+p.Body.Vy*p.Body.Vy));
            foreach(var s in Scooters)if(s.Physical)fastest=Math.Max(fastest,Math.Sqrt(s.Body.Vx*s.Body.Vx+s.Body.Vy*s.Body.Vy)+Math.Abs(s.Body.W)*CityScooter.HalfLength);
            foreach(var a in Animals)if(a.Physical&&a.Body is {} animal)fastest=Math.Max(fastest,Math.Sqrt(animal.Vx*animal.Vx+animal.Vy*animal.Vy));
            foreach(var p in _liveProps)if(p.Body is {} prop)fastest=Math.Max(fastest,Math.Sqrt(prop.Vx*prop.Vx+prop.Vy*prop.Vy)+Math.Abs(prop.W)*p.Radius);
            // Split the remainder evenly: a tiny last step amplifies contact
            // penetration bias (1/dt), throwing character discs across the map.
            double limit=Math.Clamp(.10/Math.Max(1,fastest),1d/1200,1d/60);
            double h=remaining/Math.Clamp(Math.Ceiling(remaining/limit),1,120-LastPhysicsSubsteps);LastPhysicsSubsteps++;
            Time+=h;remaining-=h;ScootersBefore(h);EntryStep(h);TransitStep(h,input);RefreshActorGrid();SocialStep(h);CombatStep(h);VehicleLifeBefore(h);WaterStep(h);FireStep(h);
            Mark(5);
            _sceneryClock-=h;if(_sceneryClock<=0){_sceneryClock+=.25;RefreshScenery();}
            _controlClock-=h;
            if(_controlClock<=0){_controlClock+=.05;foreach(var car in Cars)if(car.Physical&&!car.Hero)DriveRoute(car,.05);}
            foreach(var car in Cars)if(car.Physical){UpdateTerrainSupport(car);if(car.Destroyed&&!car.Falling)VehicleDynamics.StepWreck(car,h);else if(car.Kind is 11 or 12)SpecialDrive(car,PlayerControls(car)?input:new(0,0,true),h);else if(car.Kind!=9)VehicleDynamics.Step(car,TrolleyPower(car,PlayerControls(car)?input:car.Driverless?new(0,0,true):car.Command),h,Grip);}
            CaptureAirSupport();
            for(int i=Pedestrians.Count-1;i>=0;i--)if(Pedestrians[i].Physical)WalkBody(Pedestrians[i],h);
            DetectPedestrianImpacts();LifeBeforeContacts(h);
            Mark(6);
            BeforeContacts(h);
            RefreshLevelBodies();World.Step(h);RestoreAirSupport();
            AfterContacts(h);BoundContactMotion();ConstrainTrams();
            Mark(7);
            foreach(var car in Cars){
                if(!car.Physical&&car.TransitRoute==0)continue;
                if(car.Physical)UpdateTerrainSupport(car,true);var old=car.Position;car.Position=new(car.Body.X,car.Body.Y);car.Heading=-car.Body.Angle;
                car.Speed=car.Body.Vx*Math.Sin(car.Heading)+car.Body.Vy*Math.Cos(car.Heading);
                car.Distance+=(car.Position-old).Length;
            }
            foreach(var p in Pedestrians){
                if(!p.Physical)continue;
                var old=p.Position;p.Position=new(p.Body.X,p.Body.Y);p.Speed=Math.Sqrt(p.Body.Vx*p.Body.Vx+p.Body.Vy*p.Body.Vy);
                p.Distance+=(p.Position-old).Length;
                if(p.Speed>.15&&p.KnockedFor<=0&&!p.Dead&&!(p.IsPlayer&&p.Aiming)){
                    double heading=Math.Atan2(p.Body.Vx,p.Body.Vy);
                    p.Heading+=Math.Clamp(Math.IEEERemainder(heading-p.Heading,Math.Tau),-5*h,5*h);
                }
            }
            ScootersAfter(h);LifeAfterContacts(h);VehicleLifeAfter(h);PlayerLifeStep();PickupStep(h);AmenitiesStep(h);
            _spawn+=h;if(_spawn>=.4){_spawn-=.4;ReplenishPopulation();}
            Mark(8);
        }
    }
    private void DriveRoute(CityCar car,double dt)
    {
        if(car.AirPatrol)return;
        if(car.TransitRoute>0)return;
        if(car.PoliceVehicle&&(car.Burning||car.CrewDeployed||OnFoot&&WantedLevel>0&&Mission(car) is {Kind:CityService.Police}&&(car.Position-PlayerPosition).Length<19&&ClearSight(car.Position,PlayerPosition))){car.PanicDriver=false;car.Command=new(0,0,true);return;}

        if(car.Destroyed||car.Falling){car.Command=default;return;}
        if(car.Driverless||car.Id==EnteringCarId){car.Command=new(0,0,true);return;}
        if(car.Burning&&car.PanicDriver){car.Command=new(1,Math.Sin(Time*1.3+car.Id)*.8);return;}
        if(car.Reservation>=0){
            int i=car.Reservation%Nodes,j=car.Reservation/Nodes;var centre=new CityPoint(Coordinate(i),Coordinate(j));
            if((car.I!=i||car.J!=j)&&CityPoint.Dot(car.Position-centre,Forward(car.Direction))>Sidewalk+1+car.HalfLength
               ||(car.Position-centre).Length>32){
                _reservations[car.Reservation]=-1;car.Reservation=-1;
            }
        }
        if(BusStop(car)||ServiceParked(car)){car.Command=new(0,0,true);return;}
        if(car.Phase!=3&&PolicePursuit(car,dt))return;
        car.Along=car.Path.ClosestDistance(car.Position);
        ConsiderLaneChange(car,dt);
        double remaining=car.Path.Length-car.Along;
        // Enter builds a crossing path and runs two clearance sweeps; its answer
        // only changes the command once the braking curve below can bite, i.e.
        // within Cruise²/5 m of the stop line. Farther out every driver used to
        // pay for it twenty times a second.
        bool enter=car.Phase!=0||remaining>car.Cruise*car.Cruise/5+1||Enter(car);
        if(remaining<.5&&(car.Position-car.Path.Sample(car.Path.Length).Point).Length<1.4){
            if(car.Phase==0&&enter){Cross(car);remaining=car.Path.Length;}
            else if(car.Phase==1){Continue(car);remaining=car.Path.Length;}
            else if(car.Phase==3){FinishLevelRoute(car);remaining=car.Path.Length;}
        }
        double look=2.8+Math.Abs(car.Speed)*.55;
        var point=car.Path.Sample(car.Along+look).Point;
        if(car.Phase==1&&remaining<look&&DepartureLevel(car) is not null){
            // Look through the crossing/ramp seam. Clamping the steering target
            // to the last point makes a long bus curl back towards that point
            // instead of straightening onto the connector.
            var end=car.Path.Sample(car.Path.Length);
            point=end.Point+new CityPoint(Math.Sin(end.Heading),Math.Cos(end.Heading))*(look-remaining);
        }
        if(car.Phase!=3)UpdateOvertake(car,dt);else car.OvertakeOffset=0;point+=Right(new(Math.Sin(car.Heading),Math.Cos(car.Heading)))*car.OvertakeOffset;
        var delta=point-car.Position;
        var f=new CityPoint(Math.Sin(car.Heading),Math.Cos(car.Heading));
        var r=new CityPoint(f.Z,-f.X);
        double forward=CityPoint.Dot(delta,f),side=CityPoint.Dot(delta,r);
        double curvature=2*side/Math.Max(1,CityPoint.Dot(delta,delta));
        double steering=Math.Atan(car.Wheelbase*curvature)/.68;
        double target=car.Phase==1?Math.Min(car.Cruise,car.Outgoing==(car.Direction+2)%4?2.2:3.5):car.Cruise;
        if(!enter)target=Math.Min(target,Math.Sqrt(5*Math.Max(0,remaining-.18)));
        // Predict occupied footprints on the route. This is ordinary AI lookahead,
        // not a scanner: it only asks the game world about nearby traffic.
        foreach(var other in NearbyCars(car.Position,22)){
            if(other==car||!SameHeight(car,other)||(other.Position-car.Position).Length>22)continue;
            if(car.Phase!=1&&Math.Cos(other.Heading-car.Heading)>.8&&CityPoint.Dot(other.Position-car.Position,f)<0)continue;
            for(double d=.8;d<=Math.Min(19,remaining)+.8;d+=1){
                var ahead=car.Path.Sample(car.Along+d);
                if(CarsOverlap(ahead.Point+Right(new(Math.Sin(ahead.Heading),Math.Cos(ahead.Heading)))*car.OvertakeOffset,ahead.Heading,other.Position,other.Heading,.18,car.HalfLength,car.HalfWidth,other.HalfLength,other.HalfWidth)){
                    target=Math.Min(target,Math.Sqrt(5*Math.Max(0,d-1.25)));break;
                }
            }
        }
        foreach(var p in NearbyPeople(car.Position,14)){
            if(!SameHeight(car,p))continue;
            var d=p.Position-car.Position;double ahead=CityPoint.Dot(d,f);
            if(ahead>0&&ahead<12&&Math.Abs(CityPoint.Dot(d,r))<car.HalfWidth+.7)
                target=Math.Min(target,Math.Sqrt(5*Math.Max(0,ahead-car.HalfLength-1.2)));
        }
        // A displaced driver aims back at its existing path. If it faces away or
        // becomes wedged, back up slowly before trying again; never reset its pose.
        double routeError=(car.Path.Sample(car.Along).Point-car.Position).Length;
        if(target>1&&Math.Abs(car.Speed)<.25)car.Stuck+=dt;else car.Stuck=0;
        if(car.ReverseFor<=0&&((forward<-.7&&routeError>1.5)||car.Stuck>3.5)){
            car.ReverseFor=1.7;car.Stuck=0;
        }
        if(car.ReverseFor>0){
            car.ReverseFor-=dt;
            // The longest vehicle is 5.95 m long by half: the grid query covers every
            // car that can satisfy the distance test below.
            bool blocked=false;
            foreach(var c in NearbyCars(car.Position,car.HalfLength+7))
                if(c!=car&&(c.Position-car.Position).Length<car.HalfLength+c.HalfLength+1&&CityPoint.Dot(c.Position-car.Position,f)<0){blocked=true;break;}
            car.Command=blocked?new(0,steering,true):new(Math.Clamp((-1.8-car.Speed)*.35,-.4,.4),-Math.Sign(side)*.8);
            return;
        }
        target*=Math.Clamp(1-Math.Abs(steering)*.35,.35,1);
        car.Command=new(Math.Clamp((target-car.Speed)*.5,0,1),Math.Clamp(steering,-1,1),car.Speed>target+.25||target<.08);
    }
    private void WalkBody(CityPedestrian p,double dt)
    {
        p.KnockedFor=Math.Max(0,p.KnockedFor-dt);
        if(p.FlightHeight>0||p.VerticalSpeed>0){
            p.VerticalSpeed-=9.81*dt;p.FlightHeight+=p.VerticalSpeed*dt;
            if(p.FlightHeight<0){p.FlightHeight=0;p.VerticalSpeed=0;}
        }
        var b=p.Body;b.Wake();
        if(p.KnockedFor>0||p.Dead){
            double drag=Math.Exp(-dt*(p.FlightHeight>0?.25:3.2));b.Vx*=drag;b.Vy*=drag;
            if(p.Detached){b.X+=b.Vx*dt;b.Y+=b.Vy*dt;}return;
        }
        if(p.Detached){
            var covering=NearbyCars(p.Position,8).FirstOrDefault(c=>CarPersonOverlap(c.Position,c.Heading,p.Position,.65,c.HalfLength,c.HalfWidth));
            if(covering is not null){
                // A living knocked-down actor can crawl out from a stopped bumper.
                // Waiting forever under a parked car left survivors permanently inert.
                // KnockedFor below re-arms for 0.1 s, so this branch runs once per
                // 0.1 s, not once per substep: the crawl advances 0.75 m/s x 0.1 s.
                // With dt it crept at 7.5*dt m/s - minutes from under a parked car
                // whenever a fast body nearby had shortened the substep.
                if(Math.Abs(covering.Speed)<.5){var away=p.Position-covering.Position;double length=away.Length;if(length<.01)away=new(1,0);else away*=1/length;
                    var escape=p.Position+away*(.75*.1);if(ClearWalk(escape)){b.X=escape.X;b.Y=escape.Z;}}
                p.KnockedFor=.1;return;
            }
            if(p.Physical)World.Bodies.Add(b);p.Detached=false;Reanchor(p);
        }
        RecoverWalkerClearance(p,dt);
        if(p.IsPlayer){WalkPlayer(dt);return;}
        if(ScooterWalk(p,dt)||CoverWalk(p,dt)||TransitWaitWalk(p,dt))return;
        if(ServiceWalk(p,dt)||LevelWalk(p,dt)||JobWalk(p,dt)||RoleWalk(p,dt))return;
        if(SocialVelocity(p,out var social)){MovePerson(p,social,dt,social.Length>.2?p.Position+social*(8/social.Length):null);return;}
        CityPoint desired=default;
        if(p.Wait>0)p.Wait-=dt;
        else{
            if(p.Path is null)ChooseWalk(p);
            if(p.Path is not null){
                p.Along=p.Path.ClosestDistance(p.Position);
                var end=p.Path.Sample(p.Path.Length).Point;
                if((end-p.Position).Length<.70){
                    if(p.Exiting){
                        if(p.OriginKey>=0)_pedPlaces[p.OriginKey]=-1;
                        Remove(b);Pedestrians.Remove(p);ExitedPedestrians++;return;
                    }
                    p.I=p.NextI;p.J=p.NextJ;p.Sx=p.NextSx;p.Sz=p.NextSz;p.Path=null;p.Crossing=false;
                    p.OriginKey=p.DestinationKey;p.DestinationKey=-1;p.Wait=.1+Random()*.3;
                }else{
                    var target=p.Path.Sample(p.Along+.8).Point;var delta=target-p.Position;
                    desired=delta*(Math.Min(p.Cruise,(end-p.Position).Length*3)/Math.Max(.01,delta.Length));
                    // Anticipate cars rather than stepping into a bumper. A player
                    // can still strike an actor; the solver handles that impact.
                    foreach(var car in NearbyCars(p.Position,20)){
                        double t=.35;
                        var future=car.Position+new CityPoint(car.Body.Vx,car.Body.Vy)*t;
                        if(Math.Abs(car.Speed)>1&&(future-p.Position).Length<7&&CarPersonOverlap(future,car.Heading,p.Position,.6,car.HalfLength,car.HalfWidth)){
                            desired=default;break;
                        }
                    }
                    // Reciprocal soft avoidance before the hard collision discs touch.
                    foreach(var other in NearbyPeople(p.Position,1.1)){
                        if(other==p||other.Dead||other.Detached)continue;var away=p.Position-other.Position;double d=away.Length;
                        if(d>.001&&d<1.1)desired+=away*((1.1-d)*1.25/d);
                    }
                }
            }
        }
        if(desired.Length>.2&&(p.Position-p.LastWalkPosition).Length<dt*.12)p.WalkStall+=dt;else p.WalkStall=0;
        p.LastWalkPosition=p.Position;
        if(p.WalkStall>3){p.WalkStall=0;Reanchor(p);p.Wait=0;}
        MovePerson(p,desired,dt,p.Path?.Sample(p.Path.Length).Point);
        if(p.OriginKey>=0&&p.Path is not null&&p.Along>.9){_pedPlaces[p.OriginKey]=-1;p.OriginKey=-1;}
    }
    private void DetectPedestrianImpacts()
    {
        foreach(var p in Pedestrians){
            if(!p.Physical||p.KnockedFor>0||p.Dead)continue;
            foreach(var car in NearbyCars(p.Position,7)){
                if(!SameHeight(car,p)||(p.Position-car.Position).Length>car.HalfLength+1||car.Body.Vx*car.Body.Vx+car.Body.Vy*car.Body.Vy<1.21)continue;
                var delta=p.Position-car.Position;
                double vx=car.Body.Vx-p.Body.Vx,vy=car.Body.Vy-p.Body.Vy;
                double approach=(vx*delta.X+vy*delta.Z)/Math.Max(.3,delta.Length);
                if(approach>1.1&&CarPersonOverlap(car.Position,car.Heading,p.Position,.06,car.HalfLength,car.HalfWidth)){
                    HitPerson(p,car,approach);break;
                }
            }
        }
    }
}
