using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using FocusGuard.Contracts;
using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

public sealed class FocusControlPipeService(
    IFocusService focusService,
    string? pipeName = null,
    DesktopWorkflowService? workflows = null,
    IIpcServerSecurity? security = null,
    Func<string, Exception, Task>? failureReporter = null) : BackgroundService
{
    private const int MaximumConcurrentConnections = 16;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly string _pipeName = pipeName ?? IpcProtocol.PipeName;
    private readonly IIpcServerSecurity? _security = security;
    private readonly Func<string, Exception, Task> _failureReporter = failureReporter ?? ((worker, exception) => ServiceFailureLog.WriteAsync(worker, exception));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunListenerAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ReportListenerFailureAsync(exception).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RunListenerAsync(CancellationToken stoppingToken)
    {
        using var capacity = new SemaphoreSlim(MaximumConcurrentConnections);
        var connections = new List<Task>();
        NamedPipeServerStream? pendingPipe = null;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await capacity.WaitAsync(stoppingToken).ConfigureAwait(false);
                pendingPipe ??= _security is null ? CreatePipe(_pipeName) : _security.CreatePipe(_pipeName, connections.Count == 0, MaximumConcurrentConnections + 1);
                try { await pendingPipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false); }
                catch (IOException)
                {
                    var failed = pendingPipe;
                    pendingPipe = _security is null ? CreatePipe(_pipeName) : _security.CreatePipe(_pipeName, false, MaximumConcurrentConnections + 1);
                    failed.Dispose(); capacity.Release(); continue;
                }
                var connectedPipe = pendingPipe;
                pendingPipe = null; // The connected-client handler now owns this stream.
                try
                {
                    // Preserve a namespace handle before the connected handler can close.
                    pendingPipe = _security is null ? CreatePipe(_pipeName) : _security.CreatePipe(_pipeName, false, MaximumConcurrentConnections + 1);
                }
                catch { connectedPipe.Dispose(); throw; }
                connections.RemoveAll(task => task.IsCompleted);
                connections.Add(HandleClientAsync(connectedPipe, capacity, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            pendingPipe?.Dispose();
            await Task.WhenAll(connections).ConfigureAwait(false);
        }
    }

    private async Task ReportListenerFailureAsync(Exception exception)
    {
        try
        {
            await _failureReporter($"{nameof(FocusControlPipeService)}.ListenerLoop", exception).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostic failure must not hide the original listener fault.
        }
    }
    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        SemaphoreSlim capacity,
        CancellationToken stoppingToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            try
            {
                await HandleConnectionAsync(pipe, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (OperationCanceledException exception)
            {
                await ServiceFailureLog.WriteAsync(
                    $"{nameof(FocusControlPipeService)}.RequestTimeout",
                    exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                await ServiceFailureLog.WriteIpcRejectionAsync(exception).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is IOException
                or InvalidDataException
                or System.Text.Json.JsonException)
            {
                // Invalid or disconnected clients are isolated to one request.
            }
            catch (Exception exception)
            {
                await ServiceFailureLog.WriteAsync(nameof(FocusControlPipeService), exception);
            }
            finally
            {
                capacity.Release();
            }
        }
    }

    internal async Task HandleConnectionAsync(Stream pipe, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        var payload = await BoundedPipeFraming.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
        if (payload is null)
        {
            return;
        }

        var envelopeResult = IpcProtocol.ReadEnvelope(payload);
        if (!envelopeResult.IsValid)
        {
            throw new InvalidDataException("Control request envelope is invalid.");
        }

        var request = envelopeResult.Value!;
        if (_security is not null)
            _security.AuthorizeControl(pipe as NamedPipeServerStream
                ?? throw new UnauthorizedAccessException("Native pipe proof required."), request.MessageType);
        IpcCodecResult<IpcEnvelope> response = request.MessageType switch
        {
            IpcMessageType.PrepareSessionRequest => await HandlePrepareAsync(request, timeout.Token),
            IpcMessageType.ActivatePreparedSessionRequest => await HandleActivatePreparedAsync(request, timeout.Token),
            IpcMessageType.SkipBreakRequest => await HandleSkipBreakAsync(request, timeout.Token),
            IpcMessageType.ReviewedSkipBreakRequest => await HandleReviewedSkipAsync(request, timeout.Token),
            IpcMessageType.ReviewedStopSessionRequest => await HandleReviewedStopAsync(request, timeout.Token),
            IpcMessageType.StatusRequest => await HandleStatusAsync(request, timeout.Token),
            IpcMessageType.ActivateSessionRequest => await HandleActivationAsync(request, timeout.Token),
            IpcMessageType.StopSessionRequest => await HandleStopAsync(request, timeout.Token),
            IpcMessageType.ActivityRequest => await HandleActivityAsync(request, timeout.Token),
            IpcMessageType.CaptureRequest => await HandleCaptureAsync(request, timeout.Token),
            IpcMessageType.SessionSummaryRequest => await HandleSessionSummaryAsync(request, timeout.Token),
            IpcMessageType.ResolveCapturesRequest => await HandleResolveCapturesAsync(request, timeout.Token),
            IpcMessageType.TodayRequest => await HandleTodayAsync(request, timeout.Token),
            IpcMessageType.ScheduleListRequest => await HandleSchedulesAsync(request, timeout.Token),
            IpcMessageType.ScheduleChangeRequest => await HandleScheduleChangeAsync(request, timeout.Token),
            IpcMessageType.RevisionedScheduleListRequest => await HandleRevisionedScheduleListAsync(request, timeout.Token),
            IpcMessageType.RevisionedScheduleChangeRequest => await HandleRevisionedScheduleChangeAsync(request, timeout.Token),
            _ => IpcCodecResult<IpcEnvelope>.Failure("Unsupported control request type.")
        };
        if (!response.IsValid)
        {
            throw new InvalidDataException(string.Join(" ", response.Errors));
        }

        await BoundedPipeFraming.WriteAsync(
            pipe,
            IpcProtocol.WriteEnvelope(response.Value!),
            timeout.Token).ConfigureAwait(false);
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandlePrepareAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = CycleIpcProtocol.ReadPrepareRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var prepared = await focusService.PrepareSessionAsync(request.Value!, cancellationToken);
        return prepared.IsValid
            ? CycleIpcProtocol.CreatePrepareResponse(envelope.CorrelationId, prepared.Value!)
            : IpcCodecResult<IpcEnvelope>.Failure([.. prepared.Errors]);
    }

    private async Task<IpcCodecResult<IpcEnvelope>> HandleTodayAsync(IpcEnvelope envelope, CancellationToken token)
    {
        var decoded = IpcProtocol.ReadTodayRequest(envelope);
        if (!decoded.IsValid || workflows is null) return IpcCodecResult<IpcEnvelope>.Failure("Today is unavailable or invalid.");
        return IpcProtocol.CreateTodayResponse(envelope.CorrelationId, await workflows.TodayAsync(decoded.Value!, token));
    }

    private async Task<IpcCodecResult<IpcEnvelope>> HandleSchedulesAsync(IpcEnvelope envelope, CancellationToken token)
    {
        if (!IpcProtocol.ReadScheduleListRequest(envelope).IsValid || workflows is null) return IpcCodecResult<IpcEnvelope>.Failure("Scheduling is unavailable.");
        return IpcProtocol.CreateScheduleResponse(envelope.CorrelationId, await workflows.ListAsync(token));
    }

    private async Task<IpcCodecResult<IpcEnvelope>> HandleScheduleChangeAsync(IpcEnvelope envelope, CancellationToken token)
    {
        var decoded = IpcProtocol.ReadScheduleChangeRequest(envelope);
        if (!decoded.IsValid || workflows is null) return IpcCodecResult<IpcEnvelope>.Failure("Schedule change is unavailable or invalid.");
        return IpcProtocol.CreateScheduleResponse(envelope.CorrelationId, await workflows.ChangeAsync(decoded.Value!, token));
    }

    private async Task<IpcCodecResult<IpcEnvelope>> HandleRevisionedScheduleListAsync(IpcEnvelope envelope, CancellationToken token)
    {
        var decoded = IpcProtocol.ReadRevisionedScheduleListRequest(envelope);
        if (!decoded.IsValid || workflows is null) return IpcCodecResult<IpcEnvelope>.Failure("Scheduling is unavailable.");
        return IpcProtocol.CreateRevisionedScheduleResponse(envelope.CorrelationId,
            await workflows.ListRevisionedAsync(token, decoded.Value!.IncludeNextEndUtc).ConfigureAwait(false));
    }

    private async Task<IpcCodecResult<IpcEnvelope>> HandleRevisionedScheduleChangeAsync(IpcEnvelope envelope, CancellationToken token)
    {
        var decoded = IpcProtocol.ReadRevisionedScheduleChangeRequest(envelope);
        if (!decoded.IsValid || workflows is null) return IpcCodecResult<IpcEnvelope>.Failure("Scheduling is unavailable or invalid.");
        return IpcProtocol.CreateRevisionedScheduleResponse(envelope.CorrelationId,
            await workflows.ChangeRevisionedAsync(decoded.Value!, token).ConfigureAwait(false));
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandleActivatePreparedAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = CycleIpcProtocol.ReadActivatePreparedRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var activation = await focusService.ActivatePreparedSessionAsync(request.Value!, cancellationToken);
        return activation.IsValid
            ? CycleIpcProtocol.CreateActivatePreparedResponse(envelope.CorrelationId, activation.Value!)
            : IpcCodecResult<IpcEnvelope>.Failure([.. activation.Errors]);
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandleSkipBreakAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = CycleIpcProtocol.ReadSkipBreakRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var skipped = await focusService.SkipBreakAsync(request.Value!, cancellationToken);
        return skipped.IsValid
            ? CycleIpcProtocol.CreateSkipBreakResponse(envelope.CorrelationId, skipped.Value!)
            : IpcCodecResult<IpcEnvelope>.Failure([.. skipped.Errors]);
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandleStatusAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = IpcProtocol.ReadStatusRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var status = await focusService.GetStatusAsync(cancellationToken);
        return IpcProtocol.CreateStatusResponse(envelope.CorrelationId, status);
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandleActivationAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = IpcProtocol.ReadActivateSessionRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var activation = await focusService.ActivateAsync(request.Value!, cancellationToken);
        return activation.IsValid
            ? IpcProtocol.CreateActivateSessionResponse(envelope.CorrelationId, activation.Value!)
            : IpcCodecResult<IpcEnvelope>.Failure([.. activation.Errors]);
    }

    private async Task<IpcCodecResult<IpcEnvelope>> HandleReviewedStopAsync(IpcEnvelope envelope, CancellationToken token)
    {
        var decoded = IpcProtocol.ReadReviewedStopRequest(envelope);
        if (!decoded.IsValid) return IpcCodecResult<IpcEnvelope>.Failure([.. decoded.Errors]);
        return IpcProtocol.CreateStopSessionResponse(envelope.CorrelationId,
            await focusService.StopReviewedAsync(decoded.Value!, token).ConfigureAwait(false));
    }

    private async Task<IpcCodecResult<IpcEnvelope>> HandleReviewedSkipAsync(IpcEnvelope envelope, CancellationToken token)
    {
        var decoded = IpcProtocol.ReadReviewedSkipRequest(envelope);
        if (!decoded.IsValid) return IpcCodecResult<IpcEnvelope>.Failure([.. decoded.Errors]);
        var result = await focusService.SkipReviewedAsync(decoded.Value!, token).ConfigureAwait(false);
        return result.IsValid ? CycleIpcProtocol.CreateSkipBreakResponse(envelope.CorrelationId, result.Value!)
            : IpcCodecResult<IpcEnvelope>.Failure([.. result.Errors]);
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandleStopAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = IpcProtocol.ReadStopSessionRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var stop = await focusService.StopNormalSessionAsync(cancellationToken);
        return IpcProtocol.CreateStopSessionResponse(envelope.CorrelationId, stop);
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandleActivityAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = IpcProtocol.ReadActivityRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var activity = await focusService.GetActivityAsync(request.Value!, cancellationToken);
        return CreateBoundedActivityResponse(envelope.CorrelationId, activity);
    }

    private static IpcCodecResult<IpcEnvelope> CreateBoundedActivityResponse(
        Guid correlationId,
        ActivityResponse activity)
    {
        var events = activity.Events;
        var low = 0;
        var high = events.Count;
        IpcCodecResult<IpcEnvelope>? best = null;

        // ActivityRequest.MaximumCount is a ceiling. Keep the newest prefix that
        // fits both the nested JSON payload limit and the outer framed envelope.
        while (low <= high)
        {
            var count = low + ((high - low) / 2);
            var candidate = IpcProtocol.CreateActivityResponse(
                correlationId,
                new ActivityResponse(events.Take(count).ToArray()));
            if (!candidate.IsValid)
            {
                if (candidate.Errors.Contains("Payload exceeds the maximum size.", StringComparer.Ordinal))
                {
                    high = count - 1;
                    continue;
                }

                return candidate;
            }

            try
            {
                _ = IpcProtocol.WriteEnvelope(candidate.Value!);
                best = candidate;
                low = count + 1;
            }
            catch (InvalidDataException exception) when (
                exception.Message == "IPC envelope length is outside the supported range.")
            {
                high = count - 1;
            }
            catch (InvalidDataException exception)
            {
                return IpcCodecResult<IpcEnvelope>.Failure(exception.Message);
            }
        }

        return best ?? IpcProtocol.CreateActivityResponse(correlationId, new ActivityResponse([]));
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandleCaptureAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = IpcProtocol.ReadCaptureRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var capture = await focusService.CaptureAsync(request.Value!, cancellationToken);
        return IpcProtocol.CreateCaptureResponse(envelope.CorrelationId, capture);
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandleSessionSummaryAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = IpcProtocol.ReadSessionSummaryRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var summary = await focusService.GetSessionSummaryAsync(request.Value!, cancellationToken);
        return IpcProtocol.CreateSessionSummaryResponse(envelope.CorrelationId, summary);
    }

    private async ValueTask<IpcCodecResult<IpcEnvelope>> HandleResolveCapturesAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var request = IpcProtocol.ReadResolveCapturesRequest(envelope);
        if (!request.IsValid)
        {
            return IpcCodecResult<IpcEnvelope>.Failure([.. request.Errors]);
        }

        var resolved = await focusService.ResolveCapturesAsync(request.Value!, cancellationToken);
        return IpcProtocol.CreateResolveCapturesResponse(envelope.CorrelationId, resolved);
    }

    private static NamedPipeServerStream CreatePipe(string pipeName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        var serviceIdentity = WindowsIdentity.GetCurrent().User;
        if (serviceIdentity is not null)
        {
            security.AddAccessRule(new PipeAccessRule(
                serviceIdentity,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            IpcProtocol.MaxPayloadBytes + sizeof(uint),
            IpcProtocol.MaxPayloadBytes + sizeof(uint),
            security);
    }
}
