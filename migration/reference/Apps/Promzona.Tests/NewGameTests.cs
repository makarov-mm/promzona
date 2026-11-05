using Promzona.Game;
using MakarovPhysics.RigidBodies2D;
static partial class Tests
{
    static void RunNewFeatures(){
        Test("seeded generation and topology",()=>{
            var original=CityLayout.Current;var reports=new List<double>();
            var a=CityLayout.Generate(1,(v,_)=>reports.Add(v));var b=CityLayout.Generate(2);var repeat=CityLayout.Generate(1);
            Check(a.Blocks.SequenceEqual(repeat.Blocks)&&!a.Blocks.SequenceEqual(b.Blocks),"same seed repeats buildings; other seed changes them");
            Check(reports.Count>100&&reports.Zip(reports.Skip(1)).All(p=>p.First<=p.Second+1e-12)&&Math.Abs(reports[^1]-.48)<1e-12,"progress reports completed generation work monotonically");
            foreach(int seed in new[]{1,2,73921,42,1234567}){
                var layout=CityLayout.Generate(seed);CityLayout.Install(layout);
                var visited=new HashSet<(int,int)>{(0,0)};var queue=new Queue<(int,int)>();queue.Enqueue((0,0));bool symmetric=true;
                while(queue.Count>0){var(i,j)=queue.Dequeue();for(int d=0;d<4;d++)if(layout.Road(i,j,d)){
                    var f=CityTraffic.Forward(d);int ni=i+(int)f.X,nj=j+(int)f.Z;if(ni<0||nj<0||ni>=33||nj>=33)continue;
                    symmetric&=layout.Road(ni,nj,(d+2)%4);if(visited.Add((ni,nj)))queue.Enqueue((ni,nj));
                }}
                Check(symmetric&&visited.Count==1089,$"seed {seed}: every intersection connected, reciprocal roads");
            }
            var cts=new CancellationTokenSource();cts.Cancel();bool cancelled=false;
            try{CityLayout.Generate(5,null,cts.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"cancelled generation exits instead of retaining a task");
            CityLayout.Install(original);
        });
        Test("emergency braking and turn recovery",()=>{
            double Stop(double speed,double grip,int kind){
                var world=new CityContactWorld{GravityY=0};var car=Car(world,kind);
                car.Body.Vy=car.Speed=speed;
                Drive(world,car,new(1,0,false,true),5,grip);
                Check(Math.Abs(car.Speed)<.01 && Math.Abs(car.Heading)<.001,"Space holds the car even with throttle");
                return car.Position.Z;
            }
            double dry=Stop(20,1,0), wet=Stop(20,.8,0);
            Check(dry>20 && dry<26 && wet>dry*1.15 && wet<34,"72 km/h stop uses both axles and respects wet grip");
            Check(Stop(-7,1,0) is < -2 and > -4 && Stop(20,1,2) is >20 and <26,"reverse and truck emergency stops");
            foreach(double steer in new[]{-.8,.8}){
                var world=new CityContactWorld{GravityY=0};var car=Car(world);
                car.Body.Vy=car.Speed=20;
                double maxSlip=0;
                for(int tick=0;tick<360;tick++){
                    Drive(world,car,new(0,tick<180?steer:0,false,true),1d/240);
                    maxSlip=Math.Max(maxSlip,Math.Abs(car.Body.Vx*Math.Cos(car.Heading)-car.Body.Vy*Math.Sin(car.Heading)));
                }
                Console.WriteLine($"Emergency turn: peak lateral {maxSlip:F2} m/s, yaw after centring {car.Body.W:F2} rad/s");
                Check(maxSlip is >.2 and <5 && car.Heading*steer>0 && Math.Abs(car.Heading)<1.4,
                    "a fast braked turn allows slip without spinning around");
                Check(Math.Abs(car.Body.W)<.20,"centring the steering arrests rotation during a stop");
            }
            var t=Empty();var c=Car(t.World);t.Cars.Add(c);c.Body.Vy=18;c.Speed=18;
            for(int i=0;i<100;i++)t.Step(1d/120,new(0,.7,false,true));
            Check(t.Marks.Any(m=>m.Skid),"handbrake/drift leaves tire marks");
        });
        Test("automatic recovery",()=>{
            var t=Empty();var c=Car(t.World);t.Cars.Add(c);c.Roll=Math.PI;
            for(int i=0;i<360;i++)t.Step(1d/120);
            Check(!c.Recovering&&Math.Abs(c.Roll)>3,"roof-down car waits several seconds before recovery");
            double lift=0;bool started=false;
            for(int i=0;i<420;i++){t.Step(1d/120);started|=c.Recovering;lift=Math.Max(lift,c.Lift);}
            Check(started&&lift>1&&c.Grounded&&!c.Recovering,"automatic recovery is visible and returns tires to ground");
        });
        Test("cash and actor lifetime",()=>{
            var t=Empty();var c=Car(t.World);t.Cars.Add(c);
            t.Cash.Add(new(){Position=c.Position,Amount=35,Created=t.Time});
            for(int i=0;i<9;i++)t.Step(.1);
            Check(t.Money==0&&t.Cash.Count==1,"cash cannot be collected during first second");
            for(int i=0;i<3;i++)t.Step(.1);
            Check(t.Money==35&&t.Cash.Count==0,"cash picked once after one second");
            for(int i=0;i<12;i++)t.Step(.1);
            Check(t.Money==35,"collected cash does not pay again");
            int dropped=0;
            for(int i=0;i<30;i++){
                var p=new CityPedestrian{Id=5000+i,Position=new(20+i,20)};
                p.Body=t.World.Bodies[t.World.AddCircle(p.Position.X,p.Position.Z,.3,265,.1,.02,0)];t.Pedestrians.Add(p);
                int before=t.Cash.Count;t.HitPerson(p,c,12);
                Check(p.Dead&&p.VerticalSpeed>2,"high-speed hit is fatal and airborne");
                if(t.Cash.Count>before)dropped++;
            }
            Check(dropped>0&&dropped<30,"only some pedestrians drop money");
            // Live actors spawned earlier remain active; cleanup only applies to bodies.
            var oldPeople=t.Pedestrians.Where(p=>p.Dead).ToArray();foreach(var p in oldPeople){p.FlightHeight=0;p.VerticalSpeed=0;p.DeathTime=-300;}
            Place(c,300,300);t.Step(.1);
            Check(oldPeople.All(p=>!t.Pedestrians.Contains(p)&&!t.World.Bodies.Contains(p.Body)),"distant bodies removed from both simulation lists and solver");
            var a=new CityAnimal{Kind=AnimalKind.Pigeon,Flying=true,FlightTime=5.1,Height=20,Position=new(0,0)};
            t.Animals.Add(a);t.Step(.1);Check(!t.Animals.Contains(a),"escaped pigeon actor removed");
            var dog=new CityAnimal{Kind=AnimalKind.Dog,Dead=true,DeathTime=-30,Position=new(0,0)};
            dog.Body=t.World.Bodies[t.World.AddCircle(0,0,.38,40,.1,.01,0)];t.Animals.Add(dog);t.Step(.1);
            Check(!t.Animals.Contains(dog)&&!t.World.Bodies.Contains(dog.Body),"distant dead animal releases physics body");
        });
        Test("pigeons flee and detach",()=>{
            var t=Empty();var c=Car(t.World);t.Cars.Add(c);
            var bird=new CityAnimal{Kind=AnimalKind.Pigeon,Position=new(1,1),Target=new(2,2),Wait=20};
            bird.Body=t.World.Bodies[t.World.AddCircle(1,1,.16,4,.1,.01,0)];var body=bird.Body;t.Animals.Add(bird);
            for(int i=0;i<120;i++)t.Step(1d/120);
            Check(bird.Flying&&bird.Height>.8&&bird.Body==null&&!t.World.Bodies.Contains(body),"approaching vehicle scares pigeon; airborne bird releases planar collider");
            for(int i=0;i<600;i++)t.Step(1d/120);
            Check(!t.Animals.Contains(bird)&&t.AnimalsDeparted>0,"flown-away pigeon no longer retained");
        });
        Test("weather",()=>{
            var m=new CityModel();m.SetRain(true);double before=m.Rain;
            for(int i=0;i<40;i++)m.Step(.1);
            Check(m.Rain>before&&m.Wetness>0&&m.Traffic.Grip<1,"rain fades in, wetness accumulates and traction falls");
            m.SetRain(false);double wet=m.Wetness,rain=m.Rain;for(int i=0;i<40;i++)m.Step(.1);
            Check(m.Rain<rain*.6&&m.Wetness>wet*.95&&m.Wetness<wet,"rain fades out but roads do not instantly dry");
        });
    }
}
