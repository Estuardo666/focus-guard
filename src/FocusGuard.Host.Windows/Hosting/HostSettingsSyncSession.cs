using System.Security;
using System.Text.Json;
using FocusGuard.Client.Preferences;
using FocusGuard.Devices;
using FocusGuard.Identity;
using FocusGuard.Sync;

namespace FocusGuard.Host.Windows.Hosting;

/// <summary>
/// Owns the Windows settings-sync lifetime for one linked device. Creation is inert;
/// local storage and network access begin only after an explicit owner opt-in.
/// </summary>
internal sealed class HostSettingsSyncSession : IAsyncDisposable
{
    internal const string GlobalSettingsEntityId = "8a1792bc-4303-4e70-a9af-fbdbf56e1a37";

    private readonly WindowsDeviceVault.LoadedDeviceVault deviceVault;
    private readonly SettingsSyncCryptography cryptography;
    private readonly SettingsSyncScope scope;
    private readonly string syncRoot;
    private readonly string syncDirectory;
    private readonly DesktopSettingsStore desktopSettings;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly SettingsAutomaticSyncRuntime runtime;
    private int disposed;

    private HostSettingsSyncSession(WindowsDeviceVault.LoadedDeviceVault deviceVault,
        SettingsSyncCryptography cryptography, SettingsSyncScope scope, string syncRoot,
        string syncDirectory, DesktopSettingsStore desktopSettings, SettingsOutboxVault outbox,
        SettingsConsentStore consent, SettingsDocumentInitializer initializer,
        SettingsAutomaticSyncRuntime runtime, LocalSettingsCoordinator localSettings,
        SettingsAutomaticSyncCoordinator edits, SettingsConflictResolutionService conflicts)
    {
        this.deviceVault = deviceVault;
        this.cryptography = cryptography;
        this.scope = scope;
        this.syncRoot = syncRoot;
        this.syncDirectory = syncDirectory;
        this.desktopSettings = desktopSettings;
        Outbox = outbox;
        Consent = consent;
        Initializer = initializer;
        this.runtime = runtime;
        LocalSettings = localSettings;
        Edits = edits;
        Conflicts = conflicts;
    }

    internal SettingsOutboxVault Outbox { get; }
    internal SettingsConsentStore Consent { get; }
    internal SettingsDocumentInitializer Initializer { get; }
    internal LocalSettingsCoordinator LocalSettings { get; }
    internal SettingsAutomaticSyncCoordinator Edits { get; }
    internal SettingsConflictResolutionService Conflicts { get; }

    internal static async Task<bool> HasSavedConsentAsync(DeviceAccountContext account, string deviceId,
        CancellationToken token = default, string? localSyncRoot = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        account.Validate();
        DeviceEncoding.RequireId(deviceId);
        var scope = new SettingsSyncScope(account.Issuer, account.AccountId, account.GenerationId, deviceId);
        scope.Validate();
        var root = localSyncRoot ?? SettingsSyncLocalStorage.GetCurrentUserRoot();
        var directory = SettingsSyncLocalStorage.GetScopedDirectory(root, scope);
        if (!Directory.Exists(directory)) return false;
        _ = SettingsSyncLocalStorage.PrepareScopedDirectory(root, scope);
        var vault = new SettingsOutboxVault(Path.Combine(directory, "settings-outbox.dpapi"), scope);
        return (await vault.LoadAsync(token).ConfigureAwait(false))?.SyncConsentEnabled == true;
    }

    /// <summary>Production composition. The caller must keep IdentitySession alive while this session runs.</summary>
    internal static HostSettingsSyncSession Create(Uri apiOrigin, ISettingsRequestSender sender,
        WindowsDeviceVault.LoadedDeviceVault deviceVault, MembershipDocument[] verifiedMembershipChain,
        DesktopSettingsStore desktopSettings, LocalSettingsOverrides? overrides = null,
        TimeProvider? timeProvider = null,
        Func<SettingsRetryResult, SettingsOutbox?, CancellationToken, Task>? report = null,
        string? testSyncRoot = null)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(deviceVault);
        if (!string.Equals(sender.AccountBinding, deviceVault.Context.AccountBinding, StringComparison.Ordinal))
            throw new SecurityException("Settings transport identity does not match the linked device account.");

