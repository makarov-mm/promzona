using System.Numerics;
using MakarovPhysics.Numerics;
using MakarovPhysics.RigidBodies;
namespace MakarovPhysics.Experiments;
/// <summary>A servo linkage attached to a massive base. Payload/base
/// contacts and capture constraints use the sandbox solver. Moving the capture
/// anchor exchanges impulses between both bodies; a free base is never pinned.
/// Ring 6 (20.10): the links carry mass - the arm's centre of mass is a point
/// mass <see cref="ArmMass"/> at the mass-weighted middle of the two links, and
/// in free flight every servo step moves the base the other way so the system's
/// centre of mass stays put. Its angular momentum is kept either by the base
/// turning (the reduced mass μ = m_a M/(M + m_a) at the arm's lever adds to the
/// base's inertia, as for any two-body internal motion) or, with
/// <see cref="AttitudeHold"/>, by reaction wheels that store the arm's angular
/// momentum while the attitude stands - what a flying robot does during a
/// capture, because a base that swings 40° under its own arm cannot reach
/// anything (the SelfTest capture of 20.10 found exactly that). Flexible modes are
/// omitted. Station mode has an external station-keeping support, explicitly
/// distinct from free-flight mode. Twin: Tools/twins/space_manipulator_twin.py.</summary>
public abstract class SpaceManipulatorDynamics : SpaceExperiment
{
    public readonly PhysicsWorld World=new(){Gravity=Vector3.Zero,LinearDamping=0,AngularDamping=0,EnableSleeping=false,EnableGroundPlane=false,SolverIterations=28,MaxIterations=48};
    public RigidBody BaseBody=null!;
    public readonly List<RigidBody> Cargo=[];
    public Vector3 ToolLocal=new(.7f,.7f,0),Target=new(1.6f,.6f,0),ElbowLocal;
    public RigidBody? Held;
    private readonly List<Joint> _grasp=[];
    public double ServoSpeed=.45,Payload=25,Grip,Completed;
    /// <summary>Mass of the two links together, kg (an empty arm reacts on the base through it).</summary>
    public double ArmMass=60;
    /// <summary>Reaction wheels hold the base's attitude against the arm's torque (on by default in free flight); off, the base turns under the arm.</summary>
    public bool AttitudeHold=true;
    /// <summary>Angular momentum the wheels have taken from the arm's motion (world), N·m·s.</summary>
    public Vector3 WheelMomentum;
    /// <summary>The base box's mass, kg, and its half extents, m.</summary>
    public const float BaseMass=240;
    public static readonly Vector3 BaseHalf=new(.45f,.35f,.45f);
    /// <summary>The base box's principal moments of inertia: M/12 (b² + c²) and so on - 26, 32.4, 26 kg m².</summary>
    public static readonly Vector3 BaseInertia=new(BaseMass/12*(4*BaseHalf.Y*BaseHalf.Y+4*BaseHalf.Z*BaseHalf.Z),BaseMass/12*(4*BaseHalf.X*BaseHalf.X+4*BaseHalf.Z*BaseHalf.Z),BaseMass/12*(4*BaseHalf.X*BaseHalf.X+4*BaseHalf.Y*BaseHalf.Y));
    /// <summary>Centre of mass of the arm in the base frame: the upper link's middle and the forearm's middle, weighted equally.</summary>
    public Vector3 ArmCentreLocal=>(ElbowLocal*.5f+(ElbowLocal+ToolLocal)*.5f)*.5f;
    /// <summary>The reduced mass of the arm and the base, m_a M/(M + m_a).</summary>
    public double ReducedMass=>ArmMass*BaseMass/(BaseMass+ArmMass);
    /// <summary>Where the base goes when the arm's centre moves by <paramref name="armShift"/> (world, including what the base's own turn did to it): −m_a/(M + m_a) of it.</summary>
    public Vector3 BaseShift(Vector3 armShift)=>-armShift*(float)(ArmMass/(BaseMass+ArmMass));
    /// <summary>The angular momentum the arm's centre takes about the system's centre when it moves by <paramref name="armShift"/> (world) at lever <paramref name="lever"/> from the base: μ lever × armShift.</summary>
    public Vector3 ArmAngularMomentum(Vector3 lever,Vector3 armShift)=>Vector3.Cross(lever,armShift)*(float)ReducedMass;
    /// <summary>The base's rotation (world axis-angle) that keeps the angular momentum about the system's centre at zero when the arm's centre, at lever <paramref name="lever"/> from the base, moves by <paramref name="armShift"/>: −(I_b + μ (l² 1 − l lᵀ))⁻¹ μ (lever × armShift) in the base frame - the arm's point mass at its lever belongs to the inertia that turns.</summary>
    public Vector3 BaseTurn(Vector3 lever,Vector3 armShift)
    {
        var q=Quaternion.Conjugate(BaseBody.Rotation);var l=Vector3.Transform(lever,q);var h=Vector3.Transform(ArmAngularMomentum(lever,armShift),q);
        double mu=ReducedMass,l2=l.LengthSquared();
        // I_eff = diag(I_b) + mu (l² 1 − l lᵀ), symmetric; solved by Cramer's rule
        double a=BaseInertia.X+mu*(l2-l.X*l.X),b=-mu*l.X*l.Y,c=-mu*l.X*l.Z,d=BaseInertia.Y+mu*(l2-l.Y*l.Y),e=-mu*l.Y*l.Z,f=BaseInertia.Z+mu*(l2-l.Z*l.Z);
        double det=a*(d*f-e*e)-b*(b*f-e*c)+c*(b*e-d*c);
        double x=(h.X*(d*f-e*e)-b*(h.Y*f-e*h.Z)+c*(h.Y*e-d*h.Z))/det;
        double y=(a*(h.Y*f-e*h.Z)-h.X*(b*f-e*c)+c*(b*h.Z-h.Y*c))/det;
        double z=(a*(d*h.Z-h.Y*e)-b*(b*h.Z-h.Y*c)+h.X*(b*e-d*c))/det;
        return -Vector3.Transform(new Vector3((float)x,(float)y,(float)z),BaseBody.Rotation);
    }
    public int Selected,Phase;private double _hold;
    protected abstract bool FreeBase {get;}
    public Vector3 Tool=>BaseBody.Position+Vector3.Transform(ToolLocal,BaseBody.Rotation);
    public Vector3 Momentum=>World.Bodies.Where(b=>!b.IsStatic).Aggregate(Vector3.Zero,(a,b)=>a+b.Velocity*b.Mass);
    public override string[] Scenarios=>FreeBase?["CaptureDrifter","HeavyPayload","OffsetCargo"]:["CargoTransfer","InstallInstrument","OffsetCargo"];
    public override SpaceParameter[] Parameters=>FreeBase?[new("ServoSpeed",()=>ServoSpeed,v=>ServoSpeed=v,.1,.8,"m/s"),new("PayloadMass",()=>Payload,v=>{Payload=v;Reset(Scenario);},5,100,"kg"),new("ArmMass",()=>ArmMass,v=>ArmMass=v,10,150,"kg")]:[new("ServoSpeed",()=>ServoSpeed,v=>ServoSpeed=v,.1,.8,"m/s"),new("PayloadMass",()=>Payload,v=>{Payload=v;Reset(Scenario);},5,100,"kg")];
    public override SpaceSwitch[] Switches=>FreeBase?[new("AttitudeHold",()=>AttitudeHold,v=>AttitudeHold=v)]:[];
    public override SpaceReading[] Readings=>FreeBase
        ?[new("BaseSpeed",BaseBody.Velocity.Length(),"m/s"),new("ToolError",Vector3.Distance(Tool,Target),"m"),new("Momentum",Momentum.Length(),"kg m/s"),new("WheelMomentum",WheelMomentum.Length(),"N m s"),new("Completed",Completed)]
        :[new("BaseSpeed",BaseBody.Velocity.Length(),"m/s"),new("ToolError",Vector3.Distance(Tool,Target),"m"),new("Momentum",Momentum.Length(),"kg m/s"),new("Completed",Completed)];
    public override void Reset(int scenario=0)
    {
        Begin(scenario);World.Clear();Cargo.Clear();_grasp.Clear();Held=null;Grip=Completed=_hold=0;Selected=Phase=0;
        // CreateBox takes a DENSITY: 240 there made a 136 kg box (0.9 × 0.7 × 0.9 m³ × 240), and the twin's
        // "240 kg base" was never the body's mass until 20.10 - the density is the mass over the volume
        BaseBody=FreeBase?RigidBody.CreateBox(new(-1.2f,0,0),BaseHalf,BaseMass/(8*BaseHalf.X*BaseHalf.Y*BaseHalf.Z)):RigidBody.CreateStaticBox(new(-1.2f,0,0),BaseHalf);BaseBody.Breakable=false;World.Bodies.Add(BaseBody);
        WheelMomentum=Vector3.Zero;
        for(int i=0;i<3;i++){var b=RigidBody.CreateBox(new(.4f+i*.35f,.6f,i%2==0?.45f:-.45f),new(.18f,.18f,.18f),(float)((scenario==1?2:1)*Payload/(.36*.36*.36)));b.Restitution=.08f;b.Velocity=FreeBase?new(0,0,i==0?.008f:0):Vector3.Zero;Cargo.Add(b);World.Bodies.Add(b);}
        Selected=scenario==2?2:0;ToolLocal=new(.7f,.8f,.1f);Target=Cargo[Selected].Position;SolveElbow();
    }
    public override void Primary()
    {
        if(Held!=null){foreach(var j in _grasp)World.Joints.Remove(j);_grasp.Clear();Held=null;Grip=0;Automatic=false;Completed=0;Phase=0;return;}
        var b=Cargo.OrderBy(b=>Vector3.Distance(b.Position,Tool)).First();
        if(Vector3.Distance(b.Position,Tool)>.25){Status="OutOfReach";return;}
        Held=b;Grip=1;
        foreach(var offset in new[]{new Vector3(.07f,0,0),new Vector3(0,.07f,0),new Vector3(0,0,.07f)})
        {var at=Tool+offset;var j=new Joint{Type=Joint.Kind.Point,A=BaseBody,B=b,LocalA=Vector3.Transform(at-BaseBody.Position,Quaternion.Conjugate(BaseBody.Rotation)),LocalB=Vector3.Transform(at-b.Position,Quaternion.Conjugate(b.Rotation))};World.AddJoint(j);_grasp.Add(j);}
    }
    public override void Secondary(){Selected=(Selected+1)%Cargo.Count;Target=Cargo[Selected].Position;Phase=0;Completed=0;_hold=0;}
    private void SolveElbow(){var d=ToolLocal;double length=Math.Max(.001,d.Length());var dir=d/(float)length;var up=Vector3.Cross(dir,Vector3.UnitZ);if(up.LengthSquared()<.01)up=Vector3.UnitY;up=Vector3.Normalize(up);if(up.Y<0)up=-up;ElbowLocal=d*.5f+up*(float)Math.Sqrt(Math.Max(.01,1.45*1.45-length*length/4));}
    public override void Step(double dt)
    {
        CheckStep(dt);Time+=dt;
        if(Automatic && Completed < 1){if(Phase==0){Target=Cargo[Selected].Position;if(Vector3.Distance(Tool,Target)<.08){Primary();if(Held!=null)Phase=1;}}
          else if(Phase==1){Target=BaseBody.Position+Rotate(new Vector3(1.1f,1.45f,-.7f),BaseBody.Rotation);if(Vector3.Distance(Tool,Target)<.12){_hold+=dt;if(_hold>2){Phase=2;_hold=0;}}}
          else{Target=BaseBody.Position+Rotate(new Vector3(1.0f,Scenario==1?1.15f:.35f,-1.05f),BaseBody.Rotation);if(Vector3.Distance(Tool,Target)<.015){_hold+=dt;if(_hold>2){Completed=1;Phase=3;}}}
        }else if(!Automatic) Target+=Move*(float)(dt*.65);
        if(Automatic && Completed>=1)Target=BaseBody.Position+Rotate(ToolLocal,BaseBody.Rotation);
        Vector3 local=Vector3.Transform(Target-BaseBody.Position,Quaternion.Conjugate(BaseBody.Rotation));local=Limit(local,2.65);local.Y=Math.Max(.15f,local.Y);var before=ToolLocal;var centreBefore=ArmCentreLocal;ToolLocal+=Limit(local-ToolLocal,ServoSpeed*dt);var delta=ToolLocal-before;foreach(var j in _grasp)j.LocalA+=delta;
        if(FreeBase&&delta.LengthSquared()>0){
            // the massive arm's reaction (ring 6, 20.10): the arm's centre moved in the
            // base frame, so the base shifts the other way, and its angular momentum
            // goes either into the base's turn or into the reaction wheels - the
            // system's centre of mass and angular momentum are untouched by an
            // internal motion. The shift is taken from the arm centre's world offset
            // before and after, so a turn about the base's own centre does not move
            // the system's centre either (the first cut turned first and shifted by
            // the servo part alone, and the centre of mass crept)
            SolveElbow();var rotationBefore=BaseBody.Rotation;var offsetBefore=Vector3.Transform(centreBefore,rotationBefore);
            var armShift=Vector3.Transform(ArmCentreLocal-centreBefore,rotationBefore);
            var lever=Vector3.Transform((ArmCentreLocal+centreBefore)*.5f,rotationBefore);
            if(AttitudeHold)WheelMomentum+=ArmAngularMomentum(lever,armShift);
            else{var turn=BaseTurn(lever,armShift);var rotation=BaseBody.Rotation;IntegrateRotation(ref rotation,turn,1);BaseBody.Rotation=rotation;}
            BaseBody.Position+=BaseShift(Vector3.Transform(ArmCentreLocal,BaseBody.Rotation)-offsetBefore);
        }
        if(FreeBase&&Turn.LengthSquared()>0){BaseBody.AngularVelocity+=Turn*(float)(dt*.04);BaseBody.Wake();}
        foreach(var b in World.Bodies)if(!b.IsStatic)b.Wake();World.Step((float)dt);SolveElbow();Status=Completed>=1?"TransferComplete":Held!=null?"Carrying":Automatic?"Approaching":"Manual";
    }
}
