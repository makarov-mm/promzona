namespace Promzona.Game;

public readonly record struct ImportedSite(string Model,CityPoint Position,float Width,float Height,float Length,float Angle=0,bool Extra=false,double Elevation=double.NaN)
{
    public float HalfX=>(float)(Math.Abs(Math.Cos(Angle))*Width+Math.Abs(Math.Sin(Angle))*Length)*.5f;
    public float HalfZ=>(float)(Math.Abs(Math.Sin(Angle))*Width+Math.Abs(Math.Cos(Angle))*Length)*.5f;
    public float Radius=>MathF.Sqrt(Width*Width+Height*Height+Length*Length)*.5f+1;
    public double Ground=>double.IsFinite(Elevation)?Elevation:CityTerrain.Ground(Position)+.20;
    public bool Contains(CityPoint p,double margin=0)=>Math.Abs(p.X-Position.X)<HalfX+margin&&Math.Abs(p.Z-Position.Z)<HalfZ+margin;
}

/// <summary>Deterministic reserved footprints shared by rendering, pedestrians,
/// scenery physics and furniture generation. No dependence on OpenGL or visibility.</summary>
public static class CityImportedSites
{
    private static CityLayout? _layout;
    private static ImportedSite[] _sites=[];
    private static readonly Dictionary<(int,int),List<ImportedSite>> Cells=[];
    public static IReadOnlyList<ImportedSite> All()
    {
        if(ReferenceEquals(_layout,CityLayout.Current))return _sites;
        _layout=CityLayout.Current;var sites=new List<ImportedSite>();
        foreach (var food in CityAmenities.Food())
        {
            var fixture = food.Fixture;
            sites.Add(new(fixture.Model, food.Position, fixture.Width, fixture.Height, fixture.Depth));
        }
        foreach (var sign in CityAmenities.Stops()) sites.Add(BusStopFixture.Imported(sign));
        int depots=0;
        for(int j=2;j<CityTraffic.Nodes-3;j++)for(int i=2;i<CityTraffic.Nodes-3;i++){
            if(!CityLayout.Current.Park(i,j)||CityLayout.CanalColumn(i)||CityRoadLevels.ReservedBlock(i,j)||CityTraffic.IsClinic(i,j)||CityTraffic.IsStation(i,j))continue;
            var p=new CityPoint(CityTraffic.Coordinate(i)+18,CityTraffic.Coordinate(j)+18);
            bool Clear(ImportedSite s)=>CityWater.UrbanSite(s.Position,Math.Max(s.HalfX,s.HalfZ)+1)&&CityAmenities.BuildingClear(s.Position,Math.Max(s.HalfX,s.HalfZ)+1)&&sites.All(o=>Math.Abs(o.Position.X-s.Position.X)>o.HalfX+s.HalfX+2||Math.Abs(o.Position.Z-s.Position.Z)>o.HalfZ+s.HalfZ+2)&&CityAmenities.Garages().All(g=>(g.Position-s.Position).Length>9)&&CityAmenities.Markets().All(g=>(g.Position-s.Position).Length>6);
            if(depots<3&&(i*7+j*3)%13==0){
                var tram=new ImportedSite("free_soviet_tram_x-series",p+new CityPoint(-4,0),2.53f,4.06f,10.4f,0,true);
                var trolley=new ImportedSite("ziu_9",p+new CityPoint(4,0),2.96f,3.36f,11.9f,0,true);
                if(Clear(tram)&&Clear(trolley)){sites.Add(tram);sites.Add(trolley);depots++;continue;}}
            if((i*11+j*5)%9==0){var tires=new ImportedSite("tires_decor",p+new CityPoint(4,4),2.47f,.48f,1.49f,0,true);if(Clear(tires))sites.Add(tires);}
            if((i*5+j*11)%17==0){var pump=new ImportedSite("soviet_gasoline_pump_free",p+new CityPoint(-5,4),.76f,1.95f,1.16f,0,true);if(Clear(pump))sites.Add(pump);}
        }
        if(CityLayout.Current.RoadRevision>=CityIndustrial.Revision)
            for(int i=CityIndustrial.ZoneWest;i<CityIndustrial.ZoneEast;i++)
                foreach(var (box,kind) in CityIndustrial.Obstacles(i,CityIndustrial.RailRow))
                    if(kind==IndustrialKind.Wagon)sites.Add(new("metro_2033_train",new(box.X,box.Z),(float)(box.HalfZ*2),(float)box.Height,(float)(box.HalfX*2),MathF.PI/2,false,CityTerrain.Ground(box.X,box.Z)+.20));
        _sites=sites.Select(s=>s with{Elevation=double.IsFinite(s.Elevation)?s.Elevation:Math.Max(Math.Max(CityTerrain.Ground(s.Position.X-s.HalfX,s.Position.Z-s.HalfZ),CityTerrain.Ground(s.Position.X+s.HalfX,s.Position.Z+s.HalfZ)),Math.Max(CityTerrain.Ground(s.Position.X-s.HalfX,s.Position.Z+s.HalfZ),CityTerrain.Ground(s.Position.X+s.HalfX,s.Position.Z-s.HalfZ)))+.035}).ToArray();Cells.Clear();
        foreach(var s in _sites)for(int x=(int)Math.Floor((s.Position.X-s.HalfX-4)/24);x<=(int)Math.Floor((s.Position.X+s.HalfX+4)/24);x++)for(int z=(int)Math.Floor((s.Position.Z-s.HalfZ-4)/24);z<=(int)Math.Floor((s.Position.Z+s.HalfZ+4)/24);z++){
            if(!Cells.TryGetValue((x,z),out var cell))Cells[(x,z)]=cell=[];cell.Add(s);}
        return _sites;
    }
    private static List<ImportedSite>? Nearby(CityPoint p){All();return Cells.GetValueOrDefault(((int)Math.Floor(p.X/24),(int)Math.Floor(p.Z/24)));}
    public static bool ExtraClear(CityPoint p,double radius){
        if(Nearby(p) is {} sites)foreach(var s in sites)if(s.Extra&&s.Contains(p,radius))return false;
        return true;
    }
    public static bool StopOccupied(CityPoint p, double radius = 0)
    {
        if (Nearby(p) is not { } sites) return false;
        foreach (var site in sites)
            if (site.Model == BusStopFixture.Model && BusStopFixture.Occupied(site.Position, p, radius)) return true;
        return false;
    }
}
