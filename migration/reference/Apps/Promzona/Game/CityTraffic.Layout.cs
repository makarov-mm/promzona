namespace Promzona.Game;

/// <summary>Immutable seeded topology. A spanning tree guarantees connectivity;
/// extra streets form districts and loops. The perimeter remains accessible.</summary>
public sealed class CityLayout
{
    private static CityLayout? _current;
    public static int GridSize {get;private set;}=33;
    public int Dimension {get;private set;}
    public static bool WideRoads {get;private set;}
    public bool HasWideRoads {get;private set;}
    public static bool Rivers=>GridSize>=49;
    public static bool CanalColumn(int i)=>Rivers&&(i==GridSize/3||i==2*GridSize/3);
    public static bool BridgeRow(int j)=>j>=0&&j<GridSize&&j%6==0;
    public static bool AllowedEdge(int i,int j,int d)=>d%2==0||!CanalColumn(d==1?i:i-1)||BridgeRow(j);

    public static CityLayout Current {get {if(_current is null)Install(Generate(73921));return _current!;}}
    public static void Install(CityLayout layout) {
        _current = layout;GridSize=layout.Dimension;WideRoads=layout.HasWideRoads;
        layout.ClearServiceFurniture();
    }
    public int Seed { get; }
    public CityTerrain Terrain { get; }
    public CityRoadLevel[] Levels {get;}
    public int RoadRevision {get;}
    private readonly bool[] _roads, _parks;
    public CityModel.Obstacle[] Blocks { get; }
    public CityModel.Obstacle[] Furniture { get; private set; }
    private readonly Dictionary<(int, int), int[]> _blockIds;
    private Dictionary<(int,int),CityModel.Obstacle[]> _furnitureBlocks;
    private bool _servicesCleared;
    private void ClearServiceFurniture() {
        if(_servicesCleared)return;
        // Service sites depend on the installed topology. Remove conflicting
        // lamps and bins before either the renderer or collision grid consumes it.
        Furniture=Furniture.Where(f=>CityImportedSites.ExtraClear(new(f.X,f.Z),Math.Max(.2,Math.Max(f.HalfX,f.HalfZ)))&&CityAmenities.ServiceClear(new(f.X,f.Z),Math.Max(.2,Math.Max(f.HalfX,f.HalfZ)))).ToArray();
        _furnitureBlocks = IndexFurniture(Furniture);
        _servicesCleared=true;
    }
    private static Dictionary<(int, int), CityModel.Obstacle[]> IndexFurniture(CityModel.Obstacle[] items) =>
        items.GroupBy(item => (CityTraffic.BlockIndex(item.X), CityTraffic.BlockIndex(item.Z)))
             .ToDictionary(group => group.Key, group => group.ToArray());

    public IReadOnlyList<CityModel.Obstacle> FurnitureInBlock(int i,int j) =>
        _furnitureBlocks.TryGetValue((i,j),out var items)?items:Array.Empty<CityModel.Obstacle>();

    /// <summary>Equals <c>Furniture.Any(b => |b.X-x| &lt; b.HalfX+margin &amp;&amp; |b.Z-z| &lt; b.HalfZ+margin)</c>
    /// for any margin below 34 m, by looking only at the nine blocks around the point.
    /// The full scan over ~15 000 lamps and bins ran per probe in ClearPlayerPoint,
    /// which the door hint calls many times a frame beside parked cars.</summary>
    public bool FurnitureNear(double x,double z,double margin)
    {
        int i = CityTraffic.BlockIndex(x), j = CityTraffic.BlockIndex(z);
        for(int cj=j-1;cj<=j+1;cj++)for(int ci=i-1;ci<=i+1;ci++)
            if(_furnitureBlocks.TryGetValue((ci,cj),out var items))foreach(var b in items)
                if(Math.Abs(b.X-x)<b.HalfX+margin&&Math.Abs(b.Z-z)<b.HalfZ+margin)return true;
        return false;
    }

