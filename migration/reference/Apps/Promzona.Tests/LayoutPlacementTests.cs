using Promzona.Game;
static partial class Tests
{
    static void RunLayoutPlacement()
    {
        foreach(var config in new[]{(73921,49,true),(18613,49,true),(91377,49,true),(73921,49,false),(73921,33,false)})
        Test("placement seed/size/width "+config,()=>{
            var (seed,size,wide)=config;CityLayout.Install(CityLayout.Generate(seed,gridSize:size,wideRoads:wide));
            var t=new CityTraffic();var stops=CityAmenities.Stops();var trees=CityScenery.Trees().ToArray();
            Check(trees.All(q=>CityAmenities.BuildingClear(q.Position,1.5)),"trees and their proxies stay outside building footprints");
            Check(stops.Count>20,"bus stop locations retained");
            Check(trees.All(q=>CityAmenities.StopClear(q.Position,1.8)),"tree crowns do not invade stop reservations");
            Check(t.Props.All(p=>CityAmenities.StopClear(p.Home,p.Radius+.15)),"loose props do not intersect stop seats or waiting areas");
            Check(stops.All(s=>!CityLayout.Current.Blocks.Any(b=>Math.Abs(b.X-(s.X-.8))<b.HalfX+1.65&&Math.Abs(b.Z-s.Z)<b.HalfZ+2.25)),"stop benches do not enter buildings");
            Check(stops.All(s=>CityAmenities.BlocksWalk(new(s.X-1,s.Z))),"fixed benches participate in pedestrian navigation");
            double low=CityTraffic.Coordinate(0),high=CityTraffic.Coordinate(size-1),r=CityTraffic.RoadHalf;
            bool beach=true,street=true;
            for(int n=0;n<size-1;n++)foreach(int side in new[]{-1,1}){
                double c=CityTraffic.Coordinate(n)+18,edge=side==1?high:low;
                beach&=!CityRoadSurface.Contains(new(edge+side*(r+.2),c))&&!CityRoadSurface.Contains(new(c,edge+side*(r+.2)));
                if(CityTraffic.HasRoad(side==1?size-1:0,n,0))street&=CityRoadSurface.Contains(new(edge+side*(r-.2),c));
                if(CityTraffic.HasRoad(n,side==1?size-1:0,1))street&=CityRoadSurface.Contains(new(c,edge+side*(r-.2)));
            }
            Check(beach,"all four beaches exclude road markings");Check(street,"outer half of perimeter lanes remains asphalt");
            if(size==49){
                double cx=CityTraffic.Coordinate(size/3)+18,z=CityTraffic.Coordinate(18);
                Check(!CityWater.Bridge(cx-18,z)&&!CityWater.Bridge(cx+18,z),"bridge support no longer occupies adjacent road centres");
                Check(CityWater.RailHalf<18-r,"rails stay outside the full quayside carriageway");
                Check(Math.Abs(CityWater.Deck(cx-CityWater.DeckHalf,z)-CityTerrain.Current.BedHeight(cx-CityWater.DeckHalf,z))<.02,"west bridge abutment meets bank without height jump");
                Check(Math.Abs(CityWater.Deck(cx+CityWater.DeckHalf,z)-CityTerrain.Current.BedHeight(cx+CityWater.DeckHalf,z))<.02,"east bridge abutment meets bank without height jump");
                double maxSlope=0;for(double x=cx-CityWater.DeckHalf+.1;x<cx+CityWater.DeckHalf-.1;x+=.1)maxSlope=Math.Max(maxSlope,Math.Abs(CityTerrain.Current.Gradient(x,z).X));
                Check(maxSlope<.25,"bridge ascent remains driveable: "+maxSlope);
                Check(CityWater.Depth(new(cx,z))==0&&CityWater.Depth(new(cx,z+18))>4,"water bed and supported bridge remain distinct");
            }
        });
        Test("bridge and quayside drive-through",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true));
            foreach(bool crossing in new[]{false,true}){
                var t=QuietCity();var c=t.Hero;
                double cx=CityTraffic.Coordinate(16)+18,rz=CityTraffic.Coordinate(18);
                Place(c,cx-(crossing?20:14.45),rz-(crossing?1.8:20));
                c.Heading=crossing?Math.PI/2:0;c.Body.Angle=-c.Heading;t.UpdateTerrainSupport(c);
                double lift=0,roll=0;bool completed=false;
                for(int n=0;n<600;n++){
                    t.Step(1d/60,new(.45,0,false));lift=Math.Max(lift,c.Lift);roll=Math.Max(roll,Math.Abs(c.Roll));
                    if((crossing?c.Position.X-cx:c.Position.Z-rz)>17){completed=true;break;}
                }
                Check(completed,(crossing?"across bridge":"along quay")+": car passes the complete junction");
                Check(c.Damage<.001&&lift<.30&&roll<.2,(crossing?"across bridge":"along quay")+": no rail impact or rollover");
            }
        });
    }
}
