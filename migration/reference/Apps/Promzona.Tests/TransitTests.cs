using MakarovPhysics.RigidBodies2D;
using Promzona.Game;
static partial class Tests
{
    static void RunTransit(){
        Test("frame counter and map coordinate basis",()=>{
            var fps=new CityFrameRate();for(int n=0;n<120;n++)fps.Sample(1d/60);
            Check(Math.Abs(fps.FramesPerSecond-60)<1e-8,"FPS uses actual frame intervals");
            for(int n=0;n<20;n++)fps.Sample(.1);Check(Math.Abs(fps.FramesPerSecond-10)<1e-8,"FPS is not capped to simulation dt");
            fps.Sample(double.NaN);fps.Sample(9);Check(double.IsFinite(fps.FramesPerSecond),"pause and invalid intervals do not pollute FPS");
            for(int dir=0;dir<4;dir++){var f=CityTraffic.Forward(dir);var right=CityTraffic.Right(f);var mf=CityMapProjection.NorthUp(f);var mr=CityMapProjection.NorthUp(right);
                Check(mf.X*mr.Z-mf.Z*mr.X>0,$"map preserves camera right for direction {dir}");}
        });
        foreach(int seed in new[]{73921,1,42,91837})Test("transit topology "+seed,()=>{
            CityLayout.Install(CityLayout.Generate(seed,gridSize:49,wideRoads:true,roadRevision:3));
            Check(CityTransit.Routes.Count==4,"four usable routes "+seed);
            foreach(var route in CityTransit.Routes){
                Check(route.Length>1800&&route.Stops.Length>=7,"closed route has distributed stops");
                Check((route.Sample(0).Point-route.Sample(route.Length).Point).Length<1e-8,"route closes without a teleport");
                double maxTurn=0;bool dry=true,street=true,layer=true;
                for(double s=0;s<route.Length;s+=1){var a=route.Sample(s);var b=route.Sample(s+1);maxTurn=Math.Max(maxTurn,Math.Abs(Math.IEEERemainder(b.Heading-a.Heading,Math.Tau)));dry&=CityWater.Depth(a.Point)<.05;street&=CityRoadSurface.Contains(a.Point);layer&=CityRoadLevels.At(a.Point.X,a.Point.Z,7)==null;}
                Check(dry&&street&&layer,"route stays on dry ground streets outside layered roads");Check(maxTurn<.3,"continuous rounded corners");
                Console.WriteLine($"ROUTE seed={seed} id={route.Id} length={route.Length:F1} start={route.Sample(0).Point}");
            }
        });
        CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:3));
        Test("tram driving and constraint",()=>{
            var t=FootSandbox();var rail=CityTransit.Routes.First();var c=t.Hero;c.Kind=9;c.TransitRoute=rail.Id;c.TransitAlong=8;
            var pose=rail.Sample(8);Place(c,pose.Point.X,pose.Point.Z);c.Heading=pose.Heading;c.Body.Angle=-pose.Heading;
            c.Body.InvMass=1/c.Mass;c.Body.InvI=0;
            for(int n=0;n<180;n++){Call(t,"TransitStep",1d/60,new DriveInput(1,1));t.World.Step(1d/60);Call(t,"ConstrainTrams");c.Position=new(c.Body.X,c.Body.Y);c.Heading=-c.Body.Angle;c.Speed=c.Body.Vx*Math.Sin(c.Heading)+c.Body.Vy*Math.Cos(c.Heading);}
            Check(c.TransitAlong>12&&c.Speed>2,"W moves heavy tram along track");
            Check((c.Position-rail.Sample(c.TransitAlong).Point).Length<1e-8,"A/D cannot derail tram");
            double forward=c.TransitAlong;
            for(int n=0;n<600;n++){Call(t,"TransitStep",1d/60,new DriveInput(-1,0));t.World.Step(1d/60);Call(t,"ConstrainTrams");c.Position=new(c.Body.X,c.Body.Y);c.Heading=-c.Body.Angle;c.Speed=c.Body.Vx*Math.Sin(c.Heading)+c.Body.Vy*Math.Cos(c.Heading);}
            Check(c.Speed< -1,"S brakes then reverses the tram");
            Check(CityFleet.Model(c)=="free_soviet_tram_x-series"&&c.Mass==17000,"tram uses imported model and its own mass");
            c.Kind=10;c.TransitRoute=2;var wire=CityTransit.ById(2)!;var at=wire.Sample(0);Place(c,at.Point.X+15,at.Point.Z+15);
            var power=(DriveInput)Call(t,"TrolleyPower",c,new DriveInput(1,.5))!;
            Check(power.Throttle==0&&power.Steering==.5,"off-wire trolley loses traction but keeps steering");
        });
        foreach(bool physical in new[]{false,true}) foreach(int kind in new[]{9,10}) Test($"transit NPC circuit {kind} physical={physical}",()=>{
            var t=FootSandbox();Place(t.Hero,600,600);var route=CityTransit.ById(kind==9?1:2)!;var first=route.Sample(2);var c=Npc(t,kind,first.Point.X,first.Point.Z);
            c.TransitRoute=route.Id;c.TransitAlong=2;c.Heading=first.Heading;c.Body.Angle=-c.Heading;c.Physical=physical;if(kind==9)c.Body.InvI=0;if(!physical)t.World.Bodies.Remove(c.Body);
            double travel=0,maxError=0;int stopFrames=0;bool departed=false;
            for(int n=0;n<(route.Length/2+180)*60&&travel<route.Length+5;n++){
                typeof(CityTraffic).GetProperty("Time")!.SetValue(t,(n+1)/60d);Call(t,"RebuildActorGrid");Call(t,"TransitStep",1d/60,default(DriveInput));
                if(physical){if(kind==10)VehicleDynamics.Step(c,(DriveInput)Call(t,"TrolleyPower",c,c.Command)!,1d/60);t.World.Step(1d/60);Call(t,"ConstrainTrams");}
                var old=c.Position;c.Position=new(c.Body.X,c.Body.Y);c.Heading=-c.Body.Angle;c.Speed=c.Body.Vx*Math.Sin(c.Heading)+c.Body.Vy*Math.Cos(c.Heading);
                travel+=(c.Position-old).Length;maxError=Math.Max(maxError,(c.Position-route.Sample(route.Closest(c.Position)).Point).Length);
                if(t.Time<c.TransitStopUntil)stopFrames++;else if(stopFrames>120&&c.Speed>1)departed=true;
            }
            Console.WriteLine($"NPC kind={kind} physical={physical} travel={travel} error={maxError} stopframes={stopFrames}");
            Check(travel>route.Length,"NPC completes a circuit including signal waits");Check(maxError<(kind==9?.03:2.5),"NPC stays on its track/contact corridor");Check(stopFrames>120&&departed,"NPC dwells at a stop then resumes");
        });
        Test("emergency exit and road rescue",()=>{
            var t=QuietCity();var c=t.Hero;var b=CityModel.Blocks.First();Place(c,b.X,b.Z);c.Roll=2;c.Damage=.4;t.AddCrime(2);Call(t,"RebuildActorGrid");
            t.RecoverPlayer();Check(c.Recovering&&CityRoadSurface.Contains(c.Position),"R recovers a wedged car to a road");
            Check(c.Damage==.4&&t.WantedLevel==2,"rescue does not repair or clear wanted");
            Check((c.Position-new CityPoint(b.X,b.Z)).Length>2,"recovery actually changes trapped position");
            c.RecoveryTime=0;var before=c.Position;t.RecoverPlayer();Check(c.Position==before,"recovery cooldown prevents repeated jumps");
            c.Roll=3.1;c.Speed=0;c.Body.Vx=c.Body.Vy=0;Check(t.InteractVehicle()&&t.OnFoot,"F can escape a stationary overturned car");
            Check(!CityTraffic.CarPersonOverlap(c.Position,c.Heading,t.PlayerPosition,.3,c.HalfLength,c.HalfWidth),"escaped player is outside the car hull");
        });
        CityLayout.Install(CityLayout.Generate(73921));
        Test("specific vehicle handover contract",()=>{
            var t=FootSandbox();var car=Npc(t,0,140,0);car.Id=123456;car.Damage=.1;
            Check(t.StartContract(JobKind.VehicleDelivery)&&t.ContractActorId==car.Id,"contract chooses a specific healthy vehicle");
            Check(!t.StartContract(JobKind.Elimination),"cannot replace an active job");
            var dest=(CityPoint)typeof(CityTraffic).GetField("_contractDestination",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(t)!;
            Place(t.Hero,dest.X,dest.Z);Call(t,"JobsStep",2d);Check(t.JobsCompleted==0,"wrong vehicle does not complete delivery");
            t.Hero.Hero=false;t.Cars.Remove(car);t.Cars.Insert(0,car);car.Hero=true;car.Driverless=false;Place(car,dest.X,dest.Z);car.Damage=.51;Call(t,"JobsStep",2d);Check(t.JobsCompleted==0,"below fifty percent cannot be handed over");
            car.Damage=.5;Call(t,"JobsStep",2d);Check(t.JobsCompleted==1&&t.Money==250,"exactly fifty percent earns delivery payment");
            Call(t,"JobsStep",2d);Check(t.Money==250,"delivery pays only once");
        });
        Test("target attribution and contract persistence",()=>{
            var t=FootSandbox();var a=Person(t,140,0);a.Id=12345;var b=Person(t,160,0);b.Id=12346;
            Check(t.StartContract(JobKind.Elimination),"target contract available");int target=t.ContractActorId;var person=t.Pedestrians.First(p=>p.Id==target);
            Call(t,"FatalPerson",person,false);Check(t.JobsFailed==1&&t.Money==0,"non-player death fails without payment");
            Check(t.StartContract(JobKind.Elimination),"can accept a fresh target");target=t.ContractActorId;person=t.Pedestrians.First(p=>p.Id==target);
            t.Hero.Path=new(t.Hero.Position,t.Hero.Position+new CityPoint(0,1));var model=new CityModel();var snapshot=t.CaptureWorld(model);CityTraffic.ValidateWorld(snapshot);t.RestoreWorld(snapshot,model);
            Check(t.ContractActorId==target&&t.JobKind==JobKind.Elimination,"save/load retains exact target and job");
            person=t.Pedestrians.First(p=>p.Id==target);Call(t,"FatalPerson",person,true);Check(t.JobsCompleted==1&&t.Money==350,"player-caused target death pays");
            Call(t,"FatalPerson",person,true);Check(t.Money==350,"duplicate death cannot duplicate contract payout");
        });
    }
}
