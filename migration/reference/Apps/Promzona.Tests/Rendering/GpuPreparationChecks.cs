using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using MakarovPhysics.Renderer;
using Promzona.Game;

// Explicit real-GL test against an unprotected build. Isolated from the CPU test runner.
internal static class GpuPreparationChecks
{
    [DllImport("opengl32.dll")] private static extern void glGetFloatv(uint name, [Out] float[] values);
    [DllImport("opengl32.dll", CharSet=CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte IsObject(uint id);
    [DllImport("opengl32.dll")] private static extern void glReadPixels(int x,int y,int w,int h,uint format,uint type,[Out] byte[] data);
    private static uint Target(out uint color)
    {
        uint target=GL.GenFramebuffer();color=GL.GenRenderbuffer();GL.BindRenderbuffer(GL.RENDERBUFFER,color);
        GL.RenderbufferStorage(GL.RENDERBUFFER,GL.RGBA8,64,64);GL.BindFramebuffer(GL.FRAMEBUFFER,target);
        GL.FramebufferRenderbuffer(GL.FRAMEBUFFER,GL.COLOR_ATTACHMENT0,GL.RENDERBUFFER,color);
        GL.DrawBuffer(GL.COLOR_ATTACHMENT0);GL.ReadBuffer(GL.COLOR_ATTACHMENT0);
        GL.ColorMask(true,true,true,true);GL.Disable(GL.SCISSOR_TEST);GL.ClearColor(.2f,.4f,.6f,1);GL.Clear(GL.COLOR_BUFFER_BIT);return target;
    }
    private static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static readonly Assembly Game=typeof(CityGame).Assembly;
    private static int _checks;
    private static object? Field(object value,string name)=>value.GetType().GetField(name,Fields)!.GetValue(value);
    private static void Check(bool value,string name){if(!value)throw new Exception(name);_checks++;}
    [STAThread] private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        using var form=new Form{ClientSize=new(800,600),BackColor=Color.Black,Location=new(-4200,-4200),StartPosition=FormStartPosition.Manual,ShowInTaskbar=false};
        using var gl=new GLControl{Dock=DockStyle.Fill};form.Controls.Add(gl);Exception? error=null;bool ran=false;
        gl.Render+=_=>{if(ran)return;ran=true;try{Run(args[0]);}catch(Exception ex){error=ex;}finally{form.Close();}};
        form.Shown+=(_,_)=>gl.Start();form.FormClosing+=(_,_)=>gl.Stop();Application.Run(form);
        Console.WriteLine(error?.ToString()??$"GPU preparation: {_checks} checks passed; GL errors: 0");
        return error is null?0:1;
    }
    private static string State()
    {
        uint active=(uint)GL.GetInteger(0x84E0);var textures=new List<int>();
        foreach(uint unit in new uint[]{0,1,2,6,9,11}){GL.ActiveTexture(GL.TEXTURE0+unit);textures.Add(GL.GetInteger(0x8069));}
        GL.ActiveTexture(active);var clear=new float[4];var mask=new float[4];glGetFloatv(0x0C22,clear);glGetFloatv(0x0C23,mask);
        return JsonSerializer.Serialize(new{
            viewport=GL.PhysicalViewport().ToString(),scissor=GL.PhysicalScissor().ToString(),scale=GL.RenderScale,
            bindings=new uint[]{0x8CA6,0x8CAA,0x8B8D,0x85B5,0x8894,0x8CA7,0x0B74,0x0B72}.Select(GL.GetInteger).ToArray(),
            flags=new[]{GL.DEPTH_TEST,GL.BLEND,GL.CULL_FACE,GL.SCISSOR_TEST}.Select(GL.IsEnabled).ToArray(),active,textures,clear,mask});
    }
    private static void Released(object preparation,uint[] handles)
    {
        var framebuffer=Marshal.GetDelegateForFunctionPointer<IsObject>(wglGetProcAddress("glIsFramebuffer"));
        var renderbuffer=Marshal.GetDelegateForFunctionPointer<IsObject>(wglGetProcAddress("glIsRenderbuffer"));
        Check(framebuffer(handles[0])==0,"private framebuffer released");
        foreach(uint handle in handles.Skip(1))Check(renderbuffer(handle)==0,"private attachment released");
        Check(Handles(preparation).All(h=>h==0),"disposed handles cleared");
    }
    private static uint[] Handles(object preparation)=>new[]{"_target","_depth","_hdr","_ldr"}.Select(n=>(uint)Field(preparation,n)!).ToArray();
    private static void Run(string assetRoot)
    {
        uint caller=Target(out uint callerColor),reader=Target(out uint readerColor);
        GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,caller);GL.BindFramebuffer(GL.READ_FRAMEBUFFER,reader);
        var original=new byte[64*64*4];glReadPixels(0,0,64,64,GL.RGBA,GL.UNSIGNED_BYTE,original);
        using var program=new Shader(DefaultShaders.DepthVertex,DefaultShaders.DepthFragment);program.Use();
        // An intentionally non-default loading/UI state must survive normal and exceptional paths.
        GL.RenderScale=2;GL.RestorePhysicalViewport(7,11,301,199);GL.RestorePhysicalScissor(4,6,111,99);
        GL.Enable(GL.SCISSOR_TEST);GL.Enable(GL.BLEND);GL.Enable(GL.CULL_FACE);GL.Disable(GL.DEPTH_TEST);
        GL.DepthMask(false);GL.DepthFunc(0x0203);GL.ColorMask(false,true,false,true);GL.ClearColor(.2f,.3f,.4f,.7f);
        uint vao=GL.GenVertexArray(),buffer=GL.GenBuffer(),renderbuffer=GL.GenRenderbuffer();
        GL.BindVertexArray(vao);GL.BindBuffer(GL.ARRAY_BUFFER,buffer);GL.BindRenderbuffer(GL.RENDERBUFFER,renderbuffer);GL.ActiveTexture(GL.TEXTURE0+5);
        var type=Game.GetType("Promzona.Game.CityGpuPreparation")!;
        using(var target=(IDisposable)Activator.CreateInstance(type)!)
        {
            var run=type.GetMethod("Run")!.CreateDelegate<Action<Action<bool>>>(target);string before=State();
            var phases=new List<bool>();run(depth=>{phases.Add(depth);GL.ClearColor(1,0,0,1);});
            Check(phases.SequenceEqual(new[]{true,false,false}),"shadow/HDR/LDR paths exercised");
            Check(State()==before,"all touched UI/GL state restored");var handles=Handles(target);
            bool caught=false;try{run(_=>throw new InvalidOperationException("test cancellation"));}catch(InvalidOperationException){caught=true;}
            Check(caught&&State()==before,"exception restores UI/GL state");
            target.Dispose();Released(target,handles);target.Dispose();Check(GL.GetError()==0,"repeated dispose safe");
        }
        var pixels=new byte[original.Length];GL.BindFramebuffer(GL.READ_FRAMEBUFFER,caller);glReadPixels(0,0,64,64,GL.RGBA,GL.UNSIGNED_BYTE,pixels);
        Check(pixels.SequenceEqual(original),"loading framebuffer pixels unchanged");
        GL.BindFramebuffer(GL.FRAMEBUFFER,0);GL.DeleteFramebuffer(caller);GL.DeleteFramebuffer(reader);GL.DeleteRenderbuffer(callerColor);GL.DeleteRenderbuffer(readerColor);
        GL.BindVertexArray(0);GL.BindBuffer(GL.ARRAY_BUFFER,0);GL.BindRenderbuffer(GL.RENDERBUFFER,0);
        GL.DeleteVertexArray(vao);GL.DeleteBuffer(buffer);GL.DeleteRenderbuffer(renderbuffer);
        GL.RenderScale=1;GL.RestorePhysicalViewport(0,0,800,600);GL.Disable(GL.SCISSOR_TEST);GL.Disable(GL.BLEND);
        GL.DepthMask(true);GL.ColorMask(true,true,true,true);GL.ActiveTexture(GL.TEXTURE0);
        CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:4));
        type=Game.GetType("Promzona.Game.CityActorModels")!;
        foreach(bool cancel in new[]{true,false})
        {
            using var actors=(IDisposable)Activator.CreateInstance(type,[assetRoot])!;
            var prepare=type.GetMethod("PrepareResources")!.CreateDelegate<Func<double>>(actors);
            string before=State();double previous=0;int steps=0;object? target=null;uint[] handles=[];
            while(previous<1&&steps<100)
            {
                double progress=prepare();steps++;Check(progress>=previous&&progress<=1,"monotonic real loading progress");
                Check(State()==before,"actual actor draws preserve caller state");previous=progress;
                if(Field(actors,"_preparationTarget") is {} active){target=active;handles=Handles(active);}
                if(cancel)break;
            }
            if(!cancel){Check(previous==1&&steps>=17,"all animal LODs, people and fallbacks prepared");Check(prepare()==1&&Field(actors,"_preparationTarget") is null,"completed queue does no more GPU work");}
            actors.Dispose();Check(target is not null,"temporary target existed");Released(target!,handles);
            Check(GL.GetError()==0,"actual actor preparation and cancellation GL error free");
        }
    }
}
