using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using MakarovPhysics.Renderer;

namespace Promzona.Game;

/// <summary>Editable source files are packed only in memory, once per scene. Shared by all districts.</summary>
internal sealed class CityAtlas : IDisposable
{
    public static string AssetRoot { get; set; } = AppContext.BaseDirectory;
    public Texture Color { get; }
    public Texture Height { get; }
    public Texture Finish { get; }
    private static CityAtlas? _shared;
    private static int _references;

    // One GL-thread scene owns all atlas borrowers; the last owner releases the GPU maps.
    public static CityAtlas Acquire()
    {
        _shared ??= new CityAtlas();
        _references++;
        return _shared;
    }

    // System.Text.Json binds constructor parameter names to the manifest keys.
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    private sealed record Entry(string color, string? height, string? normal, string? orm);

    private CityAtlas()
    {
        string root = Path.Combine(AssetRoot, "Textures");
        Entry[] entries = JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(Path.Combine(root, "manifest.json"))) ??
                          [];
        if (entries.Length > 64) throw new InvalidDataException("City atlas supports 64 tiles.");

        using var color = new Bitmap(4096, 4096);
        using var height = new Bitmap(4096, 4096);
        using var finish = new Bitmap(4096, 4096);
        using var gfxFinish = Graphics.FromImage(finish);
        gfxFinish.Clear(System.Drawing.Color.FromArgb(255, 128, 128, 212));
        gfxFinish.CompositingMode = CompositingMode.SourceCopy;

        using (Graphics gfxColor = Graphics.FromImage(color))
        using (Graphics gfxHeight = Graphics.FromImage(height))
        {
            gfxColor.Clear(System.Drawing.Color.FromArgb(85, 83, 78));
            gfxHeight.Clear(System.Drawing.Color.Gray);
            gfxColor.InterpolationMode = gfxHeight.InterpolationMode = InterpolationMode.HighQualityBicubic;

            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].normal is { } normal && entries[i].orm is { } orm)
                {
                    using var packed = PackFinish(SafePath(root, normal), SafePath(root, orm));
                    gfxFinish.DrawImage(packed, new Rectangle(i % 8 * 512, i / 8 * 512, 512, 512));
                }

                var box = new Rectangle(i % 8 * 512 + 4, i / 8 * 512 + 4, 504, 504);
                using var source = new Bitmap(SafePath(root, entries[i].color));
                gfxColor.DrawImage(source, new Rectangle(i % 8 * 512, i / 8 * 512, 512, 512));
                gfxColor.DrawImage(source, box);

                if (entries[i].height is { } h)
                {
                    using var bump = new Bitmap(SafePath(root, h));
                    gfxHeight.DrawImage(bump, box);
                }
            }
        }

        Texture? texture = null, bumpTexture = null, finishTexture = null;

        try
        {
            texture = new Texture(color);
            texture.SetAnisotropy(8);
            bumpTexture = new Texture(height);
            finishTexture = new Texture(finish);
            finishTexture.SetAnisotropy(8);

            // Replicated gutters reduce edge bleed; limit coarse mips before unrelated tiles dominate.
            foreach (Texture map in new[] { texture, bumpTexture, finishTexture })
            {
                map.Bind();
                GL.TexParameteri(GL.TEXTURE_2D, 0x813D, 4);
            }

            Height = bumpTexture;
            Finish = finishTexture;
            Color = texture;
        }
        catch
        {
            texture?.Dispose();
            bumpTexture?.Dispose();
            finishTexture?.Dispose();
            throw;
        }
    }

    // Normal XY + roughness + AO. These are linear data, never sRGB or alpha composited.
    private static unsafe Bitmap PackFinish(string normalPath, string ormPath)
    {
        using var normalSource = new Bitmap(normalPath);
        using var ormSource = new Bitmap(ormPath);
        using var normal = new Bitmap(504, 504, PixelFormat.Format32bppArgb);
        using var orm = new Bitmap(504, 504, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(normal))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(normalSource, 0, 0, 504, 504);
        }

        using (var g = Graphics.FromImage(orm))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(ormSource, 0, 0, 504, 504);
        }

        var result = new Bitmap(512, 512, PixelFormat.Format32bppArgb);
        BitmapData a = normal.LockBits(new Rectangle(0, 0, 504, 504), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData b = orm.LockBits(new Rectangle(0, 0, 504, 504), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        BitmapData c = result.LockBits(new Rectangle(0, 0, 512, 512), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++)
            {
                int sx = Math.Clamp(x - 4, 0, 503), sy = Math.Clamp(y - 4, 0, 503);
                byte* n = (byte*)a.Scan0 + sy * a.Stride + sx * 4;
                byte* m = (byte*)b.Scan0 + sy * b.Stride + sx * 4;
                byte* d = (byte*)c.Scan0 + y * c.Stride + x * 4;
                d[0] = m[1];
                d[1] = n[1];
                d[2] = n[2];
                d[3] = m[2];
            }
        }
        finally
        {
            normal.UnlockBits(a);
            orm.UnlockBits(b);
            result.UnlockBits(c);
        }

        return result;
    }

    private static string SafePath(string root, string path)
    {
        string full = Path.GetFullPath(Path.Combine(root, path));

        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Texture path must stay inside Textures.");
        }

        return full;
    }

    public void Dispose()
    {
        if (!ReferenceEquals(this, _shared) || --_references > 0) return;
        _shared = null;
        Color.Dispose();
        Height.Dispose();
        Finish.Dispose();
    }
}
