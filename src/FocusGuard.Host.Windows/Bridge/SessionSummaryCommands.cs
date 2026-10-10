using System.Diagnostics;
using FocusGuard.Client;
using FocusGuard.Client.Journal;
using FocusGuard.Contracts;
using FocusGuard.Host.Windows.Hosting;

namespace FocusGuard.Host.Windows.Bridge;

internal sealed record BridgeCapturedItem(string Id, string Text, string SourceKind, string SourceLabel,
    DateTimeOffset CapturedAtUtc, string Resolution, bool Openable);
internal sealed record BridgeSessionSummary(string SessionId, string Outcome, string ProfileName, int PlannedMinutes,
    int FocusedMinutes, int BlockedAttempts, int StreakDays, DateTimeOffset EndedAtUtc, string Objective, string Reflection,
    IReadOnlyList<BridgeCapturedItem> Captures, bool CapturesTruncated, FocusGuard.Domain.CycleHistoryMetrics? CycleMetrics);
internal sealed record BridgeCaptureAction(string SessionId, string CaptureId, string Action);
internal sealed record BridgeSessionReflectionSaved(string SessionId, string Reflection);

internal sealed partial class NativeBridgeDispatcher
{
    private readonly HashSet<Guid> _openedCaptureIds = [];
    private async ValueTask<(string? Error, object? Result)> HandleSessionSummaryAsync(BridgeRequest request, CancellationToken token)
    {
        var summary = await client.GetSessionSummaryAsync(request.SummarySessionId, token).ConfigureAwait(false);
        if (!ValidSessionSummary(summary, request.SummarySessionId!.Value)) return ("SessionSummaryUnavailable", null);
        var intention = sessionJournalStore is null ? null
            : await Task.Run(() => sessionJournalStore.GetLatest(summary.SessionId!.Value), token).ConfigureAwait(false);
        var localIntention = intention is { } local && local.SessionId == summary.SessionId ? local : null;
        var objectiveText = localIntention?.Objective ?? string.Empty;
        var reflectionText = localIntention?.Reflection ?? string.Empty;
        var objective = objectiveText[..Math.Min(objectiveText.Length, 240)];
        var reflection = reflectionText[..Math.Min(reflectionText.Length, 1000)];
        var all = summary.Captures;
        var visible = all.TakeLast(24).Select(capture => new BridgeCapturedItem(capture.Id.ToString("D"), capture.Text,
            capture.SourceKind, capture.SourceLabel, capture.CapturedAtUtc, capture.Resolution,
            !_openedCaptureIds.Contains(capture.Id) && CanOpenCapture(capture))).ToArray();
        return (null, new BridgeSessionSummary(summary.SessionId!.Value.ToString("D"), summary.Outcome, summary.ProfileName,
            summary.PlannedMinutes, summary.FocusedMinutes, summary.BlockedAttempts, summary.StreakDays,
            summary.EndedAtUtc!.Value, objective, reflection, visible, all.Count > visible.Length, summary.CycleMetrics));
    }

    private async ValueTask<(string? Error, object? Result)> HandleSessionReflectionAsync(BridgeRequest request, CancellationToken token)
    {
        if (sessionJournalStore is null) return ("ReflectionUnavailable", null);
        var input = request.SessionReflection!;
        var summary = await client.GetSessionSummaryAsync(input.SessionId, token).ConfigureAwait(false);
        if (!ValidSessionSummary(summary, input.SessionId)) return ("SessionSummaryUnavailable", null);
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);

