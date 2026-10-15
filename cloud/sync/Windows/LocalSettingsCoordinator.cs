using FocusGuard.Client.Preferences;

namespace FocusGuard.Sync;

public sealed record LocalSettingsApplyResult(DesktopSettings Preferences, bool Pending, SettingsFields Conflicts,
    bool SyncWakeRequired = true);

public sealed class SettingsResetConflictException : InvalidOperationException
{
    public SettingsResetConflictException() : base("Shared settings changed or are not ready for a reset preview.") { }
}

/// <summary>Preparation-only orchestration. No network, enabling consent or changes to active sessions.</summary>
public sealed class LocalSettingsCoordinator(SettingsOutboxVault outbox, DesktopSettingsSyncStore preferences)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Apply the exact native import review against the latest durable state under the outbox lease.</summary>
    public async Task<LocalSettingsApplyResult> ImportReviewedAsync(SettingsImportPreview preview,
        LocalSettingsOverrides overrides, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(overrides);
        var syncWakeRequired = false;
        await gate.WaitAsync(token);
        try
        {
            return await outbox.UpdateAndProjectAsync(current =>
            {
                var state = Required(current);
                if (!state.SyncConsentEnabled)
                    throw new InvalidOperationException("Sync consent changed while the import was being reviewed.");
                var effective = state.LocalOverrides.WithExplicit(overrides)
                    .Apply(state.Draft ?? state.Confirmed.EffectiveValues);
                var proposed = preview.Confirm(effective, ownerConfirmed: true);
                var edited = ApplySelectedEdit(state, proposed, preview.Selected);
                syncWakeRequired = SharedIntentChanged(state, edited);
                return edited;
            }, async (state, cancellation) =>
            {
                var applied = await preferences.ApplyAsync(state, overrides, cancellation);
                return new LocalSettingsApplyResult(applied, state.Pending, state.Conflicts, syncWakeRequired);
            }, token);
        }
        finally { gate.Release(); }
    }

    public async Task<LocalSettingsApplyResult> EditSelectedAsync(GlobalSettings proposed, SettingsFields selected,
        LocalSettingsOverrides overrides, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(proposed); proposed.Validate();
        ArgumentNullException.ThrowIfNull(overrides); _ = overrides.Apply(proposed);
        const SettingsFields allowed = SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration;
        if (selected == SettingsFields.None || (selected & ~allowed) != 0) throw new ArgumentException("Select permitted settings fields.");
        var syncWakeRequired = false;
        await gate.WaitAsync(token);
        try
        {
            return await outbox.UpdateAndProjectAsync(current =>
            {
                var state = Required(current);
                var edited = ApplySelectedEdit(state, proposed, selected);
                syncWakeRequired = SharedIntentChanged(state, edited);
                return edited;
            }, async (state, cancellation) =>
            {
                var applied = await preferences.ApplyAsync(state, overrides, cancellation);
                return new LocalSettingsApplyResult(applied, state.Pending, state.Conflicts, syncWakeRequired);
            }, token);
        }
        finally { gate.Release(); }
    }

    public async Task<LocalSettingsApplyResult> EditAsync(GlobalSettings selected, LocalSettingsOverrides overrides,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(selected); selected.Validate();
        ArgumentNullException.ThrowIfNull(overrides); _ = overrides.Apply(selected);
        var all = SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration;
        var syncWakeRequired = false;
        await gate.WaitAsync(token);
        try
        {
            return await outbox.UpdateAndProjectAsync(current =>
            {
                var state = Required(current);
                var edited = ApplySelectedEdit(state, selected, all);
                syncWakeRequired = SharedIntentChanged(state, edited);
                return edited;
            },
                async (state, cancellation) =>
                {
                    var applied = await preferences.ApplyAsync(state, overrides, cancellation);
                    return new LocalSettingsApplyResult(applied, state.Pending, state.Conflicts, syncWakeRequired);
                }, token);
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Replace/clear the per-device override selection after verifying the review is still current.
    /// This operation only persists and projects local state; it never grants consent or wakes sync.
    /// </summary>
    public async Task<LocalSettingsApplyResult> UpdateOverridesAsync(long expectedRevision,
        LocalSettingsOverrides expectedOverrides, LocalSettingsOverrides selectedOverrides,
        CancellationToken token = default, GlobalSettings? expectedEffective = null)
    {
        ArgumentNullException.ThrowIfNull(expectedOverrides);
        ArgumentNullException.ThrowIfNull(selectedOverrides);
        _ = expectedOverrides.Apply(GlobalSettings.Default);
        _ = selectedOverrides.Apply(GlobalSettings.Default);
        expectedEffective?.Validate();
        if (expectedRevision is < 0 or > 9_007_199_254_740_991)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        await gate.WaitAsync(token);
        try
        {
            return await outbox.UpdateAndProjectAsync(current =>
            {
                var state = Required(current);
                if (state.Confirmed.Revision != expectedRevision || state.LocalOverrides != expectedOverrides)
                    throw new InvalidOperationException("Settings or local overrides changed while the choice was being reviewed; refresh before saving.");
                var effective = state.LocalOverrides.Apply(state.Draft ?? state.Confirmed.EffectiveValues);
                if (expectedEffective is not null && effective != expectedEffective)
                    throw new InvalidOperationException("Settings draft changed while the local choice was being reviewed; refresh before saving.");
                return state with { LocalOverrides = selectedOverrides };
            }, async (state, cancellation) =>
            {
                var applied = await preferences.ApplyAsync(state, new(), cancellation);
                return new LocalSettingsApplyResult(applied, state.Pending, state.Conflicts, SyncWakeRequired: false);
            }, token);
        }
        finally { gate.Release(); }
    }

    public async Task<LocalSettingsApplyResult> ResetSharedAsync(long expectedRevision, GlobalSettings reviewedCurrent,
        bool ownerConfirmed, LocalSettingsOverrides overrides, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(reviewedCurrent);
        ArgumentNullException.ThrowIfNull(overrides);
        reviewedCurrent.Validate();
        if (expectedRevision is < 0 or > 9_007_199_254_740_991)
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (!ownerConfirmed) throw new InvalidOperationException("Reset requires deliberate local confirmation.");

        await gate.WaitAsync(token);
        try
        {
            return await outbox.UpdateAndProjectAsync(current =>
            {
                var state = Required(current);
                if (!state.SyncConsentEnabled || state.Confirmed.Deleted || state.Confirmed.Revision != expectedRevision ||
                    state.Confirmed.EffectiveValues != reviewedCurrent || state.Draft is not null ||
                    state.InFlight is not null || state.Conflicts != SettingsFields.None || state.DraftDeleted)
                    throw new SettingsResetConflictException();
                return state.RequestReset(ownerConfirmed: true);
            }, async (state, cancellation) =>
            {
                var applied = await preferences.ApplyAsync(state, overrides, cancellation);
                return new LocalSettingsApplyResult(applied, state.Pending, state.Conflicts, SyncWakeRequired: true);
            }, token);
        }
        finally { gate.Release(); }
    }

    public async Task<LocalSettingsApplyResult> ReapplyAsync(LocalSettingsOverrides overrides, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            return await outbox.ProjectCurrentAsync(async (current, cancellation) =>
            {
                var state = Required(current);
                var applied = await preferences.ApplyAsync(state, overrides, cancellation);
                return new LocalSettingsApplyResult(applied, state.Pending, state.Conflicts, SyncWakeRequired: false);
            }, token);
        }
        finally { gate.Release(); }
    }

    private static SettingsOutbox Required(SettingsOutbox? state) => state ??
        throw new InvalidOperationException("Verified settings baseline must be initialized explicitly.");

    private static SettingsOutbox ApplySelectedEdit(SettingsOutbox state, GlobalSettings proposed, SettingsFields selected)
    {
        var latest = state.Draft ?? state.Confirmed.EffectiveValues;
        var overrides = state.LocalOverrides;
        var shared = latest;
        var hasSharedSelection = false;

        if (selected.HasFlag(SettingsFields.Theme))
        {
            if (overrides.Theme is not null) overrides = overrides with { Theme = proposed.Theme };
            else { shared = shared with { Theme = proposed.Theme }; hasSharedSelection = true; }
        }
        if (selected.HasFlag(SettingsFields.Language))
        {
            if (overrides.UiLanguage is not null) overrides = overrides with { UiLanguage = proposed.UiLanguage };
            else { shared = shared with { UiLanguage = proposed.UiLanguage }; hasSharedSelection = true; }
        }
        if (selected.HasFlag(SettingsFields.Duration))
        {
            if (overrides.DefaultDurationMinutes is not null)
                overrides = overrides with { DefaultDurationMinutes = proposed.DefaultDurationMinutes };
            else { shared = shared with { DefaultDurationMinutes = proposed.DefaultDurationMinutes }; hasSharedSelection = true; }
        }

        var result = hasSharedSelection ? state.Edit(shared) : state;
        return result with { LocalOverrides = overrides };
    }

    private static bool SharedIntentChanged(SettingsOutbox before, SettingsOutbox after) =>
        before.Draft != after.Draft || before.DraftDeleted != after.DraftDeleted;
}
