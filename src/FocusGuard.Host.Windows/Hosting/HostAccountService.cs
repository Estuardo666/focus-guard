using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusGuard.Client.Preferences;
using FocusGuard.Devices;
using FocusGuard.Identity;
using FocusGuard.Sync;

namespace FocusGuard.Host.Windows.Hosting;

internal enum HostAccountAction { Get, Login, CancelLogin, Logout, OpenSecurity, LinkDevice, RequestPairing, RefreshPairings, CompletePairing, ApprovePairing, ShareSettings, ImportSettings, ExportPortableSettings, ImportPortableSettings, EnableSettingsSync, DisableSettingsSync, UpdateLocalSettingsOverrides, ResetSettingsSync, ResolveSettingsSyncConflict, ResolveSettingsSyncResetConflict, RecoverSettingsSyncCheckpoint }
internal enum HostSettingsSyncConflictField { Theme, Language, Duration }
internal enum HostSettingsSyncConflictSource { ThisDevice, OtherDevices }
internal enum HostSettingsSyncResetSource { ThisDeviceReset, OtherDevices }
internal sealed record HostSettingsSyncConflictChoice(HostSettingsSyncConflictField Field, HostSettingsSyncConflictSource Source);
internal sealed record HostSettingsSyncConflictResolution(long Revision, HostSettingsSyncConflictChoice[] Choices);
internal sealed record HostSettingsSyncResetResolution(long Revision, HostSettingsSyncResetSource Choice);
internal sealed record HostSettingsSyncResetPreview(long Revision, GlobalSettings Current, GlobalSettings ResetTo);
internal sealed record HostSettingsSyncConflictReview(long Revision, string[] Fields, GlobalSettings Current,
    GlobalSettings ThisDevice, bool ResetConflict);
internal sealed record PortableSettingsFileSelection(string Path, SettingsFields Fields);
internal sealed record HostSettingsLocalOverrideSummary(bool Theme, bool Language, bool Duration);

internal sealed record HostPendingPairingSummary(string PairingId, string DeviceId, string Platform,
    string ClientVersion, string Fingerprint, string SigningPublicKey, string EncryptionPublicKey,
    DateTimeOffset ExpiresAtUtc);
internal sealed record HostPairingSummary(string PairingId, string DeviceId, string Platform,
    string ClientVersion, string Fingerprint, DateTimeOffset ExpiresAtUtc);
internal sealed record EncryptedDocumentResult(long Revision, EncryptedContentDocument Document);
internal sealed record EncryptedDocumentWriteResult(string State, long Revision, string EntityId);

internal sealed record HostAccountSnapshot(bool SignedIn, bool SignInPending, bool SignOutPending,
    bool CheckingSession, bool CheckingDevice, string? DisplayName, string? Email, bool EmailVerified,
    bool DeviceLinked, bool DeviceLinkPending, string? Error, bool? RemoteRevoked,
    bool PairingBusy = false, string? PairingId = null, string? PairingFingerprint = null,
    HostPairingSummary[]? PendingPairings = null, bool AccountHasLinkedDevice = false,
    bool SettingsTransferBusy = false, string? SettingsTransferStatus = null,
    bool SettingsSyncBusy = false, bool SettingsSyncEnabled = false, string? SettingsSyncStatus = null,
    HostSettingsSyncConflictReview? SettingsSyncConflict = null, bool SettingsSyncRecoveryAvailable = false,
    HostSettingsLocalOverrideSummary? SettingsSyncLocalOverrides = null);

internal interface IHostAccountService : IDisposable
{
    Task<HostAccountSnapshot> ExecuteAsync(HostAccountAction action, CancellationToken cancellationToken,
        string? pairingId = null, string? fingerprint = null,
        HostSettingsSyncConflictResolution? settingsSyncResolution = null,
        HostSettingsSyncResetResolution? settingsSyncResetResolution = null);
    void SetRecoveryKitPresenter(Func<string, CancellationToken, Task<bool>> presenter);
    void SetPairingApprovalPresenter(Func<HostPendingPairingSummary, CancellationToken, Task<bool>> presenter);
    void SetSettingsTransferPresenters(
        Func<DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>> sharePresenter,
        Func<SharedDesktopSettings, DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>> importPresenter);
    void SetPortableSettingsPresenters(
        Func<DesktopSettings, CancellationToken, Task<PortableSettingsFileSelection?>> exportPresenter,
        Func<CancellationToken, Task<string?>> importFilePicker,
        Func<SettingsImportPreview, CancellationToken, Task<SettingsFields?>> importConfirmationPresenter);
    void SetSettingsSyncConsentPresenter(Func<CancellationToken, Task<bool>> presenter);
    void SetSettingsLocalOverridesPresenter(Func<SettingsOutbox, CancellationToken, Task<LocalSettingsOverrides?>> presenter) { }
    void SetSettingsSyncResetPresenter(Func<HostSettingsSyncResetPreview, CancellationToken, Task<bool>> presenter);
    void SetSettingsSyncRecoveryPresenter(Func<CancellationToken, Task<bool>> presenter);
    bool NotifySettingsSyncConnectivityRestored();
    Task<bool> TrySyncSharedSettingsEditAsync(DesktopSettings previous, DesktopSettings proposed,
        CancellationToken cancellationToken);
    Task<bool> TryImportReviewedSettingsAsync(SettingsImportPreview preview, CancellationToken cancellationToken);
}

