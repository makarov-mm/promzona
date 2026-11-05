using System.Runtime.InteropServices;
using System.Text;

namespace MakarovPhysics.Compute;

/// <summary>
/// The OpenCL host, talked to DIRECTLY over P/Invoke into the Khronos ICD
/// loader (OpenCL.dll on Windows - present whenever any vendor GPU driver
/// is; libOpenCL.so elsewhere). No native project, no build step: kernels
/// are OpenCL C strings compiled by the driver at run time.
///
/// Runtime compilation is also the new failure class this backend brings:
/// a driver can miscompile a kernel and produce numbers instead of errors.
/// The probe therefore ends with a SMOKE TEST - a trivial kernel whose
/// exact fp32 result is known - and a driver that fails it makes the whole
/// backend report unavailable with the reason, rather than running physics
/// on a compiler nobody has vetted. Never-throw contract as everywhere in
/// this library: every public member returns false/null on failure.
/// </summary>
public static class OpenCl
{
    private const string Lib = "OpenCL";

    [DllImport(Lib)] private static extern int clGetPlatformIDs(uint entries, IntPtr[]? platforms, out uint count);
    [DllImport(Lib)] private static extern int clGetDeviceIDs(IntPtr platform, ulong type, uint entries, IntPtr[]? devices, out uint count);
    [DllImport(Lib)] private static extern int clGetDeviceInfo(IntPtr device, uint param, UIntPtr size, byte[]? value, out UIntPtr returned);
    [DllImport(Lib)] private static extern IntPtr clCreateContext(IntPtr props, uint numDevices, IntPtr[] devices, IntPtr callback, IntPtr userData, out int err);
    [DllImport(Lib)] private static extern IntPtr clCreateCommandQueue(IntPtr context, IntPtr device, ulong props, out int err);
    [DllImport(Lib)] private static extern IntPtr clCreateBuffer(IntPtr context, ulong flags, UIntPtr size, IntPtr host, out int err);
    [DllImport(Lib, CharSet = CharSet.Ansi)] private static extern IntPtr clCreateProgramWithSource(IntPtr context, uint count, string[] strings, UIntPtr[]? lengths, out int err);
    [DllImport(Lib, CharSet = CharSet.Ansi)] private static extern int clBuildProgram(IntPtr program, uint numDevices, IntPtr[] devices, string? options, IntPtr callback, IntPtr userData);
    [DllImport(Lib)] private static extern int clGetProgramBuildInfo(IntPtr program, IntPtr device, uint param, UIntPtr size, byte[]? value, out UIntPtr returned);
    [DllImport(Lib, CharSet = CharSet.Ansi)] private static extern IntPtr clCreateKernel(IntPtr program, string name, out int err);
    [DllImport(Lib)] private static extern int clSetKernelArg(IntPtr kernel, uint index, UIntPtr size, ref IntPtr value);
    [DllImport(Lib)] private static extern int clSetKernelArg(IntPtr kernel, uint index, UIntPtr size, ref int value);
    [DllImport(Lib)] private static extern int clSetKernelArg(IntPtr kernel, uint index, UIntPtr size, ref float value);
    [DllImport(Lib)] private static extern int clEnqueueNDRangeKernel(IntPtr queue, IntPtr kernel, uint dims, UIntPtr[]? offset, UIntPtr[] global, UIntPtr[]? local, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, float[] target, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, byte[] target, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, float[] source, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, byte[] source, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, sbyte[] source, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, sbyte[] target, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, ulong[] source, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, int[] target, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, ulong[] target, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, int blocking, UIntPtr offset, UIntPtr size, int[] source, uint waitCount, IntPtr[]? waitList, IntPtr ev);
    [DllImport(Lib)] private static extern int clFinish(IntPtr queue);
    [DllImport(Lib)] private static extern int clReleaseMemObject(IntPtr mem);
    [DllImport(Lib)] private static extern int clReleaseKernel(IntPtr kernel);
    [DllImport(Lib)] private static extern int clReleaseProgram(IntPtr program);

