using System.Text;
using System.Text.Json;
using FocusGuard.Contracts;

namespace FocusGuard.Service;

public interface IFocusAuditStore
{
    Task WriteAsync(FocusAuditEvent auditEvent, CancellationToken cancellationToken);

    Task<IReadOnlyList<FocusAuditEvent>> ReadRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken);
}

public sealed class JsonLinesFocusAuditStore : IFocusAuditStore, IDisposable
{
    public const int MaximumTargetLength = 512;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonLinesFocusAuditStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public async Task WriteAsync(FocusAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        Validate(auditEvent);
        var sanitized = auditEvent with { Target = SanitizeTarget(auditEvent.Kind, auditEvent.Target) };
        var line = JsonSerializer.Serialize(sanitized, JsonOptions) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(_path, line, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<FocusAuditEvent>> ReadRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount is <= 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return Array.Empty<FocusAuditEvent>();
            }

            var events = new List<FocusAuditEvent>();
            foreach (var line in await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var item = JsonSerializer.Deserialize<FocusAuditEvent>(line, JsonOptions);
                    if (item is not null)
                    {
                        Validate(item);
                        events.Add(item);
                    }
                }
                catch (JsonException)
                {
                    // A torn/corrupt record is ignored; audit visibility must not
                    // prevent enforcement or recovery from failing open.
                }
                catch (InvalidDataException)
                {
                }
            }

            return events
                .OrderByDescending(item => item.TimestampUtc)
                .Take(maximumCount)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string SanitizeTarget(FocusAuditEventKind kind, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        string sanitized;
        if (kind == FocusAuditEventKind.WebsiteBlocked)
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || string.IsNullOrWhiteSpace(uri.Host))
            {
                throw new InvalidDataException("Website audit target must be an HTTP(S) URL.");
            }

            sanitized = $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}{uri.AbsolutePath}";
        }
        else if (kind == FocusAuditEventKind.ApplicationBlocked)
        {
            sanitized = Path.GetFileName(target);
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                throw new InvalidDataException("Application audit target must contain a file name.");
            }
        }
        else
        {
            sanitized = target.Trim();
        }

        return sanitized.Length <= MaximumTargetLength
            ? sanitized
            : sanitized[..MaximumTargetLength];
    }

    public void Dispose() => _gate.Dispose();

    private static void Validate(FocusAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        if (auditEvent.Id == Guid.Empty
            || auditEvent.TimestampUtc.Offset != TimeSpan.Zero
            || !Enum.IsDefined(auditEvent.Kind)
            || string.IsNullOrWhiteSpace(auditEvent.Target)
            || string.IsNullOrWhiteSpace(auditEvent.Outcome)
            || auditEvent.Outcome.Length > 80)
        {
            throw new InvalidDataException("Audit event fields are invalid.");
        }
    }
}
