using System.IO.Pipes;
using System.Runtime.Versioning;

namespace FocusGuard.Ipc.Windows;

public sealed record TrustedIpcServer(string ServiceName, string ImagePath, string Sha256, string? ManagedSha256 = null);

public static class IpcServerAuthentication
{
    public static bool Matches(uint pipeServerPid, ObservedServiceIdentity scm, TrustedIpcServer trusted,
        string observedImagePath, string observedHash, bool protectedDeployment, string? observedManagedHash = null, uint serverSessionId = 0)
    {
        ArgumentNullException.ThrowIfNull(scm);
        ArgumentNullException.ThrowIfNull(trusted);
        return pipeServerPid != 0 && scm.ProcessId == pipeServerPid && scm.State == 4 && scm.ServiceType == 0x10 && serverSessionId == 0
            && string.Equals(scm.ServiceName, trusted.ServiceName, StringComparison.Ordinal)
            && string.Equals(scm.Account, @"NT AUTHORITY\LocalService", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(scm.CommandLine.Trim(), trusted.ImagePath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scm.CommandLine.Trim(), '"' + trusted.ImagePath + '"', StringComparison.OrdinalIgnoreCase))
            && protectedDeployment && Path.IsPathFullyQualified(trusted.ImagePath)
            && string.Equals(scm.ImagePath, trusted.ImagePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(observedImagePath, trusted.ImagePath, StringComparison.OrdinalIgnoreCase)
            && trusted.Sha256 is { Length: 64 } && trusted.Sha256.All(Uri.IsHexDigit)
            && string.Equals(observedHash, trusted.Sha256, StringComparison.OrdinalIgnoreCase)
            && (trusted.ManagedSha256 is null || string.Equals(trusted.ManagedSha256, observedManagedHash, StringComparison.OrdinalIgnoreCase));
    }

    [SupportedOSPlatform("windows")]
    public static void Verify(NamedPipeClientStream pipe, TrustedIpcServer trusted)
    {
        var pid = WindowsPipeIdentity.ServerProcessId(pipe);
        var scm = WindowsServiceIdentity.Read(trusted.ServiceName);
        var sessionId = WindowsPipeIdentity.ServerSessionId(pipe);
        if (scm.ProcessId != pid || scm.State != 4 || scm.ServiceType != 0x10 || sessionId != 0)
            throw new UnauthorizedAccessException("Pipe server is not the running own-process service.");
        var evidence = ProtectedIpcDeployment.ObserveServiceImage(trusted.ImagePath);
        if (!Matches(pid, scm, trusted, evidence.FinalImagePath, evidence.Sha256,
                true, evidence.ManagedSha256, sessionId))
            throw new UnauthorizedAccessException("Service image proof denied.");
        // Re-read to reject SCM changes during native image validation.
        var current = WindowsServiceIdentity.Read(trusted.ServiceName);
        if (current != scm || WindowsPipeIdentity.ServerProcessId(pipe) != pid
            || WindowsPipeIdentity.ServerSessionId(pipe) != sessionId)
            throw new UnauthorizedAccessException("Service changed during verification.");
    }
}
