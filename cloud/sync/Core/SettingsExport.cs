using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusGuard.Sync;

public sealed record SettingsImportPreview(GlobalSettings Baseline, GlobalSettings Proposed, SettingsFields Selected)
{
    public GlobalSettings Confirm(GlobalSettings current, bool ownerConfirmed)
    {
        ArgumentNullException.ThrowIfNull(Baseline); Baseline.Validate();
        ArgumentNullException.ThrowIfNull(Proposed); Proposed.Validate();
        ArgumentNullException.ThrowIfNull(current); current.Validate();
        if (!ownerConfirmed) throw new InvalidOperationException("Import requires deliberate confirmation.");
        if (current != Baseline) throw new InvalidOperationException("Settings changed while the import preview was open.");
        const SettingsFields allowed = SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration;
        if (Selected == SettingsFields.None || (Selected & ~allowed) != 0 ||
            !Selected.HasFlag(SettingsFields.Theme) && Proposed.Theme != Baseline.Theme ||
            !Selected.HasFlag(SettingsFields.Language) && Proposed.UiLanguage != Baseline.UiLanguage ||
            !Selected.HasFlag(SettingsFields.Duration) && Proposed.DefaultDurationMinutes != Baseline.DefaultDurationMinutes)
            throw new InvalidOperationException("Import preview changes unselected fields.");
        Proposed.Validate(); return Proposed;
    }
}

/// <summary>Explicit portable export of only three settings. No account/device identifiers, credentials or policy.</summary>
public static class SettingsExport
{
    private const string Format = "focusguard.settings.export";
    private sealed record Package(string Format, int Version, GlobalSettings Settings);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static byte[] Create(GlobalSettings settings, bool ownerConfirmed)
    {
        ArgumentNullException.ThrowIfNull(settings); settings.Validate();
        if (!ownerConfirmed) throw new InvalidOperationException("Export requires deliberate confirmation.");
        return JsonSerializer.SerializeToUtf8Bytes(new Package(Format, 1, settings), Json);
    }

    public static SettingsImportPreview Preview(ReadOnlyMemory<byte> bytes, GlobalSettings current, SettingsFields selected)
    {
        ArgumentNullException.ThrowIfNull(current); current.Validate();
        const SettingsFields allowed = SettingsFields.Theme | SettingsFields.Language | SettingsFields.Duration;
        if (selected == SettingsFields.None || (selected & ~allowed) != 0) throw new ArgumentException("Select permitted settings to import.");
        if (bytes.Length is < 2 or > 8192) throw new JsonException("Invalid settings export size.");
        using var document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Export must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in document.RootElement.EnumerateObject())
            if (!names.Add(field.Name)) throw new JsonException("Duplicate export field.");
        if (names.Count != 3 || !document.RootElement.TryGetProperty("settings", out var payload)) throw new JsonException("Incomplete export.");
        var package = JsonSerializer.Deserialize<Package>(bytes.Span, Json) ?? throw new JsonException("Missing export.");
        if (package.Format != Format || package.Version != 1) throw new InvalidDataException("Unsupported settings export format or version.");
        var imported = GlobalSettingsCodec.Decode(System.Text.Encoding.UTF8.GetBytes(payload.GetRawText()));
        var proposed = new GlobalSettings(selected.HasFlag(SettingsFields.Duration) ? imported.DefaultDurationMinutes : current.DefaultDurationMinutes,
            selected.HasFlag(SettingsFields.Theme) ? imported.Theme : current.Theme,
            selected.HasFlag(SettingsFields.Language) ? imported.UiLanguage : current.UiLanguage);
        proposed.Validate(); return new(current, proposed, selected);
    }
}
