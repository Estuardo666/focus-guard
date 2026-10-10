using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop;

public sealed partial class FocusViewModel
{
    public bool IsSessionStateFresh => _connectionSnapshot.IsFresh;
    public bool IsSessionStateStale => _connectionSnapshot.IsStale;
    public bool HasConfirmedSessionState => _connectionSnapshot.HasConfirmedStatus;
    public DateTimeOffset? LastConfirmedStatusUtc => _connectionSnapshot.LastConfirmedUtc;

    public string ConnectionNotice => _connectionSnapshot.Connection == FocusGuard.Client.State.SessionConnectionState.Unknown
        ? UiStrings.Get("SessionStateChecking")
        : IsSessionStateStale
            ? UiStrings.Get("SessionStateStale")
            : UiStrings.Get("SessionStateUnknown");

    private void UpdateConnectionSnapshot()
    {
        var previous = _connectionSnapshot;
        _connectionSnapshot = _sessionWorkflow.ConnectionSnapshot;
        if (previous.Connection == _connectionSnapshot.Connection
            && previous.IsFresh == _connectionSnapshot.IsFresh
            && previous.HasConfirmedStatus == _connectionSnapshot.HasConfirmedStatus) return;

        OnPropertyChanged(nameof(IsSessionStateFresh));
        OnPropertyChanged(nameof(IsSessionStateStale));
        OnPropertyChanged(nameof(HasConfirmedSessionState));
        OnPropertyChanged(nameof(LastConfirmedStatusUtc));
        OnPropertyChanged(nameof(ConnectionNotice));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanConfirmStrict));
        OnPropertyChanged(nameof(CanEditProfileRules));
        OnPropertyChanged(nameof(CanRenameProfile));
        OnPropertyChanged(nameof(CanDeleteProfile));
        OnPropertyChanged(nameof(CanConfirmProfileDelete));
        OnPropertyChanged(nameof(CanCreateProfile));
        OnPropertyChanged(nameof(CanEditCyclePreset));
        OnPropertyChanged(nameof(CanCommitCyclePreset));
        OnPropertyChanged(nameof(CanSaveCyclePreset));
        OnPropertyChanged(nameof(CanDeleteCyclePreset));
        OnPropertyChanged(nameof(CanEditBreakExceptions));
        OnPropertyChanged(nameof(IsMotionActive));
        OnPropertyChanged(nameof(CompactHasWarning));
        OnPropertyChanged(nameof(CompactWarningText));
        NotifyStopPresentationChanged();
        NotifyEnforcementPresentationChanged();
        RefreshRuleCommands();
        NotifyCommands();
        _retryProtectionCommand.RaiseCanExecuteChanged();
    }

    private void PresentUnavailableConnection()
    {
        // Preserve active/strict/phase/deadline/remaining and the running-session
        // identity. A transport failure is not evidence of stop, expiry or release.
        _isServiceReady = false;
        _isEnforcementConnected = false;
        ServiceHealthLabel = UiStrings.Get("ServiceUnavailable");
        StatusMessage = ConnectionNotice;
        NotifyEnforcementPresentationChanged();
        OnPropertyChanged(nameof(CompactHasWarning));
        OnPropertyChanged(nameof(CompactWarningText));
        _retryProtectionCommand.RaiseCanExecuteChanged();
    }

    private void MarkUnconfirmedMutation(Exception exception)
    {
        if (exception is not (IOException or TimeoutException or OperationCanceledException)) return;
        _sessionWorkflow.InvalidateConnection();
        UpdateConnectionSnapshot();
        PresentUnavailableConnection();
        StatusMessage = UiStrings.Get("SessionActionUnconfirmed");
    }
}
