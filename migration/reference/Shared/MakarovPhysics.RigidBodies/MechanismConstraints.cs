using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>Opt-in SI mechanism constraints, interleaved with contacts. B=null is the fixed world.
/// Motors have a per-substep impulse budget, not a budget per solver iteration.</summary>
public abstract class MechanismConstraint
{
    public RigidBody A { get; }
    public RigidBody? B { get; }
    protected MechanismConstraint(RigidBody a, RigidBody? b)
    {
        ArgumentNullException.ThrowIfNull(a);
        if (a == b || (a.IsStatic && (b is null || b.IsStatic))) throw new ArgumentException("A constraint needs distinct bodies and a dynamic body.");
        A=a; B=b;
    }
    public bool Involves(RigidBody body) => A==body || B==body;
    /// <summary>Joints this coupling reads (its shafts and guides). PhysicsWorld refuses the coupling
    /// before them and removes it with them. Gear and brake keep their own older checks.</summary>
    internal virtual IReadOnlyList<MechanismConstraint> Supports => [];
    public abstract void Prepare(float dt);
    public abstract void Solve();
    public virtual void Finish(float dt) { }
    /// <summary>Warm start, apply half: begin the rows that keep their meaning from one substep to the
    /// next from last substep's impulses, put on the bodies in a pass of its own after every constraint
    /// has been prepared - the contacts' order (see PhysicsWorld.WarmStart). Called only when
    /// <see cref="PhysicsWorld.WarmStartMechanisms"/> is set; constraints without such rows ignore it.</summary>
    internal virtual void WarmStart() { }
    internal void Wake() { A.Wake(); B?.Wake(); }
    protected static Vector3 Axis(Vector3 v)
    {
        if (!Finite(v) || v.LengthSquared()<1e-10f) throw new ArgumentException("Axis must be finite and nonzero.");
        return Vector3.Normalize(v);
    }
    protected static bool Finite(Vector3 v) => float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    protected static Vector3 Perpendicular(Vector3 n) => Vector3.Normalize(Vector3.Cross(n,Math.Abs(n.Y)<.8f?Vector3.UnitY:Vector3.UnitX));
    protected static Vector3 Local(RigidBody? b,Vector3 point) => b is null?point:Vector3.Transform(point-b.Position,Quaternion.Conjugate(b.Rotation));
    protected static Vector3 Direction(RigidBody? b,Vector3 axis) => b is null?axis:Vector3.Transform(axis,Quaternion.Conjugate(b.Rotation));
    protected static Vector3 WorldDirection(RigidBody? b,Vector3 axis) => b is null?axis:Vector3.Transform(axis,b.Rotation);
    protected static float Valid(float v,string name) => float.IsFinite(v)?v:throw new ArgumentOutOfRangeException(name);
    /// <summary>Bias of a one-sided stop row. Still short of the stop (speculative), the whole gap may
    /// close in this substep. Already past it, only a Baumgarte share is pushed back: the full depth
    /// in one substep threw a loaded rack off its end stop and it chattered there, ±1 mm at 4 Hz
    /// under a stalled motor (14.10). A row that may only push up (lo = 0) is past the stop when the
    /// error is negative; one that may only push down, when it is positive.</summary>
    protected static float StopBias(float error,float lo,float hi,float dt)
    {
        bool past = lo>=0 ? error<0 : hi<=0 && error>0;
        return past ? .2f*error/dt : error/dt;
    }
    protected static float Nonnegative(float v,string name) => Valid(v,name)>=0?v:throw new ArgumentOutOfRangeException(name);
    /// <summary>A spring of stiffness k and damping c on a row's position error C, integrated
    /// implicitly (backward Euler): the row's bias is Rate·C and its softness Gamma. Any stiffness is
    /// stable at any step; at rest the row holds the static deflection C = −F/k exactly.</summary>
    protected static (float Rate,float Gamma) Soft(float stiffness,float damping,float dt)
    {
        float d=dt*stiffness+damping;
        if(!(d>0)||!float.IsFinite(d))throw new ArgumentOutOfRangeException(nameof(stiffness));
        return (stiffness/d,1/(dt*d));
    }

    /// <summary>Rows that are plain equalities - bilateral, unbounded, the same meaning every sweep - which
    /// PhysicsWorld may solve all at once when <see cref="PhysicsWorld.DirectMechanisms"/> is set.</summary>
    internal virtual void EqualityRows(List<Row> rows) { }
    /// <summary>Rows with bounds - drives with a force cap, stops - that the block solve may take in while
    /// they stay within their bounds.</summary>
    internal virtual void BoundedRows(List<Row> rows) { }

