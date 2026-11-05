using System.Numerics;
namespace MakarovPhysics.Numerics;

public sealed record SpaceParameter(string Key, Func<double> Get, Action<double> Set, double Min, double Max, string Unit="");
public readonly record struct SpaceReading(string Key,double Value,string Unit="");
/// <summary>An on/off control of an experiment (a checkbox on the panel, keyed like a parameter).</summary>
public sealed record SpaceSwitch(string Key, Func<bool> Get, Action<bool> Set);
/// <summary>Shared controls and numerical utilities, not a shared substitute model.
/// Every experiment advances its own state in SI units. Inputs are held commands;
/// changing view, drawing and scrubbing presentation never advances physics.</summary>
public abstract class SpaceExperiment
{
    public double Time {get;protected set;}
    public bool Automatic=true;
    public int Scenario {get;protected set;}
    public Vector3 Move,Turn;
    public string Status {get;protected set;}="Ready";
    public virtual string[] Scenarios=>["Reference","Challenge","Recovery"];
    public abstract SpaceParameter[] Parameters {get;}
    public abstract SpaceReading[] Readings {get;}
    /// <summary>On/off controls; none by default.</summary>
    public virtual SpaceSwitch[] Switches=>[];
    public abstract void Reset(int scenario=0);
    public abstract void Step(double dt);
    public virtual void Primary(){}
    public virtual void Secondary(){}
    protected void Begin(int scenario){Scenario=scenario;Time=0;Move=Turn=Vector3.Zero;Status="Ready";}
    public static double Clamp(double x,double a,double b)=>Math.Clamp(x,a,b);
    public static double Angle(double a)=>Math.Atan2(Math.Sin(a),Math.Cos(a));
    public static Vector3 Limit(Vector3 a,double maximum)=>a.LengthSquared()>maximum*maximum?Vector3.Normalize(a)*(float)maximum:a;
    public static Vector3 Rotate(Vector3 a,Quaternion q)=>Vector3.Transform(a,q);
    public static void IntegrateRotation(ref Quaternion q,Vector3 worldOmega,double dt){double speed=worldOmega.Length();if(speed>1e-10)q=Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(worldOmega),(float)(speed*dt))*q);}
    public static Vector3 RotationError(Quaternion target,Quaternion current){var e=Quaternion.Normalize(target*Quaternion.Conjugate(current));if(e.W<0)e=new(-e.X,-e.Y,-e.Z,-e.W);var v=new Vector3(e.X,e.Y,e.Z);double n=v.Length();return n<1e-9?Vector3.Zero:v*(float)(2*Math.Atan2(n,e.W)/n);}
    protected static void CheckStep(double dt){if(!double.IsFinite(dt)||dt<=0||dt>.1)throw new ArgumentOutOfRangeException(nameof(dt));}
}
