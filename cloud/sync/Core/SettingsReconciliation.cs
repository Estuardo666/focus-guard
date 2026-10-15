namespace FocusGuard.Sync;

[Flags]
public enum SettingsFields { None = 0, Theme = 1, Language = 2, Duration = 4 }

public sealed record SettingsSnapshot(long Revision, GlobalSettings Values, bool Deleted = false)
{
    public GlobalSettings EffectiveValues => Deleted ? GlobalSettings.Default : Values;
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Values);
        Values.Validate();
        if (Revision is < 0 or > 9_007_199_254_740_991) throw new ArgumentException("Invalid settings revision.");
        if (Deleted && Values != GlobalSettings.Default) throw new ArgumentException("Tombstone must carry only defaults.");
    }
}

public sealed record SettingsMerge(GlobalSettings Proposed, SettingsFields Conflicts, long BaseRevision)
{
    public bool CanUpload => Conflicts == SettingsFields.None;
}

/// <summary>Pure preparation logic. Input snapshots must first pass E2EE and membership verification.</summary>
public static class SettingsReconciliation
{
    public static SettingsMerge Merge(SettingsSnapshot baseline, GlobalSettings local, SettingsSnapshot remote)
    {
        baseline.Validate(); remote.Validate(); local.Validate();
        if (remote.Revision < baseline.Revision || remote.Revision == baseline.Revision && remote != baseline)
            throw new InvalidOperationException("Rollback or equivocal settings snapshot.");
        var before = baseline.EffectiveValues;
        var after = remote.EffectiveValues;
        var conflicts = SettingsFields.None;
        T Select<T>(T original, T pending, T received, SettingsFields field) where T : IEquatable<T>
        {
            if (pending.Equals(original)) return received;
            if (!received.Equals(original) && !pending.Equals(received)) conflicts |= field;
            return pending; // Preserve the offline edit, but never upload an unresolved conflict.
        }
        var duration = Select(before.DefaultDurationMinutes, local.DefaultDurationMinutes, after.DefaultDurationMinutes, SettingsFields.Duration);
        var theme = Select(before.Theme, local.Theme, after.Theme, SettingsFields.Theme);
        var language = Select(before.UiLanguage, local.UiLanguage, after.UiLanguage, SettingsFields.Language);
        // A deletion is an operation, not three independent assignments to defaults.
        // An offline edit predating it must not silently resurrect the deleted document.
        if (remote.Deleted && !baseline.Deleted)
        {
            if (local.Theme != before.Theme) conflicts |= SettingsFields.Theme;
            if (local.UiLanguage != before.UiLanguage) conflicts |= SettingsFields.Language;
            if (local.DefaultDurationMinutes != before.DefaultDurationMinutes) conflicts |= SettingsFields.Duration;
        }
        return new(new(duration, theme, language), conflicts, remote.Revision);
    }
}

/// <summary>A stable CAS/idempotency proposal retained unchanged across retries.</summary>
public sealed record SettingsWriteIntent(Guid OperationId, long BaseRevision, GlobalSettings Values, bool Deleted = false)
{
    public long ProposedRevision => checked(BaseRevision + 1);
    public void Validate()
    {
        if (OperationId == Guid.Empty || BaseRevision is < 0 or > 9_007_199_254_740_990)
            throw new ArgumentException("Invalid settings write intent.");
        ArgumentNullException.ThrowIfNull(Values); Values.Validate();
        if (Deleted && Values != GlobalSettings.Default) throw new ArgumentException("Reset intent must contain only defaults.");
    }
    public bool MatchesAcknowledgement(Guid operationId, long revision, GlobalSettings confirmed, bool deleted = false)
    {
        Validate(); ArgumentNullException.ThrowIfNull(confirmed); confirmed.Validate();
        return operationId == OperationId && revision == ProposedRevision && confirmed == Values && deleted == Deleted;
    }
}

/// <summary>Overrides affect presentation only and are never part of the cloud payload.</summary>
public sealed record LocalSettingsOverrides(string? Theme = null, string? UiLanguage = null, int? DefaultDurationMinutes = null)
{
    /// <summary>Call-site overrides take precedence over durable per-device choices.</summary>
    public LocalSettingsOverrides WithExplicit(LocalSettingsOverrides explicitOverrides)
    {
        ArgumentNullException.ThrowIfNull(explicitOverrides);
        return new(explicitOverrides.Theme ?? Theme, explicitOverrides.UiLanguage ?? UiLanguage,
            explicitOverrides.DefaultDurationMinutes ?? DefaultDurationMinutes);
    }

    public GlobalSettings Apply(GlobalSettings global)
    {
        ArgumentNullException.ThrowIfNull(global); global.Validate();
        var effective = new GlobalSettings(DefaultDurationMinutes ?? global.DefaultDurationMinutes,
            Theme ?? global.Theme, UiLanguage ?? global.UiLanguage);
        effective.Validate(); return effective;
    }
}
