namespace FocusGuard.Sync;

/// <summary>Explicit one-shot CAS create. Does not grant automatic-sync consent or infer absence from 404.</summary>
public sealed class SettingsDocumentInitializer(SettingsOutboxVault vault, ISettingsSyncCryptography crypto,
    ISettingsDocumentTransport transport)
{
    public async Task<SettingsOutbox> InitializeAsync(GlobalSettings chosen, bool ownerConfirmed, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(chosen); chosen.Validate();
        if (!ownerConfirmed) throw new InvalidOperationException("Creating a global document requires deliberate confirmation.");
        vault.RequireScope(crypto.Account.Issuer, crypto.Account.AccountId, crypto.Account.GenerationId, crypto.DeviceId);
        var durable = await vault.UpdateAsync(current =>
        {
            if (current is null) return new SettingsOutbox(new(0, GlobalSettings.Default), Draft: chosen).Prepare(crypto.Prepare);
            current.Validate();
            if (current.Confirmed.Revision != 0 || current.InFlight is null || current.InFlight.Intent.BaseRevision != 0 ||
                current.InFlight.Intent.Values != chosen || current.InFlight.Intent.Deleted)
                throw new InvalidOperationException("An existing local sync state requires reconciliation, not initialization.");
            return current; // A lost-response retry preserves the exact ciphertext and operation.
        }, token);
        var write = durable.InFlight ?? throw new InvalidOperationException("Missing durable create intent.");
        // On retry, an authenticated read may recover a previously committed response.
        var existing = await ReadForExplicitCreateAsync(token);
        if (existing is not null)
            return await ReconcileAsync(existing, write.Intent.OperationId, token);
        var result = await transport.PublishAsync(write.Intent.OperationId, write.GetEnvelope(), token);
        if (!Enum.IsDefined(result)) throw new InvalidDataException("Unknown create outcome.");
        var committed = await transport.ReadAsync(token);
        if (committed is null) throw new InvalidDataException("Create outcome has no committed document.");
        return await ReconcileAsync(committed, write.Intent.OperationId, token);
    }

    private async Task<byte[]?> ReadForExplicitCreateAsync(CancellationToken token)
    {
        try { return await transport.ReadAsync(token); }
        catch (SettingsTransportException error) when (error.Failure == SettingsTransportFailure.AmbiguousMissing)
        {
            // Not evidence of absence: only the separately confirmed signed base0 CAS may create anything.
            return null;
        }
    }

    private Task<SettingsOutbox> ReconcileAsync(byte[] committed, Guid operationId, CancellationToken token)
    {
        var verified = crypto.Open(committed);
        return vault.UpdateAsync(current =>
        {
            var state = current ?? throw new InvalidOperationException("Missing local create state.");
            if (state.InFlight?.Intent.OperationId != operationId) throw new InvalidOperationException("Create state changed during request.");
            var ownCommit = crypto.Confirm(state, committed);
            return ownCommit != state ? ownCommit : state.Receive(verified, casRejected: true);
        }, token);
    }
}
