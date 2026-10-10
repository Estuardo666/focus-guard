using System.Runtime.InteropServices;
using FocusGuard.Client.Platform;

namespace FocusGuard.Platform.Windows;

/// <summary>Read-only shell/GDI icon extraction; releases every native handle.</summary>
public sealed class WindowsApplicationIconProvider : IApplicationIconProvider
{
    public ApplicationIconPixels? Read(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return null;
        if (SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;
        IntPtr dc = IntPtr.Zero;
        IconInfo data = default;
        try
        {
            if (!GetIconInfo(info.Icon, out data) || data.Color == IntPtr.Zero || GetObject(data.Color, Marshal.SizeOf<NativeBitmap>(), out var bitmap) == 0) return null;
            if (bitmap.Width is < 1 or > 256 || bitmap.Height is < 1 or > 256) return null;
            dc = GetDC(IntPtr.Zero);
            var header = new BitmapHeader { Size = (uint)Marshal.SizeOf<BitmapHeader>(), Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, BitCount = 32 };
            var pixels = new byte[bitmap.Width * bitmap.Height * 4];
            if (GetDIBits(dc, data.Color, 0, (uint)bitmap.Height, pixels, ref header, 0) == 0) return null;
            if (!Enumerable.Range(0, pixels.Length / 4).Any(i => pixels[i * 4 + 3] > 0))
                for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return new ApplicationIconPixels(bitmap.Width, bitmap.Height, pixels);
        }
        finally
        {
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
            if (data.Color != IntPtr.Zero) DeleteObject(data.Color);
            if (data.Mask != IntPtr.Zero) DeleteObject(data.Mask);
            DestroyIcon(info.Icon);
        }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ShellFileInfo { public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName; }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { [MarshalAs(UnmanagedType.Bool)] public bool Icon; public uint X; public uint Y; public IntPtr Mask; public IntPtr Color; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeBitmap { public int Type; public int Width; public int Height; public int WidthBytes; public ushort Planes; public ushort BitsPixel; public IntPtr Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader { public uint Size; public int Width; public int Height; public ushort Planes; public ushort BitCount; public uint Compression; public uint ImageSize; public int XPixels; public int YPixels; public uint Used; public uint Important; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(IntPtr handle, int size, out NativeBitmap bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] pixels, ref BitmapHeader header, uint usage);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
}