    /// <summary>One Jacobian row. Accumulation prevents friction/motor torque multiplying with iterations.</summary>
    protected internal sealed class Row
    {
        internal RigidBody BodyA=>_a;
        internal RigidBody? BodyB=>_b;
        internal Vector3 LinearA=>_va;
        internal Vector3 AngularA=>_wa;
        internal Vector3 LinearB=>_vb;
        internal Vector3 AngularB=>_wb;
        internal float Softness=>_gamma;
        internal bool Equality=>_mass!=0&&float.IsNegativeInfinity(_lo)&&float.IsPositiveInfinity(_hi);
        internal bool Bounded=>_mass!=0&&_lo<_hi&&!Equality;
        internal float Lower=>_lo;
        internal float Upper=>_hi;
        /// <summary>What the row still asks of the velocities: J·v + bias + γ·λ.</summary>
        internal float Residual()
        {
            float speed=Vector3.Dot(_va,_a.Velocity)+Vector3.Dot(_wa,_a.AngularVelocity);
            if(_b is not null)speed+=Vector3.Dot(_vb,_b.Velocity)+Vector3.Dot(_wb,_b.AngularVelocity);
            return speed+_bias+_gamma*Impulse;
        }
        /// <summary>Adds an impulse found elsewhere - by the block solve - and puts it on the bodies.</summary>
        internal void Add(float j)
        {
            if(j==0||!float.IsFinite(j))return;
            Impulse+=j;
            if(!_a.IsStatic){_a.Velocity+=_va*(j*_a.InvMass);_a.AngularVelocity+=_a.InvInertiaWorld*(_wa*j);}
            if(_b is {IsStatic:false}){_b.Velocity+=_vb*(j*_b.InvMass);_b.AngularVelocity+=_b.InvInertiaWorld*(_wb*j);}
        }
        private RigidBody _a=null!; private RigidBody? _b;
        private Vector3 _va,_wa,_vb,_wb; private float _mass,_bias,_lo,_hi,_gamma;
        public float Impulse { get; private set; }
        /// <summary>Softness 0 is a rigid row; a positive one (from <see cref="Soft"/>) makes it a spring.</summary>
        public void Set(RigidBody a,RigidBody? b,Vector3 va,Vector3 wa,Vector3 vb,Vector3 wb,float bias,float lo=float.NegativeInfinity,float hi=float.PositiveInfinity,float softness=0)
        {
            _a=a;_b=b;_va=va;_wa=wa;_vb=vb;_wb=wb;_bias=bias;_lo=lo;_hi=hi;_gamma=softness;Impulse=0;
            float k=a.InvMass*va.LengthSquared()+Vector3.Dot(wa,a.InvInertiaWorld*wa);
            if(b is not null)k+=b.InvMass*vb.LengthSquared()+Vector3.Dot(wb,b.InvInertiaWorld*wb);
            _mass=float.IsFinite(k)&&k>1e-10f?1/(k+softness):0;
        }
        /// <summary>Moves the bounds of the accumulated impulse - Coulomb friction under a normal
        /// impulse that the other rows are still solving. The next Solve clamps into them.</summary>
        public void Bound(float lo,float hi){_lo=lo;_hi=hi;}
        /// <summary>Warm start: begin the accumulation from an impulse - last substep's, clamped into this
        /// substep's bounds - and put it on the bodies now. Call after <see cref="Set"/>.</summary>
        public void Begin(float impulse)
        {
            if(_mass==0||impulse==0||!float.IsFinite(impulse))return;
            float j=Math.Clamp(impulse,_lo,_hi);Impulse=j;
            if(!_a.IsStatic){_a.Velocity+=_va*(j*_a.InvMass);_a.AngularVelocity+=_a.InvInertiaWorld*(_wa*j);}
            if(_b is {IsStatic:false}){_b.Velocity+=_vb*(j*_b.InvMass);_b.AngularVelocity+=_b.InvInertiaWorld*(_wb*j);}
        }
        public void Solve()
        {
            if(_mass==0)return;
            float speed=Vector3.Dot(_va,_a.Velocity)+Vector3.Dot(_wa,_a.AngularVelocity);
            if(_b is not null)speed+=Vector3.Dot(_vb,_b.Velocity)+Vector3.Dot(_wb,_b.AngularVelocity);
            float next=Math.Clamp(Impulse-(speed+_bias+_gamma*Impulse)*_mass,_lo,_hi),j=next-Impulse;Impulse=next;
            if(!_a.IsStatic){_a.Velocity+=_va*(j*_a.InvMass);_a.AngularVelocity+=_a.InvInertiaWorld*(_wa*j);}
            if(_b is {IsStatic:false}){_b.Velocity+=_vb*(j*_b.InvMass);_b.AngularVelocity+=_b.InvInertiaWorld*(_wb*j);}
        }
    }
}

