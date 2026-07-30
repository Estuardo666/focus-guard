namespace FocusGuard.Domain;

/// <summary>
/// Where an urge was captured from. The kind decides how "Open" behaves when the
/// item resurfaces at the end of the session.
/// </summary>
public enum CaptureSourceKind
{
    Website,
    Application
}

/// <summary>
/// What the user did with a captured item once the session closed. Dismissed items are
/// kept rather than deleted: two months of them is the only honest record of what
/// actually pulls attention away, and Activity can surface that later.
/// </summary>
public enum CaptureResolution
{
    Pending,
    Opened,
    Dismissed
}

/// <summary>
/// One line written on the block screen so the thought can be let go of without
/// ending the session. Deliberately tiny: text, where it came from, and when.
/// </summary>
public sealed record CapturedItem(
    Guid Id,
    Guid SessionId,
    string Text,
    CaptureSourceKind SourceKind,
    string SourceValue,
    DateTimeOffset CapturedAtUtc,
    DateTimeOffset? ResolvedAtUtc = null,
    CaptureResolution Resolution = CaptureResolution.Pending)
{
    public const int MaximumTextLength = 240;
    public const int MaximumSourceLength = 1_024;

    public static ValidationResult<CapturedItem> Create(
        Guid sessionId,
        string? text,
        CaptureSourceKind sourceKind,
        string? sourceValue,
        DateTimeOffset capturedAtUtc)
    {
        if (sessionId == Guid.Empty)
        {
            return ValidationResult<CapturedItem>.Failure(
                new ValidationError("capture.session.required", "A capture must belong to a session."));
        }

        if (sourceKind is not CaptureSourceKind.Website and not CaptureSourceKind.Application)
        {
            return ValidationResult<CapturedItem>.Failure(
                new ValidationError("capture.source_kind.invalid", "Capture source kind is invalid."));
        }

        var normalizedSource = sourceValue?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSource) || normalizedSource.Length > MaximumSourceLength)
        {
            return ValidationResult<CapturedItem>.Failure(
                new ValidationError("capture.source.invalid", "Capture source is required and bounded."));
        }

        if (sourceKind == CaptureSourceKind.Website && !IsSanitizedWebSource(normalizedSource))
        {
            return ValidationResult<CapturedItem>.Failure(
                new ValidationError(
                    "capture.source.not_sanitized",
                    "Website captures must record an HTTP(S) address without query or fragment."));
        }

        // The field is pre-filled with the blocked domain, so an empty box means the
        // user pressed Enter and accepted that default rather than writing nothing.
        var normalizedText = NormalizeText(text) ?? DefaultTextFor(sourceKind, normalizedSource);
        if (normalizedText.Length > MaximumTextLength)
        {
            normalizedText = normalizedText[..MaximumTextLength];
        }

        if (!capturedAtUtc.Offset.Equals(TimeSpan.Zero))
        {
            return ValidationResult<CapturedItem>.Failure(
                new ValidationError("capture.captured_at.not_utc", "Capture time must be expressed in UTC."));
        }

        return ValidationResult<CapturedItem>.Success(new CapturedItem(
            Guid.NewGuid(),
            sessionId,
            normalizedText,
            sourceKind,
            normalizedSource,
            capturedAtUtc));
    }

    public CapturedItem Resolve(CaptureResolution resolution, DateTimeOffset resolvedAtUtc) =>
        resolution is CaptureResolution.Opened or CaptureResolution.Dismissed
            ? this with { Resolution = resolution, ResolvedAtUtc = resolvedAtUtc }
            : throw new ArgumentOutOfRangeException(nameof(resolution));

    /// <summary>
    /// The label the end-of-session list shows under the text: "x.com", "ChatGPT.exe".
    /// </summary>
    public string SourceLabel => SourceKind == CaptureSourceKind.Website
        ? Uri.TryCreate(SourceValue, UriKind.Absolute, out var uri) ? uri.Host : SourceValue
        : Path.GetFileName(SourceValue);

    public static string? NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Newlines would let one capture masquerade as several rows in the summary list.
        var collapsed = string.Join(' ', text.Split(
            ['\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(collapsed) ? null : collapsed;
    }

    private static string DefaultTextFor(CaptureSourceKind kind, string source) =>
        kind == CaptureSourceKind.Website
            ? Uri.TryCreate(source, UriKind.Absolute, out var uri) ? uri.Host : source
            : Path.GetFileNameWithoutExtension(source) is { Length: > 0 } name
                ? name
                : source;

    private static bool IsSanitizedWebSource(string value) =>
        !value.Contains('?', StringComparison.Ordinal)
        && !value.Contains('#', StringComparison.Ordinal)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && !string.IsNullOrWhiteSpace(uri.Host);
}
