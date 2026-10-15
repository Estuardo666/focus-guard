namespace FocusGuard.Sync;

public enum SettingsPublishResult { Stored, RevisionConflict }
public enum SettingsSyncStatus { Disabled, Idle, Pending, Conflict }

/// <summary>Authenticated scoped transport. Read returns committed envelope bytes, never notifications or drafts.</summary>
public interface ISettingsDocumentTransport
{
    /// <summary>Account-wide cursor returned with the most recent committed snapshot, when supported by the server.</summary>
    long? LastSeenCursor => null;
    /// <summary>Lightweight account cursor poll; null means the server predates this optional endpoint.</summary>
    Task<long?> ReadCursorAsync(CancellationToken token) => Task.FromResult<long?>(null);
    Task<byte[]?> ReadAsync(CancellationToken token);
    Task<SettingsPublishResult> PublishAsync(Guid operationId, ReadOnlyMemory<byte> envelope, CancellationToken token);
}

/// <summary>One caller-driven tick, no timers or automatic enablement. Never publish before durable preparation.</summary>
public sealed class SettingsSyncWorker(SettingsOutboxVault vault, ISettingsSyncCryptography cryptography,
    ISettingsDocumentTransport transport, Func<bool> consentEnabled,
    Func<CancellationToken, Task<bool>>? persistentConsent = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<SettingsSyncStatus> TickAsync(CancellationToken token = default)
    {
        if (!consentEnabled()) return SettingsSyncStatus.Disabled;
        vault.RequireScope(cryptography.Account.Issuer, cryptography.Account.AccountId,
            cryptography.Account.GenerationId, cryptography.DeviceId);
        if (!await HasConsentAsync(token)) return SettingsSyncStatus.Disabled;
        await gate.WaitAsync(token);
        try
        {
            if (!await HasConsentAsync(token)) return SettingsSyncStatus.Disabled;
            vault.RequireScope(cryptography.Account.Issuer, cryptography.Account.AccountId,
                cryptography.Account.GenerationId, cryptography.DeviceId);
            _ = await vault.LoadAsync(token) ?? throw new InvalidOperationException("Initialize the verified sync baseline first.");
            var remoteCursor = await transport.ReadCursorAsync(token);
            if (!await HasConsentAsync(token)) return SettingsSyncStatus.Disabled;
            var current = await vault.LoadAsync(token) ??
                throw new InvalidOperationException("Initialize the verified settings baseline first.");
            if (remoteCursor is { } cursor)
            {
                if (cursor < current.Cursor) throw new InvalidDataException("Account sync cursor rollback rejected.");
                if (cursor == current.Cursor && !current.Pending && current.InFlight is null &&
                    current.Conflicts == SettingsFields.None)
                    return SettingsSyncStatus.Idle;
            }
            var remote = await transport.ReadAsync(token);
            if (!await HasConsentAsync(token)) return SettingsSyncStatus.Disabled;
            var state = await ApplyRemoteAsync(remote, token);
            if (state.Conflicts != SettingsFields.None) return SettingsSyncStatus.Conflict;
            if (!state.Pending) return SettingsSyncStatus.Idle;
            state = await vault.UpdateAsync(current => Required(current).Prepare(cryptography.Prepare), token);
            if (!await HasConsentAsync(token)) return SettingsSyncStatus.Disabled;
            var write = state.InFlight ?? throw new InvalidOperationException("Missing durable settings write.");
            var latest = await vault.LoadAsync(token);
            if (latest?.InFlight?.Intent.OperationId != write.Intent.OperationId) return SettingsSyncStatus.Pending;
            var outcome = await transport.PublishAsync(write.Intent.OperationId, write.GetEnvelope(), token);
            if (!Enum.IsDefined(outcome)) throw new InvalidDataException("Unknown publication outcome.");
            if (!await HasConsentAsync(token)) return SettingsSyncStatus.Disabled;
            // A 201/receipt alone never clears the outbox: verify the committed encrypted document.
            remote = await transport.ReadAsync(token);
            if (!await HasConsentAsync(token)) return SettingsSyncStatus.Disabled;
            if (remote is null) return SettingsSyncStatus.Pending;
            state = await ApplyRemoteAsync(remote, token, write.Intent.OperationId);
            return state.Conflicts != SettingsFields.None ? SettingsSyncStatus.Conflict :
                state.Pending ? SettingsSyncStatus.Pending : SettingsSyncStatus.Idle;
        }
        finally { gate.Release(); }
    }

    private async Task<SettingsOutbox> ApplyRemoteAsync(byte[]? document, CancellationToken token, Guid? expectedOperation = null)
    {
        var cursor = transport.LastSeenCursor;
        var verified = document is null ? null : cryptography.Open(document);
        return await vault.UpdateAsync(current =>
        {
            var state = Required(current);
            if (cursor is { } receivedCursor)
            {
                if (receivedCursor < state.Cursor)
                    throw new InvalidDataException("Account sync cursor rollback rejected.");
                if (receivedCursor > state.Cursor) state = state with { Cursor = receivedCursor };
            }
            if (expectedOperation is { } id && state.InFlight?.Intent.OperationId != id) return state;
            if (verified is null)
            {
                if (state.Confirmed.Revision != 0) throw new InvalidDataException("Committed settings document is missing.");
                return state;
            }
            var confirmed = cryptography.Confirm(state, document!);
            if (confirmed != state) return confirmed;
            if (verified.Revision < state.Confirmed.Revision) throw new InvalidDataException("Settings rollback rejected.");
            if (verified.Revision == state.Confirmed.Revision)
            {
                if (verified != state.Confirmed) throw new InvalidDataException("Settings revision equivocation rejected.");
                return state;
            }
            return state.Receive(verified, casRejected: state.InFlight is not null);
        }, token);
    }

    private static SettingsOutbox Required(SettingsOutbox? state) => state ??
        throw new InvalidOperationException("Missing initialized settings state.");

    private async Task<bool> HasConsentAsync(CancellationToken token) => consentEnabled() &&
        (await vault.LoadAsync(token))?.SyncConsentEnabled == true &&
        (persistentConsent is null || await persistentConsent(token)) && consentEnabled();
}
