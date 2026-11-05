using Promzona.Game;
using System.Reflection;
static partial class Tests {
 static object? Call(CityTraffic t,string name,params object[] args)=>typeof(CityTraffic).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(t,args);
 static CityCar Npc(CityTraffic t,int kind,double x,double z){var c=Car(t.World,kind,x,z);c.Hero=false;c.Id=100000+t.Cars.Count;c.Path=new(c.Position,c.Position+new CityPoint(0,40));c.Cruise=0;t.Cars.Add(c);return c;}
 static void Tick(CityTraffic t,double seconds,DriveInput input=default){for(int i=0;i<(int)Math.Round(seconds*30);i++)t.Step(1d/30,input);}
 static void RunBehavior(){
 CityLayout.Install(CityLayout.Generate(73921));
 Test("explosion area and chain",()=>{
  var t=Empty();t.Cars.Add(Car(t.World,0,80,0));var source=Npc(t,0,0,0);var near=Npc(t,0,2.5,0);var far=Npc(t,0,8,0);var outside=Npc(t,0,11,0);
  var victim=Person(t,0,5);var safe=Person(t,0,8);var witness=Person(t,0,18);int revision=victim.VoiceRevision;
  Call(t,"Explode",source);
  Check(near.Burning&&near.Damage>far.Damage&&far.Damage>0&&outside.Damage==0,"blast damages neighboring vehicles with bounded distance falloff and ignites closest");
  Check(victim.Dead&&!safe.Dead&&victim.Detached&&!t.World.Bodies.Contains(victim.Body),"blast kills nearby pedestrian and removes contact body");
  Check(victim.VoiceRevision>revision&&!victim.CanSpeak,"blast immediately invalidates speech of victim");
  Check(witness.Fear>.5,"violent incident scares nearby witness even without threatening driving");
  double damage=far.Damage;Call(t,"Explode",source);Check(far.Damage==damage,"wreck cannot apply its explosion twice");
  near.FireAge=12.1;Call(t,"VehicleLifeBefore",.02);Check(near.Destroyed,"ignited NPC continues into a delayed chain explosion");
 });
 Test("solid buildings shield blast",()=>{
  var block=CityModel.Blocks.First(b=>b.HalfX<3.5&&b.HalfZ>1);var t=Empty();t.Cars.Add(Car(t.World,0,80,0));
  var source=Npc(t,0,block.X-block.HalfX-.3,block.Z);var shielded=Person(t,block.X+block.HalfX+.3,block.Z);var car=Npc(t,0,shielded.Position.X,shielded.Position.Z);
  Check((source.Position-shielded.Position).Length<7.5,"occlusion fixture lies within fatal blast radius");Call(t,"Explode",source);
  Check(!shielded.Dead&&car.Damage==0,"solid facade occludes blast damage instead of hitting through building");
 });
 Test("recoverable player fire",()=>{
  var t=Empty();var hero=Car(t.World,0,-1.9,0);t.Cars.Add(hero);t.DamageCar(hero,1.6);
  for(int i=0;i<600;i++)Call(t,"VehicleLifeBefore",.05);
  Check(hero.Burning&&!hero.Destroyed,"player fire has no automatic 12 second explosion");
  hero.Speed=8;hero.LastDamageAt=-100;double damage=hero.Damage;
  for(int i=0;i<370;i++)Call(t,"VehicleLifeBefore",.05);
  Check(!hero.Burning&&!hero.Destroyed&&hero.Damage==damage,"clean driving extinguishes player fire without repairing damage");
  t.DamageCar(hero,.1);hero.SafeDrivingTime=10;t.DamageCar(hero,.1);Check(hero.SafeDrivingTime==0,"new collision resets extinguishing progress");
  t.DamageCar(hero,1);Check(hero.Destroyed,"further severe impact can still destroy burning player car");
  var npc=Npc(t,0,25,0);t.DamageCar(npc,1.6);for(int i=0;i<250;i++)Call(t,"VehicleLifeBefore",.05);Check(npc.Destroyed,"non-player burning car retains timed explosion");
 });
 Test("road driving is not an attack",()=>{
  var t=Empty();var hero=Car(t.World,0,-1.9,-4);hero.Body.Vy=12;t.Cars.Add(hero);var p=Person(t,-3.7,4);
  Check(CityTraffic.OnCarriageway(hero.Position)&&!CityTraffic.OnCarriageway(new(6.5,10)),"carriageway classification separates road and pavement");
  Call(t,"RebuildActorGrid");Call(t,"SocialStep",.2);Check(p.Fear==0&&p.AggressionUntil==0,"normal road driving creates no pedestrian panic or retaliation");
  Place(hero,6.5,10,0,12);p.Position=new(6.5,18);p.Body.X=6.5;p.Body.Y=18;Call(t,"RebuildActorGrid");Call(t,"SocialStep",.2);
  Check(p.Fear>.25,"approaching car on pavement triggers fear");
 });
 Test("public-step clean driving extinguishes fire",()=>{
  var t=Empty();t.SpawnDistance=5000;var c=Car(t.World,0,-1.9,-250);t.Cars.Add(c);t.DamageCar(c,1.6);c.Body.Vy=c.Speed=8;
  Tick(t,24,new(.2,0));Check(!c.Burning&&!c.Destroyed&&c.Damage==.8&&c.Distance>100,"real physics steps extinguish fire during sustained undamaged driving");
 });
 Test("walk around stopped street object",()=>{
  var t=Empty();t.Cars.Add(Car(t.World,0,80,0));var p=Person(t,6.5,10);var obstacle=new StreetProp{Id=999999,Home=new(6.5,15),Kind=StreetKind.PhoneBooth};
  obstacle.Body=t.World.Bodies[t.World.AddBox(6.5,15,.7,.7,0,.3,0,0)];t.Props.Add(obstacle);var goal=new CityPoint(6.5,22);
  for(int k=0;k<2400;k++){
   typeof(CityTraffic).GetProperty("Time")!.SetValue(t,k/120d);Call(t,"RebuildActorGrid");var d=goal-p.Position;Call(t,"MovePerson",p,d*(1.2/Math.Max(.1,d.Length)),1d/120,goal);t.World.Step(1d/120);p.Position=new(p.Body.X,p.Body.Y);
  }
  Console.WriteLine($"walker detour ends {p.Position}");Check(p.Position.Z>20&&!p.Dead&&p.KnockedFor==0,"local pedestrian detours around phone booth and continues along pavement");
 });
 Test("police ignores traffic permissions",()=>{
  var t=Empty();var hero=Car(t.World,0,-1.9,0);t.Cars.Add(hero);var c=Npc(t,3,-1.9,-60);c.I=c.J=16;c.Direction=c.Outgoing=0;c.Path=new(c.Position,new(-1.9,-11));c.Cruise=20;
  t.Services.Add(new(){CarId=c.Id,Kind=CityService.Police,Destination=hero.Position});t.AddCrime(1);
  var reservations=(int[])typeof(CityTraffic).GetField("_reservations",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(t)!;reservations[16+16*CityTraffic.Nodes]=999;
  Check(!t.Green(16,16,0),"pursuit fixture begins on red");Tick(t,4,new(0,0,true));
  Check(c.Position.Z> -45&&t.Services.Single(m=>m.CarId==c.Id).Stage==1,"police starts interception beyond 23m despite red light and occupied reservation");
 });
 Test("pigeon flock cohesion",()=>{
  var t=new CityTraffic();var flocks=t.Animals.Where(a=>a.Kind==AnimalKind.Pigeon&&(a.Position-t.Hero.Position).Length>180).GroupBy(a=>a.Flock).Take(10).Select(g=>g.Select(a=>a.Id).ToArray()).ToArray();
  t.Pedestrians.Clear();t.Groups.Clear();foreach(var a in t.Animals.Where(a=>a.Kind!=AnimalKind.Pigeon).ToArray()){if(a.Body is {} b)t.World.RemoveBody(t.World.Bodies.IndexOf(b));t.Animals.Remove(a);}
  // Far actors have the same feeding targets without allocating contact bodies.
  for(int k=0;k<1800;k++){typeof(CityTraffic).GetProperty("Time")!.SetValue(t,t.Time+1d/30);Call(t,"MaintainFlocks",1d/30);foreach(var ids in flocks)foreach(int id in ids){var a=t.Animals.First(a=>a.Id==id);Call(t,"FarAnimal",a,1d/30);a.Position=new(a.Body!.X,a.Body.Y);}}
  double diameter=flocks.Max(ids=>ids.Select(id=>t.Animals.First(a=>a.Id==id)).Max(a=>ids.Max(id=>(a.Position-t.Animals.First(b=>b.Id==id).Position).Length)));
  Console.WriteLine($"flocks after 60s: {flocks.Length}, max diameter {diameter:F3}m");Check(diameter<5,"pigeons keep compact flocks after a minute instead of independent routes");
  var flock=flocks[0].Select(id=>t.Animals.First(a=>a.Id==id)).ToArray();flock[0].FearFrom=flock[0].Position+new CityPoint(2,0);Call(t,"LaunchFlock",flock[0]);Check(flock.All(a=>a.Flying)&&flock.Max(a=>a.Heading)-flock.Min(a=>a.Heading)<.5,"a threat launches the entire flock in a common escape direction");
 });
 Test("old dispersed flock migration",()=>{
  var t=new CityTraffic();var flock=t.Animals.Where(a=>a.Kind==AnimalKind.Pigeon&&(a.Position-t.Hero.Position).Length>300).GroupBy(a=>a.Flock).First(g=>g.Count()>=5).ToArray();
  var original=flock[0].Position;for(int k=0;k<flock.Length;k++){var a=flock[k];a.FlockHome=default;a.Position=original+new CityPoint(k*4,0);a.Body!.X=a.Position.X;a.Body.Y=a.Position.Z;}
  Call(t,"MaintainFlocks",1d);Check(flock.All(a=>!a.FlockNeedsGather)&&flock.Max(a=>(a.Position-flock[0].Position).Length)<3,"old dispersed flock regroups compactly when entirely offscreen");
  for(int k=0;k<flock.Length;k++){flock[k].FlockHome=default;flock[k].WanderAt=0;}
  Place(t.Hero,flock[0].Position.X,flock[0].Position.Z-15);var visible=flock.Select(a=>a.Position).ToArray();Call(t,"MaintainFlocks",1d);
  Check(flock.Select(a=>a.Position).SequenceEqual(visible)&&flock.All(a=>a.FlockNeedsGather),"old flock migration does not teleport visible birds");
 });
 Test("dense pavement progress",()=>{
  var t=new CityTraffic();foreach(var p in t.Pedestrians)p.AggressionUntil=0;
  var start=t.Pedestrians.ToDictionary(p=>p.Id,p=>p.Position);var distances=t.Pedestrians.ToDictionary(p=>p.Id,p=>0d);var previous=new Dictionary<int,CityPoint>(start);
  for(int k=0;k<900;k++){t.Step(1d/30);foreach(var p in t.Pedestrians)if(previous.TryGetValue(p.Id,out var at)){distances[p.Id]+=(p.Position-at).Length;previous[p.Id]=p.Position;}}
  var living=t.Pedestrians.Where(p=>!p.Dead&&start.ContainsKey(p.Id)).ToArray();double local=living.Where(p=>p.Physical).Average(p=>distances[p.Id]);int moving=living.Count(p=>distances[p.Id]>3),still=living.Count(p=>distances[p.Id]<.5);
  Console.WriteLine($"walkers30s: {moving}/{living.Length} >3m, {still} <.5m; local mean {local:F2}m");
  Check(moving>living.Length*.90&&still<living.Length*.04,"dense pavement population progresses instead of corner reservation deadlock");Check(local>10,"local walking also progresses with physical separation");
 });
 Test("police active pursuit",()=>{
  var t=new CityTraffic();t.ConsoleWanted(CityTraffic.MaxWantedLevel);var heroAt=t.Hero.Position;double nearest=9999;int maxPolice=0;double start=0;
  for(int k=0;k<1800;k++){t.Step(1d/30,new(0,0,true));if(k%60==0)t.AddCrime(.01);var cars=t.Services.Where(m=>m.Kind==CityService.Police).Select(m=>t.Cars.First(c=>c.Id==m.CarId)).ToArray();maxPolice=Math.Max(maxPolice,cars.Length);if(k==150)start=cars.Length>0?cars.Min(c=>(c.Position-t.Hero.Position).Length):9999;if(k>150&&cars.Length>0)nearest=Math.Min(nearest,cars.Min(c=>(c.Position-t.Hero.Position).Length));}
  Console.WriteLine($"police60s: max {maxPolice}, distance at5s {start:F2}m, closest {nearest:F2}m, damage {t.Hero.Damage:F3}");
  Check(maxPolice==CityTraffic.MaxPoliceUnits,"maximum wanted level fields nine active pursuit units");Check(nearest<8,"police reaches player in dense city without player searching for it");
 });
 }
}

