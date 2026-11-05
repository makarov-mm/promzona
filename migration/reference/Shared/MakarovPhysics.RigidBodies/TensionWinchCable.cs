using System.Numerics;
namespace MakarovPhysics.RigidBodies;

/// <summary>An opt-in tensile cable with finite drum speed, force and mechanical power.
/// An implicit axial spring regularizes four redundant cables. Positive paid-out rate
/// lowers a load; negative rate reels in. The old Joint.Rope behavior is unchanged.</summary>
public sealed class TensionWinchCable : MechanismConstraint
{
    private readonly Row _row=new();
    private float _length, _target, _force=500, _power=300, _speed=1, _stiffness=12000, _damping=120, _dt;
    public Vector3 Anchor { get; }
    public Vector3 LocalAttachment { get; }
    public float PaidLength=>_length;
    public float TargetLength { get=>_target;set=>_target=Nonnegative(value,nameof(TargetLength)); }
    public float MaxForce { get=>_force;set=>_force=Nonnegative(value,nameof(MaxForce)); }
    public float MaxPower { get=>_power;set=>_power=Nonnegative(value,nameof(MaxPower)); }
    public float MaxSpeed { get=>_speed;set=>_speed=Nonnegative(value,nameof(MaxSpeed)); }
    public float Stiffness { get=>_stiffness;set=>_stiffness=Valid(value,nameof(Stiffness))>0?value:throw new ArgumentOutOfRangeException(nameof(Stiffness)); }
    public float Damping { get=>_damping;set=>_damping=Nonnegative(value,nameof(Damping)); }
    public float Rate { get; private set; }
    public float Tension=>_dt>0?_row.Impulse/_dt:0;
    public double Power=>-Tension*Rate;
    public double Work { get; private set; }
    public Vector3 Attachment=>A.Position+Vector3.Transform(LocalAttachment,A.Rotation);
    public double Extension=>Math.Max(0,Vector3.Distance(Attachment,Anchor)-PaidLength);
    public TensionWinchCable(RigidBody body,Vector3 localAttachment,Vector3 anchor,float paidLength):base(body,null)
    {
        if(!Finite(localAttachment)||!Finite(anchor))throw new ArgumentException("Finite cable attachments required.");
        LocalAttachment=localAttachment;Anchor=anchor;_length=_target=Nonnegative(paidLength,nameof(paidLength));
    }
    public override void Prepare(float dt)
    {
        if(!(dt>0)||!float.IsFinite(dt))throw new ArgumentOutOfRangeException(nameof(dt));
        _dt=dt;
        float speedLimit=MaxForce>0?Math.Min(MaxSpeed,MaxPower/MaxForce):MaxSpeed;
        Rate=Math.Clamp((TargetLength-PaidLength)/dt,-speedLimit,speedLimit);
        var r=Vector3.Transform(LocalAttachment,A.Rotation);var delta=Anchor-A.Position-r;
        float distance=delta.Length();var n=distance>1e-6f?delta/distance:Vector3.UnitY;
        var soft=Soft(Stiffness,Damping,dt);
        _row.Set(A,null,n,Vector3.Cross(r,n),Vector3.Zero,Vector3.Zero,
            Rate+soft.Rate*(PaidLength-distance),0,MaxForce*dt,soft.Gamma);
    }
    public override void Solve()=>_row.Solve();
    public override void Finish(float dt){_length=Math.Max(0,_length+Rate*dt);Work+=Power*dt;}
}
