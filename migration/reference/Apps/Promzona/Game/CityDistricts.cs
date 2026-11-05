using System.Diagnostics;
using MakarovPhysics.Renderer;

namespace Promzona.Game;

/// <summary>Static city meshes. Nearby blocks stream individually. Four distant blocks
/// share GPU buffers but retain separate bounds and can change detail independently.</summary>
internal sealed class CityDistricts : IDisposable
{
    private sealed class Batch(CitySurface surface, int count)
    {
        public readonly CitySurface Surface = surface;
        public readonly Tile?[] Parts = new Tile?[count];
        public int Residents;
    }

    private readonly record struct Part(int First, int Count, Vec3 Centre, Vec3 Half);
    private sealed record Tile(int Lod, Batch Batch, int Slot, Part Bounds)
    {
        public CitySurface Surface => Batch.Surface;
        public bool InRange = true;
    }

    private readonly record struct Request(int I, int J, int Lod, float Distance)
    {
        public int Count => Lod == 3 ? 4 : 1;
        public (int, int) Key(int slot) => (I + slot % 2, J + slot / 2);
    }

    private readonly Dictionary<(int,int),Tile> _tiles = [];
    private readonly HashSet<Batch> _batches = [];
    private readonly CitySurface _shared = new(3){DetailedFinish=true, WorkshopFinish=true, Outdoor=true};
    private readonly DioramaGeometry _geometry = new(){CaptureSurface=true, BevelBoxes=false};
    private readonly List<Request> _wanted = [];
    private readonly Dictionary<(int,int),int> _needed = [];
    private readonly CityMeshStaging _staging = new();
    private readonly CityDistrictOcclusion _occlusion = new();
    private readonly Part[] _parts = new Part[4];
    private Request? _building;
    private readonly List<(int,int)> _remove = [];
    private Vec3 _plannedEye;
    private float _plannedDistance = -1;
    private int _plannedQuality = -1, _next, _generated;
    public float Distance = CityLod.DefaultDistance;
    public int Quality = 2;
    public CityReflections? Reflections {set=>_shared.Reflections=value;}
    public int LoadedCount => _tiles.Count;
    public int PendingCount {get; private set;}
    public int BuiltCount {get; private set;}
    public int DrawnTiles {get; private set;}
    public int DrawnBatches {get; private set;}
    internal bool OcclusionEnabled {get; set;} = true;
    public int OccludedTiles {get; private set;}
    public int DrawnVertices {get; private set;}
    public long ResidentVertices => _batches.Sum(b => (long)b.Surface.VertexCount);
    public long GpuBytes => _batches.Sum(b => b.Surface.GpuBytes);
    public long UniqueVertices => _batches.Sum(b => (long)b.Surface.UniqueVertexCount);
    public long StagingBytes => _batches.Sum(b => (long)b.Surface.StagingBytes);
    public long PackingBufferBytes => _staging.BufferBytes;

    private void GeneratePart(Request request, int slot)
    {
        var (i,j) = request.Key(slot);
        int first = _geometry.SurfacePoints.Count;
        CityTerrainGeometry.SetDetail(_geometry, request.Lod);
        CityArchitecture.Block(_geometry, i, j, request.Lod);
        CityRoadLevelVisual.Block(_geometry, i, j, Math.Min(request.Lod, 2));
        if (request.Lod < 3) CityTransitVisual.Block(_geometry, i, j, request.Lod);
        CityTerrainGeometry.Conform(_geometry, first);
        var vertices = _geometry.SurfacePoints;
        var low = new Vec3(float.MaxValue,float.MaxValue,float.MaxValue);
        var high = new Vec3(float.MinValue,float.MinValue,float.MinValue);
        for (int k=first; k<vertices.Count; k++)
        {
            var v = vertices[k].Position;
            low = new(Math.Min(low.X,v.X), Math.Min(low.Y,v.Y), Math.Min(low.Z,v.Z));
            high = new(Math.Max(high.X,v.X), Math.Max(high.Y,v.Y), Math.Max(high.Z,v.Z));
        }
        int count = vertices.Count - first;
        _parts[slot] = count == 0 ? default :
            new(first, count, (low+high)*.5f, (high-low)*.5f + new Vec3(1,1,1));
    }

