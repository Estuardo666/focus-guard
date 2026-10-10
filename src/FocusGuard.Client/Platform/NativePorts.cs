namespace FocusGuard.Client.Platform;

public sealed record InstalledApplication(string Name, string Path);
public interface IApplicationCatalog
{
    Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken token);
}
public interface IApplicationIconProvider
{
    ApplicationIconPixels? Read(string path);
}
/// <summary>Top-down BGRA pixels, using the existing shell alpha convention.</summary>
public sealed class ApplicationIconPixels
{
    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Pixels { get; }
    public ApplicationIconPixels(int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (width is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(width));
        if (height is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(height));
        if (pixels.Length != width * height * 4) throw new ArgumentException("Invalid BGRA buffer length.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels.ToArray();
    }
}
public interface IStartupRegistration
{
    bool IsEnabled { get; }
    bool TrySetEnabled(bool enabled);
}
public interface IProcessAgentLauncher
{
    bool EnsureRunning(string desktopBaseDirectory);
}
public interface IDesktopInstance : IDisposable
{
    void StartListening(Action activationRequested);
    void StopListening();
}
