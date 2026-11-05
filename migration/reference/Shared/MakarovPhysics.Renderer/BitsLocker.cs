using System.Drawing.Imaging;

namespace MakarovPhysics.Renderer
{
    public sealed class BitsLocker(Bitmap bmp, Rectangle rect, ImageLockMode flags, PixelFormat pixelFormat) : IDisposable
    {
        private Bitmap? _bmp = bmp;
        private BitmapData? _data = bmp.LockBits(rect, flags, pixelFormat);
        public BitmapData Data => _data!;

        public void Dispose()
        {
            if (_data is not null && _bmp is not null)
            {
                _bmp.UnlockBits(_data);
                _bmp = null;
                _data = null;
            }
        }
    }
}
