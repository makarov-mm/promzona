using MakarovPhysics.Renderer;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace Promzona.Game;

/// <summary>Prepared, credited external assets. GPU resources are shared per asset,
/// never per actor; the existing importer reads each single-material primitive.</summary>
internal sealed class CityImportedModels : IDisposable
{
    private sealed record Part(GltfModel Mesh,int Material,int Joint);
    private sealed record ImportedSurface(Vec3 Color,float Rough,float Metal,bool Glass,Texture? ColorMap,Texture? NormalMap,Texture? FinishMap);
    private readonly record struct Wheel(Vec3 Pivot,float Radius,bool Front);
    private sealed class Asset : IDisposable
    {
        public required JsonDocument Metadata;
        public required Part[][] Lods;
        public required Dictionary<string,Texture> Maps;
        public required Vec3 Size;
        public bool FarSurfaceAlpha;
        public required ImportedSurface[] Surfaces;
        public required Vec3[] Hinges;
        public required Wheel[] Wheels;
        public void Dispose(){foreach(var lod in Lods)foreach(var p in lod)p.Mesh.Dispose();foreach(var map in Maps.Values)map.Dispose();Metadata.Dispose();}
    }
    private readonly Dictionary<string,Asset> _assets=[];
    private readonly Shader _shader = new(CityShaderSource.Read("Models.Imported.vert"), CityShaderSource.ReadGeometry("Imported.frag"));
    private readonly Shader _depth = new(DefaultShaders.DepthVertex, DefaultShaders.DepthFragment);
    private readonly ConditionalWeakTable<CityCar,CityWheelMotion> _travel=new();
    public static readonly HashSet<string> Available=[];
    public CityReflections? Reflections;
    public int Count=>_assets.Count;
    public CityImportedModels(string root)
    {
        Available.Clear();string folder=Path.Combine(root,"Models","Imported");
        if(!Directory.Exists(folder))return;
        foreach(string dir in Directory.EnumerateDirectories(folder))
        {
            var meshes=new List<GltfModel>();var maps=new Dictionary<string,Texture>();JsonDocument? doc=null;
            try
            {
                doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"manifest.json")));var data=doc.RootElement;
                var lods=data.GetProperty("lods").EnumerateArray().Select(lod=>lod.EnumerateArray().Select(p=>{
                    var mesh=GltfModel.Load(Path.Combine(dir,p.GetProperty("file").GetString()!));meshes.Add(mesh);
                    return new Part(mesh,p.GetProperty("material").GetInt32(),p.GetProperty("part").GetInt32());}).ToArray()).ToArray();
                if(lods.Length!=3||lods.Any(l=>l.Length==0))throw new InvalidDataException("Three nonempty LODs required");
                foreach(string file in Directory.EnumerateFiles(dir,"*.png")){
                    using var bitmap=new Bitmap(file);var map=new Texture(bitmap);maps.Add(Path.GetFileName(file),map);
                    if(Path.GetFileName(file)=="far-color.png") {map.Bind();GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MIN_FILTER,(int)GL.NEAREST);GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MAG_FILTER,(int)GL.NEAREST);}
                    else {map.SetWrapRepeat();map.SetAnisotropy(8);}
                }
                string name=data.GetProperty("name").GetString()!;
                var surfaces=data.GetProperty("materials").EnumerateArray().Select(m=>{
                    Texture? Map(string key)=>m.TryGetProperty(key,out var p)&&maps.TryGetValue(p.GetString()!,out var t)?t:null;
                    return new ImportedSurface(Point(m.GetProperty("color")),m.GetProperty("roughness").GetSingle(),m.GetProperty("metal").GetSingle(),m.GetProperty("glass").GetBoolean(),Map("colorMap"),Map("normalMap"),Map("finishMap"));}).ToArray();
                var hinges=data.GetProperty("hinges").EnumerateArray().Select(Point).ToArray();
                var wheels=data.GetProperty("wheels").EnumerateArray().Select(w=>new Wheel(Point(w.GetProperty("pivot")),w.GetProperty("radius").GetSingle(),w.GetProperty("front").GetBoolean())).ToArray();
                // Every part index is checked here, where a bad manifest means the
                // built-in fallback; unchecked, it threw inside Draw on every frame.
                foreach(var part in lods.SelectMany(l=>l))
                    if(part.Material>=surfaces.Length||part.Material<0&&!maps.ContainsKey("far-color.png")||part.Joint<0||part.Joint is 1 or 2&&hinges.Length<2||part.Joint>=3&&part.Joint<100&&part.Joint-3>=wheels.Length)
                        throw new InvalidDataException($"Part material {part.Material} / joint {part.Joint} names a missing material, hinge, wheel or far map");
                _assets.Add(name,new Asset{Metadata=doc,Lods=lods,Maps=maps,Size=Point(data.GetProperty("size")),Surfaces=surfaces,
                    FarSurfaceAlpha=data.TryGetProperty("farSurfaceAlpha",out var farAlpha)&&farAlpha.GetBoolean(),
                    Hinges=hinges,Wheels=wheels});Available.Add(name);
            }
            catch(Exception e){foreach(var m in meshes)m.Dispose();foreach(var t in maps.Values)t.Dispose();doc?.Dispose();System.Diagnostics.Trace.WriteLine($"Imported asset {dir}: {e.Message}; built-in fallback");}
        }
    }
    private static Vec3 Point(JsonElement p)=>new(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle());
    // Distance first: this runs for every car several times a frame, and the
    // string hash of the model name is the expensive part.
    public bool Replaces(CityCar c,Vec3 eye,float range)=>(Position(c)-eye).Length()<range&&CityFleet.Model(c) is {} name&&_assets.ContainsKey(name);
    private static Vec3 Position(CityCar c)=>new((float)c.Position.X,(float)(c.SurfaceHeight+c.Lift),(float)c.Position.Z);
    private static Mat4 Transform(CityCar c,Asset a)
    {
        float support=MathF.Abs(MathF.Sin((float)c.Roll))*(float)c.HalfWidth+MathF.Abs(MathF.Sin((float)c.Pitch))*(float)c.HalfLength+MathF.Abs(MathF.Cos((float)c.Roll)*MathF.Cos((float)c.Pitch))*.7f;
        return Mat4.Translate(Position(c)+new Vec3(0,MathF.Max(.7f,support),0))*Mat4.RotateY((float)c.Heading)*Mat4.RotateX(-(float)(c.Pitch+c.TerrainPitch))*Mat4.RotateZ(-(float)(c.Roll+c.TerrainRoll))*Mat4.Translate(new(0,-.7f,0))*(c.Kind is 11 or 12?Mat4.Identity:Mat4.Scale((float)(2*c.HalfWidth)/a.Size.X,(float)c.Height/a.Size.Y,(float)(2*c.HalfLength)/a.Size.Z));
    }
    private static Mat4 Joint(CityCar? c,Asset a,int joint,double travel)
    {
        if(c is null||joint==0)return Mat4.Identity;
        if(c.Kind==14 && joint is 100 or 103)
        {
            var centre = new Vec3(0, 2.31f, .55f);
            var turn = Mat4.Translate(centre)*Mat4.RotateY((float)c.SpecialYaw)*Mat4.Translate(centre*-1);
            if (joint==100) return turn;
            var hinge = new Vec3(0,2.31f,.95f);
            return turn*Mat4.Translate(hinge)*Mat4.RotateX(-(float)c.GunElevation)*Mat4.Translate(hinge*-1);
        }
        if(joint>=100){Vec3 centre=joint==100?new(0,1.8f,.2f):joint==101?new(0,3.04f,0):new(.25f,2.5f,-3.9f);
            var rotation=joint==100?Mat4.RotateY((float)c.SpecialYaw):joint==101?Mat4.RotateY((float)c.RotorPhase):Mat4.RotateX((float)c.RotorPhase*1.7f);
            return Mat4.Translate(centre)*rotation*Mat4.Translate(centre*-1);}
        if(joint<3){Vec3 hinge=a.Hinges[joint-1];int side=joint==1?-1:1;
            return Mat4.Translate(hinge)*Mat4.RotateY((float)(c.DoorOpen*(c.CrewDeployed||side==-c.DoorSide?1:0)*-side*1.05))*Mat4.Translate(hinge*-1);}
        var w=a.Wheels[joint-3];Vec3 pivot=w.Pivot;
        float radius=w.Radius*(float)c.Height/a.Size.Y;
        return Mat4.Translate(pivot)*Mat4.RotateY(w.Front?(float)c.Steering:0)*Mat4.RotateX((float)(travel/Math.Max(.12,radius)%Math.Tau))*Mat4.Translate(pivot*-1);
    }
    private void Material(Asset a,int index)
    {
        var s=_shader;bool far=index<0;s.SetInt("uFar",far?1:0);s.SetInt("uFarSurfaceAlpha",far&&a.FarSurfaceAlpha?1:0);
        ImportedSurface? m=far?null:a.Surfaces[index];
        void Map(Texture? t,int unit,string flag){s.SetInt(flag,t is null?0:1);t?.Bind((uint)unit);}
        Map(far?a.Maps["far-color.png"]:m!.ColorMap,0,"uHasColor");Map(m?.NormalMap,1,"uHasNormal");Map(m?.FinishMap,2,"uHasFinish");
        s.SetVec3("uColor",m?.Color??new(1,1,1));
        s.SetFloat("uRough",m?.Rough??.7f);s.SetFloat("uMetal",m?.Metal??0);
        s.SetInt("uGlass",m?.Glass==true?1:0);
    }
    private void Render(Asset a,Mat4 model,int lod,CityCar? car,bool depth,float fade=0,int layer=0,bool glassPass=false)
    {
        Shader s=depth?_depth:_shader;
        if(!depth){s.SetFloat("uLodBlend",fade);s.SetInt("uLodLayer",layer);}
        if(!depth){var paint=car is null?(1f,1f,1f):CityAppearance.Paint(car);s.SetVec3("uPaint",new(paint.Item1,paint.Item2,paint.Item3));s.SetInt("uVehicle",car is null?0:1);s.SetFloat("uDamage",car is null?0:car.Destroyed?2:(float)car.Damage);s.SetVec3("uSize",a.Size);}
        double travel=car is null?0:_travel.GetOrCreateValue(car).Sample(car.Distance,car.Speed);
        int last=int.MinValue;
        foreach(var part in a.Lods[lod]){
            bool cabinGlass=car?.Kind is 9 or 10&&part.Material>=0&&a.Surfaces[part.Material].Glass;
            if(glassPass?!cabinGlass:cabinGlass)continue;
            if(!depth)s.SetInt("uTransparent",glassPass?1:0);
            s.SetMatrix4("uModel",model*Joint(car,a,part.Joint,travel));if(!depth&&last!=part.Material){Material(a,part.Material);last=part.Material;}part.Mesh.Mesh.Draw();}
    }
    private void RenderLods(Asset asset,Mat4 model,float distance,CityCar? car)
    {
        var lod=CityVehicleLod.Select(distance);
        Render(asset,model,lod.First,car,false,lod.Blend);
        if(lod.First!=lod.Second&&lod.Blend>0)Render(asset,model,lod.Second,car,false,lod.Blend,1);
    }
    public void Draw(IEnumerable<CityCar> cars,Vec3 eye,Mat4 view,Mat4 projection,Vec3 sun,Vec3 light,Mat4 ls,uint shadow,OutdoorLighting lighting,float range)
    {
        var s=_shader;s.Use();s.SetMatrix4("uView",view);s.SetMatrix4("uProj",projection);s.SetVec3("uViewPos",eye);s.SetInt("uColorMap",0);s.SetInt("uNormalMap",1);s.SetInt("uFinishMap",2);
        Materials.ApplyLight(s,sun,light,.52f,.8f,.25f);CityLighting.Apply(lighting,s);s.SetInt("uLocalLighting",1);
        if(Reflections is not null)Reflections.Apply(s);else{s.SetInt("uProbe",12);s.SetInt("uProbeReady",0);}
        s.SetMatrix4("uLightSpace",ls);s.SetInt("uShadowMap",6);s.SetFloat("uShadowStrength",shadow==0?0:.7f*(1-.72f*CityLighting.Atmosphere.Rain));GL.ActiveTexture(GL.TEXTURE0+6);GL.BindTexture(GL.TEXTURE_2D,shadow);
        var visible=new CityVisibility(projection*view);bool cull=GL.IsEnabled(GL.CULL_FACE);GL.Disable(GL.CULL_FACE);
        foreach(var c in cars)if(Replaces(c,eye,range)&&visible.Sphere(Position(c)+new Vec3(0,1,0),(float)c.HalfLength+2)){
            var a=_assets[CityFleet.Model(c)!];RenderLods(a,Transform(c,a),(Position(c)-eye).Length(),c);}
        foreach(var site in CityImportedSites.All())if(_assets.TryGetValue(site.Model,out var a)){
            Vec3 p=new((float)site.Position.X,(float)site.Ground,(float)site.Position.Z);float d=(p-eye).Length();if(d>range||!visible.Sphere(p+new Vec3(0,site.Height*.5f,0),site.Radius))continue;
            RenderLods(a,Mat4.Translate(p)*Mat4.RotateY(site.Angle)*Mat4.Scale(site.Width/a.Size.X,site.Height/a.Size.Y,site.Length/a.Size.Z),d,null);}
        if(cull)GL.Enable(GL.CULL_FACE);GL.ActiveTexture(GL.TEXTURE0);

    }
    public void DrawCabinGlass(IEnumerable<CityCar> cars,Vec3 eye,Mat4 view,Mat4 projection,Vec3 sun,Vec3 light,Mat4 ls,uint shadow,OutdoorLighting lighting)
    {
        var s=_shader;s.Use();s.SetMatrix4("uView",view);s.SetMatrix4("uProj",projection);s.SetVec3("uViewPos",eye);
        s.SetInt("uColorMap",0);s.SetInt("uNormalMap",1);s.SetInt("uFinishMap",2);
        Materials.ApplyLight(s,sun,light,.52f,.8f,.25f);CityLighting.Apply(lighting,s);s.SetInt("uLocalLighting",1);
        if(Reflections is not null)Reflections.Apply(s);else{s.SetInt("uProbe",12);s.SetInt("uProbeReady",0);}
        s.SetMatrix4("uLightSpace",ls);s.SetInt("uShadowMap",6);s.SetFloat("uShadowStrength",0);
        var visible=new CityVisibility(projection*view);bool cull=GL.IsEnabled(GL.CULL_FACE),blend=GL.IsEnabled(GL.BLEND);
        bool depthWrite=GL.GetInteger(0x0B72)!=0;
        uint blendSource=(uint)GL.GetInteger(0x80C9),blendDestination=(uint)GL.GetInteger(0x80C8);
        GL.Enable(GL.BLEND);GL.BlendFunc(GL.SRC_ALPHA,GL.ONE_MINUS_SRC_ALPHA);GL.DepthMask(false);GL.Disable(GL.CULL_FACE);
        try{
        foreach(var car in cars.Where(c=>c.Kind is 9 or 10&&!c.Destroyed&&(Position(c)-eye).Length()<103).OrderByDescending(c=>(Position(c)-eye).Length())){
            if(!_assets.TryGetValue(CityFleet.Model(car)??"",out var a)||!visible.Sphere(Position(car)+new Vec3(0,1.5f,0),(float)car.HalfLength+2))continue;
            var lod=CityVehicleLod.Select((Position(car)-eye).Length());
            Render(a,Transform(car,a),lod.First,car,false,lod.Blend,0,true);
            if(lod.First!=lod.Second&&lod.Blend>0)Render(a,Transform(car,a),lod.Second,car,false,lod.Blend,1,true);
        }
        }finally{
            GL.DepthMask(depthWrite);GL.BlendFunc(blendSource,blendDestination);
            if(cull)GL.Enable(GL.CULL_FACE);if(!blend)GL.Disable(GL.BLEND);GL.ActiveTexture(GL.TEXTURE0);
        }
    }
    public void Depth(IEnumerable<CityCar> cars,Vec3 eye,Mat4 ls,float range)
    {
        _depth.Use();_depth.SetMatrix4("uLightSpace",ls);var visible=new CityVisibility(ls);
        foreach(var c in cars)if(Replaces(c,eye,range)&&visible.Sphere(Position(c)+new Vec3(0,1,0),(float)c.HalfLength+2)){var a=_assets[CityFleet.Model(c)!];Render(a,Transform(c,a),1,c,true);}
        foreach(var site in CityImportedSites.All())if(_assets.TryGetValue(site.Model,out var a)){
            Vec3 p=new((float)site.Position.X,(float)site.Ground,(float)site.Position.Z);if((p-eye).Length()>100||!visible.Sphere(p+new Vec3(0,site.Height*.5f,0),site.Radius))continue;
            Render(a,Mat4.Translate(p)*Mat4.RotateY(site.Angle)*Mat4.Scale(site.Width/a.Size.X,site.Height/a.Size.Y,site.Length/a.Size.Z),1,null,true);}
    }
    public void Dispose(){foreach(var a in _assets.Values)a.Dispose();_assets.Clear();Available.Clear();_shader.Dispose();_depth.Dispose();_travel.Clear();}
}
