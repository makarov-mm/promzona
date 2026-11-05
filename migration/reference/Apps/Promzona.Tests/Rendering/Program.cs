using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using MakarovPhysics.Renderer;

// Opt-in real OpenGL regression, independent of the CPU gameplay test runner.
// dotnet run --project Rendering/PostProcessRenderChecks.csproj -p:GameDir=<publish> -- <evidence.json>
internal static class Program
{
    [DllImport("opengl32.dll")] private static extern void glGetTexImage(uint target,int level,uint format,uint type,IntPtr data);
    [DllImport("opengl32.dll")] private static extern void glFinish();
    private static readonly Type PostType=typeof(Promzona.Game.CityCar).Assembly.GetType("Promzona.Game.CityPostProcess")!;
    private static uint Texture(object post)=> (uint)PostType.GetField("_ao",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(post)!;
    private static unsafe float[] ReadAo(object post,int w,int h)
    {
        var values=new float[((w+1)/2)*((h+1)/2)];GL.ActiveTexture(GL.TEXTURE0);GL.BindTexture(GL.TEXTURE_2D,Texture(post));
        glFinish();fixed(float* data=values)glGetTexImage(GL.TEXTURE_2D,0,0x1903,GL.FLOAT,(IntPtr)data);return values;
    }
    [STAThread] private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        using var form=new Form{ClientSize=new(960,540),Location=new(-4200,-4200),StartPosition=FormStartPosition.Manual,ShowInTaskbar=false,BackColor=Color.Black};
        using var control=new GLControl{Dock=DockStyle.Fill};form.Controls.Add(control);
        bool ran=false;int failed=0;var results=new List<object>();
        control.Render+=_=>{
            if(ran)return;ran=true;
            try
            {
                using var post=(IDisposable)Activator.CreateInstance(PostType)!;
                using var program=new Shader(Vertex,Fragment);uint vao=GL.GenVertexArray();
                try
                {
                    foreach(var (w,h) in new[]{(960,540),(1720,720),(1721,721),(3440,1440)})
                    foreach(float pitch in new[]{.12f,.42f,.9f})
                    foreach(int quality in new[]{1,2})
                    {
                        GL.RestorePhysicalViewport(0,0,w,h);
                        var camera=new OrbitCamera{Target=Vec3.Zero,Distance=12,Pitch=pitch,Yaw=0,Near=.12f,Far=4000,Fovy=MathF.PI/3};
                        Action scene=()=>Draw(program,vao,camera,w,h,false);
                        PostType.GetMethod("Render")!.Invoke(post,[scene,camera,quality]);
                        var ao=ReadAo(post,w,h);int bad=ao.Count(v=>!float.IsFinite(v)||v<.995f||v>1.001f);
                        bool pass=bad==0;if(!pass)failed++;
                        results.Add(new{kind="flat plane",w,h,pitch,quality,min=ao.Min(),bad,pass});
                        Console.WriteLine($"{(pass?"PASS":"FAIL")} flat {w}x{h} pitch={pitch} quality={quality}: min={ao.Min()} bad={bad}");
                    }
                    GL.RestorePhysicalViewport(0,0,960,540);
                    var cornerCamera=new OrbitCamera{Target=new(0,.5f,0),Distance=7,Pitch=.6f,Yaw=.4f,Near=.12f,Far=4000,Fovy=MathF.PI/3};
                    Action corner=()=>Draw(program,vao,cornerCamera,960,540,true);
                    PostType.GetMethod("Render")!.Invoke(post,[corner,cornerCamera,2]);
                    var contact=ReadAo(post,960,540);int dark=contact.Count(v=>v<.94f);bool contactPass=dark>100&&contact.All(float.IsFinite);
                    if(!contactPass)failed++;results.Add(new{kind="wall ground contact retained",dark,min=contact.Min(),pass=contactPass});
                    Console.WriteLine($"{(contactPass?"PASS":"FAIL")} genuine contact: {dark} dark pixels");
                    bool glPass=GL.GetError()==0;if(!glPass)failed++;results.Add(new{kind="OpenGL error free",pass=glPass});
                }
                finally{GL.DeleteVertexArray(vao);}
            }
            catch(Exception ex){failed++;Console.WriteLine(ex);results.Add(new{error=ex.ToString()});}
            form.Close();
        };
        form.Shown+=(_,_)=>control.Start();form.FormClosing+=(_,_)=>control.Stop();Application.Run(form);
        if(args.Length>0)File.WriteAllText(args[0],JsonSerializer.Serialize(new{failed,results},new JsonSerializerOptions{WriteIndented=true}));
        return failed==0?0:1;
    }
    private static void Draw(Shader s,uint vao,OrbitCamera cam,int w,int h,bool wall)
    {
        GL.Enable(GL.DEPTH_TEST);GL.DepthMask(true);GL.Disable(GL.CULL_FACE);GL.Disable(GL.BLEND);
        s.Use();s.SetMatrix4("uView",cam.View());s.SetMatrix4("uProj",cam.Projection((float)w/h));GL.BindVertexArray(vao);
        s.SetInt("uWall",0);GL.DrawArrays(GL.TRIANGLES,0,6);
        if(wall){s.SetInt("uWall",1);GL.DrawArrays(GL.TRIANGLES,0,6);}
    }
    private const string Vertex=@"#version 330 core
uniform mat4 uView,uProj;uniform int uWall;
void main(){vec2 p[6]=vec2[6](vec2(-1,-1),vec2(1,-1),vec2(1,1),vec2(-1,-1),vec2(1,1),vec2(-1,1));vec2 v=p[gl_VertexID];
vec3 world=uWall==0?vec3(v.x*1000.,0.,v.y*1000.):vec3(v.x*2.,(v.y+1.)*1.5,0.);gl_Position=uProj*uView*vec4(world,1.);}";
    private const string Fragment=@"#version 330 core
out vec4 FragColor;void main(){FragColor=vec4(.5,.5,.5,1.);}";
}
