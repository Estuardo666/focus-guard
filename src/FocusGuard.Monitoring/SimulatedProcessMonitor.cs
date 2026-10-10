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
    private readonly HashSet<string> _observedThisScan = new(StringComparer.Ordinal);
    private readonly List<string> _staleInstances = [];
    private Guid? _indexedSessionId;
    private bool _indexedBreakState;
    private ApplicationRuleIndex? _ruleIndex;

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
            ClearTracking();
            return Array.Empty<ProcessAction>();
        }

        var nowUtc = _timeProvider.GetUtcNow().ToUniversalTime();
        if (nowUtc < policy.StartUtc || nowUtc >= policy.EndUtc)
        {
            ClearTracking();
            return Array.Empty<ProcessAction>();
        }

        var isAuthorizedBreak = policy.IsAuthorizedBreak(nowUtc);
        if (_indexedSessionId != policy.SessionId || _indexedBreakState != isAuthorizedBreak)
        {
            ClearTracking();
            _indexedSessionId = policy.SessionId;
            _indexedBreakState = isAuthorizedBreak;
            _ruleIndex = new ApplicationRuleIndex(policy.EffectiveApplicationRules(nowUtc));
        }

        var actions = new List<ProcessAction>();
        _observedThisScan.Clear();
        foreach (var snapshot in snapshots)
        {
            ValidateSnapshot(snapshot);
            _observedThisScan.Add(snapshot.ProcessInstanceId);
            if (snapshot.IsProtected || !_ruleIndex!.IsBlocked(snapshot))
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

        PruneExitedProcesses();
        return actions;
    }

    private void ClearTracking()
    {
        _firstBlockedAt.Clear();
        _terminationRequested.Clear();
        _observedThisScan.Clear();
        _staleInstances.Clear();
        _indexedSessionId = null;
        _indexedBreakState = false;
        _ruleIndex = null;
    }

    private void PruneExitedProcesses()
    {
        _staleInstances.Clear();
        foreach (var instanceId in _firstBlockedAt.Keys)
        {
            if (!_observedThisScan.Contains(instanceId))
            {
                _staleInstances.Add(instanceId);
            }
        }

        foreach (var instanceId in _staleInstances)
        {
            _firstBlockedAt.Remove(instanceId);
            _terminationRequested.Remove(instanceId);
        }
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

    internal int TrackedProcessCount => _firstBlockedAt.Count;
}

internal sealed class ApplicationRuleIndex
{
    private readonly RuleGroup _allow;
    private readonly RuleGroup _block;

    internal ApplicationRuleIndex(IEnumerable<ApplicationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _allow = RuleGroup.Create(rules.Where(rule => rule.Action == RuleAction.Allow));
        _block = RuleGroup.Create(rules.Where(rule => rule.Action == RuleAction.Block));
    }

    internal bool IsBlocked(ProcessSnapshot snapshot)
    {
        var path = NormalizePath(snapshot.ExecutablePath);
        var fileName = GetFileName(path);
        var hash = snapshot.Sha256?.Trim();
        return !_allow.Matches(path, fileName, hash)
            && _block.Matches(path, fileName, hash);
    }

    private static string NormalizePath(string value) =>
        value.Trim().Replace('\\', '/').TrimEnd('/');

    private static string GetFileName(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator >= 0 ? path[(separator + 1)..] : path;
    }

    private sealed record RuleGroup(
        HashSet<string> ExactPaths,
        HashSet<string> FileNames,
        HashSet<string> Hashes,
        string[] Folders)
    {
        internal static RuleGroup Create(IEnumerable<ApplicationRule> rules)
        {
            var exactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folders = new List<string>();
            foreach (var rule in rules)
            {
                switch (rule.MatchKind)
                {
                    case ApplicationMatchKind.ExactPath:
                        exactPaths.Add(NormalizePath(rule.Value));
                        break;
                    case ApplicationMatchKind.Folder:
                        folders.Add(NormalizePath(rule.Value));
                        break;
                    case ApplicationMatchKind.FileName:
                        fileNames.Add(rule.Value.Trim());
                        break;
                    case ApplicationMatchKind.Sha256:
                        hashes.Add(rule.Value.Trim());
                        break;
                }
            }

            return new RuleGroup(exactPaths, fileNames, hashes, folders.ToArray());
        }

        internal bool Matches(string path, string fileName, string? hash)
        {
            if (ExactPaths.Contains(path)
                || FileNames.Contains(fileName)
                || (hash is not null && Hashes.Contains(hash)))
            {
                return true;
            }

            foreach (var folder in Folders)
            {
                if (path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
