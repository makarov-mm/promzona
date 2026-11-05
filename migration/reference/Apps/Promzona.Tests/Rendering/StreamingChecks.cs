using System.Reflection;
using System.Runtime.InteropServices;
using MakarovPhysics.Renderer;
using Promzona.Game;

// Explicit native test, run with -p:GameDir=<unprotected Release build>.
// Reads actual GPU buffers, compares them to the previous first-occurrence
// indexing algorithm, and exercises cancellation/replanning of streamed LODs.
internal static class StreamingChecks
{
    [DllImport("opengl32.dll",CharSet=CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ReadBuffer(uint target,nint offset,nint size,IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte IsBuffer(uint buffer);
    private static int _checks;
    private static void Check(bool condition,string name){if(!condition)throw new Exception(name);_checks++;}
    private static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static object? Get(object value,string name)=>value.GetType().GetProperty(name,Flags)?.GetValue(value)??value.GetType().GetField(name,Flags)!.GetValue(value);
    private static unsafe T[] Read<T>(uint target,uint buffer,int count) where T:unmanaged
    {
        var data=new T[count];GL.BindBuffer(target,buffer);
        var read=Marshal.GetDelegateForFunctionPointer<ReadBuffer>(wglGetProcAddress("glGetBufferSubData"));
        fixed(T* p=data)read(target,0,count*sizeof(T),(IntPtr)p);
        return data;
    }
    [STAThread] private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();
        using var form=new Form{ClientSize=new(800,600),BackColor=Color.Black,Location=new(-4200,-4200),StartPosition=FormStartPosition.Manual,ShowInTaskbar=false};
        using var gl=new GLControl{Dock=DockStyle.Fill};form.Controls.Add(gl);Exception? error=null;bool ran=false;
        gl.Render+=_=>{if(ran)return;ran=true;try{Run(args[0]);}catch(Exception ex){error=ex;}finally{form.Close();}};
        form.Shown+=(_,_)=>gl.Start();form.FormClosing+=(_,_)=>gl.Stop();Application.Run(form);
        Console.WriteLine(error?.ToString()??$"Streaming: {_checks} checks passed; GL errors: 0");return error==null?0:1;
    }
    private static void Run(string assetRoot)
    {
        var assembly=typeof(CityGame).Assembly;
        assembly.GetType("Promzona.Game.CityAtlas")!.GetProperty("AssetRoot")!.SetValue(null,assetRoot);
        using var shared=new CitySurface(3);
        var runtimeStageType=assembly.GetType("Promzona.Game.CityMeshStaging")!;
        var runtimeStage=Activator.CreateInstance(runtimeStageType)!;
        var runtimeBegin=runtimeStageType.GetMethod("Begin")!;
        var runtimePack=runtimeStageType.GetMethod("Pack")!;
        var runtimeUpload=typeof(CitySurface).GetMethod("UploadStatic",BindingFlags.NonPublic|BindingFlags.Instance,null,[runtimeStageType],null)!;
        var workspace=new CityMeshStaging();var r=new Random(29);
        var vertices=new List<SurfaceVertex>();
        for(int i=0;i<22000;i++)
        {
            var v=new SurfaceVertex(new(i%71,(float)r.NextDouble(),i%93),Vec3.UnitY,new(.2f,.3f,.4f),new(i%37,0,1),(SurfaceMaterial)(i%5));
            vertices.Add(v);vertices.Add(v);vertices.Add(v with { Normal=Vec3.UnitX });
            vertices.Add(v with { Local=Vec3.UnitZ });vertices.Add(v with { Material=SurfaceMaterial.Brass });
        }
        foreach(int size in new[]{vertices.Count,17,0,vertices.Count,3})
        {
            var input=vertices.Take(size).ToArray();var lookup=new Dictionary<SurfaceVertex,uint>();var unique=new List<SurfaceVertex>();var indices=new uint[size];
            for(int i=0;i<size;i++){var v=input[i];if(!lookup.TryGetValue(v,out uint index)){index=(uint)unique.Count;lookup.Add(v,index);unique.Add(v);}indices[i]=index;}
            workspace.Begin(input);while(!workspace.Pack(127)){}
            Check(workspace.UniqueCount==unique.Count&&workspace.VertexCount==size,"exact unique and index counts");
            Check(workspace.Indices.Take(size).SequenceEqual(indices),"chunking preserves triangle order");
            var expected=new List<float>();foreach(var v in unique)expected.AddRange([v.Position.X,v.Position.Y,v.Position.Z,v.Normal.X,v.Normal.Y,v.Normal.Z,v.Color.X,v.Color.Y,v.Color.Z,v.Local.X,v.Local.Y,v.Local.Z,(float)(v.Material==SurfaceMaterial.Automatic?SurfaceMaterial.Enamel:v.Material)]);
            Check(workspace.Vertices.Take(expected.Count).SequenceEqual(expected),"UV, normals, paint and material identity");
            runtimeBegin.Invoke(runtimeStage,[input]);while(!(bool)runtimePack.Invoke(runtimeStage,[127])!){}
            using var surface=new CitySurface(3,shared);runtimeUpload.Invoke(surface,[runtimeStage]);
            Check(Read<float>(GL.ARRAY_BUFFER,(uint)Get(surface,"_vbo")!,expected.Count).SequenceEqual(expected),"GPU vertices exact");
            GL.BindVertexArray((uint)Get(surface,"_vao")!);
            Check(Read<uint>(GL.ELEMENT_ARRAY_BUFFER,(uint)Get(surface,"_ebo")!,size).SequenceEqual(indices),"GPU indices exact with reused capacity");GL.BindVertexArray(0);
            Check(surface.StagingBytes==0&&surface.GpuBytes==expected.Count*4L+size*4L,"resident meshes retain no staging arrays");
        }
        workspace.Begin(vertices);workspace.Pack(10);workspace.Cancel();workspace.Begin(vertices.Take(3).ToArray());workspace.Pack();Check(workspace.VertexCount==3,"cancelled job cannot leak indices");
        var oldVertices=workspace.Vertices;var oldIndices=workspace.Indices;workspace.Begin(vertices.Take(3).ToArray());workspace.Pack();Check(ReferenceEquals(oldVertices,workspace.Vertices)&&ReferenceEquals(oldIndices,workspace.Indices),"reuse retained arrays");workspace.Release();Check(workspace.BufferBytes==0,"workspace release");
        CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:4));
        var districtType=assembly.GetType("Promzona.Game.CityDistricts")!;
        using var districts=(IDisposable)Activator.CreateInstance(districtType)!;
        districtType.GetField("Distance")!.SetValue(districts,100f);
        var update=districtType.GetMethod("Update")!.CreateDelegate<Action<Vec3>>(districts);
        void Drain(Vec3 eye){for(int i=0;i<1500;i++){update(eye);if((int)Get(districts,"PendingCount")! ==0)return;}throw new Exception("streaming queue failed to drain");}
        update(Vec3.Zero);districtType.GetField("Quality")!.SetValue(districts,0);Drain(new(300,0,300));
        var tiles=(System.Collections.IDictionary)Get(districts,"_tiles")!;
        foreach(System.Collections.DictionaryEntry tile in tiles){var key=((int,int))tile.Key;double dx=CityTraffic.Coordinate(key.Item1)+18-300,dz=CityTraffic.Coordinate(key.Item2)+18-300;Check(dx*dx+dz*dz<=140*140,"teleport evicts old districts");}
        int count=tiles.Count,built=(int)Get(districts,"BuiltCount")!;for(int i=0;i<30;i++)update(new(300,0,300));Check(tiles.Count==count&&(int)Get(districts,"BuiltCount")! ==built,"stable camera does not rebuild tiles");
        districtType.GetField("Quality")!.SetValue(districts,2);Drain(new(300,0,300));
        foreach(System.Collections.DictionaryEntry tile in tiles)
        {
            var key=((int,int))tile.Key;double dx=CityTraffic.Coordinate(key.Item1)+18-300,dz=CityTraffic.Coordinate(key.Item2)+18-300;
            if(dx*dx+dz*dz<60*60)Check((int)Get(tile.Value!,"Lod")! ==0,"new near detail reaches ready state");
        }
        var blocker = new CityDistrictOcclusion();
        var eye = new Vec3(0,2,0);
        var camera = new CityVisibility(Mat4.Perspective(1.2f, 1.6f, .2f, 500) * Mat4.LookAt(eye, new(0,2,1), Vec3.UnitY));
        blocker.Reset(eye);
        blocker.Add(new(0,2,12), new(4,2,1), camera);
        Check(blocker.Hidden(new(0,2,30), new(1,1,1)), "box behind opaque facade");
        Check(!blocker.Hidden(new(11,2,30), new(2,1,1)), "keep a partly visible edge");
        Check(!blocker.Hidden(new(0,9,30), new(1,2,1)), "keep protruding roofs");
        Check(!blocker.Hidden(new(0,2,11), new(1,1,1)), "keep tile straddling front facade");
        blocker.Reset(eye);
        blocker.Add(new(0,2,1), new(4,2,1), camera);
        Check(!blocker.Hidden(new(0,2,30), new(1,1,1)), "never use a clipped occluder");

        districtType.GetField("Distance")!.SetValue(districts,500f);
        Drain(Vec3.Zero);
        var distant = tiles.Values.Cast<object>().First(t=>(int)Get(t,"Lod")! ==3);
        var batch = Get(distant,"Batch")!;
        var parts = (Array)Get(batch,"Parts")!;
        int firstIndex=0;
        foreach(var member in parts)
        {
            Check(member!=null && ReferenceEquals(Get(member,"Surface"),Get(distant,"Surface")),"distant group shares mesh");
            var range=Get(member!,"Bounds")!;
            Check((int)Get(range,"First")! ==firstIndex,"index ranges contiguous without overlap");
            firstIndex+=(int)Get(range,"Count")!;
        }
        var coordinate = tiles.Keys.Cast<(int,int)>().First(k=>ReferenceEquals(tiles[k],distant));
        Drain(new((float)CityTraffic.Coordinate(coordinate.Item1)+18, 0, (float)CityTraffic.Coordinate(coordinate.Item2)+18));
        Check((int)Get(tiles[coordinate]!,"Lod")! ==0, "approaching skyline swaps to full detail");
        var handles=tiles.Values.Cast<object>().Select(t=>(uint)Get(Get(t,"Surface")!,"_vbo")!).ToArray();districts.Dispose();
        var isBuffer=Marshal.GetDelegateForFunctionPointer<IsBuffer>(wglGetProcAddress("glIsBuffer"));
        foreach(uint handle in handles)Check(isBuffer(handle)==0,"tile GPU buffer deleted on dispose");
        Check((long)Get(districts,"PackingBufferBytes")! ==0,"streamer workspace released");Check(GL.GetError()==0,"GL error free");
    }
}
