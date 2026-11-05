using Promzona.Game;
static partial class Tests
{
    static void RunTransitCombat(){
        foreach(int nodes in new[]{49,33})foreach(int seed in new[]{73921,1,42,91837})Test($"citywide network {nodes}/{seed}",()=>{
            CityLayout.Install(CityLayout.Generate(seed,gridSize:nodes,wideRoads:true,roadRevision:3));
            Console.WriteLine($"NETWORK {nodes}/{seed}: {CityTransit.Routes.Count}");
            Check(CityTransit.Routes.Count==4,"four district-spanning routes");
            foreach(var route in CityTransit.Routes){
                var points=Enumerable.Range(0,(int)(route.Length/2)).Select(i=>route.Sample(i*2).Point).ToArray();
                double width=points.Max(p=>p.X)-points.Min(p=>p.X),depth=points.Max(p=>p.Z)-points.Min(p=>p.Z);
                Console.WriteLine($"route={route.Id} length={route.Length:F0} extent={width:F0}x{depth:F0} stops={route.Stops.Length} fleet={route.FleetSize}");
                Check(route.Length>1800&&width>(nodes-1)*36*.30&&depth>(nodes-1)*36*.30,"route crosses multiple districts");
                Check(route.Stops.Length>=7&&route.FleetSize>=5,"long routes have distributed stops and vehicles");
                Check(points.All(p=>CityWater.Depth(p)<.05&&CityRoadSurface.Contains(p)),"rails and wires remain over usable streets");
                Check((route.Sample(0).Point-route.Sample(route.Length).Point).Length<1e-8,"continuous closed circuit");
            }
            foreach(int kind in new[]{9,10}){
                var points=CityTransit.Routes.Where(r=>r.Kind==kind).SelectMany(r=>Enumerable.Range(0,(int)(r.Length/10)).Select(i=>r.Sample(i*10).Point)).ToArray();
                if(points.Length==0)continue;
                Check(points.Max(p=>p.X)-points.Min(p=>p.X)>(nodes-1)*36*.65&&points.Max(p=>p.Z)-points.Min(p=>p.Z)>(nodes-1)*36*.65,"each mode reaches most of both city axes");
            }
        });
        CityLayout.Install(CityLayout.Generate(73921));
        Test("launcher ammunition priority and NPC exclusion",()=>{
            var inv=new CityInventory();foreach(var w in CityWeapons.All.Where(w=>w.Id!=CityWeaponId.Flamethrower))inv.Give(w.Id);
            Check(inv.Active?.Id==CityWeaponId.Bazooka&&inv.Ammo(CityWeaponId.Bazooka)==20,"rare launcher supplies twenty rockets");
            for(int k=0;k<20;k++)inv.Consume(CityWeaponId.Bazooka);
            Check(inv.Active?.Id==CityWeaponId.Automatic,"empty launcher selects automatic");
            for(int k=0;k<30;k++)inv.Consume(CityWeaponId.Automatic);
            Check(inv.Active?.Id==CityWeaponId.Pistol,"empty automatic selects pistol");
            for(int k=0;k<30;k++)inv.Consume(CityWeaponId.Pistol);
            Check(inv.Active is null,"exhausted inventory selects fists");
            bool excluded=true;for(int role=0;role<12;role++)for(int id=1;id<150;id++){
                var p=new CityPedestrian{Persona=role,Id=id};excluded&=p.Weapon!=CityWeaponId.Bazooka;
            }Check(excluded,"all civilian and service roles exclude launcher");
        });
        Test("punch timing range and ammunition",()=>{
            var t=FootSandbox();Check(t.InteractVehicle()&&t.OnFoot,"leave car for melee");
            t.PlayerViewHeading=0;var origin=t.PlayerPosition;var victim=Person(t,origin.X,origin.Z+1.1);victim.Id=99001;
            t.PlayerFireHeld=true;Call(t,"RebuildActorGrid");Call(t,"PlayerCombat");
            Check(t.PunchesThrown==1&&victim.GunDamage==0&&t.PlayerShotsFired==0,"punch starts animation without a bullet");
            typeof(CityTraffic).GetProperty("Time")!.SetValue(t,.2);Call(t,"RocketStep",.2);
            Check(victim.GunDamage>0&&!victim.Dead&&victim.Fear>0,"contact damages and pushes a nearby target");
            double damage=victim.GunDamage;Call(t,"RocketStep",.01);Call(t,"PlayerCombat");
            Check(victim.GunDamage==damage&&t.PunchesThrown==1,"one contact per swing and cooldown");
            Check(t.Shots.Count==0&&t.Inventory.Active is null,"fist uses neither tracer nor ammo");
        });
        Test("held fire switches all weapons and HUD",()=>{
            var t=FootSandbox();t.InteractVehicle();WalkPlace(t,0,-20);t.PlayerViewHeading=0;t.PlayerFireHeld=true;
            foreach(var w in CityWeapons.All.Where(w=>w.Id!=CityWeaponId.Flamethrower))t.Inventory.Give(w.Id);
            double time=1;
            for(int n=0;n<80;n++){
                typeof(CityTraffic).GetProperty("Time")!.SetValue(t,time);Call(t,"RebuildActorGrid");
                var weapon=t.Inventory.Active!;Call(t,"PlayerCombat");time+=weapon.Interval+.01;
                Check(t.Player.PlayerWeapon==(t.Inventory.Active?.Id??CityWeaponId.None),"hand and HUD use strongest loaded weapon");
            }
            Check(t.PlayerShotsFired==80&&t.Inventory.Active is null,"twenty rockets and sixty bullets consumed once");
            typeof(CityTraffic).GetProperty("Time")!.SetValue(t,time);Call(t,"PlayerCombat");
            Check(t.PunchesThrown==1&&t.PlayerShotsFired==80,"held fire becomes punch after last pistol round");
            Check(t.Rockets.Count<=16,"projectile collection bounded");
        });
        Test("punch misses distant and rear targets",()=>{
            var t=FootSandbox();t.InteractVehicle();WalkPlace(t,0,-20);t.PlayerViewHeading=0;
            var back=Person(t,0,-21);var far=Person(t,0,-17);t.PlayerFireHeld=true;
            Call(t,"RebuildActorGrid");Call(t,"PlayerCombat");typeof(CityTraffic).GetProperty("Time")!.SetValue(t,.2);Call(t,"RocketStep",.2);
            Check(back.GunDamage==0&&far.GunDamage==0,"fist cannot hit behind or beyond reach");
            t=FootSandbox();t.PlayerFireHeld=true;Call(t,"PlayerCombat");Check(t.PunchesThrown==0,"no punching through car doors");
        });
        Test("rare launcher replenishment and save",()=>{
            var m=new CityModel();var t=m.Traffic;
            Check(t.Pickups.Count(p=>p.Weapon==CityWeaponId.Bazooka)==2,"exactly two rare map launchers");
            t.Pickups.RemoveAll(p=>p.Weapon==CityWeaponId.Bazooka);
            Call(t,"FillMapPickups",false);
            Check(t.Pickups.Count(p=>p.Weapon==CityWeaponId.Bazooka)==2&&t.Pickups.Where(p=>p.Weapon==CityWeaponId.Bazooka).All(p=>p.MapSpawn&&(p.Position-t.PlayerPosition).Length>t.SpawnDistance),"launcher replenishment is out of view and only map spawned");
            t.Inventory.Give(CityWeaponId.Bazooka);t.Inventory.Consume(CityWeaponId.Bazooka);
            var save=t.CaptureWorld(m);CityTraffic.ValidateWorld(save);t.RestoreWorld(save,m);
            Check(t.Inventory.Ammo(CityWeaponId.Bazooka)==19,"remaining rockets survive save/load");
            save=t.CaptureWorld(m);save.Pickups!.First(p=>p.Weapon==CityWeaponId.Bazooka).MapSpawn=false;
            bool rejected=false;try{CityTraffic.ValidateWorld(save);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"NPC-dropped launcher is not a valid save state");
        });
        Test("legacy transit migration and route persistence",()=>{
            var m=new CityModel();var t=m.Traffic;var save=t.CaptureWorld(m);save.TransitRevision=0;
            t.RestoreWorld(save,m);
            Check(t.Cars.Where(c=>c.TransitRoute>0).All(c=>CityTransit.ById(c.TransitRoute) is {} r&&(r.Sample(c.TransitAlong).Point-c.Position).Length<.01),"legacy transit vehicles rejoin new tracks");
            Check(CityTransit.Routes.All(r=>t.Cars.Any(c=>c.TransitRoute==r.Id)),"every citywide route receives a fleet");
            var before=t.Cars.Where(c=>c.TransitRoute>0).ToDictionary(c=>c.Id,c=>(c.Position,c.TransitAlong,c.TransitRoute));
            save=t.CaptureWorld(m);CityTraffic.ValidateWorld(save);t.RestoreWorld(save,m);
            Check(t.Cars.Where(c=>before.ContainsKey(c.Id)).All(c=>before[c.Id]==(c.Position,c.TransitAlong,c.TransitRoute)),"new saves retain transit poses without a second migration");
        });
        Test("rocket wall collision and blast occlusion",()=>{
            var t=QuietCity();var b=CityModel.Blocks.First(b=>b.Height>4&&b.HalfX>3&&b.HalfZ>3);
            var a=new CityPoint(b.X-b.HalfX-1,b.Z);var z=new CityPoint(b.X+b.HalfX+1,b.Z);
            Place(t.Hero,a.X-15,a.Z); // Keep the listener within the game's 85 m audio radius.
            double h=CityTerrain.Ground(new(b.X,b.Z))+1;
            Check(!(bool)Call(t,"BlastVisible",a,h,z,h)!,"building blocks splash damage");
            t.Rockets.Add(new(){Position=a,Direction=new(1,0),Height=h});Call(t,"RebuildActorGrid");Call(t,"RocketStep",.25);
            Check(t.Rockets.Count==0&&t.Sounds.Any(s=>s.Name=="explosion"),"large frame step cannot tunnel a rocket through facade");
        });
        Test("rocket travel impact and splash",()=>{
            var t=FootSandbox();Place(t.Hero,0,0);var target=Npc(t,0,0,18);var neighbour=Npc(t,0,4,18);
            var victim=Person(t,3,18);victim.Id=90001;t.Inventory.Give(CityWeaponId.Bazooka);t.PlayerFireHeld=true;
            Call(t,"RebuildActorGrid");Call(t,"PlayerCombat");t.PlayerFireHeld=false;
            Check(t.Rockets.Count==1&&target.Damage==0&&t.Inventory.Ammo(CityWeaponId.Bazooka)==19,"launch consumes ammo but damage waits for flight");
            for(int k=1;k<=45;k++){typeof(CityTraffic).GetProperty("Time")!.SetValue(t,k/60d);Call(t,"RocketStep",1d/60);}
            Check(t.Rockets.Count==0&&target.Damage>0&&neighbour.Damage>0,"swept projectile impacts vehicle and damages neighbours");
            Check(victim.Dead&&t.Sounds.Any(s=>s.Name=="explosion"),"nearby pedestrian and explosion effects respond");
        });
    }
}
