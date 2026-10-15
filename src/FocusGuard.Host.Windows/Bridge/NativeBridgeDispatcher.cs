using System.Text;
using System.Text.Json;
using FocusGuard.Client;
using FocusGuard.Client.Journal;
using FocusGuard.Client.Platform;
using FocusGuard.Client.Workflows;
using FocusGuard.Host.Windows.Hosting;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed partial class NativeBridgeDispatcher(
    IFocusControlClient client,
    IHostControlAuthorization authorization,
    DocumentLifetime documentLifetime,
    TimeProvider? timeProvider = null,
    IProfileStore? profileStore = null,
    IApplicationCatalog? applicationCatalog = null,
    IApplicationIconProvider? iconProvider = null,
    FocusGuard.Client.Preferences.DesktopSettingsStore? settingsStore = null,
    SessionJournalStore? sessionJournalStore = null,
    Func<string, CancellationToken, ValueTask<bool>>? recoveryCodeSaver = null,
    IHostWindowController? windowController = null,
    IHostShellController? shellController = null,
    IStartupRegistration? startupRegistration = null,
    IHostAccountService? accountService = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly BridgeRequestParser _parser = new();
    private readonly object _requestLock = new();
    private readonly HashSet<Guid> _requestIds = [];
    private readonly Queue<(Guid Id, long Timestamp)> _recentRequests = [];
    private readonly SemaphoreSlim _statusRequestLock = new(1, 1);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private string? _requestDocumentToken;

    public async ValueTask<string?> HandleAsync(
        string source,
        string json,
        CancellationToken cancellationToken)
    {
        if (!WebViewSecurityPolicy.IsTrustedBridgeSource(source)
            || !_parser.TryParse(json, out var request, out _)
            || request is null)
        {
            return null;
        }

        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out var lease))
        {
            return SerializeError(request, "StaleDocument");
        }

        if (!TryAcceptRequest(request))
        {
            return SerializeError(request, "DuplicateOrRateLimited");
        }

        if (request.AppCommand is { Action: HostAppAction.StartupGet or HostAppAction.StartupSet } startupCommand)
        {
            // Only the main window offers this; the registration writes the signed-in user's Run value, nothing machine-wide.
            if (startupRegistration is null) return SerializeError(request, "WindowUnavailable");
            var stored = startupCommand.Action == HostAppAction.StartupGet || startupRegistration.TrySetEnabled(startupCommand.Enabled == true);
            return JsonSerializer.Serialize(new BridgeAppResponse(BridgeLimits.ProtocolVersion, request.RequestId.ToString("N"),
                request.DocumentToken, stored ? null : "StartupUnavailable", new HostAppResult(stored, startupRegistration.IsEnabled)), JsonOptions);
        }

        if (request.AppCommand is { } appCommand)
        {
            if (shellController is null) return SerializeError(request, "WindowUnavailable");
            try
            {
                var accepted = await shellController.ExecuteAsync(appCommand, cancellationToken).ConfigureAwait(false);
                if (!documentLifetime.IsCurrent(lease)) return null;
                return JsonSerializer.Serialize(new BridgeAppResponse(BridgeLimits.ProtocolVersion, request.RequestId.ToString("N"),
                    request.DocumentToken, accepted ? null : "WindowUnavailable", new HostAppResult(accepted)), JsonOptions);
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException
                                               or ObjectDisposedException or System.ComponentModel.Win32Exception)
            {
                return documentLifetime.IsCurrent(lease) ? SerializeError(request, "WindowUnavailable") : null;
            }
        }

        if (request.WindowCommand is { } windowCommand)
        {
            if (windowController is null)
            {
                return SerializeError(request, "WindowUnavailable");
            }

            try
            {
                using var deadline = new CancellationTokenSource(BridgeLimits.RequestTimeout, _timeProvider);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    lease.CancellationToken,
                    deadline.Token);
                var state = windowCommand.Compact is { } compact
                    ? await windowController.SetCompactModeAsync(compact, linked.Token).ConfigureAwait(false)
                    : windowCommand.AlwaysOnTop is { } alwaysOnTop
                        ? await windowController.SetAlwaysOnTopAsync(alwaysOnTop, linked.Token).ConfigureAwait(false)
                        : await windowController.SetDetailsExpandedAsync(windowCommand.DetailsExpanded!.Value, linked.Token).ConfigureAwait(false);
                if (!documentLifetime.IsCurrent(lease) || linked.IsCancellationRequested)
                {
                    return null;
                }

                return JsonSerializer.Serialize(new BridgeCommandResponse(
                    BridgeLimits.ProtocolVersion,
                    request.RequestId.ToString("N"),
                    request.DocumentToken,
                    null,
                    state), JsonOptions);
            }
            catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException
                                               or ObjectDisposedException or System.ComponentModel.Win32Exception)
            {
                return documentLifetime.IsCurrent(lease) ? SerializeError(request, "WindowUnavailable") : null;
            }
        }

        if (request.AccountAction is { } accountAction)
        {
            try
            {
                var result = accountService is null
                    ? new HostAccountSnapshot(false, false, false, false, false, null, null, false, false, false, "AccountUnavailable", null)
                    : await accountService.ExecuteAsync(accountAction, cancellationToken,
                        request.AccountPairingId, request.AccountFingerprint, request.SettingsSyncResolution,
                        request.SettingsSyncResetResolution).ConfigureAwait(false);
                if (!documentLifetime.IsCurrent(lease)) return null;
                var accountJson = JsonSerializer.Serialize(new BridgeAccountResponse(BridgeLimits.ProtocolVersion,
                    request.RequestId.ToString("N"), request.DocumentToken, null, result), JsonOptions);
                return Encoding.UTF8.GetByteCount(accountJson) <= BridgeLimits.MaximumResponseBytes
                    ? accountJson : SerializeError(request, "ResponseTooLarge");
            }
            catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException
                or ObjectDisposedException or HttpRequestException or TimeoutException or IOException
                or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
            {
                if (!documentLifetime.IsCurrent(lease)) return null;
                var failed = new HostAccountSnapshot(false, false, false, false, false, null, null, false, false, false,
                    "AccountConnectionUnavailable", null);
                return JsonSerializer.Serialize(new BridgeAccountResponse(BridgeLimits.ProtocolVersion,
                    request.RequestId.ToString("N"), request.DocumentToken, null, failed), JsonOptions);
            }
        }

        var operation = request.Method switch
        {
            "profiles.list" or "profile.get" or "applications.list" or "cycles.list" => HostControlOperation.ReadProfiles,
            "session.prepareNormal" or "session.prepareCycle" or "session.strict.prepare" or "session.strict.cancel" or "session.strict.saveRecoveryCode" => HostControlOperation.PrepareNormal,
            "session.startNormal" or "session.startCycle" => HostControlOperation.StartNormal,
            "session.strict.activate" => HostControlOperation.StartNormal,
            "session.stopNormal" or "session.skipBreak" => HostControlOperation.ManageNormal,
            "cyclePreset.save" or "cyclePreset.delete" or "cyclePreset.duplicate" => HostControlOperation.WriteProfiles,
            "today.get" or "schedules.list" => HostControlOperation.ReadProfiles,
            "schedule.save" or "schedule.enable" or "schedule.delete" => HostControlOperation.WriteProfiles,
            "session.summary" => HostControlOperation.ReadProfiles,
            "session.intent.get" => HostControlOperation.ReadProfiles,
            "session.reflection.save" => HostControlOperation.ReadProfiles,
            "recovery.info" => HostControlOperation.ReadProfiles,
            "capture.open" or "capture.dismiss" => HostControlOperation.ManageNormal,
            "preferences.get" or "preferences.set" => HostControlOperation.ReadProfiles,
            _ => request.ProfileCommand is not null ? HostControlOperation.WriteProfiles : HostControlOperation.ReadStatus
        };
        var decision = authorization.Authorize(operation);
        if (decision.State != HostAuthorizationState.Authorized)
        {
            return documentLifetime.IsCurrent(lease)
                ? SerializeError(request, decision.State == HostAuthorizationState.NotIntegrated
                    ? "AuthorizationUnavailable"
                    : "Unauthorized")
                : null;
        }

        try
        {
            using var deadline = new CancellationTokenSource(BridgeLimits.RequestTimeout, _timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                lease.CancellationToken,
                deadline.Token);
            if (!await _statusRequestLock.WaitAsync(TimeSpan.Zero, linked.Token).ConfigureAwait(false))
            {
                return SerializeError(request, "RequestInProgress");
            }

            try
            {
                if (request.Method != "status.get")
                {
                    var result = await HandleCommandAsync(request, linked.Token).AsTask()
                        .WaitAsync(linked.Token).ConfigureAwait(false);
                    if (!documentLifetime.IsCurrent(lease) || linked.IsCancellationRequested)
                        return null;
                    var commandJson = JsonSerializer.Serialize(new BridgeCommandResponse(
                        BridgeLimits.ProtocolVersion, request.RequestId.ToString("N"),
                        request.DocumentToken, result.Error, result.Result), JsonOptions);
                    var responseLimit = request.Method is "profile.get" or "applications.list" or "session.prepareCycle" or "session.strict.prepare" or "today.get" or "session.summary" ? 64 * 1024 : BridgeLimits.MaximumResponseBytes;
                    return Encoding.UTF8.GetByteCount(commandJson) <= responseLimit
                        ? commandJson : SerializeError(request, "ResponseTooLarge");
                }
                var status = await client.GetStatusAsync(linked.Token).AsTask()
                    .WaitAsync(linked.Token).ConfigureAwait(false);
                if (!documentLifetime.IsCurrent(lease) || linked.IsCancellationRequested)
                {
                    return null;
                }

                if (!BridgeLimits.IsValidStatus(status))
                {
                    return SerializeError(request, "InvalidServiceResponse");
                }

                var response = new BridgeResponse(
                    BridgeLimits.ProtocolVersion,
                    request.RequestId.ToString("N"),
                    request.DocumentToken,
                    null,
                    BridgeLimits.Project(status));
                var jsonResponse = JsonSerializer.Serialize(response, JsonOptions);
                return Encoding.UTF8.GetByteCount(jsonResponse) <= BridgeLimits.MaximumResponseBytes
                    ? jsonResponse
                    : SerializeError(request, "ResponseTooLarge");
            }
            finally
            {
                _statusRequestLock.Release();
            }
        }
        catch (OperationCanceledException) when (lease.CancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return documentLifetime.IsCurrent(lease)
                ? SerializeError(request, IsWrite(request) ? "OutcomeUnknown" : "ServiceUnavailable")
                : null;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or InvalidDataException
                                          or UnauthorizedAccessException or System.Security.SecurityException
                                          or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            return documentLifetime.IsCurrent(lease)
                ? SerializeError(request, IsWrite(request) ? "OutcomeUnknown" : "ServiceUnavailable")
                : null;
        }
    }

    private bool TryAcceptRequest(BridgeRequest request)
    {
        lock (_requestLock)
        {
            var now = _timeProvider.GetTimestamp();
            if (_strictReview is { } review && _timeProvider.GetUtcNow().ToUniversalTime() >= review.Review.ExpiresUtc)
                _strictReview = null;
            if (!string.Equals(_requestDocumentToken, request.DocumentToken, StringComparison.Ordinal))
            {
                _requestDocumentToken = request.DocumentToken;
                _requestIds.Clear();
                _recentRequests.Clear();
                _strictReview = null;
                _normalReview = null;
                _cycleReview = null;
            }
            while (_recentRequests.TryPeek(out var oldest)
                && _timeProvider.GetElapsedTime(oldest.Timestamp, now) >= TimeSpan.FromMinutes(1))
            {
                _recentRequests.Dequeue();
                _requestIds.Remove(oldest.Id);
            }

            if (_recentRequests.Count >= BridgeLimits.MaximumRequestsPerMinute
                || !_requestIds.Add(request.RequestId))
            {
                return false;
            }

            _recentRequests.Enqueue((request.RequestId, now));
            return true;
        }
    }

    private static string SerializeError(BridgeRequest request, string error) =>
        request.Method == "status.get" ? JsonSerializer.Serialize(new BridgeResponse(
            BridgeLimits.ProtocolVersion,
            request.RequestId.ToString("N"),
            request.DocumentToken,
            error,
            null), JsonOptions) : JsonSerializer.Serialize(new BridgeCommandResponse(
                BridgeLimits.ProtocolVersion, request.RequestId.ToString("N"), request.DocumentToken,
                error, null), JsonOptions);

    private static bool IsWrite(BridgeRequest request) => request.Method is "session.startNormal" or "session.startCycle" or "session.stopNormal" or "session.skipBreak"
        or "session.strict.prepare" or "session.strict.activate" or "session.strict.cancel" or "session.strict.saveRecoveryCode"
        or "cyclePreset.save" or "cyclePreset.delete" or "cyclePreset.duplicate"
        or "schedule.save" or "schedule.enable" or "schedule.delete"
        or "capture.open" or "capture.dismiss"
        or "session.reflection.save"
        || request.ProfileCommand is not null && request.Method != "profile.get";
}
