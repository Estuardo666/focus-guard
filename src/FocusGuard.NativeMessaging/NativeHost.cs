using System.IO.Pipes;
using System.Security.Principal;
using FocusGuard.Contracts;

namespace FocusGuard.NativeMessaging;

public interface INativeMessageHandler
{
    ValueTask<byte[]> HandleAsync(ReadOnlyMemory<byte> request, CancellationToken cancellationToken);
}

public interface IExtensionHealthClient
{
    ValueTask<ExtensionHealthResponse> QueryAsync(
        ExtensionHealthRequest request,
        CancellationToken cancellationToken);
}

public sealed class NamedPipeExtensionHealthClient : IExtensionHealthClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(2);
    private readonly string _pipeName;
    private readonly TimeSpan _timeout;

    public NamedPipeExtensionHealthClient(
        string pipeName,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero || _timeout > TimeSpan.FromSeconds(10))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public async ValueTask<ExtensionHealthResponse> QueryAsync(
        ExtensionHealthRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await BoundedPipeFraming.WriteAsync(
                pipe,
                ExtensionHealthProtocol.WriteRequest(request),
                timeout.Token).ConfigureAwait(false);
            var responsePayload = await BoundedPipeFraming.ReadAsync(pipe, timeout.Token).ConfigureAwait(false)
                ?? throw new IOException("Service disconnected before returning health.");
            var response = ExtensionHealthProtocol.ReadResponse(responsePayload);
            if (!response.IsValid)
            {
                throw new InvalidDataException("Service returned an invalid health response.");
            }

            if (response.Value!.CorrelationId != request.CorrelationId)
            {
                throw new InvalidDataException("Service response correlation ID does not match.");
            }

            return response.Value;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested &&
            timeout.IsCancellationRequested)
        {
            throw new TimeoutException("Service health request timed out.");
        }
    }
}

public sealed class ServiceHealthNativeMessageHandler : INativeMessageHandler
{
    private readonly IExtensionHealthClient _client;
    private readonly TimeProvider _timeProvider;

    public ServiceHealthNativeMessageHandler(
        IExtensionHealthClient client,
        TimeProvider? timeProvider = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<byte[]> HandleAsync(
        ReadOnlyMemory<byte> request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var parsed = ExtensionHealthProtocol.ReadRequest(request.Span, nowUtc);
        if (!parsed.IsValid)
        {
            return CreateDegraded(Guid.Empty, nowUtc, "invalidMessage");
        }

        try
        {
            var response = await _client.QueryAsync(parsed.Value!, cancellationToken).ConfigureAwait(false);
            return ExtensionHealthProtocol.WriteResponse(response);
        }
        catch (TimeoutException)
        {
            return CreateDegraded(parsed.Value!.CorrelationId, nowUtc, "serviceTimeout");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return CreateDegraded(parsed.Value!.CorrelationId, nowUtc, "serviceDisconnected");
        }
    }

    private static byte[] CreateDegraded(Guid correlationId, DateTimeOffset utcNow, string errorCode) =>
        ExtensionHealthProtocol.WriteResponse(new ExtensionHealthResponse(
            ExtensionHealthProtocol.CurrentVersion,
            ExtensionHealthProtocol.StatusResponseType,
            correlationId == Guid.Empty ? Guid.NewGuid() : correlationId,
            Guid.Empty,
            ServiceReady: false,
            ExtensionReady: false,
            "Unknown",
            RemainingSeconds: 0,
            utcNow,
            errorCode));
}

public static class NativeHost
{
    public static async Task RunAsync(
        Stream input,
        Stream output,
        INativeMessageHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = await NativeMessageFraming.ReadAsync(input, cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            var response = await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
            await NativeMessageFraming.WriteAsync(output, response, cancellationToken).ConfigureAwait(false);
        }
    }
}