/// <summary>Revolute joint: three anchor rows, two alignment rows, optional bounded speed drive and angle limits.
/// Angle is continuous rotation of A relative to B; positive follows the right-hand rule of AxisWorld.</summary>
public sealed class HingeConstraint : MechanismConstraint
{
    private readonly Vector3 _anchorA,_anchorB,_axisA,_axisB,_refA,_refB;
    private readonly Row[] _rows=Enumerable.Range(0,7).Select(_=>new Row()).ToArray();
    private float _lastWrapped,_step; private double _angle;
    private float _speed,_torque,_lower=float.NegativeInfinity,_upper=float.PositiveInfinity;
    public float TargetSpeed {get=>_speed;set=>_speed=Valid(value,nameof(TargetSpeed));}
    public float MaxTorque {get=>_torque;set=>_torque=Nonnegative(value,nameof(MaxTorque));}
    public bool MotorEnabled {get;set;}
    /// <summary>After each step move A so its anchor sits exactly on B's. For a part carried round by a
    /// moving B - a planet on its carrier, a disc on its eccentric - the straight position step leaves it
    /// ½·r·(ω·h)² outside its orbit every step, and the anchor rows' 18 % correction then holds it 5.6
    /// times that far out: 4 cm on a 0.51 m arm at 20 rad/s (measured 14.10). Opt-in; positions only.</summary>
    public bool ProjectAnchor {get;set;}
    private float _anchorStiffness=float.PositiveInfinity,_anchorDamping;
    /// <summary>Stiffness of the pin across its axis, N/m: +∞ is a rigid pin; a finite value lets the
    /// two anchors part by F/k under a load F - a flexible or worn joint, solved as an implicit spring
    /// with <see cref="AnchorDamping"/>, N·s/m. The axis alignment stays rigid.</summary>
    public float AnchorStiffness {get=>_anchorStiffness;set=>_anchorStiffness=float.IsPositiveInfinity(value)||(float.IsFinite(value)&&value>0)?value:throw new ArgumentOutOfRangeException(nameof(AnchorStiffness));}
    public float AnchorDamping {get=>_anchorDamping;set=>_anchorDamping=Nonnegative(value,nameof(AnchorDamping));}
    public double Angle => _angle;
    public Vector3 AxisWorld => WorldDirection(B,_axisB);
    public float Speed => Vector3.Dot(A.AngularVelocity-(B?.AngularVelocity??Vector3.Zero),AxisWorld);
    public float MotorTorque => _step>0?_rows[5].Impulse/_step:0;
    /// <summary>Force the joint put on A in the last substep, world frame, N - the bearing load.
    /// B (or the ground) carries the same force reversed. Read-only: the rows are unchanged.</summary>
    public Vector3 ReactionForce => _step>0?(_basis[0]*_rows[0].Impulse+_basis[1]*_rows[1].Impulse+_basis[2]*_rows[2].Impulse)/_step:Vector3.Zero;
    private readonly Vector3[] _basis=[Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ];
    /// <summary>Only the two anchor rows across the axis act; holding the bodies in their plane - along
    /// the axis and against tilting - is left to a <see cref="PlaneLock"/> on each. A planar linkage of
    /// many joints is otherwise held out of its plane many times over: a three-stage scissor lift had
    /// each arm tilted and shifted by three or four joints at once, the sweeps fought over it and the
    /// loaded lift collapsed even at 1000 of them (measured 14.10). Set before the first step.</summary>
    public bool InPlane {get;set;}
    /// <summary>Moment the two alignment rows put on A in the last substep, N·m, perpendicular to the axis.</summary>
    public Vector3 ReactionTorque => _step>0?(_alignP*_rows[3].Impulse+_alignQ*_rows[4].Impulse)/_step:Vector3.Zero;
    /// <summary>Torque the angle stop put on A about the axis in the last substep, N·m (zero off the stops).</summary>
    public float LimitTorque => _step>0?_rows[6].Impulse/_step:0;
    private Vector3 _alignP,_alignQ;
    public Vector3 AnchorA => A.Position+Vector3.Transform(_anchorA,A.Rotation);
    public Vector3 AnchorB => B is null?_anchorB:B.Position+Vector3.Transform(_anchorB,B.Rotation);
    public float AnchorError => Vector3.Distance(AnchorA,AnchorB);
    public void SetLimits(float lower,float upper)
    {
        if(float.IsNaN(lower)||float.IsNaN(upper)||lower>upper)throw new ArgumentOutOfRangeException(nameof(lower));
        _lower=lower;_upper=upper;
    }
    public HingeConstraint(RigidBody a,RigidBody? b,Vector3 anchor,Vector3 axis):base(a,b)
    {
        if(!Finite(anchor))throw new ArgumentException("Finite anchor required.");
        axis=Axis(axis);var reference=Perpendicular(axis);
        _anchorA=Local(a,anchor);_anchorB=Local(b,anchor);_axisA=Direction(a,axis);_axisB=Direction(b,axis);
        _refA=Direction(a,reference);_refB=Direction(b,reference);_lastWrapped=Wrapped();
    }
    private float Wrapped()
    {
        var n=AxisWorld;var x=WorldDirection(B,_refB);var y=WorldDirection(A,_refA);
        return MathF.Atan2(Vector3.Dot(n,Vector3.Cross(x,y)),Vector3.Dot(x,y));
    }
    public override void Finish(float dt)
    {
        if(ProjectAnchor&&!A.IsStatic){A.Position-=AnchorA-AnchorB;A.UpdateDerived();}
        float wrapped=Wrapped();_angle+=MathF.IEEERemainder(wrapped-_lastWrapped,MathF.Tau);_lastWrapped=wrapped;
    }
    private readonly float[] _carried=new float[7];
    public override void Prepare(float dt)
    {
        // every row keeps its meaning between substeps: a stop that has let go is bounded to zero, and
        // the warm start clamps into the bounds
        float ratio=_step>0?dt/_step:0;for(int i=0;i<7;i++)_carried[i]=_rows[i].Impulse*ratio;
        _step=dt;var rA=AnchorA-A.Position;var rB=B is null?Vector3.Zero:AnchorB-B.Position;
        var error=AnchorA-AnchorB;
        var axis=AxisWorld;var p=Perpendicular(axis);var q=Vector3.Cross(axis,p);_alignP=p;_alignQ=q;
        if(InPlane){_basis[0]=p;_basis[1]=q;_basis[2]=axis;}
        int anchors=InPlane?2:3;
        if(float.IsPositiveInfinity(_anchorStiffness))
            for(int i=0;i<anchors;i++){var n=_basis[i];_rows[i].Set(A,B,n,Vector3.Cross(rA,n),-n,-Vector3.Cross(rB,n),.18f/dt*Vector3.Dot(error,n));}
        else
        {
            var (rate,gamma)=Soft(_anchorStiffness,_anchorDamping,dt);
            for(int i=0;i<anchors;i++){var n=_basis[i];_rows[i].Set(A,B,n,Vector3.Cross(rA,n),-n,-Vector3.Cross(rB,n),rate*Vector3.Dot(error,n),softness:gamma);}
        }
        if(InPlane)
        {
            foreach(int i in new[]{2,3,4})_rows[i].Set(A,B,Vector3.Zero,Vector3.Zero,Vector3.Zero,Vector3.Zero,0,0,0);
        }
        else
        {
            var rotationError=Vector3.Cross(axis,WorldDirection(A,_axisA));
            _rows[3].Set(A,B,Vector3.Zero,p,Vector3.Zero,-p,.18f/dt*Vector3.Dot(rotationError,p));
            _rows[4].Set(A,B,Vector3.Zero,q,Vector3.Zero,-q,.18f/dt*Vector3.Dot(rotationError,q));
        }
        float cap=MotorEnabled?MaxTorque*dt:0;
        _rows[5].Set(A,B,Vector3.Zero,axis,Vector3.Zero,-axis,-TargetSpeed,-cap,cap);
        float e=0,lo=0,hi=0;
        // Predictive limits prevent a fast shaft crossing a stop before the next substep.
        if(_angle+Speed*dt<_lower){e=(float)(_angle-_lower);hi=float.PositiveInfinity;}
        else if(_angle+Speed*dt>_upper){e=(float)(_angle-_upper);lo=float.NegativeInfinity;}
        _rows[6].Set(A,B,Vector3.Zero,axis,Vector3.Zero,-axis,StopBias(e,lo,hi,dt),lo,hi);
    }
    internal override void WarmStart(){for(int i=0;i<7;i++)_rows[i].Begin(_carried[i]);}
    internal override void EqualityRows(List<Row> rows){for(int i=0;i<5;i++)rows.Add(_rows[i]);}
    internal override void BoundedRows(List<Row> rows){rows.Add(_rows[5]);rows.Add(_rows[6]);}
    public override void Solve(){foreach(var row in _rows)row.Solve();}
}

