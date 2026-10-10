using System.Diagnostics;
using FocusGuard.Client.Platform;

namespace FocusGuard.Platform.Windows;

public static class ProcessAgentStartup
{
    private const string AgentFileName = "FocusGuard.ProcessAgent.exe";

    public static bool EnsureRunning(
        string desktopBaseDirectory,
        Func<IEnumerable<string?>>? runningPathProvider = null,
        Func<string, Process?>? processStarter = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(desktopBaseDirectory);
        var agentPath = Path.GetFullPath(Path.Combine(desktopBaseDirectory, AgentFileName));
        if (!File.Exists(agentPath))
        {
            return false;
        }

        runningPathProvider ??= ReadRunningAgentPaths;
        processStarter ??= path => Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path)!
        });

        foreach (var runningPath in runningPathProvider())
        {
            if (string.IsNullOrWhiteSpace(runningPath))
            {
                continue;
            }

            if (string.Equals(
                    Path.GetFullPath(runningPath),
                    agentPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        try
        {
            using var started = processStarter(agentPath);
            return started is not null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static IEnumerable<string?> ReadRunningAgentPaths()
    {
        using var currentProcess = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(
                     Path.GetFileNameWithoutExtension(AgentFileName)))
        {
            using (process)
            {
                string? path = null;
                try
                {
                    if (process.SessionId != currentProcess.SessionId) continue;
                    path = process.MainModule?.FileName;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                    or NotSupportedException
                    or System.ComponentModel.Win32Exception)
                {
                    // An inaccessible unrelated process is not proof that our agent is running.
                }

                yield return path;
            }
        }
    }
}

public sealed class ProcessAgentSupervisor(
    string desktopBaseDirectory,
    TimeProvider? timeProvider = null,
    TimeSpan? checkInterval = null,
    Func<string, bool>? ensureRunning = null,
    Func<TimeSpan, TimeProvider, CancellationToken, Task>? delay = null)
{
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _checkInterval = checkInterval ?? DefaultCheckInterval;
    private readonly Func<string, bool> _ensureRunning = ensureRunning
        ?? new WindowsProcessAgentLauncher().EnsureRunning;
    private readonly Func<TimeSpan, TimeProvider, CancellationToken, Task> _delay = delay
        ?? Task.Delay;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TryEnsureRunning();
            try
            {
                await _delay(_checkInterval, _timeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public bool TryEnsureRunning()
    {
        try
        {
            return _ensureRunning(desktopBaseDirectory);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or System.ComponentModel.Win32Exception
            or System.Security.SecurityException)
        {
            return false;
        }
    }
}

public sealed class WindowsProcessAgentLauncher : IProcessAgentLauncher
{
    public bool EnsureRunning(string desktopBaseDirectory) => ProcessAgentStartup.EnsureRunning(desktopBaseDirectory);
}
