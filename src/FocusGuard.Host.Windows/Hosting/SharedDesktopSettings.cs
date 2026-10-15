using System.Text.Json;
using System.Text.Json.Serialization;
using FocusGuard.Client.Preferences;
using FocusGuard.Devices;

namespace FocusGuard.Host.Windows.Hosting;

internal sealed record SettingsTransferSelection(bool Theme, bool Language, bool Duration)
{
    public bool HasAny => Theme || Language || Duration;
}

/// <summary>Strict allowlist for the one-time, user-selected settings document.</summary>
internal sealed record SharedDesktopSettings(int SchemaVersion, string? Theme, string? Language, int? DurationMinutes)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static SharedDesktopSettings Empty { get; } = new(1, null, null, null);

    public SharedDesktopSettings Validate()
    {
        if (SchemaVersion != 1 || Theme is not (null or "Dark" or "Light") ||
            Language is not (null or "es" or "en") || DurationMinutes is not (null or >= 1 and <= 720) ||
            Theme is null && Language is null && DurationMinutes is null)
            throw new InvalidDataException("Shared settings schema rejected.");
        return this;
    }

    public static SharedDesktopSettings Merge(SharedDesktopSettings? existing, DesktopSettings local,
        SettingsTransferSelection selection)
    {
        ArgumentNullException.ThrowIfNull(local);
        if (!selection.HasAny) throw new InvalidDataException("Choose at least one setting to share.");
        var merged = existing ?? Empty;
        if (selection.Theme) merged = merged with { Theme = local.Theme.ToString() };
        if (selection.Language) merged = merged with { Language = local.UiLanguage };
        if (selection.Duration) merged = merged with { DurationMinutes = local.LastDurationMinutes };
        return merged.Validate();
    }

    public DesktopSettings ApplyTo(DesktopSettings current, SettingsTransferSelection selection)
    {
        ArgumentNullException.ThrowIfNull(current);
        Validate();
        if (!selection.HasAny || selection.Theme && Theme is null ||
            selection.Language && Language is null || selection.Duration && DurationMinutes is null)
            throw new InvalidDataException("Choose only settings present in the shared document.");
        var result = current;
        if (selection.Theme) result = result with { Theme = Enum.Parse<AppTheme>(Theme!, ignoreCase: false) };
        if (selection.Language) result = result with { UiLanguage = Language! };
        if (selection.Duration) result = result with { LastDurationMinutes = DurationMinutes!.Value };
        return result;
    }

    public byte[] ToCanonicalJson()
    {
        Validate();
        var serialized = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        try { return ContentDocumentCodec.CanonicalizeJson(serialized); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(serialized); }
    }

    public static SharedDesktopSettings Parse(byte[] canonicalJson)
    {
        ArgumentNullException.ThrowIfNull(canonicalJson);
        using var document = JsonDocument.Parse(canonicalJson, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Shared settings must be an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name) || property.Name is not ("schemaVersion" or "theme" or "language" or "durationMinutes"))
                throw new InvalidDataException("Shared settings contain a duplicate or unsupported field.");
        }
        if (!root.TryGetProperty("schemaVersion", out var version) || !version.TryGetInt32(out var schemaVersion))
            throw new InvalidDataException("Shared settings version missing.");
        var result = new SharedDesktopSettings(schemaVersion,
            ReadOptionalText(root, "theme"), ReadOptionalText(root, "language"), ReadOptionalInteger(root, "durationMinutes"));
        return result.Validate();
    }

    private static string? ReadOptionalText(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Shared setting type rejected.");
        return value.GetString();
    }

    private static int? ReadOptionalInteger(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var integer))
            throw new InvalidDataException("Shared setting type rejected.");
        return integer;
    }
}
