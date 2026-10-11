using FocusGuard.Client;
using FocusGuard.Client.Journal;
using FocusGuard.Client.Platform;
using FocusGuard.Platform.Windows;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Hosting;

/// <summary>The services one host process shares between its main window and its popups.</summary>
internal sealed record HostServices(
    IFocusControlClient Client,
    IHostControlAuthorization Authorization,
    IProfileStore? ProfileStore = null,
    IApplicationCatalog? Catalog = null,
    IApplicationIconProvider? Icons = null,
    FocusGuard.Client.Preferences.DesktopSettingsStore? Settings = null,
    SessionJournalStore? Journal = null,
    IDisposable? Pipes = null) : IDisposable
{
    public void Dispose() => Pipes?.Dispose();
}

internal static class HostComposition
{
    public static MainForm CreateDefault(HostSmokeScenario smokeScenario, bool pilotEnabled = false)
    {
        var services = CreateServices(smokeScenario, pilotEnabled);
        var form = CreateMainForm(services, smokeScenario);
        form.FormClosed += (_, _) => services.Dispose();
        return form;
    }

    public static HostServices CreateServices(HostSmokeScenario smokeScenario, bool pilotEnabled)
    {
#if FOCUSGUARD_PRODUCTION
        if (smokeScenario == HostSmokeScenario.None)
        {
            try
            {
                var factory = new FocusGuard.Ipc.Windows.VerifiedPipeConnectionFactory(AppContext.BaseDirectory,
                    FocusGuard.Ipc.Windows.IpcClientRole.WebViewHost);
                try
                {
                    var settingsStore = new FocusGuard.Client.Preferences.DesktopSettingsStore();
                    var journalStore = new SessionJournalStore(settingsStore.Persists
                        ? Path.Combine(settingsStore.DataDirectory, "session-journal.json") : null);
                    return new HostServices(new NamedPipeFocusControlClient(connectionFactory: factory),
                        new VerifiedNativeHostAuthorization(pilotEnabled),
                        new SqliteProfileStore(Path.Combine(Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "profiles.db")),
                        new WindowsApplicationCatalog(), new WindowsApplicationIconProvider(),
                        settingsStore, journalStore, factory);
                }
                catch
                {
                    factory.Dispose();
                    throw;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or System.Security.SecurityException or System.ComponentModel.Win32Exception)
            {
                // A copied/unprovisioned host remains a local shell with no control access.
            }
        }
#endif
        return new HostServices(new NamedPipeFocusControlClient(), new Phase4AuthorizationNotIntegrated());
    }

    public static MainForm CreateMainForm(HostServices services, HostSmokeScenario smokeScenario) =>
        new(services.Client, services.Authorization, smokeScenario, services.ProfileStore, services.Catalog, services.Icons,
            services.Settings, services.Journal);

    /// <summary>A popup's bridge: same client and authorization as the main window, plus the shell commands, never window commands.</summary>
    public static FocusGuard.Host.Windows.Bridge.NativeBridgeDispatcher CreateSurfaceBridge(HostServices services, DocumentLifetime lifetime,
        IHostShellController shell) =>
        new(services.Client, services.Authorization, lifetime, profileStore: services.ProfileStore,
            applicationCatalog: services.Catalog, iconProvider: services.Icons, settingsStore: services.Settings,
            sessionJournalStore: services.Journal, shellController: shell);
}
internal sealed class VerifiedNativeHostAuthorization(bool pilotEnabled = false) : IHostControlAuthorization
{
    public HostAuthorizationDecision Authorize(HostControlOperation operation) =>
        operation is HostControlOperation.ReadStatus or HostControlOperation.ReadProfiles
            || pilotEnabled && operation is HostControlOperation.PrepareNormal or HostControlOperation.StartNormal or HostControlOperation.WriteProfiles or HostControlOperation.ManageNormal
            ? new(HostAuthorizationState.Authorized, "NativeDeploymentVerified")
            : new(HostAuthorizationState.Denied, "OperationDenied");
}

internal enum HostSmokeScenario
{
    None,
    LocalLoad,
    RuntimeMissing,
    RendererRecovery,
    RendererCrash,
    WindowDpiContract,
    WindowPresentation
}

internal enum HostControlOperation
{
    ReadStatus,
    ReadProfiles,
    PrepareNormal,
    StartNormal,
    WriteProfiles,
    ManageNormal
}

internal enum HostAuthorizationState
{
    NotIntegrated,
    Denied,
    Authorized
}

internal readonly record struct HostAuthorizationDecision(
    HostAuthorizationState State,
    string ReasonCode);

internal interface IHostControlAuthorization
{
    HostAuthorizationDecision Authorize(HostControlOperation operation);
}

/// <summary>
/// Fail-closed composition default. Replace only when Phase 4 provides a verified
/// native authorization result; the page cannot change this state.
/// </summary>
internal sealed class Phase4AuthorizationNotIntegrated : IHostControlAuthorization
{
    public HostAuthorizationDecision Authorize(HostControlOperation operation) =>
        new(HostAuthorizationState.NotIntegrated, "Phase4AuthorizationUnavailable");
}