        var bounded = input.Reflection[..Math.Min(input.Reflection.Length, 1000)].Trim();
        var latest = await Task.Run(() => sessionJournalStore.GetLatest(input.SessionId), token).ConfigureAwait(false);
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);
        var fallback = latest is null
            ? new SessionIntention(input.SessionId, Guid.Empty, summary.ProfileName, summary.PlannedMinutes,
                FocusGuard.Domain.SessionMode.SimpleTimer, null, null, false, string.Empty, bounded)
            : null;
        var saved = await Task.Run(() => sessionJournalStore.SaveReflection(input.SessionId, bounded, fallback), token)
            .ConfigureAwait(false);
        if (!saved) return ("ReflectionUnavailable", null);
        return (null, new BridgeSessionReflectionSaved(input.SessionId.ToString("D"), bounded));
    }

    private async ValueTask<(string? Error, object? Result)> HandleCaptureActionAsync(BridgeRequest request, CancellationToken token)
    {
        var input = request.CaptureAction!;
        var status = await client.GetStatusAsync(token).ConfigureAwait(false);
        if (!BridgeLimits.IsValidStatus(status)) return ("InvalidServiceResponse", null);
        if (status.StrictMode || !CanStart(status)) return ("SessionStateChanged", null);
        if (_profileOperationIds.Count >= 128) return ("OperationLimitReached", null);
        if (!_profileOperationIds.Add(input.OperationId)) return ("DuplicateOperation", null);
        token.ThrowIfCancellationRequested();
        if (!documentLifetime.TryGetCurrent(request.DocumentToken, out _)) return ("StaleDocument", null);
        var summary = await client.GetSessionSummaryAsync(input.SessionId, token).ConfigureAwait(false);
        if (!ValidSessionSummary(summary, input.SessionId)) return ("SessionSummaryUnavailable", null);
        var capture = summary.Captures.FirstOrDefault(item => item.Id == input.CaptureId);
        if (capture is null || capture.Resolution != CaptureProtocol.PendingResolution) return ("CaptureChanged", null);

        if (request.Method == "capture.open")
        {
            if (!CanOpenCapture(capture)) return ("CaptureCannotOpen", null);
            if (_openedCaptureIds.Count >= 256 || !_openedCaptureIds.Add(input.CaptureId)) return ("CaptureOpenAlreadyAttempted", null);
            try
            {
                var target = capture.SourceKind == CaptureProtocol.WebsiteKind
                    ? capture.SourceValue
                    : Path.GetFullPath(capture.SourceValue);
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return ("CaptureOpenFailed", null);
            }
        }
        var resolution = request.Method == "capture.open" ? CaptureProtocol.OpenedResolution : CaptureProtocol.DismissedResolution;
        var resolved = await client.ResolveCapturesAsync(new([input.CaptureId], resolution), token).ConfigureAwait(false);
        if (!resolved.Accepted || resolved.ResolvedCount != 1) return ("OutcomeUnknown", null);
        return (null, new BridgeCaptureAction(input.SessionId.ToString("D"), input.CaptureId.ToString("D"),
            request.Method == "capture.open" ? "Opened" : "Dismissed"));
    }

    private static bool ValidSessionSummary(SessionSummaryResponse summary, Guid expected) =>
        summary.Available && summary.SessionId == expected && expected != Guid.Empty
        && CaptureProtocol.IsOutcome(summary.Outcome) && !string.IsNullOrWhiteSpace(summary.ProfileName)
        && summary.ProfileName.Length <= 80 && summary.PlannedMinutes is >= 1 and <= 720
        && summary.FocusedMinutes is >= 0 and <= 43200 && summary.BlockedAttempts is >= 0 and <= 100000
        && summary.StreakDays is >= 0 and <= 100000 && summary.EndedAtUtc is { Offset: { Ticks: 0 } }
        && (summary.ProfileId is null || summary.ProfileId != Guid.Empty)
        && (summary.ScheduledIntent is null || summary.ProfileId.HasValue
            && summary.ScheduledIntent.IsValid()
            && string.Equals(summary.ProfileName, summary.ScheduledIntent.ProfileName, StringComparison.Ordinal))
        && summary.Captures is not null && summary.Captures.Count <= CaptureProtocol.MaximumCapturesPerSession
        && summary.Captures.Select(item => item.Id).Distinct().Count() == summary.Captures.Count
        && summary.Captures.All(item => item.Id != Guid.Empty && item.Text is { Length: > 0 and <= IpcProtocol.MaximumCaptureTextLength }
            && CaptureProtocol.IsSourceKind(item.SourceKind) && item.SourceValue is { Length: > 0 and <= IpcProtocol.MaximumCaptureSourceLength }
            && item.SourceLabel is { Length: > 0 and <= IpcProtocol.MaximumCaptureSourceLength } && item.CapturedAtUtc.Offset == TimeSpan.Zero
            && item.Resolution == CaptureProtocol.PendingResolution);

    private static bool CanOpenCapture(CapturedItemView capture)
    {
        if (capture.SourceKind == CaptureProtocol.WebsiteKind)
            return !capture.SourceValue.Contains('?', StringComparison.Ordinal) && !capture.SourceValue.Contains('#', StringComparison.Ordinal)
                && Uri.TryCreate(capture.SourceValue, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(uri.Host);
        if (capture.SourceKind != CaptureProtocol.ApplicationKind || !Path.IsPathFullyQualified(capture.SourceValue)
            || capture.SourceValue.StartsWith(@"\", StringComparison.Ordinal) || !string.Equals(Path.GetExtension(capture.SourceValue), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
        try { return File.Exists(Path.GetFullPath(capture.SourceValue)); }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }
}
