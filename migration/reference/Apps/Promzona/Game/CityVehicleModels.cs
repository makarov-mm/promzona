using MakarovPhysics.Renderer;
using System.Text.Json;
using System.Runtime.CompilerServices;

namespace Promzona.Game;

/// <summary>Uses the existing shared GLB importer; no second mesh parser.
/// Geometry follows the same physical body, support height and LOD as procedural cars.</summary>
internal sealed class CityVehicleModels : IDisposable
{
    private readonly Dictionary<int, GltfModel> _models = [];
    private Shader? _shader, _depth;
    private sealed record Wheel(Vec3 Pivot,float Radius,bool Front);
    private readonly Dictionary<int,Wheel[]> _wheels=[];
    private readonly ConditionalWeakTable<CityCar,CityWheelMotion> _spin=new();
    private static readonly string[] WheelNames=Enumerable.Range(0,6).Select(i=>$"uWheels[{i}]").ToArray();
    private readonly Dictionary<int,List<(GltfModel Model,Vec3 Pivot,int Side)>> _doors=[];
    private readonly Dictionary<int, Texture> _maps = [];
    public int Count => _models.Count;
    public CityReflections? Reflections;
    private bool _texturedSedan;
    private readonly CityImportedModels _imported;
    private Texture? _sedanFinish, _sedanMask, _glassFinish, _glassColor;
    private readonly List<(GltfModel Model,Vec3 Pivot,int Side)> _sedanGlass=[];
    public bool HasTexturedSedan => _texturedSedan;

