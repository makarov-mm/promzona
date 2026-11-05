using Promzona;
using Promzona.Game;
using System.Text.Json;
static partial class Tests
{
    static CityScooter LooseScooter(CityTraffic t,CityPoint at,int id=90001){
        var s=new CityScooter{Id=id,Position=at};Call(t,"RegisterScooter",s);t.Scooters.Add(s);return s;
    }
    static void RunCouriers()
    {
        Test("tank screen direction",()=>{
            foreach(double heading in new[]{0d,.6,2.7,-2.7}){
                double yaw=CityAim.MouseYaw(0,25);
                var right=new CityPoint(-Math.Cos(heading),Math.Sin(heading));
                var barrel=new CityPoint(Math.Sin(heading+yaw),Math.Cos(heading+yaw));
                Check(CityPoint.Dot(barrel,right)>0,"mouse right moves muzzle screen right at hull heading "+heading);
                Check(Math.Abs(CityAim.MouseYaw(yaw,-25))<1e-9,"opposite mouse movement restores aim");
            }
            Check(double.IsFinite(CityAim.MouseYaw(0,double.NaN))&&Math.Abs(CityAim.MouseYaw(3.13,-50))<=Math.PI,"invalid input and angle wrap remain finite");
        });
        Test("Ukrainian UI",()=>{
            Check(Ukrainian.Normalize("uk-UA")=="uk"&&Ukrainian.Cycle("ru",1)=="uk"&&Ukrainian.Cycle("uk",1)=="en"&&Ukrainian.Cycle("en",-1)=="uk","three language selection and system locale");
            foreach(var phrase in Ukrainian.Phrases)
                Check(Ukrainian.Translate(phrase.Key,"uk")==phrase.Value,"Ukrainian catalogue: "+phrase.Key);
            Check(Ukrainian.Translate("F — сойти с самоката","uk")=="F — зійти із самоката","dynamic control label translated");
            Check(Ukrainian.Translate("Здоровье 72% · $1250 · F5","uk").Contains("72% · $1250 · F5"),"numbers and keys survive translation");
            Check(Ukrainian.Translate("Новая игра","ru")=="Новая игра"&&Ukrainian.Translate("New game","en")=="New game","existing locales unchanged");
            Check(Ukrainian.Translate("сесть в машину / машина","uk")=="сісти в машину / машина","longest phrase wins over contained words");
            Check(Ukrainian.Translate("НоваяX XНовая","uk")=="НоваяX XНовая","letter boundaries prevent partial word translations");
            Check(Ukrainian.Translate("[Новая игра] · F — сойти с самоката","uk")=="[Нова гра] · F — зійти із самоката","punctuation and dynamic fragments remain intact");
            Check(Ukrainian.Translate("", "uk")=="","empty label is valid");
            string many=string.Concat(Enumerable.Repeat("F — сойти с самоката | ",10000));
            Check(Ukrainian.Translate(many,"uk")==string.Concat(Enumerable.Repeat("F — зійти із самоката | ",10000)),"large repeated UI text has deterministic results without timeouts");
        });
        Test("player scooter possession and braking",()=>{
            var t=FootSandbox();Check(t.InteractVehicle(),"exit onto pavement");for(int i=0;i<5;i++)t.Step(.1);WalkPlace(t,8,8);
            var s=LooseScooter(t,t.Player.Position);
            Check(t.InteractVehicle()&&t.OnScooter&&s.RiderId==t.Player.Id,"F mounts separate scooter");
            Check(!t.World.Bodies.Contains(s.Body),"mounted scooter does not collide with its rider");
            t.Inventory.Give(CityWeaponId.Pistol,30);t.PlayerFireHeld=true;int shots=t.PlayerShotsFired;t.Step(.1);
            Check(t.PlayerShotsFired==shots,"mounted rider cannot fire before movement consumes input");
            t.WalkInput=new(0,1);for(int i=0;i<180;i++)t.Step(1d/60);
            Check(t.Player.Position.Z>18&&t.Player.Speed>4&&s.Position==t.Player.Position,"player rides with inertial motion");
            t.ScooterBrake=true;for(int i=0;i<90;i++)t.Step(1d/60);
            Check(t.Player.Speed<.1,"Space brakes even with W held");
            Check(t.InteractVehicle()&&!t.OnScooter&&s.RiderId==0&&t.World.Bodies.Contains(s.Body),"F leaves a distinct local physical object");
            t.ClearPlayerInput();WalkPlace(t,s.Position.X,s.Position.Z);
            Check(t.MountScooter(t.Player,s),"loose scooter reusable");
            t.HitPerson(t.Player,t.Hero,3);
            Check(t.Player.ScooterId==0&&s.RiderId==0&&t.Player.KnockedFor>0&&s.VerticalSpeed>0,"impact separates rider and scooter");
        });
        Test("NPC scooter recovery and theft",()=>{
            var t=FootSandbox();t.InteractVehicle();WalkPlace(t,15,15);
            var npc=new CityPedestrian{Id=90100,Position=new(18,15),Persona=1,Cruise=1.5};typeof(CityTraffic).GetMethod("Register",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(CityPedestrian)},null)!.Invoke(t,new object[]{npc});t.Pedestrians.Add(npc);
            var s=LooseScooter(t,npc.Position);Check(t.MountScooter(npc,s),"NPC mounts own scooter");
            t.DismountScooter(npc,true);s.ReturnTime=t.Time;s.Height=s.VerticalSpeed=0;s.Body.Vx=s.Body.Vy=0;
            Call(t,"ScooterWalk",npc,.1d);
            Check(npc.ScooterId==s.Id&&s.RiderId==npc.Id,"recovered NPC remounts unclaimed scooter");
            t.DismountScooter(npc);WalkPlace(t,s.Position.X,s.Position.Z);
            Check(t.MountScooter(t.Player,s)&&npc.OwnedScooterId==0&&s.OwnerId==t.Player.Id,"player takeover removes former ownership");
            Call(t,"ScooterWalk",npc,.1d);Check(npc.ScooterId==0,"former owner cannot reclaim occupied scooter");
            t.DismountScooter(t.Player);var second=LooseScooter(t,t.Player.Position,90002);
            Check(t.MountScooter(t.Player,second)&&s.OwnerId==0,"changing scooters abandons old ownership");
            Call(t,"FatalPerson",t.Player,false);Check(second.OwnerId==0&&second.RiderId==0&&t.Player.OwnedScooterId==0,"death releases both rider and ownership");
        });
        Test("courier save and legacy migration",()=>{
            CityLayout.Install(CityLayout.Generate(73921,roadRevision:4));var model=new CityModel();var t=model.Traffic;
            Check(t.Scooters.Count>30&&t.Scooters.Count<=CityTraffic.ScooterTarget,"bounded city-wide scooter fleet");
            Check(t.Pedestrians.Count(p=>p.Courier)>60&&t.Pedestrians.Count(p=>p.Courier)<=CityTraffic.CourierTarget,"bounded courier population drawn from residents");
            Check(t.Scooters.Any(s=>t.Pedestrians.Any(p=>p.Id==s.OwnerId&&p.Courier))&&t.Pedestrians.Any(p=>p.Courier&&p.ScooterId==0),"walking and riding couriers coexist");
            var ride=t.Scooters.First();var owner=t.Pedestrians.First(p=>p.Id==ride.OwnerId);t.DismountScooter(owner);
            Check(t.InteractVehicle(),"saved player exits starter car");WalkPlace(t,ride.Position.X,ride.Position.Z);
            Check(t.MountScooter(t.Player,ride),"saved player mounts scooter");
            var save=JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(t.CaptureWorld(model),CityState.Json),CityState.Json)!;
            CityTraffic.ValidateWorld(save);var ids=t.Scooters.Select(s=>s.Id).ToArray();t.RestoreWorld(save,model);
            Check(t.Scooters.Select(s=>s.Id).SequenceEqual(ids),"save/load retains exact scooter identities");
            Check(t.OnScooter&&t.Player.ScooterId==ride.Id&&t.Scooters.First(v=>v.Id==ride.Id).RiderId==t.Player.Id,"load retains player possession without duplicate bodies");
            var bad=JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(save,CityState.Json),CityState.Json)!;
            bad.Scooters[0].State[nameof(CityScooter.OwnerId)]=int.MaxValue;bool rejected=false;try{CityTraffic.ValidateWorld(bad);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"orphaned ownership rejected before loading");
            var old=JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(save,CityState.Json),CityState.Json)!;old.ScooterRevision=0;old.Scooters.Clear();
            foreach(var a in old.People.Append(old.Player!)){a.State.Remove(nameof(CityPedestrian.ScooterId));a.State.Remove(nameof(CityPedestrian.OwnedScooterId));}
            t.RestoreWorld(old,model);Check(t.Scooters.Count>20,"old saves gain offscreen riders without new city");
        });
        foreach(int seed in new[]{73921,42})Test("autonomous remote city progression "+seed,()=>{
            CityLayout.Install(CityLayout.Generate(seed,roadRevision:4));var model=new CityModel();var t=model.Traffic;t.GodMode=true;
            var farPeople=t.Pedestrians.Where(p=>!p.Physical&&!p.Dead&&!p.Detached&&p.Persona<3).ToDictionary(p=>p.Id,p=>(p.Distance,p.Position));
            var farCars=t.Cars.Where(c=>!c.Physical&&!c.Hero&&!c.Driverless).ToDictionary(c=>c.Id,c=>c.Distance);
            var initialScooters=t.Scooters.ToDictionary(s=>s.Id,s=>s.Position);
            int bodies=0;double minWindowCars=double.MaxValue,minWindowPeople=double.MaxValue;
            for(int window=0;window<6;window++){
                double cd=t.Cars.Sum(c=>c.Distance),pd=t.Pedestrians.Sum(p=>p.Distance);
                for(int i=0;i<300;i++)t.Step(.1,new(0,0,true));
                minWindowCars=Math.Min(minWindowCars,t.Cars.Sum(c=>c.Distance)-cd);minWindowPeople=Math.Min(minWindowPeople,t.Pedestrians.Sum(p=>p.Distance)-pd);
                bodies=Math.Max(bodies,t.World.Bodies.Count);
                CheckFinite(t);
                Check(t.Scooters.Count<=CityTraffic.ScooterCap&&t.Cars.Count<=CityTraffic.CarCap+CityTraffic.MaxPoliceUnits+2&&t.Pedestrians.Count<=CityTraffic.PeopleCap+64,"population bounded through remote interval "+window);
                Check(t.Scooters.Where(s=>s.RiderId==0&&!s.Physical).All(s=>!t.World.Bodies.Contains(s.Body)),"remote loose scooters have no collision bodies");
            }
            int walking=t.Pedestrians.Count(p=>farPeople.TryGetValue(p.Id,out var a)&&p.Distance>a.Distance+10);
            int driving=t.Cars.Count(c=>farCars.TryGetValue(c.Id,out var distance)&&c.Distance>distance+30);
            Check(walking>farPeople.Count*.5&&driving>farCars.Count*.35,"majority of remote pedestrians and substantial traffic progress without player travel");
            Check(minWindowPeople>500&&minWindowCars>500,"no global deadlock in any 30 second interval");
            Check(t.CompletedDeliveries>10,"couriers finish delivery pauses away from player");
            Check(t.Scooters.Count(s=>initialScooters.TryGetValue(s.Id,out var p)&&(s.Position-p).Length>10)>20,"remote scooter routes remain active");
            Check(t.Scooters.All(s=>double.IsFinite(s.Position.X+s.Position.Z+s.Heading))&&t.Scooters.Select(s=>s.Id).Distinct().Count()==t.Scooters.Count,"finite unique scooter actors after endurance");
            Console.WriteLine($"REMOTE seed={seed}: walked={walking}/{farPeople.Count}, drove={driving}/{farCars.Count}, deliveries={t.CompletedDeliveries}, minWindow={minWindowPeople:F1}/{minWindowCars:F1}, maxBodies={bodies}");
            var save=t.CaptureWorld(model);CityTraffic.ValidateWorld(save);
        });
        CityLayout.Install(CityLayout.Generate(73921));
    }
}