    private CityLayout(int seed, bool[] roads, bool[] parks, List<CityModel.Obstacle> blocks,
        List<CityModel.Obstacle> furniture, Dictionary<(int, int), int[]> ids, Action<double, string>? progress,
        CancellationToken cancel, CityRoadLevel[] levels,int roadRevision)
    {
        Levels=levels;RoadRevision=levels.Length>0?roadRevision:0;
        Dimension=GridSize;HasWideRoads=WideRoads;Terrain = new(seed, blocks, progress, cancel, RoadRevision>=CityIndustrial.Revision);
        foreach(var level in Levels)level.Bake(Terrain);
        Seed = seed;
        _roads = roads;
        _parks = parks;
        Blocks = blocks.ToArray();
        Furniture = furniture.ToArray();
        _furnitureBlocks = IndexFurniture(Furniture);
        _blockIds = ids;
    }

    public IEnumerable<int> BuildingIds(int i, int j) =>
        _blockIds.TryGetValue((i, j), out var ids) ? ids : Array.Empty<int>();

    public bool Park(int i, int j) => i >= 0 && j >= 0 && i < CityTraffic.Nodes - 1 && j < CityTraffic.Nodes - 1 &&
                                      _parks[i + j * CityTraffic.Nodes];
    /// <summary>A block of the industrial district (road revision 3 and later).</summary>
    public bool Industrial(int i, int j) => RoadRevision >= CityIndustrial.Revision && CityIndustrial.Zone(i, j);
    public bool HasIndustry => RoadRevision >= CityIndustrial.Revision;

    public bool Road(int i, int j, int direction) => i >= 0 && j >= 0 && i < CityTraffic.Nodes &&
                                                     j < CityTraffic.Nodes &&
                                                     _roads[(i + j * CityTraffic.Nodes) * 4 + (direction & 3)];

    private static readonly int[] LevelColumns = [8, 24, 40];
    private static readonly int[] LevelRows = [8, 23, 38];

    // Level IDs are sequential. The seed changes the positions, not the count.
    internal static int RoadLevelCount(int gridSize, bool wideRoads, int revision)
    {
        if (gridSize != 49 || !wideRoads || revision < 1) return 0;
        return revision == 1 ? 2 : LevelColumns.Length * LevelRows.Length;
    }

    public static CityLayout Generate(int seed, Action<double, string>? progress = null,
        CancellationToken cancel = default,int gridSize=33,bool wideRoads=false,int roadRevision=0)
    {
        if(gridSize is not (33 or 49))throw new ArgumentOutOfRangeException(nameof(gridSize));
        int previousSize=GridSize;bool previousWide=WideRoads;
        try {
        GridSize=gridSize;WideRoads=wideRoads;int n=gridSize;
        var random = new Random(seed);
        CityRoadLevel[] levels=[];
        if(RoadLevelCount(n,wideRoads,roadRevision)>0){
            if(roadRevision==1)levels=[new(1,8,12+(int)((uint)seed%7),false),new(2,24,29+(int)((uint)seed%5),true)];
            else {
                // Three separated land bands avoid both canals. Seeded row jitter
                // varies the districts without overlapping approaches or the coast.
                var levelRandom=new Random(seed^0x4c371);var list=new List<CityRoadLevel>();
                foreach(int i in LevelColumns)foreach(int row in LevelRows){
                    int id=list.Count+1;list.Add(new(id,i,row+levelRandom.Next(-1,2),(id&1)==0));
                }
                levels=list.ToArray();
            }
        }
        var roads = new bool[n * n * 4];
        var parks = new bool[n * n];
        var seen = new bool[n * n];
        // Revision 3 (29.10.2026): the industrial district and its railway spur.
        // Everything it changes is guarded, so revisions 0-2 draw the same random stream as before.
        bool industrial = roadRevision >= CityIndustrial.Revision && n == 49 && wideRoads;
        bool RoadEdge(int i, int j, int d) => AllowedEdge(i, j, d) && !(industrial && CityIndustrial.RailEdge(i, j, d));

        void Link(int i, int j, int d)
        {
            var f = CityTraffic.Forward(d);
            int x = i + (int)f.X, z = j + (int)f.Z;
            roads[(i + j * n) * 4 + d] = true;
            if (x >= 0 && z >= 0 && x < n && z < n) roads[(x + z * n) * 4 + (d + 2) % 4] = true;
        }

        var stack = new Stack<(int I, int J)>();
        stack.Push((n / 2, n / 2));
        seen[n / 2 + n * (n / 2)] = true;
        int visited = 1;
        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var (i, j) = stack.Peek();
            var options = new List<int>(4);
            for (int d = 0; d < 4; d++)
            {
                var f = CityTraffic.Forward(d);
                int x = i + (int)f.X, z = j + (int)f.Z;
                if (x >= 0 && z >= 0 && x < n && z < n && !seen[x + z * n]&&RoadEdge(i,j,d)) options.Add(d);
            }

            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }

            int chosen = options[random.Next(options.Count)];
            Link(i, j, chosen);
            var to = CityTraffic.Forward(chosen);
            int ni = i + (int)to.X, nj = j + (int)to.Z;
            seen[ni + nj * n] = true;
            stack.Push((ni, nj));
            visited++;
            progress?.Invoke(.18 * visited / (n * n), "Connecting streets");
        }

        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            cancel.ThrowIfCancellationRequested();
            for (int d = 0; d < 2; d++)
            {
                var f = CityTraffic.Forward(d);
                int x = i + (int)f.X, z = j + (int)f.Z;
                bool artery = d == 0 ? (i % 8 == 0 || i == n / 2) : (j % 8 == 0 || j == n / 2);
                // The random draw happens before the spur exclusion, so revision 3 keeps the block stream of revision 2.
                if (x < n && z < n && AllowedEdge(i,j,d) && (artery || random.NextDouble() < .61) && RoadEdge(i,j,d)) Link(i, j, d);
            }

