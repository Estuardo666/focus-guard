using System.IO.Pipes;
using System.Security.Principal;
using FocusGuard.Contracts;

namespace FocusGuard.ProcessAgent;

public interface IProcessAgentClient
{
    ValueTask<ProcessAgentResponse> ExchangeAsync(ProcessAgentRequest request, CancellationToken cancellationToken);
}

public sealed class NamedPipeProcessAgentClient : IProcessAgentClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private readonly string _pipeName;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _clock;
    private readonly IPipeConnectionFactory _connections;
    internal string PipeName => _pipeName;

    public NamedPipeProcessAgentClient(string? pipeName = null, TimeSpan? timeout = null, TimeProvider? clock = null,
        IPipeConnectionFactory? connectionFactory = null)
    {
        _pipeName = pipeName ?? ProcessAgentProtocol.PipeName;
        ArgumentException.ThrowIfNullOrWhiteSpace(_pipeName);
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero || _timeout > DefaultTimeout) throw new ArgumentOutOfRangeException(nameof(timeout));
        _clock = clock ?? TimeProvider.System;
        _connections = connectionFactory ?? PipeConnectionFactories.Current;
    }

    public async ValueTask<ProcessAgentResponse> ExchangeAsync(ProcessAgentRequest request, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(_timeout, _clock);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await using var pipe = await _connections.ConnectAsync(_pipeName, timeout.Token).ConfigureAwait(false);
            await BoundedPipeFraming.WriteAsync(pipe, ProcessAgentProtocol.WriteRequest(request), timeout.Token).ConfigureAwait(false);
            var payload = await BoundedPipeFraming.ReadAsync(pipe, timeout.Token).ConfigureAwait(false)
                ?? throw new InvalidDataException("Agent response ended before a frame.");
            var response = ProcessAgentProtocol.ReadResponse(payload);
            if (!response.IsValid || response.Value!.CorrelationId != request.CorrelationId)
                throw new InvalidDataException("Agent response is invalid or unrelated.");
            return response.Value!;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Agent exchange deadline expired.", exception);
        }
    }
}
