namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private static readonly CityWeapon PatrolGun = CityWeapons.Find(CityWeaponId.Pistol)! with
    {
        Range = 110, NpcCarDamage = .065, PersonDamage = .24, Interval = 1.6
    };

    private void DispatchAirPatrol()
    {
        // One aircraft, including a returning patrol. Reuse it if the pursuit resumes.
        var existing=Cars.Find(c=>c.AirPatrol&&!c.Destroyed&&!c.Falling&&!c.Driverless&&!c.Hero);
        if(existing is not null){
            existing.AirDeparting=false;
            if(Mission(existing) is null)Services.Add(new(){CarId=existing.Id,Kind=CityService.Police,Destination=PlayerPosition});
            return;
        }
        if(Cars.Count>=CarCap+MaxPoliceUnits+2)return;
        double angle=Random()*Math.Tau;
        var at=PlayerPosition+new CityPoint(Math.Sin(angle),Math.Cos(angle))*(SpawnDistance+35);
        at=new(Math.Clamp(at.X,-LandEdge+30,LandEdge-30),Math.Clamp(at.Z,-LandEdge+30,LandEdge-30));
        var c=new CityCar{Id=++_nextId,Kind=12,AirPatrol=true,Position=at,Occupants=2,
            Heading=Math.Atan2(PlayerPosition.X-at.X,PlayerPosition.Z-at.Z),Path=new(at,at+new CityPoint(0,2)),Cruise=22};
        Register(c);c.Physical=false;Remove(c.Body);
        c.AirSafeHeight=AirClearance(at,at);
        c.Lift=Math.Max(24,c.AirSafeHeight-c.SurfaceHeight);
        c.AirShotAt=Time+4;
        Cars.Add(c);Services.Add(new(){CarId=c.Id,Kind=CityService.Police,Destination=PlayerPosition});SpawnedCars++;
    }

    // Look ahead over a small group of spatial buckets, not every roof in the city.
    private double AirClearance(CityPoint from,CityPoint to)
    {
        if(!_useScenery)return 32;
        double safe=Math.Max(CityTerrain.Ground(from),CityTerrain.Ground(to))+28;
        double origin=Coordinate(0),margin=12;
        int i0=(int)Math.Floor((Math.Min(from.X,to.X)-origin-margin)/Spacing)-1;
        int i1=(int)Math.Floor((Math.Max(from.X,to.X)-origin+margin)/Spacing)+1;
        int j0=(int)Math.Floor((Math.Min(from.Z,to.Z)-origin-margin)/Spacing)-1;
        int j1=(int)Math.Floor((Math.Max(from.Z,to.Z)-origin+margin)/Spacing)+1;
        var layout=CityLayout.Current;
        for(int j=j0;j<=j1;j++)for(int i=i0;i<=i1;i++)foreach(int id in layout.BuildingIds(i,j)){
            var b=layout.Blocks[id];
            if(b.X+b.HalfX<Math.Min(from.X,to.X)-margin||b.X-b.HalfX>Math.Max(from.X,to.X)+margin||
               b.Z+b.HalfZ<Math.Min(from.Z,to.Z)-margin||b.Z-b.HalfZ>Math.Max(from.Z,to.Z)+margin)continue;
            safe=Math.Max(safe,CityTerrain.Ground(b.X,b.Z)+b.Height+12);
        }
        return safe;
    }

    private bool AirPatrolSight(CityCar c)
    {
        var delta=PlayerPosition-c.Position;
        double distance=delta.Length;
        if(distance<4||distance>100||PlayerUnavailable||c.Burning)return false;
        double start=c.SurfaceHeight+c.Lift+1.2;
        double end=PlayerGround+(OnFoot?1.0:Hero.Height*.65);
        var direction=delta*(1/distance);double slope=(end-start)/distance;
        // Shared bullet trace tests the actual 3D roof, tunnel and scenery geometry.
        var hit=TraceAttack(c.Position,direction,PatrolGun with{Range=distance},false,0,null,true,start,slope,c.Id);
        return (hit.Point-c.Position).Length>=distance-(OnFoot?.65:Hero.HalfLength+1);
    }

    private void AirPatrolStep(double dt)
    {
        foreach(var c in Cars){
            if(!c.AirPatrol||c.Destroyed||c.Falling)continue;
            if(PlayerControls(c)||c.Driverless){c.AirPatrol=c.AirDeparting=false;continue;}
            if(WantedLevel<6||PlayerUnavailable||c.Burning)c.AirDeparting=true;
            c.Physical=false;Remove(c.Body);
            var at=new CityPoint(c.Body.X,c.Body.Y);
            // A slow orbit leaves room to look up, retaliate or escape under cover.
            var target=c.AirDeparting ? at+new CityPoint(Math.Sin(c.Heading),Math.Cos(c.Heading))*120 :
                PlayerPosition+PlayerVelocity*.7+new CityPoint(Math.Sin(Time*.12+c.Id),Math.Cos(Time*.12+c.Id))*38;
            if(!c.AirDeparting)target=new(Math.Clamp(target.X,-LandEdge+20,LandEdge-20),Math.Clamp(target.Z,-LandEdge+20,LandEdge-20));
            var delta=target-at;double distance=delta.Length;
            var direction=distance>.001?delta*(1/distance):default;
            if(Time>=c.AirSurveyAt){
                c.AirSafeHeight=AirClearance(at,at+direction*Math.Min(45,distance));c.AirSurveyAt=Time+.35;
            }
            double worldY=c.SurfaceHeight+c.Lift;
            double vertical=Math.Clamp((c.AirSafeHeight-worldY)*.8,-3,6);
            c.VerticalVelocity+=(vertical-c.VerticalVelocity)*(1-Math.Exp(-dt*3));
            worldY+=c.VerticalVelocity*dt;
            // Climb before crossing a taller roof; planar collision proxies are not air navigation.
            double speed=worldY<c.AirSafeHeight-5?0:Math.Min(22,distance*.9);
            double blend=1-Math.Exp(-dt*1.8);
            c.Body.Vx+=(direction.X*speed-c.Body.Vx)*blend;
            c.Body.Vy+=(direction.Z*speed-c.Body.Vy)*blend;
            double yaw=Math.Atan2(c.Body.Vx,c.Body.Vy);
            c.Heading+=Math.Clamp(Math.IEEERemainder(yaw-c.Heading,Math.Tau),-dt,dt);
            c.Body.Angle=-c.Heading;c.Body.W=0;
            var next=at+new CityPoint(c.Body.Vx,c.Body.Vy)*dt;
            c.Body.X=next.X;c.Body.Y=next.Z;c.Position=next;c.Distance+=(next-at).Length;
            UpdateTerrainSupport(c,true);c.Lift=Math.Max(0,worldY-c.SurfaceHeight);
            c.Speed=Math.Sqrt(c.Body.Vx*c.Body.Vx+c.Body.Vy*c.Body.Vy);
            c.Pitch+=(-c.Speed*.006-c.Pitch)*blend;c.Roll*=1-blend;
            c.TerrainPitch=c.TerrainRoll=0;c.RotorPhase=(c.RotorPhase+dt*28)%Math.Tau;
            if(!c.AirDeparting&&Time>=c.AirShotAt){
                c.AirShotAt=Time+.2;
                if(AirPatrolSight(c)){
                    var aim=PlayerPosition-c.Position;double range=aim.Length;
                    double start=c.SurfaceHeight+c.Lift+1.2;
                    double end=PlayerGround+(OnFoot?1.0:Hero.Height*.65);
                    TraceAttack(c.Position,aim*(1/range),PatrolGun,false,0,null,false,start,(end-start)/range,c.Id);
                    c.AirShots++;c.AirShotAt=Time+PatrolGun.Interval;
                }
            }
        }
    }

    // Ray entry into a full vertical prism. Checking Y only at the horizontal
    // facade entry misses a descending ray that first enters through the roof.
    internal static double RayPrism(CityPoint origin,CityPoint direction,double y,double slope,
        CityPoint center,double heading,double halfWidth,double halfLength,double bottom,double top,double limit)
    {
        double sn=Math.Sin(heading),cs=Math.Cos(heading),dx=origin.X-center.X,dz=origin.Z-center.Z;
        double near=0,far=limit;
        bool Slab(double p,double v,double lo,double hi){
            if(Math.Abs(v)<1e-10)return p>=lo&&p<=hi;
            double a=(lo-p)/v,b=(hi-p)/v;if(a>b)(a,b)=(b,a);
            near=Math.Max(near,a);far=Math.Min(far,b);return near<=far;
        }
        return Slab(dx*cs-dz*sn,direction.X*cs-direction.Z*sn,-halfWidth,halfWidth)&&
               Slab(dx*sn+dz*cs,direction.X*sn+direction.Z*cs,-halfLength,halfLength)&&
               Slab(y,slope,bottom,top)?near:limit;
    }
}