            // Island: no traffic graph edge leads into the sea.
            progress?.Invoke(.18 + .07 * (i + j * n + 1) / (n * n), "Opening avenues");
        }

        
        foreach(var s in levels){
            for(int i=s.I-4;i<=s.I+4;i++)for(int j=s.J-1;j<=s.J+2;j++){
                if(i<s.I+4)Link(i,j,1);if(j<s.J+2)Link(i,j,0);
            }
            foreach(int dx in new[]{-3,-2,2,3}){
                int i=s.I+dx,j=s.J;
                roads[(i+j*n)*4]=false;roads[(i+(j+1)*n)*4+2]=false;
            }
        }
        // A clear, connected start lane regardless of seed.
        Link(n / 2, n / 2, 2);
        // The district's own street runs along its first row, past the cooperatives and the market.
        if (industrial) for (int i = CityIndustrial.ZoneWest; i < CityIndustrial.ZoneEast - 1; i++) Link(i, CityIndustrial.ZoneRow, 1);
        var b = new List<CityModel.Obstacle>();
        var furniture = new List<CityModel.Obstacle>();
        var ids = new Dictionary<(int, int), int[]>();
        for (int j = 0; j < n - 1; j++)
        for (int i = 0; i < n - 1; i++)
        {
            cancel.ThrowIfCancellationRequested();
            int begin = b.Count;
            double x = CityTraffic.BlockCentre(i), z = CityTraffic.BlockCentre(j);
            bool park = !CityTraffic.IsClinic(i, j) &&
                        (random.NextDouble() < .19 || (i / 3 + j / 4 + Math.Abs(seed % 7)) % 19 == 0);
            parks[i + j * n] = park&&!CanalColumn(i);
            if (!park&&!CanalColumn(i)&&!levels.Any(s=>s.Reserve(x,z,16)))
            {
                int style = random.Next(6);
                if (CityTraffic.IsClinic(i, j))
                {
                    b.Add(CityCivicSites.Building(i, j));
                    style = -1;
                }

                if (style == 0)
                    b.Add(new(x + random.NextDouble() * 2 - 1, z, 9 + random.NextDouble() * 2,
                        7 + random.NextDouble() * 2, 5 + random.Next(4) * 3));
                else if (style == 1)
                {
                    b.Add(new(x, z - 6, 11, 4, 10 + random.Next(5) * 3));
                    b.Add(new(x, z + 8, 7, 2.4, 2.8));
                }
                else if (style >= 0)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        double hx = 3.4 + random.NextDouble() * 1.4, hz = 3.4 + random.NextDouble() * 1.4;
                        b.Add(new(x + (k % 2 == 0 ? -7 : 7) + random.NextDouble() - .5,
                            z + (k < 2 ? -7 : 7) + random.NextDouble() - .5, hx, hz, 4 + random.Next(7) * 2.5));
                    }

                    if (style == 2) b.Add(new(x, z, 2.2, 1.65, 2.8));
                }
            }

            if (park && !CanalColumn(i) && (i + j) % 4 == 0) furniture.Add(new(x + 9, z + 9, .15, .15, 3.95));
            // The militia department (29.10.2026) replaces whatever the block drew,
            // after the draw: the random stream stays the one every older save was built with.
            if (CityTraffic.IsStation(i, j) && !CanalColumn(i))
            {
                parks[i + j * n] = false;
                b.RemoveRange(begin, b.Count - begin);
                b.Add(CityCivicSites.Building(i, j));
            }
            if (industrial && CityIndustrial.Zone(i, j))
            {
                parks[i + j * n] = false;
                b.RemoveRange(begin, b.Count - begin);
                foreach (var (box, _) in CityIndustrial.Obstacles(i, j)) b.Add(box);
            }
            if(roadRevision>=4&&n==49&&wideRoads&&!park&&!CanalColumn(i)&&!CityTraffic.IsClinic(i,j)&&!CityTraffic.IsStation(i,j)&&!CityIndustrial.Zone(i,j)&&!levels.Any(s=>s.Reserve(x,z,16))){
                b.RemoveRange(begin,b.Count-begin);b.AddRange(CityNeighbourhoods.Buildings(i,j,seed,n,x,z));
            }
            ids[(i, j)] = Enumerable.Range(begin, b.Count - begin).ToArray();
            progress?.Invoke(.25 + .16 * (i + j * (n - 1) + 1) / ((n - 1) * (n - 1)), "Building districts and parks");
        }

        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            cancel.ThrowIfCancellationRequested();
            double x = CityTraffic.Coordinate(i), z = CityTraffic.Coordinate(j);
            for (int side = -1; side <= 1; side += 2)
            {
                if (roads[(i + j * n) * 4])
                {
                    furniture.Add(new(x + side * (WideRoads?8.2:5.15), z + 12, .09, .09, 4.2));
                    furniture.Add(new(x + side * (WideRoads?8.2:5.15), z + 18, .25, .25, .9));
                }

                if (roads[(i + j * n) * 4 + 1]&&!CanalColumn(i))
                {
                    furniture.Add(new(x + 12, z + side * (WideRoads?8.2:5.15), .09, .09, 4.2));
                    furniture.Add(new(x + 18, z + side * (WideRoads?8.2:5.15), .25, .25, .9));
                }
            }

            progress?.Invoke(.41 + .04 * (i + j * n + 1) / (n * n), "Placing street furniture");
        }

        // Keep every facade inside its plot when avenues are widened.
        if (WideRoads)
        {
            for (int k = 0; k < b.Count; k++)
            {
                var q = b[k];
                int bi = CityTraffic.BlockIndex(q.X), bj = CityTraffic.BlockIndex(q.Z);
                if (CityTraffic.IsClinic(bi, bj) || CityTraffic.IsStation(bi, bj) || industrial && CityIndustrial.Zone(bi, bj)) continue;
                double cx = CityTraffic.BlockCentre(bi), cz = CityTraffic.BlockCentre(bj);
                const double plotScale = .73;
                b[k] = new(cx + (q.X - cx) * plotScale, cz + (q.Z - cz) * plotScale,
                           q.HalfX * plotScale, q.HalfZ * plotScale, q.Height);
            }
        }
        furniture.RemoveAll(q=>levels.Any(s=>s.Reserve(q.X,q.Z,.65))||!CityWater.UrbanSite(new(q.X,q.Z),.65)||!CityAmenities.ClinicClear(new(q.X,q.Z)));
        return new(seed, roads, parks, b, furniture, ids, progress, cancel,levels,roadRevision);
        } catch { GridSize=previousSize;WideRoads=previousWide; throw; }
    }
}