    private const ulong DeviceTypeGpu = 1UL << 2;      // CL_DEVICE_TYPE_GPU
    private const ulong DeviceTypeAll = 0xFFFFFFFFUL;  // CL_DEVICE_TYPE_ALL
    private const uint DeviceNameParam = 0x102B;       // CL_DEVICE_NAME
    private const uint ComputeUnitsParam = 0x1002;     // CL_DEVICE_MAX_COMPUTE_UNITS
    private const uint ClockParam = 0x100C;            // CL_DEVICE_MAX_CLOCK_FREQUENCY (MHz)
    private const uint GlobalMemParam = 0x101F;        // CL_DEVICE_GLOBAL_MEM_SIZE (bytes)
    private const uint MaxWorkGroupParam = 0x1004;     // CL_DEVICE_MAX_WORK_GROUP_SIZE (size_t)
    private const uint BuildLogParam = 0x1183;         // CL_PROGRAM_BUILD_LOG
    private const ulong MemReadWrite = 1UL << 0;       // CL_MEM_READ_WRITE

    // ---- the one shared context ----

    private static readonly object _gate = new();
    private static bool? _available;
    private static string _deviceName = string.Empty;
    private static int _maxWorkGroupSize;
    private static IntPtr _device, _context, _queue;

    /// <summary>The reason <see cref="Available"/> is false, for the UI to
    /// show as a tooltip instead of a silently grey radio button.</summary>
    /// <summary>Why the PROBE said no - a driver that would not enumerate, a
    /// kernel that would not compile, a queue the runtime refused. It says
    /// nothing about <see cref="SoftDisable"/>, which is a test switch and not
    /// a property of the machine; use <see cref="DisabledReason"/> for that.</summary>
    public static string UnavailableReason { get; private set; } = string.Empty;

    /// <summary>Why the backend is unavailable RIGHT NOW, probe or switch,
    /// which is the question a panel is actually asking.</summary>
    public static string DisabledReason => SoftDisable
        ? "switched off in software (a device test is comparing backends)"
        : UnavailableReason;

    /// <summary>SelfTest's switch, same contract as CudaNBody.SoftDisable:
    /// the harness pins every model to the CPU so field-reading tests see a
    /// current host state, and the device tests re-enable locally.</summary>
    public static bool SoftDisable { get; set; }

    /// <summary>The OpenCL device name once <see cref="Available"/> is true.</summary>
    public static string DeviceName => _deviceName;

    /// <summary>Largest work-group the device will accept, 0 before the probe
    /// runs. A kernel that shares data through __local memory has to be
    /// launched with an EXPLICIT work-group size - the driver's own choice is
    /// not knowable in advance and a barrier inside a loop whose trip count
    /// depends on it cannot be written against a guess. The spec guarantees
    /// only 1, so the caller takes the minimum of this and what its local
    /// arrays hold.</summary>
    public static int MaxWorkGroupSize => _maxWorkGroupSize;

    /// <summary>GPU-only by design: an OpenCL CPU device would race our own
    /// SIMD CPU path and lose, muddying what Auto means. The one exception
    /// is MP_OPENCL_ALLOW_CPU=1 - a validation hook for the container CI,
    /// where PoCL's CPU device is the only OpenCL there is and the kernels
    /// still deserve a real compile-and-run against the reference.</summary>
    public static bool Available
    {
        get
        {
            lock (_gate)
            {
                if (SoftDisable)
                {
                    // NOT written into UnavailableReason: that field belongs to
                    // the probe, and overwriting it left the object saying
                    // "switched off in software" long after the switch went
                    // back (external analysis, 23.08 - Available == true with a
                    // reason still set is a state that cannot be read).
                    return false;
                }
                if (_available is bool cached) return cached;
                _available = Probe();
                return _available.Value;
            }
        }
    }

    /// <summary>How the chosen device won: its score, or the environment
    /// variable that forced it. Printed beside the device name in the panel,
    /// so a machine that picked the wrong card says why it thought so.</summary>
    public static string SelectionNote => _selectionNote;
    private static string _selectionNote = string.Empty;

