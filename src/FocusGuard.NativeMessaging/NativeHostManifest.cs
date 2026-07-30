using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FocusGuard.NativeMessaging;

public sealed record NativeHostManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("allowed_origins")] IReadOnlyList<string> AllowedOrigins);

public static partial class NativeHostConfiguration
{
    public const string HostName = "com.focusguard.native";
    public const string LocalManifestFileName = "focusguard.native.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8
    };

    public static NativeHostManifest Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var bytes = File.ReadAllBytes(manifestPath);
        if (bytes.Length is 0 or > NativeMessageFraming.MaxMessageBytes)
        {
            throw new InvalidDataException("Native host manifest size is outside the supported range.");
        }

        var manifest = JsonSerializer.Deserialize<NativeHostManifest>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Native host manifest is required.");
        Validate(manifest);
        return manifest;
    }

    public static void Validate(NativeHostManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!string.Equals(manifest.Name, HostName, StringComparison.Ordinal) ||
            !string.Equals(manifest.Type, "stdio", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(manifest.Description) ||
            !System.IO.Path.IsPathFullyQualified(manifest.Path) ||
            manifest.AllowedOrigins.Count is 0 or > 8 ||
            manifest.AllowedOrigins.Distinct(StringComparer.Ordinal).Count() != manifest.AllowedOrigins.Count ||
            manifest.AllowedOrigins.Any(origin => !IsCanonicalExtensionOrigin(origin)))
        {
            throw new InvalidDataException("Native host manifest is invalid.");
        }
    }

    public static bool IsAllowedOrigin(string? launchOrigin, NativeHostManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return launchOrigin is not null &&
            IsCanonicalExtensionOrigin(launchOrigin) &&
            manifest.AllowedOrigins.Contains(launchOrigin, StringComparer.Ordinal);
    }

    public static bool IsCanonicalExtensionOrigin(string origin) =>
        ExtensionOriginRegex().IsMatch(origin);

    [GeneratedRegex(
        @"^chrome-extension://[a-p]{32}/$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100)]
    private static partial Regex ExtensionOriginRegex();
}
