using System.Text.Json;
using FocusGuard.Client.Preferences;

namespace FocusGuard.Sync;

/// <summary>Explicit-path preparation store. Never falls back to defaults after corruption or IO failure.</summary>
public sealed class DesktopSettingsSyncStore(string path)
{
    private readonly string file = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Read a complete local baseline without falling back to defaults after missing/corrupt state.</summary>
    public async Task<DesktopSettings> LoadAsync(CancellationToken token = default)
    {
        await using var lease = await DesktopSettingsFileLease.AcquireAsync(file, token);
        var original = await lease.ReadAsync(token) ?? throw new FileNotFoundException("Preferences baseline is missing.", file);
        using var parsed = JsonDocument.Parse(original);
        ValidateUniqueProperties(parsed.RootElement);
        var document = parsed.RootElement;
        if (document.ValueKind != JsonValueKind.Object ||
            !document.TryGetProperty("theme", out var theme) || theme.ValueKind != JsonValueKind.Number || !theme.TryGetInt32(out _) ||
            !document.TryGetProperty("uiLanguage", out var language) || language.ValueKind != JsonValueKind.String ||
            !document.TryGetProperty("lastDurationMinutes", out var duration) || duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out _))
            throw new InvalidDataException("Local preferences baseline is incomplete; explicit recovery is required.");
        var current = JsonSerializer.Deserialize<DesktopSettings>(original, Json)
            ?? throw new InvalidDataException("Invalid preferences.");
        if (!Enum.IsDefined(current.Theme) || !Enum.IsDefined(current.SessionEnd) || !Enum.IsDefined(current.LastSessionMode) ||
            current.LastDurationMinutes is < 1 or > 720 || current.UiLanguage is not ("es" or "en") ||
            current.ProfileThemes is not null && current.ProfileThemes.Values.Any(value => !Enum.IsDefined(value)))
            throw new InvalidDataException("Invalid preferences values.");
        return current;
    }

    public async Task<DesktopSettings> ApplyAsync(SettingsOutbox verifiedState, LocalSettingsOverrides overrides,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(verifiedState); verifiedState.Validate();
        await using var lease = await DesktopSettingsFileLease.AcquireAsync(file, token);
        var original = await lease.ReadAsync(token) ?? throw new FileNotFoundException("Preferences baseline is missing.", file);
        using (var parsed = JsonDocument.Parse(original))
        {
            ValidateUniqueProperties(parsed.RootElement);
            var document = parsed.RootElement;
            if (document.ValueKind != JsonValueKind.Object ||
                !document.TryGetProperty("theme", out var theme) || theme.ValueKind != JsonValueKind.Number || !theme.TryGetInt32(out _) ||
                !document.TryGetProperty("uiLanguage", out var language) || language.ValueKind != JsonValueKind.String ||
                !document.TryGetProperty("lastDurationMinutes", out var duration) || duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out _))
                throw new InvalidDataException("Local settings baseline is incomplete; explicit recovery is required.");
        }
        var current = JsonSerializer.Deserialize<DesktopSettings>(original, Json) ?? throw new InvalidDataException("Invalid preferences.");
        if (!Enum.IsDefined(current.Theme) || !Enum.IsDefined(current.SessionEnd) || !Enum.IsDefined(current.LastSessionMode) ||
            current.LastDurationMinutes is < 1 or > 720 || current.UiLanguage is not ("es" or "en") ||
            current.ProfileThemes is not null && current.ProfileThemes.Values.Any(value => !Enum.IsDefined(value)))
            throw new InvalidDataException("Invalid preferences values.");
        var projected = DesktopSettingsProjection.ApplyOutbox(current, verifiedState, overrides);
        using var node = JsonDocument.Parse(original);
        if (node.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid preferences object.");
        var properties = node.RootElement.EnumerateObject().ToDictionary(property => property.Name,
            property => property.Value.Clone(), StringComparer.Ordinal);
        var known = JsonSerializer.SerializeToElement(projected, Json);
        foreach (var property in known.EnumerateObject()) properties[property.Name] = property.Value.Clone();
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            foreach (var property in properties.OrderBy(item => item.Key, StringComparer.Ordinal))
            { writer.WritePropertyName(property.Key); property.Value.WriteTo(writer); }
            writer.WriteEndObject();
        }
        token.ThrowIfCancellationRequested();
        var replacement = output.ToArray();
        if (!original.AsSpan().SequenceEqual(replacement)) await lease.ReplaceAsync(replacement, token);
        return projected; // Success only after atomic replacement; errors propagate to the caller.
    }

    private static void ValidateUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Ambiguous local preferences field.");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateUniqueProperties(item);
    }
}
