using System.IO.Pipes;
using System.Runtime.Versioning;
using FocusGuard.Contracts;

namespace FocusGuard.Ipc.Windows;

[SupportedOSPlatform("windows")]
public sealed class VerifiedPipeConnectionFactory(string installRoot, IpcClientRole role) : IPipeConnectionFactory, IDisposable
{
    private readonly AuthenticatedIpcClient _client = new(installRoot, role);
    public async ValueTask<NamedPipeClientStream> ConnectAsync(string name, CancellationToken token) =>
        await _client.ConnectAsync(name, token).ConfigureAwait(false);
    public void Dispose() => _client.Dispose();
}
