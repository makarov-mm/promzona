using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>Checkpointed stencil dispatch for the shared thermal-fluid grid. Identical CPU/CUDA/OpenCL/Vulkan equations; failed dispatch replays its unchanged input on CPU.</summary>
public sealed class ThermalGridDispatch : IDisposable
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
    private float[] _scratch = [];
    public ComputeBackend Backend
    {
        get => _backend;
        set { if(value == _backend) return; Dispose(); _backend=value; DeviceFailed=false; }
    }
    public void Resize(int count)
    {
        if(count < 1 || count > MaxCount) throw new ArgumentOutOfRangeException(nameof(count));
        if(count == Count) return;
        Dispose(); Count=count; Input=new float[count*Stride+TraceFloats]; Output=new float[count*Stride+TraceFloats];
        DeviceFailed=false; _scratch=new float[count*Stride+TraceFloats];
    }
    public void Commit() { (Input,Output)=(Output,Input); }
    public void Evaluate(int mode) => EvaluateBatch([mode]);
    /// <summary>Uploads one checkpoint, executes all stencil passes on the device, then downloads once.
    /// Input is preserved until Commit, including when the complete batch must be replayed on CPU.</summary>
    public void EvaluateBatch(int[] modes)
    {
        if(Count < 1 || modes is null || modes.Length < 1 || modes.Length > 2048) throw new ArgumentException("Invalid stencil batch");
        foreach(int mode in modes) if(mode < 0 || mode > 7) throw new ArgumentOutOfRangeException(nameof(modes));
        if((int)Parameters[0]*(int)Parameters[1]*(int)Parameters[2]!=Count || Parameters[0]<4 || Parameters[1]<4 || (Parameters[2]<4 && !(Parameters[2]==1 && modes.All(m=>m==7)))) throw new ArgumentException("Invalid grid dimensions");
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
        bool done=_device is not null && _device.EvaluateBatch(Count,modes,Input,Parameters,Output);
        if(done)
            for(int i=0;i<Count*Stride;i++) if(!float.IsFinite(Output[i])) {done=false;break;}
        if(!done)
        {
            if(_device is not null) { _device.Dispose();_device=null;DeviceFailed=true; }
            ResolvedBackend=ComputeBackend.Cpu;
            float[] source=Input, destination=Output;
            foreach(int mode in modes) {
                EvaluateCpu(Count,mode,source,Parameters,destination);
                source=destination; destination=ReferenceEquals(destination,Output)?_scratch:Output;
            }
            if(!ReferenceEquals(source,Output)) Array.Copy(source,Output,Count*Stride);
        }
        Milliseconds=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    /// <summary>Float reference used for parity and recovery; never aliases the input.</summary>
    public static void EvaluateCpu(int count,int mode,float[] input,float[] parameters,float[] output)
    {
        if(count<1 || count>MaxCount || mode<0 || mode>7 || input.Length<count*Stride || output.Length<count*Stride+TraceFloats
            || parameters.Length<ParameterCount || ReferenceEquals(input,output)) throw new ArgumentException("Invalid particle buffers");
        if(parameters[0]<4 || parameters[1]<4 || (parameters[2]<4 && !(mode==7&&parameters[2]==1)) || (long)parameters[0]*(long)parameters[1]*(long)parameters[2]!=count || parameters[3]<=0 || parameters[4]<0) throw new ArgumentException("Invalid grid parameters");
        foreach(float p in parameters) if(!float.IsFinite(p)) throw new ArgumentException("Non-finite parameter");
        if(count<4096) for(int i=0;i<count;i++) ThermalGridReference.One(i,count,mode,input,parameters,output);
        else Parallel.For(0,count,i=>ThermalGridReference.One(i,count,mode,input,parameters,output));
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
        internal abstract bool EvaluateBatch(int n,int[] modes,float[] data,float[] p,float[] result);
        public abstract void Dispose();
    }
    private sealed class Cl : Device
    {
        private IntPtr _program,_kernel,_input,_params,_output;
        internal static Cl? Create(int count)
        {
            var d=new Cl();
            try {
                d._program=OpenCl.BuildProgram(ThermalGridProgram.Source,out _);
                if(d._program==IntPtr.Zero) {d.Dispose();return null;}
                d._kernel=OpenCl.Kernel(d._program,"thermal");
                d._input=OpenCl.Buffer(count*32L);d._output=OpenCl.Buffer(count*32L+TraceFloats*4L);d._params=OpenCl.Buffer(ParameterCount*4L);
                if(d._kernel==IntPtr.Zero || d._input==IntPtr.Zero || d._output==IntPtr.Zero || d._params==IntPtr.Zero)
                    {d.Dispose();return null;}
                return d;
            } catch {d.Dispose();return null;}
        }
        internal override bool EvaluateBatch(int n,int[] modes,float[] data,float[] p,float[] result)
        {
            try {
                if(!OpenCl.Write(_input,data,n*Stride) || !OpenCl.Write(_params,p,ParameterCount)
                    || !OpenCl.Arg(_kernel,0,n) || !OpenCl.Arg(_kernel,3,_params)) return false;
                IntPtr source=_input,destination=_output;
                foreach(int mode in modes) {
                    if(!OpenCl.Arg(_kernel,1,mode) || !OpenCl.Arg(_kernel,2,source) || !OpenCl.Arg(_kernel,4,destination)
                        || !OpenCl.Launch(_kernel,n)) return false;
                    (source,destination)=(destination,source);
                }
                // The shared OpenCL queue is in order. Read waits for the complete batch.
                return OpenCl.Read(source,result,n*Stride);
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
        [DllImport("MakarovPhysics.Cuda.dll", EntryPoint="MpCudaThermalCreate", CallingConvention=CallingConvention.Cdecl)] private static extern int MpCudaThermalCreate(int capacity,out IntPtr handle);
        [DllImport("MakarovPhysics.Cuda.dll", EntryPoint="MpCudaThermalEvaluateBatch", CallingConvention=CallingConvention.Cdecl)] private static extern int MpCudaThermalEvaluateBatch(IntPtr handle,int n,int count,int[] modes,float[] data,float[] p,[Out] float[] result);
        [DllImport("MakarovPhysics.Cuda.dll", EntryPoint="MpCudaThermalDestroy", CallingConvention=CallingConvention.Cdecl)] private static extern int MpCudaThermalDestroy(IntPtr handle);
        [DllImport("MakarovPhysics.VkRay.dll", EntryPoint="MpVkThermalCreate", CallingConvention=CallingConvention.Cdecl)] private static extern int MpVkThermalCreate(int capacity,out IntPtr handle);
        [DllImport("MakarovPhysics.VkRay.dll", EntryPoint="MpVkThermalEvaluateBatch", CallingConvention=CallingConvention.Cdecl)] private static extern int MpVkThermalEvaluateBatch(IntPtr handle,int n,int count,int[] modes,float[] data,float[] p,[Out] float[] result);
        [DllImport("MakarovPhysics.VkRay.dll", EntryPoint="MpVkThermalDestroy", CallingConvention=CallingConvention.Cdecl)] private static extern int MpVkThermalDestroy(IntPtr handle);
        private IntPtr _handle;
        private bool _vulkan;
        internal static Native? Create(bool vulkan,int n)
        {
            int rc=vulkan?MpVkThermalCreate(n,out var h):MpCudaThermalCreate(n,out h);
            return rc==0 && h!=IntPtr.Zero ? new Native{_handle=h,_vulkan=vulkan} : null;
        }
        internal override bool EvaluateBatch(int n,int[] modes,float[] data,float[] p,float[] result)
        {
            try {return (_vulkan?MpVkThermalEvaluateBatch(_handle,n,modes.Length,modes,data,p,result):MpCudaThermalEvaluateBatch(_handle,n,modes.Length,modes,data,p,result))==0;}
            catch {return false;}
        }
        public override void Dispose()
        {
            if(_handle==IntPtr.Zero)return;
            try {if(_vulkan)MpVkThermalDestroy(_handle);else MpCudaThermalDestroy(_handle);}catch { }
            _handle=IntPtr.Zero;
        }
    }
}
