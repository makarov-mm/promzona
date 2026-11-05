using Promzona.Game;

static partial class Tests
{
    static void RunRenderDistance()
    {
        Test("view distances and legacy preferences",()=>{
            Check(CityLod.RestoreDistance(null,0)==1000,"new installations see a 1000 m city");
            Check(CityLod.RestoreDistance(205,0)==1000,"the old untouched default is upgraded");
            Check(CityLod.RestoreDistance(600,1)==1000,"0.12 default migrates to 1000 m");
            Check(CityLod.RestoreDistance(600,2)==600,"current explicit 600 m choice survives restart");
            Check(CityLod.RestoreDistance(2500,2)==2500,"long-range custom preference survives restart");
            Check(CityLod.RestoreDistance(205,1)==205,"a saved new-version preference is not upgraded again");
            Check(CityLod.RestoreDistance(130,0)==130&&CityLod.RestoreDistance(320,0)==320,"explicit legacy low/high settings survive");
            Check(CityLod.ClampDistance(-20)==130&&CityLod.ClampDistance(int.MaxValue)==3000,"corrupt distance preferences are bounded");
            foreach(int distance in new[]{130,205,320,600,1000,2000,3000})
            {
                Check(CityLod.ActorRange(distance)<distance&&CityLod.VehicleRange(distance)<distance,"actors stay inside scenery at "+distance);
                Check(CityLod.SpawnRange(distance)<=500&&CityLod.SpawnRange(distance)>CityLod.VehicleRange(distance),"replenishment stays out of view at "+distance);
                Check(CityLod.FarPlane(distance,120)>distance+120,"overview camera does not clip skyline at "+distance);
            }
            Check(CityLod.ActorRange(3000)==CityLod.ActorRange(1000)&&CityLod.VehicleRange(3000)==CityLod.VehicleRange(1000),"3000 m landscape does not multiply detailed actor workload");
            Check(CityTraffic.PhysicsRadius==90&&CityTraffic.PhysicsExitRadius==112,"longer rendering does not expand contact physics");
            Check(CityLod.NearPlane(1)==.12f&&CityLod.NearPlane(430)>4&&CityLod.NearPlane(10000)==8,"camera retracts near clip for close walls and preserves depth precision in overview");
        });
        Test("furniture spatial index",()=>{
            foreach(int dimension in new[]{33,49})
            {
                var layout=CityLayout.Generate(73921,gridSize:dimension,wideRoads:dimension==49);CityLayout.Install(layout);
                var indexed=new List<CityModel.Obstacle>();
                for(int j=-2;j<=dimension;j++)for(int i=-2;i<=dimension;i++)
                {
                    double x=CityTraffic.Coordinate(i),z=CityTraffic.Coordinate(j);
                    foreach(var item in layout.FurnitureInBlock(i,j))
                    {
                        if(item.X<x||item.X>=x+36||item.Z<z||item.Z>=z+36)throw new Exception("furniture indexed in wrong district");
                        indexed.Add(item);
                    }
                }
                Check(indexed.Count==layout.Furniture.Length,"all furniture appears exactly once in index: "+dimension);
                Check(indexed.ToHashSet().SetEquals(layout.Furniture),"indexed furniture preserves original positions: "+dimension);
                Check(layout.FurnitureInBlock(-500,600).Count==0,"out-of-city queries are empty: "+dimension);
            }
        });
    }
}
