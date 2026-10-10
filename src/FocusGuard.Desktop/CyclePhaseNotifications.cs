using System.Globalization;
using FocusGuard.Contracts;
using FocusGuard.Desktop.Resources;
using FocusGuard.Domain;

namespace FocusGuard.Desktop;

public sealed record CyclePhaseTransition(
    SessionPhaseKind? EndedKind,
    int? EndedFocusCycleNumber,
    SessionPhaseKind? StartedKind,
    int? StartedFocusCycleNumber,
    DateTimeOffset? StartedEndUtc);

public sealed class CyclePhaseTransitionEventArgs(CyclePhaseTransition transition) : EventArgs
{
    public CyclePhaseTransition Transition { get; } = transition;
}

/// <summary>
/// Deduplicates the Desktop's one-second status polls without becoming a phase clock.
/// Every transition described here has first been authorized by the service.
/// </summary>
internal sealed class CyclePhaseTransitionTracker
{
    private Guid? _sessionId;
    private PhaseStatus? _phase;

    internal CyclePhaseTransition? Observe(StatusResponse status)
    {
        var isRunningCycle = status.SessionState is "Activating" or "Active"
            && status.Mode == SessionMode.FocusCycle
            && status.SessionId is not null;
        if (!isRunningCycle)
        {
            if (_sessionId is null)
            {
                return null;
            }

            var ended = Relevant(_phase?.Kind);
            var transition = ended is null
                ? null
                : new CyclePhaseTransition(
                    ended,
                    _phase?.FocusCycleNumber,
                    null,
                    null,
                    null);
            _sessionId = null;
            _phase = null;
            return transition;
        }

        var isNewSession = status.SessionId != _sessionId;
        var previous = isNewSession ? null : _phase;
        _sessionId = status.SessionId;
        _phase = status.CurrentPhase;

        if (_phase is null)
        {
            return null;
        }

        if (!isNewSession
            && previous is not null
            && previous.Index == _phase.Index
            && previous.Kind == _phase.Kind)
        {
            return null;
        }

        var endedKind = Relevant(previous?.Kind);
        var startedKind = Relevant(_phase.Kind);
        return endedKind is null && startedKind is null
            ? null
            : new CyclePhaseTransition(
                endedKind,
                previous?.FocusCycleNumber,
                startedKind,
                startedKind is null ? null : _phase.FocusCycleNumber,
                startedKind is null ? null : _phase.EndUtc);
    }

    private static SessionPhaseKind? Relevant(SessionPhaseKind? kind) => kind is
        SessionPhaseKind.Focus or SessionPhaseKind.ShortBreak
            ? kind
            : null;
}

internal sealed record CyclePhaseNotificationPresentation(
    string Title,
    string Body,
    string IconGlyph);

internal static class CyclePhaseNotificationCopy
{
    internal static CyclePhaseNotificationPresentation Create(CyclePhaseTransition transition)
    {
        var title = transition.EndedKind switch
        {
            SessionPhaseKind.Focus => string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.PhaseNotificationFocusEndedTitle,
                transition.EndedFocusCycleNumber),
            SessionPhaseKind.ShortBreak => UiStrings.PhaseNotificationShortBreakEndedTitle,
            _ => transition.StartedKind switch
            {
                SessionPhaseKind.Focus => string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.PhaseNotificationFocusStartedTitle,
                    transition.StartedFocusCycleNumber),
                SessionPhaseKind.ShortBreak => UiStrings.PhaseNotificationShortBreakStartedTitle,
                _ => throw new ArgumentOutOfRangeException(nameof(transition))
            }
        };

        var body = transition.StartedKind switch
        {
            SessionPhaseKind.Focus => string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.PhaseNotificationFocusStartedBody,
                transition.StartedFocusCycleNumber,
                transition.StartedEndUtc?.ToLocalTime()),
            SessionPhaseKind.ShortBreak => string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.PhaseNotificationShortBreakStartedBody,
                transition.StartedEndUtc?.ToLocalTime()),
            _ => transition.EndedKind switch
            {
                SessionPhaseKind.Focus => UiStrings.PhaseNotificationFocusEndedBody,
                SessionPhaseKind.ShortBreak => UiStrings.PhaseNotificationShortBreakEndedBody,
                _ => throw new ArgumentOutOfRangeException(nameof(transition))
            }
        };

        var glyph = transition.StartedKind == SessionPhaseKind.ShortBreak
            ? "\uE823"
            : "\uE121";
        return new CyclePhaseNotificationPresentation(title, body, glyph);
    }
}
