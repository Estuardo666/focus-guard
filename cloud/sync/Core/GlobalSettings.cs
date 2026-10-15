using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusGuard.Sync;

/// <summary>The entire global-settings allowlist. Device paths, rules and permissions never enter it.</summary>
public sealed record GlobalSettings(int DefaultDurationMinutes, string Theme, string UiLanguage)
{
    public const string DocumentKind = "settings.global.v1";
    public static GlobalSettings Default { get; } = new(50, "dark", "es");

    public void Validate()
    {
        if (DefaultDurationMinutes is < 1 or > 720 || Theme is not ("dark" or "light") ||
            UiLanguage is not ("es" or "en"))
            throw new ArgumentException("Invalid global settings.");
    }
}

public static class GlobalSettingsCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    // Declared property order is alphabetical: these bounded ASCII strings and integer produce JCS JSON.
    public static byte[] Encode(GlobalSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(value, Options);
    }

    public static GlobalSettings Decode(ReadOnlyMemory<byte> json)
    {
        if (json.Length is < 2 or > 4096) throw new JsonException("Invalid settings payload size.");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Settings must be an object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!names.Add(property.Name)) throw new JsonException("Duplicate settings field.");
        if (names.Count != 3) throw new JsonException("All settings fields are required.");
        var value = JsonSerializer.Deserialize<GlobalSettings>(json.Span, Options)
            ?? throw new JsonException("Settings are missing.");
        value.Validate();
        return value;
    }
}
