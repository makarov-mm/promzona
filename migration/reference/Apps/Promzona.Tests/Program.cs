using Promzona.Game;
using MakarovPhysics.RigidBodies2D;
using System.Diagnostics;
static partial class Tests
{
    static int passed,failed;
    static void Check(bool value,string name){if(!value){failed++;Console.WriteLine("FAIL "+name);}else{passed++;Console.WriteLine("PASS "+name);}}
    static void Test(string name,Action action){try{action();}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
    static CityCar Car(CityContactWorld w,int kind=0,double x=0,double z=0,double heading=0){
        var c=new CityCar{Kind=kind,Position=new(x,z),Heading=heading,Hero=true};
        c.Body=w.Bodies[w.AddBox(x,z,c.HalfWidth,c.HalfLength,c.Mass/(4*c.HalfWidth*c.HalfLength),.30,.12,0,-heading)];return c;
    }
    static void Drive(CityContactWorld w,CityCar c,DriveInput input,double seconds,double traction=1){
        for(int i=0;i<(int)Math.Round(seconds*240);i++){
            VehicleDynamics.Step(c,input,1d/240,traction);w.Step(1d/240);c.Heading=-c.Body.Angle;
            c.Speed=c.Body.Vx*Math.Sin(c.Heading)+c.Body.Vy*Math.Cos(c.Heading);c.Position=new(c.Body.X,c.Body.Y);
        }
    }
    static CityTraffic Empty(){
        var t=new CityTraffic(false);t.World.Clear();t.Scooters.Clear();t.Cars.Clear();t.Pedestrians.Clear();t.Animals.Clear();t.Cash.Clear();return t;
    }
    static double Energy(Body2D b)=>(b.Vx*b.Vx+b.Vy*b.Vy)/(2*b.InvMass)+(b.InvI>0?b.W*b.W/(2*b.InvI):0);
    static CityTraffic QuietCity(){
        var t=new CityTraffic();
        foreach(var c in t.Cars.Skip(1))t.World.RemoveBody(t.World.Bodies.IndexOf(c.Body));
        foreach(var p in t.Pedestrians)t.World.RemoveBody(t.World.Bodies.IndexOf(p.Body));
        foreach(var a in t.Animals)if(a.Body is {} body)t.World.RemoveBody(t.World.Bodies.IndexOf(body));
        foreach(var s in t.Scooters)t.World.RemoveBody(t.World.Bodies.IndexOf(s.Body));t.Scooters.Clear();
        t.Cars.RemoveRange(1,t.Cars.Count-1);t.Pedestrians.Clear();t.Animals.Clear();return t;
    }
    static void Place(CityCar c,double x,double z,double vx=0,double vz=0){
        c.Body.X=x;c.Body.Y=z;c.Body.Vx=vx;c.Body.Vy=vz;c.Body.Angle=0;c.Body.W=0;
        c.Body.Wake();c.Position=new(x,z);c.Heading=0;c.Speed=vz;
    }
    static void Main(string[] args){
        if(args.Contains("--wrecks")){RunWreckPhysics();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--air-patrol")){RunAirPatrol();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--spatial-optimization")){RunSpatialOptimization();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--couriers")){RunCouriers();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--peace-jobs")){RunPeaceJobs();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--dissent")){RunDissent();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--console-fire")){RunConsoleFire();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--polish")){RunPolish();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--transit-combat")){RunTransitCombat();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--camera-audit")){RunCameraAudit();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--transit")){RunTransit();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--imported")){RunImportedAssets();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--refresh")){RunRefreshAudit();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--new-gameplay")){RunReview();RunSaveFormat();RunJobs();RunConsequences();RunMarket();RunIndustrial();RunTalk();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--level-traffic")){RunLevelTraffic();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--level-collisions")){RunLevelCollisions();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--levels")){RunRoadLevels();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--placement")){RunLayoutPlacement();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--distance")){RunRenderDistance();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--amenities")){RunAmenities();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--promzona")){RunPromzona();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--player")){RunPlayer();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--terrain")){RunTerrain();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--pickups")){RunPickups();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--recovery")){RunRecovery();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--behavior")){RunBehavior();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--crowd")){RunCrowdLod();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--outskirts")){RunOutskirts();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--realism")){RunRealism();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--living")){RunLivingCity();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--review")){RunReview();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--saveformat")){RunSaveFormat();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--jobs")){RunJobs();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--consequences")){RunConsequences();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--market")){RunMarket();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--industrial")){RunIndustrial();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--talk")){RunTalk();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        if(args.Contains("--audit")){RunAudit();Console.WriteLine($"Passed: {passed}, failed: {failed}");Environment.ExitCode=failed==0?0:1;return;}
        Test("vehicle",()=>{
            var w=new CityContactWorld{GravityY=0};var c=Car(w);
            Drive(w,c,default,2);Check(c.Position.Length<1e-9,"no autonomous player movement");
            Drive(w,c,new(1,0),3);double fast=c.Speed;Console.WriteLine($"speed after 3 s {fast:F3} m/s");
            Check(fast>9&&fast<20,"forward acceleration is finite");
            Drive(w,c,default,1);Check(c.Speed>fast*.85&&c.Speed<fast,"coasting preserves inertia with drag");
            Drive(w,c,new(0,0,true),3);Check(Math.Abs(c.Speed)<.05,"service brake stops without reversing");
            Drive(w,c,new(-1,0),2);Check(c.Speed< -2&&c.Speed> -7,"S reverses from rest");
            Drive(w,c,new(1,0),4);Check(c.Speed>2,"opposite pedal brakes then changes direction");
            Drive(w,c,new(1,1),1);Check(c.Heading>.1&&c.Body.W<0,"tire steering creates continuous physical yaw");
        });
        Test("mass and impact",()=>{
            var w=new CityContactWorld{GravityY=0};var a=Car(w,0,0,-4);var b=Car(w,2,0,2);
            a.Body.Vy=12;double momentum=a.Mass*12,energy=Energy(a.Body);
            for(int i=0;i<180;i++)w.Step(1d/240);
            Check(Math.Abs(a.Mass*a.Body.Vy+b.Mass*b.Body.Vy-momentum)<1e-6,"car collision conserves linear momentum");
            Check(Energy(a.Body)+Energy(b.Body)<energy*1.03,"inelastic collision does not create significant energy");
            Check(b.Body.Vy>0&&b.Body.Vy<4,"heavy truck receives smaller velocity change");
            Check(Math.Abs(1/b.Body.InvMass-6500)<1e-8&&Math.Abs(1/a.Body.InvMass-1250)<1e-8,"type mass reaches shared solver");
            Check(b.Body.Y-a.Body.Y>a.HalfLength+b.HalfLength-.05,"rectangles separate after head-on contact");
            var q=new CityContactWorld{GravityY=0};var light=Car(q);Drive(q,light,new(1,0),2);
            var r=new CityContactWorld{GravityY=0};var heavy=Car(r,2);Drive(r,heavy,new(1,0),2);
            Check(light.Speed>heavy.Speed,"truck acceleration responds to mass and engine force");
        });
        Test("glancing collision",()=>{
            var w=new CityContactWorld{GravityY=0};var a=Car(w,0,1.1,-4);var b=Car(w,0,0,1);
            a.Body.Vy=14;for(int i=0;i<150;i++)w.Step(1d/240);
            Check(Math.Abs(b.Body.Angle)>.06,"off-centre impact turns the other car");
        });
        Test("thin obstacle",()=>{
            var w=new CityContactWorld{GravityY=0};var c=Car(w);w.AddCircle(0,5,.16,0,.3,.05,0);c.Body.Vy=27;
            for(int i=0;i<150;i++){VehicleDynamics.Step(c,default,1d/480);w.Step(1d/480);}
            Check(c.Body.Y<5&&c.Body.Vy<8,"high speed car cannot tunnel through lamp post");
        });
        Test("soft pedestrian discs",()=>{
            var w=new CityContactWorld{GravityY=0};
            int ia=w.AddCircle(-.4,0,.3,75/(Math.PI*.09),.12,.02,0),ib=w.AddCircle(.4,0,.3,75/(Math.PI*.09),.12,.02,0);
            var a=w.Bodies[ia];var b=w.Bodies[ib];a.Vx=1;b.Vx=-1;
            for(int i=0;i<240;i++)w.Step(1d/240);
            Check(b.X-a.X>.58,"pedestrians cannot pass through each other");
            Check(Math.Abs(a.Vx)<.35&&Math.Abs(b.Vx)<.35,"walking collision is gentle");
        });
        Test("pedestrian hit and recovery",()=>{
            var t=Empty();var c=Car(t.World);t.Cars.Add(c);c.Body.Vy=4;
            var p=new CityPedestrian{Id=999,Position=new(0,4),Cruise=1.5,Path=new(new(0,4),new(0,100)),NextI=0,NextJ=1,NextSx=1,NextSz=1};
            p.Body=t.World.Bodies[t.World.AddCircle(0,4,.3,75/(Math.PI*.09),.12,.02,0)];p.Body.InvI=0;t.Pedestrians.Add(p);
            t.HitPerson(p,c,4);double height=0,velocity=0;
            for(int i=0;i<120;i++){t.Step(1d/120);height=Math.Max(height,p.FlightHeight);velocity=Math.Max(velocity,p.Speed);}
            Check(t.PedestrianImpacts>0&&height>.015&&velocity>1,"low-speed impact knocks pedestrian using contact impulse");
            // Brake the car and let the actor land, stand and return to its own path.
            c.Body.X+=10;c.Position=new(c.Body.X,c.Body.Y);for(int i=0;i<720;i++)t.Step(1d/120,new(0,0,true));
            Check(p.FlightHeight==0&&p.KnockedFor==0,"pedestrian lands and recovers");
            Console.WriteLine($"ped recovery: pos {p.Position}, speed {p.Speed:F3}, car {c.Position}, path {p.Path?.Length}, along {p.Along:F2}");
            // Clear the stopped vehicle from the walking route: waiting for a bumper
            // is correct, but the actor must walk again once the route is clear.
            c.Body.X+=10;c.Position=new(c.Body.X,c.Body.Y);
            var resume=p.Position;
            for(int i=0;i<240;i++)t.Step(1d/120,new(0,0,true));
            Check((p.Position-resume).Length>.3&&double.IsFinite(p.Position.X),"recovered pedestrian resumes walking");
        });
        Test("camera",()=>{
            var c=new CityCar{Position=new(3,5),Heading=Math.PI-.01};var cam=new ChaseCamera();cam.Reset(c);
            c.Heading=-Math.PI+.01;cam.Step(c,1d/60);
            Check(Math.Abs(cam.Heading-(Math.PI-.01))<.01,"camera wraps heading by short arc");
            c.Heading=0;double angle=cam.Heading;cam.Step(c,1d/60);
            Check(Math.Abs(cam.Heading-angle)<.02,"instantaneous car turn does not snap camera");
            for(int i=0;i<600;i++)cam.Step(c,1d/60);
            Check(Math.Abs(Math.IEEERemainder(cam.Heading,Math.Tau))<.01,"camera converges behind car");
            var a=new ChaseCamera();var b=new ChaseCamera();a.Reset(c);b.Reset(c);c.Heading=1.7;
            for(int i=0;i<120;i++)a.Step(c,1d/30);
            for(int i=0;i<480;i++)b.Step(c,1d/120);
            Check(Math.Abs(a.Heading-b.Heading)<1e-7,"camera smoothing independent of frame rate");
        });
        Test("streamed scenery and adaptive stepping",()=>{
            var t=new CityTraffic();
            var car=t.Hero;var block=CityModel.Blocks.First();
            car.Body.X=block.X;car.Body.Y=block.Z-block.HalfZ-12;car.Body.Angle=0;
            car.Body.Vx=0;car.Body.Vy=27;car.Position=new(car.Body.X,car.Body.Y);car.Heading=0;
            for(int i=0;i<120;i++)t.Step(1d/120);
            Check(car.Body.Y<block.Z-block.HalfZ-car.HalfLength+.10,"streamed building stops a 27 m/s player");
            Check(double.IsFinite(car.Body.Angle+car.Speed),"adaptive impact remains finite");
            int statics=t.World.Bodies.Count(b=>b.IsStatic);
            Check(statics>0&&statics<t.SceneryProxyCount*.85,"inactive scenery is culled from physics broadphase");
        });
        Test("city integration and route recovery",()=>{
            var t=new CityTraffic();var car=t.Cars[1];
            // Displace sideways into the open carriageway, without teleporting back.
            var original=car.Position;var f=new CityPoint(Math.Sin(car.Heading),Math.Cos(car.Heading));var right=new CityPoint(f.Z,-f.X);
            car.Body.X+=right.X*1.4;car.Body.Y+=right.Z*1.4;car.Position=new(car.Body.X,car.Body.Y);
            VehicleDynamics.ApplyImpulse(car.Body,right.X*car.Mass*2,right.Z*car.Mass*2);
            var start=car.Position;double best=double.MaxValue,maxError=0;
            var sw=Stopwatch.StartNew();
            for(int i=0;i<7200;i++){
                t.Step(1d/120,new(0,0,true));
                double error=(car.Path.Sample(car.Path.ClosestDistance(car.Position)).Point-car.Position).Length;
                if(i>120)best=Math.Min(best,error);maxError=Math.Max(maxError,error);
                if(i%120==0)CheckFinite(t);if(i%1200==0)Console.WriteLine($"city {i/120} s; wall {sw.Elapsed.TotalSeconds:F2}");
            }
            Console.WriteLine($"60 s city: {sw.Elapsed.TotalSeconds:F2} wall seconds; recovery min {best:F3}, max {maxError:F3}; cars {t.Cars.Count}, pedestrians {t.Pedestrians.Count}, exits {t.ExitedCars}/{t.ExitedPedestrians}");
            Check(best<.6&&car.Distance>3,"displaced AI car steers back onto route");
            Check(t.Cars.Sum(c=>c.Distance)>300&&t.Cars.Sum(c=>c.Turns)>10,"traffic drives and turns physically");
            Check(t.Cars.Count>=360&&t.Pedestrians.Count>=1000&&t.Cars.All(c=>Math.Abs(c.Position.X)<CityTraffic.LandEdge&&Math.Abs(c.Position.Z)<CityTraffic.LandEdge),"island traffic stays on land with bounded replenishment");
            Check(t.Hero.Distance<.5,"braked player remains stationary on sloped street during city run");
        });
        Test("bus and expanded world",()=>{
            var t=new CityTraffic();var bus=t.Cars.FirstOrDefault(c=>c.Kind==5);
            Check(CityTraffic.Nodes==33&&CityTraffic.EdgeHigh==636&&CityModel.Blocks.Length>1500,"doubled axes provide 1024 blocks and 1272 m world width");
            Check(bus is not null&&Math.Abs(1/bus.Body.InvMass-10500)<1e-7&&bus.Body.HalfH==bus.HalfLength,"bus dimensions and mass reach contact solver");
            Check(t.Props.Select(p=>p.Kind).Distinct().Count()==12,"twelve kinds of movable street objects including garbage bags");
            Check(CityModel.Blocks.Any(b=>b.Height<3.1)&&Enumerable.Range(0,16).Sum(i=>Enumerable.Range(0,16).Count(j=>CityModel.ParkBlock(i,j)))>20,"kiosks and parks populate map");
        });
        Test("physical street objects and remote respawn",()=>{
            // Verify the impulse in an unobstructed fixture; random city bins may
            // stand against a wall which correctly limits their travel.
            var isolated=Empty();var moving=Car(isolated.World,0,0,-5);isolated.Cars.Add(moving);
            var loose=new StreetProp{Kind=StreetKind.Bin,Home=new(0,0)};
            loose.Body=isolated.World.Bodies[isolated.World.AddCircle(0,0,loose.Radius,loose.Mass/(Math.PI*loose.Radius*loose.Radius),.35,.22,0)];isolated.Props.Add(loose);moving.Body.Vy=16;
            double peak=0;for(int i=0;i<110;i++){isolated.Step(1d/120);peak=Math.Max(peak,loose.Height);}
            Check(loose.Position.Length>.6&&peak>.03,"car launches bin in unobstructed space");
            var t=QuietCity();t.Step(.1);var c=t.Hero;
            var p=t.Props.First(p=>p.Kind==StreetKind.Bin&&p.Body!=null&&(p.Home-c.Position).Length<60);
            Place(c,p.Home.X,p.Home.Z-5,0,16);
            double maxHeight=0;
            for(int i=0;i<110;i++){t.Step(1d/120);maxHeight=Math.Max(maxHeight,p.Height);}
            Console.WriteLine($"bin moved {(p.Position-p.Home).Length:F3}, height {maxHeight:F3}");
            Check((p.Position-p.Home).Length>.05&&maxHeight>.03,"city bin responds to impact even with nearby scenery");
            Check(p.Body!=null,"nearby displaced object is retained");
            Place(c,300,300);for(int i=0;i<5;i++)t.Step(.1);
            Check(p.Body==null&&p.Position==p.Home&&p.Height==0&&t.PropRespawns>0,"far displaced object resets to original home");
            Place(c,p.Home.X+5,p.Home.Z);for(int i=0;i<4;i++)t.Step(.1);
            Check(p.Body!=null&&(p.Position-p.Home).Length<.01,"returning player sees restored physical bin");
            var bag=t.Props.First(p=>p.Kind==StreetKind.Bag&&p.Body!=null);
            var before=bag.Position;for(int i=0;i<30;i++)t.Step(1d/120);
            Check((bag.Position-before).Length>.005,"wind moves a nearby physical bag");
        });
        Test("vehicle jump and rollover",()=>{
            var t=Empty();var c=Car(t.World,0,0,0);t.Cars.Add(c);
            c.VerticalVelocity=3;c.RollVelocity=7;
            double high=0,roll=0;for(int i=0;i<240;i++){t.Step(1d/120);high=Math.Max(high,c.Lift);roll=Math.Max(roll,Math.Abs(c.Roll));}
            Check(high>.3&&roll>Math.PI/2,"resolved impact roll state can cross onto roof");
            c.Roll=Math.PI;c.RollVelocity=0;c.Pitch=c.PitchVelocity=0;c.Lift=c.VerticalVelocity=0;
            t.Step(.1,new(1,0));
            Check(!c.Grounded&&double.IsFinite(c.Roll+c.Pitch)&&Math.Abs(c.Speed)<.01,"parked roof-down vehicle remains finite and loses tire propulsion");
            c.Speed=0;t.RecoverPlayer();Check(c.Recovering&&Math.Abs(c.Roll)>3,"R starts a visible recovery");
            double raised=0;for(int i=0;i<280;i++){t.Step(1d/120);raised=Math.Max(raised,c.Lift);}
            Check(raised>1&&c.Grounded&&c.Roll==0&&!c.Recovering,"recovery visibly lifts, rolls and lowers the car");
            var hit=Empty();var a=Car(hit.World,0,1.1,-4);var b=Car(hit.World,2,0,2);b.Hero=false;b.Path=new(new(0,2),new(0,100));b.Cruise=0;hit.Cars.Add(a);hit.Cars.Add(b);a.Body.Vy=22;
            double lift=0,tilt=0;for(int i=0;i<150;i++){hit.Step(1d/120);lift=Math.Max(lift,a.Lift);tilt=Math.Max(tilt,Math.Abs(a.Roll));}
            Check(lift>.05&&tilt>.05&&a.Damage>0,"glancing physical collision excites chassis lift and roll");
        });
        Test("physical side impact rollover",()=>{
            var t=Empty();var a=Car(t.World);var b=Car(t.World,2,-8,0,Math.PI/2);t.Cars.Add(a);t.Cars.Add(b);b.Body.Vx=22;
            double roll=0,lift=0;
            for(int i=0;i<180;i++){t.Step(1d/120);roll=Math.Max(roll,Math.Abs(a.Roll));lift=Math.Max(lift,a.Lift);}
            Console.WriteLine($"broadside roll {roll:F3}, lift {lift:F3}");
            Check(roll>Math.PI/2&&lift>.2,"truck broadside impulse can overturn the lighter car");
        });
        Test("bounded effects and fading marks",()=>{
            var t=Empty();t.Cars.Add(Car(t.World));t.Emit(0,1,0,4,2000);
            Check(t.Particles.Count==900,"collision burst respects particle cap");
            var mark=new CityMark{Point=new(0,0),End=new(1,0),Skid=true,Life=.5};t.Marks.Add(mark);
            t.Step(.1);Check(mark.Opacity<1&&mark.Opacity>0,"decal fades continuously");
            for(int i=0;i<12;i++)t.Step(.1);
            Check(t.Marks.Count==0&&t.Particles.Count==0,"expired decals and fire particles are removed");
        });
        Test("solar clock",()=>{
            var m=new CityModel();m.Traffic.Cars.Clear();m.Traffic.Pedestrians.Clear();m.Traffic.World.Clear();
            // Lighting can be measured without advancing expensive actor simulation.
            m.SetHour(12);Check(m.Daylight>.99,"noon is daylight");m.SetHour(0);Check(m.Daylight<.01,"midnight is dark");
            m.SetHour(23.999);m.Step(.1);Check(m.Hour<.01,"solar clock wraps through midnight");
            m.SetHour(6);m.DayNightCycle=false;m.Step(.1);Check(m.Hour==6,"fixed time freezes only solar cycle");
            m.DayNightCycle=true;m.CycleMinutes=10;m.Step(.1);
            Check(m.Hour>6 && m.Hour<6.002,"sunrise advances more slowly than uniform time");
            foreach(double minutes in new[]{2d,10,20,30}){
                m.CycleMinutes=minutes;
                foreach(double hour in new[]{.1,5.5,7.25,12,17.75,23.9}){
                    m.SetHour(hour); m.AdvanceClock(minutes*60);
                    Check(Math.Abs(m.Hour-hour)<1e-9,"full configured day returns to the same local time");
                }
            }
            m.CycleMinutes=20;
            foreach(var (start,end) in new[]{(5,8),(16,19)}){
                m.SetHour(start);
                int seconds=0;
                while(m.Hour<end && seconds<600){m.AdvanceClock(1);seconds++;}
                Console.WriteLine($"Solar interval {start}–{end}: {seconds} s");
                Check(seconds>=240 && seconds<=310,"dawn and dusk linger within the twenty-minute cycle");
            }
            m.SetHour(17.31); m.AdvanceClock(7);
            double saved=m.Hour; m.AdvanceClock(3); double expected=m.Hour;
            m.SetHour(saved); m.AdvanceClock(3);
            Check(Math.Abs(m.Hour-expected)<1e-10,"restoring the displayed hour resumes at the same clock rate");
        });
        RunWreckPhysics();RunAirPatrol();RunCombatHud();RunSpatialOptimization();RunCouriers();RunPeaceJobs();RunDissent();RunConsoleFire();RunNewFeatures();RunLivingCity();RunRealism();RunOutskirts();RunCrowdLod();RunBehavior();RunRecovery();RunPickups();RunTerrain();RunPlayer();RunPromzona();RunAmenities();RunRenderDistance();RunLayoutPlacement();RunImportedAssets();
        RunRoadLevels();RunLevelCollisions();
        RunPolish();RunReview();RunSaveFormat();RunJobs();RunConsequences();RunMarket();RunIndustrial();RunTalk();RunRefreshAudit();RunTransitCombat();RunTransit();RunCameraAudit();
        RunAudit();
        Console.WriteLine($"Passed: {passed}, failed: {failed}");
        Environment.ExitCode=failed==0?0:1;
    }
    static void CheckFinite(CityTraffic t){
        foreach(var c in t.Cars)if(!double.IsFinite(c.Body.X+c.Body.Y+c.Body.Angle+c.Body.Vx+c.Body.Vy)||Math.Abs(c.Body.Vx)>100||Math.Abs(c.Body.Vy)>100)throw new Exception("nonfinite or explosive vehicle");
        foreach(var p in t.Pedestrians)if(!double.IsFinite(p.Body.X+p.Body.Y+p.Speed+p.FlightHeight))throw new Exception("nonfinite pedestrian");
    }
}