    private static string ReadString(IntPtr device, uint param)
    {
        byte[] buffer = new byte[256];
        if (clGetDeviceInfo(device, param, (UIntPtr)buffer.Length, buffer, out UIntPtr written) != 0)
            return string.Empty;
        int len = (int)written;
        while (len > 0 && buffer[len - 1] == 0) len--;
        return Encoding.UTF8.GetString(buffer, 0, len).Trim();
    }

    private static uint ReadUInt(IntPtr device, uint param)
    {
        byte[] buffer = new byte[4];

        return clGetDeviceInfo(device, param, (UIntPtr)buffer.Length, buffer, out _) == 0
            ? BitConverter.ToUInt32(buffer, 0) 
            : 0u;
    }

    private static ulong ReadULong(IntPtr device, uint param)
    {
        byte[] buffer = new byte[8];
        return clGetDeviceInfo(device, param, (UIntPtr)buffer.Length, buffer, out _) == 0
            ? BitConverter.ToUInt64(buffer, 0) : 0UL;
    }

    internal static IntPtr Context => _context;
    internal static IntPtr Queue => _queue;
    internal static IntPtr Device => _device;

    private static bool Probe()
    {
        try
        {
            if (clGetPlatformIDs(0, null, out uint platformCount) != 0 || platformCount == 0)
            {
                UnavailableReason = "no OpenCL platforms: no vendor driver installed an ICD";
                return false;
            }
            var platforms = new IntPtr[platformCount];
            if (clGetPlatformIDs(platformCount, platforms, out _) != 0)
            {
                UnavailableReason = "the ICD loader failed to enumerate its platforms";
                return false;
            }

            // The BEST device, not the first one (27.08). "First GPU wins"
            // held on a machine with one card and quietly failed everywhere
            // else: an Intel iGPU beside an NVIDIA card, an AMD APU beside a
            // discrete card, an eGPU, a workstation with several. Auto is
            // supposed to mean "the fastest thing here", and the driver's
            // enumeration order is not a ranking.
            //
            // The score is compute units x clock, which is the closest thing
            // to throughput that OpenCL will tell us without running a kernel,
            // with global memory as the tie-break (an iGPU shares system RAM
            // and reports a lot of it, so memory alone would be misleading -
            // it only breaks ties). A discrete device beats an integrated one
            // of the same score, because CL_DEVICE_HOST_UNIFIED_MEMORY is
            // deprecated and unreliable; the vendor string is not used for
            // ranking at all - AMD, Intel and NVIDIA all ship both kinds.
            //
            // MP_OPENCL_DEVICE=<substring> forces one by name, for the machine
            // where the score is wrong; the reason string then says so.
            ulong wantedType = Environment.GetEnvironmentVariable("MP_OPENCL_ALLOW_CPU") == "1" ? DeviceTypeAll : DeviceTypeGpu;
            string? forced = Environment.GetEnvironmentVariable("MP_OPENCL_DEVICE");
            double bestScore = -1;
            int candidates = 0;

            foreach (IntPtr platform in platforms)
            {
                if (clGetDeviceIDs(platform, wantedType, 0, null, out uint devCount) != 0 || devCount == 0)
                    continue;

                var devices = new IntPtr[devCount];

                if (clGetDeviceIDs(platform, wantedType, devCount, devices, out _) != 0)
                    continue;

                foreach (IntPtr candidate in devices)
                {
                    candidates++;
                    string candidateName = ReadString(candidate, DeviceNameParam);

                    if (!string.IsNullOrEmpty(forced))
                    {
                        if (candidateName.Contains(forced, StringComparison.OrdinalIgnoreCase))
                        {
                            _device = candidate;
                            _selectionNote = $"forced by MP_OPENCL_DEVICE={forced}";
                        }
                        continue;
                    }

                    uint units = ReadUInt(candidate, ComputeUnitsParam);
                    uint clock = ReadUInt(candidate, ClockParam);
                    ulong memory = ReadULong(candidate, GlobalMemParam);
                    double score = (double)units * Math.Max(clock, 1u) + memory / 1e12;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    _device = candidate;
                    _selectionNote = $"{units} CU x {clock} MHz, {memory / (1024.0 * 1024 * 1024):0.0} GB";
                }
            }

            if (_device != IntPtr.Zero && candidates > 1)
                _selectionNote = $"{_selectionNote} - best of {candidates}";

            if (_device == IntPtr.Zero)
            {
                UnavailableReason = "OpenCL is installed but reports no GPU device";
                return false;
            }

            _deviceName = ReadString(_device, DeviceNameParam);
            // size_t, so eight bytes on the only architecture we ship.
            _maxWorkGroupSize = (int)Math.Min(ReadULong(_device, MaxWorkGroupParam), int.MaxValue);

            _context = clCreateContext(IntPtr.Zero, 1, [_device], IntPtr.Zero, IntPtr.Zero, out int err);
            if (err != 0 || _context == IntPtr.Zero)
            {
                UnavailableReason = $"clCreateContext refused ({err})";
                return false;
            }

            _queue = clCreateCommandQueue(_context, _device, 0, out err);
            if (err != 0 || _queue == IntPtr.Zero)
            {
                UnavailableReason = $"clCreateCommandQueue refused ({err})";
                return false;
            }

            if (!SmokeTest())
            {
                // UnavailableReason already carries the specific failure
                return false;
            }

            UnavailableReason = string.Empty;
            return true;
        }
        catch (DllNotFoundException)
        {
            UnavailableReason = "the OpenCL loader (OpenCL.dll) is not on this system - it ships with every vendor GPU driver";
            return false;
        }
        catch (Exception e)
        {
            UnavailableReason = $"the OpenCL probe crashed ({e.GetType().Name})";
            return false;
        }
    }

