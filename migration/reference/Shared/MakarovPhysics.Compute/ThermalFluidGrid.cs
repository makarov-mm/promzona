namespace MakarovPhysics.Compute;
/// <summary>Bounded Boussinesq teaching solver: conservative wax transport, subcycled explicit viscosity,
/// pressure projection and thermal buoyancy. Lava uses a transported wax fraction and approximate CSF surface tension.
/// Smoke is a low-Mach exterior plume, not a blast-wave / nuclear-physics calculation.</summary>
public sealed class ThermalFluidGrid : IDisposable
{
 public int NX {get;} public int NY {get;} public int NZ {get;}
 public float Cell {get;} public bool Lava {get;}
 public ThermalGridDispatch Dispatch {get;}=new();
 public float[] Data=>Dispatch.Input;
 public double Time {get;private set;}
 public double InitialWaxVolume {get;private set;}
 public double VolumeCorrection {get;private set;}
 public float Heater=1,Cooling=1,Viscosity=1,Wind=.15f,Buoyancy=1;
 public int PressureIterations=40;
 public long Revision {get;private set;}
 public double Milliseconds {get;private set;}
 public ThermalFluidGrid(bool lava,int nx=24,int ny=52,int nz=24)
 {Lava=lava;NX=nx;NY=ny;NZ=nz;if(nx<6||ny<6||nz<6||(long)nx*ny*nz>524288)throw new ArgumentOutOfRangeException(nameof(nx));Cell=lava?.15f/(nx-2):.45f;Dispatch.Resize(nx*ny*nz);Reset();}
 public bool Solid(int x,int y,int z)
 {float xx=x-(NX-1)*.5f,zz=z-(NZ-1)*.5f,r=(NX-2)*.5f;return x==0||x==NX-1||z==0||z==NZ-1||y==0||(Lava&&(y==NY-1||xx*xx+zz*zz>r*r));}
 public int Index(int x,int y,int z)=>(x+NX*(y+NY*z))*8;
 public void Reset()
 {
  Array.Clear(Data);Time=VolumeCorrection=0;Revision++;
  for(int z=0;z<NZ;z++)for(int y=0;y<NY;y++)for(int x=0;x<NX;x++){
   int k=Index(x,y,z);float xx=(x-(NX-1)*.5f)*Cell,yy=y*Cell,zz=(z-(NZ-1)*.5f)*Cell;
   if(Lava){
    Data[k+3]=20+45*MathF.Exp(-yy/.12f);
    if(Solid(x,y,z))continue;
    // Separated preheated wax lobes above a pool. Geometry is an initial condition, never a prescribed path.
    float pool=.019f-yy;
    for(int j=0;j<4;j++){float a=j*2.39996f,cx=.026f*MathF.Cos(a),cz=.026f*MathF.Sin(a),cy=.045f+j*.063f;
     float r=.018f+(j%2)*.003f,d=MathF.Sqrt((xx-cx)*(xx-cx)+(yy-cy)*(yy-cy)+(zz-cz)*(zz-cz));pool=Math.Max(pool,r-d);}
    Data[k+4]=Math.Clamp(.5f+pool/(Cell*1.5f),0,1);
    Data[k+3]+=Data[k+4]*6;
   }else{
    if(Solid(x,y,z))continue;
    // Post-expansion thermal: a wide cap and an entrained dusty stem. This is an initial scalar field,
    // not a calculated blast or a mushroom-shaped force applied during the simulation.
    float rr=MathF.Sqrt(xx*xx+zz*zz),cy=4.5f;
    float cap=MathF.Sqrt(rr*rr/(3.45f*3.45f)+(yy-cy)*(yy-cy)/(1.55f*1.55f));
    float q=Math.Clamp((1-cap)*3,0,1),stem=MathF.Exp(-rr*rr/.48f)*Math.Clamp((cy-yy)/cy,0,1);
    Data[k+4]=Math.Clamp(.8f*q+.30f*stem,0,1);Data[k+3]=q*.75f+stem*.18f;
    float ring=MathF.Exp(-((rr-2.45f)*(rr-2.45f)+(yy-cy)*(yy-cy))/3.5f);
    float radial=2*1.1f*rr*(yy-cy)/3.5f*ring;
    Data[k]=radial*xx/Math.Max(.001f,rr);Data[k+2]=radial*zz/Math.Max(.001f,rr);
    Data[k+1]=2.2f*ring-2.2f*rr*(rr-2.45f)/3.5f*ring;

   }
  }
  InitialWaxVolume=PhaseVolume;
 }
 public double PhaseVolume {get {double s=0;for(int i=4;i<NX*NY*NZ*8;i+=8)s+=Data[i];return s*Cell*Cell*Cell;}}
 public double MeanTemperature {get {double sum=0,weight=0;for(int i=0;i<NX*NY*NZ;i++){float c=Data[i*8+4];sum+=c*Data[i*8+3];weight+=c;}return sum/Math.Max(weight,1e-20);}}
 public double Height {get {double sum=0,weight=0;for(int z=0;z<NZ;z++)for(int y=1;y<NY;y++)for(int x=0;x<NX;x++){float c=Data[Index(x,y,z)+4];sum+=c*y*Cell;weight+=c;}return sum/Math.Max(weight,1e-20);}}
 public void Step(double dt)
 {
  if(!double.IsFinite(dt)||dt<=0||dt>.2)return;
  Milliseconds=0;double limit=Lava?.025:.04;
  if(Lava){float speed=0;for(int i=0;i<NX*NY*NZ;i++)speed=Math.Max(speed,Math.Abs(Data[i*8])+Math.Abs(Data[i*8+1])+Math.Abs(Data[i*8+2]));limit=Math.Min(limit,.45*Cell/Math.Max(.001,speed));}
  int steps=(int)Math.Ceiling(dt/limit);float h=(float)(dt/steps);
  for(int s=0;s<steps;s++){
   var p=Dispatch.Parameters;p[0]=NX;p[1]=NY;p[2]=NZ;p[3]=Cell;p[4]=h;p[5]=Lava?1:0;
   p[6]=Lava?.00045f*Math.Clamp(Viscosity,.2f,4):.015f;
   p[7]=Lava?.000001f:.018f;p[8]=Lava?.06f:1.8f;
   p[9]=Lava?Math.Clamp(Heater,0,1.5f):1.5f*Math.Clamp(Buoyancy,0,3);
   p[10]=Lava?20+52*Math.Clamp(Heater,0,1.5f):Wind;p[11]=Lava?Math.Clamp(Cooling,0,3):.045f*Math.Clamp(Cooling,.1f,3);
   var modes=new List<int>{0};if(!Lava)modes.Add(5);modes.Add(1);
   if(Lava){int diffuse=(int)Math.Ceiling(6*p[6]*h/(.8f*Cell*Cell));p[12]=h/Math.Max(1,diffuse);for(int n=0;n<diffuse;n++)modes.Add(6);}
   modes.Add(2);for(int n=0;n<Math.Clamp(PressureIterations,1,160);n++)modes.Add(3);modes.Add(4);
   Dispatch.EvaluateBatch(modes.ToArray());Milliseconds+=Dispatch.Milliseconds;Dispatch.Commit();
   if(Lava)ConservePhase();Time+=h;Revision++;
  }
 }
 void ConservePhase()
 {
  // Collocated face interpolation and boundary clipping leave small phase-volume drift. Report and correct it,
  // redistributing only in interface cells; this does not pretend to be a geometric VOF method.
  double target=InitialWaxVolume/(Cell*Cell*Cell),current=0,capacity=0;
  for(int i=4;i<NX*NY*NZ*8;i+=8){current+=Data[i];capacity+=Data[i]*(1-Data[i]);}
  VolumeCorrection+=(target-current)*Cell*Cell*Cell;
  for(int iter=0;iter<4&&capacity>1e-12;iter++){
   double factor=(target-current)/capacity;current=capacity=0;
   for(int i=4;i<NX*NY*NZ*8;i+=8){float c=Data[i];c=(float)Math.Clamp(c+factor*c*(1-c),0,1);Data[i]=c;current+=c;capacity+=c*(1-c);}
  }
 }
 public void Dispose()=>Dispatch.Dispose();
}
