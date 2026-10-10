using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using FocusGuard.Contracts;
using Microsoft.Extensions.Hosting;

namespace FocusGuard.Service;

public static class ExtensionHealthPipeConstants
{
    public static readonly TimeSpan ExtensionHeartbeatFreshness = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(3);
}

public sealed class ExtensionHealthState
{
    private readonly Guid _serviceInstanceId = Guid.NewGuid();
    private long _lastExtensionHeartbeatUtcTicks;

    public Guid ServiceInstanceId => _serviceInstanceId;

    public void RecordHeartbeat(DateTimeOffset utcNow) =>
        Interlocked.Exchange(ref _lastExtensionHeartbeatUtcTicks, utcNow.UtcTicks);

    public bool IsExtensionReady(DateTimeOffset utcNow)
    {
        var ticks = Interlocked.Read(ref _lastExtensionHeartbeatUtcTicks);
        return ticks != 0 &&
            utcNow - new DateTimeOffset(ticks, TimeSpan.Zero) <=
            ExtensionHealthPipeConstants.ExtensionHeartbeatFreshness;
    }
}

public sealed class ExtensionHealthPipeService : BackgroundService
{
    private readonly ExtensionHealthState _healthState;
    private readonly IFocusService _focusService;
    private readonly TimeProvider _timeProvider;
    private readonly string _pipeName;
    private readonly PolicyDistributionCoordinator _policyDistribution;
    private readonly IFocusAuditStore _auditStore;
    private readonly IIpcServerSecurity? _security;
    private readonly Func<string, Exception, Task> _failureReporter;

