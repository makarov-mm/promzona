using Promzona.Game;
using MakarovPhysics.RigidBodies2D;
static partial class Tests {
 static CityPedestrian Person(CityTraffic t,double x,double z,int persona=0){var p=new CityPedestrian{Id=90000+t.Pedestrians.Count,Position=new(x,z),Persona=persona,Cruise=1.2,Path=new(new(x,z),new(x,z+20))};p.Body=t.World.Bodies[t.World.AddCircle(x,z,.3,75/(Math.PI*.09),.12,.02,0)];p.Body.InvI=0;t.Pedestrians.Add(p);return p;}
 static void RunLivingCity(){
 Test("contact timestep tail stability",()=>{CityLayout.Install(CityLayout.Generate(73921));var t=new CityTraffic();double peak=0;int largest=0;for(int i=0;i<180;i++){t.Step(1d/60);double speed=t.World.Bodies.Max(b=>Math.Sqrt(b.Vx*b.Vx+b.Vy*b.Vy));peak=Math.Max(peak,speed);largest=Math.Max(largest,t.World.Bodies.Count);if(!double.IsFinite(peak)||peak>35)break;}Check(peak<35,"small frame remainder does not launch walkers at hundreds of metres per second");Check(largest<8000,"contact streaming remains local after adaptive substeps");});
 Test("living city fear and recovery",()=>{var t=Empty();var c=Car(t.World,0,6.5,10);t.Cars.Add(c);var p=Person(t,6.5,20);var tough=Person(t,4.7,20,3);c.Body.Vy=12;t.Step(.12);t.Step(.05);
 Check(p.Fear>.2&&tough.Fear>0&&tough.Fear<p.Fear,"fast approaching vehicle scares ordinary people more than tough group members");
 // Park on a clear remote carriageway; the old (100,100) fixture was inside a building.
 Check(!p.Dead&&!tough.Dead,"fear decay fixture retains living witnesses outside the initial impact path");
 Place(c,-1.9,-200);t.SpawnDistance=5000;for(int i=0;i<100;i++)t.Step(.1,new(0,0,true));Check(p.Fear==0&&tough.Fear==0,"fear decays without continued pursuit");});
 Test("non solid casualties and witnesses",()=>{var t=Empty();var c=Car(t.World);t.Cars.Add(c);var p=Person(t,0,2);var witness=Person(t,6,7);t.HitPerson(p,c,12);
 Check(p.Dead&&p.Detached&&!t.World.Bodies.Contains(p.Body),"fatal pedestrian removed immediately from collision solver");
 for(int i=0;i<3;i++)t.Step(.1);Check(witness.Fear>.5,"nearby witness first flees violent incident");
 Check(t.Incidents.Count==1,"incident stored once with bounded lifetime");
 c.Body.Vy=5;for(int i=0;i<60;i++)t.Step(1d/60,new(1,0));Check(c.Position.Z>3,"player drives through nonphysical body");});
 Test("fire explosion and clinic respawn",()=>{var t=Empty();var c=Car(t.World);t.Cars.Add(c);t.DamageCar(c,1.6);Check(c.Burning&&!c.Destroyed,"damage threshold starts vehicle fire");
 t.DamageCar(c,1.8);Check(c.FireAge>10,"additional damage accelerates explosion");for(int i=0;i<20;i++)t.Step(.1);
 Check(c.Destroyed&&!c.Body.IsStatic&&c.Body.InvMass==1/c.Mass&&!c.Burning,"explosion leaves solid movable charred wreck with vehicle mass");
 // Inspect the wreck before its six-second distant-cleanup window.
 for(int i=0;i<20;i++)t.Step(.1);Check(t.Respawns==1&&t.OnFoot&&t.PlayerHealth==1,"player respawns healthy on foot");Check(t.Cars.Contains(c)&&c.Destroyed&&!c.Body.IsStatic,"player wreck persists as a movable road obstacle until distant cleanup");
 Check(CityTraffic.ClinicBlocks.Any(b=>(t.PlayerPosition-new CityPoint(CityTraffic.Coordinate(b.I),CityTraffic.Coordinate(b.J))).Length<40),"respawn is beside one of four clinics");});
 Test("water death",()=>{var t=Empty();var c=Car(t.World,0,CityTraffic.LandEdge+1,0);t.Cars.Add(c);t.Step(.1);Check(c.Falling&&!t.World.Bodies.Contains(c.Body),"leaving island removes ground contact");
 for(int i=0;i<15;i++)t.Step(.1);Check(c.SurfaceHeight+c.Lift<CityWater.Level-.3,"car visibly sinks below water level");for(int i=0;i<20;i++)t.Step(.1);Check(t.Respawns==1&&t.OnFoot&&!t.PlayerUnavailable,"water death respawns player");});
 Test("group survivor and flight",()=>{var t=new CityTraffic(false);var g=t.Groups.First();int size=g.Members.Count;var victim=t.Pedestrians.First(p=>p.Id==g.Members[0]);t.HitPerson(victim,t.Hero,15);for(int i=0;i<3;i++)t.Step(.1);
 Check(g.Members.Count==size-1&&g.Members.All(id=>t.Pedestrians.Any(p=>p.Id==id&&!p.Dead)),"group retains living members after leader death");
 var bird=t.Animals.First(a=>a.Kind==AnimalKind.Bird);bird.FlyAt=0;t.Step(.1);Check(bird.Flying,"ordinary bird starts a neighborhood flight");
 for(int i=0;i<80;i++)t.Step(.1);Check(bird.Height>1&&bird.Body is null,"flying bird releases planar body");});
 Test("overtaking incident",()=>{var t=Empty();var hero=Car(t.World,0,180,0);t.Cars.Add(hero);var c=Car(t.World,0,-1.9,-29);c.Hero=false;c.Id=2;c.Cruise=4;c.I=c.J=16;c.Direction=c.Outgoing=0;c.Path=new(new(-1.9,-29),new(-1.9,-11));t.Cars.Add(c);
 var p=Person(t,-1.9,-20);t.HitPerson(p,hero,12);double maxX=-2,minGap=100;
 for(int i=0;i<1800;i++){t.Step(1d/120);maxX=Math.Max(maxX,c.Position.X);minGap=Math.Min(minGap,(c.Position-p.Position).Length);}
 Console.WriteLine($"bypass x={maxX:F2} z={c.Position.Z:F2} minGap={minGap:F2} offset={c.OvertakeOffset:F2}");Check(maxX>.5&&c.Position.Z>-17,"AI moves into clear oncoming lane and passes casualty");Check(minGap>1.1,"AI avoids driving over casualty");});
 Test("wet tires",()=>{var t=Empty();var c=Car(t.World);t.Cars.Add(c);t.Puddles.Clear();t.Puddles.Add(new(0,0));c.Body.Vy=4;for(int i=0;i<30;i++)t.Step(1d/120);Check(c.WetTires>0&&t.Marks.Any(m=>m.Wet&&m.Skid),"puddle leaves fading wet tire marks");});
 Test("remote replenishment",()=>{var t=new CityTraffic(false);int max=t.Cars.Max(c=>c.Id);var victim=t.Cars.First(c=>!c.Hero&&(c.Position-t.Hero.Position).Length>300);t.DamageCar(victim,2);t.DamageCar(victim,2);for(int i=0;i<80;i++)t.Step(.1);
 Check(!t.Cars.Contains(victim)&&!t.World.Bodies.Contains(victim.Body),"distant burnt wreck is removed from registry and physics");Check(t.Cars.Where(c=>c.Id>max).All(c=>(c.Position-t.Hero.Position).Length>200),"replacement vehicles stay outside player's neighborhood");});
 Test("traffic island graph",()=>{for(int i=0;i<CityTraffic.Nodes;i++){Check(!CityTraffic.HasRoad(i,0,2)&&!CityTraffic.HasRoad(i,CityTraffic.Nodes-1,0)&&!CityTraffic.HasRoad(0,i,3)&&!CityTraffic.HasRoad(CityTraffic.Nodes-1,i,1),"no road graph edges go into sea");}
 Check(CityTraffic.ClinicBlocks.All(b=>!CityModel.ParkBlock(b.I,b.J)),"clinics have buildings instead of park plots");});
 }
}
