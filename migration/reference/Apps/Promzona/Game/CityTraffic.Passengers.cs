namespace Promzona.Game;
public sealed record TransitRider(int CarId,double Destination,ActorSave Person);
public sealed partial class CityTraffic
{
    public List<TransitRider> TransitRiders {get;}=[];
    public int TransitBoardings {get;private set;}
    public int TransitAlightings {get;private set;}
    private double _passengerClock;
    private bool _passengersInitialized;
    private void ResetPassengers(){TransitRiders.Clear();TransitBoardings=TransitAlightings=0;_passengerClock=0;_passengersInitialized=false;}
    public static CityPoint TransitWaitingPoint(CityTransitRoute r,int index)
    {
        var pose=r.Sample(r.Stops[index]);var f=new CityPoint(Math.Sin(pose.Heading),Math.Cos(pose.Heading));
        double lane=r.Kind==9?1.9:CityLayout.WideRoads?5.2:1.9;
        return pose.Point+Right(f)*(CityTraffic.Sidewalk-lane);
    }
    public bool RideTransit()
    {
        if(PassengerMode)return InteractVehicle();
        if(!OnFoot||NearbyEnterableCar() is not {} c||c.TransitRoute<=0||c.Driverless||c.Burning||Math.Abs(c.Speed)>.8||c.TransitStopUntil<=Time)return false;
        if(!InteractVehicle()||EnteringCarId!=c.Id)return false;
        _passengerEntry=true;c.TransitStopUntil=Math.Max(c.TransitStopUntil,Time+6);return true;
    }
    private bool TransitWaitWalk(CityPedestrian p,double dt)
    {
        if(p.WaitingTransitRoute==0)return false;
        var r=CityTransit.ById(p.WaitingTransitRoute);
        if(r is null||p.WaitingTransitStop<0||p.WaitingTransitStop>=r.Stops.Length||Time>p.TransitWaitUntil||p.Fear>.15||p.AggressionUntil>Time||p.Dead)
        {p.WaitingTransitRoute=0;p.TransitRetryAt=Time+60;return false;}
        var target=TransitWaitingPoint(r,p.WaitingTransitStop)+new CityPoint((p.Id%3-1)*.7,((p.Id/3)%3-1)*.7);
        var stopped=NearbyCars(target,18).FirstOrDefault(c=>c.TransitRoute==r.Id&&!c.Driverless&&!c.Destroyed&&!c.Burning&&Math.Abs(c.Speed)<.8&&c.TransitStopUntil>Time);
        if(stopped is not null){var door=DoorPoint(stopped,1);if(ClearSight(p.Position,door))target=door;}
        var d=target-p.Position;var velocity=d.Length>.4?d*(Math.Min(1.4,d.Length*2)/d.Length):default;
        if(p.Physical)MovePerson(p,velocity,dt,target);else MoveFarSocial(p,velocity,dt);
        return true;
    }
    private void PassengerStep(double dt)
    {
        _passengerClock-=dt;if(_passengerClock>0||!_useScenery)return;_passengerClock=.5;
        // A new city starts during an operating service, not with an entirely empty fleet.
        // Move existing off-screen civilians into cabins, preserving IDs and total population.
        if(!_passengersInitialized){
            _passengersInitialized=true;
            var candidates=new Queue<CityPedestrian>(Pedestrians.Where(p=>!p.IsPlayer&&p.OwnedScooterId==0&&!p.Dead&&!p.Detached&&p.ServiceId==0&&p.GroupId<0&&p.Persona<4&&p.Boarding<0&&!p.Hailing&&p.Id!=ContractActorId&&p.KnockedFor<=0&&p.Fear<.1&&(p.Position-PlayerPosition).Length>100));
            foreach(var car in Cars.Where(c=>c.TransitRoute>0&&!c.Driverless&&!c.Destroyed&&!c.Burning)){
                var route=CityTransit.ById(car.TransitRoute);if(route is null)continue;
                int next=Array.FindIndex(route.Stops,d=>d>car.TransitAlong+20);if(next<0)next=0;
                for(int k=0;k<2&&candidates.Count>0&&TransitRiders.Count<96;k++){
                    var person=candidates.Dequeue();ReleasePerson(person);person.WaitingTransitRoute=0;person.Path=null;person.OriginKey=person.DestinationKey=-1;
                    TransitRiders.Add(new(car.Id,route.Stops[(next+1+k)%route.Stops.Length],new(CityState.Capture(person,compact:true),null,null)));
                    Remove(person.Body);Pedestrians.Remove(person);person.Physical=false;
                }
                car.Occupants=1+TransitRiders.Count(r=>r.CarId==car.Id);
            }
            RebuildActorGrid();
        }
        // Only existing walkers are assigned: riding does not manufacture people.
        foreach(var r in CityTransit.Routes)
        {
            int waiting=Pedestrians.Count(p=>p.WaitingTransitRoute==r.Id);
            for(int k=0;k<r.Stops.Length&&waiting<16;k++)
            {
                var point=TransitWaitingPoint(r,k);
                if(!ClearWalk(point)||!CityWater.UrbanSite(point,.8))continue;
                if(NearbyPeople(point,4).Count(p=>p.WaitingTransitRoute==r.Id)>=3)continue;
                var p=NearbyPeople(point,12).FirstOrDefault(p=>!p.IsPlayer&&p.OwnedScooterId==0&&!p.Dead&&!p.Detached&&p.KnockedFor<=0&&p.ServiceId==0&&p.GroupId<0&&p.Boarding<0&&!p.Hailing&&p.Id!=ContractActorId&&p.WaitingTransitRoute==0&&p.TransitRetryAt<=Time&&p.RoadLevel==0&&p.Fear<.1&&(p.Position-point).Length<12&&ClearSight(p.Position,point));
                if(p is null)continue;
                ReleasePerson(p);p.WaitingTransitRoute=r.Id;p.WaitingTransitStop=k;p.TransitWaitUntil=Time+100;waiting++;
            }
        }
        foreach(var c in Cars.Where(c=>c.TransitRoute>0).ToArray())
        {
            var route=CityTransit.ById(c.TransitRoute);if(route is null)continue;
            bool disabled=c.Destroyed||c.Falling;
            bool evacuate=c.Driverless||c.Burning;
            bool stopped=!disabled&&Math.Abs(c.Speed)<.8&&(c.TransitStopUntil>Time||evacuate);
            if(!stopped&&!disabled)continue;
            int stop=Enumerable.Range(0,route.Stops.Length).MinBy(k=>Math.Abs(Math.IEEERemainder(c.TransitAlong-route.Stops[k],route.Length)));
            foreach(var rider in TransitRiders.Where(r=>r.CarId==c.Id&&(disabled||evacuate||Math.Abs(Math.IEEERemainder(route.Stops[stop]-r.Destination,route.Length))<2)).ToArray())
            {
                var at=disabled||evacuate?DoorPoint(c,1):TransitWaitingPoint(route,stop);
                CityPoint? clear=null;
                for(int n=0;n<40;n++){var q=at+new CityPoint(Math.Sin(n*2.4),Math.Cos(n*2.4))*(.4+n*.16);if(CityWater.Depth(q)<.1&&ClearPlayerPoint(q,roadLevel:0)){clear=q;break;}}
                if(clear is null)continue;
                var p=new CityPedestrian();CityState.Restore(p,rider.Person.State);
                p.Position=clear.Value;p.WaitingTransitRoute=0;p.TransitRetryAt=Time+80;p.RoadLevel=0;p.Physical=false;
                p.Path=null;p.OriginKey=p.DestinationKey=-1;p.Wait=.7;
                Pedestrians.Add(p);Register(p);Reanchor(p);
                if(disabled)FatalPerson(p,false);else if(evacuate){p.Fear=1;p.FearFrom=c.Position;}
                TransitRiders.Remove(rider);TransitAlightings++;
            }
            c.Occupants=(c.Driverless?0:1)+TransitRiders.Count(r=>r.CarId==c.Id);
            if(!stopped||evacuate||TransitRiders.Count>=96)continue;
            int count=TransitRiders.Count(r=>r.CarId==c.Id);
            foreach(var p in NearbyPeople(DoorPoint(c,1),1).Where(p=>!p.IsPlayer&&p.OwnedScooterId==0&&!p.Dead&&p.KnockedFor<=0&&p.Fear<.15&&p.WaitingTransitRoute==route.Id&&p.WaitingTransitStop==stop&&(p.Position-DoorPoint(c,1)).Length<.8).ToArray())
            {
                if(count>=CityTransitCabin.Capacity||TransitRiders.Count>=96)break;
                double destination=route.Stops[(stop+1+p.Id%3)%route.Stops.Length];
                ReleasePerson(p);p.WaitingTransitRoute=0;p.Path=null;p.OriginKey=p.DestinationKey=-1;
                TransitRiders.Add(new(c.Id,destination,new(CityState.Capture(p,compact:true),null,null)));
                Remove(p.Body);Pedestrians.Remove(p);p.Physical=false;TransitBoardings++;count++;c.DoorHold=1.5;
            }
            c.Occupants=1+count;
        }
        // A removed vehicle cannot leave permanent orphan passenger records.
        TransitRiders.RemoveAll(r=>Cars.All(c=>c.Id!=r.CarId));
        RebuildActorGrid();
    }
}
