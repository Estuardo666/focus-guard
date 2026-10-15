namespace FocusGuard.Sync;

/// <summary>Persists a user's conflict choice before waking the automatic sync runtime.</summary>
public sealed class SettingsConflictResolutionService(SettingsOutboxVault vault, DesktopSettingsSyncStore preferences,
    LocalSettingsOverrides overrides, SettingsAutomaticSyncRuntime runtime)
{
    private const SettingsFields AllFields = SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration;

    public async Task<SettingsOutbox> ResolveAsync(GlobalSettings chosen, GlobalSettings reviewedDraft, long expectedRevision,
        SettingsFields expectedConflicts, bool ownerConfirmed, CancellationToken token = default)
    {
        if (!ownerConfirmed) throw new InvalidOperationException("Conflict resolution requires deliberate user confirmation.");
        ArgumentNullException.ThrowIfNull(chosen);
        ArgumentNullException.ThrowIfNull(reviewedDraft);
        chosen.Validate();
        reviewedDraft.Validate();
        if (expectedRevision is < 0 or > 9_007_199_254_740_991) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (expectedConflicts == SettingsFields.None || (expectedConflicts & ~AllFields) != 0)
            throw new ArgumentOutOfRangeException(nameof(expectedConflicts));

        var resolved = await vault.UpdateAndProjectAsync(current =>
        {
            if (current is null) throw new InvalidOperationException("Settings outbox is not initialized.");
            if (current.Confirmed.Revision != expectedRevision || current.Conflicts != expectedConflicts || current.Draft != reviewedDraft)
                throw new InvalidOperationException("Conflict changed while it was being reviewed; refresh before resolving.");
            if (current.DraftDeleted)
                throw new InvalidOperationException("A reset conflict requires its dedicated reset recovery flow.");
            var local = current.Draft ?? throw new InvalidOperationException("Conflicted draft is unavailable.");
            var merged = new GlobalSettings(
                (expectedConflicts & SettingsFields.Duration) != 0 ? chosen.DefaultDurationMinutes : local.DefaultDurationMinutes,
                (expectedConflicts & SettingsFields.Theme) != 0 ? chosen.Theme : local.Theme,
                (expectedConflicts & SettingsFields.Language) != 0 ? chosen.UiLanguage : local.UiLanguage);
            return current.Resolve(merged);
        }, async (state, cancellation) =>
        {
            await preferences.ApplyAsync(state, overrides, cancellation);
            return state;
        }, token);

        runtime.NotifyConflictResolved();
        return resolved;
    }

    /// <summary>
    /// Resolves a concurrent reset as an all-or-nothing choice. Keeping the local reset
    /// re-bases its tombstone on the reviewed remote revision; accepting remote state
    /// cancels only this device's pending reset.
    /// </summary>
    public async Task<SettingsOutbox> ResolveResetAsync(bool keepThisDeviceReset, GlobalSettings reviewedDraft,
        long expectedRevision, SettingsFields expectedConflicts, bool ownerConfirmed, CancellationToken token = default)
    {
        if (!ownerConfirmed) throw new InvalidOperationException("Reset conflict resolution requires deliberate confirmation.");
        ArgumentNullException.ThrowIfNull(reviewedDraft);
        reviewedDraft.Validate();
        if (expectedRevision is < 0 or > 9_007_199_254_740_991) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (expectedConflicts == SettingsFields.None || (expectedConflicts & ~AllFields) != 0)
            throw new ArgumentOutOfRangeException(nameof(expectedConflicts));

        var resolved = await vault.UpdateAndProjectAsync(current =>
        {
            if (current is null) throw new InvalidOperationException("Settings outbox is not initialized.");
            if (current.Confirmed.Revision != expectedRevision || current.Conflicts != expectedConflicts ||
                current.Draft != reviewedDraft || !current.DraftDeleted || current.InFlight is not null)
                throw new InvalidOperationException("Reset conflict changed while it was being reviewed; refresh before resolving.");

            return current.ResolveReset(keepThisDeviceReset);
        }, async (state, cancellation) =>
        {
            await preferences.ApplyAsync(state, overrides, cancellation);
            return state;
        }, token);

        runtime.NotifyConflictResolved();
        return resolved;
    }
}