/// <summary>Prismatic joint: two transverse rows and three orientation rows leave axial translation free.
/// Optional travel limits and a force-limited velocity drive. Reference pose is captured at construction.</summary>
public sealed class SliderConstraint : MechanismConstraint
{
    private readonly Vector3 _anchorA,_anchorB,_axisB;
    private readonly Quaternion _relative;
    private readonly Row[] _rows=Enumerable.Range(0,7).Select(_=>new Row()).ToArray();
    private float _force,_speed,_lo=float.NegativeInfinity,_hi=float.PositiveInfinity,_step;
    public float MaxForce{get=>_force;set=>_force=Nonnegative(value,nameof(MaxForce));}
    public float TargetSpeed{get=>_speed;set=>_speed=Valid(value,nameof(TargetSpeed));}
    public bool MotorEnabled{get;set;}
    public Vector3 AxisWorld=>WorldDirection(B,_axisB);
    public Vector3 AnchorA=>A.Position+Vector3.Transform(_anchorA,A.Rotation);
    public Vector3 AnchorB=>B is null?_anchorB:B.Position+Vector3.Transform(_anchorB,B.Rotation);
    public float Position=>Vector3.Dot(AnchorA-AnchorB,AxisWorld);
    /// <summary>Derivative of Position, including off-centre anchors and the moving guide axis.</summary>
    public float Speed
    {
        get
        {
            var velocityA=A.Velocity+Vector3.Cross(A.AngularVelocity,AnchorA-A.Position);
            var velocityB=B is null?Vector3.Zero:B.Velocity+Vector3.Cross(B.AngularVelocity,AnchorA-B.Position);
            return Vector3.Dot(velocityA-velocityB,AxisWorld);
        }
    }
    public float TransverseError=>(AnchorA-AnchorB-AxisWorld*Position).Length();
    public float MotorForce=>_step>0?_rows[5].Impulse/_step:0;
    /// <summary>Side force the guide put on A in the last substep, world frame, N (perpendicular to the axis).</summary>
    public Vector3 ReactionForce=>_step>0?(_sideP*_rows[0].Impulse+_sideQ*_rows[1].Impulse)/_step:Vector3.Zero;
    private Vector3 _sideP,_sideQ;
    public SliderConstraint(RigidBody a,RigidBody? b,Vector3 anchor,Vector3 axis):base(a,b)
    {
        if(!Finite(anchor))throw new ArgumentException("Finite anchor required.");
        _anchorA=Local(a,anchor);_anchorB=Local(b,anchor);_axisB=Direction(b,Axis(axis));
        _relative=Quaternion.Conjugate(b?.Rotation??Quaternion.Identity)*a.Rotation;
    }
    public void SetLimits(float lower,float upper){if(float.IsNaN(lower)||float.IsNaN(upper)||lower>upper)throw new ArgumentOutOfRangeException(nameof(lower));_lo=lower;_hi=upper;}
    private readonly float[] _carried=new float[7];
    public override void Prepare(float dt)
    {
        float ratio=_step>0?dt/_step:0;for(int i=0;i<7;i++)_carried[i]=_rows[i].Impulse*ratio;
        _step=dt;var axis=AxisWorld;var p=Perpendicular(axis);var q=Vector3.Cross(axis,p);_sideP=p;_sideQ=q;
        var rA=AnchorA-A.Position;var rB=B is null?Vector3.Zero:AnchorB-B.Position;var d=AnchorA-AnchorB;
        void Linear(Row row,Vector3 n,float bias,float lo=float.NegativeInfinity,float hi=float.PositiveInfinity)
        {
            // B's rotating guide changes its transverse normal; its Jacobian includes the separation d.
            row.Set(A,B,n,Vector3.Cross(rA,n),-n,-Vector3.Cross(rB+d,n),bias,lo,hi);
        }
        Linear(_rows[0],p,.18f/dt*Vector3.Dot(d,p));Linear(_rows[1],q,.18f/dt*Vector3.Dot(d,q));
        var desired=(B?.Rotation??Quaternion.Identity)*_relative;
        var error=A.Rotation*Quaternion.Conjugate(desired);if(error.W<0)error=new(-error.X,-error.Y,-error.Z,-error.W);
        var rv=new Vector3(error.X,error.Y,error.Z)*2;
        Vector3[] axes=[Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ];
        for(int i=0;i<3;i++)_rows[i+2].Set(A,B,Vector3.Zero,axes[i],Vector3.Zero,-axes[i],.18f/dt*Vector3.Dot(rv,axes[i]));
        float cap=MotorEnabled?MaxForce*dt:0;Linear(_rows[5],axis,-TargetSpeed,-cap,cap);
        float v=Speed,e=0,lower=0,upper=0;
        if(Position+v*dt<_lo){e=Position-_lo;upper=float.PositiveInfinity;}
        else if(Position+v*dt>_hi){e=Position-_hi;lower=float.NegativeInfinity;}
        Linear(_rows[6],axis,StopBias(e,lower,upper,dt),lower,upper);
    }
    internal override void WarmStart(){for(int i=0;i<7;i++)_rows[i].Begin(_carried[i]);}
    internal override void EqualityRows(List<Row> rows){for(int i=0;i<5;i++)rows.Add(_rows[i]);}
    internal override void BoundedRows(List<Row> rows){rows.Add(_rows[5]);rows.Add(_rows[6]);}
    public override void Solve(){foreach(var row in _rows)row.Solve();}
}

