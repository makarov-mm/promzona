namespace Promzona.Game;

/// <summary>A closed, ground-level public transport line. Arc length is the authoritative
/// tram coordinate; the same curves generate rails, overhead wires and vehicle poses.</summary>
public sealed class CityTransitRoute
{
    public int Id {get;}
    public int Kind=>Id%2==1?9:10;
    public int FleetSize=>Math.Clamp((int)Math.Ceiling(Length/420),4,16);
    private readonly Dictionary<(int,int),int[]> _near = [];
    public CityPath[] Segments {get;}
    public double[] Starts {get;}
    public double Length {get;}
    public double[] Stops {get;}
    public CityTransitRoute(int id,CityPoint[] corners,double lane)
    {
        Id=id;List<CityPath> paths=[];List<double> stops=[];double length=0;
        for(int k=0;k<corners.Length;k++){
            var a=corners[k];var b=corners[(k+1)%corners.Length];var c=corners[(k+2)%corners.Length];
            var f=(b-a)*(1/(b-a).Length);var next=(c-b)*(1/(c-b).Length);
            // Long-wheelbase trolleybuses need a real turning radius, including
            // right turns in the outer lane (the old 10 m inset was too tight).
            const double inset=12;
            var start=a+f*inset+CityTraffic.Right(f)*lane;var end=b-f*inset+CityTraffic.Right(f)*lane;
            var leave=b+next*inset+CityTraffic.Right(next)*lane;
            var straight=new CityPath(start,end);
            // Stops are on the curbside part of a block, never across a junction.
            for(double d=16;d<straight.Length-4;d+=216)
                if(stops.Count==0||length+d-stops[^1]>160)stops.Add(length+d);
            paths.Add(straight);length+=straight.Length;
            double handle=Math.Abs(CityPoint.Dot(leave-end,f))*.5522847498;
            var turn=new CityPath(end,end+f*handle,leave-next*handle,leave);paths.Add(turn);length+=turn.Length;
        }
        Segments=paths.ToArray();Starts=new double[Segments.Length];double total=0;
        for(int i=0;i<Segments.Length;i++){Starts[i]=total;total+=Segments[i].Length;}
        Length=total;Stops=stops.ToArray();
        Dictionary<(int,int),HashSet<int>> cells=[];
        for(int i=0;i<Segments.Length;i++)for(double d=0;d<=Segments[i].Length+8;d+=8){
            var p=Segments[i].Sample(d).Point;int x=(int)Math.Floor(p.X/48),z=(int)Math.Floor(p.Z/48);
            for(int a=x-1;a<=x+1;a++)for(int b=z-1;b<=z+1;b++){
                if(!cells.TryGetValue((a,b),out var ids))cells[(a,b)]=ids=[];ids.Add(i);
            }
        }
        foreach(var cell in cells)_near[cell.Key]=cell.Value.Order().ToArray();
    }
    public double Wrap(double along)=>(along%Length+Length)%Length;
    public (CityPoint Point,double Heading) Sample(double along){along=Wrap(along);int i=Array.BinarySearch(Starts,along);if(i<0)i=~i-1;return Segments[Math.Max(0,i)].Sample(along-Starts[Math.Max(0,i)]);}
    public double Closest(CityPoint point){
        double best=double.MaxValue,along=0;
        void Check(int i){double s=Segments[i].ClosestDistance(point),d=(Segments[i].Sample(s).Point-point).Length;if(d<best){best=d;along=Starts[i]+s;}}
        if(_near.TryGetValue(((int)Math.Floor(point.X/48),(int)Math.Floor(point.Z/48)),out var ids))foreach(int i in ids)Check(i);
        else for(int i=0;i<Segments.Length;i++)Check(i);
        return along;
    }
    public double Ahead(double from,double to)=>Wrap(to-from);
}