        var transport = new SettingsDocumentHttpTransport(apiOrigin, sender, deviceVault.Context,
            deviceVault.Material.Identity, deviceVault.Material.DeviceSession ??
                throw new SecurityException("A renewed device session is required for settings sync."), GlobalSettingsEntityId,
            timeProvider);
        return CreateCore(sender.AccountBinding, transport, deviceVault, verifiedMembershipChain,
            desktopSettings, overrides, testSyncRoot ?? SettingsSyncLocalStorage.GetCurrentUserRoot(), timeProvider, report);
    }

    /// <summary>Test seam: preserves all membership, account, storage and consent checks while replacing HTTP.</summary>
    internal static HostSettingsSyncSession CreateForTesting(string? senderAccountBinding,
        ISettingsDocumentTransport transport, WindowsDeviceVault.LoadedDeviceVault deviceVault,
        MembershipDocument[] verifiedMembershipChain, DesktopSettingsStore desktopSettings,
        string syncRoot, TimeProvider? timeProvider = null,
        Func<SettingsRetryResult, SettingsOutbox?, CancellationToken, Task>? report = null)
    {
        if (!string.Equals(senderAccountBinding, deviceVault.Context.AccountBinding, StringComparison.Ordinal))
            throw new SecurityException("Settings transport identity does not match the linked device account.");
        return CreateCore(senderAccountBinding, transport, deviceVault, verifiedMembershipChain,
            desktopSettings, new LocalSettingsOverrides(), syncRoot, timeProvider, report);
    }

    private static HostSettingsSyncSession CreateCore(string? senderAccountBinding,
        ISettingsDocumentTransport transport, WindowsDeviceVault.LoadedDeviceVault deviceVault,
        MembershipDocument[] verifiedMembershipChain, DesktopSettingsStore desktopSettings,
        LocalSettingsOverrides? overrides, string syncRoot, TimeProvider? timeProvider,
        Func<SettingsRetryResult, SettingsOutbox?, CancellationToken, Task>? report)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(deviceVault);
        ArgumentNullException.ThrowIfNull(verifiedMembershipChain);
        ArgumentNullException.ThrowIfNull(desktopSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(syncRoot);
        if (!desktopSettings.Persists)
            throw new InvalidOperationException("Automatic settings sync requires durable desktop preferences.");
        if (!string.Equals(senderAccountBinding, deviceVault.Context.AccountBinding, StringComparison.Ordinal))
            throw new SecurityException("Settings transport identity does not match the linked device account.");
        if (deviceVault.DeviceId != deviceVault.Material.Identity.Public.DeviceId ||
            deviceVault.Material.DeviceSession is null)
            throw new SecurityException("The linked device vault is incomplete.");

        var context = deviceVault.Context;
        context.Validate();
        var authorHistory = new PinnedSettingsAuthor(verifiedMembershipChain, context,
            deviceVault.Material.MembershipRevision, deviceVault.Material.MembershipDigest);
        _ = authorHistory.Resolve(deviceVault.Material.MembershipRevision, deviceVault.DeviceId);

        var cryptography = SettingsSyncContextFactory.Create(context, deviceVault.Material,
            GlobalSettingsEntityId, verifiedMembershipChain);
        try
        {
            var scope = new SettingsSyncScope(context.Issuer, context.AccountId,
                context.GenerationId, deviceVault.DeviceId);
            scope.Validate();
            var root = Path.GetFullPath(syncRoot);
            var directory = SettingsSyncLocalStorage.GetScopedDirectory(root, scope);
            var outbox = new SettingsOutboxVault(Path.Combine(directory, "settings-outbox.dpapi"), scope);
            var consent = new SettingsConsentStore(outbox);
            var preferences = new DesktopSettingsSyncStore(desktopSettings.FilePath);
            var effectiveOverrides = overrides ?? new LocalSettingsOverrides();
            var retry = new SettingsRetryCheckpointStore(Path.Combine(directory, "settings-retry.dpapi"), scope);
            SettingsAutomaticSyncRuntime? runtime = null;
            runtime = new SettingsAutomaticSyncRuntime(consent,
                (enabled, persistedConsent) => new SettingsSyncDesktopTick(
                    new SettingsSyncWorker(outbox, cryptography, transport, enabled, persistedConsent),
                    outbox, preferences, effectiveOverrides).RunAsync,
                retry, timeProvider,
                report: report is null ? null : async (result, token) =>
                {
                    var state = await outbox.LoadAsync(token).ConfigureAwait(false);
                    await report(result, state, token).ConfigureAwait(false);
                });
            var initializer = new SettingsDocumentInitializer(outbox, cryptography, transport);
            var localSettings = new LocalSettingsCoordinator(outbox, preferences);
            var edits = new SettingsAutomaticSyncCoordinator(localSettings, runtime);
            var conflicts = new SettingsConflictResolutionService(outbox, preferences,
                effectiveOverrides, runtime);
            return new HostSettingsSyncSession(deviceVault, cryptography, scope, root, directory,
                desktopSettings, outbox, consent, initializer, runtime, localSettings, edits, conflicts);
        }
        catch
        {
            cryptography.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Initializes the encrypted baseline if needed, then persists local consent. A false
    /// confirmation is rejected before directory creation, preference writes, or requests.
    /// </summary>
    internal async Task EnableAsync(bool ownerConfirmed, CancellationToken token = default)
    {
        ThrowIfDisposed();
        if (!ownerConfirmed)
            throw new InvalidOperationException("Enabling automatic settings sync requires deliberate local confirmation.");

        await lifecycle.WaitAsync(token);
        try
        {
            ThrowIfDisposed();
            _ = SettingsSyncLocalStorage.PrepareScopedDirectory(syncRoot, scope);
            await EnsureLocalPreferencesBaselineAsync(token).ConfigureAwait(false);
            var local = await new DesktopSettingsSyncStore(desktopSettings.FilePath).LoadAsync(token).ConfigureAwait(false);
            var initialSettings = DesktopSettingsProjection.Extract(local);
            var state = await Outbox.LoadAsync(token).ConfigureAwait(false);

            if (state is null)
            {
                _ = await Initializer.InitializeAsync(initialSettings, ownerConfirmed: true, token).ConfigureAwait(false);
            }
            else if (state.Confirmed.Revision == 0)
            {
                var pendingCreate = state.InFlight;
                if (pendingCreate is null || pendingCreate.Intent.BaseRevision != 0 || pendingCreate.Intent.Deleted)
                    throw new InvalidDataException("An incomplete settings baseline requires explicit recovery.");
                // Retry exactly the original ciphertext and operation if its create response was lost.
                _ = await Initializer.InitializeAsync(pendingCreate.Intent.Values, ownerConfirmed: true, token)
                    .ConfigureAwait(false);
            }

            await runtime.EnableAsync(ownerConfirmed: true, token).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }

    /// <summary>Called by the owning host lifetime; no work starts unless durable consent exists.</summary>
    internal async Task RunAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        if (!Directory.Exists(syncDirectory)) return;
        _ = SettingsSyncLocalStorage.PrepareScopedDirectory(syncRoot, scope);
        var state = await Outbox.LoadAsync(token).ConfigureAwait(false);
        if (state?.SyncConsentEnabled == true)
            await runtime.RunAsync(token).ConfigureAwait(false);
    }

    internal async Task DisableAsync(CancellationToken token = default)
    {
        ThrowIfDisposed();
        if (!Directory.Exists(syncDirectory)) return;
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _ = SettingsSyncLocalStorage.PrepareScopedDirectory(syncRoot, scope);
            await runtime.DisableAsync(token).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }

    internal async Task RecoverRetryCheckpointAsync(bool ownerConfirmed, CancellationToken token = default)
    {
        ThrowIfDisposed();
        if (!ownerConfirmed)
            throw new InvalidOperationException("Retry checkpoint recovery requires deliberate local confirmation.");
        if (!Directory.Exists(syncDirectory))
            throw new InvalidOperationException("Settings sync storage is not initialized.");
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _ = SettingsSyncLocalStorage.PrepareScopedDirectory(syncRoot, scope);
            await runtime.RecoverStorageCheckpointAsync(ownerConfirmed: true, token).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }

    internal async Task<LocalSettingsApplyResult> ResetSharedSettingsAsync(long expectedRevision,
        GlobalSettings reviewedCurrent, bool ownerConfirmed, CancellationToken token = default)
    {
        ThrowIfDisposed();
        if (!ownerConfirmed)
            throw new InvalidOperationException("Reset requires deliberate local confirmation.");
        if (!Directory.Exists(syncDirectory))
            throw new InvalidOperationException("Settings sync storage is not initialized.");
        await lifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            _ = SettingsSyncLocalStorage.PrepareScopedDirectory(syncRoot, scope);
            if ((await Outbox.LoadAsync(token).ConfigureAwait(false))?.SyncConsentEnabled != true)
                throw new SettingsResetConflictException();
            return await Edits.ResetSharedAsync(expectedRevision, reviewedCurrent, ownerConfirmed: true,
                new LocalSettingsOverrides(), token).ConfigureAwait(false);
        }
        finally { lifecycle.Release(); }
    }

    internal bool NotifyConnectivityRestored()
    {
        ThrowIfDisposed();
        return runtime.NotifyConnectivityRestored();
    }

    private async Task EnsureLocalPreferencesBaselineAsync(CancellationToken token)
    {
        await using var lease = await DesktopSettingsFileLease.AcquireAsync(desktopSettings.FilePath, token)
            .ConfigureAwait(false);
        var existing = await lease.ReadAsync(token).ConfigureAwait(false);
        if (existing is not null) return; // Existing content, including corruption, is never replaced here.
        var json = JsonSerializer.SerializeToUtf8Bytes(DesktopSettings.Default,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await lease.ReplaceAsync(json, token).ConfigureAwait(false);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            await runtime.DisposeAsync().ConfigureAwait(false);
            cryptography.Dispose();
            deviceVault.Dispose();
        }
        finally
        {
            lifecycle.Release();
            lifecycle.Dispose();
        }
    }
}
