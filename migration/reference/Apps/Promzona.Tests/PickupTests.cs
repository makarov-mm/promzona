using System.Reflection;
using System.Text.Json;
using Promzona.Game;
static partial class Tests {
 static void RunPickups(){
  CityLayout.Install(CityLayout.Generate(73921));
  CityTraffic Sandbox(){var t=Empty();t.Pickups.Clear();t.Props.Clear();t.Cars.Add(Car(t.World));return t;}
  void Clock(CityTraffic t,double time)=>typeof(CityTraffic).GetProperty("Time")!.SetValue(t,time);
  void Shoot(CityTraffic t,double time){Clock(t,time);Call(t,"RebuildActorGrid");Call(t,"PlayerCombat");}
  Test("extensible inventory",()=>{
   var inv=new CityInventory();Check(inv.Active is null,"empty inventory has no active weapon");
   inv.Give(CityWeaponId.Pistol);Check(inv.Ammo(CityWeaponId.Pistol)==30&&inv.Active?.Id==CityWeaponId.Pistol,"first pistol gives thirty rounds");
   inv.Give(CityWeaponId.Pistol);Check(inv.Ammo(CityWeaponId.Pistol)==60,"repeated pistol stacks thirty rounds");
   inv.Give(CityWeaponId.Automatic);Check(inv.Active?.Id==CityWeaponId.Automatic&&inv.Ammo(CityWeaponId.Automatic)==30,"strongest loaded weapon is selected");
   inv.Give(CityWeaponId.Pistol);Check(inv.Active?.Id==CityWeaponId.Automatic&&inv.Ammo(CityWeaponId.Pistol)==90,"weaker pickup does not replace loaded automatic");
   for(int n=0;n<30;n++)inv.Consume(CityWeaponId.Automatic);Check(inv.Active?.Id==CityWeaponId.Pistol,"empty automatic falls back to loaded pistol");
   Check(!inv.Consume(CityWeaponId.Automatic)&&inv.Ammo(CityWeaponId.Automatic)==0,"ammunition cannot become negative");
   for(int n=0;n<90;n++)inv.Consume(CityWeaponId.Pistol);Check(inv.Active is null,"no loaded weapon remains after exhaustion");
   inv.Give(CityWeaponId.Automatic);Check(inv.Active?.Id==CityWeaponId.Automatic,"recovered automatic ammo reselects it");
   Check(!inv.Give((CityWeaponId)99),"unknown weapon is rejected");
   var copy=new CityInventory();copy.Restore(inv.Capture());Check(copy.Ammo(CityWeaponId.Automatic)==30,"inventory snapshot restores rounds");
   bool rejected=false;try{copy.Restore(new(){[CityWeaponId.Pistol]=-1});}catch(InvalidDataException){rejected=true;}Check(rejected,"negative saved ammo rejected");
  });
  Test("medicine and pickup lifecycle",()=>{
   var t=Sandbox();t.Player.GunDamage=.9;t.Hero.Damage=.9;t.Hero.Burning=true;t.Pickups.Add(new(){Id=1,Kind=CityPickupKind.Pills,Position=t.Hero.Position,Created=-2});Call(t,"PickupStep",.01);
   Check(Math.Abs(t.Player.GunDamage-.55)<1e-9&&t.Hero.Damage==.9&&t.Hero.Burning,"medicine heals driver without repairing or extinguishing vehicle");
   Check(t.Pickups.Count==0&&t.Sounds.Count(s=>s.Name=="heal"&&s.Ui)==1,"medicine is removed once with centered pickup sound");
   t.Player.GunDamage=0;t.Hero.Damage=0;t.Pickups.Add(new(){Id=2,Kind=CityPickupKind.Pills,Created=-2});Call(t,"PickupStep",.01);Check(t.Pickups.Count==1,"full health does not waste medicine");
   t.Hero.Damage=1;Call(t,"PickupStep",.01);Check(t.Pickups.Count==1&&t.Hero.Damage==1,"medicine cannot resurrect zero-health car");
   t.Player.GunDamage=.1;t.Hero.Damage=.1;Call(t,"PickupStep",.01);Check(t.Player.GunDamage==0&&t.Hero.Damage==.1,"healing clamps driver health and leaves vehicle unchanged");
   t.Pickups.Add(new(){Id=3,Kind=CityPickupKind.Weapon,Weapon=CityWeaponId.Pistol,Created=t.Time});Call(t,"PickupStep",.01);Check(t.Inventory.Active is null,"fresh dropped weapon has one second pickup delay");
   Clock(t,1.1);Call(t,"PickupStep",.01);Check(t.Inventory.Ammo(CityWeaponId.Pistol)==30,"delayed weapon collects on overlap");
   t.Hero.Destroyed=true;t.Pickups.Add(new(){Id=4,Kind=CityPickupKind.Weapon,Weapon=CityWeaponId.Automatic,Created=-2});Call(t,"PickupStep",.01);Check(t.Inventory.Ammo(CityWeaponId.Automatic)==0,"wreck cannot collect weapons");
   t.Hero.Destroyed=false;t.Hero.Lift=2;Call(t,"PickupStep",.01);Check(t.Pickups.Count==1,"airborne car does not collect ground items");
   Clock(t,400);t.Pickups[0].Position=new(500,500);Call(t,"PickupStep",.01);Check(t.Pickups.Count==0,"old distant dropped weapon is cleaned up");
  });
  Test("balanced map supplies",()=>{
   var t=new CityTraffic();Check(t.Pickups.Count(p=>p.Kind==CityPickupKind.Pills)==CityTraffic.PillTarget,"city starts with balanced medicine supply");
   Check(t.Pickups.Count(p=>p.Kind==CityPickupKind.Weapon)==CityTraffic.MapWeaponTarget&&CityTraffic.MapWeaponTarget<CityTraffic.PillTarget,"map weapons are rarer than medicine");
   Check(t.Pickups.Where(p=>p.Kind==CityPickupKind.Weapon).Select(p=>p.Weapon).Distinct().Count()==4,"all four weapons are discoverable on map");
   Check(t.Pickups.All(p=>t.ClearSight(p.Position,p.Position+new CityPoint(.01,0))&&Math.Abs(p.Position.X)<CityTraffic.LandEdge),"map items stay on land");
   var positions=t.Pickups.Select(p=>p.Position).ToArray();t.Pickups.RemoveAll(p=>p.Kind==CityPickupKind.Pills);for(int n=1;n<=10;n++){Clock(t,n*4);Call(t,"PickupStep",4d);}
   Check(t.Pickups.Count(p=>p.Kind==CityPickupKind.Pills)==CityTraffic.PillTarget,"bulk-removed medicine is replenished within bounded refill cycles");
   Check(t.Pickups.Where(p=>p.Kind==CityPickupKind.Pills).All(p=>(p.Position-t.Hero.Position).Length>t.SpawnDistance&&!positions.Contains(p.Position)),"replacement medicine appears at different out-of-view positions");
   int count=t.Pickups.Count;Call(t,"PickupStep",.1);Check(t.Pickups.Count==count,"population does not grow beyond balance");
  });
  Test("weapon drops belong to NPC",()=>{
   var t=Sandbox();var p=Person(t,0,10);p.Id=90010;Check(p.Weapon==CityWeaponId.Automatic,"some armed NPC carry automatic");
   t.HitPerson(p,t.Hero,9);Check(p.Dead&&t.Pickups.Count==1&&t.Pickups[0].Weapon==CityWeaponId.Automatic,"fatal hit drops NPC actual weapon");
   t.HitPerson(p,t.Hero,9);Call(t,"FatalPerson",p,true);Check(t.Pickups.Count==1,"repeat death processing cannot duplicate drop");
   p.Dead=false;p.KnockedFor=0;Check(!p.Armed,"revived NPC has lost dropped weapon");Call(t,"FatalPerson",p,true);Check(t.Pickups.Count==1,"revival farming cannot duplicate same weapon");
   var unarmed=Person(t,2,10);unarmed.Id=90001;t.HitPerson(unarmed,t.Hero,9);Check(t.Pickups.Count==1,"unarmed NPC has no weapon drop");
  });
  Test("player firing and first hit",()=>{
   var t=Sandbox();t.Inventory.Give(CityWeaponId.Pistol);t.PlayerFireHeld=true;var target=Car(t.World,0,0,12);target.Hero=false;target.Id=99;t.Cars.Add(target);var p=Person(t,0,20);
   Shoot(t,1);Check(t.PlayerShotsFired==1&&t.Inventory.Ammo(CityWeaponId.Pistol)==29&&target.Damage>0,"player fire consumes a round and damages vehicle ahead");
   Check(p.GunDamage==0,"vehicle blocks person behind it");Shoot(t,1.1);Check(t.PlayerShotsFired==1,"rate limit prevents duplicate substep shots");
   t.PlayerFireHeld=false;Shoot(t,2);Check(t.PlayerShotsFired==1,"released trigger stops fire");
   t.PlayerFireHeld=true;t.Hero.Destroyed=true;Shoot(t,3);Check(t.PlayerShotsFired==1,"destroyed player cannot fire");
   Check(t.Wanted>0,"firing creates a crime");
   Check(CityTraffic.RayBox(default,new(0,1),new(0,10),0,1,2,30)==8,"ray intersects actual vehicle front face");
   Check(double.IsPositiveInfinity(CityTraffic.RayBox(default,new(0,1),new(5,10),0,1,2,30)),"parallel ray outside box misses");
  });
  Test("shots respect walls and wrecks",()=>{
   var t=Sandbox();var b=CityModel.Blocks.First(b=>b.Height>4);Place(t.Hero,b.X-b.HalfX-3,b.Z);t.Hero.Heading=Math.PI/2;
   var target=Car(t.World,0,b.X+b.HalfX+3,b.Z);target.Hero=false;target.Id=99;t.Cars.Add(target);t.Inventory.Give(CityWeaponId.Pistol);t.PlayerFireHeld=true;Shoot(t,1);
   Check(target.Damage==0&&t.Shots.Last().To.X<=b.X-b.HalfX+.001,"wall stops bullet before vehicle beyond facade");
   t=Sandbox();t.Inventory.Give(CityWeaponId.Pistol);t.PlayerFireHeld=true;var wreck=Car(t.World,0,0,8);wreck.Hero=false;wreck.Destroyed=true;wreck.Id=99;t.Cars.Add(wreck);target=Car(t.World,0,0,16);target.Hero=false;target.Id=100;t.Cars.Add(target);Shoot(t,1);Check(target.Damage==0,"wreck remains solid cover from bullets");
  });
  Test("NPC automatic short bursts",()=>{
   var t=Sandbox();Place(t.Hero,0,14);var p=Person(t,0,0);p.Id=90010;p.AggressionUntil=20;p.AimTime=.64;Call(t,"RebuildActorGrid");
   Clock(t,.7);Call(t,"CombatStep",.02);Clock(t,.821);Call(t,"CombatStep",.121);Clock(t,.942);Call(t,"CombatStep",.121);
   Check(t.ShotsFired==3&&p.BurstRemaining==0,"NPC automatic fires three-round burst");
   Clock(t,1.3);Call(t,"CombatStep",.358);Check(t.ShotsFired==3,"NPC pauses between bursts");
   p.AggressionUntil=0;Clock(t,3);Call(t,"CombatStep",.01);Check(t.ShotsFired==3&&!p.Aiming,"calm NPC cancels burst");
   p.AggressionUntil=20;p.Dead=true;Clock(t,4);Call(t,"CombatStep",.1);Check(t.ShotsFired==3,"dead NPC cannot finish burst");
  });
  Test("bullets can down armed NPC",()=>{
   var t=Sandbox();t.Inventory.Give(CityWeaponId.Pistol);t.PlayerFireHeld=true;var p=Person(t,0,10);p.Id=90010;
   Shoot(t,1);Check(!p.Dead&&p.GunDamage>0,"first pistol hit damages NPC");Shoot(t,1.4);
   Check(p.Dead&&p.Detached&&!t.World.Bodies.Contains(p.Body),"fatal shot removes pedestrian collider");
   Check(t.Pickups.Any(x=>x.Weapon==CityWeaponId.Automatic),"shot victim drops automatic");
  });
  Test("bounded drops and automatic fallback while firing",()=>{
   var t=Sandbox();var p=Person(t,CityTraffic.LandEdge+2,0);p.Id=90010;Call(t,"FatalPerson",p,true);
   Check(!p.Armed&&t.Pickups.Count==0,"off-island death leaves no invalid saved pickup");
   t.Inventory.Give(CityWeaponId.Pistol);t.Inventory.Give(CityWeaponId.Automatic);t.PlayerFireHeld=true;
   for(int i=0;i<31;i++)Shoot(t,1+i*.13);
   Check(t.PlayerShotsFired==31&&t.Inventory.Ammo(CityWeaponId.Automatic)==0&&t.Inventory.Ammo(CityWeaponId.Pistol)==29,"held fire consumes automatic then falls back to pistol");
   Check(t.Shots.Count<=64,"projectile traces remain bounded");
  });
  Test("new and old save compatibility",()=>{
   var m=new CityModel();var t=m.Traffic;t.Inventory.Give(CityWeaponId.Pistol);t.Inventory.Give(CityWeaponId.Automatic);t.Inventory.Consume(CityWeaponId.Automatic);
   var p=t.Pedestrians.First(p=>p.Armed);p.WeaponReleased=true;p.GunDamage=.4;
   var saved=t.CaptureWorld(m);string json=JsonSerializer.Serialize(saved,new JsonSerializerOptions{IncludeFields=true});saved=JsonSerializer.Deserialize<CityWorldSave>(json,new JsonSerializerOptions{IncludeFields=true})!;
   CityTraffic.ValidateWorld(saved);int count=t.Pickups.Count;t.PlayerFireHeld=true;t.RestoreWorld(saved,m);
   Check(t.Inventory.Ammo(CityWeaponId.Automatic)==29&&t.Inventory.Ammo(CityWeaponId.Pistol)==30,"save preserves separate ammo amounts");
   Check(t.Pickups.Count==count&&t.Pickups.Select(x=>x.Id).Distinct().Count()==count,"save restores exact pickup population without duplication");
   Check(t.Pedestrians.First(x=>x.Id==p.Id).WeaponReleased&&!t.PlayerFireHeld,"save preserves disarm and clears held trigger");
   saved.Pickups=null;saved.Ammunition=null;t.RestoreWorld(saved,m);Check(t.Inventory.Active is null&&t.Pickups.Count==CityTraffic.PillTarget+CityTraffic.EnergyTarget+CityTraffic.MapWeaponTarget,"old format two save gains supplies and empty inventory");
   saved=t.CaptureWorld(m);saved.Ammunition![CityWeaponId.Pistol]=-1;bool bad=false;try{CityTraffic.ValidateWorld(saved);}catch(InvalidDataException){bad=true;}Check(bad,"corrupt ammunition rejected before applying world");
   saved=t.CaptureWorld(m);saved.Pickups!.Add(saved.Pickups[0].Copy());bad=false;try{CityTraffic.ValidateWorld(saved);}catch(InvalidDataException){bad=true;}Check(bad,"duplicate saved pickup IDs rejected");
  });
 }
}