public static class CityTransit
{
    private static CityLayout? _layout;
    private static CityTransitRoute[] _routes=[];
    private static CityLayout? _supportLayout;
    private static readonly Dictionary<(int,int),List<CityPoint>> _poleCells=[];
    private static List<(CityPoint Left,CityPoint Right,CityPoint Wire)> _supports=[];
    public static IReadOnlyList<(CityPoint Left,CityPoint Right,CityPoint Wire)> Supports {get{
        if(ReferenceEquals(_supportLayout,CityLayout.Current))return _supports;
        _supportLayout=CityLayout.Current;_supports=[];_poleCells.Clear();
        var trees=CityScenery.Trees().GroupBy(t=>((int)Math.Floor(t.Position.X/8),(int)Math.Floor(t.Position.Z/8))).ToDictionary(g=>g.Key,g=>g.ToArray());
        HashSet<(int,int,int,int)> placed=[];
        bool Clear(CityPoint p){
            if(!CityWater.UrbanSite(p,.5)||!CityAmenities.ServiceClear(p,.8)||!CityAmenities.StopClear(p,.8)||CityAmenities.BlocksWalk(p)||!CityImportedSites.ExtraClear(p,.7)||CityLayout.Current.FurnitureNear(p.X,p.Z,.6))return false;
            int x=(int)Math.Floor(p.X/8),z=(int)Math.Floor(p.Z/8);
            for(int i=x-1;i<=x+1;i++)for(int j=z-1;j<=z+1;j++)if(trees.TryGetValue((i,j),out var near))foreach(var t in near)if((t.Position-p).Length<=1.8)return false;
            x=(int)Math.Floor((p.X-CityTraffic.Coordinate(0))/CityTraffic.Spacing);z=(int)Math.Floor((p.Z-CityTraffic.Coordinate(0))/CityTraffic.Spacing);
            for(int i=x-1;i<=x+1;i++)for(int j=z-1;j<=z+1;j++)foreach(int id in CityLayout.Current.BuildingIds(i,j)){
                var b=CityLayout.Current.Blocks[id];if(Math.Abs(b.X-p.X)<=b.HalfX+.7&&Math.Abs(b.Z-p.Z)<=b.HalfZ+.7)return false;
            }return true;
        }
        foreach(var route in Routes)for(int k=0;k<route.Segments.Length;k+=2){
            var segment=route.Segments[k];
            for(double d=12;d<segment.Length;d+=36){
                var pose=segment.Sample(d);var f=new CityPoint(Math.Sin(pose.Heading),Math.Cos(pose.Heading));var r=CityTraffic.Right(f);
                var centre=pose.Point-r*(route.Kind==9?1.9:CityLayout.WideRoads?5.2:1.9);
                var left=centre-r*(CityTraffic.RoadHalf+.8);var right=centre+r*(CityTraffic.RoadHalf+.8);
                var key=((int)Math.Round(left.X*100),(int)Math.Round(left.Z*100),(int)Math.Round(right.X*100),(int)Math.Round(right.Z*100));
                var reverse=(key.Item3,key.Item4,key.Item1,key.Item2);
                if(placed.Contains(key)||placed.Contains(reverse))continue;
                if(!Clear(left)||!Clear(right))continue;
                placed.Add(key);
                _supports.Add((left,right,pose.Point));
                foreach(var p in new[]{left,right}){var cell=((int)Math.Floor(p.X/24),(int)Math.Floor(p.Z/24));if(!_poleCells.TryGetValue(cell,out var list))_poleCells[cell]=list=[];list.Add(p);}
            }
        }return _supports;
    }}
    public static bool PoleNear(CityPoint p,double radius){
        _=Supports;int x=(int)Math.Floor(p.X/24),z=(int)Math.Floor(p.Z/24);
        for(int i=x-1;i<=x+1;i++)for(int j=z-1;j<=z+1;j++)if(_poleCells.TryGetValue((i,j),out var list))foreach(var at in list)if((at-p).Length<radius+.1)return true;
        return false;
    }
    public static IReadOnlyList<CityTransitRoute> Routes {get{if(!ReferenceEquals(_layout,CityLayout.Current)){_layout=CityLayout.Current;_routes=Build();}return _routes;}}
    public static CityTransitRoute? ById(int id){if(id<=0)return null;foreach(var r in Routes)if(r.Id==id)return r;return null;}
    private static CityTransitRoute[] Build(){
        List<CityTransitRoute> result=[];var layout=CityLayout.Current;int n=CityTraffic.Nodes;
        // Follow actual connected streets. Four district-spanning loops use the
        // existing canal bridges; ramps and tunnel mouths are excluded.
        Dictionary<(int,int,int),bool> edges=[];
        bool Edge(int x,int z,int d){
            if(edges.TryGetValue((x,z,d),out bool cached))return cached;
            bool ok=layout.Road(x,z,d);var f=CityTraffic.Forward(d);var from=Node(x,z);
            if(ok)for(double t=0;t<=CityTraffic.Spacing;t+=3){var at=from+f*t;
                if(CityWater.Depth(at)>.05||CityRoadLevels.At(at.X,at.Z,14)!=null){ok=false;break;}
                if(CityLayout.WideRoads&&(CityWater.Depth(at+CityTraffic.Right(f)*5.2)>.05||CityWater.Depth(at-CityTraffic.Right(f)*5.2)>.05)){ok=false;break;}}
            edges[(x,z,d)]=ok;return ok;
        }
        int Scale(int value)=>Math.Clamp((int)Math.Round(value*(n-1)/48d),2,n-3);
        int[][] bounds=[[4,4,44,26],[6,4,26,44],[4,22,44,44],[22,4,42,44]];
        for(int id=1;id<=4;id++){
            var box=bounds[id-1];
            for(int attempt=0;attempt<32;attempt++){
                int shift=attempt==0?0:(attempt%2==0?1:-1)*((attempt%8+1)/2);
                int x0=Math.Clamp(Scale(box[0])+shift,2,n-8),z0=Math.Clamp(Scale(box[1])+shift,2,n-8);
                int x1=Math.Clamp(Scale(box[2])-shift,x0+5,n-3),z1=Math.Clamp(Scale(box[3])-shift,z0+5,n-3);
                (int X,int Z)[] targets=[(x0,z0),(x0,z1),(x1,z1),(x1,z0)];
                if(attempt>=8){int zShift=(attempt/8)%2==0?2:-2;for(int k=0;k<targets.Length;k++)targets[k].Z=Math.Clamp(targets[k].Z+zShift,2,n-3);}
                if(attempt>=16)Array.Reverse(targets);
                List<(int X,int Z)> cycle=[targets[0]];HashSet<(int,int)> used=[targets[0]];bool good=true;
                for(int leg=0;leg<4;leg++){
                    var start=targets[leg];var goal=targets[(leg+1)%4];
                    var queue=new PriorityQueue<(int X,int Z),double>();queue.Enqueue(start,0);
                    Dictionary<(int,int),double> costs=new(){[start]=0};Dictionary<(int,int),(int,int)> parents=[];
                    while(queue.TryDequeue(out var at,out _)){
                        if(at==goal)break;
                        for(int d=0;d<4;d++){
                            var f=CityTraffic.Forward(d);var next=(X:at.X+(int)f.X,Z:at.Z+(int)f.Z);
                            if(next.X<1||next.Z<1||next.X>=n-1||next.Z>=n-1||used.Contains(next)&&next!=goal||next!=goal&&targets.Contains(next)||!Edge(at.X,at.Z,d))continue;
                            // Prefer a straight arterial instead of equally short zigzags.
                            double deviation=start.X==goal.X?Math.Abs(next.X-start.X):Math.Abs(next.Z-start.Z);
                            double cost=costs[at]+1+deviation*.07;
                            if(costs.TryGetValue(next,out double old)&&old<=cost)continue;
                            costs[next]=cost;parents[next]=at;
                            queue.Enqueue(next,cost+Math.Abs(next.X-goal.X)+Math.Abs(next.Z-goal.Z));
                        }
                    }
                    if(!parents.ContainsKey(goal)){good=false;break;}
                    List<(int X,int Z)> path=[];for(var at=goal;at!=start;at=parents[at])path.Add(at);path.Reverse();
                    foreach(var at in path){cycle.Add(at);used.Add(at);}
                }
                if(!good)continue;cycle.RemoveAt(cycle.Count-1);
                List<CityPoint> corners=[];
                for(int k=0;k<cycle.Count;k++){
                    var prev=cycle[(k+cycle.Count-1)%cycle.Count];var at=cycle[k];var next=cycle[(k+1)%cycle.Count];
                    if(at.X-prev.X!=next.X-at.X||at.Z-prev.Z!=next.Z-at.Z)corners.Add(Node(at.X,at.Z));
                }
                if(corners.Count<4)continue;
                var route=new CityTransitRoute(id,corners.ToArray(),id%2==1?1.9:CityLayout.WideRoads?5.2:1.9);
                for(double t=0;t<route.Length&&good;t+=2){var at=route.Sample(t).Point;
                    good=CityWater.Depth(at)<.05&&CityRoadSurface.Contains(at)&&CityRoadLevels.At(at.X,at.Z,7)==null;
                }
                if(!good)continue;result.Add(route);break;
            }
        }
        return result.ToArray();
    }
    private static CityPoint Node(int i,int j)=>new(CityTraffic.Coordinate(i),CityTraffic.Coordinate(j));
}
