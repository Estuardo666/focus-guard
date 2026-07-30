using System.IO.Pipes;
using System.Security.Principal;
using FocusGuard.Contracts;

namespace FocusGuard.Desktop;

public interface IFocusControlClient
{
    ValueTask<StatusResponse> GetStatusAsync(CancellationToken cancellationToken);

    ValueTask<ActivateSessionResponse> ActivateAsync(
        ActivateSessionRequest request,
        CancellationToken cancellationToken);

    ValueTask<StopSessionResponse> StopAsync(CancellationToken cancellationToken);

    ValueTask<ActivityResponse> GetActivityAsync(
        int maximumCount,
        CancellationToken cancellationToken);

    ValueTask<CaptureResponse> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken);

    ValueTask<SessionSummaryResponse> GetSessionSummaryAsync(
        Guid? sessionId,
        CancellationToken cancellationToken);

    ValueTask<ResolveCapturesResponse> ResolveCapturesAsync(
        ResolveCapturesRequest request,
        CancellationToken cancellationToken);
}

public sealed class NamedPipeFocusControlClient(
    string? pipeName = null,
    TimeSpan? timeout = null) : IFocusControlClient
{
    private readonly string _pipeName = pipeName ?? IpcProtocol.PipeName;
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(5);

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
        await using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
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