    /// <summary>
    /// 256 floats i·0.5 through `a[i] = a[i]·2 + 1`: every operation is exact
    /// in fp32, so the answer is compared with ==, not a tolerance. A driver
    /// that cannot get THIS right does not get to run physics.
    /// </summary>
    private static bool SmokeTest()
    {
        const string source = "__kernel void mp_smoke(__global float* a)\n{ int i = get_global_id(0); a[i] = a[i] * 2.0f + 1.0f; }\n";

        IntPtr program = IntPtr.Zero, kernel = IntPtr.Zero, buffer = IntPtr.Zero;
        try
        {
            program = BuildProgram(source, out string log);
            if (program == IntPtr.Zero)
            {
                UnavailableReason = $"the driver failed to compile the smoke kernel: {log}";
                return false;
            }
            kernel = clCreateKernel(program, "mp_smoke", out int err);
            if (err != 0) { UnavailableReason = $"clCreateKernel refused ({err})"; return false; }

            const int n = 256;
            float[] host = new float[n];
            for (int i = 0; i < n; i++) host[i] = i * 0.5f;

            buffer = clCreateBuffer(_context, MemReadWrite, (UIntPtr)(n * sizeof(float)), IntPtr.Zero, out err);
            if (err != 0) { UnavailableReason = $"clCreateBuffer refused ({err})"; return false; }
            if (clEnqueueWriteBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)(n * sizeof(float)), host, 0, null, IntPtr.Zero) != 0)
            { UnavailableReason = "the smoke upload failed"; return false; }