/// <summary>Identity only; a signed-in account never implies a linked device or permission to control one.</summary>
internal sealed class HostAccountService(
    IdentitySession identity,
    UserTokenVault vault,
    IdentityConfiguration configuration,
    string? deviceVaultRoot = null,
    DesktopSettingsStore? settingsStore = null,
    Func<UserTokenVault, CancellationToken, Task<bool>>? restoreSessionAsync = null,
    string? settingsSyncTestRoot = null) : IHostAccountService
{
    private sealed record AccountCloudContext(bool DeviceLinked, string? AccountId, string? GenerationId, long? MembershipRevision);
    private sealed record BootstrapChallenge(string ChallengeId, string AccountId, string GenerationId,
        string DeviceId, string Nonce, DateTimeOffset ExpiresAt, string Purpose);
    private sealed record BootstrapAccountResult(string AccountId, long Revision, string GenerationId, bool DeviceLinked);
    internal sealed record MembershipSnapshot(MembershipDocument Membership, string MembershipDigest, SignedKeyEnvelope[] KeyEnvelopes)
    {
        public MembershipDocument[]? MembershipChain { get; init; } = [];
    }
    private sealed record RenewDeviceSessionResponse(DateTimeOffset ExpiresAtUtc, MembershipSnapshot Snapshot);
    private sealed record BootstrapPairingResponse(string PairingId, string DeviceId, DateTimeOffset ExpiresAtUtc, string State);
    private sealed record BootstrapPairingRequest(BootstrapPairingProof Proof, string ProofSignature);
    private sealed record PairingStateResponse(string PairingId, string DeviceId, string State, DateTimeOffset ExpiresAtUtc,
        string Fingerprint, string MembershipDigest, MembershipDocument? Membership, SignedKeyEnvelope[]? KeyEnvelopes);
    private sealed record PairingApprovalResult(string PairingId, string State, long MembershipRevision, string MembershipDigest);
    private sealed record ConfirmPairingResult(string PairingId, string State, DateTimeOffset ExpiresAtUtc);
    private sealed record SharedSettingsDocument(long Revision, SharedDesktopSettings Settings);

    private readonly object sync = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly SemaphoreSlim settingsSyncLifecycle = new(1, 1);
    private readonly ExternalSystemBrowser browser = new();
    private readonly Uri accountPortal = new(configuration.Authority.AbsoluteUri.TrimEnd('/') + "/account/");
    private readonly string deviceVaultDirectory = Path.GetFullPath(deviceVaultRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "devices"));
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web);
    private const string SharedSettingsEntityId = "baf56f37-89a7-4a98-9a1e-e87bb3504a91";
    private const string SharedSettingsKind = "preferences.initial";
    private readonly DesktopSettingsStore? settingsStore = settingsStore;
    private readonly string? testSettingsSyncRoot = settingsSyncTestRoot;
    private readonly Func<UserTokenVault, CancellationToken, Task<bool>> restoreSession = restoreSessionAsync ?? identity.RestoreAsync;
    private CancellationTokenSource? loginCancellation;
    private Task? loginTask;
    private Task? logoutTask;
    private Task? restoreTask;
    private Task? deviceCheckTask;
    private Task? deviceLinkTask;
    private Task? pairingTask;
    private Task? settingsTransferTask;
    private Task? settingsSyncTask;
    private Task? settingsSyncRunnerTask;
    private HostSettingsSyncSession? settingsSyncSession;
    private Func<string, CancellationToken, Task<bool>>? recoveryKitPresenter;
    private Func<HostPendingPairingSummary, CancellationToken, Task<bool>>? pairingApprovalPresenter;
    private Func<DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>>? settingsSharePresenter;
    private Func<SharedDesktopSettings, DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>>? settingsImportPresenter;
    private Func<DesktopSettings, CancellationToken, Task<PortableSettingsFileSelection?>>? portableSettingsExportPresenter;
    private Func<CancellationToken, Task<string?>>? portableSettingsImportFilePicker;
    private Func<SettingsImportPreview, CancellationToken, Task<SettingsFields?>>? portableSettingsImportConfirmationPresenter;
    private Func<CancellationToken, Task<bool>>? settingsSyncConsentPresenter;
    private Func<SettingsOutbox, CancellationToken, Task<LocalSettingsOverrides?>>? settingsSyncLocalOverridesPresenter;
    private Func<HostSettingsSyncResetPreview, CancellationToken, Task<bool>>? settingsSyncResetPresenter;
    private Func<CancellationToken, Task<bool>>? settingsSyncRecoveryPresenter;
    private bool restoreStarted;
    private bool checkingSession;
    private bool checkingDevice;
    private bool deviceLinked;
    private bool accountHasLinkedDevice;
    private bool settingsTransferBusy;
    private string? settingsTransferStatus;
    private bool settingsSyncBusy;
    private bool settingsSyncEnabled;
    private bool settingsSyncRecoveryAvailable;
    private string? settingsSyncStatus;
    private HostSettingsSyncConflictReview? settingsSyncConflictReview;
    private HostSettingsLocalOverrideSummary? settingsSyncLocalOverrideSummary;
    private bool deviceLinkPending;
    private bool pairingBusy;
    private string? pendingPairingId;
    private string? pendingPairingFingerprint;
    private HostPendingPairingSummary[] pendingPairings = [];
    private bool signInPending;
    private bool signOutPending;
    private bool disposed;
    private string? error;
    private bool? remoteRevoked;
    private DateTimeOffset retryRestoreAfterUtc;
    private DateTimeOffset nextSessionCheckUtc;
    private DateTimeOffset nextDeviceCheckUtc;

    public Task<HostAccountSnapshot> ExecuteAsync(HostAccountAction action, CancellationToken cancellationToken,
        string? pairingId = null, string? fingerprint = null,
        HostSettingsSyncConflictResolution? settingsSyncResolution = null,
        HostSettingsSyncResetResolution? settingsSyncResetResolution = null) => action switch
        {
            HostAccountAction.Get => Task.FromResult(GetSnapshot()),
            HostAccountAction.Login => Task.FromResult(StartLogin()),
            HostAccountAction.CancelLogin => Task.FromResult(CancelLogin()),
            HostAccountAction.Logout => Task.FromResult(StartLogout()),
            HostAccountAction.OpenSecurity => OpenSecurityAsync(cancellationToken),
            HostAccountAction.LinkDevice => Task.FromResult(StartDeviceLink()),
            HostAccountAction.RequestPairing => Task.FromResult(StartPairingOperation(RequestDevicePairingAsync)),
            HostAccountAction.RefreshPairings => Task.FromResult(StartPairingRefresh()),
            HostAccountAction.CompletePairing => Task.FromResult(StartCompletePairing()),
            HostAccountAction.ApprovePairing => Task.FromResult(StartApprovePairing(pairingId, fingerprint)),
            HostAccountAction.ShareSettings => Task.FromResult(StartSettingsTransfer(share: true)),
            HostAccountAction.ImportSettings => Task.FromResult(StartSettingsTransfer(share: false)),
            HostAccountAction.ExportPortableSettings => Task.FromResult(StartPortableSettingsTransfer(export: true)),
            HostAccountAction.ImportPortableSettings => Task.FromResult(StartPortableSettingsTransfer(export: false)),
            HostAccountAction.EnableSettingsSync => Task.FromResult(StartSettingsSync(enable: true)),
            HostAccountAction.DisableSettingsSync => Task.FromResult(StartSettingsSync(enable: false)),
            HostAccountAction.UpdateLocalSettingsOverrides => Task.FromResult(StartSettingsLocalOverrides()),
            HostAccountAction.ResetSettingsSync => Task.FromResult(StartSettingsSyncReset()),
            HostAccountAction.ResolveSettingsSyncConflict => Task.FromResult(StartResolveSettingsSyncConflict(settingsSyncResolution)),
            HostAccountAction.ResolveSettingsSyncResetConflict => Task.FromResult(StartResolveSettingsSyncResetConflict(settingsSyncResetResolution)),
            HostAccountAction.RecoverSettingsSyncCheckpoint => Task.FromResult(StartSettingsSyncRecovery()),
            _ => Task.FromResult(Snapshot(errorOverride: "AccountActionUnavailable"))
        };

    public void SetRecoveryKitPresenter(Func<string, CancellationToken, Task<bool>> presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        lock (sync) recoveryKitPresenter = presenter;
    }

    public void SetPairingApprovalPresenter(Func<HostPendingPairingSummary, CancellationToken, Task<bool>> presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        lock (sync) pairingApprovalPresenter = presenter;
    }

    public void SetSettingsTransferPresenters(
        Func<DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>> sharePresenter,
        Func<SharedDesktopSettings, DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>> importPresenter)
    {
        ArgumentNullException.ThrowIfNull(sharePresenter);
        ArgumentNullException.ThrowIfNull(importPresenter);
        lock (sync)
        {
            settingsSharePresenter = sharePresenter;
            settingsImportPresenter = importPresenter;
        }
    }

    public void SetPortableSettingsPresenters(
        Func<DesktopSettings, CancellationToken, Task<PortableSettingsFileSelection?>> exportPresenter,
        Func<CancellationToken, Task<string?>> importFilePicker,
        Func<SettingsImportPreview, CancellationToken, Task<SettingsFields?>> importConfirmationPresenter)
    {
        ArgumentNullException.ThrowIfNull(exportPresenter);
        ArgumentNullException.ThrowIfNull(importFilePicker);
        ArgumentNullException.ThrowIfNull(importConfirmationPresenter);
        lock (sync)
        {
            portableSettingsExportPresenter = exportPresenter;
            portableSettingsImportFilePicker = importFilePicker;
            portableSettingsImportConfirmationPresenter = importConfirmationPresenter;
        }
    }

    public void SetSettingsSyncConsentPresenter(Func<CancellationToken, Task<bool>> presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        lock (sync) settingsSyncConsentPresenter = presenter;
    }

    public void SetSettingsLocalOverridesPresenter(
        Func<SettingsOutbox, CancellationToken, Task<LocalSettingsOverrides?>> presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        lock (sync) settingsSyncLocalOverridesPresenter = presenter;
    }

    public void SetSettingsSyncResetPresenter(
        Func<HostSettingsSyncResetPreview, CancellationToken, Task<bool>> presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        lock (sync) settingsSyncResetPresenter = presenter;
    }

    public void SetSettingsSyncRecoveryPresenter(Func<CancellationToken, Task<bool>> presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);
        lock (sync) settingsSyncRecoveryPresenter = presenter;
    }

    public bool NotifySettingsSyncConnectivityRestored()
    {
        HostSettingsSyncSession? session;
        lock (sync)
        {
            if (disposed || !settingsSyncEnabled) return false;
            session = settingsSyncSession;
        }
        if (session is null) return false;
        try { return session.NotifyConnectivityRestored(); }
        catch (ObjectDisposedException) { return false; }
    }

    private HostAccountSnapshot GetSnapshot()
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            var now = DateTimeOffset.UtcNow;
            var retryAllowed = now >= retryRestoreAfterUtc;
            var sessionRetryPending = retryRestoreAfterUtc != DateTimeOffset.MinValue;
            var shouldRestore = vault.Exists && retryAllowed && !checkingSession && !signInPending && !signOutPending
                && (!restoreStarted || sessionRetryPending || identity.State.SignedIn && now >= nextSessionCheckUtc);
            if (shouldRestore)
            {
                restoreStarted = true;
                checkingSession = true;
                error = null;
                restoreTask = Task.Run(RestoreAsync);
            }
            else if (!vault.Exists)
            {
                restoreStarted = true;
            }
            if (identity.State.SignedIn && !signInPending && !checkingDevice && !deviceLinkPending && !pairingBusy && !settingsTransferBusy && !settingsSyncBusy && !signOutPending &&
                now >= nextDeviceCheckUtc)
            {
                checkingDevice = true;
                deviceCheckTask = Task.Run(CheckDeviceLinkAsync);
            }
            return Snapshot();
        }
    }

    private async Task RestoreAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var restored = await restoreSession(vault, deadline.Token).ConfigureAwait(false);
            lock (sync)
            {
                if (!restored && vault.Exists)
                {
                    error = "AccountConnectionUnavailable";
                    retryRestoreAfterUtc = DateTimeOffset.UtcNow.AddSeconds(30);
                }
                else
                {
                    error = null;
                    retryRestoreAfterUtc = DateTimeOffset.MinValue;
                    if (restored)
                    {
                        nextSessionCheckUtc = DateTimeOffset.UtcNow.AddMinutes(2);
                        nextDeviceCheckUtc = DateTimeOffset.MinValue;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            lock (sync)
            {
                error = "AccountConnectionUnavailable";
                retryRestoreAfterUtc = DateTimeOffset.UtcNow.AddSeconds(30);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
            or InvalidOperationException or IOException or InvalidDataException or CryptographicException
            or UnauthorizedAccessException or System.Security.SecurityException)
        {
            lock (sync)
            {
                error = "AccountConnectionUnavailable";
                retryRestoreAfterUtc = DateTimeOffset.UtcNow.AddSeconds(30);
            }
        }
        finally
        {
            lock (sync) checkingSession = false;
        }
    }

    private HostAccountSnapshot StartLogin()
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (signInPending || signOutPending) return Snapshot(errorOverride: "AccountActionInProgress");
            error = null;
            remoteRevoked = null;
            retryRestoreAfterUtc = DateTimeOffset.MinValue;
            settingsTransferStatus = null;
            deviceLinked = false;
            accountHasLinkedDevice = false;
            pendingPairingId = null;
            pendingPairingFingerprint = null;
            pendingPairings = [];
            settingsTransferStatus = null;
            nextDeviceCheckUtc = DateTimeOffset.MinValue;
            signInPending = true;
            loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
            var current = loginCancellation;
            loginTask = Task.Run(() => RunLoginAsync(current));
            return Snapshot();
        }
    }

    private async Task RunLoginAsync(CancellationTokenSource cancellation)
    {
        try
        {
            var accepted = await SystemBrowserLogin.RunAsync(identity, configuration.RedirectUri, browser, cancellation.Token)
                .ConfigureAwait(false);
            if (!accepted) return;
            try { await identity.SaveAsync(vault, cancellation.Token).ConfigureAwait(false); }
            catch
            {
                await identity.LogoutAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            lock (sync) { restoreStarted = true; error = null; nextSessionCheckUtc = DateTimeOffset.UtcNow.AddMinutes(2); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidOperationException
            or IOException or InvalidDataException or CryptographicException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            lock (sync) error = "AccountConnectionUnavailable";
        }
        finally
        {
            lock (sync)
            {
                signInPending = false;
                if (ReferenceEquals(loginCancellation, cancellation))
                {
                    loginCancellation = null;
                    loginTask = null;
                }
            }
            cancellation.Dispose();
        }
    }

    private HostAccountSnapshot CancelLogin()
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            loginCancellation?.Cancel();
            return Snapshot();
        }
    }

    private HostAccountSnapshot StartLogout()
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (signOutPending || pairingBusy || settingsTransferBusy || settingsSyncBusy || deviceLinkPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");
            signOutPending = true;
            error = null;
            retryRestoreAfterUtc = DateTimeOffset.MinValue;
            remoteRevoked = null;
            deviceLinked = false;
            accountHasLinkedDevice = false;
            pendingPairingId = null;
            pendingPairingFingerprint = null;
            pendingPairings = [];
            nextDeviceCheckUtc = DateTimeOffset.MinValue;
            loginCancellation?.Cancel();
            var pendingLogin = loginTask;
            logoutTask = Task.Run(() => RunLogoutAsync(pendingLogin));
            return Snapshot();
        }
    }

    private async Task RunLogoutAsync(Task? pendingLogin)
    {
        try
        {
            if (pendingLogin is not null) await pendingLogin.WaitAsync(shutdown.Token).ConfigureAwait(false);
            await DisposeSettingsSyncSessionAsync(shutdown.Token).ConfigureAwait(false);
            var revoked = await identity.LogoutAsync(shutdown.Token).ConfigureAwait(false);
            lock (sync)
            {
                remoteRevoked = revoked;
                error = revoked ? null : "RemoteLogoutUnconfirmed";
                restoreStarted = true;
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidOperationException
            or IOException or InvalidDataException or CryptographicException or UnauthorizedAccessException
            or System.Security.SecurityException)
        {
            lock (sync) { remoteRevoked = false; error = "RemoteLogoutUnconfirmed"; }
        }
        finally
        {
            lock (sync) signOutPending = false;
        }
    }

    private async Task<HostAccountSnapshot> OpenSecurityAsync(CancellationToken cancellationToken)
    {
        if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
        try
        {
            await browser.LaunchAsync(accountPortal, cancellationToken).ConfigureAwait(false);
            return Snapshot();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception
            or OperationCanceledException)
        {
            return Snapshot(errorOverride: "AccountPortalUnavailable");
        }
    }

    private HostAccountSnapshot Snapshot(string? errorOverride = null)
    {
        var state = identity.State;
        lock (sync)
        {
            return new(state.SignedIn && !signInPending, signInPending, signOutPending, checkingSession, checkingDevice,
                state.DisplayName, state.Email, state.EmailVerified, deviceLinked, deviceLinkPending,
                errorOverride ?? error, remoteRevoked, pairingBusy,
                pendingPairingId, pendingPairingFingerprint,
                pendingPairings.Select(pairing => new HostPairingSummary(pairing.PairingId, pairing.DeviceId,
                    pairing.Platform, pairing.ClientVersion, pairing.Fingerprint, pairing.ExpiresAtUtc)).ToArray(), accountHasLinkedDevice,
                settingsTransferBusy, settingsTransferStatus, settingsSyncBusy, settingsSyncEnabled, settingsSyncStatus,
                 settingsSyncConflictReview, settingsSyncRecoveryAvailable, settingsSyncLocalOverrideSummary);
        }
    }

    private HostAccountSnapshot StartDeviceLink()
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
            if (!identity.State.EmailVerified) return Snapshot(errorOverride: "VerifiedEmailRequired");
            if (deviceLinkPending || pairingBusy || settingsTransferBusy || settingsSyncBusy || signInPending || signOutPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");
            error = null;
            deviceLinkPending = true;
            deviceLinkTask = Task.Run(LinkDeviceAsync);
            return Snapshot();
        }
    }

    private async Task CheckDeviceLinkAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var binding = identity.AccountBinding;
            if (binding is null) return;
            var cloud = await GetCloudAccountContextAsync(deadline.Token).ConfigureAwait(false);
            var localVault = GetDeviceVault(binding);
            lock (sync) accountHasLinkedDevice = cloud.DeviceLinked;
            if (!cloud.DeviceLinked)
            {
                await DisposeSettingsSyncSessionAsync(deadline.Token).ConfigureAwait(false);
                lock (sync) { deviceLinked = false; error = null; }
                return;
            }
            if (cloud.AccountId is null || cloud.GenerationId is null || cloud.MembershipRevision is null)
                throw new DeviceEnrollmentException("DeviceAccountUnavailable");
            if (!localVault.Exists)
            {
                await DisposeSettingsSyncSessionAsync(deadline.Token).ConfigureAwait(false);
                lock (sync) { deviceLinked = false; error = "DevicePairingRequired"; }
                return;
            }
            using var loaded = await localVault.LoadForAccountAsync(configuration.Authority.AbsoluteUri.TrimEnd('/'),
                binding, deadline.Token).ConfigureAwait(false);
            if (loaded is null || loaded.Context.AccountId != cloud.AccountId || loaded.Context.GenerationId != cloud.GenerationId)
                throw new DeviceEnrollmentException("DeviceVaultMismatch");
            if (loaded.Material.MembershipRevision != cloud.MembershipRevision)
            {
                await DisposeSettingsSyncSessionAsync(deadline.Token).ConfigureAwait(false);
                lock (sync) { deviceLinked = false; error = "DeviceMembershipUpdateRequired"; }
                return;
            }
            var membership = await EnsureDeviceSessionAsync(localVault, loaded, deadline.Token).ConfigureAwait(false);
            lock (sync) { deviceLinked = true; error = null; }
            await ResumeSettingsSyncIfConsentedAsync(localVault, loaded.Context, loaded.DeviceId,
                membership, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            lock (sync) { deviceLinked = false; error = "DeviceConnectionUnavailable"; }
        }
        catch (DeviceEnrollmentException exception)
        {
            lock (sync) { deviceLinked = false; error = exception.Code; }
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidOperationException
            or IOException or InvalidDataException or CryptographicException or UnauthorizedAccessException
            or System.Security.SecurityException or JsonException)
        {
            lock (sync) { deviceLinked = false; error = "DeviceConnectionUnavailable"; }
        }
        finally
        {
            lock (sync)
            {
                checkingDevice = false;
                nextDeviceCheckUtc = DateTimeOffset.UtcNow.Add(error is null ? TimeSpan.FromMinutes(2) : TimeSpan.FromSeconds(30));
            }
        }
    }

    private async Task LinkDeviceAsync()
    {
        DeviceIdentity? createdIdentity = null;
        RecoveryKit? createdRecoveryKit = null;
        RestoredDeviceMaterial? resumedMaterial = null;
        byte[]? createdContentKey = null;
        try
        {
            var binding = identity.AccountBinding ?? throw new DeviceEnrollmentException("SignInRequired");
            var issuer = configuration.Authority.AbsoluteUri.TrimEnd('/');
            var cloud = await GetCloudAccountContextAsync(shutdown.Token).ConfigureAwait(false);
            var localVault = GetDeviceVault(binding);
            var resume = localVault.Exists;
            if (cloud.DeviceLinked)
            {
                if (!resume)
                {
                    lock (sync) { deviceLinked = false; error = "DevicePairingRequired"; }
                    return;
                }
                using var enrolled = await localVault.LoadForAccountAsync(issuer, binding, shutdown.Token).ConfigureAwait(false)
                    ?? throw new DeviceEnrollmentException("DeviceVaultMismatch");
                if (enrolled.Context.AccountId != cloud.AccountId || enrolled.Context.GenerationId != cloud.GenerationId)
                    throw new DeviceEnrollmentException("DeviceVaultMismatch");
                if (enrolled.Material.MembershipRevision == cloud.MembershipRevision)
                {
                    await EnsureDeviceSessionAsync(localVault, enrolled, shutdown.Token).ConfigureAwait(false);
                    lock (sync) { deviceLinked = true; error = null; }
                    return;
                }
                lock (sync) { deviceLinked = false; error = "DeviceMembershipUpdateRequired"; }
                return;
            }

            DeviceAccountContext accountContext;
            DeviceIdentity device;
            RecoveryKit recovery;
            ReadOnlyMemory<byte> contentKey;
            string deviceSession;
            if (resume)
            {
                var loaded = await localVault.LoadForAccountAsync(issuer, binding, shutdown.Token).ConfigureAwait(false)
                    ?? throw new DeviceEnrollmentException("DeviceVaultMismatch");
                resumedMaterial = loaded.Material;
                accountContext = loaded.Context;
                device = resumedMaterial.Identity;
                recovery = resumedMaterial.RecoveryKit
                    ?? throw new DeviceEnrollmentException("DeviceRecoveryKitUnavailable");
                contentKey = resumedMaterial.AccountContentKey;
                deviceSession = resumedMaterial.DeviceSession
                    ?? throw new DeviceEnrollmentException("DeviceSessionRecoveryRequired");
            }
            else
            {
                var accountId = Guid.NewGuid().ToString("D");
                var generationId = Guid.NewGuid().ToString("D");
                accountContext = new DeviceAccountContext(issuer, binding, accountId, generationId);
                accountContext.Validate();
                createdIdentity = DeviceIdentity.Create();
                createdRecoveryKit = RecoveryKit.Generate();
                createdContentKey = RandomNumberGenerator.GetBytes(32);
                var sessionBytes = RandomNumberGenerator.GetBytes(32);
                try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
                finally { CryptographicOperations.ZeroMemory(sessionBytes); }
                device = createdIdentity;
                recovery = createdRecoveryKit;
                contentKey = createdContentKey;
            }

            Func<string, CancellationToken, Task<bool>>? presenter;
            lock (sync) presenter = recoveryKitPresenter;
            if (presenter is null || !await presenter(recovery.Export(), shutdown.Token).ConfigureAwait(false))
                throw new DeviceEnrollmentException("RecoveryKitNotConfirmed");

            var membership = CreateInitialMembership(accountContext, device, recovery);
            var digest = MembershipProtocol.Digest(membership);
            if (!resume)
                await localVault.SaveAsync(device, recovery, accountContext, contentKey, 1, digest, shutdown.Token,
                    deviceSession).ConfigureAwait(false);
            await RegisterFirstDeviceAsync(accountContext, device, recovery, contentKey, membership,
                deviceSession, shutdown.Token).ConfigureAwait(false);
            lock (sync)
            {
                deviceLinked = true;
                error = null;
                nextDeviceCheckUtc = DateTimeOffset.UtcNow.AddMinutes(2);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (DeviceEnrollmentException exception)
        {
            lock (sync) { deviceLinked = false; error = exception.Code; }
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidOperationException
            or IOException or InvalidDataException or CryptographicException or UnauthorizedAccessException
            or System.Security.SecurityException or JsonException or ArgumentException)
        {
            lock (sync) { deviceLinked = false; error = "DeviceLinkUnavailable"; }
        }
        finally
        {
            if (createdIdentity is not null) createdIdentity.Dispose();
            if (createdRecoveryKit is not null) createdRecoveryKit.Dispose();
            resumedMaterial?.Dispose();
            if (createdContentKey is not null) CryptographicOperations.ZeroMemory(createdContentKey);
            lock (sync)
            {
                deviceLinkPending = false;
                nextDeviceCheckUtc = DateTimeOffset.UtcNow.AddMinutes(2);
            }
        }
    }

    private WindowsDeviceVault GetDeviceVault(string accountBinding)
    {
        _ = DeviceEncoding.Decode(accountBinding, 32);
        return new WindowsDeviceVault(Path.Combine(deviceVaultDirectory, accountBinding + ".dpapi"));
    }

    private WindowsDeviceVault GetPendingPairingVault(string accountBinding)
    {
        _ = DeviceEncoding.Decode(accountBinding, 32);
        return new WindowsDeviceVault(Path.Combine(deviceVaultDirectory, accountBinding + ".pairing.dpapi"));
    }

    private HostAccountSnapshot StartPairingOperation(Func<Task> operation)
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
            if (!identity.State.EmailVerified) return Snapshot(errorOverride: "VerifiedEmailRequired");
            if (pairingBusy || settingsTransferBusy || settingsSyncBusy || deviceLinkPending || signOutPending || signInPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");
            error = null;
            pairingBusy = true;
            pairingTask = Task.Run(async () =>
            {
                try { await operation().ConfigureAwait(false); }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
                catch (DeviceEnrollmentException exception) { lock (sync) error = exception.Code; }
                catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidOperationException
                    or IOException or InvalidDataException or CryptographicException or UnauthorizedAccessException
                    or System.Security.SecurityException or JsonException or ArgumentException)
                { lock (sync) error = "DevicePairingUnavailable"; }
                finally
                {
                    lock (sync)
                    {
                        pairingBusy = false;
                        nextDeviceCheckUtc = DateTimeOffset.UtcNow.AddSeconds(30);
                    }
                }
            });
            return Snapshot();
        }
    }

    private HostAccountSnapshot StartPairingRefresh() => StartPairingOperation(RefreshPendingPairingsAsync);
    private HostAccountSnapshot StartCompletePairing() => StartPairingOperation(CompleteDevicePairingAsync);

    private HostAccountSnapshot StartApprovePairing(string? pairingId, string? fingerprint)
    {
        if (pairingId is null || fingerprint is null) return Snapshot(errorOverride: "PairingRequestInvalid");
        return StartPairingOperation(() => ApproveDevicePairingAsync(pairingId, fingerprint));
    }

    private HostAccountSnapshot StartSettingsTransfer(bool share)
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
            if (!identity.State.EmailVerified) return Snapshot(errorOverride: "VerifiedEmailRequired");
            if (!deviceLinked) return Snapshot(errorOverride: "DevicePairingRequired");
            if (settingsTransferBusy || settingsSyncBusy || pairingBusy || deviceLinkPending || signInPending || signOutPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");
            error = null;
            settingsTransferStatus = null;
            settingsTransferBusy = true;
            settingsTransferTask = Task.Run(() => RunSettingsTransferAsync(share));
            return Snapshot();
        }
    }

    private async Task RunSettingsTransferAsync(bool share)
    {
        try
        {
            if (share) await ShareSelectedSettingsAsync().ConfigureAwait(false);
            else await ImportSelectedSettingsAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (DeviceEnrollmentException exception)
        {
            lock (sync) { error = exception.Code; settingsTransferStatus = null; }
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or InvalidOperationException
            or IOException or InvalidDataException or CryptographicException or UnauthorizedAccessException
            or System.Security.SecurityException or JsonException or ArgumentException)
        {
            lock (sync) { error = "AccountSettingsUnavailable"; settingsTransferStatus = null; }
        }
        finally
        {
            lock (sync)
            {
                settingsTransferBusy = false;
                nextDeviceCheckUtc = DateTimeOffset.UtcNow.AddSeconds(30);
            }
        }
    }

    private HostAccountSnapshot StartPortableSettingsTransfer(bool export)
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (settingsTransferBusy || settingsSyncBusy || pairingBusy || deviceLinkPending || signInPending || signOutPending)
                return Snapshot(errorOverride: "AccountActionInProgress");
            error = null;
            settingsTransferStatus = null;
            settingsTransferBusy = true;
            settingsTransferTask = Task.Run(() => RunPortableSettingsTransferAsync(export));
            return Snapshot();
        }
    }

    private async Task RunPortableSettingsTransferAsync(bool export)
    {
        try
        {
            var store = settingsStore ?? throw new InvalidOperationException("AccountSettingsUnavailable");
            var token = shutdown.Token;
            if (export)
            {
                Func<DesktopSettings, CancellationToken, Task<PortableSettingsFileSelection?>>? presenter;
                lock (sync) presenter = portableSettingsExportPresenter;
                if (presenter is null) throw new InvalidOperationException("AccountSettingsUnavailable");
                var exportCurrent = await LoadPortableSettingsBaselineAsync(store, token).ConfigureAwait(false);
                var exportSelection = await presenter(exportCurrent, token).ConfigureAwait(false);
                if (exportSelection is null || exportSelection.Fields == SettingsFields.None)
                {
                    lock (sync) settingsTransferStatus = "Cancelled";
                    return;
                }
                var portable = ToGlobalSettings(exportCurrent);
                var exportBytes = SettingsExport.Create(portable, ownerConfirmed: true);
                await File.WriteAllBytesAsync(exportSelection.Path, exportBytes, token).ConfigureAwait(false);
                lock (sync) { settingsTransferStatus = "PortableExported"; error = null; }
                return;
            }

            Func<CancellationToken, Task<string?>>? picker;
            Func<SettingsImportPreview, CancellationToken, Task<SettingsFields?>>? confirmation;
            lock (sync) { picker = portableSettingsImportFilePicker; confirmation = portableSettingsImportConfirmationPresenter; }
            if (picker is null || confirmation is null) throw new InvalidOperationException("AccountSettingsUnavailable");
            var path = await picker(token).ConfigureAwait(false);
            if (path is null)
            {
                lock (sync) settingsTransferStatus = "Cancelled";
                return;
            }
            byte[] bytes;
            await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                if (stream.Length is < 2 or > 8192) throw new InvalidDataException("Settings export size is invalid.");
                bytes = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                if (stream.ReadByte() != -1) throw new InvalidDataException("Settings export changed while reading.");
            }

            var baselineDesktop = await LoadPortableSettingsBaselineAsync(store, token).ConfigureAwait(false);
            var baseline = ToGlobalSettings(baselineDesktop);
            const SettingsFields all = SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration;
            var fullPreview = SettingsExport.Preview(bytes, baseline, all);
            var selection = await confirmation(fullPreview, token).ConfigureAwait(false);
            if (selection is null || selection == SettingsFields.None)
            {
                lock (sync) settingsTransferStatus = "Cancelled";
                return;
            }
            var currentDesktop = await LoadPortableSettingsBaselineAsync(store, token).ConfigureAwait(false);
            var current = ToGlobalSettings(currentDesktop);
            if (current != fullPreview.Baseline)
                throw new InvalidOperationException("Settings changed while the import preview was open.");
            var preview = SettingsExport.Preview(bytes, current, selection.Value);
            var proposed = preview.Confirm(current, ownerConfirmed: true);
            var updated = currentDesktop with
            {
                Theme = selection.Value.HasFlag(SettingsFields.Theme) ? proposed.Theme == "light" ? AppTheme.Light : AppTheme.Dark : currentDesktop.Theme,
                UiLanguage = selection.Value.HasFlag(SettingsFields.Language) ? proposed.UiLanguage : currentDesktop.UiLanguage,
                LastDurationMinutes = selection.Value.HasFlag(SettingsFields.Duration) ? proposed.DefaultDurationMinutes : currentDesktop.LastDurationMinutes
            };
            var synced = await TryImportReviewedSettingsAsync(preview, token).ConfigureAwait(false);
            if (!synced)
            {
                var applied = store.TryApplySharedIfUnchanged(currentDesktop.Theme, currentDesktop.UiLanguage,
                    currentDesktop.LastDurationMinutes,
                    selection.Value.HasFlag(SettingsFields.Theme) ? updated.Theme : null,
                    selection.Value.HasFlag(SettingsFields.Language) ? updated.UiLanguage : null,
                    selection.Value.HasFlag(SettingsFields.Duration) ? updated.LastDurationMinutes : null,
                    out _);
                if (!applied)
                    throw new InvalidOperationException("Settings changed while the import preview was open.");
            }
            lock (sync) { settingsTransferStatus = "PortableImported"; error = null; }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException
            or CryptographicException or UnauthorizedAccessException or System.Security.SecurityException
            or JsonException or ArgumentException or NotSupportedException)
        {
            lock (sync)
            {
                error = exception is InvalidOperationException && exception.Message.Contains("changed while the import preview", StringComparison.Ordinal)
                    ? "PortableSettingsStale"
                    : exception is InvalidDataException or JsonException or ArgumentException
                        ? "PortableSettingsInvalid" : "AccountSettingsUnavailable";
                settingsTransferStatus = null;
            }
        }
        finally
        {
            lock (sync) settingsTransferBusy = false;
        }
    }

    private static GlobalSettings ToGlobalSettings(DesktopSettings settings) => new(
        settings.LastDurationMinutes, settings.Theme == AppTheme.Light ? "light" : "dark", settings.UiLanguage);

    private static Task<DesktopSettings> LoadPortableSettingsBaselineAsync(DesktopSettingsStore store,
        CancellationToken token)
    {
        if (!store.Persists || !File.Exists(store.FilePath)) return Task.FromResult(store.Load());
        return new DesktopSettingsSyncStore(store.FilePath).LoadAsync(token);
    }

    private HostAccountSnapshot StartSettingsSync(bool enable)
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
            if (!identity.State.EmailVerified) return Snapshot(errorOverride: "VerifiedEmailRequired");
            if (!deviceLinked) return Snapshot(errorOverride: "DevicePairingRequired");
            if (settingsSyncBusy || settingsTransferBusy || pairingBusy || deviceLinkPending || signInPending || signOutPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");
            if (!enable && !settingsSyncEnabled)
            {
                settingsSyncStatus = "Disabled";
                return Snapshot();
            }

            error = null;
            settingsSyncBusy = true;
            settingsSyncStatus = enable ? "Enabling" : "Disabling";
            settingsSyncTask = Task.Run(() => RunSettingsSyncOperationAsync(enable));
            return Snapshot();
        }
    }

    private HostAccountSnapshot StartSettingsLocalOverrides()
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
            if (!identity.State.EmailVerified) return Snapshot(errorOverride: "VerifiedEmailRequired");
            if (!deviceLinked) return Snapshot(errorOverride: "DevicePairingRequired");
            if (!settingsSyncEnabled || settingsSyncStatus != "Enabled")
                return Snapshot(errorOverride: "AccountSettingsOverridesUnavailable");
            if (settingsSyncBusy || settingsTransferBusy || pairingBusy || deviceLinkPending || signInPending || signOutPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");

            error = null;
            settingsSyncBusy = true;
            settingsSyncTask = Task.Run(RunSettingsLocalOverridesAsync);
            return Snapshot();
        }
    }

    private async Task RunSettingsLocalOverridesAsync()
    {
        var token = shutdown.Token;
        var entered = false;
        HostSettingsSyncSession? session = null;
        try
        {
            Func<SettingsOutbox, CancellationToken, Task<LocalSettingsOverrides?>>? presenter;
            lock (sync) presenter = settingsSyncLocalOverridesPresenter;
            if (presenter is null) throw new DeviceEnrollmentException("AccountSettingsOverridesUnavailable");

            await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            session = settingsSyncSession ?? throw new DeviceEnrollmentException("AccountSettingsOverridesUnavailable");
            if (!settingsSyncEnabled || !await session.Consent.IsEnabledAsync(token).ConfigureAwait(false))
                throw new DeviceEnrollmentException("AccountSettingsOverridesUnavailable");
            var reviewed = await session.Outbox.LoadAsync(token).ConfigureAwait(false)
                ?? throw new DeviceEnrollmentException("AccountSettingsOverridesUnavailable");
            if (!reviewed.SyncConsentEnabled || reviewed.Conflicts != SettingsFields.None || reviewed.Pending ||
                reviewed.InFlight is not null || reviewed.DraftDeleted)
                throw new DeviceEnrollmentException("AccountSettingsOverridesUnavailable");
            var expectedEffective = reviewed.LocalOverrides.Apply(reviewed.Draft ?? reviewed.Confirmed.EffectiveValues);
            settingsSyncLifecycle.Release();
            entered = false;

            var selected = await presenter(reviewed, token).ConfigureAwait(false);
            if (selected is null) return; // Cancel is intentionally a no-op.
            _ = selected.Apply(GlobalSettings.Default);

            await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            if (!ReferenceEquals(settingsSyncSession, session) || !settingsSyncEnabled ||
                !await session.Consent.IsEnabledAsync(token).ConfigureAwait(false))
                throw new DeviceEnrollmentException("AccountSettingsOverridesUnavailable");

            _ = await session.Edits.UpdateOverridesAsync(reviewed.Confirmed.Revision, reviewed.LocalOverrides,
                selected, token, expectedEffective).ConfigureAwait(false);
            var after = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
            lock (sync)
            {
                settingsSyncEnabled = after?.SyncConsentEnabled == true;
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(after);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(after);
                settingsSyncStatus = SettingsSyncStatusLabel(after);
                error = null;
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (DeviceEnrollmentException exception)
        {
            lock (sync) error = exception.Code;
        }
        catch (Exception exception) when (IsSettingsSyncFailure(exception))
        {
            SettingsOutbox? latest = null;
            if (session is not null)
            {
                try { latest = await session.Outbox.LoadAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
            lock (sync)
            {
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(latest);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(latest);
                settingsSyncStatus = SettingsSyncStatusLabel(latest);
                error = exception is InvalidOperationException or ArgumentException
                    ? "AccountSettingsConflict" : "AccountSettingsUnavailable";
            }
        }
        finally
        {
            if (entered) settingsSyncLifecycle.Release();
            lock (sync) settingsSyncBusy = false;
        }
    }

    private HostAccountSnapshot StartSettingsSyncReset()
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
            if (!identity.State.EmailVerified) return Snapshot(errorOverride: "VerifiedEmailRequired");
            if (!deviceLinked) return Snapshot(errorOverride: "DevicePairingRequired");
            if (!settingsSyncEnabled || settingsSyncStatus != "Enabled")
                return Snapshot(errorOverride: "AccountSettingsResetUnavailable");
            if (settingsSyncBusy || settingsTransferBusy || pairingBusy || deviceLinkPending || signInPending || signOutPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");

            error = null;
            settingsSyncBusy = true;
            settingsSyncStatus = "Resetting";
            settingsSyncTask = Task.Run(RunSettingsSyncResetAsync);
            return Snapshot();
        }
    }

    private async Task RunSettingsSyncResetAsync()
    {
        var token = shutdown.Token;
        HostSettingsSyncSession? session = null;
        var entered = false;
        try
        {
            Func<HostSettingsSyncResetPreview, CancellationToken, Task<bool>>? presenter;
            lock (sync) presenter = settingsSyncResetPresenter;
            if (presenter is null) throw new DeviceEnrollmentException("AccountSettingsResetUnavailable");

            await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            session = settingsSyncSession ?? throw new DeviceEnrollmentException("AccountSettingsResetUnavailable");
            var before = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
            var preview = CreateSettingsSyncResetPreview(before) ??
                throw new DeviceEnrollmentException("AccountSettingsResetUnavailable");
            settingsSyncLifecycle.Release();
            entered = false;

            if (!await presenter(preview, token).ConfigureAwait(false))
            {
                var afterCancel = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
                lock (sync)
                {
                    error = null;
                    settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(afterCancel);
                    settingsSyncConflictReview = CreateSettingsSyncConflictReview(afterCancel);
                    settingsSyncStatus = SettingsSyncStatusLabel(afterCancel);
                }
                return;
            }

            await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            if (!ReferenceEquals(settingsSyncSession, session) || !settingsSyncEnabled)
                throw new DeviceEnrollmentException("AccountSettingsResetUnavailable");
            _ = await session.ResetSharedSettingsAsync(preview.Revision, preview.Current,
                ownerConfirmed: true, token).ConfigureAwait(false);
            var after = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
            lock (sync)
            {
                settingsSyncEnabled = after?.SyncConsentEnabled == true;
                settingsSyncRecoveryAvailable = false;
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(after);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(after);
                settingsSyncStatus = SettingsSyncStatusLabel(after);
            }
            StartSettingsSyncRunner(session);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (SettingsResetConflictException)
        {
            SettingsOutbox? latest = null;
            if (session is not null)
            {
                try { latest = await session.Outbox.LoadAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
            lock (sync)
            {
                error = "AccountSettingsConflict";
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(latest);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(latest);
                settingsSyncStatus = SettingsSyncStatusLabel(latest);
            }
        }
        catch (DeviceEnrollmentException exception)
        {
            SettingsOutbox? latest = null;
            if (session is not null)
            {
                try { latest = await session.Outbox.LoadAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
            lock (sync)
            {
                error = exception.Code;
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(latest);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(latest);
                settingsSyncStatus = SettingsSyncStatusLabel(latest);
            }
        }
        catch (Exception exception) when (IsSettingsSyncFailure(exception))
        {
            lock (sync) { error = "AccountSettingsUnavailable"; settingsSyncStatus = "Attention"; }
        }
        finally
        {
            if (entered) settingsSyncLifecycle.Release();
            lock (sync) settingsSyncBusy = false;
        }
    }

    private HostAccountSnapshot StartSettingsSyncRecovery()
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
            if (!identity.State.EmailVerified) return Snapshot(errorOverride: "VerifiedEmailRequired");
            if (!deviceLinked) return Snapshot(errorOverride: "DevicePairingRequired");
            if (!settingsSyncEnabled || !settingsSyncRecoveryAvailable || settingsSyncStatus != "Attention")
                return Snapshot(errorOverride: "AccountSettingsUnavailable");
            if (settingsSyncBusy || settingsTransferBusy || pairingBusy || deviceLinkPending || signInPending || signOutPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");

            error = null;
            settingsSyncBusy = true;
            settingsSyncStatus = "Recovering";
            settingsSyncTask = Task.Run(RunSettingsSyncRecoveryAsync);
            return Snapshot();
        }
    }

    private async Task RunSettingsSyncRecoveryAsync()
    {
        var token = shutdown.Token;
        var entered = false;
        try
        {
            Func<CancellationToken, Task<bool>>? presenter;
            lock (sync) presenter = settingsSyncRecoveryPresenter;
            if (presenter is null) throw new DeviceEnrollmentException("AccountSettingsUnavailable");
            if (!await presenter(token).ConfigureAwait(false))
            {
                lock (sync) settingsSyncStatus = "Attention";
                return;
            }

            await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            var session = settingsSyncSession ?? throw new DeviceEnrollmentException("AccountSettingsUnavailable");
            var before = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
            if (before?.SyncConsentEnabled != true)
                throw new DeviceEnrollmentException("AccountSettingsUnavailable");

            await session.RecoverRetryCheckpointAsync(ownerConfirmed: true, token).ConfigureAwait(false);
            var after = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
            lock (sync)
            {
                settingsSyncEnabled = after?.SyncConsentEnabled == true;
                settingsSyncRecoveryAvailable = false;
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(after);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(after);
                settingsSyncStatus = SettingsSyncStatusLabel(after);
            }
            StartSettingsSyncRunner(session);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (DeviceEnrollmentException exception)
        {
            lock (sync) { error = exception.Code; settingsSyncStatus = "Attention"; }
        }
        catch (Exception exception) when (IsSettingsSyncFailure(exception))
        {
            lock (sync) { error = "AccountSettingsUnavailable"; settingsSyncStatus = "Attention"; }
        }
        finally
        {
            if (entered) settingsSyncLifecycle.Release();
            lock (sync) settingsSyncBusy = false;
        }
    }

    private HostAccountSnapshot StartResolveSettingsSyncConflict(HostSettingsSyncConflictResolution? resolution)
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
            if (!deviceLinked) return Snapshot(errorOverride: "DevicePairingRequired");
            if (resolution is null || settingsSyncConflictReview is null ||
                resolution.Revision != settingsSyncConflictReview.Revision || settingsSyncStatus != "Conflict")
                return Snapshot(errorOverride: "AccountSettingsConflict");
            if (settingsSyncBusy || settingsTransferBusy || pairingBusy || deviceLinkPending || signInPending || signOutPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");

            error = null;
            settingsSyncBusy = true;
            settingsSyncStatus = "Resolving";
            settingsSyncTask = Task.Run(() => ResolveSettingsSyncConflictAsync(resolution));
            return Snapshot();
        }
    }

    private async Task ResolveSettingsSyncConflictAsync(HostSettingsSyncConflictResolution resolution)
    {
        var token = shutdown.Token;
        var entered = false;
        HostSettingsSyncSession? session = null;
        try
        {
            await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            session = settingsSyncSession ?? await LoadConsentedSettingsSyncSessionAsync(token).ConfigureAwait(false);
            if (session is null) throw new DeviceEnrollmentException("AccountSettingsConflict");
            var state = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
            var review = CreateSettingsSyncConflictReview(state);
            HostSettingsSyncConflictReview? displayed;
            lock (sync) displayed = settingsSyncConflictReview;
            if (state is null || review is null || displayed is null || review.ResetConflict ||
                displayed.ResetConflict || displayed.Revision != review.Revision || displayed.Current != review.Current ||
                displayed.ThisDevice != review.ThisDevice || !displayed.Fields.SequenceEqual(review.Fields, StringComparer.Ordinal) ||
                resolution.Revision != review.Revision || state.Conflicts == SettingsFields.None || state.Draft is null)
                throw new DeviceEnrollmentException("AccountSettingsConflict");

            var expectedFields = review.Fields.Select(ParseConflictField).ToHashSet();
            var selected = new Dictionary<HostSettingsSyncConflictField, HostSettingsSyncConflictSource>();
            if (resolution.Choices is null || resolution.Choices.Length != expectedFields.Count)
                throw new DeviceEnrollmentException("AccountSettingsConflict");
            foreach (var choice in resolution.Choices)
            {
                if (!Enum.IsDefined(choice.Field) || !Enum.IsDefined(choice.Source) ||
                    !expectedFields.Contains(choice.Field) || !selected.TryAdd(choice.Field, choice.Source))
                    throw new DeviceEnrollmentException("AccountSettingsConflict");
            }

            var current = state.Confirmed.EffectiveValues;
            var draft = state.Draft;
            var chosen = draft;
            foreach (var (field, source) in selected)
            {
                if (source != HostSettingsSyncConflictSource.OtherDevices) continue;
                chosen = field switch
                {
                    HostSettingsSyncConflictField.Theme => chosen with { Theme = current.Theme },
                    HostSettingsSyncConflictField.Language => chosen with { UiLanguage = current.UiLanguage },
                    HostSettingsSyncConflictField.Duration => chosen with { DefaultDurationMinutes = current.DefaultDurationMinutes },
                    _ => throw new DeviceEnrollmentException("AccountSettingsConflict")
                };
            }

            _ = await session.Conflicts.ResolveAsync(chosen, draft, state.Confirmed.Revision,
                state.Conflicts, ownerConfirmed: true, token).ConfigureAwait(false);
            var resolved = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
            lock (sync)
            {
                settingsSyncSession = session;
                settingsSyncEnabled = resolved?.SyncConsentEnabled == true;
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(resolved);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(resolved);
                settingsSyncStatus = SettingsSyncStatusLabel(resolved);
            }
            StartSettingsSyncRunner(session);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (DeviceEnrollmentException exception)
        {
            SettingsOutbox? latest = null;
            if (session is not null)
            {
                try { latest = await session.Outbox.LoadAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
            lock (sync)
            {
                error = exception.Code;
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(latest);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(latest);
                settingsSyncStatus = latest?.Conflicts != SettingsFields.None ? "Conflict" : "Attention";
            }
        }
        catch (Exception exception) when (IsSettingsSyncFailure(exception))
        {
            SettingsOutbox? latest = null;
            if (session is not null)
            {
                try { latest = await session.Outbox.LoadAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
            lock (sync)
            {
                error = exception is InvalidOperationException or ArgumentException
                    ? "AccountSettingsConflict" : "AccountSettingsUnavailable";
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(latest);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(latest);
                settingsSyncStatus = latest?.Conflicts != SettingsFields.None && error == "AccountSettingsConflict"
                    ? "Conflict" : "Attention";
            }
        }
        finally
        {
            if (entered) settingsSyncLifecycle.Release();
            lock (sync) settingsSyncBusy = false;
        }
    }

    private HostAccountSnapshot StartResolveSettingsSyncResetConflict(HostSettingsSyncResetResolution? resolution)
    {
        lock (sync)
        {
            if (disposed) return Snapshot(errorOverride: "AccountUnavailable");
            if (!identity.State.SignedIn) return Snapshot(errorOverride: "SignInRequired");
            if (!identity.State.EmailVerified) return Snapshot(errorOverride: "VerifiedEmailRequired");
            if (!deviceLinked) return Snapshot(errorOverride: "DevicePairingRequired");
            var review = settingsSyncConflictReview;
            if (resolution is null || !Enum.IsDefined(resolution.Choice) || review is null || !review.ResetConflict ||
                resolution.Revision != review.Revision || settingsSyncStatus != "Conflict")
                return Snapshot(errorOverride: "AccountSettingsConflict");
            if (settingsSyncBusy || settingsTransferBusy || pairingBusy || deviceLinkPending || signInPending || signOutPending || checkingDevice)
                return Snapshot(errorOverride: "AccountActionInProgress");

            error = null;
            settingsSyncBusy = true;
            settingsSyncStatus = "Resolving";
            settingsSyncTask = Task.Run(() => ResolveSettingsSyncResetConflictAsync(resolution));
            return Snapshot();
        }
    }

    private async Task ResolveSettingsSyncResetConflictAsync(HostSettingsSyncResetResolution resolution)
    {
        var token = shutdown.Token;
        var entered = false;
        HostSettingsSyncSession? session = null;
        try
        {
            await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            session = settingsSyncSession ?? await LoadConsentedSettingsSyncSessionAsync(token).ConfigureAwait(false);
            if (session is null) throw new DeviceEnrollmentException("AccountSettingsConflict");
            var state = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
            var review = CreateSettingsSyncConflictReview(state);
            HostSettingsSyncConflictReview? displayed;
            lock (sync) displayed = settingsSyncConflictReview;
            if (state is null || review is null || displayed is null || !state.DraftDeleted || !review.ResetConflict ||
                !displayed.ResetConflict || displayed.Revision != review.Revision || displayed.Current != review.Current ||
                displayed.ThisDevice != review.ThisDevice || !displayed.Fields.SequenceEqual(review.Fields, StringComparer.Ordinal) ||
                resolution.Revision != review.Revision || state.Conflicts == SettingsFields.None || state.Draft is null || state.InFlight is not null)
                throw new DeviceEnrollmentException("AccountSettingsConflict");

            _ = await session.Conflicts.ResolveResetAsync(
                keepThisDeviceReset: resolution.Choice == HostSettingsSyncResetSource.ThisDeviceReset,
                reviewedDraft: state.Draft,
                expectedRevision: state.Confirmed.Revision,
                expectedConflicts: state.Conflicts,
                ownerConfirmed: true,
                token).ConfigureAwait(false);
            var resolved = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
            lock (sync)
            {
                settingsSyncSession = session;
                settingsSyncEnabled = resolved?.SyncConsentEnabled == true;
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(resolved);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(resolved);
                settingsSyncStatus = SettingsSyncStatusLabel(resolved);
            }
            StartSettingsSyncRunner(session);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (DeviceEnrollmentException exception)
        {
            SettingsOutbox? latest = null;
            if (session is not null)
            {
                try { latest = await session.Outbox.LoadAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
            lock (sync)
            {
                error = exception.Code;
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(latest);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(latest);
                settingsSyncStatus = latest?.Conflicts != SettingsFields.None ? "Conflict" : "Attention";
            }
        }
        catch (Exception exception) when (IsSettingsSyncFailure(exception))
        {
            SettingsOutbox? latest = null;
            if (session is not null)
            {
                try { latest = await session.Outbox.LoadAsync(CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
            lock (sync)
            {
                error = exception is InvalidOperationException or ArgumentException
                    ? "AccountSettingsConflict" : "AccountSettingsUnavailable";
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(latest);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(latest);
                settingsSyncStatus = latest?.Conflicts != SettingsFields.None && error == "AccountSettingsConflict"
                    ? "Conflict" : "Attention";
            }
        }
        finally
        {
            if (entered) settingsSyncLifecycle.Release();
            lock (sync) settingsSyncBusy = false;
        }
    }

    private async Task RunSettingsSyncOperationAsync(bool enable)
    {
        var token = shutdown.Token;
        var entered = false;
        try
        {
            if (enable)
            {
                Func<CancellationToken, Task<bool>>? presenter;
                lock (sync) presenter = settingsSyncConsentPresenter;
                if (presenter is null) throw new DeviceEnrollmentException("AccountSettingsUnavailable");
                if (!await presenter(token).ConfigureAwait(false))
                {
                    lock (sync) settingsSyncStatus = "Cancelled";
                    return;
                }
            }

            await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
            entered = true;
            if (enable)
            {
                var session = settingsSyncSession;
                if (session is null)
                {
                    session = await CreateSettingsSyncSessionAsync(token).ConfigureAwait(false);
                    settingsSyncSession = session;
                }
                await session.EnableAsync(ownerConfirmed: true, token).ConfigureAwait(false);
                StartSettingsSyncRunner(session);
                var state = await session.Outbox.LoadAsync(token).ConfigureAwait(false);
                lock (sync)
                {
                    settingsSyncEnabled = state?.SyncConsentEnabled == true;
                    settingsSyncStatus = SettingsSyncStatusLabel(state);
                    settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(state);
                    settingsSyncConflictReview = CreateSettingsSyncConflictReview(state);
                }
            }
            else
            {
                var session = settingsSyncSession;
                if (session is not null) await session.DisableAsync(token).ConfigureAwait(false);
                var state = session is null ? null : await session.Outbox.LoadAsync(token).ConfigureAwait(false);
                lock (sync)
                {
                    settingsSyncEnabled = false;
                    settingsSyncStatus = "Disabled";
                    settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(state);
                }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (DeviceEnrollmentException exception)
        {
            lock (sync) { error = exception.Code; settingsSyncStatus = "Attention"; }
        }
        catch (Exception exception) when (IsSettingsSyncFailure(exception))
        {
            lock (sync) { error = "AccountSettingsUnavailable"; settingsSyncStatus = "Attention"; }
        }
        finally
        {
            if (entered) settingsSyncLifecycle.Release();
            lock (sync) settingsSyncBusy = false;
        }
    }

    private async Task<HostSettingsSyncSession> CreateSettingsSyncSessionAsync(CancellationToken token)
    {
        var store = settingsStore ?? throw new DeviceEnrollmentException("AccountSettingsUnavailable");
        var binding = identity.AccountBinding ?? throw new DeviceEnrollmentException("SignInRequired");
        var issuer = configuration.Authority.AbsoluteUri.TrimEnd('/');
        var localVault = GetDeviceVault(binding);
        using var beforeRenewal = await localVault.LoadForAccountAsync(issuer, binding, token).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DevicePairingRequired");
        var membership = await EnsureDeviceSessionAsync(localVault, beforeRenewal, token).ConfigureAwait(false);
        var loaded = await localVault.LoadForAccountAsync(issuer, binding, token).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceVaultMismatch");
        try
        {
            ValidateMembershipSnapshot(membership, loaded.Context, loaded.Material);
            if (membership.MembershipChain is not { Length: > 0 } chain)
                throw new DeviceEnrollmentException("DeviceMembershipUpdateRequired");
            var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
            var session = CreateObservedSettingsSyncSession(origin, loaded, chain, store);
            loaded = null!; // Ownership transfers to the returned session.
            return session;
        }
        finally { loaded?.Dispose(); }
    }

    private HostSettingsSyncSession CreateObservedSettingsSyncSession(Uri origin,
        WindowsDeviceVault.LoadedDeviceVault loaded, MembershipDocument[] chain, DesktopSettingsStore store)
    {
        if (testSettingsSyncRoot is not null && !configuration.LocalTest)
            throw new InvalidOperationException("A custom settings-sync storage root is restricted to local tests.");
        HostSettingsSyncSession? session = null;
        session = HostSettingsSyncSession.Create(origin, new NativeSettingsRequestSender(identity), loaded, chain, store,
            report: (result, state, token) => ReportSettingsSyncStateAsync(session!, result, state, token),
            testSyncRoot: testSettingsSyncRoot);
        return session;
    }

    private Task ReportSettingsSyncStateAsync(HostSettingsSyncSession session, SettingsRetryResult retry,
        SettingsOutbox? state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (disposed || !ReferenceEquals(settingsSyncSession, session)) return Task.CompletedTask;
            settingsSyncEnabled = state?.SyncConsentEnabled == true;
            settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(state);
            settingsSyncConflictReview = CreateSettingsSyncConflictReview(state);
            settingsSyncRecoveryAvailable = retry.Status == SettingsRetryStatus.NeedsAttention &&
                retry.RetryCheckpointRecoveryAvailable && state?.SyncConsentEnabled == true;
            settingsSyncStatus = retry.Status == SettingsRetryStatus.NeedsAttention
                ? "Attention" : SettingsSyncStatusLabel(state);
        }
        return Task.CompletedTask;
    }

    private async Task ResumeSettingsSyncIfConsentedAsync(WindowsDeviceVault localVault,
        DeviceAccountContext context, string deviceId, MembershipSnapshot membership, CancellationToken token)
    {
        try
        {
            if (!await HostSettingsSyncSession.HasSavedConsentAsync(context, deviceId, token,
                localSyncRoot: configuration.LocalTest ? testSettingsSyncRoot : null).ConfigureAwait(false))
            {
                lock (sync)
                {
                    if (settingsSyncSession is null) { settingsSyncEnabled = false; settingsSyncStatus = "Disabled"; }
                }
                return;
            }

            await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (settingsSyncSession is not null) return;
                var loaded = await localVault.LoadForAccountAsync(context.Issuer, context.AccountBinding, token)
                    .ConfigureAwait(false) ?? throw new DeviceEnrollmentException("DeviceVaultMismatch");
                HostSettingsSyncSession? created = null;
                try
                {
                    ValidateMembershipSnapshot(membership, loaded.Context, loaded.Material);
                    if (membership.MembershipChain is not { Length: > 0 } chain)
                        throw new DeviceEnrollmentException("DeviceMembershipUpdateRequired");
                    var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
                    created = CreateObservedSettingsSyncSession(origin, loaded, chain,
                        settingsStore ?? throw new DeviceEnrollmentException("AccountSettingsUnavailable"));
                    loaded = null!; // Ownership transfers to the session.
                    var state = await created.Outbox.LoadAsync(token).ConfigureAwait(false);
                    if (state?.SyncConsentEnabled != true)
                    {
                        await created.DisposeAsync().ConfigureAwait(false);
                        created = null;
                        return;
                    }
                    settingsSyncSession = created;
                    lock (sync)
                    {
                        settingsSyncEnabled = true;
                        settingsSyncStatus = SettingsSyncStatusLabel(state);
                        settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(state);
                        settingsSyncConflictReview = CreateSettingsSyncConflictReview(state);
                    }
                    StartSettingsSyncRunner(created);
                    created = null;
                }
                finally
                {
                    loaded?.Dispose();
                    if (created is not null) await created.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally { settingsSyncLifecycle.Release(); }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (IsSettingsSyncFailure(exception))
        {
            lock (sync) { settingsSyncEnabled = false; settingsSyncStatus = "Attention"; }
        }
    }

    private void StartSettingsSyncRunner(HostSettingsSyncSession session)
    {
        lock (sync)
        {
            if (settingsSyncRunnerTask is { IsCompleted: false }) return;
            settingsSyncRunnerTask = Task.Run(() => RunSettingsSyncLoopAsync(session));
        }
    }

    private async Task RunSettingsSyncLoopAsync(HostSettingsSyncSession session)
    {
        try { await session.RunAsync(shutdown.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception exception) when (IsSettingsSyncFailure(exception))
        {
            lock (sync) { settingsSyncStatus = "Attention"; error = "AccountSettingsUnavailable"; }
        }
    }

    private async Task DisposeSettingsSyncSessionAsync(CancellationToken token)
    {
        await settingsSyncLifecycle.WaitAsync(token).ConfigureAwait(false);
        try
        {
            HostSettingsSyncSession? session;
            Task? runner;
            lock (sync)
            {
                session = settingsSyncSession;
                runner = settingsSyncRunnerTask;
                settingsSyncSession = null;
                settingsSyncRunnerTask = null;
                settingsSyncEnabled = false;
                settingsSyncStatus = "Disabled";
                settingsSyncConflictReview = null;
                settingsSyncRecoveryAvailable = false;
                settingsSyncLocalOverrideSummary = null;
            }
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            if (runner is not null)
            {
                try { await runner.WaitAsync(token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            }
        }
        finally { settingsSyncLifecycle.Release(); }
    }

    public async Task<bool> TrySyncSharedSettingsEditAsync(DesktopSettings previous,
        DesktopSettings proposed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(proposed);
        var fields = SettingsFields.None;
        if (previous.Theme != proposed.Theme) fields |= SettingsFields.Theme;
        if (previous.UiLanguage != proposed.UiLanguage) fields |= SettingsFields.Language;
        if (previous.LastDurationMinutes != proposed.LastDurationMinutes) fields |= SettingsFields.Duration;
        if (fields == SettingsFields.None) return false;

        await settingsSyncLifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = settingsSyncSession;
            if (session is null)
            {
                if (await TryPersistDisabledLocalOverrideEditAsync(null, proposed, fields, cancellationToken)
                    .ConfigureAwait(false)) return true;
                session = await LoadConsentedSettingsSyncSessionAsync(cancellationToken).ConfigureAwait(false);
                if (session is null) return false;
                try
                {
                    var state = await session.Outbox.LoadAsync(cancellationToken).ConfigureAwait(false);
                    if (state?.SyncConsentEnabled != true)
                    {
                        await session.DisposeAsync().ConfigureAwait(false);
                        return false;
                    }
                    settingsSyncSession = session;
                    lock (sync)
                    {
                        settingsSyncEnabled = true;
                        settingsSyncStatus = SettingsSyncStatusLabel(state);
                        settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(state);
                        settingsSyncConflictReview = CreateSettingsSyncConflictReview(state);
                    }
                    StartSettingsSyncRunner(session);
                }
                catch
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            if (!await session.Consent.IsEnabledAsync(cancellationToken).ConfigureAwait(false))
                return await TryPersistDisabledLocalOverrideEditAsync(session.Outbox, proposed, fields,
                    cancellationToken).ConfigureAwait(false);
            _ = await session.Edits.EditSelectedAsync(DesktopSettingsProjection.Extract(proposed), fields,
                new LocalSettingsOverrides(), cancellationToken).ConfigureAwait(false);
            var stateAfterEdit = await session.Outbox.LoadAsync(cancellationToken).ConfigureAwait(false);
            lock (sync)
            {
                settingsSyncEnabled = true;
                settingsSyncStatus = SettingsSyncStatusLabel(stateAfterEdit);
                settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(stateAfterEdit);
                settingsSyncConflictReview = CreateSettingsSyncConflictReview(stateAfterEdit);
            }
            return true;
        }
        catch (Exception exception) when (IsSettingsSyncFailure(exception))
        {
            lock (sync) { settingsSyncStatus = "Attention"; error = "AccountSettingsUnavailable"; }
            return false;
        }
        finally { settingsSyncLifecycle.Release(); }
    }

    private async Task<bool> TryPersistDisabledLocalOverrideEditAsync(SettingsOutboxVault? knownOutbox,
        DesktopSettings proposed, SettingsFields changedFields, CancellationToken token)
    {
        var outbox = knownOutbox;
        if (outbox is null)
        {
            var binding = identity.AccountBinding;
            if (binding is null) return false;
            var issuer = configuration.Authority.AbsoluteUri.TrimEnd('/');
            var deviceVault = GetDeviceVault(binding);
            using var loaded = await deviceVault.LoadForAccountAsync(issuer, binding, token).ConfigureAwait(false);
            if (loaded is null) return false;

            var scope = new SettingsSyncScope(loaded.Context.Issuer, loaded.Context.AccountId,
                loaded.Context.GenerationId, loaded.DeviceId);
            scope.Validate();
            var root = configuration.LocalTest && testSettingsSyncRoot is not null
                ? testSettingsSyncRoot : SettingsSyncLocalStorage.GetCurrentUserRoot();
            var directory = SettingsSyncLocalStorage.GetScopedDirectory(root, scope);
            if (!Directory.Exists(directory)) return false;

            // Reapply the same owner ACL and reparse-point checks used by the sync session before
            // touching a persisted outbox. The payload itself is also scope-authenticated by DPAPI.
            var preparedDirectory = SettingsSyncLocalStorage.PrepareScopedDirectory(root, scope);
            if (!string.Equals(Path.GetFullPath(directory), Path.GetFullPath(preparedDirectory), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Settings sync storage resolved outside its linked-device scope.");
            var outboxPath = Path.Combine(preparedDirectory, "settings-outbox.dpapi");
            if (!File.Exists(outboxPath)) return false;
            if ((File.GetAttributes(outboxPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Settings outbox cannot be a reparse point.");
            outbox = new SettingsOutboxVault(outboxPath, scope);
        }

        var saved = await outbox.LoadAsync(token).ConfigureAwait(false);
        if (saved?.SyncConsentEnabled != false) return false;
        var overrides = UpdateEditedLocalOverrides(saved.LocalOverrides, proposed, changedFields);
        if (overrides == saved.LocalOverrides) return false;

        var updated = await outbox.UpdateAsync(current =>
        {
            if (current?.SyncConsentEnabled != false)
                throw new InvalidOperationException("Settings sync consent changed while saving a local preference.");
            return current with { LocalOverrides = UpdateEditedLocalOverrides(current.LocalOverrides, proposed, changedFields) };
        }, token).ConfigureAwait(false);
        lock (sync)
        {
            settingsSyncEnabled = false;
            settingsSyncStatus = "Disabled";
            settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(updated);
        }
        return true;
    }

    private static LocalSettingsOverrides UpdateEditedLocalOverrides(LocalSettingsOverrides current,
        DesktopSettings proposed, SettingsFields changedFields) => current with
    {
        Theme = current.Theme is not null && (changedFields & SettingsFields.Theme) != 0
            ? proposed.Theme == AppTheme.Light ? "light" : "dark" : current.Theme,
        UiLanguage = current.UiLanguage is not null && (changedFields & SettingsFields.Language) != 0
            ? proposed.UiLanguage : current.UiLanguage,
        DefaultDurationMinutes = current.DefaultDurationMinutes is not null &&
            (changedFields & SettingsFields.Duration) != 0
            ? proposed.LastDurationMinutes : current.DefaultDurationMinutes
    };

    public async Task<bool> TryImportReviewedSettingsAsync(SettingsImportPreview preview,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preview);
        await settingsSyncLifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = settingsSyncSession;
            if (session is null)
            {
                session = await LoadConsentedSettingsSyncSessionAsync(cancellationToken).ConfigureAwait(false);
                if (session is null) return false;
                var state = await session.Outbox.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (state?.SyncConsentEnabled != true)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                    return false;
                }
                settingsSyncSession = session;
                lock (sync)
                {
                    settingsSyncEnabled = true;
                    settingsSyncStatus = SettingsSyncStatusLabel(state);
                    settingsSyncLocalOverrideSummary = CreateSettingsLocalOverrideSummary(state);
                    settingsSyncConflictReview = CreateSettingsSyncConflictReview(state);
                }
                StartSettingsSyncRunner(session);
            }
            if (!await session.Consent.IsEnabledAsync(cancellationToken).ConfigureAwait(false)) return false;
            var result = await session.Edits.ImportReviewedAsync(preview, new LocalSettingsOverrides(), cancellationToken)
                .ConfigureAwait(false);
            lock (sync)
            {
                settingsSyncEnabled = true;
                settingsSyncStatus = result.Pending ? "Pending" : result.Conflicts != SettingsFields.None ? "Conflict" : "Enabled";
            }
            return true;
        }
        finally { settingsSyncLifecycle.Release(); }
    }

    private async Task<HostSettingsSyncSession?> LoadConsentedSettingsSyncSessionAsync(CancellationToken token)
    {
        var binding = identity.AccountBinding;
        if (binding is null) return null;
        var issuer = configuration.Authority.AbsoluteUri.TrimEnd('/');
        var localVault = GetDeviceVault(binding);
        using var current = await localVault.LoadForAccountAsync(issuer, binding, token).ConfigureAwait(false);
        if (current is null || !await HostSettingsSyncSession.HasSavedConsentAsync(current.Context,
            current.DeviceId, token, localSyncRoot: configuration.LocalTest ? testSettingsSyncRoot : null).ConfigureAwait(false)) return null;
        var membership = await EnsureDeviceSessionAsync(localVault, current, token).ConfigureAwait(false);
        var loaded = await localVault.LoadForAccountAsync(issuer, binding, token).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceVaultMismatch");
        try
        {
            ValidateMembershipSnapshot(membership, loaded.Context, loaded.Material);
            if (membership.MembershipChain is not { Length: > 0 } chain)
                throw new DeviceEnrollmentException("DeviceMembershipUpdateRequired");
            var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
            var session = CreateObservedSettingsSyncSession(origin, loaded, chain,
                settingsStore ?? throw new DeviceEnrollmentException("AccountSettingsUnavailable"));
            loaded = null!;
            return session;
        }
        finally { loaded?.Dispose(); }
    }

    private static HostSettingsSyncConflictReview? CreateSettingsSyncConflictReview(SettingsOutbox? state)
    {
        if (state is null || state.Conflicts == SettingsFields.None || state.Draft is null) return null;
        var fields = new List<string>(3);
        if ((state.Conflicts & SettingsFields.Theme) != 0) fields.Add("theme");
        if ((state.Conflicts & SettingsFields.Language) != 0) fields.Add("language");
        if ((state.Conflicts & SettingsFields.Duration) != 0) fields.Add("duration");
        return new HostSettingsSyncConflictReview(state.Confirmed.Revision, fields.ToArray(),
            state.Confirmed.EffectiveValues, state.Draft, state.DraftDeleted);
    }

    private static HostSettingsLocalOverrideSummary? CreateSettingsLocalOverrideSummary(SettingsOutbox? state) =>
        state is null ? null : new(state.LocalOverrides.Theme is not null,
            state.LocalOverrides.UiLanguage is not null, state.LocalOverrides.DefaultDurationMinutes is not null);

    private static HostSettingsSyncResetPreview? CreateSettingsSyncResetPreview(SettingsOutbox? state)
    {
        if (state?.SyncConsentEnabled != true || state.Confirmed.Deleted || state.Draft is not null ||
            state.InFlight is not null || state.Conflicts != SettingsFields.None || state.DraftDeleted)
            return null;
        return new HostSettingsSyncResetPreview(state.Confirmed.Revision, state.Confirmed.EffectiveValues,
            GlobalSettings.Default);
    }

    private static HostSettingsSyncConflictField ParseConflictField(string field) => field switch
    {
        "theme" => HostSettingsSyncConflictField.Theme,
        "language" => HostSettingsSyncConflictField.Language,
        "duration" => HostSettingsSyncConflictField.Duration,
        _ => throw new ArgumentException("Unknown settings conflict field.", nameof(field))
    };

    private static string SettingsSyncStatusLabel(SettingsOutbox? state) => state?.SyncConsentEnabled != true ? "Disabled" :
        state.Conflicts != SettingsFields.None ? "Conflict" : state.Pending ? "Pending" : "Enabled";

    private static bool IsSettingsSyncFailure(Exception exception) => exception is DeviceEnrollmentException or HttpRequestException or TimeoutException
        or InvalidOperationException or IOException or InvalidDataException or CryptographicException
        or UnauthorizedAccessException or System.Security.SecurityException or JsonException or ArgumentException;

    private async Task RequestDevicePairingAsync()
    {
        var binding = identity.AccountBinding ?? throw new DeviceEnrollmentException("SignInRequired");
        var issuer = configuration.Authority.AbsoluteUri.TrimEnd('/');
        var cloud = await GetCloudAccountContextAsync(shutdown.Token).ConfigureAwait(false);
        if (!cloud.DeviceLinked || cloud.AccountId is null || cloud.GenerationId is null)
            throw new DeviceEnrollmentException("DeviceLinkFirstRequired");
        var context = new DeviceAccountContext(issuer, binding, cloud.AccountId, cloud.GenerationId);
        context.Validate();
        var vault = GetPendingPairingVault(binding);
        DeviceIdentity? created = null;
        WindowsDeviceVault.LoadedDeviceVault? resumed = null;
        byte[]? dummyKey = null;
        try
        {
            DeviceIdentity device;
            if (vault.Exists)
            {
                resumed = await vault.LoadForAccountAsync(issuer, binding, shutdown.Token).ConfigureAwait(false)
                    ?? throw new DeviceEnrollmentException("DeviceVaultMismatch");
                if (resumed.Context.AccountId != context.AccountId || resumed.Context.GenerationId != context.GenerationId)
                    throw new DeviceEnrollmentException("PairingRecoveryUnavailable");
                device = resumed.Material.Identity;
                if (resumed.Material.PendingPairingId is { } savedPairingId)
                {
                    PairingStateResponse? state = null;
                    try
                    {
                        state = await ReadOwnPairingAsync(resumed.Context, device, savedPairingId,
                            shutdown.Token).ConfigureAwait(false);
                    }
                    catch (DeviceEnrollmentException exception) when (exception.Code == "PairingNotFound") { }
                    if (state is not null)
                    {
                        ValidateOwnPairingState(state, savedPairingId, device.Public);
                        if (state.State != "Expired")
                        {
                            lock (sync)
                            {
                                pendingPairingId = savedPairingId;
                                pendingPairingFingerprint = device.Public.Fingerprint;
                                error = null;
                            }
                            return;
                        }
                    }
                }
            }
            else
            {
                created = DeviceIdentity.Create();
                device = created;
                dummyKey = RandomNumberGenerator.GetBytes(32);
                var zeroDigestBytes = new byte[32];
                string zeroDigest;
                try { zeroDigest = DeviceEncoding.Encode(zeroDigestBytes); }
                finally { CryptographicOperations.ZeroMemory(zeroDigestBytes); }
                await vault.SaveAsync(device, null, context, dummyKey, 1, zeroDigest,
                    shutdown.Token).ConfigureAwait(false);
            }

            ReadOnlyMemory<byte> placeholderKey = resumed is null
                ? dummyKey ?? throw new DeviceEnrollmentException("PairingRecoveryUnavailable")
                : resumed.Material.AccountContentKey;
            var placeholderRevision = resumed?.Material.MembershipRevision ?? 1;
            var placeholderDigest = resumed?.Material.MembershipDigest ?? DeviceEncoding.Encode(new byte[32]);
            var challengeRequest = new BootstrapChallengeRequest(context.AccountId, context.GenerationId,
                device.Public.DeviceId, device.Public.SigningPublicKey, device.Public.EncryptionPublicKey, null, null);
            var challenge = await CreateBootstrapChallengeAsync(challengeRequest, shutdown.Token).ConfigureAwait(false);
            if (challenge.Purpose != "pairing" || challenge.AccountId != context.AccountId ||
                challenge.GenerationId != context.GenerationId || challenge.DeviceId != device.Public.DeviceId ||
                challenge.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new DeviceEnrollmentException("DeviceChallengeRejected");
            var challengeProof = new BootstrapChallengeProof(challenge.ChallengeId, context.AccountId,
                context.GenerationId, device.Public.DeviceId, challenge.Nonce);
            var capabilities = new DeviceCapabilities("Windows", "0.1.155", "Real", "Unsupported",
                ["Applications", "Websites"], false, false);
            var pairingProof = new BootstrapPairingProof(challenge.ChallengeId, challengeProof, capabilities);
            var canonical = MembershipProtocol.CanonicalPairingProof(pairingProof);
            string signature;
            try { signature = device.Sign("FG-PAIRING-1", canonical); }
            finally { CryptographicOperations.ZeroMemory(canonical); }
            // PairingId is the challenge id. Persist it before the network mutation
            // so an interrupted response can be recovered by reading that request.
            await vault.SaveAsync(device, null, context, placeholderKey, placeholderRevision,
                placeholderDigest, shutdown.Token, pendingPairingId: challenge.ChallengeId,
                allowReplace: true).ConfigureAwait(false);
            var request = new BootstrapPairingRequest(pairingProof, signature);
            var response = await CreatePairingAsync(request, shutdown.Token).ConfigureAwait(false);
            DeviceEncoding.RequireId(response.PairingId);
            if (response.PairingId != challenge.ChallengeId || response.State != "Pending" ||
                response.DeviceId != device.Public.DeviceId || response.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                throw new DeviceEnrollmentException("PairingRequestRejected");
            lock (sync)
            {
                pendingPairingId = response.PairingId;
                pendingPairingFingerprint = device.Public.Fingerprint;
                deviceLinked = false;
                error = null;
            }
        }
        finally
        {
            created?.Dispose();
            resumed?.Dispose();
            if (dummyKey is not null) CryptographicOperations.ZeroMemory(dummyKey);
        }
    }

    private async Task<BootstrapChallenge> CreateBootstrapChallengeAsync(BootstrapChallengeRequest request,
        CancellationToken cancellationToken)
    {
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        using var response = await identity.SendApiRequestAsync(() => new HttpRequestMessage(HttpMethod.Post,
            new Uri(origin, "/v1/bootstrap/challenges"))
        { Content = JsonContent.Create(request, options: ApiJson) },
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Created)
            throw new DeviceEnrollmentException(response.StatusCode == System.Net.HttpStatusCode.Conflict
                ? "DevicePairingRequired" : "DeviceCloudUnavailable");
        return await response.Content.ReadFromJsonAsync<BootstrapChallenge>(ApiJson, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceChallengeUnavailable");
    }

    private async Task<BootstrapPairingResponse> CreatePairingAsync(BootstrapPairingRequest body,
        CancellationToken cancellationToken)
    {
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        using var response = await identity.SendApiRequestAsync(() => new HttpRequestMessage(HttpMethod.Post,
            new Uri(origin, "/v1/bootstrap/pairings"))
        { Content = JsonContent.Create(body, options: ApiJson) },
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Created)
            throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        return await response.Content.ReadFromJsonAsync<BootstrapPairingResponse>(ApiJson, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("PairingRequestUnavailable");
    }

    private async Task RefreshPendingPairingsAsync()
    {
        var mapped = await LoadPendingPairingsAsync(shutdown.Token).ConfigureAwait(false);
        lock (sync) { pendingPairings = mapped; error = null; }
    }

    private async Task ApproveDevicePairingAsync(string pairingId, string expectedFingerprint)
    {
        DeviceEncoding.RequireId(pairingId);
        if (expectedFingerprint.Length != 43 || expectedFingerprint.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
            throw new DeviceEnrollmentException("PairingFingerprintInvalid");
        await RefreshPendingPairingsAsync().ConfigureAwait(false);
        var pending = pendingPairings.SingleOrDefault(item => item.PairingId == pairingId)
            ?? throw new DeviceEnrollmentException("PairingUnavailable");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(pending.Fingerprint),
            Encoding.ASCII.GetBytes(expectedFingerprint)))
            throw new DeviceEnrollmentException("PairingFingerprintMismatch");
        if (pending.ExpiresAtUtc <= DateTimeOffset.UtcNow) throw new DeviceEnrollmentException("PairingExpired");
        Func<HostPendingPairingSummary, CancellationToken, Task<bool>>? presenter;
        lock (sync) presenter = pairingApprovalPresenter;
        if (presenter is null || !await presenter(pending, shutdown.Token).ConfigureAwait(false))
            throw new DeviceEnrollmentException("PairingApprovalNotConfirmed");

        var binding = identity.AccountBinding ?? throw new DeviceEnrollmentException("SignInRequired");
        var issuer = configuration.Authority.AbsoluteUri.TrimEnd('/');
        var localVault = GetDeviceVault(binding);
        using var enrolled = await localVault.LoadForAccountAsync(issuer, binding, shutdown.Token).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceVaultMismatch");
        if (enrolled.Material.DeviceSession is null)
            throw new DeviceEnrollmentException("DeviceSessionRecoveryRequired");
        var snapshot = await GetCurrentMembershipAsync(enrolled.Context, enrolled.Material, shutdown.Token).ConfigureAwait(false);
        var current = snapshot.Membership;
        if (current.ProposedRevision != enrolled.Material.MembershipRevision ||
            snapshot.MembershipDigest != enrolled.Material.MembershipDigest ||
            current.Members.Any(member => member.DeviceId == pending.DeviceId))
            throw new DeviceEnrollmentException("DeviceMembershipUpdateRequired");
        if (current.Members.Length >= 10) throw new DeviceEnrollmentException("DeviceLimitReached");
        var requester = new DevicePublicIdentity(pending.DeviceId, pending.SigningPublicKey, pending.EncryptionPublicKey);
        requester.Validate();
        if (requester.Fingerprint != pending.Fingerprint) throw new DeviceEnrollmentException("PairingFingerprintInvalid");
        var next = current with
        {
            BaseRevision = current.ProposedRevision,
            ProposedRevision = current.ProposedRevision + 1,
            KeyEpoch = current.KeyEpoch,
            PreviousDigest = snapshot.MembershipDigest,
            Members = [.. current.Members, new DeviceMember(pending.DeviceId, requester.SigningPublicKey,
                requester.EncryptionPublicKey, "Active", ["Read", "StartNormal", "WriteSettings"])],
            SignerDeviceId = enrolled.Material.Identity.Public.DeviceId,
            Signature = DeviceEncoding.Encode(new byte[64])
        };
        var canonical = MembershipProtocol.CanonicalMembership(next);
        try { next = next with { Signature = enrolled.Material.Identity.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
        var digest = MembershipProtocol.Digest(next);
        var recipients = next.Members.Where(member => member.Status == "Active")
            .Select(member => (Id: member.DeviceId, Key: member.EncryptionPublicKey)).ToList();
        recipients.Add((next.AccountId, next.RecoveryEncryptionPublicKey));
        var envelopes = recipients.Select(recipient => SignedKeyEnvelopeCodec.SealForDevice(
            enrolled.Material.AccountContentKey.Span,
            new KeyEnvelopeContext(next.AccountId, next.GenerationId, next.KeyEpoch, recipient.Id,
                digest, enrolled.Material.Identity.Public.DeviceId), recipient.Key, enrolled.Material.Identity)).ToArray();
        var request = new PairingApprovalRequest(pairingId, next, envelopes);
        var target = $"/v1/pairings/{pairingId}/approve";
        var requestId = Guid.NewGuid().ToString("D");
        var timestamp = DateTimeOffset.UtcNow;
        timestamp = new DateTimeOffset(timestamp.Ticks - timestamp.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var bodyBytes = MembershipProtocol.CanonicalPairingApprovalBody(request);
        var bodyDigest = DeviceEncoding.Encode(SHA256.HashData(bodyBytes));
        CryptographicOperations.ZeroMemory(bodyBytes);
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        using var response = await identity.SendApiRequestWithTokenDigestAsync(tokenDigest =>
        {
            var proof = new DeviceRequestProof("POST", target, next.AccountId, next.GenerationId,
                enrolled.Material.Identity.Public.DeviceId, requestId, timestamp.ToUnixTimeMilliseconds(),
                tokenDigest, bodyDigest);
            var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try
            {
                var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, target))
                { Content = JsonContent.Create(request, options: ApiJson) };
                message.Headers.Add("X-FG-Device-Id", enrolled.Material.Identity.Public.DeviceId);
                message.Headers.Add("X-FG-Request-Id", requestId);
                message.Headers.Add("X-FG-Timestamp", timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
                message.Headers.Add("X-FG-Proof", enrolled.Material.Identity.Sign("FG-REQUEST-1", proofBytes));
                return message;
            }
            finally { CryptographicOperations.ZeroMemory(proofBytes); }
        }, shutdown.Token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new DeviceEnrollmentException("DeviceMembershipUpdateRequired");
        if (!response.IsSuccessStatusCode) throw new DeviceEnrollmentException("PairingApprovalUnavailable");
        var result = await response.Content.ReadFromJsonAsync<PairingApprovalResult>(ApiJson, shutdown.Token).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("PairingApprovalUnavailable");
        if (result.State != "Approved" || result.MembershipRevision != next.ProposedRevision || result.MembershipDigest != digest)
            throw new DeviceEnrollmentException("PairingApprovalRejected");
        await localVault.UpdateMembershipAsync(enrolled.Context, enrolled.Material.Identity.Public.DeviceId,
            enrolled.Material.MembershipRevision, result.MembershipRevision, result.MembershipDigest,
            shutdown.Token).ConfigureAwait(false);
        await RefreshPendingPairingsAsync().ConfigureAwait(false);
    }

    private async Task CompleteDevicePairingAsync()
    {
        var binding = identity.AccountBinding ?? throw new DeviceEnrollmentException("SignInRequired");
        var issuer = configuration.Authority.AbsoluteUri.TrimEnd('/');
        var pendingVault = GetPendingPairingVault(binding);
        if (!pendingVault.Exists) throw new DeviceEnrollmentException("PairingRequestRequired");
        using var pending = await pendingVault.LoadForAccountAsync(issuer, binding, shutdown.Token).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceVaultMismatch");
        var pairingId = pending.Material.PendingPairingId
            ?? throw new DeviceEnrollmentException("PairingRecoveryUnavailable");
        lock (sync)
        {
            pendingPairingId = pairingId;
            pendingPairingFingerprint = pending.Material.Identity.Public.Fingerprint;
        }

        if (pending.Material.DeviceSession is { } persistedSession)
        {
            try
            {
                var current = await GetCurrentMembershipAsync(pending.Context, pending.Material, shutdown.Token).ConfigureAwait(false);
                await FinalizePairingVaultAsync(pendingVault, pending.Context, pending.Material,
                    current.Membership.ProposedRevision, current.MembershipDigest, shutdown.Token).ConfigureAwait(false);
                return;
            }
            catch (DeviceEnrollmentException exception) when (exception.Code == "DeviceSessionInvalid") { }
            _ = persistedSession;
        }

        var state = await ReadOwnPairingAsync(pending.Context, pending.Material.Identity, pairingId,
            shutdown.Token).ConfigureAwait(false);
        ValidateOwnPairingState(state, pairingId, pending.Material.Identity.Public);
        if (state.State == "Pending")
        {
            lock (sync) { pendingPairingFingerprint = state.Fingerprint; error = null; }
            return;
        }
        if (state.State != "Approved" || state.ExpiresAtUtc <= DateTimeOffset.UtcNow ||
            state.Membership is null || state.KeyEnvelopes is null)
            throw new DeviceEnrollmentException(state.State == "Expired" ? "PairingExpired" : "PairingUnavailable");

        var membership = state.Membership;
        var member = membership.Members.SingleOrDefault(candidate => candidate.DeviceId == pending.DeviceId);
        var signerMember = membership.Members.SingleOrDefault(candidate => candidate.DeviceId == membership.SignerDeviceId);
        if (!MembershipProtocol.VerifyMembership(membership) || membership.AccountId != pending.Context.AccountId ||
            membership.GenerationId != pending.Context.GenerationId ||
            MembershipProtocol.Digest(membership) != state.MembershipDigest || member?.Status != "Active" ||
            member.SigningPublicKey != pending.Material.Identity.Public.SigningPublicKey ||
            member.EncryptionPublicKey != pending.Material.Identity.Public.EncryptionPublicKey ||
            signerMember?.Status != "Active" || !signerMember.Roles.Contains("ManageDevices", StringComparer.Ordinal))
            throw new DeviceEnrollmentException("PairingMembershipInvalid");

        byte[]? receivedContentKey = null;
        string deviceSession;
        if (pending.Material.DeviceSession is { } existingSession)
        {
            deviceSession = existingSession;
            receivedContentKey = pending.Material.AccountContentKey.ToArray();
        }
        else
        {
            var envelope = state.KeyEnvelopes.SingleOrDefault(item => item.RecipientId == pending.DeviceId)
                ?? throw new DeviceEnrollmentException("PairingEnvelopeMissing");
            var keyContext = new KeyEnvelopeContext(membership.AccountId, membership.GenerationId,
                membership.KeyEpoch, pending.DeviceId, state.MembershipDigest, membership.SignerDeviceId);
            receivedContentKey = SignedKeyEnvelopeCodec.OpenForDevice(envelope, keyContext,
                new DevicePublicIdentity(signerMember.DeviceId, signerMember.SigningPublicKey, signerMember.EncryptionPublicKey),
                pending.Material.Identity);
            var sessionBytes = RandomNumberGenerator.GetBytes(32);
            try { deviceSession = DeviceEncoding.Encode(sessionBytes); }
            finally { CryptographicOperations.ZeroMemory(sessionBytes); }
            await pendingVault.SaveAsync(pending.Material.Identity, null, pending.Context, receivedContentKey,
                membership.ProposedRevision, state.MembershipDigest, shutdown.Token, deviceSession,
                pairingId, allowReplace: true, keyEpoch: membership.KeyEpoch).ConfigureAwait(false);
        }

        try
        {
            var confirmTarget = $"/v1/bootstrap/pairings/{pairingId}/confirm";
            var confirmBody = new ConfirmPairingRequest(pairingId, state.MembershipDigest, deviceSession);
            var canonicalBody = MembershipProtocol.CanonicalConfirmPairingBody(confirmBody);
            var bodyDigest = DeviceEncoding.Encode(SHA256.HashData(canonicalBody));
            CryptographicOperations.ZeroMemory(canonicalBody);
            var requestId = Guid.NewGuid().ToString("D");
            var timestamp = DateTimeOffset.UtcNow;
            timestamp = new DateTimeOffset(timestamp.Ticks - timestamp.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
            var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
            using (var confirmation = await identity.SendApiRequestWithTokenDigestAsync(tokenDigest =>
            {
                var proof = new DeviceRequestProof("POST", confirmTarget, pending.Context.AccountId,
                    pending.Context.GenerationId, pending.DeviceId, requestId, timestamp.ToUnixTimeMilliseconds(),
                    tokenDigest, bodyDigest);
                var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(proof);
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, confirmTarget))
                    { Content = JsonContent.Create(confirmBody, options: ApiJson) };
                    request.Headers.Add("X-FG-Request-Id", requestId);
                    request.Headers.Add("X-FG-Timestamp", timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
                    request.Headers.Add("X-FG-Pending-Proof", pending.Material.Identity.Sign("FG-PAIRING-CONFIRM-1", proofBytes));
                    return request;
                }
                finally { CryptographicOperations.ZeroMemory(proofBytes); }
            }, shutdown.Token).ConfigureAwait(false))
            {
                if (!confirmation.IsSuccessStatusCode)
                {
                    // The request may have committed before a transport failure. The
                    // already DPAPI-persisted session is the recovery proof.
                    using var savedPending = await pendingVault.LoadAsync(pending.Context,
                        pending.DeviceId, shutdown.Token).ConfigureAwait(false);
                    _ = await GetCurrentMembershipAsync(pending.Context, savedPending, shutdown.Token).ConfigureAwait(false);
                }
                else
                {
                    var confirmed = await confirmation.Content.ReadFromJsonAsync<ConfirmPairingResult>(ApiJson, shutdown.Token).ConfigureAwait(false);
                    if (confirmed?.State != "Consumed") throw new DeviceEnrollmentException("PairingConfirmationRejected");
                }
            }
            await FinalizePairingVaultAsync(pendingVault, pending.Context, pending.Material,
                membership.ProposedRevision, state.MembershipDigest, shutdown.Token, receivedContentKey,
                deviceSession, membership.KeyEpoch).ConfigureAwait(false);
        }
        finally
        {
            if (receivedContentKey is not null) CryptographicOperations.ZeroMemory(receivedContentKey);
        }
    }

    private async Task FinalizePairingVaultAsync(WindowsDeviceVault pendingVault, DeviceAccountContext context,
        RestoredDeviceMaterial current, long revision, string digest, CancellationToken cancellationToken,
        ReadOnlyMemory<byte>? contentKey = null, string? deviceSession = null, long? keyEpoch = null)
    {
        using var refreshed = await pendingVault.LoadAsync(context, current.Identity.Public.DeviceId, cancellationToken).ConfigureAwait(false);
        var finalVault = GetDeviceVault(context.AccountBinding);
        if (!finalVault.Exists)
        {
            await finalVault.SaveAsync(refreshed.Identity, refreshed.RecoveryKit, context,
                contentKey ?? refreshed.AccountContentKey, revision, digest, cancellationToken,
                deviceSession ?? refreshed.DeviceSession, keyEpoch: keyEpoch ?? refreshed.KeyEpoch).ConfigureAwait(false);
        }
        pendingVault.Delete();
        lock (sync)
        {
            deviceLinked = true;
            deviceLinkPending = false;
            pendingPairingId = null;
            pendingPairingFingerprint = null;
            error = null;
            nextDeviceCheckUtc = DateTimeOffset.UtcNow.AddMinutes(2);
        }
    }

    private async Task<HostPendingPairingSummary[]> LoadPendingPairingsAsync(CancellationToken cancellationToken)
    {
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        using var response = await identity.SendApiRequestAsync(() => new HttpRequestMessage(HttpMethod.Get,
            new Uri(origin, "/v1/bootstrap/pairings/pending")), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        var pending = await response.Content.ReadFromJsonAsync<PendingPairingSummary[]>(ApiJson, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("PairingListUnavailable");
        return pending.Select(item =>
        {
            DeviceEncoding.RequireId(item.PairingId);
            var publicIdentity = new DevicePublicIdentity(item.DeviceId, item.SigningPublicKey, item.EncryptionPublicKey);
            item.Capabilities.Validate();
            if (publicIdentity.Fingerprint != item.Fingerprint) throw new DeviceEnrollmentException("PairingFingerprintInvalid");
            return new HostPendingPairingSummary(item.PairingId, item.DeviceId, item.Capabilities.Platform,
                item.Capabilities.ClientVersion, item.Fingerprint, item.SigningPublicKey, item.EncryptionPublicKey,
                item.ExpiresAtUtc);
        }).ToArray();
    }


    private async Task<PairingStateResponse> ReadOwnPairingAsync(DeviceAccountContext context,
        DeviceIdentity device, string pairingId, CancellationToken cancellationToken)
    {
        DeviceEncoding.RequireId(pairingId);
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        var target = $"/v1/bootstrap/pairings/{pairingId}";
        var requestId = Guid.NewGuid().ToString("D");
        var timestamp = DateTimeOffset.UtcNow;
        timestamp = new DateTimeOffset(timestamp.Ticks - timestamp.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        using var response = await identity.SendApiRequestWithTokenDigestAsync(tokenDigest =>
        {
            var proof = new DeviceRequestProof("GET", target, context.AccountId, context.GenerationId,
                device.Public.DeviceId, requestId, timestamp.ToUnixTimeMilliseconds(), tokenDigest,
                DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
            var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, target));
                request.Headers.Add("X-FG-Request-Id", requestId);
                request.Headers.Add("X-FG-Timestamp", timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
                request.Headers.Add("X-FG-Pending-Proof", device.Sign("FG-PENDING-REQUEST-1", proofBytes));
                return request;
            }
            finally { CryptographicOperations.ZeroMemory(proofBytes); }
        }, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new DeviceEnrollmentException("PairingNotFound");
        if (!response.IsSuccessStatusCode) throw new DeviceEnrollmentException("PairingStateUnavailable");
        return await response.Content.ReadFromJsonAsync<PairingStateResponse>(ApiJson, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("PairingStateUnavailable");
    }

    private static void ValidateOwnPairingState(PairingStateResponse state, string pairingId,
        DevicePublicIdentity expectedIdentity)
    {
        if (!string.Equals(state.PairingId, pairingId, StringComparison.Ordinal) ||
            !string.Equals(state.DeviceId, expectedIdentity.DeviceId, StringComparison.Ordinal))
            throw new DeviceEnrollmentException("PairingStateUnavailable");
        if (!string.Equals(state.Fingerprint, expectedIdentity.Fingerprint, StringComparison.Ordinal))
            throw new DeviceEnrollmentException("PairingFingerprintMismatch");
    }

    private async Task<AccountCloudContext> GetCloudAccountContextAsync(CancellationToken cancellationToken)
    {
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        using var response = await identity.SendApiRequestAsync(() => new HttpRequestMessage(HttpMethod.Get,
            new Uri(origin, "/v1/bootstrap/account")), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        return await response.Content.ReadFromJsonAsync<AccountCloudContext>(ApiJson, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceAccountUnavailable");
    }

    private async Task<MembershipSnapshot> GetCurrentMembershipAsync(DeviceAccountContext context,
        RestoredDeviceMaterial material, CancellationToken cancellationToken)
    {
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        var target = "/v1/bootstrap/membership";
        var requestId = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;
        now = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        using var response = await identity.SendApiRequestWithTokenDigestAsync(accessTokenDigest =>
        {
            var proof = new DeviceRequestProof("GET", target, context.AccountId, context.GenerationId,
                material.Identity.Public.DeviceId, requestId, now.ToUnixTimeMilliseconds(), accessTokenDigest,
                DeviceEncoding.Encode(SHA256.HashData(ReadOnlySpan<byte>.Empty)));
            var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, target));
                request.Headers.Add("X-FG-Device-Id", material.Identity.Public.DeviceId);
                request.Headers.Add("X-FG-Device-Session", material.DeviceSession
                    ?? throw new DeviceEnrollmentException("DeviceSessionRecoveryRequired"));
                request.Headers.Add("X-FG-Request-Id", requestId);
                request.Headers.Add("X-FG-Timestamp", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
                request.Headers.Add("X-FG-Proof", material.Identity.Sign("FG-REQUEST-1", proofBytes));
                return request;
            }
            finally { CryptographicOperations.ZeroMemory(proofBytes); }
        }, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new DeviceEnrollmentException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Conflict => "DeviceMembershipUpdateRequired",
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "DeviceSessionInvalid",
                _ => "DeviceConnectionUnavailable"
            });
        var snapshot = await response.Content.ReadFromJsonAsync<MembershipSnapshot>(ApiJson, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceMembershipUnavailable");
        ValidateMembershipSnapshot(snapshot, context, material);
        return snapshot;
    }

    internal static void ValidateMembershipSnapshot(MembershipSnapshot snapshot, DeviceAccountContext context,
        RestoredDeviceMaterial material)
    {
        if (snapshot is null || snapshot.Membership is null || snapshot.Membership.Members is null ||
            snapshot.Membership.Members.Any(candidate => candidate is null) || snapshot.KeyEnvelopes is null ||
            string.IsNullOrWhiteSpace(snapshot.MembershipDigest))
            throw new DeviceEnrollmentException("DeviceMembershipUpdateRequired");
        var member = snapshot.Membership.Members.SingleOrDefault(candidate => candidate.DeviceId == material.Identity.Public.DeviceId);
        if (!MembershipProtocol.VerifyMembership(snapshot.Membership) ||
            snapshot.Membership.AccountId != context.AccountId || snapshot.Membership.GenerationId != context.GenerationId ||
            snapshot.Membership.ProposedRevision != material.MembershipRevision ||
            snapshot.Membership.KeyEpoch != material.KeyEpoch ||
            MembershipProtocol.Digest(snapshot.Membership) != snapshot.MembershipDigest ||
            snapshot.MembershipDigest != material.MembershipDigest || member?.Status != "Active" ||
            !member.Roles.Contains("Read", StringComparer.Ordinal) ||
            member.SigningPublicKey != material.Identity.Public.SigningPublicKey ||
            member.EncryptionPublicKey != material.Identity.Public.EncryptionPublicKey)
            throw new DeviceEnrollmentException("DeviceMembershipUpdateRequired");

        if (snapshot.MembershipChain is { Length: > 0 } chain)
        {
            try
            {
                _ = new PinnedSettingsAuthor(chain, context,
                    material.MembershipRevision, material.MembershipDigest);
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException or FormatException or InvalidDataException)
            {
                throw new DeviceEnrollmentException("DeviceMembershipUpdateRequired");
            }
        }
    }

    private async Task<MembershipSnapshot> EnsureDeviceSessionAsync(WindowsDeviceVault deviceVault,
        WindowsDeviceVault.LoadedDeviceVault loaded, CancellationToken cancellationToken)
    {
        if (loaded.Material.DeviceSession is not null)
        {
            try { return await GetCurrentMembershipAsync(loaded.Context, loaded.Material, cancellationToken).ConfigureAwait(false); }
            catch (DeviceEnrollmentException exception) when (exception.Code == "DeviceSessionInvalid") { }
        }
        var sessionBytes = RandomNumberGenerator.GetBytes(32);
        string session;
        try { session = DeviceEncoding.Encode(sessionBytes); }
        finally { CryptographicOperations.ZeroMemory(sessionBytes); }
        await deviceVault.UpdateDeviceSessionAsync(loaded.Context, loaded.Material.Identity.Public,
            loaded.Material.MembershipRevision, loaded.Material.MembershipDigest, session, cancellationToken).ConfigureAwait(false);

        const string target = "/v1/bootstrap/sessions/renew";
        var body = new RenewDeviceSessionRequest(loaded.Context.AccountId, loaded.Context.GenerationId,
            loaded.DeviceId, loaded.Material.MembershipDigest, session);
        var bodyBytes = MembershipProtocol.CanonicalRenewDeviceSessionBody(body);
        string bodyDigest;
        try { bodyDigest = DeviceEncoding.Encode(SHA256.HashData(bodyBytes)); }
        finally { CryptographicOperations.ZeroMemory(bodyBytes); }
        var requestId = Guid.NewGuid().ToString("D");
        var timestamp = DateTimeOffset.UtcNow;
        timestamp = new DateTimeOffset(timestamp.Ticks - timestamp.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        using var response = await identity.SendApiRequestWithTokenDigestAsync(tokenDigest =>
        {
            var proof = new DeviceRequestProof("POST", target, loaded.Context.AccountId, loaded.Context.GenerationId,
                loaded.DeviceId, requestId, timestamp.ToUnixTimeMilliseconds(), tokenDigest, bodyDigest);
            var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(proof);
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, target))
                { Content = JsonContent.Create(body, options: ApiJson) };
                request.Headers.Add("X-FG-Request-Id", requestId);
                request.Headers.Add("X-FG-Timestamp", timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
                request.Headers.Add("X-FG-Proof", loaded.Material.Identity.Sign("FG-DEVICE-SESSION-RENEW-1", proofBytes));
                return request;
            }
            finally { CryptographicOperations.ZeroMemory(proofBytes); }
        }, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new DeviceEnrollmentException(response.StatusCode == System.Net.HttpStatusCode.Conflict
                ? "DeviceMembershipUpdateRequired" : "DeviceConnectionUnavailable");
        var renewed = await response.Content.ReadFromJsonAsync<RenewDeviceSessionResponse>(ApiJson, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceConnectionUnavailable");
        ValidateMembershipSnapshot(renewed.Snapshot, loaded.Context, loaded.Material);
        if (renewed.ExpiresAtUtc <= DateTimeOffset.UtcNow || renewed.ExpiresAtUtc > DateTimeOffset.UtcNow.AddHours(8).AddMinutes(2))
            throw new DeviceEnrollmentException("DeviceConnectionUnavailable");
        using var refreshed = await deviceVault.LoadAsync(loaded.Context, loaded.DeviceId, cancellationToken).ConfigureAwait(false);
        return await GetCurrentMembershipAsync(loaded.Context, refreshed, cancellationToken).ConfigureAwait(false);
    }

    private async Task ShareSelectedSettingsAsync()
    {
        var store = settingsStore ?? throw new DeviceEnrollmentException("AccountSettingsUnavailable");
        var binding = identity.AccountBinding ?? throw new DeviceEnrollmentException("SignInRequired");
        using var loaded = await GetDeviceVault(binding).LoadForAccountAsync(
            configuration.Authority.AbsoluteUri.TrimEnd('/'), binding, shutdown.Token).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DevicePairingRequired");
        var membership = await GetCurrentMembershipAsync(loaded.Context, loaded.Material, shutdown.Token).ConfigureAwait(false);
        RequireSettingsMember(membership.Membership, loaded.DeviceId, "WriteSettings");
        RequireSettingsMember(membership.Membership, loaded.DeviceId, "Read");
        if (loaded.Material.DeviceSession is null) throw new DeviceEnrollmentException("DeviceSessionRecoveryRequired");

        Func<DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>>? presenter;
        lock (sync) presenter = settingsSharePresenter;
        if (presenter is null) throw new DeviceEnrollmentException("AccountSettingsUnavailable");
        var local = store.Load();
        var selection = await presenter(local, shutdown.Token).ConfigureAwait(false);
        if (selection is null || !selection.HasAny)
        {
            lock (sync) settingsTransferStatus = "Cancelled";
            return;
        }

        var existing = await ReadSharedSettingsDocumentAsync(loaded.Context, loaded.Material,
            membership, shutdown.Token).ConfigureAwait(false);
        var payload = SharedDesktopSettings.Merge(existing?.Settings, local, selection);
        var clearJson = payload.ToCanonicalJson();
        byte[]? body = null;
        try
        {
            var revision = existing?.Revision ?? 0;
            var documentContext = new ContentDocumentContext(loaded.Context.AccountId, loaded.Context.GenerationId,
                SharedSettingsEntityId, SharedSettingsKind, revision, revision + 1, loaded.Material.KeyEpoch,
                loaded.Material.MembershipRevision);
            var document = ContentDocumentCodec.Encrypt(clearJson,
                loaded.Material.GetContentKey(loaded.Material.KeyEpoch).Span, documentContext, loaded.Material.Identity);
            body = ContentDocumentCodec.CanonicalDocumentBody(document);
            using var response = await SendSettingsDocumentRequestAsync(HttpMethod.Post, loaded.Context,
                loaded.Material, body, shutdown.Token).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                var code = await ReadApiErrorCodeAsync(response, shutdown.Token).ConfigureAwait(false);
                throw new DeviceEnrollmentException(code == "DocumentRevisionConflict"
                    ? "AccountSettingsConflict" : "DeviceMembershipUpdateRequired");
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                throw new DeviceEnrollmentException("AccountSettingsPermissionRequired");
            if (!response.IsSuccessStatusCode)
                throw new DeviceEnrollmentException("AccountSettingsUnavailable");
            var saved = await response.Content.ReadFromJsonAsync<EncryptedDocumentWriteResult>(ApiJson, shutdown.Token)
                .ConfigureAwait(false) ?? throw new DeviceEnrollmentException("AccountSettingsUnavailable");
            if (saved.State != "Stored" || saved.Revision != documentContext.ProposedRevision ||
                saved.EntityId != SharedSettingsEntityId)
                throw new DeviceEnrollmentException("AccountSettingsUnavailable");
            lock (sync) { settingsTransferStatus = "Shared"; error = null; }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearJson);
            if (body is not null) CryptographicOperations.ZeroMemory(body);
        }
    }

    private async Task ImportSelectedSettingsAsync()
    {
        var store = settingsStore ?? throw new DeviceEnrollmentException("AccountSettingsUnavailable");
        var binding = identity.AccountBinding ?? throw new DeviceEnrollmentException("SignInRequired");
        using var loaded = await GetDeviceVault(binding).LoadForAccountAsync(
            configuration.Authority.AbsoluteUri.TrimEnd('/'), binding, shutdown.Token).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DevicePairingRequired");
        var membership = await GetCurrentMembershipAsync(loaded.Context, loaded.Material, shutdown.Token).ConfigureAwait(false);
        RequireSettingsMember(membership.Membership, loaded.DeviceId, "Read");
        if (loaded.Material.DeviceSession is null) throw new DeviceEnrollmentException("DeviceSessionRecoveryRequired");
        var shared = await ReadSharedSettingsDocumentAsync(loaded.Context, loaded.Material,
            membership, shutdown.Token).ConfigureAwait(false);
        if (shared is null)
        {
            lock (sync) settingsTransferStatus = "NoSharedSettings";
            return;
        }

        Func<SharedDesktopSettings, DesktopSettings, CancellationToken, Task<SettingsTransferSelection?>>? presenter;
        lock (sync) presenter = settingsImportPresenter;
        if (presenter is null) throw new DeviceEnrollmentException("AccountSettingsUnavailable");
        var local = store.Load();
        var selection = await presenter(shared.Settings, local, shutdown.Token).ConfigureAwait(false);
        if (selection is null || !selection.HasAny)
        {
            lock (sync) settingsTransferStatus = "Cancelled";
            return;
        }
        var updated = shared.Settings.ApplyTo(local, selection);
        store.Save(updated);
        var persisted = store.Load();
        if (selection.Theme && persisted.Theme != updated.Theme ||
            selection.Language && persisted.UiLanguage != updated.UiLanguage ||
            selection.Duration && persisted.LastDurationMinutes != updated.LastDurationMinutes)
            throw new DeviceEnrollmentException("AccountSettingsSaveFailed");
        lock (sync) { settingsTransferStatus = "Imported"; error = null; }
    }

    private async Task<SharedSettingsDocument?> ReadSharedSettingsDocumentAsync(DeviceAccountContext context,
        RestoredDeviceMaterial material, MembershipSnapshot membership, CancellationToken cancellationToken)
    {
        using var response = await SendSettingsDocumentRequestAsync(HttpMethod.Get, context, material,
            null, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new DeviceEnrollmentException("DeviceMembershipUpdateRequired");
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            throw new DeviceEnrollmentException("AccountSettingsPermissionRequired");
        if (!response.IsSuccessStatusCode) throw new DeviceEnrollmentException("AccountSettingsUnavailable");
        var stored = await response.Content.ReadFromJsonAsync<EncryptedDocumentResult>(ApiJson, cancellationToken)
            .ConfigureAwait(false) ?? throw new DeviceEnrollmentException("AccountSettingsUnavailable");
        var document = stored.Document;
        if (stored.Revision != document.ProposedRevision || document.AccountId != context.AccountId ||
            document.GenerationId != context.GenerationId || document.EntityId != SharedSettingsEntityId ||
            document.Kind != SharedSettingsKind || document.ProposedRevision < 1 || document.KeyEpoch > membership.Membership.KeyEpoch)
            throw new DeviceEnrollmentException("AccountSettingsDocumentInvalid");
        if (membership.MembershipChain is not { Length: > 0 } chain)
            throw new DeviceEnrollmentException("AccountSettingsDocumentInvalid");
        DevicePublicIdentity authorIdentity;
        try
        {
            var authors = new PinnedSettingsAuthor(chain, context,
                material.MembershipRevision, material.MembershipDigest);
            authorIdentity = authors.Resolve(document.MembershipRevision, document.AuthorDeviceId);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or FormatException or InvalidDataException)
        {
            throw new DeviceEnrollmentException("AccountSettingsDocumentInvalid");
        }
        var documentContext = new ContentDocumentContext(document.AccountId, document.GenerationId, document.EntityId,
            document.Kind, document.BaseRevision, document.ProposedRevision, document.KeyEpoch,
            document.MembershipRevision);
        byte[]? clearJson = null;
        try
        {
            clearJson = ContentDocumentCodec.Decrypt(document, documentContext,
                material.GetContentKey(document.KeyEpoch).Span, authorIdentity);
            return new(stored.Revision, SharedDesktopSettings.Parse(clearJson));
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or FormatException)
        {
            throw new DeviceEnrollmentException("AccountSettingsDocumentInvalid");
        }
        finally
        {
            if (clearJson is not null) CryptographicOperations.ZeroMemory(clearJson);
        }
    }

    private async Task<HttpResponseMessage> SendSettingsDocumentRequestAsync(HttpMethod method,
        DeviceAccountContext context, RestoredDeviceMaterial material, byte[]? canonicalBody,
        CancellationToken cancellationToken)
    {
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        var target = $"/v1/documents/{SharedSettingsEntityId}";
        var requestId = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;
        now = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        var bodyHash = canonicalBody is null
            ? SHA256.HashData(ReadOnlySpan<byte>.Empty)
            : SHA256.HashData(canonicalBody);
        var bodyDigest = DeviceEncoding.Encode(bodyHash);
        CryptographicOperations.ZeroMemory(bodyHash);
        var requestBody = canonicalBody?.ToArray();
        try
        {
            return await identity.SendApiRequestWithTokenDigestAsync(accessTokenDigest =>
            {
                var proof = new DeviceRequestProof(method.Method, target, context.AccountId, context.GenerationId,
                    material.Identity.Public.DeviceId, requestId, now.ToUnixTimeMilliseconds(), accessTokenDigest, bodyDigest);
                var proofBytes = MembershipProtocol.CanonicalDeviceRequestProof(proof);
                try
                {
                    var request = new HttpRequestMessage(method, new Uri(origin, target));
                    request.Headers.Add("X-FG-Device-Id", material.Identity.Public.DeviceId);
                    request.Headers.Add("X-FG-Device-Session", material.DeviceSession
                        ?? throw new DeviceEnrollmentException("DeviceSessionRecoveryRequired"));
                    request.Headers.Add("X-FG-Request-Id", requestId);
                    request.Headers.Add("X-FG-Timestamp", now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
                    request.Headers.Add("X-FG-Proof", material.Identity.Sign("FG-REQUEST-1", proofBytes));
                    if (requestBody is not null)
                    {
                        request.Content = new ByteArrayContent(requestBody);
                        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                    }
                    return request;
                }
                finally { CryptographicOperations.ZeroMemory(proofBytes); }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (requestBody is not null) CryptographicOperations.ZeroMemory(requestBody);
        }
    }

    private static DeviceMember RequireSettingsMember(MembershipDocument membership, string deviceId, string role)
    {
        var member = membership.Members.SingleOrDefault(candidate => candidate.DeviceId == deviceId);
        if (member?.Status != "Active" || !member.Roles.Contains(role, StringComparer.Ordinal))
            throw new DeviceEnrollmentException("AccountSettingsPermissionRequired");
        return member;
    }

    private static async Task<string?> ReadApiErrorCodeAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return json.RootElement.TryGetProperty("error", out var code) && code.ValueKind == JsonValueKind.String
                ? code.GetString() : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        { return null; }
    }

    private async Task RegisterFirstDeviceAsync(DeviceAccountContext context, DeviceIdentity device, RecoveryKit recovery,
        ReadOnlyMemory<byte> contentKey, MembershipDocument membership, string deviceSession,
        CancellationToken cancellationToken)
    {
        var origin = configuration.ApiOrigin ?? throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        var challengeRequest = new BootstrapChallengeRequest(context.AccountId, context.GenerationId,
            device.Public.DeviceId, device.Public.SigningPublicKey, device.Public.EncryptionPublicKey,
            recovery.Public.SigningPublicKey, recovery.Public.EncryptionPublicKey);
        using var challengeResponse = await identity.SendApiRequestAsync(() => new HttpRequestMessage(HttpMethod.Post,
            new Uri(origin, "/v1/bootstrap/challenges"))
        { Content = JsonContent.Create(challengeRequest, options: ApiJson) },
            cancellationToken).ConfigureAwait(false);
        if (challengeResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new DeviceEnrollmentException("DeviceLinkConflict");
        if (challengeResponse.StatusCode != System.Net.HttpStatusCode.Created)
            throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<BootstrapChallenge>(ApiJson, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceChallengeUnavailable");
        if (challenge.Purpose != "initial" || challenge.AccountId != context.AccountId ||
            challenge.GenerationId != context.GenerationId || challenge.DeviceId != device.Public.DeviceId ||
            challenge.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new DeviceEnrollmentException("DeviceChallengeRejected");

        var proof = new BootstrapChallengeProof(challenge.ChallengeId, context.AccountId,
            context.GenerationId, device.Public.DeviceId, challenge.Nonce);
        var canonicalProof = MembershipProtocol.CanonicalChallenge(proof);
        string proofSignature;
        try { proofSignature = device.Sign(MembershipProtocol.ChallengeProofDomain, canonicalProof); }
        finally { CryptographicOperations.ZeroMemory(canonicalProof); }
        var membershipDigest = MembershipProtocol.Digest(membership);
        var deviceEnvelopeContext = new KeyEnvelopeContext(context.AccountId, context.GenerationId, 1,
            device.Public.DeviceId, membershipDigest, device.Public.DeviceId);
        var recoveryEnvelopeContext = deviceEnvelopeContext with { RecipientId = context.AccountId };
        var envelopes = new[]
        {
            SignedKeyEnvelopeCodec.SealForDevice(contentKey.Span, deviceEnvelopeContext,
                device.Public.EncryptionPublicKey, device),
            SignedKeyEnvelopeCodec.SealForDevice(contentKey.Span, recoveryEnvelopeContext,
                recovery.Public.EncryptionPublicKey, device)
        };
        var body = new { challengeId = challenge.ChallengeId, proof, proofSignature, deviceSession, membership, keyEnvelopes = envelopes };
        using var registerResponse = await identity.SendApiRequestAsync(() => new HttpRequestMessage(HttpMethod.Post,
            new Uri(origin, "/v1/bootstrap/accounts"))
        { Content = JsonContent.Create(body, options: ApiJson) },
            cancellationToken).ConfigureAwait(false);
        if (registerResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
            throw new DeviceEnrollmentException("DeviceLinkConflict");
        if (registerResponse.StatusCode != System.Net.HttpStatusCode.Created)
            throw new DeviceEnrollmentException("DeviceCloudUnavailable");
        var result = await registerResponse.Content.ReadFromJsonAsync<BootstrapAccountResult>(ApiJson, cancellationToken).ConfigureAwait(false)
            ?? throw new DeviceEnrollmentException("DeviceRegistrationUnavailable");
        if (!result.DeviceLinked || result.AccountId != context.AccountId || result.GenerationId != context.GenerationId ||
            result.Revision != 1)
            throw new DeviceEnrollmentException("DeviceRegistrationRejected");
    }

    private static MembershipDocument CreateInitialMembership(DeviceAccountContext context,
        DeviceIdentity device, RecoveryKit recovery)
    {
        var membership = new MembershipDocument("FG-MEMBERSHIP-1", context.AccountId, context.GenerationId,
            0, 1, 1, DeviceEncoding.Encode(new byte[32]),
            [new DeviceMember(device.Public.DeviceId, device.Public.SigningPublicKey,
                device.Public.EncryptionPublicKey, "Active", ["ManageDevices", "Read", "StartNormal", "StartStrict", "WriteSettings"])],
            recovery.Public.SigningPublicKey, recovery.Public.EncryptionPublicKey,
            device.Public.DeviceId, DeviceEncoding.Encode(new byte[64]));
        var canonical = MembershipProtocol.CanonicalMembership(membership);
        try { return membership with { Signature = device.Sign("FG-MEMBERSHIP-1", canonical) }; }
        finally { CryptographicOperations.ZeroMemory(canonical); }
    }

    private sealed class DeviceEnrollmentException(string code) : Exception
    {
        public string Code { get; } = code;
    }

    public void Dispose()
    {
        Task? restore; Task? login; Task? logout; Task? deviceCheck; Task? deviceLink; Task? settingsSync;
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            shutdown.Cancel();
            loginCancellation?.Cancel();
            restore = restoreTask; login = loginTask; logout = logoutTask;
            deviceCheck = deviceCheckTask; deviceLink = deviceLinkTask;
            settingsSync = settingsSyncTask;
        }
        try { Task.WaitAll(new Task?[] { restore, login, logout, deviceCheck, deviceLink, settingsSync }.Where(task => task is not null).Cast<Task>().ToArray(), TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
        try { DisposeSettingsSyncSessionAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception exception) when (IsSettingsSyncFailure(exception) || exception is OperationCanceledException) { }
        identity.Dispose();
        settingsSyncLifecycle.Dispose();
        shutdown.Dispose();
    }
}
