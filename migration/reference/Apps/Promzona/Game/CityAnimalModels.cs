using System.Text.Json;
using System.Runtime.CompilerServices;
using MakarovPhysics.Renderer;

namespace Promzona.Game;

/// <summary>Imported animals share meshes, textures and sampled GPU joint palettes.
/// The AI, collisions and saves continue to use the existing animal identities.</summary>
internal sealed class CityAnimalModels : IDisposable
{
    private sealed record Part(CitySkin Skin,int Material);
    private sealed record AnimalMaterial(Vec3 Color,float Roughness,float NormalStrength,Texture? ColorMap,Texture? NormalMap,Texture? FinishMap);
    private sealed class Asset : IDisposable
    {
        public required Part[][] Lods;
        public required AnimalMaterial[] Materials;
        public required Dictionary<string,Texture> Maps;
        public required float IdleSeconds,WalkStride,RunStride;
        public readonly Batch[] Batches=[new(),new(),new()];
        public void Dispose(){foreach(var lod in Lods)foreach(var p in lod)p.Skin.Dispose();foreach(var map in Maps.Values)map.Dispose();}
    }
    private readonly Dictionary<string,Asset> _assets=[];
    private readonly Shader _color=new(CityShaderSource.ReadGeometry("Animal.vert"),CityShaderSource.ReadGeometry("Animal.frag")),_depth=new(CityShaderSource.ReadGeometry("AnimalDepth.vert"),DefaultShaders.DepthFragment);
    public float VisibleDistance=CityLod.Actor;
    public int Count=>_assets.Count;
    public CityAnimalModels(string root)
    {
        string folder=Path.Combine(root,"Models","AnimalsSkinned");
        if(!Directory.Exists(folder))return;
        foreach(string dir in Directory.EnumerateDirectories(folder))
        {
            var skins=new List<CitySkin>();var maps=new Dictionary<string,Texture>();
            try{
                using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"manifest.json")));var data=doc.RootElement;
                var lods=data.GetProperty("lods").EnumerateArray().Select(l=>l.EnumerateArray().Select(p=>{
                    var skin=new CitySkin(Path.Combine(dir,p.GetProperty("file").GetString()!));skins.Add(skin);
                    return new Part(skin,p.GetProperty("material").GetInt32());}).ToArray()).ToArray();
                if(lods.Length!=3||lods.Any(l=>l.Length==0))throw new InvalidDataException("Animal needs three LODs");
                foreach(string file in Directory.EnumerateFiles(dir,"*.png")){using var b=new Bitmap(file);var t=new Texture(b);t.SetWrapRepeat();t.SetAnisotropy(4);maps.Add(Path.GetFileName(file),t);}
                var materials=data.GetProperty("materials").EnumerateArray().Select(m=>{
                    Texture? Map(string key)=>m.TryGetProperty(key,out var p)&&maps.TryGetValue(p.GetString()!,out var t)?t:null;
                    var c=m.GetProperty("color");return new AnimalMaterial(new(c[0].GetSingle(),c[1].GetSingle(),c[2].GetSingle()),m.GetProperty("roughness").GetSingle(),m.GetProperty("normalStrength").GetSingle(),Map("colorMap"),Map("normalMap"),Map("finishMap"));}).ToArray();
                if(lods.SelectMany(l=>l).Any(p=>p.Material<0||p.Material>=materials.Length))throw new InvalidDataException("Invalid animal material");
                var clips=data.GetProperty("clips");_assets.Add(data.GetProperty("name").GetString()!,new Asset{Lods=lods,Materials=materials,Maps=maps,
                    IdleSeconds=clips.GetProperty("idle").GetProperty("seconds").GetSingle(),WalkStride=clips.GetProperty("walk").GetProperty("stride").GetSingle(),RunStride=clips.GetProperty("run").GetProperty("stride").GetSingle()});
            }catch(Exception ex){foreach(var skin in skins)skin.Dispose();foreach(var map in maps.Values)map.Dispose();System.Diagnostics.Trace.WriteLine($"Animal {dir}: {ex.Message}; fallback retained");}
        }
    }
    private sealed class Batch
    {
        public float[] Data=new float[24*16];
        public int Count;
        public void Add(Mat4 model,(float From,float To,float Blend) pose,Vec3 tint)
        {
            if((Count+1)*24>Data.Length)Array.Resize(ref Data,Data.Length*2);
            int offset=Count++*24;
            for(int column=0;column<4;column++)for(int row=0;row<4;row++)Data[offset+column*4+row]=model[row,column];
            Data[offset+16]=pose.From;Data[offset+17]=pose.To;Data[offset+18]=pose.Blend;Data[offset+19]=0;
            Data[offset+20]=tint.X;Data[offset+21]=tint.Y;Data[offset+22]=tint.Z;Data[offset+23]=1;
        }
    }
    private sealed class Motion
    {
        public readonly CityAnimalMotion Animation=new();
        public double Time=double.NaN;
        public (CityPoint Point,double Height,double Heading,double Distance,double Fear,double Knocked,bool Dead,bool Flying) Actor;
        public Vec3 Position;
        public Mat4 Matrix;
        public (float From,float To,float Blend) Pose;
    }
    private readonly ConditionalWeakTable<CityAnimal,Motion> _motion=new();
    public int LastDrawCalls {get;private set;}
    public int LastVisibleAnimals {get;private set;}
    internal static string Model(CityAnimal a)=>a.Kind switch{AnimalKind.Cat=>a.Variant%2==0?"cat-calico":"cat-ginger",AnimalKind.Dog=>a.Variant%3==0?"puppy":"labrador",_=>"pigeon"};
    public bool Replaces(CityAnimal a,Vec3 eye)=>_assets.ContainsKey(Model(a))&&CityLod.InHorizontalRange(a.Position.X,a.Position.Z,eye.X,eye.Z,Math.Min(CityLod.Actor,VisibleDistance));
    private Motion Prepare(CityAnimal a,Asset asset,double time)
    {
        var state=_motion.GetOrCreateValue(a);
        var actor=(a.Position,a.Height,a.Heading,a.Distance,a.Fear,a.KnockedFor,a.Dead,a.Flying);
        if(state.Time==time&&state.Actor==actor)return state;
        state.Time=time;state.Actor=actor;
        float clearance=CityRoadSurface.Contains(a.Position)?.025f:.16f;
        state.Position=new((float)a.Position.X,(float)(CityTerrain.Ground(a.Position)+a.Height)+clearance,(float)a.Position.Z);
        state.Pose=state.Animation.Sample(a,time,asset.IdleSeconds,asset.WalkStride,asset.RunStride);
        bool airborne=a.Flying||a.Height>.08||a.Dead||a.KnockedFor>0;
        var slope=airborne?default:CityTerrain.Current.Gradient(a.Position.X,a.Position.Z,a.Kind==AnimalKind.Dog?.4:.25);
        state.Animation.Ground(slope.X,slope.Z,airborne);
        float tilt=a.Dead?1.5f:(float)Math.Clamp(a.KnockedFor,0,1)*1.2f;
        float size=a.Kind==AnimalKind.Bird?.72f:.94f+(a.Variant%5)*.03f;
        state.Matrix=Mat4.Translate(state.Position+new Vec3(0,MathF.Sin(tilt)*.12f,0))*
            Mat4.RotateY(state.Animation.Heading)*Mat4.RotateX(state.Animation.Pitch+state.Animation.FlightPitch)*
            Mat4.RotateZ(state.Animation.Roll+tilt)*Mat4.Scale(size,size,size);
        return state;
    }

    private void Render(CityTraffic traffic, Vec3 eye, Mat4 matrix, bool depth)
    {
        var visible = new CityVisibility(matrix);
        Shader shader = depth ? _depth : _color;
        float range = Math.Min(CityLod.Actor, VisibleDistance);
        foreach (var asset in _assets.Values)
        foreach (var batch in asset.Batches)
            batch.Count = 0;
        if (!depth)
        {
            LastDrawCalls = 0;
            LastVisibleAnimals = 0;
        }

        foreach (var animal in traffic.Animals)
        {
            // Reject distant agents before any terrain sampling, animation or matrix work.
            if (!CityLod.InHorizontalRange(animal.Position.X, animal.Position.Z, eye.X, eye.Z, range) ||
                !_assets.TryGetValue(Model(animal), out var asset)) continue;
            var state = Prepare(animal, asset, traffic.Time);
            float distance = (state.Position - eye).Length();
            if (distance >= range || !visible.Sphere(state.Position + new Vec3(0, .35f, 0), 1.3f)) continue;
            int lod = distance < 18 ? 0 : distance < 48 ? 1 : 2;
            if (depth) lod = Math.Max(1, lod);
            asset.Batches[lod].Add(state.Matrix, state.Pose,
                animal.Kind == AnimalKind.Bird ? new(.70f, .59f, .49f) : new(1, 1, 1));
            if (!depth) LastVisibleAnimals++;
        }

        bool cull = GL.IsEnabled(GL.CULL_FACE);
        GL.Disable(GL.CULL_FACE);
        foreach (var asset in _assets.Values)
            for (int lod = 0; lod < 3; lod++)
            {
                var batch = asset.Batches[lod];
                if (batch.Count == 0) continue;
                foreach (var part in asset.Lods[lod])
                {
                    if (!depth)
                    {
                        BindMaterial(shader, asset.Materials[part.Material]);
                        LastDrawCalls++;
                    }

                    part.Skin.DrawInstances(shader, batch.Data, batch.Count);
                }
            }

        if (cull) GL.Enable(GL.CULL_FACE);
        GL.ActiveTexture(GL.TEXTURE0);
    }

    // The matching program is already bound by Render/PrepareResources.
    private static void BindMaterial(Shader shader, AnimalMaterial material)
    {
        shader.SetVec3("uBase", material.Color);
        shader.SetFloat("uRoughness", material.Roughness);
        shader.SetFloat("uNormalStrength", material.NormalStrength);
        Map(material.ColorMap, 0, "uHasColor");
        Map(material.NormalMap, 1, "uHasNormal");
        Map(material.FinishMap, 2, "uHasFinish");
        return;

        void Map(Texture? texture, uint unit, string flag)
        {
            shader.SetInt(flag, texture is null ? 0 : 1);
            texture?.Bind(unit);
        }
    }

    internal IEnumerable<Action<bool>> PreparationSteps()
    {
        // Every material/LOD is exercised, even if no actor of that kind is initially nearby.
        // Uploading a mesh and linking its shader alone do not exercise the driver's draw path.
        return from asset in _assets.Values from lod in asset.Lods select (Action<bool>)(depth =>
        {
            if (depth)
            {
                _depth.Use();
                _depth.SetMatrix4("uLightSpace", CityGpuPreparation.Projection * CityGpuPreparation.View);
            }
            else
            {
                BindColor(CityGpuPreparation.Eye, CityGpuPreparation.View, CityGpuPreparation.Projection, new Vec3(0, 1, 0), new Vec3(1, 1, 1), Mat4.Identity, 0, new OutdoorLighting());
            }

            Shader shader = depth ? _depth : _color;

            foreach (var part in lod)
            {
                if (!depth)
                {
                    BindMaterial(shader, asset.Materials[part.Material]);
                }

                part.Skin.DrawInstances(shader, CityGpuPreparation.Instance, 1);
            }
        });
    }

    public void Depth(CityTraffic traffic, Vec3 eye, Mat4 ls)
    {
        _depth.Use();
        _depth.SetMatrix4("uLightSpace", ls);
        Render(traffic, eye, ls, true);
    }

    public void Draw(CityTraffic traffic,Vec3 eye,Mat4 view,Mat4 proj,Vec3 sun,Vec3 light,Mat4 ls,uint shadow,OutdoorLighting lighting)
    {
        BindColor(eye,view,proj,sun,light,ls,shadow,lighting);
        Render(traffic,eye,proj*view,false);
    }

    private void BindColor(Vec3 eye, Mat4 view, Mat4 proj, Vec3 sun, Vec3 light, Mat4 ls, uint shadow, OutdoorLighting lighting)
    {
        Shader s = _color;
        s.Use();
        s.SetMatrix4("uView", view);
        s.SetMatrix4("uProj", proj);
        s.SetVec3("uViewPos", eye);
        Materials.ApplyLight(s, sun, light, .52f, .8f, .2f);
        CityLighting.Apply(lighting, s);
        s.SetInt("uLocalLighting", 1);
        s.SetInt("uColorMap", 0);
        s.SetInt("uNormalMap", 1);
        s.SetInt("uFinishMap", 2);
        s.SetMatrix4("uLightSpace", ls);
        s.SetInt("uShadowMap", 6);
        s.SetFloat("uShadowStrength", shadow == 0 ? 0 : .65f * (1 - .72f * CityLighting.Atmosphere.Rain));
        GL.ActiveTexture(GL.TEXTURE0 + 6);
        GL.BindTexture(GL.TEXTURE_2D, shadow);
    }

    public void Dispose()
    {
        foreach (Asset asset in _assets.Values)
        {
            asset.Dispose();
        }

        _assets.Clear();
        _color.Dispose();
        _depth.Dispose();
    }
}
