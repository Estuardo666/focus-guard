using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;
using FocusGuard.Contracts;

namespace FocusGuard.Ipc.Windows;

/// <summary>Protected deployment and native observer are the only sources of caller identity.</summary>
[SupportedOSPlatform("windows")]
public sealed class AuthenticatedIpcServer : IIpcServerSecurity, IDisposable
{
    private readonly object _gate = new();
    private readonly IpcDeploymentManifest _manifest;
    private OwnerLogonScope? _ownerScope;
    private readonly Dictionary<uint, ObservedPipeClient> _ownerProcesses = [];

    public AuthenticatedIpcServer(string installRoot) => _manifest = IpcDeploymentManifest.ReadProtected(installRoot);

    public NamedPipeServerStream CreatePipe(string name, bool firstInstance, int maximumInstances)
    {
        // The stable owner SID permits initial logon discovery. Native role proof
        // then binds the service lifetime to one owner logon/session before dispatch.
        var security = WindowsAuthenticatedPipeStreams.CreateOwnerSecurity(_manifest.OwnerUserSid, "S-1-5-19");
        return WindowsAuthenticatedPipeStreams.CreateServer(name, security, firstInstance, maximumInstances);
    }

    public void AuthorizeControl(NamedPipeServerStream pipe, IpcMessageType requestType) =>
        Authorize(pipe, policy => policy.Policy.AuthorizeControl(policy.Peer, requestType));
    public void AuthorizeAgent(NamedPipeServerStream pipe, string requestType) =>
        Authorize(pipe, policy => policy.Policy.AuthorizeAgent(policy.Peer, requestType));
    public void AuthorizeBrowser(NamedPipeServerStream pipe, string requestType) =>
        Authorize(pipe, policy => policy.Policy.AuthorizeBrowser(policy.Peer, requestType));

    private void Authorize(NamedPipeServerStream pipe, Func<(IpcAuthorizationPolicy Policy, IpcPeerEvidence Peer), IpcAccessDecision> decide)
    {
        ObservedPipeClient observed;
        try { observed = WindowsPipeIdentity.CaptureClient(pipe); }
        catch (Win32Exception exception) { throw new UnauthorizedAccessException("Native caller identity unavailable.", exception); }
        var retained = false;
        try
        {
            var peer = ProtectedIpcDeployment.Observe(observed, _manifest.InstallRoot);
            if (peer.UserSid != _manifest.OwnerUserSid) throw new UnauthorizedAccessException("IPC owner denied.");
            lock (_gate)
            {
                foreach (var id in _ownerProcesses.Keys.ToArray())
                    if (!WindowsPipeIdentity.IsAlive(_ownerProcesses[id]))
                    {
                        _ownerProcesses[id].Dispose(); _ownerProcesses.Remove(id);
                    }
                if (_ownerProcesses.Count == 0) _ownerScope = null;
                var scope = _ownerScope ?? new(peer.UserSid, peer.LogonSid, peer.SessionId);
                var policy = new IpcAuthorizationPolicy(scope, _manifest.InstallRoot, _manifest.Binaries);
                var decision = decide((policy, peer));
                if (!decision.Allowed) throw new IpcPolicyDeniedException(decision.Code, "IPC caller or operation denied.");
                if (!WindowsPipeIdentity.IsAlive(observed)) throw new UnauthorizedAccessException("IPC caller exited.");
                _ownerScope = scope;
                if (!_ownerProcesses.ContainsKey(peer.ProcessId))
                {
                    if (_ownerProcesses.Count >= 32) throw new UnauthorizedAccessException("Owner process bound reached.");
                    _ownerProcesses.Add(peer.ProcessId, observed); retained = true;
                }
            }
        }
        finally { if (!retained) observed.Dispose(); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var process in _ownerProcesses.Values) process.Dispose();
            _ownerProcesses.Clear(); _ownerScope = null;
        }
    }
}
