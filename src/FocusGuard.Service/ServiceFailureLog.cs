using System.Text.Json;

namespace FocusGuard.Service;

internal static class ServiceFailureLog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly object RejectionGate = new();
    private static long? LastRejection;

    public static async Task WriteIpcRejectionAsync(Exception exception)
    {
        // One overwritten snapshot, at most once per ten seconds even under a request flood.
        lock (RejectionGate)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (LastRejection is long previous && System.Diagnostics.Stopwatch.GetElapsedTime(previous, now) < TimeSpan.FromSeconds(10))
                return;
            LastRejection = now;
        }
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "FocusGuard", "diagnostics");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "ipc-control-rejection.json"),
                JsonSerializer.Serialize(new
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Failure = FocusGuard.Ipc.Windows.IpcFailureDiagnostic.FromException(exception)
                })).ConfigureAwait(false);
        }
        catch (Exception loggingException) when (loggingException is IOException or UnauthorizedAccessException) { }
    }

    public static async Task WriteAsync(
        string worker,
        Exception exception,
        CancellationToken cancellationToken = default,
        string? diagnosticsDirectory = null)
    {
        try
        {
            var directory = diagnosticsDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "FocusGuard",
                "diagnostics");
            Directory.CreateDirectory(directory);
            var methodTrace = new System.Diagnostics.StackTrace(exception, false).GetFrames()?
                .Select(frame => frame.GetMethod())
                .Where(method => method is not null)
                .Select(method => method!.DeclaringType?.FullName is { } typeName
                    ? $"{typeName}.{method.Name}"
                    : method!.Name)
                .Take(16)
                .ToArray() ?? [];
            var record = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                worker,
                exceptionType = exception.GetType().FullName,
                exception.HResult,
                methodTrace
            });
            await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await File.AppendAllTextAsync(
                    Path.Combine(directory, "service-failures.jsonl"),
                    record + Environment.NewLine,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }
        }
        catch (Exception loggingException) when (
            loggingException is IOException or UnauthorizedAccessException)
        {
            // Never replace the original worker failure with diagnostic failure.
        }
    }

}
