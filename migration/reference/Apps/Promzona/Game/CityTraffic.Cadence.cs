namespace Promzona.Game;

public sealed partial class CityTraffic
{
    internal struct FarRoutineState
    {
        public double Next, Last;
        public bool Active, Afraid;
    }
    private bool FarRoutine(CityPedestrian p,double dt)
    {
        ref var state = ref p.FarRoutine;
        bool afraid=p.Fear>.12;
        if(Time>=state.Next||Time<state.Last||afraid!=state.Afraid)
        {
            double elapsed=state.Next==0?dt:Math.Clamp(Time-state.Last,dt,.25);
            state.Last=Time;state.Next=Time+.18+(p.Id%7)*.01;state.Afraid=afraid;
            state.Active=RoleWalk(p,elapsed)||SocialVelocity(p,out var velocity)&&MoveFarSocial(p,velocity,elapsed);
        }
        // Positions still integrate every frame. Services, boarding, level changes,
        // traffic lights and crossing reservations are never delayed by this cache.
        return state.Active;
    }
    private void SyncFarPoses(double dt)
    {
        foreach(var c in Cars)if(!c.Physical&&(c.TransitRoute==0||c.Destroyed))
        {
            var old=c.Position;c.Position=new(c.Body.X,c.Body.Y);c.Heading=-c.Body.Angle;
            c.Speed=c.Body.Vx*Math.Sin(c.Heading)+c.Body.Vy*Math.Cos(c.Heading);
            c.Distance+=(c.Position-old).Length;
        }
        foreach(var p in Pedestrians)if(!p.Physical)
        {
            var old=p.Position;p.Position=new(p.Body.X,p.Body.Y);
            p.Speed=Math.Sqrt(p.Body.Vx*p.Body.Vx+p.Body.Vy*p.Body.Vy);p.Distance+=(p.Position-old).Length;
            if(p.Speed>.15&&p.KnockedFor<=0&&!p.Dead)
                p.Heading+=Math.Clamp(Math.IEEERemainder(Math.Atan2(p.Body.Vx,p.Body.Vy)-p.Heading,Math.Tau),-5*dt,5*dt);
        }
    }
}
