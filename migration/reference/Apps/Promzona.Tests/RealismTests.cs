using Promzona.Game;
static partial class Tests {
 static void RunRealism(){
  Test("measured people and vehicles",()=>{
   var people=Enumerable.Range(1,100).Select(id=>new CityPedestrian{Id=id}).ToArray();
   Check(people.All(p=>p.Height>=1.60&&p.Height<=1.82&&p.ShoulderWidth>=.40&&p.ShoulderWidth<=.51),"adults have bounded metre dimensions");
   Check(Math.Abs(new CityCar().HalfLength*2-4.1)<1e-9,"sedan length matches adult scale");
   Check(people.Count(p=>p.Armed)==10,"exactly ten percent of ordinary identifiers carry weapons");
   foreach(var p in people)p.Persona=3;Check(people.Count(p=>p.Armed)==75,"three quarters of tough group identifiers carry weapons");
  });
  Test("local contact bubble and global routes",()=>{
   var t=new CityTraffic();var car=t.Cars.First(c=>!c.Physical&&!c.Hero);var p=t.Pedestrians.First(p=>!p.Physical);var initial=car.Position;var start=p.Position;
   Check(t.PhysicalCars<t.Cars.Count/4&&t.PhysicalPeople<t.Pedestrians.Count/4,"distant actors excluded from detailed simulation");
   Check(!t.World.Bodies.Contains(car.Body)&&!t.World.Bodies.Contains(p.Body),"far actor bodies absent from contact world");
   for(int k=0;k<300;k++)t.Step(1d/60);
   Check(t.Cars.Where(c=>!c.Physical).Any(c=>c.Distance>3)&&t.Pedestrians.Where(p=>!p.Physical).Any(p=>p.Distance>1),"far route traffic and walkers keep progressing");
   Check(t.World.Bodies.Count<900,"contact world stays bounded around player");
   var hero=t.Hero;hero.Body.X=car.Position.X+12;hero.Body.Y=car.Position.Z;hero.Position=new(hero.Body.X,hero.Body.Y);t.Step(.1);t.Step(.1);t.Step(.1);
   Check(car.Physical&&t.World.Bodies.Contains(car.Body),"approaching distant traffic activates its physics");
   CheckFinite(t);
   hero.Body.X=0;hero.Body.Y=0;hero.Position=new(0,0);for(int k=0;k<4;k++)t.Step(.1);
   Check(!car.Physical&&!t.World.Bodies.Contains(car.Body),"leaving contact bubble unloads bodies again");
  });
  Test("contact bubble repeated district transitions",()=>{
   var t=new CityTraffic();int maxBodies=0;bool bounded=true,finite=true;
   for(int k=0;k<3600;k++){
    if(k%300==0){int i=5+(k/300*7)%23,j=5+(k/300*11)%23;
     bool placed=false;
     for(int search=0;search<80&&!placed;search++){
      double x=CityTraffic.Coordinate(i)+1.9,z=CityTraffic.Coordinate(j)+8+search*.55;
      var at=new CityPoint(x,z);
      if(t.Cars.Any(c=>!c.Hero&&(c.Position-at).Length<c.HalfLength+5)||t.Pedestrians.Any(p=>(p.Position-at).Length<3)||t.Props.Any(p=>(p.Position-at).Length<4))continue;
      if(CityModel.Blocks.Any(b=>Math.Abs(b.X-x)<b.HalfX+3&&Math.Abs(b.Z-z)<b.HalfZ+3))continue;
      if(!CityTraffic.HasRoad(i,j,0)&&z>CityTraffic.Coordinate(j)+12)continue;
      Place(t.Hero,x,z);placed=true;
     }
     if(!placed)throw new Exception("No clear district test landing");
    }
    t.Step(1d/30);maxBodies=Math.Max(maxBodies,t.World.Bodies.Count);
    bounded&=t.Cars.Count<=CityTraffic.CarCap+7&&t.Pedestrians.Count<=CityTraffic.PeopleCap+4&&t.Animals.Count<=CityTraffic.AnimalCap;
    bool currentFinite=t.Cars.All(c=>double.IsFinite(c.Body.X+c.Body.Y+c.Speed)&&Math.Abs(c.Speed)<100)&&t.Pedestrians.All(p=>double.IsFinite(p.Position.X+p.Position.Z+p.Speed)&&p.Speed<50);
    if(finite&&!currentFinite){foreach(var p in t.Pedestrians.Where(p=>!double.IsFinite(p.Position.X+p.Position.Z+p.Speed)||p.Speed>=50))Console.WriteLine($"Bad walker k={k} id={p.Id} pos={p.Position} v={p.Speed} body={p.Body.Vx}/{p.Body.Vy} dead={p.Dead} detached={p.Detached} physical={p.Physical}");}
    finite&=currentFinite;
   }
   Console.WriteLine($"120 s / twelve districts: max local bodies {maxBodies}; population {t.Cars.Count}/{t.Pedestrians.Count}/{t.Animals.Count}");
   Check(finite&&bounded,"repeated physics LOD transitions keep actors finite and bounded");
   Check(maxBodies<1000,"physics body count stays local while travelling the city");
   Check(t.Cars.Where(c=>!c.Physical).All(c=>!t.World.Bodies.Contains(c.Body)),"no distant car leaks back into solver after spawning");
   Check(t.Animals.Where(a=>!a.Physical&&a.Body!=null).All(a=>!t.World.Bodies.Contains(a.Body!)),"no far landing bird leaks a collider");
  });
  Test("directional animal fear rules",()=>{
   foreach(var bird in new[]{AnimalKind.Pigeon,AnimalKind.Bird})foreach(var threat in new[]{CityThreat.Person,CityThreat.Cat,CityThreat.Dog})Check(CityReactions.Animal(bird,threat).Fly,"birds flee people and predators: "+bird+" / "+threat);
   Check(CityReactions.Animal(AnimalKind.Cat,CityThreat.Person).Radius>0&&CityReactions.Animal(AnimalKind.Cat,CityThreat.Dog).Radius>0,"cats fear approaching people and dogs");
   Check(CityReactions.Animal(AnimalKind.Dog,CityThreat.Cat).Radius==0&&CityReactions.Animal(AnimalKind.Cat,CityThreat.Cat).Radius==0,"fear relationships are directional, not all against all");
   var t=new CityTraffic();var flocks=t.Animals.Where(a=>a.Kind==AnimalKind.Pigeon).GroupBy(a=>a.Flock).ToArray();
   Check(flocks.Length>3&&flocks.All(g=>g.Key>=0&&g.Count()>=3&&g.Count()<=8),"pigeons spawn as independent actors in bounded flocks");
   Check(t.Props.Any(p=>p.Kind==StreetKind.Can)&&t.Props.Any(p=>p.Kind==StreetKind.Bottle),"random lawn litter contains cans and bottles");
  });
  Test("animals react in simulation",()=>{
   var t=Empty();t.Cars.Add(Car(t.World,0,50,50));
   CityAnimal Animal(AnimalKind kind,double x,double z,int flock=-1){var a=new CityAnimal{Id=80000+t.Animals.Count,Kind=kind,Position=new(x,z),Target=new(x,z),Wait=100,FlyAt=100,Flock=flock};int b=t.World.AddCircle(x,z,a.Radius,a.Mass/(Math.PI*a.Radius*a.Radius),.13,.04,0);a.Body=t.World.Bodies[b];a.Body.InvI=0;t.Animals.Add(a);return a;}
   var cat=Animal(AnimalKind.Cat,0,0);var dog=Animal(AnimalKind.Dog,3,0);var bird=Animal(AnimalKind.Pigeon,-2,1,7);var mate=Animal(AnimalKind.Pigeon,-3,1,7);
   for(int i=0;i<30;i++)t.Step(1d/120);
   Check(cat.Fear>.3&&cat.FearFrom.X>1,"cat recognizes nearby dog as a threat");
   Check(bird.Flying&&mate.Flying,"predator scares a whole flock into flight");
   dog.Body!.X=40;dog.Body.Y=40;dog.Position=new(40,40);double fear=cat.Fear;for(int i=0;i<120;i++)t.Step(1d/120);
   Check(cat.Fear<fear,"fear fades when the threatening animal leaves");
   var person=Person(t,15,0);person.Wait=100;var second=Animal(AnimalKind.Cat,16,0);
   for(int i=0;i<20;i++)t.Step(1d/120);Check(second.Fear>.2,"nearby human scares a cat");
  });
  Test("armed response and obstruction",()=>{
   var t=Empty();var car=Car(t.World,0,0,12);t.Cars.Add(car);var p=Person(t,0,0);p.Id=90000;p.AggressionUntil=20;p.Wait=100;
   for(int i=0;i<30;i++)t.Step(1d/120);Check(t.ShotsFired==0,"drawing and aiming delay prevents instantaneous shooting");
   for(int i=0;i<240;i++)t.Step(1d/120);Check(t.ShotsFired>0&&car.Damage>0,"armed aiming produces bounded shots and vehicle damage");
   p.AggressionUntil=0;int shots=t.ShotsFired;for(int i=0;i<120;i++)t.Step(1d/120);Check(t.ShotsFired==shots&&t.Shots.Count==0,"calm actor stops firing and tracers expire");
   p.Id=90001;p.AggressionUntil=t.Time+10;for(int i=0;i<120;i++)t.Step(1d/120);Check(t.ShotsFired==shots,"unarmed civilian never fires");
   var b=CityModel.Blocks.First(b=>b.Height>4);Check(!t.ClearSight(new(b.X-b.HalfX-2,b.Z),new(b.X+b.HalfX+2,b.Z)),"solid building blocks sight and gunfire");
  });
  Test("unpinned pothole distribution",()=>{
   var sets=(from i in Enumerable.Range(0,15) from j in Enumerable.Range(0,15) select CityRoadDetail.Holes(i,j).ToArray()).ToArray();
   Check(sets.Any(s=>s.Length==0)&&sets.Any(s=>s.Length==1)&&sets.Any(s=>s.Length>1),"road damage has zero one and multiple random holes");
   var all=sets.SelectMany(s=>s).ToArray();Check(all.Length>60&&all.All(h=>h.Size>=.48&&h.Size<=1.13),"pothole sizes are bounded");
   Check(CityRoadDetail.Holes(5,5).SequenceEqual(CityRoadDetail.Holes(5,5)),"visual and contact potholes share deterministic positions");
  });
 }
}
