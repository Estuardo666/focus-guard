using FocusGuard.Client.Journal;
using FocusGuard.Client.Preferences;
using FocusGuard.Contracts;
using FocusGuard.Domain;
using FocusGuard.Storage;

namespace FocusGuard.Client.Workflows;

/// <summary>Optional user-local intentions and accepted configuration, not session authority.</summary>
public sealed class SessionJournalWorkflow(
    SessionJournalStore journal, DesktopSettingsStore settingsStore, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public SessionIntention? Get(Guid? sessionId) => sessionId is { } id ? journal.Get(id) : null;

    public SessionIntention? GetLatest(Guid? sessionId) => sessionId is { } id ? journal.GetLatest(id) : null;

    public DesktopSettings RememberAccepted(SessionIntention intention, DesktopSettings currentSettings)
    {
        var minutes = Math.Clamp(intention.DurationMinutes, 1, 720);
        var accepted = intention with { DurationMinutes = minutes };
        var settings = currentSettings with
        {
            LastProfileId = accepted.ProfileId,
            LastDurationMinutes = minutes,
            LastSessionMode = accepted.Mode,
            LastCyclePresetId = accepted.Mode == SessionMode.FocusCycle ? accepted.CyclePresetId : null
        };
        settingsStore.Save(settings);
        journal.Save(accepted);
        return settings;
    }

    public void RecordObservedSession(StatusResponse status, StoredProfile? profile, bool isActive)
    {
        if (!isActive || status.SessionId is not { } id || profile is null) return;
        if (status.ScheduledIntent is { } scheduled && scheduled.IsValid()
            && status.ProfileId == profile.Id.Value && !status.StrictMode)
        {
            journal.SaveObserved(new(id, profile.Id.Value, scheduled.ProfileName, scheduled.DurationMinutes,
                SessionMode.SimpleTimer, null, null, false, string.Empty, EnforcementEnabled: scheduled.EnforcementEnabled));
            return;
        }
        if (journal.Get(id) is not null) return;
        var minutes = Math.Clamp((int)Math.Ceiling(
            ((status.EndUtc ?? _clock.GetUtcNow()) - (status.StartUtc ?? _clock.GetUtcNow())).TotalMinutes), 1, 720);
        // Retain the original fallback: status alone cannot reconstruct cycle intent.
        journal.SaveIfAbsent(new(id, profile.Id.Value, profile.Name, minutes, SessionMode.SimpleTimer,
            null, null, status.StrictMode, "", EnforcementEnabled: null));
    }

    public bool SaveReflection(Guid? sessionId, string reflection)
    {
        if (sessionId is not { } id || id == Guid.Empty) return false;
        var fallback = new SessionIntention(id, Guid.Empty, "", 50, SessionMode.SimpleTimer,
            null, null, false, "", reflection);
        return journal.SaveReflection(id, reflection, fallback);
    }
}
