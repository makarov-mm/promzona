using Promzona.Game;
using System.Reflection;
static partial class Tests {
 static void RunCrowdLod(){
  Test("expanded populations across seeds",()=>{
   foreach(int seed in new[]{73921,48115,90210}){
    CityLayout.Install(CityLayout.Generate(seed));var t=new CityTraffic();
    Console.WriteLine($"Seed {seed}: cars {t.Cars.Count}; passenger {t.OriginalPassengers}->{t.Cars.Count(c=>!c.Hero&&c.Kind==0)}; people {t.Pedestrians.Count}; pigeon {t.OriginalPigeons}->{t.Animals.Count(a=>a.Kind==AnimalKind.Pigeon)}");
    Check(t.Cars.Count(c=>!c.Hero&&c.Kind==0)==(t.OriginalPassengers*3+1)/2,"passenger cars alone increase by half: "+seed);
    Check(t.Cars.Count(c=>c.Kind!=0&&c.TransitRoute==0)==t.OriginalOtherCars,"other vehicle population preserved: "+seed);
    Check(t.Pedestrians.Count==4860,"people increase from 3240 to 4860: "+seed);
    Check(t.Animals.Count(a=>a.Kind==AnimalKind.Pigeon)==t.OriginalPigeons*3,"pigeons triple without tripling other animals: "+seed);
    Check(t.Animals.Where(a=>a.Kind==AnimalKind.Pigeon).GroupBy(a=>a.Flock).All(g=>g.Count()>=4&&g.Count()<=8),"pigeons remain in bounded flocks: "+seed);
    Check(t.Pedestrians.Skip(3240).All(p=>!CityTraffic.CarPersonOverlap(t.Hero.Position,t.Hero.Heading,p.Position,.5,t.Hero.HalfLength,t.Hero.HalfWidth)),"extra walkers do not spawn in player car: "+seed);
   }CityLayout.Install(CityLayout.Generate(73921));
  });
  Test("paint identity",()=>{
   foreach(int model in new[]{0,1,2}){
    var choices=Enumerable.Range(2,1800).Where(id=>id%3==model).Select(id=>CityAppearance.Paint(new(){Id=id,Kind=0})).Distinct().Count();
    Check(choices==12,"every passenger family has all twelve body colours: "+model);
   }
   var c=new CityCar{Id=912,Kind=0};var paint=CityAppearance.Paint(c);c.Position=new(400,50);c.Heading=2;c.Physical=false;
   Check(CityAppearance.Paint(c)==paint,"distance and physics LOD never change paint");
   for(int k=0;k<6;k++){c.Kind=k;Check(CityAppearance.BodySlot(k)>=0&&CityAppearance.BodySlot(k)<CityAppearance.PaletteWidth(k),"valid authored body slot for vehicle kind "+k);}
   c.Destroyed=true;Check(CityAppearance.Paint(c)==(.055f,.050f,.045f),"wreck paint shared across LOD");
   Check(CityLod.Vehicle==127.5f&&CityLod.Actor==187.5f&&CityLod.ActorVisible>CityLod.Actor,"detail radius grows by 50 percent without actors disappearing first");
  });
  Test("crowd correction cannot launch an upright walker",()=>{
   var t=Empty();var c=Car(t.World,0,20,20);t.Cars.Add(c);var p=Person(t,0,0);t.World.AddBox(.05,.05,.7,.7,0,.3,.1,0);
   for(int k=0;k<30;k++){t.Step(.05);Check(p.Speed<=12.000001,"penetration correction retains walking speed bound");}
   Check(!p.Dead&&!p.Detached,"overlap correction does not invent a vehicle hit");
  });
  Test("impact invalidates speech",()=>{
   var t=QuietCity();var p=Person(t,-6.5,8);int revision=p.VoiceRevision;t.HitPerson(p,t.Hero,3);
   Check(!p.CanSpeak&&p.VoiceRevision==revision+1,"nonfatal hit immediately invalidates in-flight phrase");
   Check(t.Sounds.Any(s=>s.Name=="impact")&&!t.Sounds.Any(s=>s.Name is "fear" or "tough_fear"),"hit queues impact and never starts post-hit speech");
   var q=Person(t,-6.5,14);t.HitPerson(q,t.Hero,8);Check(q.Dead&&!q.CanSpeak,"fatal hit cannot start or continue speech");
  });
 }
}
