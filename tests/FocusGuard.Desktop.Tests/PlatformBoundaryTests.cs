using Xunit;

namespace FocusGuard.Desktop.Tests;

public sealed class PlatformBoundaryTests
{
    [Fact]
    public void NativeLibraryHasNoPresentationOrEnforcementDependency()
    {
        var references = typeof(WindowsApplicationCatalog).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, name => name.Name!.StartsWith("Avalonia", StringComparison.Ordinal)
            || name.Name is "FocusGuard.Desktop" or "FocusGuard.Service" or "FocusGuard.Infrastructure.Windows");
        Assert.Equal(typeof(IApplicationCatalog).Assembly, typeof(IStartupRegistration).Assembly);
    }

    [Fact]
    public void MissingIconReturnsNoPixels()
    {
        Assert.Null(new WindowsApplicationIconProvider().Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")));
    }

    [Fact]
    public void RealExecutableIconReturnsBoundedPixels()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        Assert.True(File.Exists(path));
        var icon = new WindowsApplicationIconProvider().Read(path);
        Assert.NotNull(icon);
        Assert.InRange(icon.Width, 1, 256);
        Assert.InRange(icon.Height, 1, 256);
        Assert.Equal(icon.Width * icon.Height * 4, icon.Pixels.Length);
    }

    [Fact]
    public async Task CatalogCancellationDoesNotStartDiscovery()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsApplicationCatalog().ListAsync(cancellation.Token));
    }
}
