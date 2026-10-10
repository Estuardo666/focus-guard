using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace FocusGuard.Desktop;

internal static class ApplicationIconCache
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IApplicationIconProvider Provider = new WindowsApplicationIconProvider();
    public static Bitmap? Get(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return null;
        if (Cache.TryGetValue(path, out var value)) return value;
        if (Cache.Count >= 256) return null;
        var data = Provider.Read(path);
        Bitmap? image = null;
        if (data is not null)
        {
            var bitmap = new WriteableBitmap(new PixelSize(data.Width, data.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            var pixels = data.Pixels.ToArray();
            using (var buffer = bitmap.Lock())
                for (var y = 0; y < data.Height; y++)
                    Marshal.Copy(pixels, y * data.Width * 4, buffer.Address + y * buffer.RowBytes, data.Width * 4);
            image = bitmap;
        }
        Cache[path] = image;
        return image;
    }
}
