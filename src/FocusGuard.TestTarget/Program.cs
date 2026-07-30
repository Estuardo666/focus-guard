using System.Globalization;

var lifetime = ParseLifetime(args);
using var shutdown = new CancellationTokenSource();
if (lifetime is not null)
{
    shutdown.CancelAfter(lifetime.Value);
}

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    Console.WriteLine("Normal shutdown requested.");
    shutdown.Cancel();
};

Console.WriteLine("FocusGuard Test Target");
Console.WriteLine(FormattableString.Invariant($"PID: {Environment.ProcessId}"));
Console.WriteLine("Status: Running");
Console.WriteLine("Mode: cooperative");
Console.WriteLine("Press Ctrl+C to exit normally.");

try
{
    while (!shutdown.IsCancellationRequested)
    {
        Console.WriteLine(FormattableString.Invariant($"Heartbeat: {DateTimeOffset.Now:HH:mm:ss}"));
        await Task.Delay(TimeSpan.FromSeconds(1), shutdown.Token);
    }
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}

Console.WriteLine("Status: Exited normally");

static TimeSpan? ParseLifetime(string[] arguments)
{
    for (var index = 0; index < arguments.Length; index++)
    {
        if (string.Equals(arguments[index], "--mode", StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= arguments.Length
                || !string.Equals(arguments[++index], "cooperative", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Only '--mode cooperative' is supported.");
            }
        }
        else if (string.Equals(arguments[index], "--lifetime", StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= arguments.Length
                || !int.TryParse(arguments[++index], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || seconds is < 1 or > 3600)
            {
                throw new ArgumentException("Lifetime must be between 1 and 3600 seconds.");
            }

            return TimeSpan.FromSeconds(seconds);
        }
        else
        {
            throw new ArgumentException($"Unknown argument: {arguments[index]}");
        }
    }

    return null;
}
