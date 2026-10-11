using System.Reflection;

namespace FocusGuard.Setup.Tests;

public sealed class SetupBridgeTests
{
    [Theory]
    [InlineData("{\"type\":\"setup.hello\"}", "Hello")]
    [InlineData("{\"type\":\"setup.install\",\"consent\":true}", "Install")]
    [InlineData("{\"type\":\"setup.uninstall\"}", "Uninstall")]
    [InlineData("{\"type\":\"setup.open\"}", "Open")]
    [InlineData("{\"type\":\"setup.close\"}", "Close")]
    [InlineData("{\"type\":\"setup.minimize\"}", "Minimize")]
    [InlineData("{\"type\":\"setup.drag\"}", "Drag")]
    [InlineData("{\"type\":\"setup.blockers\",\"close\":false}", "Blockers")]
    public void Only_the_fixed_installer_actions_are_accepted(string json, string kind)
    {
        Assert.True(SetupBridge.TryParse(json, out var request));
        Assert.Equal(kind, request!.Kind.ToString());
    }

    [Theory]
    [InlineData("{\"type\":\"setup.install\"}")]
    [InlineData("{\"type\":\"setup.install\",\"consent\":false}")]
    [InlineData("{\"type\":\"setup.install\",\"consent\":\"yes\"}")]
    [InlineData("{\"type\":\"setup.install\",\"consent\":true,\"args\":\"-Force\"}")]
    [InlineData("{\"type\":\"setup.run\",\"script\":\"x.ps1\"}")]
    [InlineData("{\"type\":\"setup.hello\",\"extra\":1}")]
    [InlineData("{\"type\":\"setup.blockers\",\"close\":1}")]
    [InlineData("[\"setup.hello\"]")]
    [InlineData("not json")]
    [InlineData("")]
    public void Anything_else_is_dropped_including_install_without_consent(string json)
    {
        Assert.False(SetupBridge.TryParse(json, out _));
    }

    [Fact]
    public void Oversized_messages_are_dropped_before_parsing()
    {
        Assert.False(SetupBridge.TryParse("{\"type\":\"setup.hello\",\"pad\":\"" + new string('x', 600) + "\"}", out _));
    }

    [Theory]
    [InlineData("https://setup.focusguard.app/setup.html", true)]
    [InlineData("https://setup.focusguard.app/fonts/Geist-Regular.ttf", true)]
    [InlineData("https://setup.focusguard.app/setup.js?x=1", false)]
    [InlineData("https://setup.focusguard.app/../payload.zip", false)]
    [InlineData("https://setup.focusguard.app/install-focusguard.ps1", false)]
    [InlineData("http://setup.focusguard.app/setup.html", false)]
    [InlineData("https://setup.focusguard.app:8443/setup.html", false)]
    [InlineData("https://evil.example/setup.html", false)]
    [InlineData("https://user@setup.focusguard.app/setup.html", false)]
    public void Only_packaged_page_assets_are_served(string uri, bool served)
    {
        Assert.Equal(served, SetupBridge.TryResolveAsset(uri, out _));
    }

    [Fact]
    public void Every_served_asset_is_embedded_in_the_installer()
    {
        var names = typeof(SetupBridge).Assembly.GetManifestResourceNames();
        foreach (var asset in SetupBridge.Assets.Values) Assert.Contains(asset.ResourceName, names);
    }

    [Fact]
    public void The_page_keeps_the_strict_content_security_policy()
    {
        using var stream = typeof(SetupBridge).Assembly.GetManifestResourceStream("SetupUi/setup.html")!;
        using var reader = new StreamReader(stream);
        var html = reader.ReadToEnd();
        Assert.Contains("default-src 'self'; script-src 'self'", html, StringComparison.Ordinal);
        Assert.Contains("connect-src 'none'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.Ordinal);
    }
}