    public ExtensionHealthPipeService(
        ExtensionHealthState healthState,
        IFocusService focusService,
        PolicyDistributionCoordinator? policyDistribution = null,
        TimeProvider? timeProvider = null,
        string? pipeName = null,
        IFocusAuditStore? auditStore = null,
        IIpcServerSecurity? security = null,
        Func<string, Exception, Task>? failureReporter = null)
    {
        _security = security;
        _failureReporter = failureReporter ?? ((worker, exception) => ServiceFailureLog.WriteAsync(worker, exception));
        _healthState = healthState ?? throw new ArgumentNullException(nameof(healthState));
        _focusService = focusService ?? throw new ArgumentNullException(nameof(focusService));
        _policyDistribution = policyDistribution ?? new PolicyDistributionCoordinator(
            new FocusGuard.Monitoring.ProcessMonitorPolicyReceiver(timeProvider),
            timeProvider);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pipeName = pipeName ?? ExtensionHealthProtocol.PipeName;
        _auditStore = auditStore ?? new EmptyFocusAuditStore();
    }

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
        NamedPipeServerStream? pending = null;
        var firstInstance = true;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                pending ??= _security is null ? CreatePipe(_pipeName) : _security.CreatePipe(_pipeName, firstInstance, 2);
                firstInstance = false;
                var pipe = pending;
                try
                {
                    await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                    pending = null;
                    if (_security is not null) pending = _security.CreatePipe(_pipeName, false, 2);
                    await HandleConnectionAsync(pipe, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (IOException)
                {
                    // A client disconnect is isolated to this one-request connection.
                }
                catch (InvalidDataException)
                {
                    // Invalid frames are rejected without affecting the listener loop.
                }
                catch (OperationCanceledException exception)
                {
                    await ServiceFailureLog.WriteAsync(
                        $"{nameof(ExtensionHealthPipeService)}.RequestTimeout",
                        exception);
                }
                catch (UnauthorizedAccessException) { }

                finally
                {
                    if (pending == pipe)
                        pending = stoppingToken.IsCancellationRequested ? null
                            : _security is null ? CreatePipe(_pipeName) : _security.CreatePipe(_pipeName, false, 2);
                    await pipe.DisposeAsync();
                }
            }
        }
        finally { if (pending is not null) await pending.DisposeAsync(); }
    }

    private async Task ReportListenerFailureAsync(Exception exception)
    {
        try
        {
            await _failureReporter($"{nameof(ExtensionHealthPipeService)}.ListenerLoop", exception).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostic failure must not hide the original listener fault.
        }
    }
    internal async Task HandleConnectionAsync(Stream pipe, CancellationToken stoppingToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(ExtensionHealthPipeConstants.RequestTimeout);
        var payload = await BoundedPipeFraming.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
        if (payload is null)
        {
            return;
        }

        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        var requestResult = ExtensionHealthProtocol.ReadRequest(payload, nowUtc);
        if (!requestResult.IsValid)
        {
            throw new InvalidDataException("Pipe request failed health-contract validation.");
        }

        var request = requestResult.Value!;
        if (_security is not null) _security.AuthorizeBrowser(pipe as NamedPipeServerStream
            ?? throw new UnauthorizedAccessException("Native pipe proof required."), request.Type);
        if (request.Type == ExtensionHealthProtocol.HeartbeatType)
        {
            _healthState.RecordHeartbeat(nowUtc);
        }

        var acknowledgement = request.AcknowledgedPolicyId is null
            ? null
            : new FocusGuard.Domain.PolicyAcknowledgement(
                request.AcknowledgedPolicyId.Value,
                request.AcknowledgedPolicyHash!);

        // The status poll is what notices a cycle phase boundary and redistributes the
        // policy that authorizes it. It must run before reading the current delivery,
        // but must not clear a still-valid browser acknowledgement when status has none.
        var sessionStatus = await _focusService.GetStatusAsync(timeout.Token);
        var policy = request.Type == ExtensionHealthProtocol.StatusType
            ? _policyDistribution.GetCurrentDeliveryForBrowserStatus()
            : _policyDistribution.ExchangeBrowserAcknowledgement(acknowledgement);

        if (request.Type == ExtensionHealthProtocol.AuditType
            && request.BlockedUrl is not null
            && policy is not null
            && acknowledgement?.PolicyId == policy.PolicyId
            && string.Equals(acknowledgement.PolicyHash, policy.PolicyHash, StringComparison.Ordinal)
            && _policyDistribution.IsBrowserConnected)
        {
            await _auditStore.WriteAsync(
                new FocusAuditEvent(
                    Guid.NewGuid(),
                    nowUtc,
                    FocusAuditEventKind.WebsiteBlocked,
                    request.BlockedUrl,
                    "Blocked",
                    policy.Policy.SessionId),
                timeout.Token).ConfigureAwait(false);
        }
        // A capture is only honoured while the browser is genuinely under this policy;
        // otherwise any local page could write into the user's end-of-session list.
        if (request.Type == ExtensionHealthProtocol.CaptureType
            && request.BlockedUrl is not null
            && policy is not null
            && acknowledgement?.PolicyId == policy.PolicyId
            && string.Equals(acknowledgement.PolicyHash, policy.PolicyHash, StringComparison.Ordinal)
            && _policyDistribution.IsBrowserConnected)
        {
            await _focusService.CaptureAsync(
                new CaptureRequest(
                    request.CaptureText,
                    CaptureProtocol.WebsiteKind,
                    request.BlockedUrl),
                timeout.Token).ConfigureAwait(false);
        }

        var response = new ExtensionHealthResponse(
            ExtensionHealthProtocol.CurrentVersion,
            ExtensionHealthProtocol.StatusResponseType,
            request.CorrelationId,
            _healthState.ServiceInstanceId,
            ServiceReady: true,
            _healthState.IsExtensionReady(nowUtc),
            sessionStatus.SessionState,
            sessionStatus.RemainingSeconds,
            nowUtc,
            ErrorCode: null,
            policy,
            EnforcementConfirmed: _policyDistribution.IsBrowserConnected);
        await BoundedPipeFraming.WriteAsync(
            pipe,
            ExtensionHealthProtocol.WriteResponse(response),
            timeout.Token).ConfigureAwait(false);
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
            inBufferSize: ExtensionHealthProtocol.MaxMessageBytes + sizeof(uint),
            outBufferSize: ExtensionHealthProtocol.MaxMessageBytes + sizeof(uint),
            security);
    }
}