/// <summary>Ideal external gear coupling between two ground-supported shafts.
/// thetaA + ratio*thetaB = phase; work-conjugate impulses transfer load both ways.
/// Tooth shape is visual; this is a bilateral gear constraint, not tooth contact/FEM.</summary>
public sealed class GearConstraint : MechanismConstraint
{
    public HingeConstraint First {get;}
    public HingeConstraint Second {get;}
    public float Ratio{get;}
    public double Phase{get;}
    private readonly Row _row=new();private float _step;
    public double Error=>First.Angle+Ratio*Second.Angle-Phase;
    public float Torque=>_step>0?_row.Impulse/_step:0;
    public GearConstraint(HingeConstraint first,HingeConstraint second,float ratio):base(first.A,second.A)
    {
        if(first.B is not null||second.B is not null)throw new ArgumentException("This gear coupling requires ground-supported shafts.");
        if(!float.IsFinite(ratio)||Math.Abs(ratio)<1e-5f)throw new ArgumentOutOfRangeException(nameof(ratio));
        First=first;Second=second;Ratio=ratio;Phase=first.Angle+ratio*second.Angle;
    }
    public override void Prepare(float dt){_step=dt;_row.Set(A,B,Vector3.Zero,First.AxisWorld,Vector3.Zero,Second.AxisWorld*Ratio,.12f/dt*(float)Error);}
    public override void Solve()=>_row.Solve();
}

/// <summary>Torque-limited Coulomb shaft brake; total normal force includes all friction faces.
/// Accumulated impulse stops at zero relative speed instead of reversing the rotor.</summary>
public sealed class ShaftBrake : MechanismConstraint
{
    public HingeConstraint Hinge=>_hinge;
    private readonly HingeConstraint _hinge;private readonly Row _row=new();private float _torque,_step,_before;
    public float MaxTorque{get=>_torque;set=>_torque=Nonnegative(value,nameof(MaxTorque));}
    public float Torque=>_step>0?_row.Impulse/_step:0;
    public double DissipatedEnergy{get;private set;}
    public ShaftBrake(HingeConstraint hinge):base(hinge.A,hinge.B){_hinge=hinge;}
    public override void Prepare(float dt){_step=dt;_before=_hinge.Speed;var n=_hinge.AxisWorld;_row.Set(A,B,Vector3.Zero,n,Vector3.Zero,-n,0,-MaxTorque*dt,MaxTorque*dt);}
    public override void Solve()=>_row.Solve();
    public override void Finish(float dt){DissipatedEnergy+=Math.Max(0,-_row.Impulse*.5*(_before+_hinge.Speed));}
}
