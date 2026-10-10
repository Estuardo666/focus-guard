using System.IO.Pipes;

namespace FocusGuard.Contracts;

public interface IPipeConnectionFactory
{
    ValueTask<NamedPipeClientStream> ConnectAsync(string name, CancellationToken token);
}

public interface IIpcServerSecurity
{
    NamedPipeServerStream CreatePipe(string name, bool firstInstance, int maximumInstances);
    void AuthorizeControl(NamedPipeServerStream pipe, IpcMessageType requestType);
    void AuthorizeAgent(NamedPipeServerStream pipe, string requestType);
    void AuthorizeBrowser(NamedPipeServerStream pipe, string requestType);
}

public static class PipeConnectionFactories
{
    private static IPipeConnectionFactory _current = new DefaultFactory();
    public static IPipeConnectionFactory Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value ?? throw new ArgumentNullException(nameof(value)));
    }

    private sealed class DefaultFactory : IPipeConnectionFactory
    {
        public async ValueTask<NamedPipeClientStream> ConnectAsync(string name, CancellationToken token)
        {
#if FOCUSGUARD_PRODUCTION
            // Private GUID test names are never installed endpoints and cannot authenticate a production client.
            if (!name.StartsWith("FocusGuard.Tests.", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Production IPC requires native authorization composition.");
#else
            if (!name.StartsWith("FocusGuard.Tests.", StringComparison.Ordinal)
                && !name.StartsWith("FocusGuard.Development.", StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Development transport cannot connect to a production endpoint.");
#endif
            var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous,
                System.Security.Principal.TokenImpersonationLevel.Identification);
            try { await pipe.ConnectAsync(token).ConfigureAwait(false); return pipe; }
            catch { await pipe.DisposeAsync().ConfigureAwait(false); throw; }
        }
    }
}