    public CityVehicleModels(string root)
    {
        string[] names = ["sedan", "van", "truck", "police", "ambulance", "bus", "sedan2", "suv", "bobik", "guardvan"];

        for (int i = 0; i < names.Length; i++)
        {
            string p = Path.Combine(root,"Models","Articulated",names[i]+"-body.glb");

            if (!File.Exists(p))
            {
                p = Path.Combine(root, "Models", names[i] + ".glb");
            }

            if (!File.Exists(p)) continue;
            GltfModel? m = null;

            try
            {
                m = GltfModel.Load(p);
                Vec3 span = m.Max - m.Min;

                if (span.X < .01 || span.Y < .01 || span.Z < .01)
                {
                    throw new InvalidDataException("Degenerate vehicle bounds");
                }

                _models[i] = m;
                string metadata=Path.Combine(root,"Models","Articulated","manifest.json");

                if (File.Exists(metadata))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(metadata));

                    if (doc.RootElement.TryGetProperty(names[i], out var entry))
                    {
                        if(entry.TryGetProperty("wheels",out var wheels))_wheels[i]=wheels.EnumerateArray().Select(w=>new Wheel(new Vec3(w.GetProperty("pivot")[0].GetSingle(),w.GetProperty("pivot")[1].GetSingle(),w.GetProperty("pivot")[2].GetSingle()),w.GetProperty("radius").GetSingle(),w.GetProperty("front").GetBoolean())).ToArray();
                        for (int side = 0; side < 2; side++)
                        {
                            string door = Path.Combine(root, "Models", "Articulated", names[i] + (side == 0 ? "-left.glb" : "-right.glb"));
                            if (!File.Exists(door)) continue;
                            var pivot = entry.GetProperty("hinges")[side];
                            if (!_doors.TryGetValue(i, out var parts)) _doors[i] = parts = [];
                            parts.Add((GltfModel.Load(door), new Vec3(pivot[0].GetSingle(), pivot[1].GetSingle(), pivot[2].GetSingle()), side == 0 ? -1 : 1));
                        }
                    }
                }

                string map = Path.Combine(root, "Textures", "Vehicles", names[i] + ".png");

                if (File.Exists(map))
                {
                    using var image = new Bitmap(map);
                    _maps[i] = new Texture(image, false);
                }
            }
            catch (Exception e)
            {
                m?.Dispose();
                _models.Remove(i);
                if (_maps.Remove(i, out var failedMap)) failedMap.Dispose();
                // A door that loaded before the failure must not outlive its body.
                if (_doors.Remove(i, out var failedDoors))
                {
                    foreach ((GltfModel Model, Vec3 Pivot, int Side) door in failedDoors) door.Model.Dispose();
                }

                System.Diagnostics.Trace.WriteLine($"Vehicle {p}: {e.Message}; procedural fallback");
            }
        }

        // Inside the guard: a failure in the imported-asset loader or the shaders
        // must release the bodies, doors and maps already on the GPU (Dispose
        // tolerates the half-built state), or every retry from the menu leaked them.
        try
        {
            LoadTexturedSedan(root);
            _imported = new(root);
            _shader = new Shader(CityShaderSource.ReadGeometry("Vehicle.vert"), CityShaderSource.ReadGeometry("Vehicle.frag"));
            _depth = new Shader(CityShaderSource.ReadGeometry("VehicleDepth.vert"), DefaultShaders.DepthFragment);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void LoadTexturedSedan(string root)
    {
        string folder=Path.Combine(root,"Models","VAZ2101");
        if(!File.Exists(Path.Combine(folder,"manifest.json")))return;
        var loaded=new List<GltfModel>();var textures=new List<Texture>();
        try
        {
            GltfModel Model(string name){var model=GltfModel.Load(Path.Combine(folder,name+".glb"));loaded.Add(model);return model;}
            Texture Map(string name){using var bitmap=new Bitmap(Path.Combine(folder,name+".png"));var texture=new Texture(bitmap);textures.Add(texture);return texture;}
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"manifest.json")));
            static Vec3 Point(JsonElement v)=>new(v[0].GetSingle(),v[1].GetSingle(),v[2].GetSingle());
            var metadata=doc.RootElement;
            var body=Model("body");var preparedDoors=new List<(GltfModel Model,Vec3 Pivot,int Side)>();
            var glass=new List<(GltfModel Model,Vec3 Pivot,int Side)>{(Model("glass"),Vec3.Zero,0)};
            for(int i=0;i<2;i++){string side=i==0?"left":"right";var pivot=Point(metadata.GetProperty("hinges")[i]);preparedDoors.Add((Model(side),pivot,i==0?-1:1));glass.Add((Model("glass-"+side),pivot,i==0?-1:1));}
            var wheels=metadata.GetProperty("wheels").EnumerateArray().Select(w=>new Wheel(Point(w.GetProperty("pivot")),w.GetProperty("radius").GetSingle(),w.GetProperty("front").GetBoolean())).ToArray();
            var finish=Map("body-finish");var mask=Map("paint-mask");var glassFinish=Map("glass-finish");
            var bodyColor=Map("body-color");var glassColor=Map("glass-color");
            // Commit only after every mesh and texture loaded, so damaged/missing
            // optional content keeps the complete original vehicle as a fallback.
            if(_models.Remove(0,out var oldBody))oldBody.Dispose();
            if(_doors.Remove(0,out var oldDoors))foreach(var door in oldDoors)door.Model.Dispose();
            if(_maps.Remove(0,out var oldMap))oldMap.Dispose();
            _models[0]=body;_doors[0]=preparedDoors;_wheels[0]=wheels;_sedanGlass.AddRange(glass);
            _sedanFinish=finish;_sedanMask=mask;_glassFinish=glassFinish;_glassColor=glassColor;_maps[0]=bodyColor;_texturedSedan=true;
        }
        catch(Exception e)
        {
            foreach(var model in loaded)model.Dispose();foreach(var texture in textures)texture.Dispose();
            System.Diagnostics.Trace.WriteLine($"VAZ 2101: {e.Message}; original sedan fallback");
        }
    }

    public float VisibleDistance = CityLod.Vehicle;

    public bool Replaces(CityCar car, Vec3 eye) => _imported.Replaces(car,eye,VisibleDistance) || BuiltInReplaces(car,eye);

    // Distance first: the model-key and dictionary lookups ran for all ~2 200 cars
    // several times a frame before the few dozen in range were even selected.
    private bool BuiltInReplaces(CityCar car, Vec3 eye) =>
        (eye - new Vec3((float)car.Position.X, (float)car.SurfaceHeight, (float)car.Position.Z)).Length() < Math.Min(CityLod.Vehicle,VisibleDistance) &&
        _models.ContainsKey(ModelKey(car)) &&
        !_imported.Replaces(car,eye,VisibleDistance);

    // Built-in body sets: 0 sedan, 1 van, 2 truck, 3 police, 4 ambulance, 5 bus,
    // 6 sedan2, 7 suv, 8 bobik, 9 guardvan. Kinds 9 and up (tram, trolleybus, tank,
    // helicopter, fsb, btr) are imported or procedural only: the tram (kind 9)
    // used to fall through to key 9 and wear the guard van whenever its imported
    // model was missing.
    private static int ModelKey(CityCar car) => car.Kind switch
    {
        8 => 1,
        6 => 8,
        7 => 9,
        0 when car.Id != 1 && car.Id % 3 != 0 => car.Id % 2 == 0 ? 6 : 7,
        >= 9 => -1,
        _ => car.Kind
    };

    private static Mat4 Transform(CityCar c, GltfModel m)
    {
        float roll = (float)(c.Roll + c.TerrainRoll), pitch = (float)(c.Pitch + c.TerrainPitch);
        float support = MathF.Abs(MathF.Sin((float)c.Roll)) * (float)c.HalfWidth +
                        MathF.Abs(MathF.Sin((float)c.Pitch)) * (float)c.HalfLength +
                        MathF.Abs(MathF.Cos((float)c.Roll) * MathF.Cos((float)c.Pitch)) * .7f;

        Vec3 span = m.Max - m.Min;

        return Mat4.Translate(new Vec3((float)c.Position.X, (float)(c.SurfaceHeight + c.Lift) + MathF.Max(.7f, support), (float)c.Position.Z)) *
               Mat4.RotateY((float)c.Heading) * Mat4.RotateX(-pitch) * Mat4.RotateZ(-roll) *
               Mat4.Translate(new Vec3(0, -.7f, 0)) *
               Mat4.Scale((float)c.HalfWidth * 2 / span.X, (float)c.Height / span.Y, (float)c.HalfLength * 2 / span.Z) *
               Mat4.Translate(new Vec3(-m.Centre.X, -m.Min.Y, -m.Centre.Z));
    }

    private static Mat4 DoorTransform(CityCar c, GltfModel m, Vec3 pivot, int side)
    {
        // World Right is local -X; the selected exit therefore opens the opposite mesh side.
        float angle = (float)(c.DoorOpen * (c.CrewDeployed || side == -c.DoorSide ? 1 : 0) * 1.05 * -side);
        return Transform(c, m) * Mat4.Translate(pivot) * Mat4.RotateY(angle) * Mat4.Translate(pivot * -1);
    }

    private void WheelUniforms(Shader shader,CityCar car,GltfModel body) {
        shader.SetInt("uTextured",_texturedSedan&&ModelKey(car)==0?1:0);
        // Both passes use the same signed accumulated distance, independent of render rate.
        double angle=_spin.GetOrCreateValue(car).Sample(car.Distance,car.Speed);
        shader.SetFloat("uWheelTravel",(float)(angle%10000));shader.SetFloat("uSteer",(float)car.Steering);
        shader.SetVec3("uVehicleScale",new Vec3((float)(car.HalfWidth*2)/(body.Max.X-body.Min.X),(float)car.Height/(body.Max.Y-body.Min.Y),(float)(car.HalfLength*2)/(body.Max.Z-body.Min.Z)));
        // The shader declares six wheel slots; a manifest with more must not index past them.
        var wheels=_wheels.GetValueOrDefault(ModelKey(car));int wheelCount=Math.Min(wheels?.Length??0,WheelNames.Length);shader.SetInt("uWheelCount",wheelCount);
        for(int i=0;i<wheelCount;i++){var w=wheels![i];shader.SetVec4(WheelNames[i],w.Pivot.X,w.Pivot.Y,w.Pivot.Z,w.Front?w.Radius:-w.Radius);}
    }

    public void Depth(IEnumerable<CityCar> cars, Vec3 eye, Mat4 ls)
    {
        if (_depth is null) return;
        _imported.Depth(cars,eye,ls,VisibleDistance);
        _depth.Use();
        _depth.SetMatrix4("uLightSpace", ls);
        var visibility = new CityVisibility(ls);

        foreach (CityCar car in cars)
        {
            if (BuiltInReplaces(car, eye) && visibility.Sphere(new((float)car.Position.X, (float)(car.SurfaceHeight + car.Lift) + 1, (float)car.Position.Z), (float)car.HalfLength + 2))
            {
                GltfModel gltfModel = _models[ModelKey(car)];
                WheelUniforms(_depth,car,gltfModel);
                _depth.SetMatrix4("uModel", Transform(car, gltfModel));
                gltfModel.Mesh.Draw();

                if (_doors.TryGetValue(ModelKey(car), out List<(GltfModel Model, Vec3 Pivot, int Side)>? doors))
                {
                    foreach ((GltfModel Model, Vec3 Pivot, int Side) door in doors)
                    {
                        _depth.SetMatrix4("uModel", DoorTransform(car, gltfModel, door.Pivot, door.Side)); door.Model.Mesh.Draw();
                    }
                }
            }
        }
    }

    public void DrawCabinGlass(IEnumerable<CityCar> cars,Vec3 eye,Mat4 view,Mat4 projection,Vec3 sun,Vec3 light,Mat4 ls,uint shadow,OutdoorLighting lighting)
        =>_imported.DrawCabinGlass(cars,eye,view,projection,sun,light,ls,shadow,lighting);
    public void Draw(IEnumerable<CityCar> cars, Vec3 eye, Mat4 view, Mat4 proj, Vec3 sun, Vec3 light, Mat4 ls, uint shadow, OutdoorLighting lighting, float time)
    {
        if (_shader is null) return;
        _imported.Reflections=Reflections;_imported.Draw(cars,eye,view,proj,sun,light,ls,shadow,lighting,VisibleDistance);
        Shader s = _shader;
        s.Use();
        s.SetMatrix4("uView", view);
        s.SetMatrix4("uProj", proj);
        s.SetVec3("uViewPos", eye);
        Materials.ApplyLight(s, sun, light, .52f, .8f, .25f);
        CityLighting.Apply(lighting, s);
        if(Reflections is not null)Reflections.Apply(s);else {s.SetInt("uProbe",12);s.SetInt("uProbeReady",0);}
        s.SetInt("uLocalLighting", 1);
        s.SetInt("uTexture", 0);
        s.SetInt("uFinish", 1);s.SetInt("uPaintMask",2);s.SetInt("uGlass",0);
        _sedanMask?.Bind(2);GL.ActiveTexture(GL.TEXTURE0);
        s.SetFloat("uTime", time);
        s.SetMatrix4("uLightSpace", ls);
        s.SetFloat("uShadowStrength", shadow == 0 ? 0 : .7f*(1-.72f*CityLighting.Atmosphere.Rain));
        s.SetInt("uShadowMap", 6);
        GL.ActiveTexture(GL.TEXTURE0 + 6);
        GL.BindTexture(GL.TEXTURE_2D, shadow);
        GL.ActiveTexture(GL.TEXTURE0);

        var visibility = new CityVisibility(proj * view);

        foreach (CityCar c in cars)
        {
            if (BuiltInReplaces(c, eye) && visibility.Sphere(new Vec3((float)c.Position.X, (float)(c.SurfaceHeight + c.Lift) + 1, (float)c.Position.Z), (float)c.HalfLength + 2))
            {
                GltfModel m = _models[ModelKey(c)];
                WheelUniforms(s,c,m);
                s.SetVec3("uBodyMin", m.Min);
                s.SetVec3("uBodySpan", m.Max - m.Min);
                if(_texturedSedan&&ModelKey(c)==0){_sedanFinish?.Bind(1);GL.ActiveTexture(GL.TEXTURE0);}
                Texture? map = _maps.GetValueOrDefault(ModelKey(c)) ?? m.BaseColor;
                map?.Bind();
                s.SetInt("uHasMap", map is null ? 0 : 1);
                (float R, float G, float B) paint = CityAppearance.Paint(c);
                s.SetVec3("uPaint", new Vec3(paint.R, paint.G, paint.B));
                s.SetInt("uBodySlot", CityAppearance.BodySlot(c.Kind));
                s.SetInt("uPaletteWidth", CityAppearance.PaletteWidth(c.Kind));
                s.SetFloat("uDamage", c.Destroyed ? 2 : (float)c.Damage);
                s.SetInt("uKind", c.Kind);
                s.SetInt("uBraking", c.Braking ? 1 : 0);
                s.SetMatrix4("uModel", Transform(c, m));
                m.Mesh.Draw();

                if (_doors.TryGetValue(ModelKey(c), out List<(GltfModel Model, Vec3 Pivot, int Side)>? doors))
                {
                    foreach ((GltfModel Model, Vec3 Pivot, int Side) door in doors)
                    {
                        s.SetMatrix4("uModel", DoorTransform(c, m, door.Pivot, door.Side)); door.Model.Mesh.Draw();
                    }
                }
            }
        }
        if(_texturedSedan)
        {
            bool blend=GL.IsEnabled(GL.BLEND),cull=GL.IsEnabled(GL.CULL_FACE);
            const uint depthWriteMask=0x0B72,blendSrcRgb=0x80C9,blendDstRgb=0x80C8;
            int depthWrite=GL.GetInteger(depthWriteMask),src=GL.GetInteger(blendSrcRgb),dst=GL.GetInteger(blendDstRgb);
            try
            {
                GL.Enable(GL.BLEND);GL.BlendFunc(GL.SRC_ALPHA,GL.ONE_MINUS_SRC_ALPHA);GL.DepthMask(false);GL.Disable(GL.CULL_FACE);
                s.SetInt("uTextured",1);s.SetInt("uGlass",1);s.SetInt("uHasMap",1);s.SetInt("uWheelCount",0);_glassFinish?.Bind(1);GL.ActiveTexture(GL.TEXTURE0);
                foreach(var c in cars)
                {
                    if(ModelKey(c)!=0||!BuiltInReplaces(c,eye)||!visibility.Sphere(new((float)c.Position.X,(float)(c.SurfaceHeight+c.Lift)+1,(float)c.Position.Z),(float)c.HalfLength+2))continue;
                    var body=_models[0];s.SetVec3("uBodyMin",body.Min);s.SetVec3("uBodySpan",body.Max-body.Min);s.SetFloat("uDamage",c.Destroyed?2:(float)c.Damage);
                    _glassColor?.Bind();
                    foreach(var part in _sedanGlass){s.SetMatrix4("uModel",part.Side==0?Transform(c,body):DoorTransform(c,body,part.Pivot,part.Side));part.Model.Mesh.Draw();}
                }
            }
            finally
            {
                s.SetInt("uGlass",0);GL.DepthMask(depthWrite!=0);GL.BlendFunc((uint)src,(uint)dst);
                if(!blend)GL.Disable(GL.BLEND);if(cull)GL.Enable(GL.CULL_FACE);GL.ActiveTexture(GL.TEXTURE0);
            }
        }
    }

    public void Dispose()
    {
        _imported?.Dispose();
        // Dispose every door of every vehicle first, then clear. The previous
        // form cleared the dictionary inside the loop, so only the doors of the
        // first vehicle type were ever released: every New game / Load leaked
        // the remaining door meshes on the GPU.
        foreach ((GltfModel Model, Vec3 Pivot, int Side) p in _doors.Values.SelectMany(parts => parts))
        {
            p.Model.Dispose();
        }

        _doors.Clear();
        foreach(var pane in _sedanGlass)pane.Model.Dispose();_sedanGlass.Clear();
        _sedanFinish?.Dispose();_sedanMask?.Dispose();_glassFinish?.Dispose();_glassColor?.Dispose();
        _sedanFinish=_sedanMask=_glassFinish=_glassColor=null;_texturedSedan=false;

        foreach (Texture map in _maps.Values)
        {
            map.Dispose();
        }

        _maps.Clear();

        foreach (GltfModel m in _models.Values)
        {
            m.Dispose();
        }

        _models.Clear();
        _shader?.Dispose();
        _shader = null;
        _depth?.Dispose();
        _depth = null;
    }
}
