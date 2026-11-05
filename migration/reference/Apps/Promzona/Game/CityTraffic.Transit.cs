namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private double _transitClock;
    private void EnsureTransit(bool initial=false){
        if(!_useScenery)return;
        foreach(var route in CityTransit.Routes){
            int count=Cars.Count(c=>c.TransitRoute==route.Id&&!c.Destroyed&&!c.Falling);
            for(int n=count;n<route.FleetSize;n++)for(int attempt=0;attempt<40;attempt++){
                double along=route.Length*((n/(double)route.FleetSize+attempt*.071)%1);var pose=route.Sample(along);
                if(!initial&&(pose.Point-PlayerPosition).Length<SpawnDistance)continue;
                var car=new CityCar{Id=_nextId+1,Kind=route.Kind,TransitRoute=route.Id,TransitAlong=along,Position=pose.Point,Heading=pose.Heading,Cruise=7,Occupants=1,Path=new(pose.Point,route.Sample(along+1).Point)};
                if(!CarClear(car.Position,car.Heading,car))continue;
                _nextId++;Cars.Add(car);Register(car);SpawnedCars++;break;
            }
        }
    }
    private double TransitSpeed(CityCar c,CityTransitRoute route){
        if(c.Destroyed||c.Falling||c.Driverless||c.Id==EnteringCarId||c.Burning)return 0;
        if(Time<c.TransitStopUntil)return 0;
        double target=7;
        double bend=Math.Abs(Math.IEEERemainder(route.Sample(c.TransitAlong+7).Heading-c.Heading,Math.Tau));
        target=Math.Min(target,Math.Sqrt(2.4*7/Math.Max(.08,bend)));
        if(Time>c.TransitNextStop)foreach(double stop in route.Stops){
            double distance=route.Ahead(c.TransitAlong,stop);
            if(distance<1&&Math.Abs(c.Speed)<.8){c.TransitStopUntil=Time+5;c.TransitNextStop=Time+25;return 0;}
            if(distance<22)target=Math.Min(target,Math.Sqrt(3*Math.Max(0,distance-.35)));
        }
        for(double d=1;d<24;d+=2){var point=route.Sample(c.TransitAlong+d).Point;
            int i=(int)Math.Round((point.X-Coordinate(0))/Spacing),j=(int)Math.Round((point.Z-Coordinate(0))/Spacing);
            var delta=point-new CityPoint(Coordinate(i),Coordinate(j));
            int dir=((int)Math.Round(c.Heading/(Math.PI/2))+4)%4;
            if(Math.Abs(delta.X)<RoadHalf&&Math.Abs(delta.Z)<RoadHalf&&!Green(i,j,dir)){
                // Once the nose is inside the junction, clear it rather than stopping across traffic.
                var now=c.Position-new CityPoint(Coordinate(i),Coordinate(j));
                if(Math.Abs(now.X)>RoadHalf+c.HalfLength||Math.Abs(now.Z)>RoadHalf+c.HalfLength)
                    target=Math.Min(target,Math.Sqrt(3*Math.Max(0,d-c.HalfLength-.8)));
            }
        }
        foreach(var other in NearbyCars(c.Position,30))if(other!=c&&SameHeight(c,other)){
            for(double d=1;d<25;d+=1.5){var pose=route.Sample(c.TransitAlong+d);
                if(CarsOverlap(pose.Point,pose.Heading,other.Position,other.Heading,.25,c.HalfLength,c.HalfWidth,other.HalfLength,other.HalfWidth)){target=Math.Min(target,Math.Sqrt(3*Math.Max(0,d-1.2)));break;}}
        }
        var f=new CityPoint(Math.Sin(c.Heading),Math.Cos(c.Heading));
        foreach(var p in NearbyPeople(c.Position,18))if(SameHeight(c,p)&&!p.Detached){var d=p.Position-c.Position;double ahead=CityPoint.Dot(d,f);if(ahead>0&&Math.Abs(CityPoint.Dot(d,Right(f)))<c.HalfWidth+.6)target=Math.Min(target,Math.Sqrt(3*Math.Max(0,ahead-c.HalfLength-1)));}
        return target;
    }
    private void TransitStep(double dt,DriveInput input){
        _transitClock-=dt;if(_transitClock<=0){_transitClock=8;EnsureTransit();}
        foreach(var c in Cars)if(CityTransit.ById(c.TransitRoute) is {} route&&!c.Destroyed&&!c.Falling){
            bool player=PlayerControls(c);var command=player?input.Sanitized():new DriveInput(Math.Clamp((TransitSpeed(c,route)-c.Speed)*.7,-1,1),0,c.Driverless);
            if(c.Kind==10){
                if(c.Physical)c.TransitAlong=route.Closest(c.Position);var aim=route.Sample(c.TransitAlong+Math.Max(4,Math.Abs(c.Speed)*.8)).Point;
                if(!player){
                    var delta=aim-c.Position;double angle=Math.IEEERemainder(Math.Atan2(delta.X,delta.Z)-c.Heading,Math.Tau);
                    double steering=Math.Atan2(2*c.Wheelbase*Math.Sin(angle),Math.Max(1,delta.Length));
                    double limit=.68/(1+Math.Pow(Math.Abs(c.Speed)/22,2));
                    c.Command=command with{Steering=Math.Clamp(steering/limit,-1,1)};
                }
                if(c.Physical)continue;
            }
            if(c.Recovering){c.Body.Vx=c.Body.Vy=c.Body.W=0;continue;}
            if(c.Physical&&c.Kind==10)continue;
            var pose=route.Sample(c.TransitAlong);var f=new CityPoint(Math.Sin(pose.Heading),Math.Cos(pose.Heading));
            double speed=CityPoint.Dot(new(c.Body.Vx,c.Body.Vy),f);
            bool brake=command.Brake||command.Handbrake||command.Throttle*speed<-.15;
            double acceleration=command.Throttle*1.25-speed*.025;
            if(brake){double amount=Math.Min(Math.Abs(speed),3.6*dt);speed-=Math.Sign(speed)*amount;acceleration=0;}
            speed=Math.Clamp(speed+acceleration*dt,-4.5,14);
            if(!player&&speed<0)speed=0;
            c.Braking=brake;c.Steering=0;
            c.Body.Vx=f.X*speed;c.Body.Vy=f.Z*speed;c.Body.W=0;c.Body.Angle=-pose.Heading;
            if(Math.Abs(speed)>.001)c.Body.Wake();
            if(!c.Physical){c.TransitAlong=route.Wrap(c.TransitAlong+speed*dt);pose=route.Sample(c.TransitAlong);c.Body.X=pose.Point.X;c.Body.Y=pose.Point.Z;c.Body.Angle=-pose.Heading;UpdateTerrainSupport(c);}
        }
    }
    private void ConstrainTrams(){
        foreach(var c in Cars)if(c.Kind==9&&c.Physical&&!c.Falling&&!c.Destroyed&&CityTransit.ById(c.TransitRoute) is {} route){
            // Project only within the local segment neighbourhood: a collision cannot
            // jump the tram onto the opposite side of a loop. Preserve tangent impulse.
            var old=route.Sample(c.TransitAlong);var f=new CityPoint(Math.Sin(old.Heading),Math.Cos(old.Heading));
            double moved=CityPoint.Dot(new CityPoint(c.Body.X,c.Body.Y)-old.Point,f);
            c.TransitAlong=route.Wrap(c.TransitAlong+Math.Clamp(moved,-1,1));var pose=route.Sample(c.TransitAlong);
            double speed=CityPoint.Dot(new(c.Body.Vx,c.Body.Vy),f);c.Body.X=pose.Point.X;c.Body.Y=pose.Point.Z;c.Body.Angle=-pose.Heading;c.Body.W=0;
            c.Body.Vx=Math.Sin(pose.Heading)*speed;c.Body.Vy=Math.Cos(pose.Heading)*speed;c.Roll=c.Pitch=c.RollVelocity=c.PitchVelocity=0;
        }
    }
    private DriveInput TrolleyPower(CityCar c,DriveInput input){
        if(c.Kind!=10||CityTransit.ById(c.TransitRoute) is not {} route)return input;
        bool powered=(route.Sample(route.Closest(c.Position)).Point-c.Position).Length<3;
        return powered?input:input with{Throttle=0};
    }
}
