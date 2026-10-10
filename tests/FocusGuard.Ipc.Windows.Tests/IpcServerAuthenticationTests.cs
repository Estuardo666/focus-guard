namespace FocusGuard.Ipc.Windows.Tests;

public sealed class IpcServerAuthenticationTests
{
    private static readonly string Image = Path.GetFullPath("C:/Program Files/FocusGuard/FocusGuard.Service.exe");
    private static readonly string Hash = new('A', 64);

    [Fact]
    public void ExactRunningProtectedScmServerMatches() => Assert.True(IpcServerAuthentication.Matches(42,
        new("FocusGuard", 42, 4, @"NT AUTHORITY\LocalService", Image, '"' + Image + '"'),
        new("FocusGuard", Image, Hash), Image, Hash, true));

    [Theory]
    [InlineData("squatter")]
    [InlineData("stopped")]
    [InlineData("service-name")]
    [InlineData("system-account")]
    [InlineData("image")]
    [InlineData("hash")]
    [InlineData("writable")]
    [InlineData("pid-zero")]
    [InlineData("unexpected-arguments")]
    [InlineData("shared-service")]
    [InlineData("interactive-session")]
    public void InvalidServerProofFailsClosed(string kind)
    {
        var scm = new ObservedServiceIdentity("FocusGuard", 42, 4, @"NT AUTHORITY\LocalService", Image, '"' + Image + '"');
        scm = kind switch
        {
            "squatter" => scm with { ProcessId = 43 }, "stopped" => scm with { State = 1 },
            "service-name" => scm with { ServiceName = "Other" },
            "system-account" => scm with { Account = "LocalSystem" },
            "image" => scm with { ImagePath = Path.Combine(Path.GetTempPath(), "FocusGuard.Service.exe") }, _ => scm
        };
        if (kind == "unexpected-arguments") scm = scm with { CommandLine = '"' + Image + "\" --other-config" };
        if (kind == "shared-service") scm = scm with { ServiceType = 0x20 };
        Assert.False(IpcServerAuthentication.Matches(kind == "pid-zero" ? 0u : 42u, scm,
            new("FocusGuard", Image, Hash), Image, kind == "hash" ? new string('B', 64) : Hash, kind != "writable",
            serverSessionId: kind == "interactive-session" ? 1u : 0u));
    }
}
