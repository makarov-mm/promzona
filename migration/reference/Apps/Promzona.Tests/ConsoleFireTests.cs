using Promzona.Game;
static partial class Tests
{
    static void Clock(CityTraffic t,double time)=>typeof(CityTraffic).GetProperty("Time")!.SetValue(t,time);
    static void RunConsoleFire(){
        Test("console editing and bounded memory",()=>{
            var c=new CityConsole();foreach(char x in "money 42")c.Insert(x);Check(c.Submit()=="money 42","console submits complete command");
            c.History(-1);Check(c.Text=="money 42"&&c.Caret==8,"history restores caret");c.History(1);Check(c.Text=="","history returns to blank line");
            c.Text="wea";c.Complete();Check(c.Text=="weapon ","unique completion");for(int n=0;n<200;n++)c.Write("log");Check(c.Lines.Count==100,"console log bounded");
            c.Text="";c.Caret=0;for(int n=0;n<300;n++)c.Insert('a');Check(c.Text.Length==160,"input bounded");
        });
        Test("console validation and persistent special actors",()=>{
            var m=new CityModel();var t=m.Traffic;
            CityConsole.Execute(m,"money 1000");Check(t.Money==1000,"money command");
            foreach(string bad in new[]{"money NaN","money Infinity","money -1","money 3 extra","speed 99","weapon flamethrower 0","spawn unknown"})CityConsole.Execute(m,bad);
            Check(t.Money==1000,"invalid commands cannot corrupt money");
            CityConsole.Execute(m,"weapon flamethrower 17");Check(t.Inventory.Ammo(CityWeaponId.Flamethrower)==17,"explicit fuel count");
            CityConsole.Execute(m,"hour 19");Check(m.Hour==19,"clock command");CityConsole.Execute(m,"god on");Check(t.GodMode,"god command");
            foreach(string type in new[]{"tank","helicopter"})Check(t.ConsoleSpawn(type).StartsWith("Spawned"),"safe special spawn "+type);
            Check(t.Cars.Any(c=>c.Kind==11&&c.Mass==32000)&&t.Cars.Any(c=>c.Kind==12&&c.Mass==2100),"real special actor dimensions and masses");
            var save=t.CaptureWorld(m);CityTraffic.ValidateWorld(save);t.RestoreWorld(save,m);
            Check(t.Cars.Any(c=>c.Kind==11)&&t.Cars.Any(c=>c.Kind==12)&&t.Inventory.Ammo(CityWeaponId.Flamethrower)==17,"new actors and fuel survive save/load");
        });
        Test("direct owner immunity and fire survival",()=>{
            var t=FootSandbox();t.InteractVehicle();WalkPlace(t,0,-20);
            t.IgnitePerson(t.Player,true);Check(t.Player.BurnUntil==0,"direct flame cannot ignite owner");
            t.IgnitePerson(t.Player);for(int n=1;n<=70;n++){Clock(t,n*.1);Call(t,"FireStep",.1);}
            Check(!t.Player.Dead&&t.PlayerHealth>.58&&t.PlayerHealth<.7&&t.Player.BurnUntil<t.Time,"healthy player survives finite six-second burn");
            var p=Person(t,5,-20);t.IgnitePerson(p,false,true);
            for(int n=71;n<=140;n++){Clock(t,n*.1);Call(t,"FireStep",.1);}
            Check(p.Dead,"unextinguished NPC fire is normally lethal");
        });
        Test("flame hits forward target and secondary fire returns",()=>{
            var t=FootSandbox();t.InteractVehicle();WalkPlace(t,0,-20);var target=Npc(t,0,0,-14);target.Hero=false;
            Call(t,"RebuildActorGrid");Call(t,"Flame",new CityPoint(0,1));
            Check(target.Burning&&t.Player.BurnUntil==0,"forward vehicle burns without direct owner damage");
            Check(target.Damage>0&&target.Damage<.01,"overlapping flame rays heat a vehicle only once per pulse");
            for(int n=0;n<9;n++)Call(t,"Flame",new CityPoint(0,1));
            Check(!target.Destroyed&&target.Damage<.1&&target.FireAge<1,"one second of flame does not instantly explode a healthy car");
            WalkPlace(t,0,-17);Call(t,"RebuildActorGrid");Call(t,"FireStep",.11);
            Check(t.Player.BurnUntil>t.Time,"burning vehicle can ignite its attacker nearby");
            t.GodMode=true;Call(t,"FireStep",.11);Check(t.Player.BurnUntil==0,"god mode extinguishes player");
        });
        Test("helicopter climb ceiling landing and tank cannon",()=>{
            var t=Empty();var c=Car(t.World,12);t.Cars.Add(c);t.HelicopterLiftInput=1;
            for(int n=0;n<600;n++)t.Step(1d/60,new(.2,.1));
            Check(c.Lift>35&&c.Lift<66&&c.RotorPhase!=0&&c.Position.Length>3,"assisted helicopter climbs and translates");
            for(int n=0;n<600;n++)t.Step(1d/60);Check(c.Lift<68,"altitude cap remains bounded");
            t.HelicopterLiftInput=-1;for(int n=0;n<1200;n++)t.Step(1d/60,new(0,0,true,true));
            Check(c.Lift==0&&c.VerticalVelocity==0,"helicopter lands without sinking");CheckFinite(t);
            var tank=FootSandbox();tank.Hero.Kind=11;tank.Hero.SpecialYaw=.4;tank.PlayerFireHeld=true;Call(tank,"PlayerCombat");
            Check(tank.Rockets.Count==1&&Math.Abs(Math.Atan2(tank.Rockets[0].Direction.X,tank.Rockets[0].Direction.Z)-.4)<1e-8,"tank cannon follows turret");
        });
        Test("helicopter roof and underside contact",()=>{
            var t=QuietCity();var c=t.Hero;c.Kind=12;
            var b=CityModel.Blocks.OrderBy(b=>Math.Abs(b.X)+Math.Abs(b.Z)).First();
            Place(c,b.X,b.Z);t.UpdateTerrainSupport(c);double roof=CityTerrain.Ground(b.X,b.Z)+b.Height;
            c.Lift=roof-c.SurfaceHeight-.2;c.VerticalVelocity=-5;
            Call(t,"HelicopterContact",c,roof+.2);
            Check(Math.Abs(c.SurfaceHeight+c.Lift-roof-.025)<1e-6&&c.VerticalVelocity==0,"full roof catches helicopter between facade walls");
            Place(c,20000,20000);c.Lift=roof-c.SurfaceHeight-.2;c.VerticalVelocity=-5;
            Call(t,"HelicopterContact",c,roof+.2);
            Check(c.VerticalVelocity==-5,"clear space beside roof does not produce invisible support");
        });
        Test("fatal occupant fire returns to clinic",()=>{
            var t=FootSandbox();t.Player.GunDamage=.99;t.IgnitePerson(t.Player);
            Clock(t,.2);Call(t,"FireStep",.2);Check(t.Player.Dead,"fire can kill an injured occupant");
            Clock(t,4);Call(t,"PlayerLifeStep");
            Check(t.OnFoot&&!t.Player.Dead&&t.PlayerHealth==1&&t.Player.BurnUntil==0,"occupant death respawns healthy and extinguished on foot");
        });
        Test("weapon fallback includes fuel",()=>{
            var inv=new CityInventory();foreach(var w in CityWeapons.All)inv.Give(w.Id,1);
            foreach(var id in new[]{CityWeaponId.Bazooka,CityWeaponId.Flamethrower,CityWeaponId.Automatic,CityWeaponId.Pistol}){Check(inv.Active?.Id==id,"fallback "+id);inv.Consume(id);}
            Check(inv.Active is null,"last round returns fists");
        });
    }
}

