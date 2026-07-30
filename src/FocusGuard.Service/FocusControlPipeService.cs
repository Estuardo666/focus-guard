using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using FocusGuard.Contracts;
using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

public sealed class FocusControlPipeService(
    IFocusService focusService,
    string? pipeName = null) : BackgroundService
{
    private const int MaximumConcurrentConnections = 16;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly string _pipeName = pipeName ?? IpcProtocol.PipeName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var capacity = new SemaphoreSlim(MaximumConcurrentConnections);
        var connections = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await capacity.WaitAsync(stoppingToken).ConfigureAwait(false);
                var pipe = CreatePipe(_pipeName);
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                connections.RemoveAll(task => task.IsCompleted);
                connections.Add(HandleClientAsync(pipe, capacity, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await Task.WhenAll(connections).ConfigureAwait(false);
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
            catch (Exception exception) when (
                exception is IOException
                or InvalidDataException
                or UnauthorizedAccessException
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
        IpcCodecResult<IpcEnvelope> response = request.MessageType switch
        {
            IpcMessageType.StatusRequest => await HandleStatusAsync(request, timeout.Token),
            IpcMessageType.ActivateSessionRequest => await HandleActivationAsync(request, timeout.Token),
            IpcMessageType.StopSessionRequest => await HandleStopAsync(request, timeout.Token),
            IpcMessageType.ActivityRequest => await HandleActivityAsync(request, timeout.Token),
            IpcMessageType.CaptureRequest => await HandleCaptureAsync(request, timeout.Token),
            IpcMessageType.SessionSummaryRequest => await HandleSessionSummaryAsync(request, timeout.Token),
            IpcMessageType.ResolveCapturesRequest => await HandleResolveCapturesAsync(request, timeout.Token),
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
        return IpcProtocol.CreateActivityResponse(envelope.CorrelationId, activity);
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
