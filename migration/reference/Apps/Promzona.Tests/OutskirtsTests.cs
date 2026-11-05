using Promzona;
using Promzona.Game;
using System.Text.Json;
using System.Reflection;
static partial class Tests {
 static void RunOutskirts(){
 Test("population and morning",()=>{CityLayout.Install(CityLayout.Generate(73921));var m=new CityModel();Check(m.Traffic.Cars.Count(c=>c.TransitRoute==0)==1+m.Traffic.OriginalOtherCars+m.Traffic.InitialPassengerTarget&&m.Traffic.Pedestrians.Count==CityTraffic.InitialPeople,"expanded passenger and pedestrian population");Check(m.Hour==7&&m.CycleMinutes==20,"new game starts at seven with full twenty minute day cycle");
 var c=m.Traffic.Hero;m.Traffic.DamageCar(c,.68);Check(Math.Abs(c.Damage-.34)<1e-12&&!c.Burning,"same damage consumes half the previous health budget");m.Traffic.DamageCar(c,.68);Check(c.Burning,"twice the former threshold starts fire");
 Check(m.Traffic.Props.Any(p=>p.Kind==StreetKind.PhoneBooth)&&m.Traffic.Props.Any(p=>p.Kind==StreetKind.DrinkMachine)&&m.Traffic.Props.Any(p=>p.Kind==StreetKind.Mailbox),"new movable street props are generated");
 Check(CityTraffic.IsBeach(new(610,0))&&!CityTraffic.IsBeach(new(10,0)),"sand is confined to the coast");
 });
 Test("wanted and dispatch",()=>{var t=QuietCity();t.AddCrime(50);Check(t.Wanted==6&&t.WantedLevel==6,"wanted is bounded at six");t.ConsoleWanted(5);
 for(int k=0;k<160;k++)t.Step(.1);var police=t.Services.Where(s=>s.Kind==CityService.Police).ToArray();Check(police.Length==8,"wanted five dispatches eight pursuing units");
 Check(police.All(s=>t.Cars.Any(c=>c.Id==s.CarId&&c.PoliceVehicle)),"dispatch references real police actors");
 var old=t.Cars.Find(c=>c.Id==police[0].CarId)!;old.Destroyed=true;for(int k=0;k<35;k++)t.Step(.1);
 Check(!t.Services.Any(s=>s.CarId==old.Id)&&t.Services.Count(s=>s.Kind==CityService.Police)==8,"destroyed pursuit unit is replaced within its cap");
 typeof(CityTraffic).GetProperty("Wanted")!.SetValue(t,2.9d);t.Step(.1);Check(t.Services.Count(s=>s.Kind==CityService.Police)==5,"lower wanted level releases excess pursuit cars back to traffic");
 var clock=typeof(CityTraffic).GetField("_lastCrime",BindingFlags.NonPublic|BindingFlags.Instance)!;clock.SetValue(t,-100d);
 typeof(CityTraffic).GetProperty("Wanted")!.SetValue(t,.001d);t.Step(.1);Check(t.WantedLevel==0&&t.Services.All(s=>s.Kind!=CityService.Police),"wanted decays to zero and pursuit ends");
 });
 Test("ambulance journey and treatment",()=>{var t=QuietCity();var p=Person(t,-6.5,10);t.HitPerson(p,t.Hero,7);typeof(CityTraffic).GetProperty("Wanted")!.SetValue(t,0d);p.VerticalSpeed=0;p.FlightHeight=0;p.Body.Vx=p.Body.Vy=0;
 ServiceMission? mission=null;CityPoint spawned=default;double travelled=0;int maxMedical=0;bool sawMedics=false;
 for(int k=0;k<5000&&!(!p.Dead&&t.PatientsRevived>0);k++){
  t.Step(.1);maxMedical=Math.Max(maxMedical,t.Services.Count(s=>s.Kind==CityService.Ambulance));
  var m=t.Services.FirstOrDefault(s=>s.Kind==CityService.Ambulance);if(m is not null){if(mission is null){mission=m;spawned=t.Cars.First(c=>c.Id==m.CarId).Position;}
    var c=t.Cars.First(c=>c.Id==m.CarId);travelled=Math.Max(travelled,(c.Position-spawned).Length);sawMedics|=m.Medics.Count>0;
    if(k%500==0)Console.WriteLine($"medical {k*.1:F0}s stage={m.Stage} distance={(c.Position-p.Position).Length:F1} speed={c.Speed:F2} node={c.I}/{c.J} direction={c.Direction}/{c.Outgoing} timer={m.Timer:F1} medics={string.Join(";",m.Medics.Select(id=>t.Pedestrians.FirstOrDefault(a=>a.Id==id)).Where(a=>a is not null).Select(a=>$"{a!.Position} speed{a.Speed:F2} physical{a.Physical} detached{a.Detached} nav{a.NavigationStall:F1} next{a.NextNavigation:F1}"))}");
  }
 }
 Console.WriteLine($"medical result alive={!p.Dead}, travelled={travelled:F1}, time={t.Time:F1}");
 Check(mission is not null&&(spawned-t.Hero.Position).Length>240,"ambulance spawns outside the visible neighborhood");
 Check(travelled>100&&sawMedics&&!p.Dead&&t.PatientsRevived==1,"ambulance drives from afar, medics exit and revive patient");
 Check(maxMedical<=2,"medical response respects two-unit cap");Check(t.Marks.Any(m=>m.Blood),"blood decal remains after on-scene revival");
 });
 Test("police interception",()=>{var t=QuietCity();var hero=t.Hero;Place(hero,-1.9,0);var police=Car(t.World,3,-1.9,-17);police.Hero=false;police.Id=99001;police.I=police.J=16;police.Path=new(new(-1.9,-17),new(-1.9,-11));police.Cruise=12;t.Cars.Add(police);t.Services.Add(new(){CarId=police.Id,Kind=CityService.Police,Destination=hero.Position});t.AddCrime(1);
 double closest=100;for(int k=0;k<120;k++){t.Step(.05);closest=Math.Min(closest,(hero.Position-police.Position).Length);}
 Check(closest<5&&hero.Damage>0,"pursuing officer closes distance and rams player's car physically");
 });
 Test("multiple patients and preserved dispatch",()=>{var m=new CityModel();var t=m.Traffic;foreach(var p in t.Pedestrians.Take(5)){t.HitPerson(p,t.Hero,7);p.Body.Vx=p.Body.Vy=0;}typeof(CityTraffic).GetProperty("Wanted")!.SetValue(t,0d);
 for(int k=0;k<160;k++)m.Step(.1);var medical=t.Services.Where(s=>s.Kind==CityService.Ambulance).ToArray();Check(medical.Length==2&&medical.Select(s=>s.PatientId).Distinct().Count()==2,"multiple casualties dispatch exactly two distinct assignments");
 var state=t.CaptureWorld(m);var restored=new CityModel();restored.Traffic.RestoreWorld(JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(state,CityState.Json),CityState.Json)!,restored);
 Check(restored.Traffic.Services.Count==t.Services.Count&&restored.Traffic.Services.Zip(t.Services).All(x=>x.First.CarId==x.Second.CarId&&x.First.PatientId==x.Second.PatientId),"active service vehicles and patient claims survive save/load");
 });
 Test("world save roundtrip",()=>{var m=new CityModel();for(int k=0;k<5;k++)m.Step(.1);var t=m.Traffic;t.SetMoney(435);t.AddCrime(2.5);t.Hero.Damage=.42;t.Hero.Body.Vy=3;m.SetHour(11.25);m.SetRain(true);
 var victim=t.Pedestrians.First();t.HitPerson(victim,t.Hero,7);var state=t.CaptureWorld(m);string json=JsonSerializer.Serialize(state,CityState.Json);using(var doc=JsonDocument.Parse(json))CityState.ValidateJson(doc.RootElement);
 var read=JsonSerializer.Deserialize<CityWorldSave>(json,CityState.Json)!;var second=new CityModel();second.Traffic.RestoreWorld(read,second);var q=second.Traffic;
 Check(q.Cars.Count==t.Cars.Count&&q.Pedestrians.Count==t.Pedestrians.Count&&q.Animals.Count==t.Animals.Count,"world load preserves all actor counts");
 Check(q.Money==435&&q.Hero.Damage==.42&&q.Hero.Body.Vy==3&&q.Wanted==t.Wanted&&second.Hour==11.25&&second.RainTarget,"world load preserves damage velocity wallet wanted and weather");
 Check(q.Pedestrians.Single(p=>p.Id==victim.Id).Dead&&!q.World.Bodies.Contains(q.Pedestrians.Single(p=>p.Id==victim.Id).Body),"loaded casualty remains non-solid");
 Check(q.Cars.Zip(t.Cars).All(x=>x.First.Position==x.Second.Position&&x.First.Path.Length==x.Second.Path.Length),"routes and actor positions survive save");
 for(int k=0;k<10;k++)second.Step(.1);CheckFinite(q);Check(q.World.Bodies.Count<1500,"loaded world returns to bounded local physics");
 read.Services.Add(new(){CarId=-10,Kind=CityService.Police});bool refused=false;try{CityTraffic.ValidateWorld(read);}catch(InvalidDataException){refused=true;}Check(refused,"broken responder references rejected before loading");
 });
 Test("revived former group member",()=>{var t=new CityTraffic();var g=t.Groups.First();var p=t.Pedestrians.First(p=>p.Id==g.Leader);t.HitPerson(p,t.Hero,7);for(int n=0;n<4;n++)t.Step(.1);t.RevivePatient(p);for(int n=0;n<40;n++)t.Step(.1);Check(!p.Dead&&p.GroupId==-1&&double.IsFinite(p.Position.X+p.Position.Z),"revived former gang member resumes independent walk without stale trail index");});
 Test("overlap cannot starve the frame",()=>{var t=Empty();var a=Car(t.World,0,0,0);var b=Car(t.World,5,.1,.1);a.Hero=true;t.Cars.Add(a);t.Cars.Add(b);
 for(int n=0;n<12;n++){t.Step(.1);Check(t.LastPhysicsSubsteps<=120,"deep overlapping contacts have a bounded substep budget");}
 Check(Math.Abs(t.Time-1.2)<1e-8&&t.Cars.All(c=>double.IsFinite(c.Position.X+c.Position.Z+c.Speed)),"overlap recovery advances simulation time without an adaptive-step stall");
 });
 Test("sand tyre tracks",()=>{var t=Empty();var c=Car(t.World,0,600,0);t.Cars.Add(c);c.Body.Vy=4;for(int n=0;n<20;n++)t.Step(.05);Check(t.Marks.Any(m=>m.Sand&&m.Skid&&m.Life==150),"driving on beach makes persistent tread decals");});
 }
}
