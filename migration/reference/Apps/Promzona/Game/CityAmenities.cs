namespace Promzona.Game;

public static class CityAmenities
{
    private static CityLayout? _layout;
    private static FoodSite[] _food = [];
    private static CityPoint[] _stops = [];
    private static GarageSite[] _garages = [];
    private static MarketSite[] _markets = [];
    private readonly record struct WalkBox(CityPoint Centre, double HalfX, double HalfZ);
    private static readonly Dictionary<(int, int), List<WalkBox>> _walkCells = [];

    private static void Ensure()
    {
        if (ReferenceEquals(_layout, CityLayout.Current)) return;
        _layout = CityLayout.Current;
        _food = [.. BuildFood()];
        _stops = [.. BuildStops()];
        _garages = [.. BuildGarages()];
        _markets = [.. BuildMarkets()];
        BuildWalkCells();
    }

    private static void BuildWalkCells()
    {
        // Immutable site footprints are indexed once per generated/loaded layout.
        // Store a box in every touched cell, including boundary cells. The exact
        // strict containment test below remains authoritative, not the grid.
        _walkCells.Clear();

        foreach (CityPoint s in _stops)
        {
            Add(BusStopFixture.WalkCentre(s), BusStopFixture.WalkHalfSize.X, BusStopFixture.WalkHalfSize.Z);
        }

        foreach (FoodSite s in _food)
        {
            var half = s.Fixture.WalkHalfSize;
            Add(s.Position, half.X, half.Z);
        }

        foreach (GarageSite s in _garages)
        {
            Add(s.Position, GarageSite.HalfX, GarageSite.HalfZ);
        }

        foreach (MarketSite s in _markets)
        {
            Add(s.Position, MarketSite.HalfX, MarketSite.HalfZ);
        }

        return;

        static void Add(CityPoint p, double hx, double hz)
        {
            for (int x = (int)Math.Floor((p.X - hx) / 24); x <= (int)Math.Floor((p.X + hx) / 24); x++)
            {
                for (int z = (int)Math.Floor((p.Z - hz) / 24); z <= (int)Math.Floor((p.Z + hz) / 24); z++)
                {
                    if (!_walkCells.TryGetValue((x, z), out List<WalkBox>? boxes))
                    {
                        _walkCells[(x, z)] = boxes = [];
                    }

                    boxes.Add(new WalkBox(p, hx, hz));
                }
            }
        }
    }

    public static IReadOnlyList<FoodSite> Food()
    {
        Ensure();
        return _food;
    }

    public static IReadOnlyList<CityPoint> Stops()
    {
        Ensure();
        return _stops;
    }

    public static IReadOnlyList<GarageSite> Garages()
    {
        Ensure();
        return _garages;
    }

    public static IReadOnlyList<MarketSite> Markets()
    {
        Ensure();
        return _markets;
    }

    private static bool SiteClear(CityPoint p, double halfX, double halfZ, double margin)
    {
        if (CityRoadLevels.Reserved(p, Math.Max(halfX, halfZ) + 1) || !CityWater.UrbanSite(p, Math.Max(halfX, halfZ) + 1) || CityWater.Depth(p) > .01) return false;
        if (CityLayout.Current.Blocks.Any(b => Math.Abs(b.X - p.X) < b.HalfX + halfX + margin && Math.Abs(b.Z - p.Z) < b.HalfZ + halfZ + margin)) return false;
        // A kiosk within ten metres would offer a parcel on the same E as the forecourt.
        if (_food.Any(s => (s.Interaction - p).Length < halfX + 10)) return false;
        if (_stops.Any(s => Math.Abs(s.X - p.X) < halfX + 5 && Math.Abs(s.Z - p.Z) < halfZ + 4)) return false;
        return ClinicClear(p) && (StationEntrance() - p).Length > 12;
    }

