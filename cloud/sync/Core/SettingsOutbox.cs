namespace FocusGuard.Sync;

public sealed record PreparedSettingsWrite(SettingsWriteIntent Intent, string EnvelopeBase64)
{
    public byte[] GetEnvelope() => Convert.FromBase64String(EnvelopeBase64);
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Intent);
        Intent.Validate();
        if (string.IsNullOrEmpty(EnvelopeBase64) || EnvelopeBase64.Length > 90_000)
            throw new ArgumentException("Invalid prepared settings envelope.");
        var bytes = GetEnvelope();
        if (bytes.Length is < 1 or > 65_536 || Convert.ToBase64String(bytes) != EnvelopeBase64)
            throw new ArgumentException("Invalid prepared settings envelope.");
    }
}

/// <summary>One in-flight CAS write plus a newer local draft. All state is persisted together.</summary>
public sealed record SettingsOutbox(SettingsSnapshot Confirmed, GlobalSettings? Draft = null,
    PreparedSettingsWrite? InFlight = null, SettingsFields Conflicts = SettingsFields.None, bool DraftDeleted = false,
    bool SyncConsentEnabled = false)
{
    /// <summary>Last account-wide transport cursor observed with a committed snapshot.</summary>
    public long Cursor { get; init; }

    /// <summary>Per-device presentation choices; stored locally and never included in a write intent.</summary>
    public LocalSettingsOverrides LocalOverrides { get; init; } = new();

    public bool Pending => Draft is not null;
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Confirmed);
        Confirmed.Validate(); Draft?.Validate(); InFlight?.Validate();
        if (LocalOverrides is null || Cursor is < 0 or > 9_007_199_254_740_991 ||
            (Conflicts & ~(SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration)) != 0 ||
            DraftDeleted && Draft != GlobalSettings.Default ||
            Conflicts != SettingsFields.None && (Draft is null || InFlight is not null) ||
            InFlight is not null && (Draft is null || InFlight.Intent.BaseRevision != Confirmed.Revision))
            throw new ArgumentException("Invalid settings outbox state.");
        _ = LocalOverrides.Apply(Confirmed.EffectiveValues);
    }

    public SettingsOutbox Edit(GlobalSettings values)
    {
        Validate(); ArgumentNullException.ThrowIfNull(values); values.Validate();
        return this with { Draft = InFlight is null && values == Confirmed.EffectiveValues && Conflicts == SettingsFields.None ? null : values, DraftDeleted = false };
    }

    public SettingsOutbox RequestReset(bool ownerConfirmed)
    {
        Validate();
        if (!ownerConfirmed) throw new InvalidOperationException("Reset requires deliberate confirmation.");
        return this with { Draft = GlobalSettings.Default, DraftDeleted = true, Conflicts = SettingsFields.None };
    }

    public SettingsOutbox Prepare(Func<SettingsWriteIntent, byte[]> encryptAndSign)
    {
        Validate(); ArgumentNullException.ThrowIfNull(encryptAndSign);
        if (InFlight is not null || Draft is null) return this;
        if (Conflicts != SettingsFields.None) throw new InvalidOperationException("Resolve settings conflicts before upload.");
        var intent = new SettingsWriteIntent(Guid.NewGuid(), Confirmed.Revision, Draft, DraftDeleted);
        intent.Validate();
        var envelope = new PreparedSettingsWrite(intent, Convert.ToBase64String(encryptAndSign(intent)));
        envelope.Validate();
        return this with { InFlight = envelope };
    }

    public SettingsOutbox Acknowledge(Guid operationId, long revision, GlobalSettings confirmed, bool deleted = false)
    {
        Validate();
        if (InFlight is null || !InFlight.Intent.MatchesAcknowledgement(operationId, revision, confirmed, deleted)) return this;
        var complete = Draft == confirmed && DraftDeleted == deleted;
        return this with
        {
            Confirmed = new(revision, confirmed, deleted),
            Draft = complete ? null : Draft,
            InFlight = null,
            Conflicts = SettingsFields.None,
            DraftDeleted = !complete && DraftDeleted
        };
    }

    public SettingsOutbox Receive(SettingsSnapshot verifiedRemote, bool casRejected = false)
    {
        Validate(); ArgumentNullException.ThrowIfNull(verifiedRemote);
        if (InFlight is not null && !casRejected)
            throw new InvalidOperationException("Resolve the in-flight write before advancing the snapshot.");
        if (DraftDeleted)
        {
            verifiedRemote.Validate();
            if (verifiedRemote.Revision < Confirmed.Revision || verifiedRemote.Revision == Confirmed.Revision && verifiedRemote != Confirmed)
                throw new InvalidOperationException("Reset snapshot rollback or equivocation.");
            var conflict = Conflicts | (verifiedRemote != Confirmed ? SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration : SettingsFields.None);
            return new(verifiedRemote, GlobalSettings.Default, Conflicts: conflict, DraftDeleted: true,
                SyncConsentEnabled: SyncConsentEnabled)
            {
                Cursor = Cursor,
                LocalOverrides = LocalOverrides
            };
        }
        var merge = SettingsReconciliation.Merge(Confirmed, Draft ?? Confirmed.EffectiveValues, verifiedRemote);
        var unresolved = Conflicts | merge.Conflicts;
        return new(verifiedRemote, merge.Proposed == verifiedRemote.EffectiveValues && unresolved == SettingsFields.None ? null : merge.Proposed,
            Conflicts: unresolved, SyncConsentEnabled: SyncConsentEnabled)
        {
            Cursor = Cursor,
            LocalOverrides = LocalOverrides
        };
    }

    // Deliberate user resolution may replace a tombstone; automatic reconciliation never does.
    public SettingsOutbox Resolve(GlobalSettings chosen)
    {
        Validate(); ArgumentNullException.ThrowIfNull(chosen); chosen.Validate();
        if (InFlight is not null) throw new InvalidOperationException("Resolve the in-flight write first.");
        return new(Confirmed, chosen == Confirmed.EffectiveValues ? null : chosen,
            SyncConsentEnabled: SyncConsentEnabled)
        {
            Cursor = Cursor,
            LocalOverrides = LocalOverrides
        };
    }

    /// <summary>Resolve a conflicted tombstone as a fresh reset intent or cancel it for current remote state.</summary>
    public SettingsOutbox ResolveReset(bool keepThisDeviceReset)
    {
        Validate();
        if (!DraftDeleted || Conflicts == SettingsFields.None || InFlight is not null)
            throw new InvalidOperationException("A conflicted reset is required.");
        return keepThisDeviceReset
            ? this with { Draft = GlobalSettings.Default, DraftDeleted = true, Conflicts = SettingsFields.None }
            : Resolve(Confirmed.EffectiveValues);
    }
}
