namespace MakarovPhysics.Renderer;

/// <summary>Opt-in opaque scientific surfaces and depth-tested, pixel-width strokes.
/// Does not redirect the caller's framebuffer or change its viewport.</summary>
public sealed class ScientificPlot : IDisposable
{
    /// <summary>Sample an image as sRGB colours, left-to-right and top-to-bottom.
    /// File decoding stays in the Windows renderer; modules need no GDI dependency.</summary>
    public static Vec3[] ImageColours(string path,int width,int height)
    {
        if(width<1||height<1||width>4096||height>4096)throw new ArgumentOutOfRangeException(nameof(width));
        using var bitmap=new System.Drawing.Bitmap(path);
        var pixels=new Vec3[checked(width*height)];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            var c=bitmap.GetPixel(x*(bitmap.Width-1)/Math.Max(1,width-1),y*(bitmap.Height-1)/Math.Max(1,height-1));
            pixels[y*width+x]=new(c.R/255f,c.G/255f,c.B/255f);
        }
        return pixels;
    }

    private readonly StudioSurface _surface = new(240000);
    private readonly ColorLineMesh _stroke = new(420000) { Mode = GL.TRIANGLES };
    private readonly List<TrailPoint> _triangles = new(420000);
    public void Surface(IReadOnlyList<SurfaceVertex> vertices, Mat4 view, Mat4 projection, Vec3 eye)
    {
        GL.Enable(GL.DEPTH_TEST); GL.DepthMask(true); GL.Disable(GL.BLEND); GL.Disable(GL.CULL_FACE);
        _surface.Update(vertices);
        _surface.Draw(view, projection, eye, KeyLight.FromView(view), new(1, 1, 1), Mat4.Identity, 0, 0, .70f, environmentFill: 1.25f);
        GL.Enable(GL.CULL_FACE);
    }
    public void Lines(IReadOnlyList<TrailPoint> pairs, Mat4 view, Mat4 projection, Vec3 eye, int height, float width = 1.5f)
    {
        _triangles.Clear();
        // Camera-facing world strips. Endpoint widths follow perspective depth;
        // widths are in physical pixels, so callers can apply their UI scale.
        for (int i = 0; i + 1 < pairs.Count; i += 2)
        {
            var a = pairs[i]; var b = pairs[i + 1];
            if(!a.Pos.IsFinite || !b.Pos.IsFinite || !a.Color.IsFinite || !b.Color.IsFinite) continue;
            var d = b.Pos - a.Pos;
            var side = Vec3.Cross(d, eye - (a.Pos + b.Pos) * .5f).Normalized();
            if (d.Length() < 1e-7f || side.Length() < .5f) continue;
            float Size(Vec3 p) => Math.Max(.0001f, -(view[2,0]*p.X+view[2,1]*p.Y+view[2,2]*p.Z+view[2,3])) * width / Math.Max(1, height) / projection[1,1];
            var da = side * Size(a.Pos); var db = side * Size(b.Pos);
            var p0 = a.Pos - da; var p1 = a.Pos + da; var p2 = b.Pos + db; var p3 = b.Pos - db;
            _triangles.Add(new(p0, a.Color)); _triangles.Add(new(p1, a.Color)); _triangles.Add(new(p2, b.Color));
            _triangles.Add(new(p0, a.Color)); _triangles.Add(new(p2, b.Color)); _triangles.Add(new(p3, b.Color));
        }
        GL.Enable(GL.DEPTH_TEST); GL.DepthMask(false); GL.Disable(GL.BLEND); GL.Disable(GL.CULL_FACE);
        _stroke.Update(_triangles); _stroke.Draw(view, projection);
        GL.DepthMask(true); GL.Enable(GL.CULL_FACE);
    }
    private static readonly Vec3[] SequentialStops = [new(.16f,.25f,.58f), new(.13f,.54f,.72f), new(.20f,.72f,.62f), new(.91f,.80f,.35f), new(.83f,.30f,.21f)];
    public static Vec3 Colour(float t)
    {
        t = Math.Clamp(t, 0, 1) * 4;

        int k = Math.Min(3, (int)t); return Vec3.Lerp(SequentialStops[k], SequentialStops[k + 1], t - k);
    }
    private static readonly Vec3[] PhaseStops = [new(.87f,.39f,.28f), new(.88f,.70f,.30f), new(.35f,.72f,.49f), new(.20f,.65f,.75f), new(.35f,.43f,.77f), new(.71f,.38f,.68f), new(.87f,.39f,.28f)];
    public static Vec3 Phase(float angle)
    {
        float t = (angle / MathF.Tau + 1) % 1 * 6;

        int k = Math.Clamp((int)t, 0, 5); return Vec3.Lerp(PhaseStops[k], PhaseStops[k + 1], t - k);
    }
    public void Dispose() { _surface.Dispose(); _stroke.Dispose(); }
}
