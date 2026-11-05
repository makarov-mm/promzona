using Promzona.Game;
static partial class Tests
{
    // The "day of a gypsy cab" loop (29.10.2026): taxi, courier and patient jobs.
    static CityPedestrian Hailer(CityTraffic t,double x,double z){var p=Person(t,x,z);p.Hailing=true;p.HailUntil=t.Time+200;p.RoleHome=p.Position;Call(t,"RebuildActorGrid");return p;}
    static void RunJobs(){
        CityLayout.Install(CityLayout.Generate(73921));
        Test("hailers wait at bus stops while the player drives",()=>{
            var t=FootSandbox();Tick(t,7);var hailers=t.Hailers.ToList();
            Check(hailers.Count>=1&&hailers.Count<=3,$"a driver sees up to three hailers ({hailers.Count})");
            Check(hailers.All(p=>t.BusStops.Any(s=>(s-p.Position).Length<3.5)),"every hailer stands at a bus stop");
            Check(hailers.All(p=>{double d=(p.Position-t.PlayerPosition).Length;return d>=45&&d<=215;}),"hailers appear between 45 and 210 m from the driver");
            Check(hailers.All(p=>p.Speed<.3&&Math.Abs(p.Heading-Math.PI/2)<.3),"a hailer stands still facing the road");
            var first=hailers[0];first.HailUntil=t.Time-1;Tick(t,2.5);
            Check(!first.Hailing&&t.Pedestrians.Contains(first),"a hailer gives up after the wait and walks on");
            t.InteractVehicle();Tick(t,.5);foreach(var p in t.Hailers.ToList())p.HailUntil=t.Time-1;Tick(t,5);
            Check(!t.Hailers.Any(),"nobody hails a player on foot");
        });
        Test("street names cover every road",()=>{
            Check(CityNames.Street(0,true).StartsWith("ул. ")&&CityNames.Street(0,false).EndsWith(" St")&&CityNames.Street(48,true)!=CityNames.Street(0,true),"streets have Russian and English names");
            var names=new HashSet<string>();for(int i=0;i<49;i++){names.Add(CityNames.Street(i,true));names.Add(CityNames.Avenue(i,true));}
            Check(names.Count==98,"98 distinct names for a 49-node grid");
            var stop=new CityPoint(CityTraffic.Coordinate(5)-CityTraffic.Sidewalk,CityTraffic.Coordinate(7)+18);
            Check(CityNames.StreetAt(stop,true)==CityNames.Street(5,true),"a bus stop is named after its vertical street");
            var kiosk=new CityPoint(CityTraffic.Coordinate(5)+18,CityTraffic.Coordinate(7)+CityTraffic.Sidewalk+.4);
            Check(CityNames.StreetAt(kiosk,true)==CityNames.Avenue(7,true),"a kiosk is named after its horizontal street");
        });
        Test("taxi fare from the kerb to another stop",()=>{
            var t=FootSandbox();var p=Hailer(t,3.4,1.0);int id=p.Id;
            Check(t.NearbyJob() is {Kind:JobKind.Taxi,Deliver:false} offer&&offer.Fare>=20&&t.BusStops.Contains(offer.Target)&&(offer.Target-p.Position).Length>=160,"a stopped car beside a hailer is offered a fare to a distant stop");
            var target=t.NearbyJob()!.Value.Target;
            Check(t.InteractJob()&&t.JobKind==JobKind.Taxi&&t.JobStage==0&&p.Boarding==t.Hero.Id&&!p.Hailing,"E calls the passenger to the door");
            Tick(t,5);
            Check(t.JobStage==1&&!t.Pedestrians.Contains(p)&&!t.World.Bodies.Contains(p.Body),"the passenger boards and leaves the pedestrian list");
            Check(t.JobTarget==target&&t.JobFare==CityTraffic.Fare(JobKind.Taxi,(target-new CityPoint(0,0)).Length)&&t.JobTimeLeft>45,"fare and allowance follow the distance");
            Check(t.NearbyJob() is null,"no second job is offered while one is active");
            t.Hero.Speed=9;Tick(t,.2);Check(t.JobStage==1,"driving on keeps the passenger aboard");
            int i=(int)Math.Round((target.X+CityTraffic.Sidewalk-CityTraffic.Coordinate(0))/CityTraffic.Spacing);
            Place(t.Hero,CityTraffic.Coordinate(i)-1.9,target.Z);Tick(t,1.5);
            Check(t.JobKind==JobKind.None&&t.JobsCompleted==1&&t.Money==t.LastJobPay&&t.LastJobPay>=20,"stopping at the destination pays the fare");
            var back=t.Pedestrians.Find(x=>x.Id==id);
            Check(back is not null&&!back.Hailing&&back.Boarding<0&&(back.Position-t.Hero.Position).Length<4.5&&t.World.Bodies.Contains(back.Body),"the passenger steps out beside the car with the same identity");
            Check(t.InteractionMessage=="job_paid"&&t.MessageUntil>t.Time,"the HUD announces the payment");
        });
        Test("a dropped or abandoned passenger does not pay",()=>{
            var t=FootSandbox();var p=Hailer(t,3.4,1.0);int id=p.Id;t.InteractJob();Tick(t,5);Check(t.JobStage==1,"passenger aboard");
            Place(t.Hero,0,60);Tick(t,.3);Check(t.InteractJob()&&t.JobKind==JobKind.None&&t.Money==0&&t.JobsFailed==1,"E away from the destination drops the passenger without a fare");
            var out1=t.Pedestrians.Find(x=>x.Id==id);Check(out1 is not null&&out1.Fear>.2,"the dropped passenger is annoyed and nearby");
            var q=Hailer(t,3.4,61);Tick(t,.4);Check(t.InteractJob()&&t.JobKind==JobKind.Taxi,"a second fare can be taken");Tick(t,5);Check(t.JobStage==1,"second passenger aboard");
            Check(t.InteractVehicle()&&t.OnFoot,"the driver gets out");Tick(t,10);Check(t.JobKind==JobKind.Taxi,"the passenger waits a while for the driver");
            Tick(t,17);Check(t.JobKind==JobKind.None&&t.JobsFailed==2&&t.Pedestrians.Any(x=>x.Id==q.Id),"after 25 s the passenger gets out and the job is lost");
        });
        Test("boarding is cancelled when the car drives away",()=>{
            var t=FootSandbox();var p=Hailer(t,3.4,1.0);t.InteractJob();Check(t.JobStage==0,"passenger walking");
            Place(t.Hero,0,40);Tick(t,.5);Check(t.JobKind==JobKind.None&&p.Boarding<0&&p.Hailing,"a car that leaves cancels the pickup and the hailer waits again");
        });
        Test("courier parcels between kiosks",()=>{
            var t=FootSandbox();var s=t.FoodSites.First(f=>t.FoodSites.Any(g=>g.Id!=f.Id&&(g.Interaction-f.Interaction).Length is>=120 and<=450));
            Place(t.Hero,s.Interaction.X,s.Interaction.Z-6);Call(t,"RebuildActorGrid");
            Check(t.NearbyJob() is {Kind:JobKind.Courier,Deliver:false} offer&&t.FoodSites.Any(g=>g.Interaction==offer.Target&&g.Id!=s.Id),"a stopped car at a kiosk is offered a parcel for another kiosk");
            var target=t.NearbyJob()!.Value.Target;int fare=t.NearbyJob()!.Value.Fare;
            Check(t.InteractJob()&&t.JobKind==JobKind.Courier&&t.JobStage==1&&t.JobFare==fare,"E takes the parcel");
            Check(t.NearbyJob() is null&&!t.InteractJob(),"E at the pickup kiosk does nothing more");
            Place(t.Hero,target.X,target.Z-6);Call(t,"RebuildActorGrid");Tick(t,.5);
            Check(t.NearbyJob() is {Kind:JobKind.Courier,Deliver:true},"the destination kiosk offers the hand-over");
            Check(t.InteractJob()&&t.JobKind==JobKind.None&&t.Money==fare&&t.JobsCompleted==1,"handing the parcel over pays the fee");
            Place(t.Hero,s.Interaction.X,s.Interaction.Z-6);Call(t,"RebuildActorGrid");Tick(t,.5);t.InteractJob();Check(t.JobKind==JobKind.Courier,"another parcel is available at once");
            Tick(t,150);Check(t.JobLate&&t.JobKind==JobKind.Courier,"a slow courier keeps the parcel but is late");
            Place(t.Hero,t.JobTarget.X,t.JobTarget.Z-6);Call(t,"RebuildActorGrid");Tick(t,.5);int before=t.Money,fare2=t.JobFare;t.InteractJob();
            Check(t.JobKind==JobKind.None&&t.Money-before==t.LastJobPay&&t.LastJobPay<=fare2*.5+5&&t.LastJobPay>=5,"a late delivery pays about half");
        });
        Test("on foot the parcel is handed over at the counter",()=>{
            var t=FootSandbox();var s=t.FoodSites.First(f=>t.FoodSites.Any(g=>g.Id!=f.Id&&(g.Interaction-f.Interaction).Length is>=120 and<=450));
            t.InteractVehicle();WalkPlace(t,s.Interaction.X,s.Interaction.Z+1.2);Tick(t,.3);
            Check(t.NearbyJob() is {Kind:JobKind.Courier}&&t.InteractJob()&&t.JobKind==JobKind.Courier,"a walker takes a parcel at the counter");
            WalkPlace(t,t.JobTarget.X,t.JobTarget.Z+1.2);Tick(t,.4);Check(t.InteractJob()&&t.JobKind==JobKind.None&&t.Money>0,"and hands it over at the other counter");
        });
        Test("survivors of a car hit can be taken to a clinic",()=>{
            var t=FootSandbox();var p=Person(t,0,6);t.HitPerson(p,t.Hero,3);
            Check(!p.Dead&&p.Injured>60&&p.KnockedFor>0,"a slow hit leaves a survivor injured");
            var fatal=Person(t,0,12);t.HitPerson(fatal,t.Hero,9);Check(fatal.Dead&&fatal.Injured==0,"a fatal hit is not an injury");
            var home=p.Position;Tick(t,30);
            Check(!p.Dead&&p.Injured>0&&(p.Position-home).Length<7&&t.Pedestrians.Contains(p),"the injured limp about where they fell");
            Place(t.Hero,3.5,p.Position.Z+.5);Call(t,"RebuildActorGrid");Tick(t,.2);
            Check(t.NearbyJob() is {Kind:JobKind.Patient} offer&&CityTraffic.ClinicBlocks.Any(c=>CityAmenities.ClinicEntrance(c.I,c.J)==offer.Target),"a stopped car beside a survivor offers a clinic run");
            Check(t.InteractJob()&&t.JobKind==JobKind.Patient,"E picks the patient up");Tick(t,8);
            Check(t.JobStage==1&&!t.Pedestrians.Contains(p),"the patient limps in and boards");
            var clinic=t.JobTarget;Place(t.Hero,clinic.X,clinic.Z+8.75-1.9);Tick(t,1.5);
            Check(t.JobKind==JobKind.None&&t.JobsCompleted==1&&t.Money>=35,"the clinic run pays");
            var healed=t.Pedestrians.Find(x=>x.Id==p.Id);Check(healed is not null&&healed.Injured==0,"the delivered patient walks into the clinic healed");
        });
        Test("job state travels through the save",()=>{
            var t=FootSandbox();var p=Hailer(t,3.4,1.0);t.InteractJob();Tick(t,5);Check(t.JobStage==1,"passenger aboard");
            var clock=CityState.Capture(t);var fresh=FootSandbox();CityState.Restore(fresh,clock);
            Check(fresh.JobKind==JobKind.Taxi&&fresh.JobStage==1&&fresh.JobTarget==t.JobTarget&&fresh.JobFare==t.JobFare&&Math.Abs(fresh.JobTimeLeft-t.JobTimeLeft)<1e-9,"the clock state carries the active job");
            var hailer=Hailer(t,-3.4,30);var state=CityState.Capture(hailer,compact:true);
            Check(state.ContainsKey(nameof(CityPedestrian.Hailing))&&state.ContainsKey(nameof(CityPedestrian.HailUntil))&&!state.ContainsKey(nameof(CityPedestrian.Boarding)),"a hailer's flags are saved and a default boarding is not");
        });
        Test("death ends the job",()=>{
            var t=FootSandbox();var p=Hailer(t,3.4,1.0);int id=p.Id;t.InteractJob();Tick(t,5);Check(t.JobStage==1,"passenger aboard");
            t.InteractVehicle();Tick(t,.3);t.DamagePlayer(100);Tick(t,4);
            Check(t.OnFoot&&!t.Player.Dead&&t.JobKind==JobKind.None&&t.JobsFailed==1&&t.Pedestrians.Any(x=>x.Id==id),"after the respawn the job is gone and the passenger is back on the street");
        });
    }
}