    public void Update(Vec3 eye)
    {
        long started = Stopwatch.GetTimestamp();
        float dx = eye.X-_plannedEye.X, dz = eye.Z-_plannedEye.Z;
        if (_plannedDistance != Distance || _plannedQuality != Quality || dx*dx+dz*dz > 4) Plan(eye);
        int budget = 2;
        while (_building.HasValue || _next < _wanted.Count)
        {
            if (_building is null)
            {
                var next = _wanted[_next++];
                if (!_needed.ContainsKey((next.I,next.J))) continue;
                _building = next;
                _generated = 0;
                _geometry.Clear();
                _geometry.Eye = eye;
            }
            var request = _building.Value;
            if (_generated < request.Count)
            {
                GeneratePart(request, _generated++);
                if (_generated == request.Count) _staging.Begin(_geometry.SurfacePoints);
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 6) break;
                continue;
            }
            if (!_staging.Pack())
            {
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 6) break;
                continue;
            }

            var mesh = new CitySurface(3,_shared);
            try { mesh.UploadStatic(_staging); } catch { mesh.Dispose(); throw; }
            var batch = new Batch(mesh, request.Count);
            _batches.Add(batch);
            for (int slot=0; slot<request.Count; slot++)
            {
                var key = request.Key(slot);
                Remove(key);
                var tile = new Tile(request.Lod,batch,slot,_parts[slot]);
                batch.Parts[slot] = tile;
                batch.Residents++;
                if (request.Count == 4) tile.InRange = CellDistanceSquared(key, _plannedEye) <= Distance*Distance;
                _tiles[key] = tile;
                _needed.Remove(key);
            }
            BuiltCount += request.Count;
            _building = null;
            _staging.Cancel();
            if (--budget == 0 || Stopwatch.GetElapsedTime(started).TotalMilliseconds >= 6) break;
        }
        PendingCount = _needed.Count;
    }

    private void Remove((int,int) key)
    {
        if (!_tiles.Remove(key, out var tile)) return;
        var batch = tile.Batch;
        batch.Parts[tile.Slot] = null;
        if (--batch.Residents != 0) return;
        _batches.Remove(batch);
        batch.Surface.Dispose();
    }

    private void Plan(Vec3 eye)
    {
        _plannedEye=eye; _plannedDistance=Distance; _plannedQuality=Quality;
        _wanted.Clear(); _needed.Clear(); _next=0;
        int ci=Cell(eye.X), cj=Cell(eye.Z);
        int radius=(int)Math.Ceiling(Distance/36)+2;
        float near=(Quality==0?35:Quality==1?55:72)*CityLod.DetailScale;
        int last=CityTraffic.Nodes;
        // Decide at the same 2x2 origin for all four cells. At the distance
        // boundary stream the complete group once but draw only in-range cells.
        for (int j=Math.Max(-2,cj-radius)&~1; j<=Math.Min(last,cj+radius); j+=2)
        for (int i=Math.Max(-2,ci-radius)&~1; i<=Math.Min(last,ci+radius); i+=2)
        {
            float x=(float)CityTraffic.Coordinate(i)+36-eye.X, z=(float)CityTraffic.Coordinate(j)+36-eye.Z;
            float centreDistance=MathF.Sqrt(x*x+z*z);
            bool wasGrouped=_tiles.TryGetValue((i,j),out var anchor) && anchor.Lod==3;
            float closestX=Math.Abs(x)-18, closestZ=Math.Abs(z)-18;
            if (i+1<=last && j+1<=last && centreDistance>(wasGrouped?320:350) && closestX*closestX+closestZ*closestZ<=Distance*Distance)
            {
                Queue(new(i,j,3,centreDistance));
                continue;
            }
            for (int slot=0; slot<4; slot++)
            {
                int a=i+slot%2,b=j+slot/2;
                if (a>last || b>last) continue;
                float dx=x+(slot%2==0?-18:18), dz=z+(slot/2==0?-18:18);
                float distance=MathF.Sqrt(dx*dx+dz*dz);
                if (distance>Distance) continue;
                int lod=distance<near?0:distance<198?1:2;
                if (_tiles.TryGetValue((a,b),out var old))
                {
                    if (old.Lod==0 && distance<near+15) lod=0;
                    else if (old.Lod==1 && distance>near-15 && distance<213) lod=1;
                    else if (old.Lod>=2 && distance>183) lod=2;
                }
                Queue(new(a,b,lod,distance));
            }
        }
        if (_building is { } active && (!_needed.TryGetValue((active.I,active.J),out int lodNow) || lodNow!=active.Lod))
        { _building=null; _staging.Cancel(); }
        _wanted.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
        _remove.Clear();
        float evict = (Distance+40)*(Distance+40);
        foreach (var (key,tile) in _tiles)
        {
            float d2 = CellDistanceSquared(key, eye);
            if (tile.Lod == 3)
            {
                tile.InRange = d2 <= Distance*Distance;
                // Keep the four buffers' ranges together until the whole group leaves the halo.
                float groupX = (float)CityTraffic.Coordinate(key.Item1 & ~1)+36-eye.X;
                float groupZ = (float)CityTraffic.Coordinate(key.Item2 & ~1)+36-eye.Z;
                float nearX = Math.Abs(groupX)-18, nearZ = Math.Abs(groupZ)-18;
                d2 = nearX*nearX + nearZ*nearZ;
            }
            if (d2 > evict) _remove.Add(key);
        }
        foreach (var key in _remove) Remove(key);
    }

    private static float CellDistanceSquared((int I,int J) key, Vec3 eye)
    {
        float dx=(float)CityTraffic.Coordinate(key.I)+18-eye.X, dz=(float)CityTraffic.Coordinate(key.J)+18-eye.Z;
        return dx*dx+dz*dz;
    }

    private static int Cell(float p) => (int)Math.Floor((p-CityTraffic.Coordinate(0))/36);

    private void Queue(Request request)
    {
        for (int slot=0; slot<request.Count; slot++)
        {
            if (_tiles.TryGetValue(request.Key(slot),out var t) && t.Lod==request.Lod) continue;
            _wanted.Add(request);
            for (int k=0; k<request.Count; k++) _needed[request.Key(k)]=request.Lod;
            return;
        }
    }

    public void Depth(Mat4 lightSpace,float time=0)
    {
        var visibility = new CityVisibility(lightSpace);
        _shared.BeginDepthBatch(lightSpace,time);
        foreach (var tile in _tiles.Values)
            if (tile.Lod==0 && tile.Bounds.Count!=0 && visibility.Box(tile.Bounds.Centre,tile.Bounds.Half))
                tile.Surface.DrawBound();
    }

    private void FindOccluders(Vec3 eye, CityVisibility visibility)
    {
        _occlusion.Reset(eye);
        int ci=Cell(eye.X), cj=Cell(eye.Z);
        for (int j=Math.Max(0,cj-2); j<=Math.Min(CityTraffic.Nodes-2,cj+2); j++)
        for (int i=Math.Max(0,ci-2); i<=Math.Min(CityTraffic.Nodes-2,ci+2); i++)
        {
            if (!_tiles.ContainsKey((i,j)) || CityLayout.CanalColumn(i) ||
                CityRoadLevels.ReservedBlock(i,j) || CityLayout.Current.Industrial(i,j)) continue;
            foreach (int id in CityLayout.Current.BuildingIds(i,j))
            {
                var b=CityModel.Blocks[id];
                if (b.Height<6 || b.HalfX<1 || b.HalfZ<1) continue;
                var centre=new Vec3((float)b.X,(float)(CityTerrain.Ground(b.X,b.Z)+b.Height*.5),(float)b.Z);
                _occlusion.Add(centre,new((float)b.HalfX-.3f,(float)b.Height*.5f-.3f,(float)b.HalfZ-.3f),visibility);
            }
        }
    }

    public void Draw(Mat4 view,Mat4 proj,Vec3 eye,Vec3 sun,Vec3 light,Mat4 ls,uint shadow,float time,OutdoorLighting lighting,bool emission=false)
    {
        var visibility = new CityVisibility(proj*view);
        if (OcclusionEnabled) FindOccluders(eye,visibility);
        _shared.Lighting=lighting;
        _shared.BeginBatch(view,proj,eye,sun,light,ls,shadow,time,Vec3.Zero,.52f,emission,2.3f);
        if (!emission) { DrawnTiles=0; DrawnBatches=0; DrawnVertices=0; OccludedTiles=0; }
        foreach (var batch in _batches)
        {
            int first=0, count=0;
            foreach (var tile in batch.Parts)
            {
                if (tile==null || !tile.InRange) continue;
                var p=tile.Bounds;
                if (p.Count==0 || !visibility.Box(p.Centre,p.Half)) continue;
                if (OcclusionEnabled && _occlusion.Hidden(p.Centre,p.Half))
                {
                    if (!emission) OccludedTiles++;
                    continue;
                }
                if (count!=0 && first+count!=p.First)
                {
                    batch.Surface.DrawIndices(first,count);
                    if (!emission) DrawnBatches++;
                    count=0;
                }
                if (count==0) first=p.First;
                count+=p.Count;
                if (!emission) { DrawnVertices+=p.Count; DrawnTiles++; }
            }
            if (count!=0)
            {
                batch.Surface.DrawIndices(first,count);
                if (!emission) DrawnBatches++;
            }
        }
    }

    public void Dispose()
    {
        foreach (var batch in _batches) batch.Surface.Dispose();
        _batches.Clear(); _tiles.Clear(); _wanted.Clear(); _needed.Clear(); _building=null;
        _staging.Release(); _geometry.Clear(); _shared.Dispose();
    }
}

