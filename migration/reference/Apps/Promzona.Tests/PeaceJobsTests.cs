using Promzona.Game;
using System.Text.Json;
static partial class Tests
{
    static void RunPeaceJobs()
    {
        void Time(CityTraffic t,double value)=>typeof(CityTraffic).GetProperty("Time")!.SetValue(t,value);
        void Step(CityTraffic t,double seconds){for(int n=0;n<(int)Math.Round(seconds*60);n++){Time(t,t.Time+1d/60);Call(t,"JobsStep",1d/60);}}
        CityWorldSave RoundTrip(CityTraffic t,CityModel m)=>JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(t.CaptureWorld(m),CityState.Json),CityState.Json)!;
        CityPoint StreetNear(CityPoint p)=>Enumerable.Range(-65,131).SelectMany(x=>Enumerable.Range(-65,131).Select(z=>p+new CityPoint(x,z)))
            .Where(q=>CityRoadSurface.Contains(q)&&!CityRoadLevels.Reserved(q,3)&&(q-p).Length<60).MinBy(q=>(q-p).Length);
        foreach(int seed in new[]{73921,42,1,19})Test("peace sites "+seed,()=>{
            CityLayout.Install(CityLayout.Generate(seed,gridSize:49,wideRoads:true,roadRevision:4));var t=FootSandbox();
            Check(t.StartPeaceJob(JobKind.Leaflets),"leaflet job can choose public sites on seed "+seed);
            if(!t.PeaceJobActive)return;
            for(int i=0;i<4;i++){
                var p=t.PeaceSite(i);Check(CityWater.UrbanSite(p,1)&&CityWater.Depth(p)<=.01&&!CityRoadSurface.Contains(p)&&!CityRoadLevels.Reserved(p,3),"site dry and off streets / structures");
                Check((bool)Call(t,"ClearPlayerPoint",p,null,null,null)!&&Enumerable.Range(0,i).All(j=>(t.PeaceSite(j)-p).Length>95),"clear separated interaction sites");
                Check((StreetNear(p)-p).Length<60,"nearby reachable street for broadcast");
            }
        });
        CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:4));
        Test("leaflets actual job progression",()=>{
            var t=FootSandbox();t.InteractVehicle();Check(t.StartPeaceJob(JobKind.Leaflets),"start leaflets on foot");
            Check(!t.StartPeaceJob(JobKind.PeaceBroadcast)&&!t.StartChainContract(),"only one contract at a time");
            var at=t.JobTarget;WalkPlace(t,at.X,at.Z);
            Check(t.Interact()&&t.PeaceEnabled,"E begins placement before other interactions");Step(t,1);
            Check(t.PeaceProgress>.9&&t.PeaceSitesDone==0,"placement needs time");
            WalkPlace(t,at.X+4,at.Z);Step(t,.1);Check(!t.PeaceEnabled&&t.PeaceProgress==0,"leaving circle cancels unfinished placement");
            WalkPlace(t,at.X,at.Z);t.PlayerFireHeld=true;t.Interact();Step(t,.5);Check(t.PeaceProgress==0,"shooting cannot place leaflets");t.PlayerFireHeld=false;
            for(int i=0;i<4;i++){
                at=t.JobTarget;WalkPlace(t,at.X,at.Z);Step(t,.4);Check(t.InteractPeaceJob(),"begin leaflet site "+i);Step(t,3.1);
                Check(t.PeaceSitesDone==i+1,"each completed site advances once");
            }
            Check(t.PeaceEscape&&t.WantedLevel>0&&t.Money==0,"route alone does not award money");
            Step(t,1);Check(t.Money==0,"pursuit blocks payout");t.ConsoleWanted(0);Step(t,.1);
            Check(t.JobKind==JobKind.None&&t.Money==300&&t.JobsCompleted==1,"escape pays exactly once");
            Step(t,2);Call(t,"CompleteContract");Check(t.Money==300&&t.JobsCompleted==1,"idle frames and duplicate completion do not repay");
        });
        Test("loudspeaker movement gate and interruption",()=>{
            var t=FootSandbox();t.Hero.Kind=12;Check(!t.StartPeaceJob(JobKind.PeaceBroadcast),"helicopter cannot take street PA job");t.Hero.Kind=0;
            Check(t.StartPeaceJob(JobKind.PeaceBroadcast)&&t.BroadcastCar==t.Hero,"parked personal car receives PA");
            var street=StreetNear(t.JobTarget);Place(t.Hero,street.X,street.Z);t.InteractPeaceJob();Step(t,9);
            Check(t.WantedLevel>0&&t.PeaceProgress==0,"parked noise attracts attention but earns no driving time");
            Check(t.Sounds.Any(s=>s.Name=="peace_pa"&&s.SpeakerId==CityTraffic.BroadcastSpeakerId),"positional PA voice has stable owner");
            for(int n=0;n<180;n++){Place(t.Hero,street.X,street.Z+Math.Sin(n*.03)*3,0,5);Step(t,1d/60);}
            Check(t.PeaceProgress>0&&t.PeaceProgress<4,"real street travel advances zone time");
            double before=t.PeaceProgress;for(int n=0;n<120;n++){t.Hero.Speed=5;Step(t,1d/60);}
            Check(t.PeaceProgress==before,"spinning speed without displacement cannot farm time");
            Place(t.Hero,street.X,street.Z);Step(t,.4);t.InteractPeaceJob();Check(!t.BroadcastAudible,"E switches PA off");
            Step(t,.4);t.InteractPeaceJob();Check(t.BroadcastAudible,"E restarts PA");
            t.InteractVehicle();Step(t,.1);Check(t.OnFoot&&!t.BroadcastAudible&&!t.PeaceEnabled,"exit stops speech immediately");
            t.Hero.Destroyed=true;Step(t,.1);Check(t.JobKind==JobKind.None&&t.JobsFailed==1,"destroyed assigned vehicle fails once");
        });
        Test("complete three loudspeaker districts",()=>{
            var t=FootSandbox();Check(t.StartPeaceJob(JobKind.PeaceBroadcast),"accept full PA route");
            t.InteractPeaceJob();
            for(int district=0;district<3;district++){
                var street=StreetNear(t.JobTarget);var direction=CityRoadSurface.Contains(street+new CityPoint(0,4))?new CityPoint(0,1):new CityPoint(1,0);
                for(int n=0;n<1900&&t.PeaceSitesDone==district;n++){
                    // Drive a bounded out-and-back stretch at 4 m/s using actual displacement.
                    double travel=n/60d*4,offset=2-Math.Abs(travel%8-4);
                    var p=street+direction*offset;Place(t.Hero,p.X,p.Z,0,4);Step(t,1d/60);
                }
                Check(t.PeaceSitesDone==district+1,"30 seconds of travel completes district "+district);
            }
            Check(t.PeaceEscape&&!t.BroadcastAudible&&t.WantedLevel>0&&t.Money==0,"PA stops at escape phase without early payment");
            t.ConsoleWanted(0);Step(t,.1);Check(t.Money==500&&t.JobsCompleted==1&&t.JobKind==JobKind.None,"full PA route plus escape pays 500 once");
            Step(t,.1);Check(t.Money==500,"completed PA cannot repay");
        });
        Test("peace progress roundtrip and legacy load",()=>{
            var m=new CityModel();var t=m.Traffic;t.GodMode=true;
            Check(t.StartPeaceJob(JobKind.Leaflets),"save fixture accepts job");
            var first=t.JobTarget;Call(t,"NextPeaceSite");
            var save=RoundTrip(t,m);CityTraffic.ValidateWorld(save);t.RestoreWorld(save,m);
            Check(t.PeaceSitesDone==1&&t.PeaceSite(0)==first&&t.JobTarget==t.PeaceSite(1)&&!t.PeaceEnabled,"save restores route and completed sites without auto-interaction");
            var old=RoundTrip(t,m);foreach(var key in old.Clock.Keys.Where(k=>k.StartsWith("_peace")).ToArray())old.Clock.Remove(key);
            t.RestoreWorld(old,m);Check(t.JobKind==JobKind.None&&t.PeaceSitesDone==0,"missing new fields cannot inherit a current mission");
            t.StartPeaceJob(JobKind.PeaceBroadcast);t.InteractPeaceJob();save=RoundTrip(t,m);t.RestoreWorld(save,m);
            Check(t.JobKind==JobKind.PeaceBroadcast&&!t.BroadcastAudible,"loading PA mission requires explicit E to broadcast again");
            t.CancelContract();Check(t.JobKind==JobKind.None&&t.JobsFailed==1,"cancel records one failure");t.CancelContract();Check(t.JobsFailed==1,"repeated cancel is harmless");
        });
        Test("timeout death and chain regression",()=>{
            var t=FootSandbox();t.StartPeaceJob(JobKind.Leaflets);Time(t,t.Time+721);Step(t,.1);
            Check(t.JobKind==JobKind.None&&t.JobsFailed==1&&t.Money==0,"expired mission pays nothing");
            t.StartPeaceJob(JobKind.Leaflets);Call(t,"JobsOnRespawn");Step(t,.1);
            Check(t.JobKind==JobKind.None&&t.JobsFailed==2,"death cancels peace job once");
            var m=new CityModel();t=m.Traffic;t.StartChainContract();Call(t,"CompleteContract");Step(t,.2);
            Check(t.ChainStage==1,"normal job updates no longer erase chain progress");
            t.RestoreWorld(RoundTrip(t,m),m);Step(t,.2);
            Check(t.ChainStage==1&&t.StartChainContract()&&t.JobKind==JobKind.Elimination,"saved chain continues after simulation resumes");
        });
    }
}
