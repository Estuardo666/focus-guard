namespace FocusGuard.Sync;

/// <summary>Durable local edit entry point that wakes sync only after the outbox and projection commit.</summary>
public sealed class SettingsAutomaticSyncCoordinator(LocalSettingsCoordinator local, SettingsAutomaticSyncRuntime runtime)
{
    public async Task<LocalSettingsApplyResult> ImportReviewedAsync(SettingsImportPreview preview,
        LocalSettingsOverrides overrides, CancellationToken token = default)
    {
        var result = await local.ImportReviewedAsync(preview, overrides, token);
        if (result.SyncWakeRequired) runtime.NotifyLocalSettingsChanged();
        return result;
    }

    public async Task<LocalSettingsApplyResult> EditAsync(GlobalSettings proposed, LocalSettingsOverrides overrides,
        CancellationToken token = default)
    {
        var result = await local.EditAsync(proposed, overrides, token);
        if (result.SyncWakeRequired) runtime.NotifyLocalSettingsChanged();
        return result;
    }

    public async Task<LocalSettingsApplyResult> EditSelectedAsync(GlobalSettings proposed, SettingsFields selected,
        LocalSettingsOverrides overrides, CancellationToken token = default)
    {
        var result = await local.EditSelectedAsync(proposed, selected, overrides, token);
        if (result.SyncWakeRequired) runtime.NotifyLocalSettingsChanged();
        return result;
    }

    /// <summary>Persist or clear local-only choices without enabling consent or scheduling network work.</summary>
    public Task<LocalSettingsApplyResult> UpdateOverridesAsync(long expectedRevision,
        LocalSettingsOverrides expectedOverrides, LocalSettingsOverrides selectedOverrides,
        CancellationToken token = default, GlobalSettings? expectedEffective = null) =>
        local.UpdateOverridesAsync(expectedRevision, expectedOverrides, selectedOverrides, token, expectedEffective);

    public async Task<LocalSettingsApplyResult> ResetSharedAsync(long expectedRevision, GlobalSettings reviewedCurrent,
        bool ownerConfirmed, LocalSettingsOverrides overrides, CancellationToken token = default)
    {
        var result = await local.ResetSharedAsync(expectedRevision, reviewedCurrent, ownerConfirmed, overrides, token);
        runtime.NotifyLocalSettingsChanged();
        return result;
    }

    /// <summary>Reapply durable state locally without scheduling a network attempt.</summary>
    public Task<LocalSettingsApplyResult> ReapplyAsync(LocalSettingsOverrides overrides, CancellationToken token = default) =>
        local.ReapplyAsync(overrides, token);
}
