using Promzona.Game;
static partial class Tests
{
    static void RunSpatialOptimization()
    {
        Test("neighbour query equivalence and allocations",()=>{
            var random=new Random(73921);
            Dictionary<(int,int),List<int>> cells=[];
            for(int i=0;i<800;i++){
                var key=(random.Next(-12,13),random.Next(-12,13));
                if(!cells.TryGetValue(key,out var list))cells[key]=list=[];
                list.Add(i);
            }
            IEnumerable<int> Reference(CityPoint p,double radius,double size){
                for(int x=(int)Math.Floor((p.X-radius)/size);x<=(int)Math.Floor((p.X+radius)/size);x++)
                for(int z=(int)Math.Floor((p.Z-radius)/size);z<=(int)Math.Floor((p.Z+radius)/size);z++)
                    if(cells.TryGetValue((x,z),out var list))foreach(int id in list)yield return id;
            }
            bool matches=true;
            foreach(double size in new[]{8d,16d})for(int i=0;i<600;i++){
                var p=new CityPoint(i%3==0?-size:random.NextDouble()*300-150,random.NextDouble()*300-150);
                double radius=i%5==0?0:i%7==0?size:random.NextDouble()*100;
                var query=new CityTraffic.CellQuery<int>(cells,p,radius,size);
                matches &= query.SequenceEqual(Reference(p,radius,size));
                var enumerator=query.GetEnumerator();while(enumerator.MoveNext()){}
                matches &= !enumerator.MoveNext()&&!enumerator.MoveNext();
            }
            Check(matches,"1200 searches preserve negative coordinates, cell boundaries, order, and exhausted enumerators");
            var reusable=new CityTraffic.CellQuery<int>(cells,new(0,0),0,8);
            cells[(0,0)]=[10,20];Check(reusable.SequenceEqual(new[]{10,20}),"query observes current cell contents");
            cells[(0,0)].Clear();Check(!reusable.Any(),"cleared cell contributes no stale actors");
            cells[(0,0)].Add(42);Check(reusable.Single()==42,"new registrations are visible without retaining old enumerators");
            long checksum=0;
            for(int n=0;n<10000;n++)foreach(int id in reusable)checksum+=id;
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int n=0;n<10000;n++)foreach(int id in reusable)checksum+=id;
            long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            Check(checksum==840000&&allocated==0,"direct neighbour iteration allocates zero managed bytes");
        });
        foreach(var config in new[]{(73921,49,4),(42,33,4),(73921,49,3),(73921,49,4)})
        Test("walk footprint index "+config,()=>{
            var (seed,size,revision)=config;
            CityLayout.Install(CityLayout.Generate(seed,gridSize:size,roadRevision:revision));
            var stops=CityAmenities.Stops();var food=CityAmenities.Food();var garages=CityAmenities.Garages();var markets=CityAmenities.Markets();
            bool Reference(CityPoint p){
                if(!CityImportedSites.ExtraClear(p,.3)||CityImportedSites.StopOccupied(p,.2))return true;
                if(stops.Any(s=>Math.Abs(p.X-(s.X-1))<.7&&Math.Abs(p.Z-s.Z)<1.75))return true;
                if(food.Any(s=>Math.Abs(p.X-s.Position.X)<(s.Shawarma?2.05:.9)&&Math.Abs(p.Z-s.Position.Z)<(s.Shawarma?1.6:.8)))return true;
                return garages.Any(s=>Math.Abs(p.X-s.Position.X)<GarageSite.HalfX&&Math.Abs(p.Z-s.Position.Z)<GarageSite.HalfZ)
                    ||markets.Any(s=>Math.Abs(p.X-s.Position.X)<MarketSite.HalfX&&Math.Abs(p.Z-s.Position.Z)<MarketSite.HalfZ);
            }
            var probes=new List<CityPoint>();
            void Box(CityPoint p,double hx,double hz){
                foreach(double x in new[]{-hx-1e-8,-hx,-hx+1e-8,0,hx-1e-8,hx,hx+1e-8})
                foreach(double z in new[]{-hz-1e-8,-hz,-hz+1e-8,0,hz-1e-8,hz,hz+1e-8})probes.Add(p+new CityPoint(x,z));
            }
            foreach(var s in stops)Box(new(s.X-1,s.Z),.7,1.75);
            foreach(var s in food)Box(s.Position,s.Shawarma?2.05:.9,s.Shawarma?1.6:.8);
            foreach(var s in garages)Box(s.Position,GarageSite.HalfX,GarageSite.HalfZ);
            foreach(var s in markets)Box(s.Position,MarketSite.HalfX,MarketSite.HalfZ);
            var random=new Random(seed);
            for(int i=0;i<10000;i++)probes.Add(new((random.NextDouble()*2-1)*CityTraffic.LandEdge,(random.NextDouble()*2-1)*CityTraffic.LandEdge));
            Check(probes.All(p=>CityAmenities.BlocksWalk(p)==Reference(p)),"indexed obstacles match full search at "+probes.Count+" interior/boundary/random points after layout replacement");
            var sites=CityImportedSites.All();bool imported=true;
            foreach(var s in sites){
                foreach(var p in new[]{s.Position,s.Position+new CityPoint(-.48,0),s.Position+new CityPoint(0,2),s.Position+new CityPoint(3,3)}){
                    bool stop=sites.Any(o=>o.Model=="outdated_soviet_bus_stop"&&(Math.Abs(p.X-(o.Position.X-.48))<.34&&Math.Abs(p.Z-o.Position.Z)<1.68||Math.Abs(p.X-(o.Position.X-.25))<.54&&Math.Abs(p.Z-o.Position.Z)<1.32));
                    bool extra=sites.All(o=>!o.Extra||!o.Contains(p,.3));
                    imported &= CityImportedSites.StopOccupied(p,.2)==stop&&CityImportedSites.ExtraClear(p,.3)==extra;
                }
            }
            Check(imported,"imported site searches retain bench opening and extra prop footprints");
        });
        CityLayout.Install(CityLayout.Generate(73921));
    }
}
