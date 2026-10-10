using FocusGuard.Client;
using FocusGuard.Client.Journal;
using FocusGuard.Platform.Windows;
using FocusGuard.Storage;

namespace FocusGuard.Host.Windows.Hosting;

internal static class HostComposition
{
    public static MainForm CreateDefault(HostSmokeScenario smokeScenario, bool pilotEnabled = false)
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
                    var form = new MainForm(new NamedPipeFocusControlClient(connectionFactory: factory),
                        new VerifiedNativeHostAuthorization(pilotEnabled), smokeScenario,
                        new SqliteProfileStore(Path.Combine(Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData), "FocusGuard", "profiles.db")),
                        new WindowsApplicationCatalog(), new WindowsApplicationIconProvider(),
                        settingsStore, journalStore);
                    form.FormClosed += (_, _) => factory.Dispose();
                    return form;
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
        return new(
        new NamedPipeFocusControlClient(),
        new Phase4AuthorizationNotIntegrated(),
        smokeScenario);
    }
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
    WindowDpiContract
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
