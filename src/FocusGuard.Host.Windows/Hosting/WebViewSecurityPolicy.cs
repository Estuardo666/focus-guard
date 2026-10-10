namespace FocusGuard.Host.Windows.Hosting;

public static class WebViewSecurityPolicy
{
    public const string VirtualHostName = "focusguard.app";
    public const string Origin = "https://focusguard.app";
    public const string EntryPath = "/index.html";

    public static Uri EntryUri { get; } = new($"{Origin}{EntryPath}", UriKind.Absolute);

    public static bool IsAllowedTopLevelNavigation(string? candidate) =>
        TryGetUri(candidate, out var uri)
        && IsTrustedOrigin(uri)
        && string.Equals(uri.AbsolutePath, EntryPath, StringComparison.Ordinal)
        && string.IsNullOrEmpty(uri.Query);

    public static bool IsTrustedBridgeSource(string? candidate) =>
        IsAllowedTopLevelNavigation(candidate);

    public static bool IsAllowedLocalResource(string? candidate) =>
        TryGetUri(candidate, out var uri)
        && IsTrustedOrigin(uri)
        && uri.AbsolutePath is "/index.html" or "/host.js" or "/host.css"
            or "/chunks/CompactFocus.js" or "/chunks/DashboardStart.js" or "/chunks/PreferencesPanel.js" or "/chunks/ProfileEditor.js"
            or "/chunks/ScheduleEditor.js" or "/chunks/SessionEndExperience.js" or "/chunks/SessionHistoryDetail.js"
            or "/chunks/StrictReviewPanel.js" or "/chunks/WorkflowViews.js" or "/chunks/count-label.js" or "/chunks/focus-time.js" or "/chunks/strict.js"
            or "/fonts/Geist-Regular.ttf" or "/fonts/Geist-SemiBold.ttf"
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool TryGetUri(string? candidate, out Uri uri)
    {
        uri = null!;
        return !string.IsNullOrWhiteSpace(candidate)
            && Uri.TryCreate(candidate, UriKind.Absolute, out uri!);
    }

    private static bool IsTrustedOrigin(Uri uri) =>
        uri.IsAbsoluteUri
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.IdnHost, VirtualHostName, StringComparison.OrdinalIgnoreCase)
        && uri.Port == 443
        && string.IsNullOrEmpty(uri.UserInfo);
}