    private static IEnumerable<GarageSite> BuildGarages(){
        int id=0;
        for(int j=1;j<CityTraffic.Nodes-2;j++)for(int i=1;i<CityTraffic.Nodes-2;i++){
            // The cooperatives of the industrial district: two boxes side by side along the street.
            if(CityLayout.Current.Industrial(i,j)){
                if(!CityIndustrial.IsCooperative(i,j)||!CityTraffic.HasRoad(i,j,1))continue;
                for(int n=-1;n<=1;n+=2)yield return new GarageSite(++id,new CityPoint(CityTraffic.BlockCentre(i)+n * (GarageSite.HalfX + .4),CityTraffic.Coordinate(j)+CityTraffic.Sidewalk+GarageSite.HalfZ+.6));
                continue;
            }
            // Garage cooperatives take vacant lots: built-up blocks have no room at the road edge.
            if(CityLayout.CanalColumn(i)||(i*5+j*7)%11!=0||CityTraffic.Nodes>=49&&(i+j)%2!=0||!CityLayout.Current.Park(i,j)||!CityTraffic.HasRoad(i,j,1))continue;
            var p=new CityPoint(CityTraffic.BlockCentre(i),CityTraffic.Coordinate(j)+CityTraffic.Sidewalk+GarageSite.HalfZ+.6);
            if(!SiteClear(p,GarageSite.HalfX,GarageSite.HalfZ,2.5))continue;
            yield return new GarageSite(++id,p);
        }
    }
    private static IEnumerable<MarketSite> BuildMarkets(){
        int id=0;
        for(int j=1;j<CityTraffic.Nodes-2;j++)for(int i=1;i<CityTraffic.Nodes-2;i++){
            bool zone=CityLayout.Current.Industrial(i,j);
            if(zone?(i,j)!=CityIndustrial.Market||!CityTraffic.HasRoad(i,j,1):CityLayout.CanalColumn(i)||(i*3+j*5)%31!=0||!CityLayout.Current.Park(i,j)||!CityTraffic.HasRoad(i,j,1))continue;
            // Three stalls in a row along the park's road edge make a small market.
            for(int n=-1;n<=1;n++){
                var p=new CityPoint(CityTraffic.BlockCentre(i)+n * (2 * MarketSite.HalfX + 1.2),CityTraffic.Coordinate(j)+CityTraffic.Sidewalk+MarketSite.HalfZ+.5);
                if(!SiteClear(p,MarketSite.HalfX,MarketSite.HalfZ,1.5))continue;
                yield return new MarketSite(++id,p);
            }
        }
    }
    private static IEnumerable<FoodSite> BuildFood(){
        int id=0;
        for(int j=1;j<CityTraffic.Nodes-2;j++)for(int i=1;i<CityTraffic.Nodes-2;i++){
            if(CityLayout.CanalColumn(i)||(i*7+j*11)%23!=0||CityTraffic.IsClinic(i,j)||CityTraffic.IsStation(i,j))continue;
            var p=new CityPoint(CityTraffic.BlockCentre(i),CityTraffic.Coordinate(j)+CityTraffic.Sidewalk+.4);
            bool shop=CityLayout.Current.Park(i,j)&&(i+j)%2==0;
            var site=new FoodSite(++id,p,shop);
            if(CityRoadLevels.Reserved(p,4)||!CityWater.UrbanSite(p,3)||CityWater.Depth(p)>.01)continue;
            if(CityLayout.Current.Blocks.Any(b=>Math.Abs(b.X-p.X)<b.HalfX+2.5&&Math.Abs(b.Z-p.Z)<b.HalfZ+3.5))continue;
            yield return site;
        }
    }
    private static IEnumerable<CityPoint> BuildStops()
    {
        for (int j = 1; j < CityTraffic.Nodes - 2; j += 3)
        for (int i = 1; i < CityTraffic.Nodes - 2; i += 4)
        {
            if (!CityTraffic.HasRoad(i, j, 0)) continue;
            var sign = new CityPoint(CityTraffic.Coordinate(i) - CityTraffic.Sidewalk,
                                     CityTraffic.BlockCentre(j));
            if (CityRoadLevels.Reserved(sign, 4) || !CityWater.UrbanSite(sign, 3) || !ClinicClear(sign)
                || _food.Any(s => (s.Position - sign).Length <= 6)) continue;
            var centre = BusStopFixture.Centre(sign);
            if (!CityLayout.Current.Blocks.Any(b => Math.Abs(b.X - centre.X) < b.HalfX + BusStopFixture.ReservedHalfX
                                                 && Math.Abs(b.Z - centre.Z) < b.HalfZ + BusStopFixture.ReservedHalfZ))
                yield return sign;
        }
    }

