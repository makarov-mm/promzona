using Promzona.Game;
static partial class Tests {
 static void RunRecovery(){
 CityLayout.Install(CityLayout.Generate(73921));
 Test("health and death share boundary",()=>{
  for(int kind=0;kind<6;kind++){
   var t=Empty();var h=Car(t.World,kind,-1.9,0);t.Cars.Add(h);t.DamageCar(h,1.98);
   Check(h.Burning&&!h.Destroyed&&Math.Abs(h.HealthRemaining-.01)<1e-10,$"type {kind}: critical car retains positive health");
   t.DamageCar(h,.021);Check(h.Destroyed&&h.HealthRemaining==0,$"type {kind}: final hit destroys at zero health");
  }
  var one=Empty();one.Cars.Add(Car(one.World));one.DamageCar(one.Hero,2.1);Check(one.Hero.Destroyed,"first severe impact destroys without requiring previous fire");
  var old=Empty();old.Cars.Add(Car(old.World));old.Hero.Damage=1.1;old.Hero.Burning=true;old.Hero.SafeDrivingTime=19;Call(old,"VehicleLifeBefore",.01);
  Check(old.Hero.Destroyed,"old save zero-health vehicle cannot extinguish and survive");
 });
 Test("death ends old pursuit and attribution",()=>{
  var t=Empty();t.Cars.Add(Car(t.World,0,-1.9,0));var police=Npc(t,3,-1.9,-25);var ambulance=Npc(t,4,40,0);
  t.Services.Add(new(){CarId=police.Id,Kind=CityService.Police,Stage=1});t.Services.Add(new(){CarId=ambulance.Id,Kind=CityService.Ambulance});
  t.AddCrime(5);police.PlayerDamageUntil=50;var armed=Person(t,15,0);armed.AggressionUntil=100;armed.Aiming=true;
  t.DamageCar(t.Hero,3);Check(t.Hero.Destroyed&&t.PoliceBudget==0,"destroyed player has no pursuit budget");
  Call(t,"RespawnPlayer");Check(t.Respawns==1&&t.OnFoot&&t.PlayerHealth==1&&t.WantedLevel==0,"clinic respawn restores health and clears wanted");
  Check(t.Services.Count==1&&t.Services[0].Kind==CityService.Ambulance,"respawn cancels police missions but preserves medical service");
  Check(t.Cars.All(c=>c.PlayerDamageUntil==0)&&!armed.Aiming&&armed.AggressionUntil==0,"old life damage attribution and retaliation expire");
  Call(t,"ServicesStep",.1);Check(t.Wanted==0&&!t.Services.Any(m=>m.Kind==CityService.Police),"police does not redispatch after respawn");
  t.AddCrime(.2);Check(t.WantedLevel==1,"fresh crimes create a new pursuit");
  t.Hero.Falling=true;Call(t,"RespawnPlayer");Check(t.Respawns==2&&t.Wanted==0,"water death also clears wanted");
 });
 Test("critical player is destroyed by physical police ram",()=>{
  var t=Empty();t.SpawnDistance=5000;var hero=Car(t.World,0,-1.9,0);t.Cars.Add(hero);hero.Damage=.999;hero.Burning=true;
  var police=Npc(t,3,-1.9,-17);police.I=police.J=16;police.Path=new(police.Position,new(-1.9,-11));police.Cruise=23;
  t.Services.Add(new(){CarId=police.Id,Kind=CityService.Police,Destination=hero.Position});t.AddCrime(1);
  for(int k=0;k<360&&!hero.Destroyed;k++)t.Step(1d/60,new(0,0,true));
  Check(hero.Destroyed&&hero.HealthRemaining==0,"resolved police collision destroys critical player without direct test damage call");
 });
 Test("wanted budgets",()=>{
  int[] expected=[0,2,3,5,6,8];
  for(int level=0;level<=5;level++){var t=Empty();t.Cars.Add(Car(t.World));t.ConsoleWanted(level);Check(t.PoliceBudget==expected[level],$"wanted {level} has {expected[level]} pursuit slots");}
 });
 Test("cash collection feedback",()=>{
  var t=Empty();t.Cars.Add(Car(t.World));t.Cash.Add(new(){Position=t.Hero.Position,Created=0,Amount=25});Call(t,"LifeAfterContacts",.01);
  Check(t.Money==0&&!t.Sounds.Any(s=>s.Name=="cash"),"fresh cash cannot be collected before delay");
  typeof(CityTraffic).GetProperty("Time")!.SetValue(t,2d);t.Cash.Add(new(){Position=t.Hero.Position,Created=0,Amount=50});Call(t,"LifeAfterContacts",.01);
  Check(t.Money==75&&t.Cash.Count==0&&t.Sounds.Count(s=>s.Name=="cash"&&s.Ui)==1,"multi pickup credits wallet and emits one centered chime");
  Call(t,"LifeAfterContacts",.01);Check(t.Sounds.Count(s=>s.Name=="cash")==1,"collected cash cannot sound twice");
  t.Hero.Destroyed=true;t.Cash.Add(new(){Position=t.Hero.Position,Amount=25,Created=0});Call(t,"LifeAfterContacts",.01);Check(t.Money==75,"wreck cannot collect money");
 });
 Test("embedded pedestrian recovery",()=>{
  var b=CityModel.Blocks.First(b=>b.HalfX>3&&b.HalfZ>3);var t=Empty();t.Cars.Add(Car(t.World,0,b.X-20,b.Z));
  var p=Person(t,b.X+b.HalfX-.2,b.Z);p.GroupId=-1;Call(t,"RebuildActorGrid");Call(t,"RecoverWalkerClearance",p,.01);
  Check(Math.Abs(p.Position.X-b.X)>b.HalfX+.4||Math.Abs(p.Position.Z-b.Z)>b.HalfZ+.4,"embedded standing pedestrian moves to free facade edge");
  Check(p.Body.Vx==0&&p.Body.Vy==0&&p.Position.X==p.Body.X,"penetration repair creates no launch velocity or pose desync");
  var bus=Npc(t,5,6.5,12);p.Position=bus.Position;p.Body.X=6.5;p.Body.Y=12;typeof(CityTraffic).GetProperty("Time")!.SetValue(t,1d);Call(t,"RebuildActorGrid");Call(t,"RecoverWalkerClearance",p,.01);
  Check(!CityTraffic.CarPersonOverlap(bus.Position,bus.Heading,p.Position,.10,bus.HalfLength,bus.HalfWidth),"standing pedestrian escapes stationary bus footprint");
  p.Position=new(bus.Position.X-bus.HalfWidth-.45,bus.Position.Z);p.Body.X=p.Position.X;p.Body.Y=p.Position.Z;var before=p.Position;
  typeof(CityTraffic).GetProperty("Time")!.SetValue(t,1.5d);Call(t,"RecoverWalkerClearance",p,.01);Check(p.Position==before,"clear pedestrian beside bus is not teleported by recovery margin");
  p.Dead=true;p.Position=bus.Position;p.Body.X=6.5;p.Body.Y=12;typeof(CityTraffic).GetProperty("Time")!.SetValue(t,2d);Call(t,"RecoverWalkerClearance",p,.01);Check(p.Position==bus.Position,"dead actor is never resurrected or ejected by walker recovery");
 });
 }
}
