using System.Text.Json;
using MakarovPhysics.Renderer;

namespace Promzona.Game;

/// <summary>GPU-skinned people and rigid animal GLBs. Editable clothing palettes remain external.</summary>
internal sealed class CityActorModels : IDisposable
{
    private sealed record PersonAsset(CitySkin Skin, Texture Texture);
    private sealed record Pose(GltfModel? Model, CitySkin? Skin, float Frame, Texture? Map, Mat4 Transform, Vec3 Tint, Vec3 Gait,float From=-1,float Blend=1,bool Cabin=false);
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<CityPedestrian,CityPersonMotion> _motion=new();
    private readonly List<PersonAsset> _people = [];
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<TransitRider,CityPedestrian> _riderModels=new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<CityCar,CityPedestrian> _drivers=new();
    private readonly Dictionary<AnimalKind, List<GltfModel>> _animals = [];
    private Shader? _shader, _depth;
    private CityAnimalModels? _wildlife;
    public int Count => _people.Count;
    public float VisibleDistance = CityLod.Actor;

    public CityActorModels(string root)
    {
        try
        {
            foreach (string name in new[] { "man", "woman" })
            {
                CitySkin? skin = null;
                Texture? texture = null;

                try
                {
                    skin = new CitySkin(Path.Combine(root, "Models", "PeopleSkinned", $"{name}.cityskin"));
                    using var bmp = new Bitmap(Path.Combine(root, "Textures", "People", name + "-palette.png"));
                    texture = new Texture(bmp, false);
                    _people.Add(new PersonAsset(skin, texture));
                }
                catch
                {
                    skin?.Dispose();
                    texture?.Dispose();
                    throw;
                }
            }

            string animalRoot = Path.Combine(root, "Models", "Animals");
            var animals = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(Path.Combine(animalRoot, "manifest.json")))!;

            foreach (KeyValuePair<string, string[]> entry in animals)
            {
                // An empty list would register the kind and divide by zero at the
                // first draw (Variant % Count); such a kind stays procedural.
                if (!Enum.TryParse<AnimalKind>(entry.Key, out var kind) || entry.Value.Length == 0) continue;
                List<GltfModel> list = [];
                _animals[kind] = list;
                list.AddRange(entry.Value.Select(filename => GltfModel.Load(Path.Combine(animalRoot, filename))));
            }

            _shader = new Shader(CityShaderSource.ReadGeometry("Actor.vert"), CityShaderSource.ReadGeometry("Actor.frag"));
            _depth = new Shader(CityShaderSource.ReadGeometry("ActorDepth.vert"), DefaultShaders.DepthFragment);
            _wildlife = new CityAnimalModels(root);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool Replaces(CityPedestrian p, Vec3 eye) =>
        _people.Count > 0
        && 
        CityLod.InHorizontalRange(p.Position.X, p.Position.Z, eye.X, eye.Z, Math.Min(CityLod.Actor,VisibleDistance))
        &&
        (eye - new Vec3((float)p.Position.X, (float)CityRoadLevels.Ground(p), (float)p.Position.Z)).Length() < Math.Min(CityLod.Actor, VisibleDistance);

    public bool Replaces(CityAnimal a, Vec3 eye) =>
        _animals.ContainsKey(a.Kind)
        &&
        CityLod.InHorizontalRange(a.Position.X, a.Position.Z, eye.X, eye.Z, Math.Min(CityLod.Actor, VisibleDistance))
        &&
        (eye - new Vec3((float)a.Position.X, (float)(CityTerrain.Ground(a.Position) + a.Height), (float)a.Position.Z)).Length() < Math.Min(CityLod.Actor, VisibleDistance);

    internal bool HeadSocket(CityPedestrian p,CityTraffic t,Vec3 eye,out Vec3 crown,out float heading)
    {
        crown=default;heading=(float)p.Heading;if(!Replaces(p,eye))return false;
        var asset=_people[p.Persona switch{7 or 8=>1,5 or 6 or 12 or 13=>0,_=>p.Id%2}];
        var motion=_motion.GetOrCreateValue(p);
        var animation=motion.Sample(p,t.Time,p.IsPlayer&&asset.Skin.PoseCount>=320?t.PunchPhase:0,p.IsPlayer&&t.EnteringCarId>=0?t.EntryProgress:0);
        float tilt=(p.Injured>0?.22f:0)+motion.Lean;
        Mat4 world=Mat4.Translate(new Vec3((float)p.Position.X,(float)(CityRoadLevels.Ground(p)+p.FlightHeight)+(p.ScooterId!=0?.287f:p.Crossing?.025f:.15f)+MathF.Sin(tilt)*.20f,(float)p.Position.Z))*
            Mat4.RotateY(motion.Heading)*Mat4.RotateX(tilt)*
            Mat4.Scale((float)p.ShoulderWidth/.51f,(float)p.Height/1.72f,.94f+p.Id%5*.035f);
        crown=world.TransformPoint(asset.Skin.Crown(animation.From,animation.To,animation.Blend));heading=motion.Heading;return true;
    }

    private IEnumerable<Pose> Poses(CityTraffic t, Vec3 eye, CityVisibility visibility)
    {
        foreach (CityPedestrian p in t.Pedestrians)
        {
            float range = Math.Min(CityLod.Actor, VisibleDistance);
            if (_people.Count == 0 || !CityLod.InHorizontalRange(p.Position.X,p.Position.Z,eye.X,eye.Z,range)) continue;
            float ground = (float)CityRoadLevels.Ground(p);
            var feet = new Vec3((float)p.Position.X, ground, (float)p.Position.Z);
            var offset = feet - eye;
            if (Vec3.Dot(offset,offset) < range*range && visibility.Sphere(feet+new Vec3(0,(float)p.FlightHeight+1,0),2.5f))
            {
                PersonAsset asset = _people[p.Persona switch
                {
                    7 or 8 => 1,
                    5 or 6 or 12 or 13 => 0,
                    _ => p.Id % 2
                }];

                // An injured survivor stands hunched forward; a hailer is upright with the arm drawn by the detail pass.
                float tilt = p.Dead ? MathF.PI * .5f : p.KnockedFor > 0 ? (float)Math.Clamp(p.KnockedFor, 0, 1) * 1.45f : p.Injured > 0 ? .22f : 0;

                var motion=_motion.GetOrCreateValue(p);
                var animation=motion.Sample(p,t.Time,p.IsPlayer&&asset.Skin.PoseCount>=320?t.PunchPhase:0,p.IsPlayer&&t.EnteringCarId>=0?t.EntryProgress:0);
                float frame=animation.To;tilt+=motion.Lean;

                float h = (float)p.Height / 1.72f, w = (float)p.ShoulderWidth / .51f;

                Mat4 world = Mat4.Translate(new Vec3((float)p.Position.X, ground+(float)p.FlightHeight + (p.ScooterId!=0?.287f:p.Crossing ? .025f : .15f) + MathF.Sin(tilt) * .20f, (float)p.Position.Z)) *
                             Mat4.RotateY(motion.Heading) * Mat4.RotateX(tilt) * Mat4.RotateZ(p is { Persona: 9, Dead: false } ? (float)Math.Sin(t.Time * 1.7 + p.Id) * .075f:0) *
                             Mat4.Scale(w, h, .94f + p.Id % 5 * .035f);

                // Gait.x/y encode stable clothing/skin choices; Gait.z is zero for
                // people and pitch-10 (below zero) while aiming, which the vertex
                // shader reads as "raise the arm".
                yield return new Pose(null, asset.Skin, frame, asset.Texture, world, new Vec3(1, 1, 1), new Vec3(p.Persona>=4?9+p.Persona-4:p.Id%9,p.Id%6,p.Aiming?(float)(p.IsPlayer?t.PlayerViewPitch:Math.Atan2(CityTerrain.Ground(t.PlayerPosition)-ground,Math.Max(1,(t.PlayerPosition-p.Position).Length)))-10:0),animation.From,animation.Blend);
            }
        }

        // Near cabins use the same saved civilians and skins as pedestrians.
        // The outer car rejection happens before rider restoration or skinning.
        foreach(var car in t.Cars){
            if(car.Kind is not (9 or 10)||car.Destroyed||car.Falling)continue;
            Vec3 position=new((float)car.Position.X,(float)(car.SurfaceHeight+car.Lift),(float)car.Position.Z);
            if((position-eye).Length()>CityTransitCabin.VisibleRange||!visibility.Sphere(position+new Vec3(0,1.5f,0),(float)car.HalfLength+2))continue;
            float support=MathF.Abs(MathF.Sin((float)car.Roll))*(float)car.HalfWidth+MathF.Abs(MathF.Sin((float)car.Pitch))*(float)car.HalfLength+MathF.Abs(MathF.Cos((float)car.Roll)*MathF.Cos((float)car.Pitch))*.7f;
            Mat4 cabin=Mat4.Translate(position+new Vec3(0,MathF.Max(.7f,support),0))*Mat4.RotateY((float)car.Heading)*
                Mat4.RotateX(-(float)(car.Pitch+car.TerrainPitch))*Mat4.RotateZ(-(float)(car.Roll+car.TerrainRoll))*Mat4.Translate(new(0,-.7f,0));
            Pose Seated(CityPedestrian person,CityTransitCabin.Seat seat,bool standing=false){
                var asset=_people[person.Persona switch{7 or 8=>1,5 or 6 or 12 or 13=>0,_=>person.Id%2}];
                var transform=cabin*Mat4.Translate(new((float)seat.X,(float)seat.Floor,(float)seat.Z))*Mat4.RotateY((float)seat.Heading)*
                    Mat4.Scale((float)person.ShoulderWidth/.51f,(float)person.Height/1.72f,.94f+person.Id%5*.035f);
                // The seated clip occupies poses 320-383; a skin baked without it
                // would clamp to 319, the last punch frame.
                return new(null,asset.Skin,standing||asset.Skin.PoseCount<384?0:320,asset.Texture,transform,new(1,1,1),new(person.Persona>=4?9+person.Persona-4:person.Id%9,person.Id%6,0),Cabin:true);
            }
            if(!car.Driverless)yield return Seated(t.PlayerControls(car)?t.Player:_drivers.GetValue(car,c=>new CityPedestrian{Id=c.Id}),CityTransitCabin.Driver(car.Kind));
            int seatIndex=0;
            foreach(var rider in t.TransitRiders){
                if(rider.CarId!=car.Id||seatIndex>=CityTransitCabin.Capacity)continue;
                var person=_riderModels.GetValue(rider,r=>{var p=new CityPedestrian();CityState.Restore(p,r.Person.State);return p;});
                yield return Seated(person,CityTransitCabin.At(car.Kind,seatIndex++));
            }
            if(t.PassengerMode&&t.Hero.Id==car.Id)
                yield return Seated(t.Player,seatIndex<CityTransitCabin.Capacity?CityTransitCabin.At(car.Kind,seatIndex):CityTransitCabin.Aisle(car.Kind),seatIndex>=CityTransitCabin.Capacity);
        }

        foreach (CityAnimal a in t.Animals)
        {
            if (_wildlife?.Replaces(a, eye) != true && Replaces(a, eye) && visibility.Sphere(new Vec3((float)a.Position.X, (float)(CityTerrain.Ground(a.Position) + a.Height) + 1, (float)a.Position.Z), 2.5f))
            {
                List<GltfModel> choices = _animals[a.Kind];
                GltfModel model = choices[a.Variant % choices.Count];

                float size = a.Kind switch
                {
                    AnimalKind.Dog => (a.Variant % 3 == 0 ? .76f : 1.10f),
                    AnimalKind.Bird => .58f,
                    _ => 1
                };

                Vec3 tint = (a.Variant % 6) switch
                {
                    0 => new Vec3(.32f, .30f, .28f),
                    1 => new Vec3(.9f, .52f, .24f),
                    2 => new Vec3(.95f, .93f, .82f),
                    3 => new Vec3(.38f, .25f, .15f),
                    4 => new Vec3(.72f, .65f, .51f),
                    _ => new Vec3(.15f, .17f, .18f)
                };

                if (a.Kind == AnimalKind.Bird)
                {
                    tint = new Vec3(.48f, .30f, .16f);
                }

                if (a.Kind == AnimalKind.Pigeon)
                {
                    tint = a.Variant % 2 == 0 
                        ? new Vec3(.40f, .46f, .49f) 
                        : new Vec3(.73f, .72f, .67f);
                }

                float tilt = a.Dead ? 1.5f : (float)Math.Clamp(a.KnockedFor, 0, 1) * 1.2f;
                float bob = a.Dead || a.Flying ? 0 : .025f * MathF.Sin((float)a.Distance * 12);

                Mat4 world = Mat4.Translate(new((float)a.Position.X, (float)(CityTerrain.Ground(a.Position) + a.Height) + .16f + bob + MathF.Sin(tilt) * .13f, (float)a.Position.Z)) *
                             Mat4.RotateY((float)a.Heading) * 
                             Mat4.RotateZ(tilt) * Mat4.Scale(size, size, size);

                float cut = a.Kind switch
                {
                    AnimalKind.Dog => .43f,
                    AnimalKind.Cat => .22f,
                    _ => .12f
                };

                float stride = a.Dead || a.Flying || a.KnockedFor > 0 
                    ? 0 
                    : a.Kind == AnimalKind.Dog 
                        ? .12f 
                        : a.Kind == AnimalKind.Cat 
                            ? .065f 
                            : .035f;

                yield return new Pose(model, null, 0, model.BaseColor, world, tint, new Vec3((float)a.Distance * 10, stride, cut));
            }
        }
    }

    public void Depth(CityTraffic t, Vec3 eye, Mat4 ls)
    {
        if (_depth is null) return;
        if(_wildlife is not null){_wildlife.VisibleDistance=VisibleDistance;_wildlife.Depth(t,eye,ls);}
        _depth.Use();
        _depth.SetMatrix4("uLightSpace", ls);
        var visibility = new CityVisibility(ls);

        foreach (Pose p in Poses(t, eye, visibility))
        {
            if (!visibility.Sphere(new Vec3(p.Transform[0, 3], p.Transform[1, 3] + 1, p.Transform[2, 3]), 2.5f)) continue;
            _depth.SetVec3("uGait", p.Gait);
            _depth.SetMatrix4("uModel", p.Transform);
            if(p.Skin is {} skin)skin.Draw(_depth,p.Frame,p.From,p.Blend);
            else {_depth.SetInt("uSkin",0);p.Model!.Mesh.Draw();}
        }
    }

    public void Draw(CityTraffic t, Vec3 eye, Mat4 view, Mat4 projection, Vec3 sun, Vec3 light, Mat4 ls, uint shadow, OutdoorLighting lighting)
    {
        if (_shader is null) return;
        if(_wildlife is not null){_wildlife.VisibleDistance=VisibleDistance;_wildlife.Draw(t,eye,view,projection,sun,light,ls,shadow,lighting);}
        BindColor(eye,view,projection,sun,light,ls,shadow,lighting);
        Shader s = _shader;
        var visibility = new CityVisibility(projection * view);

        foreach (Pose p in Poses(t, eye, visibility))
        {
            if (!visibility.Sphere(new(p.Transform[0, 3], p.Transform[1, 3] + 1, p.Transform[2, 3]), 2.5f)) continue;
            p.Map?.Bind();
            s.SetVec3("uGait", p.Gait);
            s.SetInt("uCabin", p.Cabin ? 1 : 0);
            s.SetInt("uHasMap", p.Map is null ? 0 : 1);
            s.SetVec3("uTint", p.Tint);
            s.SetMatrix4("uModel", p.Transform);
            if(p.Skin is {} skin)skin.Draw(s,p.Frame,p.From,p.Blend);
            else {s.SetInt("uSkin",0);p.Model!.Mesh.Draw();}
        }
    }

    private void BindColor(Vec3 eye,Mat4 view,Mat4 projection,Vec3 sun,Vec3 light,Mat4 ls,uint shadow,OutdoorLighting lighting)
    {
        Shader s = _shader!;
        s.Use();
        s.SetMatrix4("uView", view);
        s.SetMatrix4("uProj", projection);
        s.SetVec3("uViewPos", eye);
        Materials.ApplyLight(s, sun, light, .52f, .8f, .2f);
        CityLighting.Apply(lighting, s);
        s.SetInt("uLocalLighting", 1);
        s.SetInt("uCabin", 0);
        s.SetInt("uTexture", 0);
        s.SetMatrix4("uLightSpace", ls);
        s.SetFloat("uShadowStrength", shadow == 0 ? 0 : .65f*(1-.72f*CityLighting.Atmosphere.Rain));
        s.SetInt("uShadowMap", 6);
        GL.ActiveTexture(GL.TEXTURE0 + 6);
        GL.BindTexture(GL.TEXTURE_2D, shadow);
        GL.ActiveTexture(GL.TEXTURE0);
    }
    private Queue<Action<bool>>? _preparation;
    private int _prepared,_preparationCount;
    private CityGpuPreparation? _preparationTarget;
    public double PrepareResources()
    {
        if(_shader is null||_depth is null)return 1;
        _preparation??=new Queue<Action<bool>>(PreparationSteps());
        _preparationCount=Math.Max(_preparationCount,_preparation.Count);
        if(_preparation.Count==0)return 1;
        _preparationTarget??=new CityGpuPreparation();
        _preparationTarget.Run(_preparation.Peek());
        _preparation.Dequeue();_prepared++;
        if(_preparation.Count==0){_preparationTarget.Dispose();_preparationTarget=null;}
        return (double)_prepared/_preparationCount;
    }

    private IEnumerable<Action<bool>> PreparationSteps()
    {
        if (_wildlife is not null)
        {
            foreach (Action<bool> step in _wildlife.PreparationSteps())
            {
                yield return step;
            }
        }

        foreach (PersonAsset person in _people)
        {
            yield return depth => PrepareActor(person.Skin, null, person.Texture, depth);
        }

        foreach (GltfModel model in _animals.Values.SelectMany(a => a))
        {
            yield return depth => PrepareActor(null, model, model.BaseColor, depth);
        }
    }

    private void PrepareActor(CitySkin? skin, GltfModel? model, Texture? map, bool depth)
    {
        Shader s = depth ? _depth! : _shader!;
        if (depth)
        {
            s.Use();
            s.SetMatrix4("uLightSpace", CityGpuPreparation.Projection * CityGpuPreparation.View);
        }
        else
        {
            BindColor(CityGpuPreparation.Eye, CityGpuPreparation.View, CityGpuPreparation.Projection, new(0, 1, 0),
                new(1, 1, 1), Mat4.Identity, 0, new());
            map?.Bind();
            s.SetInt("uHasMap", map is null ? 0 : 1);
            s.SetVec3("uTint", new(1, 1, 1));
        }

        s.SetVec3("uGait", skin is null ? new(0, .065f, .22f) : Vec3.Zero);
        s.SetMatrix4("uModel", Mat4.Identity);
        if (skin is not null) skin.Draw(s, 0);
        else
        {
            s.SetInt("uSkin", 0);
            model!.Mesh.Draw();
        }
    }

    public void Dispose()
    {
        _preparationTarget?.Dispose();
        _preparationTarget = null;
        _preparation?.Clear();

        foreach (PersonAsset personAsset in _people)
        {
            personAsset.Skin.Dispose();
            personAsset.Texture.Dispose();
        }

        _people.Clear();
        _wildlife?.Dispose();
        _wildlife = null;

        foreach (GltfModel gltfModel in _animals.Values.SelectMany(list => list))
        {
            gltfModel.Dispose();
        }

        _animals.Clear();
        _shader?.Dispose();
        _depth?.Dispose();
        _shader = _depth = null;
    }
}
