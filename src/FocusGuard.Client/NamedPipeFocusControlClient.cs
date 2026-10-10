using System.IO.Pipes;
using System.Security.Principal;
using FocusGuard.Contracts;

namespace FocusGuard.Client;

public sealed partial class NamedPipeFocusControlClient(
    string? pipeName = null,
    TimeSpan? timeout = null,
    IPipeConnectionFactory? connectionFactory = null) : IFocusControlClient, IReviewedSessionControlClient
{
    private readonly string _pipeName = pipeName ?? IpcProtocol.PipeName;
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(5);
    private readonly IPipeConnectionFactory _connections = connectionFactory ?? PipeConnectionFactories.Current;

    public async ValueTask<PrepareSessionResponse> PrepareAsync(
        PrepareSessionRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var envelope = CycleIpcProtocol.CreatePrepareRequest(correlationId, request);
        if (!envelope.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", envelope.Errors));
        }

        var response = await SendAsync(envelope.Value!, correlationId, cancellationToken);
        var decoded = CycleIpcProtocol.ReadPrepareResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    public async ValueTask<ActivatePreparedSessionResponse> ActivatePreparedAsync(
        ActivatePreparedSessionRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var envelope = CycleIpcProtocol.CreateActivatePreparedRequest(correlationId, request);
        if (!envelope.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", envelope.Errors));
        }

        var response = await SendAsync(envelope.Value!, correlationId, cancellationToken);
        var decoded = CycleIpcProtocol.ReadActivatePreparedResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    public async ValueTask<SkipBreakResponse> SkipBreakAsync(
        SkipBreakRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var envelope = CycleIpcProtocol.CreateSkipBreakRequest(correlationId, request);
        if (!envelope.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", envelope.Errors));
        }

        var response = await SendAsync(envelope.Value!, correlationId, cancellationToken);
        var decoded = CycleIpcProtocol.ReadSkipBreakResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    public async ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var request = IpcProtocol.CreateStatusRequest(correlationId);
        var response = await SendAsync(request.Value!, correlationId, cancellationToken);
        var decoded = IpcProtocol.ReadStatusResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    public async ValueTask<ActivateSessionResponse> ActivateAsync(
        ActivateSessionRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var envelope = IpcProtocol.CreateActivateSessionRequest(correlationId, request);
        if (!envelope.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", envelope.Errors));
        }

        var response = await SendAsync(envelope.Value!, correlationId, cancellationToken);
        var decoded = IpcProtocol.ReadActivateSessionResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    public async ValueTask<StopSessionResponse> StopAsync(CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var request = IpcProtocol.CreateStopSessionRequest(correlationId);
        var response = await SendAsync(request.Value!, correlationId, cancellationToken);
        var decoded = IpcProtocol.ReadStopSessionResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    public async ValueTask<ActivityResponse> GetActivityAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var request = IpcProtocol.CreateActivityRequest(
            correlationId,
            new ActivityRequest(maximumCount));
        if (!request.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCount),
                string.Join(" ", request.Errors));
        }

        var response = await SendAsync(request.Value!, correlationId, cancellationToken);
        var decoded = IpcProtocol.ReadActivityResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    public async ValueTask<CaptureResponse> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var envelope = IpcProtocol.CreateCaptureRequest(correlationId, request);
        if (!envelope.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", envelope.Errors));
        }

        var response = await SendAsync(envelope.Value!, correlationId, cancellationToken);
        var decoded = IpcProtocol.ReadCaptureResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    public async ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(
        Guid? sessionId,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var envelope = IpcProtocol.CreateSessionSummaryRequest(
            correlationId,
            new SessionSummaryRequest(sessionId));
        if (!envelope.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", envelope.Errors));
        }

        var response = await SendAsync(envelope.Value!, correlationId, cancellationToken);
        var decoded = IpcProtocol.ReadSessionSummaryResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    public async ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(
        ResolveCapturesRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var envelope = IpcProtocol.CreateResolveCapturesRequest(correlationId, request);
        if (!envelope.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", envelope.Errors));
        }

        var response = await SendAsync(envelope.Value!, correlationId, cancellationToken);
        var decoded = IpcProtocol.ReadResolveCapturesResponse(response);
        return decoded.IsValid
            ? decoded.Value!
            : throw new InvalidDataException(string.Join(" ", decoded.Errors));
    }

    private async ValueTask<IpcEnvelope> SendAsync(
        IpcEnvelope request,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        await using var pipe = await _connections.ConnectAsync(_pipeName, timeout.Token).ConfigureAwait(false);
        await BoundedPipeFraming.WriteAsync(
            pipe,
            IpcProtocol.WriteEnvelope(request),
            timeout.Token).ConfigureAwait(false);
        var payload = await BoundedPipeFraming.ReadAsync(pipe, timeout.Token).ConfigureAwait(false)
            ?? throw new EndOfStreamException("FocusGuard service closed the control pipe.");
        var response = IpcProtocol.ReadEnvelope(payload);
        if (!response.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", response.Errors));
        }

        if (response.Value!.CorrelationId != correlationId)
        {
            throw new InvalidDataException("FocusGuard service returned the wrong correlation ID.");
        }

        return response.Value;
    }
}
