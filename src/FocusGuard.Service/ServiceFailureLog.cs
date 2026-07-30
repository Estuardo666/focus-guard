using System.Text.Json;

namespace FocusGuard.Service;

internal static class ServiceFailureLog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task WriteAsync(
        string worker,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "FocusGuard",
                "diagnostics");
            Directory.CreateDirectory(directory);
            var record = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                worker,
                exceptionType = exception.GetType().FullName,
                exception.HResult
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
