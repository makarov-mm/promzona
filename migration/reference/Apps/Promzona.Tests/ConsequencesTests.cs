using Promzona.Game;
static partial class Tests
{
    // Consequences (29.10.2026): witnessed crimes, fading pursuit, arrest, the paid clinic.
    static void RunConsequences(){
        CityLayout.Install(CityLayout.Generate(73921));
        Test("crimes count only with a witness",()=>{
            var t=FootSandbox();var victim=Person(t,0,6);Call(t,"RebuildActorGrid");
            t.HitPerson(victim,t.Hero,9);Check(victim.Dead&&t.Wanted==0&&t.UnseenCrimes==1,"a fatal hit with nobody left to see it raises nothing");
            var witness=Person(t,0,-30);var second=Person(t,0,14);Call(t,"RebuildActorGrid");
            t.HitPerson(second,t.Hero,9);Check(second.Dead&&t.Wanted>.8,"a bystander with a clear view reports the killing");
            var t2=FootSandbox();var far=Person(t2,0,120);var third=Person(t2,0,12);Call(t2,"RebuildActorGrid");
            t2.HitPerson(third,t2.Hero,9);Check(t2.Wanted==0&&far.Fear==0,"a person 120 m away is no witness");
            var patrol=Npc(t2,3,0,-50);var fourth=Person(t2,0,18);Call(t2,"RebuildActorGrid");
            t2.HitPerson(fourth,t2.Hero,9);Check(t2.Wanted>.8,"a crewed militia car within seventy metres sees it");
            var t3=FootSandbox();var hurt=Person(t3,0,8);Call(t3,"RebuildActorGrid");t3.HitPerson(hurt,t3.Hero,3);
            Check(!hurt.Dead&&t3.Wanted>0,"the living victim of a knock-down is a witness himself");
            var t4=FootSandbox();t4.Inventory.Give(CityWeaponId.Pistol);t4.PlayerFireHeld=true;Tick(t4,.3);t4.PlayerFireHeld=false;
            Check(t4.PlayerShotsFired>0&&t4.Wanted==0&&t4.UnseenCrimes>0,"a shot into an empty street is nobody's business");
            var listener=Person(t4,-30,40);Call(t4,"RebuildActorGrid");Tick(t4,.5);t4.PlayerFireHeld=true;Tick(t4,.3);t4.PlayerFireHeld=false;
            Check(t4.Wanted>0,"a shot is heard fifty metres away without a line of sight");
            t4.AddCrime(1);Check(t4.Wanted>1,"AddCrime stays unconditional for callers that decided already");
        });
        Test("pursuit fades fast once the militia loses sight",()=>{
            var t=FootSandbox();t.AddCrime(1);Tick(t,11.5);Check(t.Wanted>.99,"nothing fades in the first twelve seconds");
            Tick(t,13);Check(t.Wanted>.7&&t.Wanted<.85,"the old slow fade applies until the dispatch had its chance");
            Tick(t,12);Check(t.Wanted==0,"unseen for long, the level is gone within forty seconds");
            Check(!(bool)Call(t,"PoliceSees")!,"no militia around");
            var patrol=Npc(t,3,0,-70);Check((bool)Call(t,"PoliceSees")!,"a crewed militia car within ninety metres sees the player");
            Place(patrol,200,0);Check(!(bool)Call(t,"PoliceSees")!,"not from two hundred");
            var officer=Person(t,0,-20,5);Check((bool)Call(t,"PoliceSees")!,"an officer on foot within forty-five metres sees the player");
        });
        Test("an officer beside a subdued player makes an arrest",()=>{
            var t=FootSandbox();t.SetMoney(500);t.Inventory.Give(CityWeaponId.Pistol);t.InteractVehicle();Tick(t,.3);t.AddCrime(1.5);
            var officer=Person(t,t.PlayerPosition.X-2.6,t.PlayerPosition.Z,5);Call(t,"RebuildActorGrid");
            Tick(t,4);
            Check(t.Arrests==1&&t.Wanted==0&&t.Respawns==1,"standing still next to an officer ends in an arrest");
            Check(t.Money==340&&t.LastFine==160&&!t.Inventory.HasWeapons,"the fine depends on the level and the weapons are seized");
            Check(t.TakeHourSkip()==5&&t.TakeHourSkip()==0,"five hours pass in the cell, once");
            Check(t.OnFoot&&(CityAmenities.StationEntrance()-t.PlayerPosition).Length<1.6&&t.InteractionMessage=="arrest","the player is released at the militia department");
            Check(t.Pedestrians.Contains(officer)&&!officer.Aiming,"the officer did not shoot a surrendering suspect");
        });
        Test("a moving or dangerous suspect is not arrested",()=>{
            var t=FootSandbox();t.InteractVehicle();Tick(t,.3);t.AddCrime(1.5);
            Person(t,t.PlayerPosition.X-2.2,t.PlayerPosition.Z,5);Call(t,"RebuildActorGrid");t.WalkInput=new(0,-1);Tick(t,4);
            Check(t.Arrests==0&&t.WantedLevel>0,"a suspect who keeps walking is chased, not cuffed");
            var t2=FootSandbox();t2.InteractVehicle();Tick(t2,.3);t2.ConsoleWanted(5);
            Person(t2,t2.PlayerPosition.X-2.6,t2.PlayerPosition.Z,6);Call(t2,"RebuildActorGrid");Tick(t2,4);
            Check(t2.Arrests==0,"at wanted level five the squad shoots instead of arresting");
        });
        Test("a stopped car is boxed in and its driver arrested",()=>{
            var t=FootSandbox();t.SetMoney(100);t.AddCrime(1);var car=t.Hero;
            var officer=Person(t,3.6,.5,5);Call(t,"RebuildActorGrid");Tick(t,5);
            Check(t.Arrests==1&&t.OnFoot&&car.Driverless&&t.Money==0&&t.LastFine==100,"the officer reaches the door of the stopped car and the driver is taken away");
        });
        Test("the clinic charges for treatment",()=>{
            var t=FootSandbox();t.InteractVehicle();var at=CityAmenities.ClinicEntrance(CityTraffic.ClinicBlocks[0].I,CityTraffic.ClinicBlocks[0].J);WalkPlace(t,at.X,at.Z);
            t.Player.GunDamage=.5;t.SetMoney(10);Call(t,"AmenitiesStep",2d);
            Check(Math.Abs(t.PlayerHealth-.65)<1e-8&&t.Money==4,"fifteen points of health cost six dollars");
            t.SetMoney(0);Call(t,"AmenitiesStep",2d);Check(Math.Abs(t.PlayerHealth-.70)<1e-8&&t.InteractionMessage=="clinic_poor","with no money the queue heals a third as fast");
            t.SetMoney(200);t.DamagePlayer(100);Tick(t,3.5);Check(t.Respawns==1&&t.Money==150&&t.LastBill==50&&t.InteractionMessage=="bill","a death costs a quarter of the wallet");
            t.SetMoney(30);t.DamagePlayer(100);Tick(t,3.5);Check(t.Respawns==2&&t.Money==30&&t.LastBill==0,"a poor patient is treated for free");
        });
        Test("the militia department stands in its block",()=>{
            var layout=CityLayout.Current;var (i,j)=CityTraffic.StationBlock;
            Check(layout.BuildingIds(i,j).Count()==1&&!layout.Park(i,j),"the station block holds one public building and no park");
            var b=CityModel.Blocks[layout.BuildingIds(i,j).First()];Check(Math.Abs(b.HalfX-9)<1e-9&&Math.Abs(b.HalfZ-8)<1e-9,"the building has the clinic footprint");
            var door=CityAmenities.StationEntrance();Check(layout.Furniture.All(f=>(new CityPoint(f.X,f.Z)-door).Length>3)&&CityAmenities.Stops().All(s=>(s-door).Length>3),"nothing stands in the doorway");
            Check(!CityAmenities.Food().Any(s=>(s.Position-door).Length<12),"no kiosk in front of the station");
        });
    }
}
