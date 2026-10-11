namespace FocusGuard.Setup;

/// <summary>Launching a process is not proof that the owner's window opened. Keep Setup until presentation succeeds.</summary>
internal sealed class InstalledAppActivation(Action activate, Func<bool> isPresented, TimeProvider? timeProvider = null)
{
    internal async Task<bool> OpenAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var time = timeProvider ?? TimeProvider.System;
        cancellationToken.ThrowIfCancellationRequested();
        var start = time.GetTimestamp();
        var retried = false;
        activate();
        while (time.GetElapsedTime(start) < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (isPresented()) return true;
            if (!retried && time.GetElapsedTime(start) >= timeout / 2) { retried = true; activate(); }
            await Task.Delay(TimeSpan.FromMilliseconds(50), time, cancellationToken);
        }
        return isPresented();
    }
}
