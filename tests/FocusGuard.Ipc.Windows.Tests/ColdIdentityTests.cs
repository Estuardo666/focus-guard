using System.Diagnostics;
using System.Security.Principal;

namespace FocusGuard.Ipc.Windows.Tests;

public sealed class ColdIdentityTests
{
    [Fact]
    public async Task FreshProcessCapturesIdentificationWithoutLoadingClaimsUnderCallerToken()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var identity = WindowsIdentity.GetCurrent();
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "ColdIdentityProbe", "FocusGuard.Ipc.ColdIdentityProbe.dll"));
        start.ArgumentList.Add(identity.User!.Value);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Probe did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(deadline.Token);
        var stdout = await output;
        var stderr = await error;
        Assert.True(process.ExitCode == 0, $"exit={process.ExitCode}; stdout={stdout}; stderr={stderr}");
        Assert.Contains("ClaimsBeforeCapture=False", stdout);
        Assert.Contains("ColdCaptureSucceeded", stdout);
    }
}
