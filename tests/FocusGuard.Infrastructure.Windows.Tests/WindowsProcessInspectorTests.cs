using System.Diagnostics;
using FocusGuard.Infrastructure.Windows.Processes;

namespace FocusGuard.Infrastructure.Windows.Tests;

public sealed class WindowsProcessInspectorTests
{
    [Fact]
    public void TryRead_UsesLimitedInformationToResolveCurrentProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var current = Process.GetCurrentProcess();
        var instance = WindowsProcessInspector.TryRead(current.Id);

        Assert.NotNull(instance);
        Assert.Equal(current.Id, instance.ProcessId);
        Assert.True(Path.IsPathFullyQualified(instance.ExecutablePath));
        Assert.Equal(TimeSpan.Zero, instance.StartedAtUtc.Offset);
    }
}
