using System.Text.Json;

namespace FocusGuard.Setup;

internal enum SetupRequestKind { Hello, Install, Uninstall, Blockers, Open, Close, Minimize, Drag }

internal sealed record SetupRequest(SetupRequestKind Kind, bool Close = false);

/// <summary>
/// The installer runs elevated, so its page may ask for only these fixed actions. Every message is parsed with exact
/// properties and types; anything else is dropped. Install additionally requires the explicit consent flag.
/// </summary>
internal static class SetupBridge
{
    internal const string Origin = "https://setup.focusguard.app";
    private const int MaximumMessageLength = 512;

    internal static readonly IReadOnlyDictionary<string, (string ResourceName, string ContentType)> Assets =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["/setup.html"] = ("SetupUi/setup.html", "text/html; charset=utf-8"),
            ["/setup.js"] = ("SetupUi/setup.js", "text/javascript; charset=utf-8"),
            ["/setup.css"] = ("SetupUi/setup.css", "text/css; charset=utf-8"),
            ["/fonts/Geist-Regular.ttf"] = ("SetupFonts/Geist-Regular.ttf", "font/ttf"),
            ["/fonts/Geist-SemiBold.ttf"] = ("SetupFonts/Geist-SemiBold.ttf", "font/ttf"),
        };

    internal static bool IsTrustedSource(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort
        && string.Equals(uri.Host, "setup.focusguard.app", StringComparison.Ordinal)
        && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>Maps a requested URL to one packaged asset; everything else is refused.</summary>
    internal static bool TryResolveAsset(string uri, out (string ResourceName, string ContentType) asset)
    {
        asset = default;
        return IsTrustedSource(uri) && Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            && string.IsNullOrEmpty(parsed.Query) && string.IsNullOrEmpty(parsed.Fragment)
            && Assets.TryGetValue(parsed.AbsolutePath, out asset);
    }

    internal static bool TryParse(string? json, out SetupRequest? request)
    {
        request = null;
        if (string.IsNullOrEmpty(json) || json.Length > MaximumMessageLength) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) return false;
            var count = root.EnumerateObject().Count();
            switch (type.GetString())
            {
                case "setup.hello" when count == 1: request = new(SetupRequestKind.Hello); return true;
                case "setup.uninstall" when count == 1: request = new(SetupRequestKind.Uninstall); return true;
                case "setup.open" when count == 1: request = new(SetupRequestKind.Open); return true;
                case "setup.close" when count == 1: request = new(SetupRequestKind.Close); return true;
                case "setup.minimize" when count == 1: request = new(SetupRequestKind.Minimize); return true;
                case "setup.drag" when count == 1: request = new(SetupRequestKind.Drag); return true;
                case "setup.install" when count == 2 && root.TryGetProperty("consent", out var consent) && consent.ValueKind == JsonValueKind.True:
                    request = new(SetupRequestKind.Install); return true;
                case "setup.blockers" when count == 2 && root.TryGetProperty("close", out var close) && close.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    request = new(SetupRequestKind.Blockers, close.GetBoolean()); return true;
                default: return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
