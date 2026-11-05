using System.Drawing.Imaging;

namespace MakarovPhysics.Renderer;

public sealed unsafe class Texture : IDisposable
{
    public uint Id { get; private set; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Upload a bitmap as a texture.
    ///
    /// `mipmapped: false` is for atlases drawn one screen pixel to one texel -
    /// a UI font above all. Mipmapping such an atlas is not a refinement but a
    /// defect: as soon as the sampler's derivatives suggest the slightest
    /// minification, and at exactly 1:1 they wobble either side of it, glyphs
    /// blend with a half-resolution copy of themselves. A Steam reviewer at
    /// 1920x1080 called the result "smudgy and very hard to read" (28.08), and
    /// that is exactly what it is.
    ///
    /// `srgb: true` is for photographs and painted colour maps - a planet's
    /// albedo above all. Such a file stores sRGB-encoded values; sampled as if
    /// linear and gamma-encoded again on output, the Earth came out pale
    /// (19.10). With the sRGB internal format the sampler decodes to linear
    /// light before filtering, so the shader works in radiance and encodes
    /// once. Height, mask and data maps stay linear.
    /// </summary>
    public Texture(Bitmap src, bool mipmapped = true, bool srgb = false)
    {
        Width = src.Width;
        Height = src.Height;

        using var bmp = (Bitmap)src.Clone();
        bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        using var bitsLocker = new BitsLocker(bmp, rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            Id = GL.GenTexture();

            GL.BindTexture(GL.TEXTURE_2D, Id);
            GL.PixelStorei(GL.UNPACK_ALIGNMENT, 1);
            GL.TexImage2D(GL.TEXTURE_2D, 0, (int)(srgb ? GL.SRGB8_ALPHA8 : GL.RGBA8), bmp.Width, bmp.Height, 0, GL.BGRA, GL.UNSIGNED_BYTE, (void*)bitsLocker.Data.Scan0);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);
            GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);

            if (mipmapped)
            {
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR_MIPMAP_LINEAR);
                GL.GenerateMipmap(GL.TEXTURE_2D);
            }
            else
            {
                GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR);
            }

            GL.BindTexture(GL.TEXTURE_2D, 0);
        }
        catch
        {
            // upload failed AFTER the GL name was created: the constructor
            // throws, so nobody will ever own this object or Dispose it -
            // roll the GL texture back here, or it leaks for the app lifetime
            if (Id != 0)
            {
                GL.DeleteTexture(Id); 
                Id = 0;
            }

            throw;
        }
    }

    /// <summary>
    /// Tile this map instead of clamping it.
    ///
    /// A triplanar projection multiplies the position by a scale, so its
    /// coordinates leave 0..1 immediately: under the default clamp the whole
    /// surface takes the colour of one edge pixel and a wood map turns into a
    /// smear. Anything sampled once per surface - an icon, a module preview -
    /// wants the clamp, which is why it stays the default and this is a
    /// separate call.
    ///
    /// A METHOD rather than a constructor flag on purpose: a second Bitmap
    /// overload cannot be stubbed (System.Drawing does not exist in the
    /// container), and the stub gap is a ratchet that should not be nudged up
    /// for a convenience.
    /// </summary>
    public void SetWrapRepeat()
    {
        GL.BindTexture(GL.TEXTURE_2D, Id);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.REPEAT);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.REPEAT);
        GL.BindTexture(GL.TEXTURE_2D, 0);
    }

    /// <summary>
    /// Anisotropic filtering up to the given degree. A globe seen near its
    /// limb is a map viewed at a grazing angle: trilinear sampling picks the
    /// mip level by the shorter screen derivative and the coastlines smear
    /// into a blur one texel wide. Anisotropy takes several samples along the
    /// long axis instead. Clamped to what the driver offers; without the
    /// extension the call does nothing (and raises no GL error - GlCheck
    /// asserts a clean error flag after every frame).
    /// </summary>
    public void SetAnisotropy(int degree)
    {
        if (!GL.HasExtension("GL_EXT_texture_filter_anisotropic") && !GL.HasExtension("GL_ARB_texture_filter_anisotropic")) return;
        int supported = GL.GetInteger(GL.MAX_TEXTURE_MAX_ANISOTROPY);
        if (supported < 2) return;
        GL.BindTexture(GL.TEXTURE_2D, Id);
        GL.TexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAX_ANISOTROPY, Math.Clamp(degree, 1, supported));
        GL.BindTexture(GL.TEXTURE_2D, 0);
    }

    /// <summary>Update a top-left-oriented bitmap region without rebuilding the
    /// texture. The current unit's texture binding is restored after the upload.</summary>
    public void UpdateRegion(Bitmap source, Rectangle region)
    {
        if (source.Width != Width || source.Height != Height)
            throw new ArgumentException("Atlas source dimensions must match the texture.", nameof(source));
        region.Intersect(new Rectangle(0, 0, Width, Height));
        if (region.IsEmpty || region.Width <= 0 || region.Height <= 0) return;
        using var patch = source.Clone(region, PixelFormat.Format32bppArgb);
        patch.RotateFlip(RotateFlipType.RotateNoneFlipY);
        using var bits = new BitsLocker(patch, new Rectangle(0, 0, patch.Width, patch.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        uint previous = (uint)GL.GetInteger(0x8069); // GL_TEXTURE_BINDING_2D, on the current unit.
        int alignment = GL.GetInteger(GL.UNPACK_ALIGNMENT);
        try
        {
            GL.BindTexture(GL.TEXTURE_2D, Id);
            GL.PixelStorei(GL.UNPACK_ALIGNMENT, 1);
            GL.TexSubImage2D(GL.TEXTURE_2D, 0, region.X, Height - region.Bottom,
                patch.Width, patch.Height, GL.BGRA, GL.UNSIGNED_BYTE, (void*)bits.Data.Scan0);
        }
        finally
        {
            GL.PixelStorei(GL.UNPACK_ALIGNMENT, alignment);
            GL.BindTexture(GL.TEXTURE_2D, previous);
        }
    }

    public void Bind(uint unit = 0)
    {
        GL.ActiveTexture(GL.TEXTURE0 + unit);
        GL.BindTexture(GL.TEXTURE_2D, Id);
    }

    public void Dispose()
    {
        if (Id != 0)
        {
            GL.DeleteTexture(Id);
            Id = 0;
        }
    }
}
