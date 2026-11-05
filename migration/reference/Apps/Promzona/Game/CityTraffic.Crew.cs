namespace Promzona.Game;

public sealed partial class CityTraffic
{
    // Occupants have persistent count, then become independent pedestrian actors.
    // Reserve all exit points before changing possession, so a failed carjack is atomic.
    public int DeployCrew(CityCar c,ServiceMission? mission,bool ejected)
    {
        int count=c.OccupantCount;
        if(count==0)return 0;
        var points=new List<CityPoint>();
        var f=new CityPoint(Math.Sin(c.Heading),Math.Cos(c.Heading));var right=Right(f);
        for(int seat=0;seat<count;seat++){
            CityPoint? chosen=null;
            for(int attempt=0;attempt<32;attempt++){
                int side=(seat+attempt)%2==0?1:-1;
                double along=(seat/2)*-1.2+.6+(attempt/2%4-1.5)*.65;
                var at=c.Position+right*(side*(c.HalfWidth+1.0+attempt/8*.55))+f*along;
                if(ClearPlayerPoint(at,c)&&points.All(p=>(p-at).Length>.75)){chosen=at;break;}
            }
            if(chosen is null)return 0;
            points.Add(chosen.Value);
        }
        for(int n=0;n<count;n++){
            var at=points[n];var p=MakePerson(c.I,c.J,1,1,at);
            p.Persona=c.PoliceVehicle?(c.Kind==14?13:c.Kind==13?12:c.Kind==7?6:5):c.Kind==4?4:1;
            p.ServiceId=mission?.CarId??0;p.Fear=c.PoliceVehicle?.1:1;
            p.FearFrom=PlayerPosition;p.KnockedFor=ejected?1.25:0;
            p.VoiceAt=Time+3;p.AggressionUntil=p.Officer&&WantedLevel>0?Time+12:0;
            Register(p);Pedestrians.Add(p);Reanchor(p);mission?.Medics.Add(p.Id);
            if(ejected){p.Detached=true;Remove(p.Body);var push=at-c.Position;p.Body.Vx=push.X*.65;p.Body.Vy=push.Z*.65;p.VerticalSpeed=1;p.FlightHeight=.05;}
        }
        if(ejected)ReportCrime(c.PoliceVehicle?1.25:.65);
        c.Occupants=0;c.Driverless=true;c.CrewDeployed=c.PoliceVehicle;c.DoorHold=1.8;
        FreePoliceReservation(c);return count;
    }
}
