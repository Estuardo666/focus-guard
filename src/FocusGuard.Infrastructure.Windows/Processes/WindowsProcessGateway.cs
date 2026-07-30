using System.ComponentModel;
using System.Diagnostics;

namespace FocusGuard.Infrastructure.Windows.Processes;

public sealed record WindowsProcessInstance(int ProcessId, DateTimeOffset StartedAtUtc, string ExecutablePath);

public interface IWindowsProcessGateway
{
    IReadOnlyList<WindowsProcessInstance> FindByExecutablePath(string executablePath);

    bool TryRequestGracefulClose(WindowsProcessInstance process);

    Task<bool> WaitForExitAsync(
        WindowsProcessInstance process,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    void Terminate(WindowsProcessInstance process);

    bool HasExited(WindowsProcessInstance process);
}

public sealed class WindowsProcessGateway : IWindowsProcessGateway
{
    public IReadOnlyList<WindowsProcessInstance> FindByExecutablePath(string executablePath)
    {
        var canonicalPath = Path.GetFullPath(executablePath);
        var matches = new List<WindowsProcessInstance>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var actualPath = process.MainModule?.FileName;
                    if (actualPath is not null
                        && string.Equals(Path.GetFullPath(actualPath), canonicalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        matches.Add(new WindowsProcessInstance(
                            process.Id,
                            process.StartTime.ToUniversalTime(),
                            Path.GetFullPath(actualPath)));
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (Win32Exception)
                {
                }
            }
        }

        return matches;
    }

    public bool TryRequestGracefulClose(WindowsProcessInstance process)
    {
        using var systemProcess = GetVerifiedProcess(process);
        return systemProcess?.CloseMainWindow() ?? false;
    }

    public async Task<bool> WaitForExitAsync(
        WindowsProcessInstance process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var systemProcess = GetVerifiedProcess(process);
        if (systemProcess is null || systemProcess.HasExited)
        {
            return true;
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var combined = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            await systemProcess.WaitForExitAsync(combined.Token);
            return true;
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public void Terminate(WindowsProcessInstance process)
    {
        using var systemProcess = GetVerifiedProcess(process)
            ?? throw new InvalidOperationException("The process instance has already exited.");
        systemProcess.Kill(entireProcessTree: false);
    }

    public bool HasExited(WindowsProcessInstance process)
    {
        using var systemProcess = GetVerifiedProcess(process);
        return systemProcess is null || systemProcess.HasExited;
    }

    private static Process? GetVerifiedProcess(WindowsProcessInstance expected)
    {
        try
        {
            var process = Process.GetProcessById(expected.ProcessId);
            var startedAtUtc = new DateTimeOffset(process.StartTime.ToUniversalTime());
            if (startedAtUtc != expected.StartedAtUtc)
            {
                process.Dispose();
                return null;
            }

            return process;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
