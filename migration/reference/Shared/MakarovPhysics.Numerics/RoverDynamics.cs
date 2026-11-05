using System.Numerics;
namespace MakarovPhysics.Numerics;
/// <summary>Six compliant wheel contacts on an analytic heightfield. Traction is
/// capped by summed normal load times friction; gravity drives slope motion.
/// Chassis pitch/roll follow a damped support plane. This is a reduced suspension
/// model, not a tyre finite-element solver or a full rocker-bogie linkage.</summary>
public abstract class RoverDynamics : SpaceExperiment
{
    public double Gravity=1.62,Friction=.65,DriveSpeed=1.2,Roughness=.7;
    public Vector3 Position,Velocity;
    public double Heading,Pitch,Roll,WheelAngle,NormalLoad,Slip,Distance,SampleProgress;
    public int Waypoint,Samples;
    public bool Sampling;
    public readonly Vector3[] Wheels=new Vector3[6];
    public readonly double[] Loads=new double[6];
    public readonly List<Vector3> Trail=[];
    public Vector3[] Targets=[new(5,0,3),new(5,0,-4),new(-5,0,-4),new(-5,0,4)];
    protected abstract bool Mars {get;}
    protected virtual float HalfExtent=>9.4f;
    protected virtual double ContactHeight(double x,double z)=>Terrain(x,z);
    protected virtual void ResolveObstacles() { }
    public override string[] Scenarios=>["Survey","Ridge","LowGrip"];
    public override SpaceParameter[] Parameters=>[new("Speed",()=>DriveSpeed,v=>DriveSpeed=v,.15,2.2,"m/s"),new("Friction",()=>Friction,v=>Friction=v,.05,1),new("Roughness",()=>Roughness,v=>Roughness=v,0,1.5)];
    public override SpaceReading[] Readings=>[new("Speed",Velocity.Length(),"m/s"),new("Slip",Slip*100,"%"),new("Load",NormalLoad,"N"),new("Samples",Samples),new("Distance",Distance,"m")];
    public virtual double Terrain(double x,double z){double cr=Math.Sqrt((x-2)*(x-2)+(z+1)*(z+1));return Roughness*(.16*Math.Sin(.6*x)*Math.Cos(.45*z)+.30*Math.Exp(-Math.Pow((cr-1.5)/.45,2))-.28*Math.Exp(-cr*cr/1.5)+(Scenario==1?.055*x:0));}
    public override void Reset(int scenario=0){Begin(scenario);Gravity=Mars?3.71:1.62;Friction=scenario==2?.12:.65;Position=new(-5,(float)Terrain(-5,3)+.8f,3);Velocity=Vector3.Zero;Heading=0;Pitch=Roll=WheelAngle=Distance=SampleProgress=Slip=0;Waypoint=Samples=0;Sampling=false;Trail.Clear();UpdateWheels(.01);}
    public override void Primary(){Sampling=!Sampling;SampleProgress=0;}
    public override void Secondary(){Velocity+=new Vector3(0,.8f,0);}
    private void UpdateWheels(double dt){NormalLoad=0;double front=0,back=0,left=0,right=0;for(int i=0;i<6;i++){double a=(i%3-1)*.65,b=(i<3?-1:1)*.62;double x=Position.X+a*Math.Cos(Heading)-b*Math.Sin(Heading),z=Position.Z+a*Math.Sin(Heading)+b*Math.Cos(Heading),y=ContactHeight(x,z)+.24,mount=Position.Y+a*Math.Sin(Pitch)+b*Math.Sin(Roll);Wheels[i]=new((float)x,(float)Math.Clamp(y,mount-.82,mount-.28),(float)z);double extension=mount-y;Loads[i]=y>=mount-.82?Math.Clamp(720*(.58-extension)-85*Velocity.Y,0,2000):0;NormalLoad+=Loads[i];if(i%3==2)front+=y;if(i%3==0)back+=y;if(i<3)left+=y;else right+=y;}
      double p=Math.Atan2((front-back)/2,1.3),r=Math.Atan2((right-left)/3,1.24);if(NormalLoad>1){Pitch+=(p-Pitch)*Math.Min(1,dt*5);Roll+=(r-Roll)*Math.Min(1,dt*5);}}
    public override void Step(double dt){CheckStep(dt);int n=(int)Math.Ceiling(dt*240);for(int i=0;i<n;i++)Integrate(dt/n);}
    private void Integrate(double dt){Time+=dt;double drive=Move.Z,steer=Move.X;var target=Targets[Waypoint];var delta=target-Position;delta.Y=0;double yaw=Angle(Math.Atan2(delta.Z,delta.X)-Heading);if(Automatic){steer=Clamp(yaw*1.5,-1,1);drive=Math.Max(.1,Math.Cos(yaw));if(delta.Length()<.55||Mars&&Sampling){if(Mars){Sampling=true;if(SampleProgress>=3){Samples++;Waypoint=(Waypoint+1)%Targets.Length;Sampling=false;SampleProgress=0;}}else{Waypoint=(Waypoint+1)%Targets.Length;Samples++;}}}
      if(Sampling){drive=0;SampleProgress+=dt;Status="Sampling";}else Status=Automatic?"Surveying":"Manual";
      UpdateWheels(dt);const double mass=120;var forward=new Vector3((float)Math.Cos(Heading),0,(float)Math.Sin(Heading));double speed=Vector3.Dot(Velocity,forward),desired=drive*DriveSpeed,request=(desired-speed)*mass*2,limit=Friction*NormalLoad;double force=Clamp(request,-limit,limit);
      var side=new Vector3(-forward.Z,0,forward.X);double lateral=Vector3.Dot(Velocity,side),sideForce=-lateral*mass*3;
      double forceScale=Math.Min(1,limit/Math.Max(1e-12,Math.Sqrt(request*request+sideForce*sideForce)));force=request*forceScale;sideForce*=forceScale;Slip=Math.Abs(request)>1?1-forceScale:0;
      double gx=(Terrain(Position.X+.02,Position.Z)-Terrain(Position.X-.02,Position.Z))/.04,gz=(Terrain(Position.X,Position.Z+.02)-Terrain(Position.X,Position.Z-.02))/.04;
      if(NormalLoad<1){gx=0;gz=0;}
      var accel=forward*(float)(force/mass)+side*(float)(sideForce/mass)+new Vector3((float)(-Gravity*gx),(float)(NormalLoad/mass-Gravity),(float)(-Gravity*gz));Velocity+=accel*(float)dt;Position+=Velocity*(float)dt;Heading+=steer*Math.Min(1,NormalLoad/(mass*Gravity))*dt*.75;WheelAngle+=speed/.24*dt;Distance+=Math.Sqrt(Velocity.X*Velocity.X+Velocity.Z*Velocity.Z)*dt;
      ResolveObstacles();
      if(Math.Abs(Position.X)>HalfExtent||Math.Abs(Position.Z)>HalfExtent){Position.X=Math.Clamp(Position.X,-HalfExtent,HalfExtent);Position.Z=Math.Clamp(Position.Z,-HalfExtent,HalfExtent);Velocity.X*=.1f;Velocity.Z*=.1f;Status="Boundary";}
      if(Trail.Count==0||Vector3.Distance(Trail[^1],Position)>.18f){Trail.Add(Position);if(Trail.Count>600)Trail.RemoveAt(0);}
    }
}
