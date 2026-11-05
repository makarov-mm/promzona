using Promzona.Game;
static partial class Tests
{
    // Garage and market (29.10.2026): where the money goes.
    static void RunMarket(){
        CityLayout.Install(CityLayout.Generate(73921));
        Test("garages and stalls stand clear on dry land",()=>{
            var layout=CityLayout.Current;var garages=CityAmenities.Garages();var markets=CityAmenities.Markets();
            Console.WriteLine($"garages={garages.Count}, stalls={markets.Count}");
            Check(garages.Count>=3&&markets.Count>=2,"a 33-node city has several garages and a market");
            Check(garages.All(s=>CityWater.UrbanSite(s.Position,4)&&layout.Blocks.All(b=>Math.Abs(b.X-s.Position.X)>=b.HalfX+GarageSite.HalfX||Math.Abs(b.Z-s.Position.Z)>=b.HalfZ+GarageSite.HalfZ)),"garage boxes overlap no building");
            Check(markets.All(s=>CityWater.UrbanSite(s.Position,2)&&layout.Park((int)Math.Floor((s.Position.X-CityTraffic.Coordinate(0))/36),(int)Math.Floor((s.Position.Z-CityTraffic.Coordinate(0))/36))),"stalls stand in parks");
            Check(garages.All(s=>CityAmenities.Food().All(f=>(f.Position-s.Position).Length>5)&&CityAmenities.Stops().All(p=>(p-s.Position).Length>5)),"garages keep away from kiosks and bus stops");
            Check(layout.Furniture.All(f=>CityAmenities.ServiceClear(new(f.X,f.Z),.2)),"no street furniture inside a garage or a stall");
            var t=new CityTraffic();Check(t.Props.All(p=>CityAmenities.ServiceClear(p.Home,p.Radius)),"no movable prop homes inside them either");
            // Static contacts stream around nearby actors, not across the whole city.
            var garage=garages[0];Place(t.Hero,garage.Door.X,garage.Door.Z-.5);Call(t,"RefreshDetail");Call(t,"RefreshScenery");
            Check(t.World.Bodies.Any(b=>Math.Abs(b.X-garage.Position.X)<.01&&Math.Abs(b.Y-garage.Position.Z)<.01&&b.IsStatic),"a nearby garage box is solid");
        });
        Test("the garage repairs and resprays for money",()=>{
            var t=FootSandbox();var s=t.Garages.First(g=>t.FoodSites.All(f=>(f.Interaction-g.Door).Length>16));Place(t.Hero,s.Door.X,s.Door.Z-5);Call(t,"RebuildActorGrid");
            Check(t.NearbyGarage() is {} g&&g.Id==s.Id,"a car stopped on the forecourt is served");
            Check(t.GarageCost==0&&!t.Interact()&&t.InteractionMessage=="garage_fine","an undamaged, unwanted car has nothing to pay for");
            t.Hero.Damage=.5;t.SetMoney(100);Tick(t,.6);Check(t.RepairCost==60&&t.Interact()&&t.Hero.Damage==0&&t.Money==40&&t.RepairsBought==1,"repair costs sixty for half the damage");
            t.AddCrime(1);Tick(t,.6);Check(t.ResprayOffered&&t.GarageCost==CityTraffic.ResprayPrice,"a wanted driver is offered a respray");
            var before=CityAppearance.Paint(t.Hero);Check(t.Interact()&&t.Hero.Paint==1&&t.Wanted==0&&t.Money==10&&t.Resprays==1&&CityAppearance.Paint(t.Hero)!=before,"the respray changes the colour and ends the pursuit");
            t.Hero.Damage=.9;Tick(t,.6);Check(!t.Interact()&&t.InteractionMessage=="poor"&&t.Hero.Damage==.9,"no money, no repair");
            t.Hero.Burning=true;t.Hero.FireAge=4;t.SetMoney(500);Tick(t,.6);Check(t.Interact()&&!t.Hero.Burning&&t.Hero.Damage==0,"a repair puts the fire out");
            Place(t.Hero,s.Door.X+40,s.Door.Z-5);Call(t,"RebuildActorGrid");Check(t.NearbyGarage() is null,"forty metres away the garage is out of reach");
            Check(CityAppearance.Paint(new CityCar{Id=1})!=CityAppearance.Paint(new CityCar{Id=1,Paint=3})&&CityAppearance.Paint(new CityCar{Id=1,Paint=3})==CityAppearance.Slot(2),"the paint slot selects the palette colour");
        });
        Test("the market sells arms, pills and drinks",()=>{
            var t=FootSandbox();t.InteractVehicle();var s=t.Markets[0];WalkPlace(t,s.Interaction.X,s.Interaction.Z);Tick(t,.1);
            Check(t.NearbyMarket() is {} m&&m.Id==s.Id,"a walker at the counter is a customer");
            t.SetMoney(500);Check(t.Price(ShopItem.Pistol)==120&&t.Buy(ShopItem.Pistol)&&t.Money==380&&t.Inventory.Ammo(CityWeaponId.Pistol)==30,"the first pistol comes with thirty rounds for 120");
            Check(t.Price(ShopItem.Pistol)==40&&t.Buy(ShopItem.Pistol)&&t.Money==340&&t.Inventory.Ammo(CityWeaponId.Pistol)==60,"rounds alone cost forty");
            Check(!t.Buy(ShopItem.Pills)&&t.Money==340&&t.InteractionMessage=="full","pills are not sold to a healthy customer");
            t.Player.GunDamage=.5;Check(t.Buy(ShopItem.Pills)&&t.Money==315&&Math.Abs(t.Player.GunDamage-.15)<1e-9,"pills heal thirty-five points for 25");
            Check(t.Buy(ShopItem.EnergyDrink)&&t.Money==300&&t.EnergySeconds==25&&!t.Buy(ShopItem.EnergyDrink)&&t.Money==300,"one drink at a time");
            t.SetMoney(10);Check(!t.Buy(ShopItem.Automatic)&&t.InteractionMessage=="poor"&&t.Inventory.Ammo(CityWeaponId.Automatic)==0,"no money, no rifle");
            Check(t.Purchases==4,"four purchases counted");
            WalkPlace(t,s.Interaction.X+30,s.Interaction.Z);Check(t.NearbyMarket() is null&&!t.Buy(ShopItem.Pills),"nothing is sold away from the counter");
        });
    }
}