    public static bool StopClear(CityPoint p, double radius = 0)
    {
        Ensure();
        foreach (var sign in _stops)
        {
            var offset = p - BusStopFixture.Centre(sign);
            if (Math.Abs(offset.X) <= BusStopFixture.ReservedHalfX + radius
                && Math.Abs(offset.Z) <= BusStopFixture.ReservedHalfZ + radius) return false;
        }
        return true;
    }
    // Shared by visible trees and collision proxies; only nearby building buckets
    // are queried while districts stream, never the complete city per tree.
    public static bool BuildingClear(CityPoint p,double radius){
        var layout = CityLayout.Current;
        int i = CityTraffic.BlockIndex(p.X), j = CityTraffic.BlockIndex(p.Z);
        for(int z=j-1;z<=j+1;z++)for(int x=i-1;x<=i+1;x++)foreach(int id in layout.BuildingIds(x,z)){
            var b=layout.Blocks[id];if(Math.Abs(p.X-b.X)<b.HalfX+radius&&Math.Abs(p.Z-b.Z)<b.HalfZ+radius)return false;
        }return true;
    }
    public static bool TreeClear(CityPoint p){Ensure();return CityImportedSites.ExtraClear(p,2.5)&&!CityRoadLevels.Reserved(p,3)&&BuildingClear(p,1.5)&&StopClear(p,1.8)&&!_food.Any(s=>(s.Position-p).Length<3.5||(s.Interaction-p).Length<2.5)&&ServiceClear(p,1.5);}
    public static bool PropClear(CityPoint p,double radius){Ensure();return CityImportedSites.ExtraClear(p,radius)&&!CityRoadLevels.Reserved(p,radius)&&StopClear(p,radius)&&_food.All(s=>
        (Math.Abs(p.X-s.Position.X)>=s.Fixture.WalkHalfSize.X+radius||Math.Abs(p.Z-s.Position.Z)>=s.Fixture.WalkHalfSize.Z+radius)
        &&(p-s.Interaction).Length>=FoodSite.PurchaseRadius+radius)&&ServiceClear(p,radius);}
    // Garage boxes, their forecourts and market stalls with their counters.
    public static bool ServiceClear(CityPoint p,double radius){Ensure();
        foreach(var s in _garages)if(Math.Abs(p.X-s.Position.X)<GarageSite.HalfX+radius&&p.Z-s.Position.Z<GarageSite.HalfZ+radius&&s.Position.Z-p.Z<GarageSite.HalfZ+GarageSite.ForecourtDepth+radius)return false;
        foreach(var s in _markets)if(Math.Abs(p.X-s.Position.X)<MarketSite.HalfX+radius&&Math.Abs(p.Z-s.Position.Z)<MarketSite.HalfZ+radius||(p-s.Interaction).Length<1.2+radius)return false;
        return true;}
    public static bool BlocksWalk(CityPoint p){
        Ensure();if(!CityImportedSites.ExtraClear(p,.3)||CityImportedSites.StopOccupied(p,.2))return true;
        if(_walkCells.TryGetValue(((int)Math.Floor(p.X/24),(int)Math.Floor(p.Z/24)),out var boxes))
            foreach(var box in boxes)if(Math.Abs(p.X-box.Centre.X)<box.HalfX&&Math.Abs(p.Z-box.Centre.Z)<box.HalfZ)return true;
        return false;
    }
    public const double ClinicHalf=1.15;
    public static CityPoint ClinicEntrance(int i, int j) => CityCivicSites.Entrance(i, j);
    public static CityPoint StationEntrance()=>ClinicEntrance(CityTraffic.StationBlock.I,CityTraffic.StationBlock.J);
    public static bool ClinicClear(CityPoint p)=>CityTraffic.ClinicBlocks.All(s=>(ClinicEntrance(s.I,s.J)-p).Length>3)&&(StationEntrance()-p).Length>3;
}