using System.Text.Json;
using Promzona.Game;

static partial class Tests
{
    static void RunRefreshAudit()
    {
        Test("industrial siding has no wagon foundation steps", () => {
            foreach(int seed in new[]{73921,1,42}) {
                CityLayout.Install(CityLayout.Generate(seed,gridSize:49,wideRoads:true,roadRevision:3));
                double end=CityIndustrial.RailX1,z=CityIndustrial.RailZ,datum=CityTerrain.Ground(end-23,z),step=0,flat=0;string at="";
                for(double x=end-140;x<end+3;x+=.25)for(double dz=-1.25;dz<=1.25;dz+=.25){
                    double d=Math.Abs(CityTerrain.Ground(x+.25,z+dz)-CityTerrain.Ground(x,z+dz));if(d>step){step=d;at=$" at {x},{z+dz} end={end}";}
                    if(x>=end-44)flat=Math.Max(flat,Math.Abs(CityTerrain.Ground(x,z+dz)-datum));
                }
                Check(step<.15,"rail approach is continuous: "+seed+" step="+step+at);
                Check(flat<.01,"all wagon wheel positions share the siding datum: "+seed+" error="+flat);
            }
        });
        CityLayout.Install(CityLayout.Generate(73921));
        Test("empty owned weapons retain ammunition prices after loading", () => {
            var t = FootSandbox();
            foreach (var pair in new[] { (CityWeaponId.Pistol, ShopItem.Pistol, 40), (CityWeaponId.Automatic, ShopItem.Automatic, 80) }) {
                t.Inventory.Give(pair.Item1);
                for (int i = 0; i < 30; i++) t.Inventory.Consume(pair.Item1);
                var saved = JsonSerializer.Deserialize<Dictionary<CityWeaponId, int>>(JsonSerializer.Serialize(t.Inventory.Capture()))!;
                t.Inventory.Clear(); t.Inventory.Restore(saved);
                Check(t.Inventory.Active is null && t.Price(pair.Item2) == pair.Item3, "an owned empty weapon only needs ammunition: " + pair.Item1);
                t.Inventory.Clear();
                Check(t.Price(pair.Item2) > pair.Item3, "confiscation restores the full weapon price: " + pair.Item1);
            }
        });
        Test("respray always changes factory paint", () => {
            var t = FootSandbox();
            t.Hero.Id = Enumerable.Range(2, 200).First(id => CityAppearance.Paint(new CityCar { Id = id }) == CityAppearance.Slot(0));
            var site = t.Garages[0]; Place(t.Hero, site.Door.X, site.Door.Z - 5); Call(t, "RebuildActorGrid");
            var paint = CityAppearance.Paint(t.Hero); t.SetMoney(100); t.AddCrime(1);
            Check(t.InteractGarage() && CityAppearance.Paint(t.Hero) != paint && t.Money == 70 && t.Wanted == 0,
                "a factory-red car gets a different colour for its paid respray");
        });
        Test("passengers alight on the same road layer", () => {
            CityLayout.Install(CityLayout.Generate(73921, gridSize:49, wideRoads:true, roadRevision:3));
            foreach (var road in CityRoadLevels.All.Take(2)) {
                var t = FootSandbox(); var p = Hailer(t, 3.4, 1); int id = p.Id;
                t.InteractJob(); Tick(t, 5); Check(t.JobStage == 1, "passenger boards before visiting layer " + road.Id);
                Place(t.Hero, road.X, road.CentreZ(road.X) + 2); t.Hero.RoadLevel = road.Id; Call(t, "RebuildActorGrid");
                Check(t.InteractJob() && t.JobKind == JobKind.None, "passenger can leave a stopped car on layer " + road.Id);
                var back = t.Pedestrians.Single(x => x.Id == id);
                Check(back.RoadLevel == road.Id && Math.Abs(CityRoadLevels.Ground(back) - road.Floor(back.Position.X, back.Position.Z)) < .05,
                    "passenger stands on the deck or tunnel floor instead of the surface street: " + road.Id);
            }
        });
    }
}
