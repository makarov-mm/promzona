using Promzona.Game;
static partial class Tests
{
    static void RunCameraAudit(){
        foreach(int seed in new[]{73921,1,42})Test("camera clearance "+seed,()=>{
            CityLayout.Install(CityLayout.Generate(seed,gridSize:49,wideRoads:true,roadRevision:3));
            var b=CityModel.Blocks.First(v=>v.Height>5);
            double y=CityTerrain.Ground(b.X,b.Z)+1.35,z=b.Z+b.HalfZ+.4;
            double f=ChaseCamera.VisibleFraction(b.X,y,z,0,0,-7);
            Check(f>=0&&f<.02,"camera may retract below old six-percent minimum beside facade");
            Check(z-7*f>b.Z+b.HalfZ+.3,"close camera remains outside facade");
            foreach(var level in CityRoadLevels.All){
                foreach(double offset in new[]{-60d,0,60})foreach(double distance in new[]{7d,12d,60d}){
                    double x=level.X+offset,cz=level.CentreZ(x)+CityTraffic.Lane,cy=level.Floor(x,cz)+1.35;
                    double dx=-distance*Math.Cos(.42),dy=distance*Math.Sin(.42);
                    double fraction=ChaseCamera.VisibleFraction(x,cy,cz,dx,dy,0);
                    bool clear=true;for(int n=1;n<=100;n++){double u=fraction*n/100;clear&=!CityRoadLevels.Solid(x+dx*u,cy+dy*u,cz,.3);}
                    Check(clear,"boom stays clear of tunnel/bridge solid "+level.Id+" / "+offset+" / "+distance);
                }
                if(!level.Tunnel){
                    double x=level.X,cz=level.CentreZ(x),cy=CityTerrain.Ground(x,cz)+1.35;
                    double f2=ChaseCamera.VisibleFraction(x,cy,cz,0,20,0);
                    Check(f2<.5&&cy+20*f2<level.CentreFloor(x)-.55,"camera underneath cannot cross bridge slab");
                    double px=level.X-54,pz=level.CentreZ(px)+7.1,py=CityTerrain.Ground(px,pz)+1.35;
                    double pier=ChaseCamera.VisibleFraction(px-3,py,pz,6,0,0);
                    Check(pier<.4,"bridge pier blocks camera outside deck footprint");
                }
            }
        });
        CityLayout.Install(CityLayout.Generate(73921));
    }
}
