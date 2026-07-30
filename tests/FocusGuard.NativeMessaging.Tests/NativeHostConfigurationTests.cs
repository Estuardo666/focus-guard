using System.Text.Json;
using FocusGuard.NativeMessaging;

namespace FocusGuard.NativeMessaging.Tests;

public sealed class NativeHostConfigurationTests
{
    private const string AllowedOrigin = "chrome-extension://abcdefghijklmnopabcdefghijklmnop/";

    [Theory]
    [InlineData(AllowedOrigin, true)]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop", false)]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnopa/", false)]
    [InlineData("chrome-extension://ABCDEFGHIJKLMNOPABCDEFGHIJKLMNOP/", false)]
    [InlineData("https://abcdefghijklmnopabcdefghijklmnop/", false)]
    [InlineData("chrome-extension://abcdefghijklmnopabcdefghijklmnop.evil/", false)]
    public void IsAllowedOrigin_RequiresExactCanonicalMatch(string origin, bool expected)
    {
        var manifest = ValidManifest();

        Assert.Equal(expected, NativeHostConfiguration.IsAllowedOrigin(origin, manifest));
    }

    [Fact]
    public void Validate_RejectsDuplicateOrigins()
    {
        var manifest = ValidManifest() with { AllowedOrigins = [AllowedOrigin, AllowedOrigin] };

        Assert.Throws<InvalidDataException>(() => NativeHostConfiguration.Validate(manifest));
    }

    [Fact]
    public void Load_RejectsRelativeExecutablePath()
    {
        var manifest = ValidManifest() with { Path = "FocusGuard.NativeMessaging.exe" };
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(manifest));
            Assert.Throws<InvalidDataException>(() => NativeHostConfiguration.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static NativeHostManifest ValidManifest() =>
        new(
            NativeHostConfiguration.HostName,
            "FocusGuard native messaging host",
            Path.GetFullPath("FocusGuard.NativeMessaging.exe"),
            "stdio",
            [AllowedOrigin]);
}
