using System.ComponentModel;
using FocusGuard.Application;

namespace FocusGuard.Infrastructure.Windows.Processes;

public sealed record ProcessTerminationDeveloperOptions(
    bool EnableRealProcessTermination,
    IReadOnlyList<string> AllowedExecutablePaths,
    bool AllowAnyNonProtectedTarget = false)
{
    public static ProcessTerminationDeveloperOptions Disabled { get; } = new(false, Array.Empty<string>());
}

public sealed class WindowsApplicationProcessController : IApplicationProcessController
{
    private const string TestTargetFileName = "FocusGuard.TestTarget.exe";
    private readonly ProcessTerminationDeveloperOptions _developerOptions;
    private readonly IWindowsProcessGateway _gateway;
    private readonly ProtectedProcessPolicy _protectedProcessPolicy;

    public WindowsApplicationProcessController(
        ProcessTerminationDeveloperOptions developerOptions,
        IWindowsProcessGateway gateway,
        ProtectedProcessPolicy? protectedProcessPolicy = null)
    {
        _developerOptions = developerOptions ?? throw new ArgumentNullException(nameof(developerOptions));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _protectedProcessPolicy = protectedProcessPolicy ?? new ProtectedProcessPolicy();
    }

    public async Task<ProcessTerminationResult> TerminateAsync(
        ApplicationProcessTarget target,
        ProcessTerminationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        if (options.GracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Grace period cannot be negative.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new(ProcessTerminationStatus.Cancelled, target.ProcessId, "operation-cancelled");
        }

        if (!_developerOptions.EnableRealProcessTermination)
        {
            return new(ProcessTerminationStatus.Disabled, target.ProcessId, "real-termination-disabled",
                "Real process termination is disabled.");
        }

        string canonicalTargetPath;
        try
        {
            canonicalTargetPath = Path.GetFullPath(target.ExecutablePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new(ProcessTerminationStatus.NotAllowed, target.ProcessId, "invalid-target-path");
        }

        if (_protectedProcessPolicy.IsProtected(canonicalTargetPath))
        {
            return new(ProcessTerminationStatus.ProtectedProcess, target.ProcessId, "protected-process");
        }

        if (!_developerOptions.AllowAnyNonProtectedTarget
            && (!string.Equals(Path.GetFileName(canonicalTargetPath), TestTargetFileName, StringComparison.OrdinalIgnoreCase)
            || !_developerOptions.AllowedExecutablePaths
                .Select(Path.GetFullPath)
                .Contains(canonicalTargetPath, StringComparer.OrdinalIgnoreCase)))
        {
            return new(ProcessTerminationStatus.NotAllowed, target.ProcessId, "target-not-allowlisted");
        }

        try
        {
            var candidates = _gateway.FindByExecutablePath(canonicalTargetPath)
                .Where(candidate => target.ProcessId is null || candidate.ProcessId == target.ProcessId)
                .Where(candidate => target.StartedAtUtc is null || candidate.StartedAtUtc == target.StartedAtUtc)
                .ToArray();
            if (candidates.Length == 0)
            {
                return new(ProcessTerminationStatus.NotFound, target.ProcessId, "target-not-found");
            }

            if (candidates.Length > 1)
            {
                return new(ProcessTerminationStatus.Failed, null, "ambiguous-target",
                    "Specify a process ID and start time.");
            }

            var process = candidates[0];
            if (_gateway.HasExited(process))
            {
                return new(ProcessTerminationStatus.AlreadyExited, process.ProcessId);
            }

            _gateway.TryRequestGracefulClose(process);
            if (await _gateway.WaitForExitAsync(process, options.GracePeriod, cancellationToken))
            {
                return new(ProcessTerminationStatus.Terminated, process.ProcessId);
            }

            _gateway.Terminate(process);
            return _gateway.HasExited(process)
                ? new(ProcessTerminationStatus.Terminated, process.ProcessId)
                : new(ProcessTerminationStatus.Failed, process.ProcessId, "termination-not-confirmed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(ProcessTerminationStatus.Cancelled, target.ProcessId, "operation-cancelled");
        }
        catch (UnauthorizedAccessException)
        {
            return new(ProcessTerminationStatus.AccessDenied, target.ProcessId, "access-denied");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 5)
        {
            return new(ProcessTerminationStatus.AccessDenied, target.ProcessId, "access-denied");
        }
        catch (InvalidOperationException)
        {
            return new(ProcessTerminationStatus.AlreadyExited, target.ProcessId, "process-instance-ended");
        }
        catch (Exception)
        {
            return new(ProcessTerminationStatus.Failed, target.ProcessId, "process-operation-failed");
        }
    }
}
