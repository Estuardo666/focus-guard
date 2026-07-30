using FocusGuard.Domain;

namespace FocusGuard.Monitoring;

public sealed record ProcessSnapshot(
    string ProcessInstanceId,
    string ExecutablePath,
    string? Sha256 = null,
    bool IsProtected = false,
    int? ProcessId = null,
    DateTimeOffset? StartedAtUtc = null);

public enum ProcessActionKind
{
    RequestGracefulClose,
    RequestTermination
}

public sealed record ProcessAction(
    string ProcessInstanceId,
    ProcessActionKind Kind,
    string Reason,
    DateTimeOffset ObservedAtUtc);

public sealed class SimulatedProcessMonitor
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _gracePeriod;
    private readonly Dictionary<string, DateTimeOffset> _firstBlockedAt = new(StringComparer.Ordinal);
    private readonly HashSet<string> _terminationRequested = new(StringComparer.Ordinal);

    public SimulatedProcessMonitor(TimeSpan gracePeriod, TimeProvider? timeProvider = null)
    {
        if (gracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(gracePeriod), "Grace period cannot be negative.");
        }

        _gracePeriod = gracePeriod;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<ProcessAction> Evaluate(
        EnforcementPolicy? policy,
        IEnumerable<ProcessSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (policy is null || !policy.EnforcementEnabled)
        {
            return Array.Empty<ProcessAction>();
        }

        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        if (nowUtc < policy.StartUtc || nowUtc >= policy.EndUtc)
        {
            return Array.Empty<ProcessAction>();
        }

        var actions = new List<ProcessAction>();
        foreach (var snapshot in snapshots)
        {
            ValidateSnapshot(snapshot);
            if (snapshot.IsProtected || !IsBlocked(policy.ApplicationRules, snapshot))
            {
                continue;
            }

            if (!_firstBlockedAt.TryGetValue(snapshot.ProcessInstanceId, out var firstBlockedAt))
            {
                _firstBlockedAt[snapshot.ProcessInstanceId] = nowUtc;
                actions.Add(new ProcessAction(
                    snapshot.ProcessInstanceId,
                    ProcessActionKind.RequestGracefulClose,
                    "blocked-application-rule",
                    nowUtc));
                continue;
            }

            if (!_terminationRequested.Contains(snapshot.ProcessInstanceId)
                && nowUtc - firstBlockedAt >= _gracePeriod)
            {
                _terminationRequested.Add(snapshot.ProcessInstanceId);
                actions.Add(new ProcessAction(
                    snapshot.ProcessInstanceId,
                    ProcessActionKind.RequestTermination,
                    "blocked-application-rule-after-grace-period",
                    nowUtc));
            }
        }

        return actions;
    }

    private static bool IsBlocked(IReadOnlyList<ApplicationRule> rules, ProcessSnapshot snapshot)
    {
        var candidate = new ApplicationCandidate(snapshot.ExecutablePath, snapshot.Sha256);
        var matchedRules = rules.Where(rule => ApplicationRuleMatcher.Matches(rule, candidate)).ToArray();
        return !matchedRules.Any(rule => rule.Action == RuleAction.Allow)
            && matchedRules.Any(rule => rule.Action == RuleAction.Block);
    }

    private static void ValidateSnapshot(ProcessSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.ProcessInstanceId))
        {
            throw new ArgumentException("Process instance ID is required.", nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(snapshot.ExecutablePath))
        {
            throw new ArgumentException("Executable path is required.", nameof(snapshot));
        }
    }
}
