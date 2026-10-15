using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace FocusGuard.Sync;

public enum SettingsRetryStatus { Disabled, Waiting, Idle, Pending, Conflict, NeedsAttention }
[Flags]
public enum SettingsSyncWakeReason
{
    Scheduled = 0,
    Startup = 1,
    LocalSettingsChanged = 2,
    ConnectivityRestored = 4,
    ConsentChanged = 8,
    RecoveryCompleted = 16,
    ConflictResolved = 32
}
public sealed record SettingsRetryResult(SettingsRetryStatus Status, TimeSpan? Wait = null,
    SettingsTransportFailure? Failure = null, bool RetryCheckpointRecoveryAvailable = false);

/// <summary>Caller-driven monotonic retry gate. Does not start a timer, enable consent, or alter the durable outbox.</summary>
public sealed class SettingsRetryController(Func<CancellationToken, Task<SettingsSyncStatus>> tick,
    Func<bool> consentEnabled, TimeProvider time, Func<double>? jitter = null,
    SettingsRetryCheckpointStore? checkpointStore = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<double> random = jitter ?? Random.Shared.NextDouble;
    private long started;
    private TimeSpan delay;
    private int failures;
    private SettingsTransportFailure? attention;
    private bool idleDelayCanBeInterrupted;
    private bool conflictDelayCanBeInterrupted;
    private bool checkpointLoaded;
    private bool durableDelay;
    private bool retryCheckpointRecoveryAvailable;

    public Task<SettingsRetryResult> RunIfDueAsync(CancellationToken token = default) =>
        RunIfDueAsync(SettingsSyncWakeReason.Scheduled, token);

    public async Task<SettingsRetryResult> RunIfDueAsync(SettingsSyncWakeReason reason, CancellationToken token = default)
    {
        const SettingsSyncWakeReason validReasons = SettingsSyncWakeReason.Startup | SettingsSyncWakeReason.LocalSettingsChanged |
            SettingsSyncWakeReason.ConnectivityRestored | SettingsSyncWakeReason.ConsentChanged |
            SettingsSyncWakeReason.RecoveryCompleted | SettingsSyncWakeReason.ConflictResolved;
        if ((reason & ~validReasons) != 0) throw new ArgumentOutOfRangeException(nameof(reason));
        await gate.WaitAsync(token);
        try
        {
            if (!consentEnabled()) return new(SettingsRetryStatus.Disabled);
            if (attention is { } failure)
                return new(SettingsRetryStatus.NeedsAttention, Failure: failure,
                    RetryCheckpointRecoveryAvailable: retryCheckpointRecoveryAvailable);
            var checkpointFailure = await LoadCheckpointIfNeededAsync(token);
            if (checkpointFailure is not null) return checkpointFailure;
            if (attention is { } storedFailure)
                return new(SettingsRetryStatus.NeedsAttention, Failure: storedFailure,
                    RetryCheckpointRecoveryAvailable: retryCheckpointRecoveryAvailable);
            if (delay > TimeSpan.Zero)
            {
                var mayInterruptIdlePoll = idleDelayCanBeInterrupted &&
                    (reason & (SettingsSyncWakeReason.LocalSettingsChanged | SettingsSyncWakeReason.ConnectivityRestored |
                        SettingsSyncWakeReason.ConsentChanged | SettingsSyncWakeReason.RecoveryCompleted)) != 0;
                var mayInterruptConflictPoll = conflictDelayCanBeInterrupted &&
                    (reason & SettingsSyncWakeReason.ConflictResolved) != 0;
                var remaining = delay - time.GetElapsedTime(started);
                if (remaining > TimeSpan.Zero && !mayInterruptIdlePoll && !mayInterruptConflictPoll)
                    return new(SettingsRetryStatus.Waiting, remaining);
                if (durableDelay && checkpointStore is not null)
                {
                    try { await checkpointStore.ClearAsync(token); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception error) when (IsCheckpointFailure(error))
                    { return Stop(SettingsTransportFailure.Storage, retryCheckpointRecoveryAvailable: true); }
                    durableDelay = false;
                }
                delay = TimeSpan.Zero;
                idleDelayCanBeInterrupted = false;
                conflictDelayCanBeInterrupted = false;
            }
            try
            {
                var status = await tick(token);
                failures = 0; delay = TimeSpan.Zero;
                return status switch
                {
                    SettingsSyncStatus.Disabled => new(SettingsRetryStatus.Disabled),
                    SettingsSyncStatus.Idle => ScheduleSuccess(SettingsRetryStatus.Idle, TimeSpan.FromSeconds(30), interruptible: true),
                    SettingsSyncStatus.Pending => ScheduleSuccess(SettingsRetryStatus.Pending, TimeSpan.FromSeconds(5)),
                    SettingsSyncStatus.Conflict => ScheduleSuccess(SettingsRetryStatus.Conflict, TimeSpan.FromSeconds(30),
                        conflictResolutionInterruptible: true),
                    _ => Stop(SettingsTransportFailure.Protocol)
                };
            }
            catch (SettingsTransportException error)
            {
                return error.Failure is SettingsTransportFailure.Unavailable or SettingsTransportFailure.Throttled
                    ? await RetryAsync(error.RetryAfter, token) : Stop(error.Failure);
            }
            catch (HttpRequestException error) when (error.HttpRequestError is HttpRequestError.NameResolutionError or
                HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded or HttpRequestError.Unknown)
            { return await RetryAsync(null, token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { return await RetryAsync(null, token); }
            catch (Exception error) when (error is CryptographicException or InvalidDataException or JsonException or
                ArgumentException or InvalidOperationException or UnauthorizedAccessException or HttpRequestException or IOException)
            { return Stop(SettingsTransportFailure.Protocol); }
        }
        finally { gate.Release(); }
    }

    /// <summary>Only after deliberate recovery/re-authentication; never called automatically by a poll.</summary>
    public async Task ResumeAfterRecoveryAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (attention == SettingsTransportFailure.Storage && checkpointStore is not null)
            {
                var fallbackDelay = delay;
                attention = null;
                try
                {
                    var stored = await checkpointStore.LoadAsync(token);
                    checkpointLoaded = true;
                    durableDelay = stored is not null;
                    if (stored is not null)
                    {
                        failures = stored.FailureCount;
                        delay = TimeSpan.FromTicks(stored.DelayTicks);
                    }
                    else
                    {
                        // If the failed write never committed, keep the already observed server
                        // delay in memory for this process; never turn a storage failure into an early retry.
                        delay = fallbackDelay;
                    }
                    retryCheckpointRecoveryAvailable = false;
                    if (delay > TimeSpan.Zero) started = time.GetTimestamp();
                    idleDelayCanBeInterrupted = false;
                    conflictDelayCanBeInterrupted = false;
                    return;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error) when (IsCheckpointFailure(error))
                {
                    checkpointLoaded = false;
                    attention = SettingsTransportFailure.Storage;
                    retryCheckpointRecoveryAvailable = true;
                    delay = fallbackDelay;
                    return;
                }
            }

            attention = null; failures = 0; delay = TimeSpan.Zero; idleDelayCanBeInterrupted = false;
            conflictDelayCanBeInterrupted = false;
            retryCheckpointRecoveryAvailable = false;
            checkpointLoaded = checkpointStore is null;
            durableDelay = false;
        }
        finally { gate.Release(); }
    }

    /// <summary>Remove the scoped checkpoint only when this controller is stopped on a Storage failure.</summary>
    public async Task ResetStorageCheckpointAfterOwnerRecoveryAsync(bool ownerConfirmed, CancellationToken token = default)
    {
        if (!ownerConfirmed) throw new InvalidOperationException("Explicit owner recovery is required to reset the retry checkpoint.");
        await gate.WaitAsync(token);
        try
        {
            if (attention != SettingsTransportFailure.Storage || checkpointStore is null || !retryCheckpointRecoveryAvailable)
                throw new InvalidOperationException("A storage checkpoint failure must be active before recovery.");
            await checkpointStore.ResetAfterOwnerRecoveryAsync(ownerConfirmed: true, token);
        }
        finally { gate.Release(); }
    }

    private async Task<SettingsRetryResult?> LoadCheckpointIfNeededAsync(CancellationToken token)
    {
        if (checkpointStore is null || checkpointLoaded) return null;
        try
        {
            var stored = await checkpointStore.LoadAsync(token);
            checkpointLoaded = true;
            if (stored is not null)
            {
                failures = stored.FailureCount;
                delay = TimeSpan.FromTicks(stored.DelayTicks);
                started = time.GetTimestamp(); // A restart conservatively starts the full delay anew.
                idleDelayCanBeInterrupted = false;
                conflictDelayCanBeInterrupted = false;
                durableDelay = true;
            }
            return null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (IsCheckpointFailure(error))
        {
            return Stop(SettingsTransportFailure.Storage, retryCheckpointRecoveryAvailable: true);
        }
    }

    private async Task<SettingsRetryResult> RetryAsync(TimeSpan? retryAfter, CancellationToken token)
    {
        failures = Math.Min(failures + 1, 30);
        var sample = random();
        if (!double.IsFinite(sample) || sample is < 0 or > 1) return Stop(SettingsTransportFailure.Protocol);
        var seconds = Math.Min(300, 5 * Math.Pow(2, Math.Min(failures - 1, 6)) * (0.8 + sample * 0.4));
        delay = TimeSpan.FromSeconds(seconds);
        // Never shorten the server's Retry-After, including long maintenance windows.
        if (retryAfter is { } server && server > delay) delay = server;
        started = time.GetTimestamp();
        idleDelayCanBeInterrupted = false;
        conflictDelayCanBeInterrupted = false;
        if (checkpointStore is not null)
        {
            try
            {
                // Once a server throttle has been observed, finish the tiny local durable write
                // even if the caller is shutting down. This prevents restart from erasing Retry-After.
                await checkpointStore.SaveAsync(new SettingsRetryCheckpoint(failures, delay.Ticks), CancellationToken.None);
                checkpointLoaded = true;
                durableDelay = true;
            }
            catch (Exception error) when (IsCheckpointFailure(error))
            {
                return Stop(SettingsTransportFailure.Storage, retryCheckpointRecoveryAvailable: true);
            }
        }
        return new(SettingsRetryStatus.Waiting, delay);
    }

    private static bool IsCheckpointFailure(Exception error) => error is CryptographicException or InvalidDataException or
        JsonException or ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException;

    private SettingsRetryResult ScheduleSuccess(SettingsRetryStatus status, TimeSpan next, bool interruptible = false,
        bool conflictResolutionInterruptible = false)
    {
        delay = next;
        started = time.GetTimestamp();
        idleDelayCanBeInterrupted = interruptible;
        conflictDelayCanBeInterrupted = conflictResolutionInterruptible;
        return new(status, next);
    }
    private SettingsRetryResult Stop(SettingsTransportFailure failure, bool retryCheckpointRecoveryAvailable = false)
    {
        attention = failure;
        this.retryCheckpointRecoveryAvailable = retryCheckpointRecoveryAvailable;
        if (failure != SettingsTransportFailure.Storage) delay = TimeSpan.Zero;
        idleDelayCanBeInterrupted = false;
        conflictDelayCanBeInterrupted = false;
        return new(SettingsRetryStatus.NeedsAttention, Failure: failure,
            RetryCheckpointRecoveryAvailable: retryCheckpointRecoveryAvailable);
    }
}
