using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace FocusGuard.Ipc.Windows;

[SupportedOSPlatform("windows")]
public sealed class AuthenticatedIpcClient : IDisposable
{
    private readonly IpcDeploymentManifest _manifest;
    private readonly CurrentProcessMetadataConsent _consent;

    public AuthenticatedIpcClient(string installRoot, IpcClientRole role)
    {
        _manifest = IpcDeploymentManifest.ReadProtected(installRoot);
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != _manifest.OwnerUserSid)
            throw new UnauthorizedAccessException("Only the provisioned owner can use IPC.");
        using var current = Process.GetCurrentProcess();
        using var observed = WindowsPipeIdentity.CaptureProcess((uint)current.Id,
            identity.User.Value, "S-1-5-5-1-1", (uint)current.SessionId);
        var evidence = ProtectedIpcDeployment.Observe(observed, installRoot);
        var policy = new IpcAuthorizationPolicy(new(identity.User.Value, "S-1-5-5-1-1", (uint)current.SessionId),
            installRoot, _manifest.Binaries);
        var decision = policy.Authenticate(evidence);
        if (!decision.Allowed || decision.Role != role)
            throw new IpcPolicyDeniedException(decision.Allowed ? "operationDenied" : decision.Code,
                "Client deployment/role proof denied.");
        _consent = CurrentProcessMetadataConsent.Enable(_manifest.ClientMetadataQueryConsent);
    }

    public async Task<NamedPipeClientStream> ConnectAsync(string name, CancellationToken token)
    {
        NamedPipeClientStream pipe;
        try { pipe = await WindowsAuthenticatedPipeStreams.ConnectAsync(name, token).ConfigureAwait(false); }
        catch (Win32Exception exception)
        {
            throw new IOException("Native IPC connection failed.", exception);
        }
        try
        {
            token.ThrowIfCancellationRequested();
            IpcServerAuthentication.Verify(pipe, new(_manifest.Service.ServiceName,
                Path.Combine(_manifest.InstallRoot, _manifest.Service.FileName), _manifest.Service.Sha256,
                _manifest.Service.ManagedSha256));
            token.ThrowIfCancellationRequested();
            return pipe;
        }
        catch (Win32Exception exception)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw new UnauthorizedAccessException("Native service proof unavailable.", exception);
        }
        catch { await pipe.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public void Dispose() => _consent.Dispose();
}
