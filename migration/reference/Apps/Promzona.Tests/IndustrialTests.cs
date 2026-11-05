using Promzona.Game;
static partial class Tests
{
    // The industrial district and its railway spur (29.10.2026, road revision 3).
    static void RunIndustrial(){
        Test("revision 3 adds the district without touching the old blocks",()=>{
            var old=CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:2);
            var oldBlocks=new Dictionary<(int,int),CityModel.Obstacle[]>();
            for(int j=0;j<48;j++)for(int i=0;i<48;i++)oldBlocks[(i,j)]=old.BuildingIds(i,j).Select(id=>old.Blocks[id]).ToArray();
            bool[] oldParks=new bool[48*48];for(int j=0;j<48;j++)for(int i=0;i<48;i++)oldParks[i+j*48]=old.Park(i,j);
            var layout=CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:3);CityLayout.Install(layout);
            Check(layout.RoadRevision==3&&layout.HasIndustry&&!old.HasIndustry,"revision 3 carries the district flag");
            int changed=0,zone=0;
            for(int j=0;j<48;j++)for(int i=0;i<48;i++){
                if(CityIndustrial.Zone(i,j)){zone++;continue;}
                var now=layout.BuildingIds(i,j).Select(id=>layout.Blocks[id]).ToArray();
                if(!now.SequenceEqual(oldBlocks[(i,j)])||layout.Park(i,j)!=oldParks[i+j*48])changed++;
            }
            Check(zone==5*(CityIndustrial.ZoneEast-CityIndustrial.ZoneWest)&&changed==0,$"every block outside the district is the revision 2 block ({changed} changed)");
            Check(Enumerable.Range(CityIndustrial.ZoneWest,CityIndustrial.ZoneEast-CityIndustrial.ZoneWest).All(i=>!layout.Park(i,CityIndustrial.ZoneRow+1)),"no parks inside the district");
            int halls=0,fences=0,tanks=0,chimneys=0;
            for(int j=CityIndustrial.ZoneRow;j<CityIndustrial.ZoneRow+CityIndustrial.ZoneRows;j++)for(int i=CityIndustrial.ZoneWest;i<CityIndustrial.ZoneEast;i++)
                foreach(var (box,kind) in CityIndustrial.Obstacles(i,j)){
                    if(kind==IndustrialKind.Hall)halls++;else if(kind==IndustrialKind.Fence)fences++;else if(kind==IndustrialKind.Tank)tanks++;else if(kind==IndustrialKind.Chimney)chimneys++;
                    Check(layout.BuildingIds(i,j).Any(id=>layout.Blocks[id]==box),$"obstacle {kind} of block ({i},{j}) is in the layout");
                    if(kind is not (IndustrialKind.Wagon or IndustrialKind.Buffer)){
                        double ix=CityTraffic.Coordinate(i)+18,iz=CityTraffic.Coordinate(j)+18;
                        Check(Math.Abs(box.X-ix)+box.HalfX<=8.95&&Math.Abs(box.Z-iz)+box.HalfZ<=8.95,$"{kind} of ({i},{j}) stays off the pavement");
                    }
                }
            Check(halls>=8&&fences>=16&&tanks>=6&&chimneys>=3,$"halls {halls}, fences {fences}, tanks {tanks}, chimneys {chimneys}");
        });
        Test("the spur replaces one street row and ends at a buffer stop",()=>{
            var layout=CityLayout.Current;int r=CityIndustrial.RailRow;
            Check(Enumerable.Range(CityIndustrial.ZoneWest,CityIndustrial.ZoneEast-1-CityIndustrial.ZoneWest).All(i=>!layout.Road(i,r,1)&&!layout.Road(i+1,r,3)),"no horizontal street along the rail row between the canals");
            Check(layout.Road(CityIndustrial.ZoneWest-2,r,1)||layout.Road(5,r,1)||layout.Road(40,r,1),"streets elsewhere on that row are untouched by the rule");
            var seen=new bool[49*49];var queue=new Queue<(int,int)>();queue.Enqueue((24,24));seen[24+24*49]=true;int count=1;
            while(queue.Count>0){var (i,j)=queue.Dequeue();for(int d=0;d<4;d++){if(!layout.Road(i,j,d))continue;var f=CityTraffic.Forward(d);int x=i+(int)f.X,z=j+(int)f.Z;if(x<0||z<0||x>=49||z>=49||seen[x+z*49])continue;seen[x+z*49]=true;count++;queue.Enqueue((x,z));}}
            Check(count==49*49,$"every node is still reachable by road ({count})");
            var wagons=layout.Blocks.Where(b=>Math.Abs(b.HalfX-CityIndustrial.WagonHalfX)<1e-9&&Math.Abs(b.Z-CityIndustrial.RailZ)<1e-9).ToList();
            Check(wagons.Count==3&&wagons.All(w=>w.X<CityIndustrial.RailX1)&&wagons.Zip(wagons.Skip(1)).All(p=>Math.Abs(p.First.X-p.Second.X)>2*CityIndustrial.WagonHalfX),"three wagons stand on the spur without overlapping");
            Check(layout.Blocks.Any(b=>Math.Abs(b.X-(CityIndustrial.RailX1+.4))<1e-9&&Math.Abs(b.Z-CityIndustrial.RailZ)<1e-9),"the buffer stop is solid");
            foreach(var w in wagons){int bi=(int)Math.Floor((w.X-CityTraffic.Coordinate(0))/36),bj=(int)Math.Floor((w.Z-CityTraffic.Coordinate(0))/36);
                Check(w.X-w.HalfX>=CityTraffic.Coordinate(bi)&&w.X+w.HalfX<CityTraffic.Coordinate(bi+1)&&layout.BuildingIds(bi,bj).Any(id=>layout.Blocks[id]==w),"a wagon lies inside the block that lists it");}
            Check(!CityAmenities.BuildingClear(new(wagons[0].X,wagons[0].Z),0)&&CityAmenities.BuildingClear(new(wagons[0].X,CityIndustrial.RailZ-5),0),"a wagon blocks walking, the corridor beside it does not");
            Check(CityIndustrial.RailX0<CityTraffic.Coordinate(CityIndustrial.ZoneWest)&&CityIndustrial.RailX1>CityIndustrial.RailX0+300,"the spur runs from the canal bank for more than three hundred metres");
        });
        Test("the district has its cooperatives and a market",()=>{
            var garages=CityAmenities.Garages();var markets=CityAmenities.Markets();
            var zoneGarages=garages.Where(g=>CityIndustrial.Zone((int)Math.Floor((g.Position.X-CityTraffic.Coordinate(0))/36),(int)Math.Floor((g.Position.Z-CityTraffic.Coordinate(0))/36))).ToList();
            Check(zoneGarages.Count==4&&zoneGarages.All(g=>CityLayout.Current.Blocks.All(b=>Math.Abs(b.X-g.Position.X)>=b.HalfX+GarageSite.HalfX||Math.Abs(b.Z-g.Position.Z)>=b.HalfZ+GarageSite.HalfZ)),$"two cooperatives of two boxes ({zoneGarages.Count})");
            var zoneMarket=markets.Where(m=>CityIndustrial.Zone((int)Math.Floor((m.Position.X-CityTraffic.Coordinate(0))/36),(int)Math.Floor((m.Position.Z-CityTraffic.Coordinate(0))/36))).ToList();
            Check(zoneMarket.Count==3,$"three stalls at the district market ({zoneMarket.Count})");
            Check(garages.Count>zoneGarages.Count&&markets.Count>zoneMarket.Count,"the rest of the city keeps its own garages and stalls");
            var t=new CityTraffic();
            Place(t.Hero,zoneGarages[0].Door.X,zoneGarages[0].Door.Z-.5);Call(t,"RefreshDetail");Call(t,"RefreshScenery");
            Check(t.World.Bodies.Any(b=>Math.Abs(b.X-zoneGarages[0].Position.X)<.01&&Math.Abs(b.Y-zoneGarages[0].Position.Z)<.01&&b.IsStatic),"the nearby cooperative boxes are solid");
            var hall=CityIndustrial.Obstacles(CityIndustrial.ZoneWest+1,CityIndustrial.ZoneRow+1).First(o=>o.Kind!=IndustrialKind.Fence).Box;
            Place(t.Hero,hall.X,hall.Z-hall.HalfZ-.5);Call(t,"RefreshDetail");Call(t,"RefreshScenery");
            Check(t.World.Bodies.Any(b=>Math.Abs(b.X-hall.X)<hall.HalfX+.5&&Math.Abs(b.Y-hall.Z)<hall.HalfZ+.5&&b.IsStatic),"a nearby hall has contact walls");
            Check(t.Cars.Count>1000&&t.Pedestrians.Count>3000,"the city still fills with traffic and people");
        });
        Test("older saves keep their revision",()=>{
            var world=new CityWorldSave{RoadRevision=3,Reservations=new int[49*49],PedPlaces=new int[49*49*4]};
            try{CityTraffic.ValidateWorld(world);Check(false,"an empty car list must fail elsewhere");}catch(InvalidDataException e){Check(!e.Message.Contains("revision"),"revision 3 passes the save check");}
            world.RoadRevision=5;
            try{CityTraffic.ValidateWorld(world);Check(false,"revision 5 must be rejected");}catch(InvalidDataException e){Check(e.Message.Contains("revision"),"an unknown revision is rejected");}
        });
    }
}
