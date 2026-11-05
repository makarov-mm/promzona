using MakarovPhysics.Renderer;
namespace Promzona.Game;

/// <summary>Bounded alpha batch: tire/deformation marks and billboard dust, sparks,
/// water and debris. Simulation owns ages; rendering never advances an effect.</summary>
internal sealed class CityEffects : IDisposable
{
    private Shader? _shader;
    private uint _vao, _vbo;
    private readonly float[] _data = new float[18000 * 10];
    private int _count;
    private bool _onGround;private int _roadLevel;

    public CityEffects()
    {
        try
        {
            _shader = new(CityShaderSource.ReadGeometry("Effects.vert"), CityShaderSource.ReadGeometry("Effects.frag"));
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, _data.Length * 4, GL.DYNAMIC_DRAW);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, 40, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 4, GL.FLOAT, false, 40, 12);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 3, GL.FLOAT, false, 40, 28);
            GL.EnableVertexAttribArray(2);
            GL.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void V(Vec3 p, Vec3 c, float a, float u, float v, float soft)
    {
        if (_count >= 18000) return;
        if (_onGround) p = new(p.X,p.Y+(float)CityRoadLevels.Surface(p.X,p.Z,_roadLevel),p.Z);
        int k = _count++ * 10;
        _data[k] = p.X;
        _data[k + 1] = p.Y;
        _data[k + 2] = p.Z;
        _data[k + 3] = c.X;
        _data[k + 4] = c.Y;
        _data[k + 5] = c.Z;
        _data[k + 6] = a;
        _data[k + 7] = u;
        _data[k + 8] = v;
        _data[k + 9] = soft;
    }

    private void Quad(Vec3 p, Vec3 right, Vec3 up, Vec3 c, float a, float soft)
    {
        if (_count + 6 > 18000) return;
        V(p - right - up, c, a, -1, -1, soft);
        V(p + right - up, c, a, 1, -1, soft);
        V(p + right + up, c, a, 1, 1, soft);
        V(p - right - up, c, a, -1, -1, soft);
        V(p + right + up, c, a, 1, 1, soft);
        V(p - right + up, c, a, -1, 1, soft);
    }

    public void Draw(CityModel model, Mat4 view, Mat4 proj, Vec3 eye)
    {
        _count = 0;
        var t = model.Traffic;
        var visibility = new CityVisibility(proj * view);
        foreach (var m in t.Marks)
        {
            _onGround = true;_roadLevel=m.RoadLevel;
            Vec3 p = new((float)m.Point.X, .028f, (float)m.Point.Z);
            if (!visibility.Sphere(new Vec3(p.X,p.Y+(float)CityRoadLevels.Surface(p.X,p.Z,m.RoadLevel),p.Z), 3)) continue;
            if (m.Blood)
            {
                float size = (float)m.Size;
                Quad(p, Vec3.UnitX * size, Vec3.UnitZ * size, new(.24f, .012f, .008f), (float)m.Opacity * .9f, 2);
            }
            else if (m.CarId >= 0)
            {
                _onGround = false;
                var c = t.Cars.Find(c => c.Id == m.CarId);
                if (c is null || Math.Abs(c.Roll) > .3) continue;
                Vec3 f = new((float)Math.Sin(c.Heading), 0, (float)Math.Cos(c.Heading)), r = new(f.Z, 0, -f.X);
                p = new((float)c.Position.X, .67f + (float)(c.SurfaceHeight + c.Lift), (float)c.Position.Z);
                for (int side = -1; side <= 1; side += 2)
                    Quad(p + r * (side * ((float)c.HalfWidth + .015f)), f * .55f, Vec3.UnitY * .10f,
                        new(.05f, .045f, .04f), (float)m.Opacity * .8f, 1);
            }
            else if (m.Skid)
            {
                var end = new Vec3((float)m.End.X, .028f, (float)m.End.Z);
                var forward = end - p;
                var right = new Vec3(forward.Z, 0, -forward.X).Normalized() * .09f;
                Quad((p + end) * .5f, right, forward * .5f,
                    m.Sand ? new(.32f, .25f, .14f) : m.Wet ? new(.04f, .07f, .08f) : new(.012f, .012f, .014f),
                    (float)m.Opacity * (m.Wet ? .38f : .65f), m.Sand ? 3 : 0);
            }
            else
            {
                Vec3 tangent = new((float)Math.Cos(m.Heading), 0, -(float)Math.Sin(m.Heading));
                Quad(p + new Vec3(0, (float)m.Height, 0), tangent * .7f, Vec3.UnitY * .26f, new(.08f, .07f, .06f),
                    (float)m.Opacity * .8f, 1);
            }
        }

        _onGround = false;_roadLevel=0;
        foreach(var s in t.Splashes){
            float radius=(float)(s.Size*.35+s.Age*1.5),a=(float)(1-s.Age/3)*.55f;
            for(int n=0;n<36;n++){
                float angle=n*MathF.Tau/36;Vec3 tangent=new(-MathF.Sin(angle),0,MathF.Cos(angle));
                Vec3 radial=new(MathF.Cos(angle),0,MathF.Sin(angle));
                Quad(new Vec3((float)s.Position.X,-2.77f,(float)s.Position.Z)+radial*radius,tangent*(radius*.10f),radial*.065f,new(.64f,.81f,.80f),a,1);
            }
        }
        Vec3 cameraRight = new(view[0, 0], view[0, 1], view[0, 2]), cameraUp = new(view[1, 0], view[1, 1], view[1, 2]);
        // Short-lived exhaust / wet tyre mist is derived from travelled distance
        // and visual time. No particle actor allocation, no simulation at distance.
        int emitters=0,beams=0;
        foreach(var car in t.Cars){
            if(car.Destroyed||car.Falling)continue;
            Vec3 origin=new((float)car.Position.X,(float)(car.SurfaceHeight+car.Lift),(float)car.Position.Z);
            if((origin-eye).Length()>65||!visibility.Sphere(origin,12))continue;
            Vec3 forward=new((float)Math.Sin(car.Heading),0,(float)Math.Cos(car.Heading)),right=new(forward.Z,0,-forward.X);
            if(emitters++<14){
                Vec3 exhaust=origin-forward*(float)car.HalfLength+right*(float)(car.HalfWidth*.65)+Vec3.UnitY*.34f;
                for(int i=0;i<7;i++){
                    float age=Fract((float)model.Time*.65f+i/7f+car.Id*.17f),size=.035f+age*.17f;
                    Quad(exhaust-forward*(age*.65f)+Vec3.UnitY*(age*.28f),cameraRight*size,cameraUp*size,new(.40f,.43f,.46f),.065f*(1-age),1);
                }
                if(model.Wetness>.2&&Math.Abs(car.Speed)>3){
                    for(int side=-1;side<=1;side+=2)for(int i=0;i<8;i++){
                        float age=Fract((float)model.Time*1.4f+i*.125f+car.Id*.13f),size=.05f+age*.25f;
                        Vec3 p=origin+right*(side*(float)car.HalfWidth)-forward*((float)car.Wheelbase*.5f+age*Math.Min(3,(float)Math.Abs(car.Speed)*.18f))+Vec3.UnitY*(.12f+MathF.Sin(age*3.14f)*.25f);
                        Quad(p,cameraRight*size,cameraUp*(size*.55f),new(.53f,.59f,.63f),.13f*(float)model.Wetness*(1-age),1);
                    }
                }
            }
            if(model.Daylight<.7&&beams++<4){
                // Overlapping soft slices, depth-tested against the actual scene.
                // Centre-ray building/terrain clipping prevents beams behind facades.
                Vec3 start=origin+forward*((float)car.HalfLength+.08f)+Vec3.UnitY*.78f;
                float length=BeamLength(start,forward,car.RoadLevel),density=(float)((1-model.Daylight)*(.12+.88*model.Rain));
                for(int i=17;i>=0;i--){float d=.5f+i*.9f;if(d>length)continue;
                    float size=.22f+d*.20f,alpha=density*.012f*(1-d/19);
                    for(int side=-1;side<=1;side+=2)
                        Quad(start+forward*d+right*(side*.62f)-Vec3.UnitY*(d*.025f),cameraRight*size,cameraUp*(size*.60f),new(.95f,.90f,.74f),alpha,1);
                }
            }
        }
        // Bounded, camera-facing spray: the emitter stays at the broken pipe,
        // independent of the knocked-away hydrant. No retained droplet actors.
        int fountains=0;
        foreach(var prop in t.LiveProps){
            if(prop.Kind!=StreetKind.Hydrant||prop.Water<=0)continue;
            Vec3 origin=new((float)prop.Home.X,(float)CityTerrain.Ground(prop.Home)+.15f,(float)prop.Home.Z);
            if((origin-eye).Length()>100||!visibility.Sphere(origin+Vec3.UnitY*2.2f,5))continue;
            if(fountains++>=8)break;
            float pressure=(float)Math.Min(1,prop.Water/3),time=(float)model.Time;
            for(int i=0;i<150;i++){
                float age=Fract(i*.618034f+time*.58f)*1.8f;
                float angle=i*2.399963f,spread=.15f+Fract(i*.317f)*1.25f;
                Vec3 centre=origin+new Vec3(MathF.Cos(angle)*spread*age,MathF.Max(0,9*age-4.9f*age*age)*pressure,MathF.Sin(angle)*spread*age);
                float size=.025f+.025f*age;
                Quad(centre,cameraRight*size,cameraUp*(size*2.4f),new(.72f,.85f,.89f),.62f*pressure,1);
                if(i%5==0)Quad(centre,cameraRight*(.14f+age*.07f),cameraUp*(.15f+age*.08f),new(.68f,.80f,.83f),.055f*pressure,1);
            }
        }
        foreach (var p in t.Particles.OrderByDescending(p =>
                     Math.Pow(p.X - eye.X, 2) + Math.Pow(p.Y - eye.Y, 2) + Math.Pow(p.Z - eye.Z, 2)))
        {
            Vec3 centre = new((float)p.X, (float)p.Y, (float)p.Z);
            if ((centre - eye).Length() > 130 || !visibility.Sphere(centre, (float)p.Size)) continue;
            Vec3 color = p.Kind switch
            {
                6 => p.Age<.12 ? new(2.2f,1.5f,.35f) : new(1.8f,.42f,.035f), 5 => new(.63f, .53f, .34f), 0 => new(1.7f, .68f, .14f), 1 => new(.24f, .25f, .24f),
                2 => new(.32f, .29f, .24f), 4 => new(3f, .62f, .06f), _ => new(.50f, .72f, .85f)
            };
            float alpha = (float)Math.Clamp((1 - p.Age / p.Life) * 3, 0, 1) * (p.Kind == 1 ? .38f : p.Kind==6 ? .46f : .85f);
            Quad(centre, cameraRight * (float)p.Size, cameraUp * (float)p.Size, color, alpha,
                p.Kind is 0 or 1 or 3 or 4 or 6 ? 1 : 0);
        }

        foreach(var rocket in t.Rockets){
            var at=new Vec3((float)rocket.Position.X,(float)rocket.Height,(float)rocket.Position.Z);
            var axis=new Vec3((float)rocket.Direction.X,(float)rocket.Slope,(float)rocket.Direction.Z).Normalized();
            Quad(at,cameraRight*.055f,axis*.32f,new(.55f,.53f,.25f),1,0);
            Quad(at-axis*.4f,cameraRight*.12f,cameraUp*.12f,new(3,1.4f,.25f),.9f,1);
            Quad(at-axis*.68f,cameraRight*.06f,axis*.28f,new(2.8f,.7f,.1f),.75f,1);
        }
        foreach (var shot in t.Shots)
        {
            var a = new Vec3((float)shot.From.X, (float)shot.Height, (float)shot.From.Z);
            var b = new Vec3((float)shot.To.X, (float)shot.EndHeight, (float)shot.To.Z);
            Quad((a + b) * .5f, cameraRight * .012f, (b - a) * .5f, new(1.7f, 1.15f, .45f),
                (float)(1 - shot.Age / .12) * .6f, 0);
            Quad(a, cameraRight * .11f, cameraUp * .11f, new(2.4f, 1.2f, .3f), (float)(1 - shot.Age / .12), 1);
        }

        // Fixed-size deterministic rain batch: no per-droplet heap objects or retained emitters.
        if (model.Rain > .01)
        {
            int drops = (int)(650 * model.Rain);
            float time = (float)model.Time;
            for (int i = 0; i < drops; i++)
            {
                float x = MathF.Floor(eye.X / 8) * 8 - 24 + 48 * Fract(i * .618034f + time * .018f),
                    z = MathF.Floor(eye.Z / 8) * 8 - 24 + 48 * Fract(i * .414214f + time * .006f);
                float y = eye.Y - 6 + 28 * Fract(i * .732051f - time * .65f);
                if (y < Roof(x, z) + .15f || !visibility.Sphere(new(x, y, z), 1)) continue;
                Quad(new(x, y, z), cameraRight * .013f, new(.10f, .55f, .035f), new(.65f, .72f, .79f),
                    .29f * (float)model.Rain, 0);
            }

            for (int i = 0; i < 90; i++)
            {
                float x = eye.X - 18 + 36 * Fract(i * .618034f),
                    z = eye.Z - 18 + 36 * Fract(i * .414214f),
                    phase = Fract(time * 2 + i * .37f);
                if (phase > .45f || Roof(x, z) > CityTerrain.Ground(x, z) + .2 ||
                    !visibility.Sphere(CityTerrainGeometry.Ground(new(x, 0, z)), 1)) continue;
                float radius = .03f + phase * .15f;
                _onGround = true;
                Quad(new(x, .033f, z), Vec3.UnitX * radius, Vec3.UnitZ * radius, new(.57f, .63f, .69f),
                    .15f * (float)model.Rain * (1 - phase * 2), 1);
            }
        }

        if (_count == 0 || _shader is null) return;
        bool blend = GL.IsEnabled(GL.BLEND), cull = GL.IsEnabled(GL.CULL_FACE);
        int mask = GL.GetInteger(0x0B72), src = GL.GetInteger(0x80C9), dst = GL.GetInteger(0x80C8);
        try
        {
            GL.Enable(GL.BLEND);
            GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
            GL.DepthMask(false);
            GL.Disable(GL.CULL_FACE);
            _shader.Use();
        CityLighting.Atmosphere.Apply(_shader);
        _shader.SetVec3("uViewPos",eye);
            _shader.SetMatrix4("uView", view);
            _shader.SetMatrix4("uProj", proj);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, _count * 10);
            GL.BindVertexArray(_vao);
            GL.DrawArrays(GL.TRIANGLES, 0, _count);
            GL.BindVertexArray(0);
        }
        finally
        {
            GL.DepthMask(mask != 0);
            GL.BlendFunc((uint)src, (uint)dst);
            if (!blend) GL.Disable(GL.BLEND);
            if (cull) GL.Enable(GL.CULL_FACE);
        }
    }

    private static float Roof(float x, float z)
    {
        int i = (int)Math.Floor((x - CityTraffic.Coordinate(0)) / 36),
            j = (int)Math.Floor((z - CityTraffic.Coordinate(0)) / 36);
        foreach (int id in CityLayout.Current.BuildingIds(i, j))
        {
            var b = CityModel.Blocks[id];
            if (Math.Abs(x - b.X) < b.HalfX && Math.Abs(z - b.Z) < b.HalfZ)
                return (float)(CityTerrain.Ground(b.X, b.Z) + b.Height);
        }

        var road=CityRoadLevels.At(x,z);
        return road is null?(float)CityTerrain.Ground(x,z):road.Tunnel&&road.Roof(x)?(float)CityTerrain.Ground(x,z):(float)road.Floor(x,z);
    }

    private static float Fract(float x) => x - MathF.Floor(x);

    private static float BeamLength(Vec3 origin,Vec3 forward,int level)
    {
        for(int step=1;step<18;step++){
            Vec3 p=origin+forward*step;double ground=CityRoadLevels.Surface(p.X,p.Z,level);
            if(ground>p.Y)return step-.5f;
            int i=(int)Math.Floor((p.X-CityTraffic.Coordinate(0))/36),j=(int)Math.Floor((p.Z-CityTraffic.Coordinate(0))/36);
            foreach(int id in CityLayout.Current.BuildingIds(i,j)){
                var b=CityModel.Blocks[id];if(Math.Abs(p.X-b.X)<b.HalfX&&Math.Abs(p.Z-b.Z)<b.HalfZ){
                    double bottom=CityTerrain.Ground(b.X,b.Z);if(p.Y>=bottom&&p.Y<=bottom+b.Height)return step-.5f;
                }
            }
        }return 18;
    }

    public void Dispose()
    {
        _shader?.Dispose();
        _shader = null;
        if (_vbo != 0)
        {
            GL.DeleteBuffer(_vbo);
            _vbo = 0;
        }

        if (_vao != 0)
        {
            GL.DeleteVertexArray(_vao);
            _vao = 0;
        }
    }
}
