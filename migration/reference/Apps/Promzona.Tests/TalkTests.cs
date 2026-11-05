using Promzona.Game;
static partial class Tests
{
    // Talking to people on F (29.10.2026).
    static CityTraffic Walker(){var t=FootSandbox();t.InteractVehicle();WalkPlace(t,0,-30);t.PlayerViewHeading=0;Tick(t,.5);return t;}
    static CityPedestrian Facing(CityTraffic t,int persona,double ahead=1.3,double side=0){var p=Person(t,t.Player.Position.X+side,t.Player.Position.Z+ahead,persona);p.Wait=100;Call(t,"RebuildActorGrid");return p;}
    static void RunTalk(){
        CityLayout.Install(CityLayout.Generate(73921));
        Test("F addresses the person in front",()=>{
            var t=Walker();Check(t.NearbyTalker() is null&&!t.InteractVehicle()&&t.InteractionMessage=="near","with nobody around F still reports no car");
            var behind=Facing(t,0,-1.3);Check(t.NearbyTalker() is null,"a person behind the player is not addressed");
            var p=Facing(t,0);p.Id=90005;Tick(t,.6);
            Check(t.NearbyTalker()==p&&t.InteractVehicle()&&t.Talks==1&&t.SpeechUntil>t.Time,"the person ahead answers");
            Check(t.Speech=="ride"&&t.SpeechHasPlace&&p.Hailing&&p.HailUntil>t.Time,"this one wants a lift and waits for the car");
            Check(Math.Abs(Math.IEEERemainder(p.Heading-Math.PI,Math.Tau))<.1,"the person turns to face the player");
            Tick(t,.6);Check(t.InteractVehicle()&&t.Speech=="hailer","asked again, a hailer names the destination");
        });
        Test("roles have their own lines",()=>{
            var t=Walker();var o=Facing(t,5);Check(t.InteractVehicle()&&t.Speech=="officer","an officer moves the citizen along");
            t.AddCrime(1);Tick(t,.6);Check(t.InteractVehicle()&&t.Speech=="officer_wanted","a wanted player is shouted at");
            typeof(CityTraffic).GetProperty("Wanted")!.SetValue(t,0d);
            var t2=Walker();var e=Facing(t2,2);e.Id=90004;Check(t2.InteractVehicle()&&t2.Speech=="elder_clinic"&&t2.SpeechHasPlace,"an elder points to the clinic");
            var clinic=CityTraffic.ClinicBlocks.Select(c=>CityAmenities.ClinicEntrance(c.I,c.J)).OrderBy(c=>(c-e.Position).Length).First();
            Check(t2.SpeechPlace==clinic,"the nearest one");
            var t3=Walker();var tough=Facing(t3,3);Check(t3.InteractVehicle()&&t3.Speech=="tough","a tough brushes the player off by day");
            t3.Night=true;Tick(t3,.6);Check(t3.InteractVehicle()&&t3.Speech=="tough_night","and threatens at night");
            var t4=Walker();var hurt=Facing(t4,0);hurt.Injured=30;Check(t4.InteractVehicle()&&t4.Speech=="injured"&&t4.SpeechHasPlace,"an injured person asks for the clinic");
            var t5=Walker();var m=Facing(t5,0);m.Id=90003;Check(t5.InteractVehicle()&&t5.Speech=="market"&&t5.SpeechHasPlace,"directions to the nearest market");
        });
        Test("a drunk asks for five dollars and the next F gives them",()=>{
            var t=Walker();t.SetMoney(12);var d=Facing(t,9);
            Check(t.InteractVehicle()&&t.Speech=="drunk_ask"&&t.AskPending,"the drunk asks");
            Tick(t,.6);Check(t.InteractVehicle()&&t.Speech=="thanks"&&t.Money==7&&t.Gifts==1&&!t.AskPending,"F gives five");
            Tick(t,.6);Check(t.InteractVehicle()&&t.Speech=="drunk_ask","he asks again");
            Tick(t,5);Check(!t.AskPending,"the request expires");
            t.SetMoney(3);Tick(t,.6);t.InteractVehicle();Tick(t,.6);Check(t.InteractVehicle()&&t.Speech=="nocash"&&t.Money==3,"an empty wallet gives nothing");
            var h=Facing(t,10,1.2,.9);Tick(t,.6);t.PlayerViewHeading=Math.Atan2(.9,1.2);t.SetMoney(20);
            Check(t.InteractVehicle()&&t.Speech is "homeless_ask" or "drunk_ask","the homeless ask too");
        });
    }
}
