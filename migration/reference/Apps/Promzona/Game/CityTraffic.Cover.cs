using System.Runtime.CompilerServices;
namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private sealed class CoverState
    {
        public double Next,Since,HoldUntil;
        public CityPoint Hide,Peek;
        public bool Found,Peeking,AllowsAim;
    }
    private readonly ConditionalWeakTable<CityPedestrian,CoverState> _cover=new();
    private bool CoverAllowsAim(CityPedestrian p)=>!_cover.TryGetValue(p,out var s)||!s.Found||s.AllowsAim;
    private bool CoverWalk(CityPedestrian p,double dt)
    {
        if(p.IsPlayer||!p.Physical||!p.Armed||p.Dead||p.Detached||p.KnockedFor>0||p.RoadLevel!=0||Time>=p.AggressionUntil||PlayerUnavailable||p.Officer&&WantedLevel<4)
        { _cover.Remove(p);return false; }
        var s=_cover.GetOrCreateValue(p);
        if(s.Next==0){s.Since=Time;s.Next=Time+(p.Officer?3.5:.75)+(p.Id%5)*.07;return false;}
        if(Time>=s.Next&&(!s.Found||Time-s.Since>12))
        {
            s.Next=Time+1.7+(p.Id%5)*.13;s.Found=false;
            double best=12;
            bool Blocked(CityPoint at)
            {
                if(!ClearLevelSight(at,0,PlayerPosition,OnFoot?Player.RoadLevel:Hero.RoadLevel))return true;
                var d=PlayerPosition-at;double distance=d.Length;
                foreach(var c in NearbyCars(at,14))if(c!=Hero&&!c.Falling&&Math.Abs(c.Speed)<.7&&(!_useScenery||Math.Abs(c.SurfaceHeight-CityTerrain.Ground(at))<1.5)&&
                    RayBox(at,d*(1/Math.Max(.01,distance)),c.Position,c.Heading,c.HalfWidth,c.HalfLength,distance)<distance)return true;
                return false;
            }
            void Consider(CityPoint hide,CityPoint peek)
            {
                double distance=(hide-p.Position).Length;
                if(distance>=best||!ClearPlayerPoint(hide,ignorePerson:p,roadLevel:0)||!ClearPlayerPoint(peek,ignorePerson:p,roadLevel:0)||
                    !ClearSight(p.Position,hide)||!ClearSight(hide,peek)||!Blocked(hide)||Blocked(peek))return;
                best=distance;s.Hide=hide;s.Peek=peek;s.Found=true;
            }
            foreach(var c in NearbyCars(p.Position,10))if(c!=Hero&&!c.Falling&&Math.Abs(c.Speed)<.7)
            {
                var f=new CityPoint(Math.Sin(c.Heading),Math.Cos(c.Heading));var right=Right(f);
                int side=CityPoint.Dot(PlayerPosition-c.Position,right)>0?-1:1;
                var hide=c.Position+right*(side*(c.HalfWidth+.8));
                for(int end=-1;end<=1;end+=2)Consider(hide,hide+f*(end*(c.HalfLength+1.1)));
            }
            int i=(int)Math.Floor((p.Position.X-Coordinate(0))/Spacing),j=(int)Math.Floor((p.Position.Z-Coordinate(0))/Spacing);
            if(_useScenery)for(int x=i-1;x<=i+1;x++)for(int z=j-1;z<=j+1;z++)foreach(int id in CityLayout.Current.BuildingIds(x,z))
            {
                var b=CityLayout.Current.Blocks[id];
                for(int sx=-1;sx<=1;sx+=2)for(int sz=-1;sz<=1;sz+=2)
                {
                    var corner=new CityPoint(b.X+sx*(b.HalfX+.85),b.Z+sz*(b.HalfZ+.85));
                    Consider(corner-new CityPoint(sx*2,0),corner);
                    Consider(corner-new CityPoint(0,sz*2),corner);
                }
            }
            if(s.Found){s.Since=Time;s.HoldUntil=0;s.Peeking=s.AllowsAim=false;}
        }
        if(!s.Found)return false;
        var target=s.Peeking?s.Peek:s.Hide;var delta=target-p.Position;
        // A parked car may have moved since the search; never walk into its new hull.
        if(!ClearPlayerPoint(target,ignorePerson:p,roadLevel:0)){s.Next=Time;s.Found=false;return false;}
        if(delta.Length>.4)
        {
            s.AllowsAim=false;s.HoldUntil=0;p.Aiming=false;p.AimTime=0;MovePerson(p,delta*(Math.Min(2.8,delta.Length*3)/delta.Length),dt,target);
        }
        else {
            MovePerson(p,default,dt);s.AllowsAim=s.Peeking;
            if(!s.Peeking){p.Aiming=false;p.AimTime=0;}
            // Start the exposure clock on arrival, not while still approaching.
            if(s.HoldUntil==0)s.HoldUntil=Time+(s.Peeking?1.8:1.1);
            if(Time>=s.HoldUntil){s.Peeking=!s.Peeking;s.HoldUntil=0;s.AllowsAim=false;}
        }
        return true;
    }
}
