using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MakarovPhysics.Renderer;
namespace Promzona.Game;

internal sealed class CityTerrainLighting : IDisposable
{
    private readonly Texture _map;

    public CityTerrainLighting()
    {
        var terrain = CityTerrain.Current;
        int n = terrain.Size;
        var pixels = new int[n * n];

        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            // Two bytes of 1/512 m: 0..128 m. River beds and the beach go to -8 m,
            // and a negative value shifted into the red byte decoded as ~120 m.
            int h = Math.Clamp((int)Math.Round(terrain.Height(terrain.Origin + x, terrain.Origin + z) * 512), 0, 65535);
            pixels[x + z * n] = unchecked((int)0xff000000) | ((h >> 8) << 16) | ((h & 255) << 8);
        }

        using var bitmap = new Bitmap(n, n, PixelFormat.Format32bppArgb);
        BitmapData bits = bitmap.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        _map = new Texture(bitmap, false);
        CityLighting.TerrainMap = _map;
    }

    public void Dispose()
    {
        if (CityLighting.TerrainMap == _map)
        {
            CityLighting.TerrainMap = null;
        }

        _map.Dispose();
    }
}