            IntPtr b = buffer;
            if (clSetKernelArg(kernel, 0, (UIntPtr)IntPtr.Size, ref b) != 0)
            { UnavailableReason = "clSetKernelArg refused the smoke buffer"; return false; }
            if (clEnqueueNDRangeKernel(_queue, kernel, 1, null, new[] { (UIntPtr)n }, null, 0, null, IntPtr.Zero) != 0)
            { UnavailableReason = "the smoke kernel would not launch"; return false; }
            if (clFinish(_queue) != 0)
            { UnavailableReason = "the smoke kernel crashed in flight"; return false; }
            if (clEnqueueReadBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)(n * sizeof(float)), host, 0, null, IntPtr.Zero) != 0)
            { UnavailableReason = "the smoke readback failed"; return false; }

            for (int i = 0; i < n; i++)
            {
                if (host[i] != i + 1.0f)
                {
                    UnavailableReason = $"the driver failed the smoke test (a[{i}] = {host[i]}, expected {i + 1})";
                    return false;
                }
            }

            return true;
        }
        finally
        {
            if (buffer != IntPtr.Zero) clReleaseMemObject(buffer);
            if (kernel != IntPtr.Zero) clReleaseKernel(kernel);
            if (program != IntPtr.Zero) clReleaseProgram(program);
        }
    }

    /// <summary>Compile + build, returning Zero and the build log on failure.</summary>
    /// <summary>The compiler's own words about the LAST kernel that failed to
    /// build, empty while nothing has failed.
    ///
    /// Every backend used to call BuildProgram with `out _` and report the
    /// failure as a silent null: the module fell back to the CPU and said
    /// nothing, on a machine where the driver had just explained itself in
    /// one line. Finding out why cost a reflection hack against this class
    /// (22.08, the Schrodinger kernel, where the answer was that `half` is a
    /// RESERVED TYPE in OpenCL C). A driver that bothers to answer should be
    /// quoted, not discarded - vendor compilers differ, and the one line that
    /// matters will come from a machine that is not this one.</summary>
    public static string LastBuildLog { get; private set; } = string.Empty;

    internal static IntPtr BuildProgram(string source, out string buildLog)
    {
        buildLog = string.Empty;
        IntPtr program = clCreateProgramWithSource(_context, 1, [source], null, out int err);

        if (err != 0 || program == IntPtr.Zero)
        {
            buildLog = $"clCreateProgramWithSource: {err}";
            LastBuildLog = buildLog;
            return IntPtr.Zero;
        }

        if (clBuildProgram(program, 1, [_device], null, IntPtr.Zero, IntPtr.Zero) != 0)
        {
            byte[] log = new byte[8192];

            if (clGetProgramBuildInfo(program, _device, BuildLogParam, (UIntPtr)log.Length, log, out UIntPtr written) == 0)
            {
                int len = Math.Min((int)written, log.Length);
                while (len > 0 && log[len - 1] == 0) len--;
                buildLog = Encoding.UTF8.GetString(log, 0, len).Trim();
            }

            clReleaseProgram(program);
            LastBuildLog = buildLog.Length > 0 ? buildLog : "clBuildProgram failed and the driver returned no log";
            return IntPtr.Zero;
        }

        return program;
    }

    internal static IntPtr Kernel(IntPtr program, string name) => clCreateKernel(program, name, out int err) is var k && err == 0 ? k : IntPtr.Zero;
    internal static IntPtr Buffer(long bytes) => clCreateBuffer(_context, MemReadWrite, (UIntPtr)bytes, IntPtr.Zero, out int err) is var b && err == 0 ? b : IntPtr.Zero;
    internal static bool Arg(IntPtr kernel, uint index, IntPtr buffer) => clSetKernelArg(kernel, index, (UIntPtr)IntPtr.Size, ref buffer) == 0;
    internal static bool Arg(IntPtr kernel, uint index, int value) => clSetKernelArg(kernel, index, sizeof(int), ref value) == 0;
    internal static bool Arg(IntPtr kernel, uint index, float value) => clSetKernelArg(kernel, index, sizeof(float), ref value) == 0;

    /// <summary>One reusable cell for the global work size. Launch is the
    /// hottest call in the layer - LBM alternates two kernels per iteration
    /// and the cloth runs four per sub-step - and allocating a one-element
    /// array per dispatch put a steady drip into gen0 for no reason (external
    /// analysis, 23.08). Every caller is inside the lock-free single-threaded
    /// simulation step, so one cell is enough; if that ever stops being true
    /// the queue itself would need a lock long before this array would.</summary>
    private static readonly UIntPtr[] _globalSize = new UIntPtr[1];

    private static readonly UIntPtr[] _localSize = new UIntPtr[1];

    internal static bool Launch(IntPtr kernel, long globalSize)
    {
        _globalSize[0] = (UIntPtr)globalSize;
        return clEnqueueNDRangeKernel(_queue, kernel, 1, null, _globalSize, null, 0, null, IntPtr.Zero) == 0;
    }

    /// <summary>Launch with an EXPLICIT work-group size, for kernels that share
    /// data through __local memory: the global size is rounded UP to a multiple
    /// of it, because OpenCL 1.2 requires the division to be exact and returns
    /// CL_INVALID_WORK_GROUP_SIZE otherwise - an error that arrives as "the
    /// backend just stopped working" three kernels later. The kernel must
    /// therefore expect work-items past the end of the data AND must not return
    /// early from them: every work-item in a group has to reach every barrier.
    /// (12.09, first client: the tiled n-body force pass.)</summary>
    internal static bool Launch(IntPtr kernel, long globalSize, long localSize)
    {
        if (localSize <= 0) return Launch(kernel, globalSize);
        long groups = (globalSize + localSize - 1) / localSize;
        _globalSize[0] = (UIntPtr)(groups * localSize);
        _localSize[0] = (UIntPtr)localSize;
        return clEnqueueNDRangeKernel(_queue, kernel, 1, null, _globalSize, _localSize, 0, null, IntPtr.Zero) == 0;
    }

    internal static bool Write(IntPtr buffer, float[] source, long floats, long floatOffset = 0) => clEnqueueWriteBuffer(_queue, buffer, 1, (UIntPtr)(floatOffset * sizeof(float)), (UIntPtr)(floats * sizeof(float)), source, 0, null, IntPtr.Zero) == 0;
    internal static bool Write(IntPtr buffer, byte[] source, long bytes) => clEnqueueWriteBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)bytes, source, 0, null, IntPtr.Zero) == 0;
    internal static bool Write(IntPtr buffer, sbyte[] source, long bytes) => clEnqueueWriteBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)bytes, source, 0, null, IntPtr.Zero) == 0;
    internal static bool Read(IntPtr buffer, byte[] target, long bytes) => clEnqueueReadBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)bytes, target, 0, null, IntPtr.Zero) == 0;
    internal static bool Read(IntPtr buffer, sbyte[] target, long bytes) => clEnqueueReadBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)bytes, target, 0, null, IntPtr.Zero) == 0;
    internal static bool Write(IntPtr buffer, ulong[] source, long count) => clEnqueueWriteBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)(count * 8), source, 0, null, IntPtr.Zero) == 0;
    internal static bool Read(IntPtr buffer, ulong[] target, long count) => clEnqueueReadBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)(count * 8), target, 0, null, IntPtr.Zero) == 0;
    internal static bool Write(IntPtr buffer, int[] source, long count) => clEnqueueWriteBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)(count * 4), source, 0, null, IntPtr.Zero) == 0;

    /// <summary>Read an int buffer back. Added for the dendrite (23.08), whose
    /// occupancy grid and its four-slot stats block are integers - every
    /// earlier client read floats or bytes.</summary>
    internal static bool Read(IntPtr buffer, int[] target, long count) => clEnqueueReadBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)(count * 4), target, 0, null, IntPtr.Zero) == 0;
    internal static bool Read(IntPtr buffer, float[] target, long floats) => clEnqueueReadBuffer(_queue, buffer, 1, UIntPtr.Zero, (UIntPtr)(floats * sizeof(float)), target, 0, null, IntPtr.Zero) == 0;
    internal static bool Finish() => clFinish(_queue) == 0;

    internal static void Release(IntPtr mem)
    {
        if (mem != IntPtr.Zero)
        {
            clReleaseMemObject(mem);
        }
    }

    internal static void ReleaseKernel(IntPtr k)
    {
        if (k != IntPtr.Zero)
        {
            clReleaseKernel(k);
        }
    }

    internal static void ReleaseProgram(IntPtr p)
    {
        if (p != IntPtr.Zero)
        {
            clReleaseProgram(p);
        }
    }
}
