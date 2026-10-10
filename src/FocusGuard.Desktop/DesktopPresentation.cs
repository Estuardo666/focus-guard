using System.Windows.Input;
using FocusGuard.Client.Preferences;

namespace FocusGuard.Desktop;

/// <summary>
/// The repo's other view models each carry a private DelegateCommand that never raises
/// CanExecuteChanged. The block and summary screens do need to re-evaluate — a capture
/// button stops being available once the capture is saved — so they share this one.
/// </summary>
public sealed class NotifyingDelegateCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => canExecute();

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
        {
            execute();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class SessionEndedEventArgs(Guid sessionId) : EventArgs
{
    public Guid SessionId { get; } = sessionId;
}

public sealed class ApplicationBlockedEventArgs(string target) : EventArgs
{
    /// <summary>Sanitized by the service to a file name, never a full path.</summary>
    public string Target { get; } = target;
}

/// <summary>One row in the Settings picker for how a session should announce its end.</summary>
public sealed record SessionEndOptionViewModel(SessionEndPresentation Value, string Label)
{
    public override string ToString() => Label;
}

