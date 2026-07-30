using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using FocusGuard.Application;
using FocusGuard.Contracts;
using FocusGuard.Infrastructure.Windows.Processes;
using FocusGuard.Monitoring;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FocusGuard.Service;

public sealed class ProcessEnforcementWorker(
    ProcessMonitorPolicyReceiver receiver,
    IApplicationProcessController controller,
    IFocusAuditStore auditStore,
    ILogger<ProcessEnforcementWorker> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(2);
    private readonly ConfirmedProcessPolicyEvaluator _evaluator =
        new(receiver, GracePeriod, timeProvider);
    private readonly ExecutableSha256Cache _hashCache = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delivery = receiver.GetActiveDelivery();
                if (delivery is null)
                {
                    await Task.Delay(PollInterval, timeProvider ?? TimeProvider.System, stoppingToken)
                        .ConfigureAwait(false);
                    continue;
                }
                var needsHashes = delivery.Policy.ApplicationRules.Any(
                    rule => rule.MatchKind == FocusGuard.Domain.ApplicationMatchKind.Sha256);
                var snapshots = SnapshotProcesses(needsHashes);
                foreach (var action in _evaluator.Evaluate(snapshots))
                {
                    if (action.Kind != ProcessActionKind.RequestGracefulClose)
                    {
                        continue;
                    }
                    var snapshot = snapshots.First(item => item.ProcessInstanceId == action.ProcessInstanceId);
                    var result = await controller.TerminateAsync(
                        new ApplicationProcessTarget(snapshot.ExecutablePath, snapshot.ProcessId, snapshot.StartedAtUtc),
                        new ProcessTerminationOptions(GracePeriod),
                        stoppingToken).ConfigureAwait(false);
                    try
                    {
                        await auditStore.WriteAsync(
                            new FocusAuditEvent(
                                Guid.NewGuid(),
                                (timeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime(),
                                FocusAuditEventKind.ApplicationBlocked,
                                snapshot.ExecutablePath,
                                result.Status.ToString(),
                                delivery.Policy.SessionId),
                            stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        logger.LogError(exception, "Application audit persistence failed.");
                    }
                    logger.LogInformation(
                        "Application enforcement completed with {Status} and {ErrorCode} for process {ProcessId}.",
                        result.Status,
                        result.ErrorCode,
                        result.ProcessId);
                }
                await Task.Delay(PollInterval, timeProvider ?? TimeProvider.System, stoppingToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await ServiceFailureLog.WriteAsync(nameof(ProcessEnforcementWorker), exception);
            throw;
        }
    }

    private IReadOnlyList<ProcessSnapshot> SnapshotProcesses(bool computeHashes)
    {
        var snapshots = new List<ProcessSnapshot>();
        var protectedPolicy = new ProtectedProcessPolicy();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var instance = WindowsProcessInspector.TryRead(process.Id);
                    if (instance is null) continue;
                    var path = instance.ExecutablePath;
                    var started = instance.StartedAtUtc;
                    snapshots.Add(new ProcessSnapshot(
                        $"{process.Id}:{started.UtcTicks}",
                        path,
                        computeHashes ? _hashCache.GetHash(path) : null,
                        protectedPolicy.IsProtected(path),
                        process.Id,
                        started));
                }
                catch (Exception exception) when (
                    exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    // Inaccessible and exiting processes fail open for this scan.
                }
            }
        }
        return snapshots;
    }

}

internal sealed class ExecutableSha256Cache
{
    private const int MaximumEntries = 4096;
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string?> _computeHash;

    public ExecutableSha256Cache(Func<string, string?>? computeHash = null) =>
        _computeHash = computeHash ?? ComputeSha256;

    public string? GetHash(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileStamp before;
        try
        {
            before = FileStamp.Read(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(path, out var cached) && cached.Stamp == before)
            {
                return cached.Hash;
            }
        }

        var hash = _computeHash(path);
        if (hash is null)
        {
            return null;
        }

        try
        {
            var after = FileStamp.Read(path);
            if (after != before)
            {
                return null;
            }

            lock (_gate)
            {
                if (_entries.Count >= MaximumEntries && !_entries.ContainsKey(path))
                {
                    _entries.Remove(_entries.Keys.First());
                }
                _entries[path] = new CacheEntry(after, hash);
            }
            return hash;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ComputeSha256(string path)
    {
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private readonly record struct CacheEntry(FileStamp Stamp, string Hash);

    private readonly record struct FileStamp(long Length, DateTime LastWriteUtc)
    {
        public static FileStamp Read(string path)
        {
            var info = new FileInfo(path);
            info.Refresh();
            if (!info.Exists)
            {
                throw new FileNotFoundException("Executable no longer exists.", path);
            }
            return new FileStamp(info.Length, info.LastWriteTimeUtc);
        }
    }
}
