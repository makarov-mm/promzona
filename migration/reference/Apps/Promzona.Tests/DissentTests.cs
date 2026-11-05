using Promzona.Game;
static partial class Tests
{
    static void DronePose(CityTraffic t,CityPoint p,double h){typeof(CityTraffic).GetProperty("DronePosition")!.SetValue(t,p);typeof(CityTraffic).GetProperty("DroneHeight")!.SetValue(t,h);}
    static void RunDissent(){
        Test("progressive wanted thresholds",()=>{
            var t=FootSandbox();double last=0;
            for(int level=0;level<5;level++){
                double width=CityTraffic.WantedPoints(level+1)-CityTraffic.WantedPoints(level);
                Check(width>last,"each later level costs more crimes");last=width;
                for(int n=0;n<10;n++){double x=level+n*.1;Check(Math.Abs(CityTraffic.WantedFromPoints(CityTraffic.WantedPoints(x))-x)<1e-9,"wanted conversion is reversible");}
            }
            t.AddCrime(3);Check(t.WantedLevel==3&&t.Wanted<2.2,"one large crime crosses variable intervals correctly");
            t.AddCrime(100);Check(t.Wanted==6&&t.PoliceBudget==9,"maximum remains bounded");
        });
        Test("escalating police dispatch",()=>{
            var m=new CityModel();var t=m.Traffic;t.GodMode=true;
            for(int level=1;level<=5;level++){
                t.ConsoleWanted(level);
                for(int n=0;n<20;n++)Call(t,"PoliceDispatchStep",1d);
                var kinds=t.Services.Where(s=>s.Kind==CityService.Police).Select(s=>t.Cars.Single(c=>c.Id==s.CarId).Kind).Order().ToArray();
                Check(kinds.SequenceEqual(CityTraffic.PoliceRoster(level).Order()),"actual dispatched roster at level "+level);
                Check(kinds.Length==t.PoliceBudget,"dispatch cap at level "+level);
            }
            var unit=t.Services.Last(s=>t.Cars.Single(c=>c.Id==s.CarId).Kind==14);var dead=t.Cars.Single(c=>c.Id==unit.CarId);dead.Destroyed=true;
            Call(t,"ServicesStep",.1);for(int n=0;n<10;n++)Call(t,"PoliceDispatchStep",1d);
            Check(t.Services.Any(s=>s.CarId!=dead.Id&&t.Cars.Any(c=>c.Id==s.CarId&&c.Kind==14)),"army losses receive replacements");
            var save=t.CaptureWorld(m);CityTraffic.ValidateWorld(save);t.RestoreWorld(save,m);
            Check(t.Cars.Any(c=>c.Kind==13)&&t.Cars.Any(c=>c.Kind==14),"FSB and armor persist in saves");
            foreach(int role in new[]{6,12,13}){var p=new CityPedestrian{Persona=role,Id=200};Check(p.Officer&&p.Weapon==CityWeaponId.Automatic,"response crew automatic "+role);}
        });
        Test("heli recovery can complete and relaunch",()=>{
            var t=Empty();var c=Car(t.World,12);t.Cars.Add(c);c.RecoveryTime=.001;c.Roll=Math.PI;
            for(int n=0;n<300;n++)t.Step(1d/60);
            Check(!c.Recovering&&Math.Abs(c.Roll)<.01,"helicopter no longer skips recovery state machine");
            t.HelicopterLiftInput=1;for(int n=0;n<240;n++)t.Step(1d/60);
            Check(c.Lift>12,"recovered helicopter takes off");
            t.HelicopterLiftInput=0;t.ToggleHelicopterLanding();c.Body.Vx=8;
            for(int n=0;n<1000;n++)t.Step(1d/60,new(1,1));
            Check(c.Lift==0&&Math.Abs(c.Body.Vx)<.1&&Math.Abs(c.Body.Vy)<.1,"automatic landing brakes horizontal thrust and reaches ground");
            t.HelicopterLiftInput=1;for(int n=0;n<180;n++)t.Step(1d/60);
            Check(!t.HelicopterLanding&&c.Lift>8,"Q cancels landing and climbs again");
        });
        Test("drone possession mission and loss",()=>{
            var m=new CityModel();var t=m.Traffic;t.GodMode=true;
            t.ToggleDrone();Check(!t.DroneActive,"drone requires leaving vehicle");
            Check(t.InteractVehicle()&&t.OnFoot,"operator exits");
            t.ToggleDrone();Check(t.DroneActive&&t.DroneTargets.Length==3,"K opens local three-target mission");
            var body=t.PlayerPosition;double health=t.PlayerHealth;int cash=t.Money;
            for(int n=0;n<3;n++){
                var b=t.DroneTargets[n];double ground=CityTerrain.Ground(b.X,b.Z);
                DronePose(t,new(b.X,b.Z-3),ground+b.Height+8);
                t.DroneHeading=0;t.DronePitch=Math.Atan2(7.3,3);t.DroneScanHeld=true;
                for(int k=0;k<21;k++){Clock(t,t.Time+.1);Call(t,"DroneStep",.1);}
                Check(t.DroneScans==n+1,"visible roof records once "+n);
            }
            Check(!t.DroneActive&&t.Money==cash+200&&t.DroneNotice=="complete","third scan pays exactly once and returns control");
            Call(t,"DroneStep",1d);Check(t.Money==cash+200,"completed mission cannot repeat payment");
            Check(t.PlayerPosition==body&&t.PlayerHealth==health,"remote input leaves operator at launch site");
            t.ToggleDrone();Check(!t.DroneActive,"completed mission cooldown");Clock(t,t.Time+61);t.ToggleDrone();
            t.DamageDrone(1);Check(!t.DroneActive&&t.DroneNotice=="lost"&&t.PlayerHealth==health,"drone shot down returns to healthy operator");
            Clock(t,t.Time+21);t.ToggleDrone();t.DroneInput=new(1,0);t.DroneLiftInput=1;t.DroneScanHeld=true;t.ClearPlayerInput();
            Check(t.DroneInput.Length==0&&t.DroneLiftInput==0&&!t.DroneScanHeld,"focus loss clears remote controls");
            var save=t.CaptureWorld(m);t.RestoreWorld(save,m);Check(!t.DroneActive,"load restores operator, not an orphan drone");
        });
        Test("drone obstacles range and battery",()=>{
            var m=new CityModel();var t=m.Traffic;t.InteractVehicle();t.ToggleDrone();
            var b=t.DroneTargets[0];DronePose(t,new(b.X-b.HalfX-1,b.Z),CityTerrain.Ground(b.X,b.Z)+3);
            t.DroneInput=new(1,0);for(int n=0;n<60&&t.DroneActive;n++)Call(t,"DroneStep",1d/60);
            Check(t.DronePosition.X<b.X-b.HalfX-.3,"swept drone movement stops at facade");
            t.DroneInput=default;DronePose(t,new(b.X,b.Z),CityTerrain.Ground(b.X,b.Z)+b.Height+8);
            t.DroneScanHeld=false;for(int n=0;n<1100&&t.DroneActive;n++)Call(t,"DroneStep",.1);
            Check(!t.DroneActive&&t.DroneNotice=="battery","finite battery returns control");
        });
        Test("civic buildings and vehicle dimensions",()=>{
            Check(CityCivicSites.Offices().Any(),"generated city includes enlistment offices");
            foreach(var (_,b) in CityCivicSites.Offices())Check(CityCivicSites.BlankEnds(b)&&CityWater.Depth(new(b.X,b.Z))<.1,"office uses an existing dry building");
            foreach(int kind in new[]{3,6,7,13,14}){var c=new CityCar{Id=90000,Kind=kind};Check(c.HalfWidth>.8&&c.HalfWidth<1.5&&c.Height<3&&c.Mass>=1600&&c.PoliceVehicle,"service vehicle dimensions "+kind);}
        });
    }
}

