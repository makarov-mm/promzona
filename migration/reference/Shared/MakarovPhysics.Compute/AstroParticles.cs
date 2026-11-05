using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>Independent astronomical tracers, evaluated on the selected device.
/// Modes 0–4 and 6 evaluate prescribed fields; mode 5 integrates restricted
/// two-centre gravity. No mode introduces particle self-gravity or gas dynamics.
/// Input/output are separate checkpoints: a failed dispatch is replayed on CPU.</summary>
public sealed class AstroParticles : IDisposable
{
    public const int MaxCount = 1048576, Stride = 8, ParameterCount = 416, TraceFloats = 192;
    public float[] Input { get; private set; } = [];
    public float[] Output { get; private set; } = [];
    public float[] Parameters { get; } = new float[ParameterCount];
    public int Count { get; private set; }
    public ComputeBackend ResolvedBackend { get; private set; } = ComputeBackend.Cpu;
    public double Milliseconds { get; private set; }
    public bool DeviceFailed { get; private set; }
    public string BackendStatus => ComputeBackends.Describe(ResolvedBackend);
    private ComputeBackend _backend = ComputeBackend.Auto;
    private Device? _device;
    private bool _attempted;
    public ComputeBackend Backend
    {
        get => _backend;
        set { if(value == _backend) return; Dispose(); _backend=value; DeviceFailed=false; }
    }
    public void Resize(int count)
    {
        if(count < 1 || count > MaxCount) throw new ArgumentOutOfRangeException(nameof(count));
        if(count == Count) return;
        Dispose(); Count=count; Input=new float[count*Stride]; Output=new float[count*Stride+TraceFloats];
        DeviceFailed=false;
    }
    public void Evaluate(int mode)
    {
        if(Count < 1 || mode < 0 || mode > 6) throw new ArgumentOutOfRangeException(nameof(mode));
        if(mode == 5 && (Parameters[2] < 1 || Parameters[2] > 64 || Parameters[2] != (int)Parameters[2]
            || Parameters[4] < 0 || Parameters[4] >= Count || Parameters[4] != (int)Parameters[4]))
            throw new ArgumentOutOfRangeException(nameof(Parameters));
        foreach(float p in Parameters) if(!float.IsFinite(p)) throw new ArgumentException("Non-finite parameter");
        long start=Stopwatch.GetTimestamp();
        if(!_attempted)
        {
            _attempted=true;
            var choices=_backend==ComputeBackend.Auto
                ? new[]{ComputeBackend.Cuda,ComputeBackend.OpenCl,ComputeBackend.Vulkan}
                : new[]{_backend};
            foreach(var choice in choices)
            {
                _device=Device.Create(choice,Count);
                if(_device is not null) { ResolvedBackend=choice; break; }
            }
            if(_backend!=ComputeBackend.Cpu && _backend!=ComputeBackend.Auto && _device is null) DeviceFailed=true;
        }
        bool done=_device is not null && _device.Evaluate(Count,mode,Input,Parameters,Output);
        if(done)
            for(int i=0;i<Count*Stride+(mode==5?(int)Parameters[2]*3:0);i++) if(!float.IsFinite(Output[i])) {done=false;break;}
        if(!done)
        {
            if(_device is not null) { _device.Dispose();_device=null;DeviceFailed=true; }
            ResolvedBackend=ComputeBackend.Cpu;
            EvaluateCpu(Count,mode,Input,Parameters,Output);
        }
        Milliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    /// <summary>Float reference used for parity and recovery; never aliases the input.</summary>
    public static void EvaluateCpu(int count,int mode,float[] input,float[] parameters,float[] output)
    {
        if(count<1 || count>MaxCount || mode<0 || mode>6 || input.Length<count*Stride || output.Length<count*Stride+TraceFloats
            || parameters.Length<ParameterCount || ReferenceEquals(input,output)) throw new ArgumentException("Invalid particle buffers");
        if(mode==5 && (parameters[2]<1 || parameters[2]>64 || parameters[2]!=(int)parameters[2]
            || parameters[4]<0 || parameters[4]>=count || parameters[4]!=(int)parameters[4])) throw new ArgumentException("Invalid step count");
        if(count<4096) for(int i=0;i<count;i++) AstroParticleReference.One(i,count,mode,input,parameters,output);
        else Parallel.For(0,count,i=>AstroParticleReference.One(i,count,mode,input,parameters,output));
    }
    public void Dispose()
    { _device?.Dispose();_device=null;_attempted=false;ResolvedBackend=ComputeBackend.Cpu; }

    private abstract class Device : IDisposable
    {
        internal static Device? Create(ComputeBackend backend,int count)
        {
            try { return backend switch {
                ComputeBackend.Cuda when CudaNBody.Available => Native.Create(false,count),
                ComputeBackend.OpenCl when OpenCl.Available => Cl.Create(count),
                ComputeBackend.Vulkan when VulkanCompute.Available => Native.Create(true,count),
                _=>null }; } catch { return null; }
        }
        internal abstract bool Evaluate(int n,int mode,float[] data,float[] p,float[] result);
        public abstract void Dispose();
    }
    private sealed class Cl : Device
    {
        private IntPtr _program,_kernel,_input,_params,_output;
        internal static Cl? Create(int count)
        {
            var d=new Cl();
            try {
                d._program=OpenCl.BuildProgram(AstroParticleProgram.Source,out _);
                if(d._program==IntPtr.Zero) {d.Dispose();return null;}
                d._kernel=OpenCl.Kernel(d._program,"astro");
                d._input=OpenCl.Buffer(count*32L);d._output=OpenCl.Buffer(count*32L+TraceFloats*4L);d._params=OpenCl.Buffer(ParameterCount*4L);
                if(d._kernel==IntPtr.Zero || d._input==IntPtr.Zero || d._output==IntPtr.Zero || d._params==IntPtr.Zero)
                    {d.Dispose();return null;}
                return d;
            } catch {d.Dispose();return null;}
        }
        internal override bool Evaluate(int n,int mode,float[] data,float[] p,float[] result)
        {
            try {return OpenCl.Write(_input,data,n*Stride) && OpenCl.Write(_params,p,ParameterCount)
                && OpenCl.Arg(_kernel,0,n) && OpenCl.Arg(_kernel,1,mode)
                && OpenCl.Arg(_kernel,2,_input) && OpenCl.Arg(_kernel,3,_params) && OpenCl.Arg(_kernel,4,_output)
                && OpenCl.Launch(_kernel,n) && OpenCl.Read(_output,result,n*Stride+(mode==5?(int)p[2]*3:0));
            } catch {return false;}
        }
        public override void Dispose()
        {
            OpenCl.Release(_input);OpenCl.Release(_params);OpenCl.Release(_output);
            OpenCl.ReleaseKernel(_kernel);OpenCl.ReleaseProgram(_program);
            _input=_params=_output=_kernel=_program=IntPtr.Zero;
        }
    }
    private sealed class Native : Device
    {
        [DllImport("MakarovPhysics.Cuda.dll", EntryPoint="MpCudaAstroCreate", CallingConvention=CallingConvention.Cdecl)] private static extern int MpCudaAstroCreate(int capacity,out IntPtr handle);
        [DllImport("MakarovPhysics.Cuda.dll", EntryPoint="MpCudaAstroEvaluate", CallingConvention=CallingConvention.Cdecl)] private static extern int MpCudaAstroEvaluate(IntPtr handle,int n,int mode,float[] data,float[] p,[Out] float[] result);
        [DllImport("MakarovPhysics.Cuda.dll", EntryPoint="MpCudaAstroDestroy", CallingConvention=CallingConvention.Cdecl)] private static extern int MpCudaAstroDestroy(IntPtr handle);
        [DllImport("MakarovPhysics.VkRay.dll", EntryPoint="MpVkAstroCreate", CallingConvention=CallingConvention.Cdecl)] private static extern int MpVkAstroCreate(int capacity,out IntPtr handle);
        [DllImport("MakarovPhysics.VkRay.dll", EntryPoint="MpVkAstroEvaluate", CallingConvention=CallingConvention.Cdecl)] private static extern int MpVkAstroEvaluate(IntPtr handle,int n,int mode,float[] data,float[] p,[Out] float[] result);
        [DllImport("MakarovPhysics.VkRay.dll", EntryPoint="MpVkAstroDestroy", CallingConvention=CallingConvention.Cdecl)] private static extern int MpVkAstroDestroy(IntPtr handle);
        private IntPtr _handle;
        private bool _vulkan;
        internal static Native? Create(bool vulkan,int n)
        {
            int rc=vulkan?MpVkAstroCreate(n,out var h):MpCudaAstroCreate(n,out h);
            return rc==0 && h!=IntPtr.Zero ? new Native{_handle=h,_vulkan=vulkan} : null;
        }
        internal override bool Evaluate(int n,int mode,float[] data,float[] p,float[] result)
        {
            try {return (_vulkan?MpVkAstroEvaluate(_handle,n,mode,data,p,result):MpCudaAstroEvaluate(_handle,n,mode,data,p,result))==0;}
            catch {return false;}
        }
        public override void Dispose()
        {
            if(_handle==IntPtr.Zero)return;
            try {if(_vulkan)MpVkAstroDestroy(_handle);else MpCudaAstroDestroy(_handle);}catch { }
            _handle=IntPtr.Zero;
        }
    }
}
